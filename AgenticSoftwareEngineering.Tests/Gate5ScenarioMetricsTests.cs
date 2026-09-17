using AgenticSoftwareEngineering.Api.Application.Orchestration;
using AgenticSoftwareEngineering.Api.Domain.Orchestration;
using AgenticSoftwareEngineering.Api.Infrastructure.Persistence;
using AgenticSoftwareEngineering.Api.Providers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AgenticSoftwareEngineering.Tests;

public sealed class Gate5ScenarioMetricsTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly AppDbContext db;

    public Gate5ScenarioMetricsTests()
    {
        connection.Open();
        db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        db.Database.Migrate();
    }

    [Fact]
    public async Task AmbiguousRequirementBlocksBeforeProviderAndResumesAfterClarification()
    {
        var provider = new TrackingProvider("ambiguity-provider");
        var service = CreateService(provider);
        var blocked = await service.CreateGreenfieldAsync("Make shortened URLs expire.", CancellationToken.None);

        Assert.Equal(WorkflowState.Blocked, blocked.State);
        Assert.Equal("Required", blocked.ClarificationStatus);
        Assert.Empty(provider.Requests);
        Assert.Contains(blocked.Events, item => item.EventType == "RequirementAssessed" && item.Details.Contains("ClarificationRequired"));
        Assert.Contains(blocked.Events, item => item.EventType == "ClarificationRequired");

        var resolved = await service.ProvideClarificationAsync(blocked.WorkflowId, new ClarificationRequest("Expire links after 30 days using UTC timestamps."), CancellationToken.None);
        Assert.Equal(WorkflowState.Planning, resolved.State);
        Assert.Equal("Resolved", resolved.ClarificationStatus);
        Assert.Contains(resolved.Events, item => item.EventType == "ClarificationProvided");
        Assert.Contains(resolved.Events, item => item.EventType == "RequirementReassessed" && item.Details.Contains("Clear"));
        Assert.Contains(resolved.Events, item => item.EventType == "ClarificationResolved");
        Assert.Contains(resolved.Artifacts, item => item.ArtifactType == "clarified-requirement" && item.ValidationStatus == ArtifactValidationStatus.Valid);
        Assert.Equal(new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero), await db.EngineeringArtifacts.Where(item => item.WorkflowId == blocked.WorkflowId && item.ArtifactType == "clarified-requirement").Select(item => item.CreatedAtUtc).SingleAsync());

        var completed = await AdvanceUntilCompletedAsync(service, resolved.WorkflowId);
        Assert.Equal(WorkflowState.Completed, completed.State);
        var normalizeRequest = provider.Requests.First(item => item.TaskType == "normalize-requirement");
        Assert.Equal("Make shortened URLs expire. Clarification: Expire links after 30 days using UTC timestamps.", normalizeRequest.EffectiveRequirement);
    }

    [Fact]
    public async Task ClearRequirementReachesNormalizeRequirementProvider()
    {
        var provider = new TrackingProvider("clear-requirement-provider");
        var service = CreateService(provider);
        var created = await service.CreateGreenfieldAsync("Create a short URL", CancellationToken.None);
        await service.AdvanceAsync(created.WorkflowId, CancellationToken.None);

        var normalizeRequest = provider.Requests.Single(item => item.TaskType == "normalize-requirement");
        Assert.Equal("Create a short URL", normalizeRequest.EffectiveRequirement);
    }

    [Fact]
    public async Task ClarificationRejectsBlankDuplicateAndUnrelatedRequests()
    {
        var service = CreateService();
        var blocked = await service.CreateGreenfieldAsync("Make shortened URLs expire.", CancellationToken.None);
        await Assert.ThrowsAsync<ArgumentException>(() => service.ProvideClarificationAsync(blocked.WorkflowId, new ClarificationRequest(" "), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ProvideClarificationAsync(blocked.WorkflowId, new ClarificationRequest("Use a custom alias format."), CancellationToken.None));
        Assert.DoesNotContain((await service.GetStatusAsync(blocked.WorkflowId, CancellationToken.None)).Events, item => item.EventType == "ClarificationResolved");
        var resolved = await service.ProvideClarificationAsync(blocked.WorkflowId, new ClarificationRequest("Use UTC and a 30 day lifetime."), CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ProvideClarificationAsync(resolved.WorkflowId, new ClarificationRequest("Second resolution"), CancellationToken.None));

        var unrelated = await service.CreateGreenfieldAsync("Make shortened URLs expire.", CancellationToken.None);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ProvideClarificationAsync(Guid.NewGuid(), new ClarificationRequest("Unrelated"), CancellationToken.None));
        Assert.Equal(WorkflowState.Blocked, unrelated.State);
    }

    [Fact]
    public async Task ClearGreenfieldAndBrownfieldStatusesRemainReviewerVisible()
    {
        var service = CreateService();
        var greenfield = await CompleteWorkflowAsync(service);
        Assert.Equal("greenfield", greenfield.Scenario);
        var architecture = greenfield.Artifacts.Single(item => item.ArtifactType == "architecture-design" && item.Version == 1);

        var brownfield = await service.ReviseArtifactAsync(greenfield.WorkflowId, architecture.Id, new BrownfieldArtifactRevisionRequest("architecture/gate5-v2", "gate5-hash"), CancellationToken.None);
        Assert.Equal("brownfield", brownfield.Scenario);
        Assert.Contains(brownfield.Events, item => item.EventType == "ImpactAnalysisCompleted");
        Assert.Equal(2, brownfield.PlanRevisions.Count);
    }

    [Fact]
    public async Task MetricsAreDerivedFromPersistedRetryEvidenceAndSurviveFreshServiceQuery()
    {
        var provider = new SequenceFailureProvider(FailureClassification.Transient, null);
        var service = CreateService(provider);
        var completed = await CompleteWorkflowAsync(service);
        var metrics = await service.GetMetricsAsync(completed.WorkflowId, CancellationToken.None);
        var freshService = CreateService(new DeterministicAgentProvider());
        var freshMetrics = await freshService.GetMetricsAsync(completed.WorkflowId, CancellationToken.None);

        Assert.True(metrics.Completed);
        Assert.Equal(metrics.RetryCount, freshMetrics.RetryCount);
        Assert.Equal(1, metrics.RetryCount);
        Assert.True(metrics.ProviderExecutionCount >= 7);
        Assert.NotNull(metrics.WorkflowLatencyMilliseconds);
        Assert.NotNull(metrics.ProviderExecutionLatencyMilliseconds);
        Assert.NotNull(metrics.MeanRecoveryTimeMilliseconds);
        Assert.Equal(0, metrics.FallbackCount);
        Assert.False(metrics.SafeStopped);
        var retryEvent = (await service.GetStatusAsync(completed.WorkflowId, CancellationToken.None)).Events.Single(item => item.EventType == "RetryScheduled");
        Assert.Contains("node-id=", retryEvent.Details);
        Assert.Contains("attempt=1", retryEvent.Details);
    }

    [Fact]
    public async Task MetricsExposeFallbackApprovalAndSafeStoppedEvidenceWithoutFabrication()
    {
        var primary = new SequenceFailureProvider(FailureClassification.Permanent);
        var fallback = new TrackingProvider("fallback");
        var fallbackService = CreateService(primary, fallback);
        var fallbackWorkflow = await fallbackService.CreateGreenfieldAsync("Create a short URL", CancellationToken.None);
        await fallbackService.AdvanceAsync(fallbackWorkflow.WorkflowId, CancellationToken.None);
        var fallbackMetrics = await fallbackService.GetMetricsAsync(fallbackWorkflow.WorkflowId, CancellationToken.None);
        Assert.Equal(1, fallbackMetrics.FallbackCount);
        Assert.Null(fallbackMetrics.MeanRecoveryTimeMilliseconds);

        var approvalService = CreateService(new TrackingProvider("approval-provider"));
        var waiting = await approvalService.CreateGreenfieldAsync("Create a short URL", CancellationToken.None, true);
        for (var index = 0; index < 3; index++) waiting = await approvalService.AdvanceAsync(waiting.WorkflowId, CancellationToken.None);
        var approval = waiting.Approvals.Single();
        var rejected = await approvalService.DecideApprovalAsync(waiting.WorkflowId, approval.Id, new ApprovalDecisionRequest(false, "Rejected for metrics test"), CancellationToken.None);
        var stoppedMetrics = await approvalService.GetMetricsAsync(rejected.WorkflowId, CancellationToken.None);
        Assert.True(stoppedMetrics.SafeStopped);
        Assert.Equal(1, stoppedMetrics.ApprovalCount);
        Assert.NotNull(stoppedMetrics.ApprovalWaitMilliseconds);
    }

    [Fact]
    public async Task UnrecoveredRetryCannotUseAnotherNodesSuccessForMeanRecoveryTime()
    {
        var service = CreateService(new SequenceFailureProvider(FailureClassification.Transient));
        var created = await service.CreateGreenfieldAsync("Create a short URL", CancellationToken.None);
        var retryState = await service.AdvanceAsync(created.WorkflowId, CancellationToken.None);
        var otherNodeId = retryState.Nodes.Single(item => item.TaskType == "decompose-plan").Id;
        var unrelatedExecution = new AgentExecution(otherNodeId, "unrelated-provider", 2, new DateTimeOffset(2026, 9, 16, 12, 1, 0, TimeSpan.Zero));
        unrelatedExecution.Complete(AgentExecutionStatus.Succeeded, new DateTimeOffset(2026, 9, 16, 12, 1, 1, TimeSpan.Zero), "unrelated success");
        db.AgentExecutions.Add(unrelatedExecution);
        await db.SaveChangesAsync();

        var metrics = await service.GetMetricsAsync(created.WorkflowId, CancellationToken.None);

        Assert.Null(metrics.MeanRecoveryTimeMilliseconds);
    }

    private OrchestrationService CreateService(params IAgentProvider[] providers)
    {
        if (providers.Length == 0)
        {
            providers = new IAgentProvider[] { new DeterministicAgentProvider() };
        }

        return new OrchestrationService(db, providers[0], new FixedTimeProvider(), providers);
    }

    private async Task<WorkflowStatusResponse> CompleteWorkflowAsync(OrchestrationService service)
    {
        var status = await service.CreateGreenfieldAsync("Create a short URL", CancellationToken.None);
        return await AdvanceUntilCompletedAsync(service, status.WorkflowId);
    }

    private static async Task<WorkflowStatusResponse> AdvanceUntilCompletedAsync(OrchestrationService service, Guid workflowId)
    {
        var status = await service.GetStatusAsync(workflowId, CancellationToken.None);
        for (var attempt = 0; attempt < 20 && status.State != WorkflowState.Completed; attempt++)
        {
            status = await service.AdvanceAsync(workflowId, CancellationToken.None);
        }

        return status;
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }
}
