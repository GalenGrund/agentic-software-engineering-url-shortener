using AgenticSoftwareEngineering.Api.Application.Orchestration;
using AgenticSoftwareEngineering.Api.Domain.Orchestration;
using AgenticSoftwareEngineering.Api.Infrastructure.Persistence;
using AgenticSoftwareEngineering.Api.Providers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AgenticSoftwareEngineering.Tests;

public sealed class GovernanceRecoveryTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly AppDbContext db;

    public GovernanceRecoveryTests()
    {
        connection.Open();
        db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        db.Database.Migrate();
    }

    [Fact]
    public async Task LowRiskWorkflowExecutesWithoutApproval()
    {
        var service = CreateService(new DeterministicAgentProvider());
        var status = await service.CreateGreenfieldAsync("Create a short URL", CancellationToken.None);
        await service.AdvanceAsync(status.WorkflowId, CancellationToken.None);

        var next = await service.GetStatusAsync(status.WorkflowId, CancellationToken.None);
        Assert.Empty(next.Approvals);
        Assert.Contains(next.Policies, policy => policy.Risk == RiskLevel.Low && policy.Allowed);
    }

    [Fact]
    public async Task HighRiskWorkflowWaitsForApprovalWithoutInvokingProvider()
    {
        var provider = new TrackingProvider();
        var service = CreateService(provider);
        var status = await service.CreateGreenfieldAsync("Create a short URL", CancellationToken.None, true);
        for (var i = 0; i < 3; i++) status = await service.AdvanceAsync(status.WorkflowId, CancellationToken.None);

        Assert.Equal(WorkflowState.WaitingForApproval, status.State);
        Assert.Contains(status.Approvals, approval => approval.Risk == RiskLevel.High && approval.Decision == ApprovalDecision.Pending && approval.PlanRevisionId is not null);
        Assert.Contains(provider.Requests, request => request.TaskType == "implementation-preparation" && request.Mode == AgentExecutionMode.Proposal);
        Assert.DoesNotContain(provider.Requests, request => request.TaskType == "implementation-preparation" && request.Mode == AgentExecutionMode.Apply);
    }

    [Fact]
    public async Task ApprovedHighRiskActionResumesExecution()
    {
        var provider = new TrackingProvider();
        var service = CreateService(provider);
        var status = await service.CreateGreenfieldAsync("Create a short URL", CancellationToken.None, true);
        for (var i = 0; i < 3; i++) status = await service.AdvanceAsync(status.WorkflowId, CancellationToken.None);
        var approval = status.Approvals.Single();

        status = await service.DecideApprovalAsync(status.WorkflowId, approval.Id, new ApprovalDecisionRequest(true, "Approved within prototype scope"), CancellationToken.None);

        Assert.Equal(ApprovalDecision.Approved, status.Approvals.Single().Decision);
        Assert.NotNull(status.Approvals.Single().PlanRevisionId);
        var authorizationIndex = status.Events.ToList().FindIndex(item => item.EventType == "PostApprovalAuthorizationAllowed");
        var executionIndex = status.Events.ToList().FindIndex(item => item.EventType == "NodeExecutionStarted" && item.Details == "Implementation Preparation");
        Assert.True(authorizationIndex >= 0 && authorizationIndex < executionIndex);
        Assert.Contains(status.Policies, policy => policy.PolicyName == "gate3-post-approval-authorization" && policy.Allowed);
        Assert.Contains(provider.Requests, request => request.TaskType == "implementation-preparation");
        Assert.NotEqual(WorkflowState.SafeStopped, status.State);
    }

    [Fact]
    public async Task ApprovalCannotBeDecidedTwice()
    {
        var service = CreateService(new TrackingProvider());
        var status = await CreateWaitingHighRiskWorkflow(service);
        var approval = status.Approvals.Single();

        await service.DecideApprovalAsync(status.WorkflowId, approval.Id, new ApprovalDecisionRequest(false, "Rejected for review"), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DecideApprovalAsync(status.WorkflowId, approval.Id, new ApprovalDecisionRequest(true, "Second decision"), CancellationToken.None));
    }

    [Fact]
    public async Task ApprovalCannotAuthorizeAnotherWorkflow()
    {
        var service = CreateService(new TrackingProvider());
        var first = await CreateWaitingHighRiskWorkflow(service);
        var second = await CreateWaitingHighRiskWorkflow(service);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.DecideApprovalAsync(second.WorkflowId, first.Approvals.Single().Id, new ApprovalDecisionRequest(true, "Cross-workflow attempt"), CancellationToken.None));
    }

    [Fact]
    public async Task StalePlanRevisionCannotAuthorizeExecution()
    {
        var provider = new TrackingProvider();
        var service = CreateService(provider);
        var status = await CreateWaitingHighRiskWorkflow(service);
        var approval = status.Approvals.Single();
        var workflow = await db.Workflows.SingleAsync(item => item.Id == status.WorkflowId);
        var staleReplacement = new PlanRevision(workflow.Id, 2, DateTimeOffset.UtcNow, approval.PlanRevisionId);
        db.PlanRevisions.Add(staleReplacement);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DecideApprovalAsync(status.WorkflowId, approval.Id, new ApprovalDecisionRequest(true, "Stale revision attempt"), CancellationToken.None));

        Assert.DoesNotContain(provider.Requests, request => request.TaskType == "implementation-preparation" && request.Mode == AgentExecutionMode.Apply);
        Assert.Contains((await service.GetStatusAsync(status.WorkflowId, CancellationToken.None)).Policies, policy => policy.PolicyName == "gate3-post-approval-authorization" && !policy.Allowed);
    }

    [Fact]
    public async Task RejectedHighRiskActionSafeStopsWithoutExecution()
    {
        var provider = new TrackingProvider();
        var service = CreateService(provider);
        var status = await service.CreateGreenfieldAsync("Create a short URL", CancellationToken.None, true);
        for (var i = 0; i < 3; i++) status = await service.AdvanceAsync(status.WorkflowId, CancellationToken.None);
        var approval = status.Approvals.Single();

        status = await service.DecideApprovalAsync(status.WorkflowId, approval.Id, new ApprovalDecisionRequest(false, "Rejected for review"), CancellationToken.None);

        Assert.Equal(WorkflowState.SafeStopped, status.State);
        Assert.DoesNotContain(provider.Requests, request => request.TaskType == "implementation-preparation" && request.Mode == AgentExecutionMode.Apply);
        Assert.Contains(status.Events, item => item.EventType == "HumanEscalationRequired");
    }

    [Fact]
    public async Task TransientFailureSchedulesRetryAndCreatesDistinctExecution()
    {
        var provider = new SequenceFailureProvider(FailureClassification.Transient, FailureClassification.Transient, FailureClassification.Transient);
        var service = CreateService(provider);
        var created = await service.CreateGreenfieldAsync("Create a short URL", CancellationToken.None);
        var first = await service.AdvanceAsync(created.WorkflowId, CancellationToken.None);
        Assert.Contains(first.Nodes, node => node.TaskType == "normalize-requirement" && node.State == WorkflowNodeState.RetryScheduled);
        var ready = await service.AdvanceAsync(created.WorkflowId, CancellationToken.None);
        Assert.Contains(ready.Nodes, node => node.TaskType == "normalize-requirement" && node.State == WorkflowNodeState.Ready);
        var stopped = await service.AdvanceAsync(created.WorkflowId, CancellationToken.None);
        Assert.Equal(2, stopped.Executions.Count(item => item.WorkflowNodeId == stopped.Nodes.Single(node => node.TaskType == "normalize-requirement").Id));
        Assert.Equal(WorkflowState.SafeStopped, stopped.State);
    }

    [Fact]
    public async Task SuccessfulRetryContinuesNormalExecution()
    {
        var provider = new SequenceFailureProvider(FailureClassification.Transient, null);
        var service = CreateService(provider);
        var created = await service.CreateGreenfieldAsync("Create a short URL", CancellationToken.None);
        await service.AdvanceAsync(created.WorkflowId, CancellationToken.None);
        await service.AdvanceAsync(created.WorkflowId, CancellationToken.None);
        var status = await service.AdvanceAsync(created.WorkflowId, CancellationToken.None);

        Assert.NotEqual(WorkflowState.SafeStopped, status.State);
        Assert.Contains(status.Executions, item => item.Attempt == 2 && item.Status == AgentExecutionStatus.Succeeded);
    }

    [Fact]
    public async Task PermanentFailureUsesCompatibleFallback()
    {
        var primary = new SequenceFailureProvider(FailureClassification.Permanent);
        var fallback = new TrackingProvider("fallback");
        var service = CreateService(primary, fallback);
        var created = await service.CreateGreenfieldAsync("Create a short URL", CancellationToken.None);
        var status = await service.AdvanceAsync(created.WorkflowId, CancellationToken.None);

        Assert.Contains(fallback.Requests, request => request.TaskType == "normalize-requirement");
        Assert.Contains(status.Executions, execution => execution.ProviderName == "fallback");
        Assert.Contains(status.Policies, policy => policy.PolicyName == "gate3-fallback-policy" && policy.Allowed);
        Assert.NotEqual(WorkflowState.SafeStopped, status.State);
    }

    [Fact]
    public async Task DeniedFallbackPolicyDoesNotInvokeFallbackAndEscalates()
    {
        var primary = new SequenceFailureProvider(FailureClassification.Permanent);
        var fallback = new TrackingProvider("fallback");
        var service = CreateService(new OrchestrationOptions { AllowFallback = false }, primary, fallback);
        var created = await service.CreateGreenfieldAsync("Create a short URL", CancellationToken.None);
        var status = await service.AdvanceAsync(created.WorkflowId, CancellationToken.None);

        Assert.Equal(WorkflowState.SafeStopped, status.State);
        Assert.Empty(fallback.Requests);
        Assert.Contains(status.Policies, policy => policy.PolicyName == "gate3-fallback-policy" && !policy.Allowed);
        Assert.Contains(status.Events, item => item.EventType == "HumanEscalationRequired");
    }

    [Fact]
    public async Task FailedFallbackSafeStopsAndEscalates()
    {
        var service = CreateService(new SequenceFailureProvider(FailureClassification.Permanent), new FailedFallbackProvider());
        var created = await service.CreateGreenfieldAsync("Create a short URL", CancellationToken.None);
        var status = await service.AdvanceAsync(created.WorkflowId, CancellationToken.None);

        Assert.Equal(WorkflowState.SafeStopped, status.State);
        Assert.Contains(status.Events, item => item.EventType == "HumanEscalationRequired" && item.Details.Contains("providers=sequence-failure,fallback"));
    }

    [Fact]
    public async Task InvalidFallbackOutputSafeStopsAndEscalates()
    {
        var service = CreateService(new SequenceFailureProvider(FailureClassification.Permanent), new InvalidFallbackProvider());
        var created = await service.CreateGreenfieldAsync("Create a short URL", CancellationToken.None);
        var status = await service.AdvanceAsync(created.WorkflowId, CancellationToken.None);

        Assert.Equal(WorkflowState.SafeStopped, status.State);
        Assert.Contains(status.Events, item => item.EventType == "HumanEscalationRequired" && item.Details.Contains("fallback-exit-gate"));
    }

    [Fact]
    public async Task ConfiguredRetryLimitIsHonored()
    {
        var provider = new SequenceFailureProvider(FailureClassification.Transient, FailureClassification.Transient, FailureClassification.Transient);
        var service = CreateService(new OrchestrationOptions { MaxTotalExecutionAttempts = 3, AllowFallback = false }, provider);
        var created = await service.CreateGreenfieldAsync("Create a short URL", CancellationToken.None);
        var status = await service.AdvanceAsync(created.WorkflowId, CancellationToken.None);
        for (var attempt = 0; attempt < 6 && status.State != WorkflowState.SafeStopped; attempt++)
        {
            status = await service.AdvanceAsync(created.WorkflowId, CancellationToken.None);
        }

        Assert.Equal(WorkflowState.SafeStopped, status.State);
        Assert.Equal(3, status.Executions.Count(item => item.WorkflowNodeId == status.Nodes.Single(node => node.TaskType == "normalize-requirement").Id));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task InvalidRetryLimitIsClampedToOneOrMoreAttempts(int configuredLimit)
    {
        var provider = new SequenceFailureProvider(FailureClassification.Transient, FailureClassification.Transient);
        var service = CreateService(new OrchestrationOptions { MaxTotalExecutionAttempts = configuredLimit, AllowFallback = false }, provider);
        var created = await service.CreateGreenfieldAsync("Create a short URL", CancellationToken.None);
        var status = await service.AdvanceAsync(created.WorkflowId, CancellationToken.None);

        Assert.Equal(WorkflowState.SafeStopped, status.State);
        Assert.Single(status.Executions);
    }

    [Fact]
    public async Task PermanentFailureDoesNotConsumeTransientRetryAttempts()
    {
        var service = CreateService(new SequenceFailureProvider(FailureClassification.Permanent));
        var created = await service.CreateGreenfieldAsync("Create a short URL", CancellationToken.None);
        var status = await service.AdvanceAsync(created.WorkflowId, CancellationToken.None);

        Assert.Equal(WorkflowState.SafeStopped, status.State);
        Assert.Single(status.Executions);
        Assert.Equal(1, status.Executions[0].Attempt);
    }

    [Fact]
    public async Task UnregisteredFallbackIsNotUsed()
    {
        var service = CreateService(new UnregisteredFallbackProvider());
        var created = await service.CreateGreenfieldAsync("Create a short URL", CancellationToken.None);
        var status = await service.AdvanceAsync(created.WorkflowId, CancellationToken.None);

        Assert.Equal(WorkflowState.SafeStopped, status.State);
        Assert.Single(status.Executions);
        Assert.Equal("unregistered-primary", status.Executions[0].ProviderName);
    }

    [Fact]
    public async Task PolicyBlockedFailureSafeStopsWithoutFallback()
    {
        var primary = new SequenceFailureProvider(FailureClassification.PolicyBlocked);
        var fallback = new TrackingProvider("fallback");
        var service = CreateService(primary, fallback);
        var created = await service.CreateGreenfieldAsync("Create a short URL", CancellationToken.None);
        var status = await service.AdvanceAsync(created.WorkflowId, CancellationToken.None);

        Assert.Equal(WorkflowState.SafeStopped, status.State);
        Assert.Empty(fallback.Requests);
        Assert.Contains(status.Events, item => item.EventType == "HumanEscalationRequired");
    }

    private OrchestrationService CreateService(params IAgentProvider[] providers) =>
        new(db, providers[0], new FixedTimeProvider(), providers);

    private OrchestrationService CreateService(OrchestrationOptions options, params IAgentProvider[] providers) =>
        new(db, providers[0], new FixedTimeProvider(), providers, Options.Create(options));

    private static async Task<WorkflowStatusResponse> CreateWaitingHighRiskWorkflow(OrchestrationService service)
    {
        var status = await service.CreateGreenfieldAsync("Create a short URL", CancellationToken.None, true);
        for (var i = 0; i < 3; i++) status = await service.AdvanceAsync(status.WorkflowId, CancellationToken.None);
        return status;
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }
}

