using AgenticSoftwareEngineering.Api.Application.Orchestration;
using AgenticSoftwareEngineering.Api.Domain.Orchestration;
using AgenticSoftwareEngineering.Api.Infrastructure.Persistence;
using AgenticSoftwareEngineering.Api.Providers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AgenticSoftwareEngineering.Tests;

public sealed class Gate6GovernanceIntegrationTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly AppDbContext db;

    public Gate6GovernanceIntegrationTests()
    {
        connection.Open();
        db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        db.Database.Migrate();
    }

    [Fact]
    public async Task HighRiskProposalStopsBeforeApplyAndAppliesOnlyAfterExactApproval()
    {
        var provider = new TrackingProvider("gate6-provider");
        var service = CreateService(provider);
        var status = await service.CreateGreenfieldAsync("Create a short URL", CancellationToken.None, true);
        for (var index = 0; index < 3; index++) status = await service.AdvanceAsync(status.WorkflowId, CancellationToken.None);

        var proposal = status.ActionProposals.Single();
        var changeSet = status.ChangeSets.Single();
        var approval = status.Approvals.Single();
        Assert.Equal(ActionProposalState.Proposed, proposal.State);
        Assert.Equal(changeSet.Id, proposal.ChangeSetId);
        Assert.Equal(changeSet.Fingerprint, approval.ChangeSetFingerprint);
        Assert.Contains(provider.Requests, request => request.Mode == AgentExecutionMode.Proposal && request.TaskType == "implementation-preparation");
        Assert.DoesNotContain(provider.Requests, request => request.Mode == AgentExecutionMode.Apply && request.TaskType == "implementation-preparation");

        var completed = await service.DecideApprovalAsync(status.WorkflowId, approval.Id, new ApprovalDecisionRequest(true, "Approved exact ChangeSet"), CancellationToken.None);
        var appliedProposal = completed.ActionProposals.Single();
        Assert.Equal(ActionProposalState.Applied, appliedProposal.State);
        Assert.Contains(completed.ChangeSetAuthorizations, item => item.ChangeSetId == changeSet.Id && item.ApprovalId == approval.Id && item.ChangeSetFingerprint == changeSet.Fingerprint);
        Assert.Contains(provider.Requests, request => request.Mode == AgentExecutionMode.Apply && request.AuthorizedChangeSetId == changeSet.Id && request.AuthorizedChangeSetFingerprint == changeSet.Fingerprint);
        Assert.NotNull(appliedProposal.AppliedExecutionId);
    }

    [Fact]
    public async Task RejectedExactChangeSetCannotApply()
    {
        var provider = new TrackingProvider("gate6-provider");
        var service = CreateService(provider);
        var status = await service.CreateGreenfieldAsync("Create a short URL", CancellationToken.None, true);
        for (var index = 0; index < 3; index++) status = await service.AdvanceAsync(status.WorkflowId, CancellationToken.None);
        var approval = status.Approvals.Single();

        var stopped = await service.DecideApprovalAsync(status.WorkflowId, approval.Id, new ApprovalDecisionRequest(false, "Rejected exact ChangeSet"), CancellationToken.None);

        Assert.Equal(WorkflowState.SafeStopped, stopped.State);
        Assert.Equal(ActionProposalState.Rejected, stopped.ActionProposals.Single().State);
        Assert.DoesNotContain(provider.Requests, request => request.Mode == AgentExecutionMode.Apply && request.TaskType == "implementation-preparation");
        Assert.Empty(stopped.ChangeSetAuthorizations);
    }

    [Fact]
    public async Task MalformedProviderProposalSafeStopsBeforeAuthorization()
    {
        var service = CreateService(new MalformedProposalProvider());
        var status = await service.CreateGreenfieldAsync("Create a short URL", CancellationToken.None, true);
        for (var index = 0; index < 3; index++) status = await service.AdvanceAsync(status.WorkflowId, CancellationToken.None);

        Assert.Equal(WorkflowState.SafeStopped, status.State);
        Assert.Empty(status.ActionProposals);
        Assert.Empty(status.ChangeSets);
        Assert.Contains(status.Events, item => item.EventType == "HumanEscalationRequired");
    }

    [Fact]
    public async Task FingerprintMismatchPreventsPrivilegedAuthorization()
    {
        var service = CreateService(new TrackingProvider("gate6-provider"));
        var status = await service.CreateGreenfieldAsync("Create a short URL", CancellationToken.None, true);
        for (var index = 0; index < 3; index++) status = await service.AdvanceAsync(status.WorkflowId, CancellationToken.None);
        var approval = status.Approvals.Single();
        var changeSet = await db.ChangeSets.SingleAsync(item => item.Id == approval.ChangeSetId);
        db.Entry(changeSet).Property(item => item.Fingerprint).CurrentValue = "tampered";
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DecideApprovalAsync(status.WorkflowId, approval.Id, new ApprovalDecisionRequest(true, "Tampered approval"), CancellationToken.None));

        Assert.DoesNotContain((await service.GetStatusAsync(status.WorkflowId, CancellationToken.None)).ChangeSetAuthorizations, item => item.ChangeSetId == changeSet.Id);
    }

    [Fact]
    public async Task MediumRiskAuthorizationIsPersistedBeforeApplyInvocation()
    {
        var provider = new PersistenceObservingProvider(db);
        var service = CreateService(provider);
        var status = await service.CreateGreenfieldAsync("Create a short URL", CancellationToken.None);

        for (var index = 0; index < 5 && status.State != WorkflowState.Completed; index++)
        {
            status = await service.AdvanceAsync(status.WorkflowId, CancellationToken.None);
        }

        Assert.Contains(provider.Requests, item => item.Mode == AgentExecutionMode.Apply && item.TaskType == "implementation-preparation");
        var implementationApplyRequest = provider.Requests.First(item => item.Mode == AgentExecutionMode.Apply && item.TaskType == "implementation-preparation");
        Assert.True(provider.ObservedAuthorizationBeforeApply, $"authorizationCount={provider.AuthorizationCountAtApply};applyRequest={implementationApplyRequest.AuthorizedChangeSetId}");
        Assert.Contains(status.ChangeSetAuthorizations, item => item.Mechanism == ChangeSetAuthorizationMechanism.PolicyAuthorized && item.ApprovalId is null);
        Assert.Contains(status.ActionProposals, item => item.Risk == RiskLevel.Medium && item.State == ActionProposalState.Applied);
    }

    [Fact]
    public async Task MissingPersistedAuthorizationBlocksApply()
    {
        var provider = new TrackingProvider("gate6-provider");
        var service = CreateService(provider);
        var status = await service.CreateGreenfieldAsync("Create a short URL", CancellationToken.None, true);
        for (var index = 0; index < 3; index++) status = await service.AdvanceAsync(status.WorkflowId, CancellationToken.None);
        var proposal = await db.ActionProposals.SingleAsync(item => item.WorkflowId == status.WorkflowId);
        var changeSet = await db.ChangeSets.SingleAsync(item => item.ActionProposalId == proposal.Id);
        var approval = await db.Approvals.SingleAsync(item => item.ActionProposalId == proposal.Id);
        approval.Decide(ApprovalDecision.Approved, "Prepare missing-authorization test", DateTimeOffset.UtcNow);
        proposal.Authorize(approval, changeSet, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
        var beforeApplyCount = provider.Requests.Count(item => item.Mode == AgentExecutionMode.Apply);

        var stopped = await service.ApplyAuthorizedProposalAsync(status.WorkflowId, proposal.Id, CancellationToken.None);

        Assert.Equal(WorkflowState.SafeStopped, stopped.State);
        Assert.Equal(beforeApplyCount, provider.Requests.Count(item => item.Mode == AgentExecutionMode.Apply));
        Assert.Equal(ActionProposalState.Authorized, stopped.ActionProposals.Single().State);
    }

    [Fact]
    public async Task AuthorizedRevisionOneChangeSetCannotApplyAfterRevisionTwoBecomesActive()
    {
        var provider = new TrackingProvider("gate6-provider");
        var service = CreateService(provider);
        var status = await service.CreateGreenfieldAsync("Create a short URL", CancellationToken.None, true);
        for (var index = 0; index < 3; index++) status = await service.AdvanceAsync(status.WorkflowId, CancellationToken.None);
        var proposal = await db.ActionProposals.SingleAsync(item => item.WorkflowId == status.WorkflowId);
        var changeSet = await db.ChangeSets.SingleAsync(item => item.ActionProposalId == proposal.Id);
        var approval = await db.Approvals.SingleAsync(item => item.ActionProposalId == proposal.Id);
        approval.Decide(ApprovalDecision.Approved, "Authorize revision one", DateTimeOffset.UtcNow);
        proposal.Authorize(approval, changeSet, DateTimeOffset.UtcNow);
        db.PlanRevisions.Add(new PlanRevision(status.WorkflowId, 2, DateTimeOffset.UtcNow, proposal.PlanRevisionId));
        await db.SaveChangesAsync();
        var beforeApplyCount = provider.Requests.Count(item => item.Mode == AgentExecutionMode.Apply);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAuthorizedProposalAsync(status.WorkflowId, proposal.Id, CancellationToken.None));

        Assert.Equal(beforeApplyCount, provider.Requests.Count(item => item.Mode == AgentExecutionMode.Apply));
    }

    [Fact]
    public async Task FallbackGeneratedProposalCannotReuseDifferentProposalAuthorization()
    {
        var primaryService = CreateService(new TrackingProvider("primary"));
        var primaryWorkflow = await primaryService.CreateGreenfieldAsync("Create a short URL", CancellationToken.None, true);
        for (var index = 0; index < 3; index++) primaryWorkflow = await primaryService.AdvanceAsync(primaryWorkflow.WorkflowId, CancellationToken.None);
        var primaryProposal = await db.ActionProposals.SingleAsync(item => item.WorkflowId == primaryWorkflow.WorkflowId);
        var primaryChangeSet = await db.ChangeSets.SingleAsync(item => item.ActionProposalId == primaryProposal.Id);
        var primaryApproval = await db.Approvals.SingleAsync(item => item.ActionProposalId == primaryProposal.Id);
        primaryApproval.Decide(ApprovalDecision.Approved, "Authorize primary proposal only", DateTimeOffset.UtcNow);

        var fallback = new TrackingProvider("fallback");
        var service = CreateService(new ProposalFailureProvider(), fallback);
        var fallbackWorkflow = await service.CreateGreenfieldAsync("Create another short URL", CancellationToken.None, true);
        for (var index = 0; index < 3; index++) fallbackWorkflow = await service.AdvanceAsync(fallbackWorkflow.WorkflowId, CancellationToken.None);
        var fallbackProposal = await db.ActionProposals.SingleAsync(item => item.WorkflowId == fallbackWorkflow.WorkflowId);
        var fallbackChangeSet = await db.ChangeSets.SingleAsync(item => item.ActionProposalId == fallbackProposal.Id);

        Assert.NotEqual(primaryProposal.Id, fallbackProposal.Id);
        Assert.NotEqual(primaryChangeSet.Id, fallbackChangeSet.Id);
        Assert.NotEqual(primaryChangeSet.Fingerprint, fallbackChangeSet.Fingerprint);
        Assert.Throws<InvalidOperationException>(() => fallbackProposal.Authorize(primaryApproval, fallbackChangeSet, DateTimeOffset.UtcNow));
        Assert.DoesNotContain(fallback.Requests, request => request.Mode == AgentExecutionMode.Apply && request.AuthorizedChangeSetId == fallbackChangeSet.Id);
    }

    [Fact]
    public async Task AppliedProposalCannotBeAppliedAgain()
    {
        var provider = new TrackingProvider("gate6-provider");
        var service = CreateService(provider);
        var status = await service.CreateGreenfieldAsync("Create a short URL", CancellationToken.None, true);
        for (var index = 0; index < 3; index++) status = await service.AdvanceAsync(status.WorkflowId, CancellationToken.None);
        var completed = await service.DecideApprovalAsync(status.WorkflowId, status.Approvals.Single().Id, new ApprovalDecisionRequest(true, "Authorize once"), CancellationToken.None);
        var proposal = completed.ActionProposals.Single();
        var applyCount = provider.Requests.Count(request => request.Mode == AgentExecutionMode.Apply && request.TaskType == "implementation-preparation");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAuthorizedProposalAsync(completed.WorkflowId, proposal.Id, CancellationToken.None));

        Assert.Equal(applyCount, provider.Requests.Count(request => request.Mode == AgentExecutionMode.Apply && request.TaskType == "implementation-preparation"));
        Assert.Equal(ActionProposalState.Applied, (await service.GetStatusAsync(completed.WorkflowId, CancellationToken.None)).ActionProposals.Single().State);
    }

    private OrchestrationService CreateService(params IAgentProvider[] providers) =>
        new(db, providers[0], new FixedTimeProvider(), providers);

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }
}

