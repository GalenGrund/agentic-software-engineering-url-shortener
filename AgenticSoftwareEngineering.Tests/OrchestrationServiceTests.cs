using AgenticSoftwareEngineering.Api.Application.Orchestration;
using AgenticSoftwareEngineering.Api.Domain.Orchestration;
using AgenticSoftwareEngineering.Api.Infrastructure.Persistence;
using AgenticSoftwareEngineering.Api.Providers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AgenticSoftwareEngineering.Tests;

public sealed class OrchestrationServiceTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly AppDbContext db;
    private readonly IOrchestrationService service;

    public OrchestrationServiceTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        db.Database.Migrate();
        service = new OrchestrationService(db, new DeterministicAgentProvider(), new FixedTimeProvider());
    }

    [Fact]
    public async Task CreateGreenfield_PersistsDagAndInitialReadyNode()
    {
        var status = await service.CreateGreenfieldAsync("Create a short URL", CancellationToken.None);

        Assert.Equal(WorkflowState.Planning, status.State);
        Assert.Equal(6, status.Nodes.Count);
        Assert.Equal(6, status.Dependencies.Count);
        Assert.Single(status.Nodes, node => node.TaskType == "normalize-requirement" && node.State == WorkflowNodeState.Ready);
        Assert.Equal(2, status.Events.Count(eventItem => eventItem.EventType == "WorkflowCreated" || eventItem.EventType == "PlanRevisionCreated"));
    }

    [Fact]
    public async Task Advance_MakesParallelBranchesReadyAfterPlanSucceeds()
    {
        var created = await service.CreateGreenfieldAsync("Create a short URL", CancellationToken.None);
        await service.AdvanceAsync(created.WorkflowId, CancellationToken.None);
        var status = await service.AdvanceAsync(created.WorkflowId, CancellationToken.None);

        Assert.Equal(WorkflowNodeState.Ready, status.Nodes.Single(node => node.TaskType == "architecture-design").State);
        Assert.Equal(WorkflowNodeState.Ready, status.Nodes.Single(node => node.TaskType == "implementation-preparation").State);
    }

    [Fact]
    public async Task SynchronizationNodeWaitsUntilBothParallelBranchesSucceed()
    {
        var created = await service.CreateGreenfieldAsync("Create a short URL", CancellationToken.None);
        await service.AdvanceAsync(created.WorkflowId, CancellationToken.None);
        var parallelReady = await service.AdvanceAsync(created.WorkflowId, CancellationToken.None);
        var architectureId = parallelReady.Nodes.Single(node => node.TaskType == "architecture-design").Id;
        var implementationId = parallelReady.Nodes.Single(node => node.TaskType == "implementation-preparation").Id;

        var architecture = db.WorkflowNodes.Single(node => node.Id == architectureId);
        architecture.TransitionTo(WorkflowNodeState.Executing);
        architecture.TransitionTo(WorkflowNodeState.Validating);
        architecture.TransitionTo(WorkflowNodeState.Succeeded);
        await db.SaveChangesAsync();
        var partial = await service.GetStatusAsync(created.WorkflowId, CancellationToken.None);
        Assert.Equal(WorkflowNodeState.Pending, partial.Nodes.Single(node => node.TaskType == "validation").State);

        var implementation = db.WorkflowNodes.Single(node => node.Id == implementationId);
        implementation.TransitionTo(WorkflowNodeState.Executing);
        implementation.TransitionTo(WorkflowNodeState.Validating);
        implementation.TransitionTo(WorkflowNodeState.Succeeded);
        await db.SaveChangesAsync();
        var complete = await service.AdvanceAsync(created.WorkflowId, CancellationToken.None);
        Assert.Equal(WorkflowNodeState.Ready, complete.Nodes.Single(node => node.TaskType == "validation").State);
    }

    [Fact]
    public async Task EndToEndWorkflowPersistsLineageEventsAndCompletesThroughReleaseReadiness()
    {
        var status = await service.CreateGreenfieldAsync("Create a short URL", CancellationToken.None);
        for (var attempt = 0; attempt < 8 && status.State != WorkflowState.Completed; attempt++)
        {
            status = await service.AdvanceAsync(status.WorkflowId, CancellationToken.None);
        }

        Assert.Equal(WorkflowState.Completed, status.State);
        Assert.All(status.Nodes, node => Assert.Equal(WorkflowNodeState.Succeeded, node.State));
        Assert.Equal(6, status.Executions.Count);
        Assert.Equal(6, status.Artifacts.Count);
        Assert.Contains(status.Validations, validation => validation.ValidationName == "validation" && validation.Passed);
        Assert.Contains(status.Validations, validation => validation.ValidationName == "release-readiness" && validation.Passed);
        Assert.Contains(status.Events, eventItem => eventItem.EventType == "ReleaseReadinessEvaluated");
        Assert.All(status.Artifacts, artifact => Assert.NotEqual(Guid.Empty, artifact.ProducerExecutionId));
    }

    [Fact]
    public async Task FailedProviderPreventsValidationAndReleaseReadiness()
    {
        var failingService = new OrchestrationService(db, new FailingProvider("validation"), new FixedTimeProvider());
        var status = await failingService.CreateGreenfieldAsync("Create a short URL", CancellationToken.None);
        for (var attempt = 0; attempt < 8 && status.State is not (WorkflowState.Failed or WorkflowState.Completed); attempt++)
        {
            status = await failingService.AdvanceAsync(status.WorkflowId, CancellationToken.None);
        }

        Assert.Equal(WorkflowState.SafeStopped, status.State);
        Assert.DoesNotContain(status.Validations, validation => validation.ValidationName == "validation" && validation.Passed);
        Assert.DoesNotContain(status.Events, eventItem => eventItem.EventType == "WorkflowCompleted");
    }

    [Fact]
    public async Task SuccessfulProviderWithMalformedOutputFailsExitGate()
    {
        var malformedService = new OrchestrationService(db, new MalformedProvider(), new FixedTimeProvider());
        var created = await malformedService.CreateGreenfieldAsync("Create a short URL", CancellationToken.None);

        var failed = await malformedService.AdvanceAsync(created.WorkflowId, CancellationToken.None);

        Assert.Equal(WorkflowState.Failed, failed.State);
        Assert.Equal(WorkflowNodeState.Failed, failed.Nodes.Single(node => node.TaskType == "normalize-requirement").State);
        Assert.Contains(failed.Validations, validation => validation.ValidationName == "normalize-requirement-exit-gate" && !validation.Passed);
        Assert.DoesNotContain(failed.Artifacts, artifact => artifact.ArtifactType == "normalized-requirement");
        Assert.DoesNotContain(failed.Nodes, node => node.TaskType == "decompose-plan" && node.State == WorkflowNodeState.Ready);
    }

    [Fact]
    public async Task ReleaseReadinessGovernanceFailureFailsReleaseNodeBeforeSuccess()
    {
        var failingReleaseService = new OrchestrationService(db, new ReleaseEvidenceGapProvider(), new FixedTimeProvider());
        var status = await failingReleaseService.CreateGreenfieldAsync("Create a short URL", CancellationToken.None);
        for (var attempt = 0; attempt < 8 && status.State is not (WorkflowState.Failed or WorkflowState.Completed); attempt++)
        {
            status = await failingReleaseService.AdvanceAsync(status.WorkflowId, CancellationToken.None);
        }

        Assert.Equal(WorkflowState.Failed, status.State);
        Assert.Equal(WorkflowNodeState.Failed, status.Nodes.Single(node => node.TaskType == "release-readiness").State);
        Assert.Contains(status.Validations, validation => validation.ValidationName == "release-readiness" && !validation.Passed);
        Assert.DoesNotContain(status.Events, eventItem => eventItem.EventType == "WorkflowCompleted");
    }

    [Fact]
    public async Task ProviderReceivesOnlyPersistedPredecessorArtifacts()
    {
        var capturingProvider = new CapturingProvider();
        var capturingService = new OrchestrationService(db, capturingProvider, new FixedTimeProvider());
        var created = await capturingService.CreateGreenfieldAsync("Create a short URL", CancellationToken.None);
        var status = created;
        for (var attempt = 0; attempt < 8 && status.State != WorkflowState.Completed; attempt++)
        {
            status = await capturingService.AdvanceAsync(status.WorkflowId, CancellationToken.None);
        }

        var architectureRequest = capturingProvider.Requests.Single(request => request.TaskType == "architecture-design");
        var implementationRequest = capturingProvider.Requests.Single(request => request.TaskType == "implementation-preparation");
        var validationRequest = capturingProvider.Requests.Single(request => request.TaskType == "validation");
        Assert.Single(architectureRequest.UpstreamArtifactReferences);
        Assert.Single(implementationRequest.UpstreamArtifactReferences);
        Assert.Equal(2, validationRequest.UpstreamArtifactReferences.Count);
    }

    [Fact]
    public async Task CompletedNodeIsNotExecutedAgainOnRepeatedAdvance()
    {
        var status = await service.CreateGreenfieldAsync("Create a short URL", CancellationToken.None);
        for (var attempt = 0; attempt < 8 && status.State != WorkflowState.Completed; attempt++)
        {
            status = await service.AdvanceAsync(status.WorkflowId, CancellationToken.None);
        }

        var normalizedNodeId = status.Nodes.Single(node => node.TaskType == "normalize-requirement").Id;
        var before = await db.AgentExecutions.CountAsync(execution => execution.WorkflowNodeId == normalizedNodeId);
        await service.AdvanceAsync(status.WorkflowId, CancellationToken.None);
        await service.AdvanceAsync(status.WorkflowId, CancellationToken.None);
        var after = await db.AgentExecutions.CountAsync(execution => execution.WorkflowNodeId == normalizedNodeId);

        Assert.Equal(1, before);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task FailedFirstParallelNodeStopsWaveAndPreservesReadySibling()
    {
        var failingService = new OrchestrationService(db, new FirstParallelNodeFailingProvider(), new FixedTimeProvider());
        var created = await failingService.CreateGreenfieldAsync("Create a short URL", CancellationToken.None);
        await failingService.AdvanceAsync(created.WorkflowId, CancellationToken.None);
        var parallel = await failingService.AdvanceAsync(created.WorkflowId, CancellationToken.None);

        Assert.Equal(WorkflowNodeState.Ready, parallel.Nodes.Single(node => node.TaskType == "architecture-design").State);
        Assert.Equal(WorkflowNodeState.Ready, parallel.Nodes.Single(node => node.TaskType == "implementation-preparation").State);

        var failed = await failingService.AdvanceAsync(created.WorkflowId, CancellationToken.None);
        var architecture = failed.Nodes.Single(node => node.TaskType == "architecture-design");
        var implementation = failed.Nodes.Single(node => node.TaskType == "implementation-preparation");

        Assert.Equal(WorkflowState.SafeStopped, failed.State);
        Assert.Equal(WorkflowNodeState.Failed, architecture.State);
        Assert.Equal(WorkflowNodeState.Ready, implementation.State);
        Assert.Equal(1, failed.Executions.Count(execution => execution.WorkflowNodeId == architecture.Id));
        Assert.Equal(0, failed.Executions.Count(execution => execution.WorkflowNodeId == implementation.Id));
        Assert.Equal(WorkflowNodeState.Pending, failed.Nodes.Single(node => node.TaskType == "validation").State);
        Assert.Equal(WorkflowNodeState.Pending, failed.Nodes.Single(node => node.TaskType == "release-readiness").State);
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }
}