internal sealed class TrackingProvider(string name = "tracking") : IAgentProvider
{
    public string Name => name;
    public IReadOnlySet<string> CompatibleFallbackProviders { get; } = new HashSet<string>();
    public List<AgentExecutionRequest> Requests { get; } = new();

    public Task<AgenticSoftwareEngineering.Api.Providers.AgentExecutionResponse> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return new DeterministicAgentProvider().ExecuteAsync(request, cancellationToken);
    }
}

internal sealed class SequenceFailureProvider(params FailureClassification?[] failures) : IAgentProvider
{
    private int callCount;
    public string Name => "sequence-failure";
    public IReadOnlySet<string> CompatibleFallbackProviders { get; } = new HashSet<string>(new[] { "fallback" }, StringComparer.OrdinalIgnoreCase);

    public Task<AgenticSoftwareEngineering.Api.Providers.AgentExecutionResponse> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken)
    {
        var failure = failures[Math.Min(callCount++, failures.Length - 1)];
        if (failure is null)
        {
            return new DeterministicAgentProvider().ExecuteAsync(request, cancellationToken);
        }

        return Task.FromResult(new AgenticSoftwareEngineering.Api.Providers.AgentExecutionResponse(false, string.Empty, string.Empty, string.Empty, string.Empty, "controlled failure", failure.Value));
    }
}