internal sealed class MalformedProposalProvider : IAgentProvider
{
    public string Name => "malformed-proposal";
    public IReadOnlySet<string> CompatibleFallbackProviders { get; } = new HashSet<string>();

    public Task<AgenticSoftwareEngineering.Api.Providers.AgentExecutionResponse> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new AgenticSoftwareEngineering.Api.Providers.AgentExecutionResponse(true, "output", "implementation-preparation", "deterministic://malformed", "hash"));
}

internal sealed class ProposalFailureProvider : IAgentProvider
{
    public string Name => "primary-proposal";
    public IReadOnlySet<string> CompatibleFallbackProviders { get; } = new HashSet<string>(new[] { "fallback" }, StringComparer.OrdinalIgnoreCase);

    public Task<AgenticSoftwareEngineering.Api.Providers.AgentExecutionResponse> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken) =>
        request.TaskType == "implementation-preparation" && request.Mode == AgentExecutionMode.Proposal
            ? Task.FromResult(new AgenticSoftwareEngineering.Api.Providers.AgentExecutionResponse(false, string.Empty, string.Empty, string.Empty, string.Empty, "proposal failure", FailureClassification.Permanent))
            : new DeterministicAgentProvider().ExecuteAsync(request, cancellationToken);
}

internal sealed class PersistenceObservingProvider(AppDbContext db) : IAgentProvider
{
    public string Name => "persistence-observer";
    public IReadOnlySet<string> CompatibleFallbackProviders { get; } = new HashSet<string>();
    public List<AgentExecutionRequest> Requests { get; } = new();
    public bool ObservedAuthorizationBeforeApply { get; private set; }
    public int AuthorizationCountAtApply { get; private set; }

    public async Task<AgenticSoftwareEngineering.Api.Providers.AgentExecutionResponse> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (request.Mode == AgentExecutionMode.Apply)
        {
            if (request.TaskType == "implementation-preparation")
            {
                AuthorizationCountAtApply = await db.ChangeSetAuthorizations.CountAsync(item => item.WorkflowId == request.WorkflowId, cancellationToken);
                ObservedAuthorizationBeforeApply = await db.ChangeSetAuthorizations.AnyAsync(item => item.ChangeSetId == request.AuthorizedChangeSetId && item.ChangeSetFingerprint == request.AuthorizedChangeSetFingerprint, cancellationToken);
            }
        }

        return await new DeterministicAgentProvider().ExecuteAsync(request, cancellationToken);
    }
}