internal sealed class FailingProvider(string taskTypeToFail) : IAgentProvider
{
    public string Name => "test-failure-provider";
    public IReadOnlySet<string> CompatibleFallbackProviders { get; } = new HashSet<string>();

    public Task<AgenticSoftwareEngineering.Api.Providers.AgentExecutionResponse> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(request.TaskType == taskTypeToFail
            ? new AgenticSoftwareEngineering.Api.Providers.AgentExecutionResponse(false, string.Empty, "", "", "", "controlled test failure")
            : new DeterministicAgentProvider().ExecuteAsync(request, cancellationToken).Result);
}

internal sealed class MalformedProvider : IAgentProvider
{
    public string Name => "malformed-provider";
    public IReadOnlySet<string> CompatibleFallbackProviders { get; } = new HashSet<string>();

    public Task<AgenticSoftwareEngineering.Api.Providers.AgentExecutionResponse> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new AgenticSoftwareEngineering.Api.Providers.AgentExecutionResponse(true, string.Empty, "normalized-requirement", "deterministic://malformed", "hash"));
}

internal sealed class ReleaseEvidenceGapProvider : IAgentProvider
{
    public string Name => "release-gap-provider";
    public IReadOnlySet<string> CompatibleFallbackProviders { get; } = new HashSet<string>();