internal sealed class UnregisteredFallbackProvider : IAgentProvider
{
    public string Name => "unregistered-primary";
    public IReadOnlySet<string> CompatibleFallbackProviders { get; } = new HashSet<string>(new[] { "missing-fallback" }, StringComparer.OrdinalIgnoreCase);

    public Task<AgenticSoftwareEngineering.Api.Providers.AgentExecutionResponse> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new AgenticSoftwareEngineering.Api.Providers.AgentExecutionResponse(false, string.Empty, string.Empty, string.Empty, string.Empty, "permanent", FailureClassification.Permanent));
}

internal sealed class FailedFallbackProvider : IAgentProvider
{
    public string Name => "fallback";
    public IReadOnlySet<string> CompatibleFallbackProviders { get; } = new HashSet<string>();

    public Task<AgenticSoftwareEngineering.Api.Providers.AgentExecutionResponse> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new AgenticSoftwareEngineering.Api.Providers.AgentExecutionResponse(false, string.Empty, string.Empty, string.Empty, string.Empty, "fallback failure", FailureClassification.Permanent));
}

internal sealed class InvalidFallbackProvider : IAgentProvider
{
    public string Name => "fallback";
    public IReadOnlySet<string> CompatibleFallbackProviders { get; } = new HashSet<string>();

    public Task<AgenticSoftwareEngineering.Api.Providers.AgentExecutionResponse> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new AgenticSoftwareEngineering.Api.Providers.AgentExecutionResponse(true, string.Empty, "invalid", "", "", null));
}