    public Task<AgenticSoftwareEngineering.Api.Providers.AgentExecutionResponse> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken)
    {
        var response = new DeterministicAgentProvider().ExecuteAsync(request, cancellationToken).Result;
        return Task.FromResult(request.TaskType == "architecture-design"
            ? response with { ArtifactType = "unexpected-design" }
            : response);
    }
}

internal sealed class CapturingProvider : IAgentProvider
{
    public string Name => "capturing-provider";
    public IReadOnlySet<string> CompatibleFallbackProviders { get; } = new HashSet<string>();
    public List<AgentExecutionRequest> Requests { get; } = new();

    public Task<AgenticSoftwareEngineering.Api.Providers.AgentExecutionResponse> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return new DeterministicAgentProvider().ExecuteAsync(request, cancellationToken);
    }
}

internal sealed class FirstParallelNodeFailingProvider : IAgentProvider
{
    public string Name => "first-parallel-failure-provider";
    public IReadOnlySet<string> CompatibleFallbackProviders { get; } = new HashSet<string>();

    public Task<AgenticSoftwareEngineering.Api.Providers.AgentExecutionResponse> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(request.TaskType == "architecture-design"
            ? new AgenticSoftwareEngineering.Api.Providers.AgentExecutionResponse(false, string.Empty, "", "", "", "controlled first-wave failure")
            : new DeterministicAgentProvider().ExecuteAsync(request, cancellationToken).Result);
}
