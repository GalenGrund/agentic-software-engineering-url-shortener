using AgenticSoftwareEngineering.Api.Application.Orchestration;
using AgenticSoftwareEngineering.Api.Domain.Orchestration;
using AgenticSoftwareEngineering.Api.Infrastructure.Persistence;
using AgenticSoftwareEngineering.Api.Providers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AgenticSoftwareEngineering.Tests;

public sealed class Gate4ReplanningTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly AppDbContext db;

    public Gate4ReplanningTests()
    {
        connection.Open();
        db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        db.Database.Migrate();
    }

    [Fact]
    public void ArtifactRevisionCreatesImmutableSupersedingVersion()
    {
        var workflowId = Guid.NewGuid();
        var first = EngineeringArtifact.Create(workflowId, "architecture-design", 1, "design/v1", "hash-v1", null, null, null, DateTimeOffset.UtcNow);
        var second = EngineeringArtifact.Revise(first, "design/v2", "hash-v2", DateTimeOffset.UtcNow);

        Assert.Equal(1, first.Version);
        Assert.Equal("design/v1", first.ContentReference);
        Assert.Equal(2, second.Version);
        Assert.Equal(first.Id, second.SupersedesArtifactId);
        Assert.Equal(workflowId, second.WorkflowId);
        Assert.Equal("hash-v2", second.ContentHash);
    }

    [Fact]
    public async Task ArtifactDependencyTraversalIsDirectTransitiveAndCycleSafe()
    {
        var service = CreateService();
        var status = await CompleteWorkflowAsync(service);
        var artifacts = await db.EngineeringArtifacts.Where(item => item.WorkflowId == status.WorkflowId).OrderBy(item => item.Version).ToListAsync();
        Assert.NotEmpty(await db.ArtifactDependencies.Where(item => item.DependentArtifactId != Guid.Empty).ToListAsync());

        var root = EngineeringArtifact.Create(status.WorkflowId, "impact-root", 1, "impact/root", "impact-v1", null, null, null, DateTimeOffset.UtcNow);
        var direct = EngineeringArtifact.Create(status.WorkflowId, "impact-direct", 1, "impact/direct", "impact-direct-v1", null, null, null, DateTimeOffset.UtcNow);
        var transitive = EngineeringArtifact.Create(status.WorkflowId, "impact-transitive", 1, "impact/transitive", "impact-transitive-v1", null, null, null, DateTimeOffset.UtcNow);
        db.EngineeringArtifacts.AddRange(root, direct, transitive);
        await db.SaveChangesAsync();
        db.ArtifactDependencies.AddRange(new ArtifactDependency(root.Id, direct.Id), new ArtifactDependency(direct.Id, transitive.Id), new ArtifactDependency(transitive.Id, root.Id));
        await db.SaveChangesAsync();

        var revised = await service.ReviseArtifactAsync(status.WorkflowId, root.Id, new BrownfieldArtifactRevisionRequest("impact/root-v2", "impact-v2"), CancellationToken.None);
        var impactEvent = revised.Events.Single(item => item.EventType == "ImpactAnalysisCompleted" && item.Details.Contains($"artifact={root.Id}"));

        Assert.Contains("impacted-artifacts=3", impactEvent.Details);
        Assert.Contains(artifacts, artifact => artifact.ArtifactType == "normalized-requirement");
    }

    [Fact]
    public async Task CrossWorkflowArtifactDependencyIsRejected()
    {
        var firstWorkflow = Guid.NewGuid();
        var secondWorkflow = Guid.NewGuid();
        var first = EngineeringArtifact.Create(firstWorkflow, "design", 1, "first", "first", null, null, null, DateTimeOffset.UtcNow);
        var second = EngineeringArtifact.Create(secondWorkflow, "design", 1, "second", "second", null, null, null, DateTimeOffset.UtcNow);
        db.EngineeringArtifacts.AddRange(first, second);
        await db.SaveChangesAsync();
        db.ArtifactDependencies.Add(new ArtifactDependency(first.Id, second.Id));

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task DuplicateArtifactDependencyIsRejectedByPersistenceConstraint()
    {
        var workflowId = Guid.NewGuid();
        var first = EngineeringArtifact.Create(workflowId, "first", 1, "first", "first", null, null, null, DateTimeOffset.UtcNow);
        var second = EngineeringArtifact.Create(workflowId, "second", 1, "second", "second", null, null, null, DateTimeOffset.UtcNow);
        db.EngineeringArtifacts.AddRange(first, second);
        await db.SaveChangesAsync();
        db.ArtifactDependencies.Add(new ArtifactDependency(first.Id, second.Id));
        await db.SaveChangesAsync();
        db.ArtifactDependencies.Add(new ArtifactDependency(first.Id, second.Id));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task BrownfieldRevisionInvalidatesOnlyAffectedWorkAndReevaluatesRelease()
    {
        var provider = new TrackingProvider("gate4-provider");
        var service = CreateService(provider);
        var original = await CompleteWorkflowAsync(service);
        var architecture = await db.EngineeringArtifacts.SingleAsync(item => item.WorkflowId == original.WorkflowId && item.ArtifactType == "architecture-design" && item.Version == 1);
        var implementationNodeId = original.Nodes.Single(node => node.TaskType == "implementation-preparation").Id;
        var beforeImplementationExecutions = await db.AgentExecutions.CountAsync(item => item.WorkflowNodeId == implementationNodeId);

        var replanned = await service.ReviseArtifactAsync(original.WorkflowId, architecture.Id, new BrownfieldArtifactRevisionRequest("architecture/v2", "architecture-hash-v2"), CancellationToken.None);
        Assert.Contains(replanned.Events, item => item.EventType == "NodeInvalidated" && item.Details.Contains("brownfield-artifact-revision"));
        Assert.Equal(2, replanned.PlanRevisions.Count);
        Assert.Equal(replanned.PlanRevisions[0].Id, replanned.PlanRevisions[1].SupersedesRevisionId);
        Assert.Equal(WorkflowNodeState.Ready, replanned.Nodes.Single(node => node.TaskType == "architecture-design").State);
        Assert.Equal(WorkflowNodeState.Succeeded, replanned.Nodes.Single(node => node.TaskType == "implementation-preparation").State);

        var completed = await AdvanceUntilCompletedAsync(service, replanned.WorkflowId);
        var architectureNodeId = completed.Nodes.Single(node => node.TaskType == "architecture-design").Id;
        implementationNodeId = completed.Nodes.Single(node => node.TaskType == "implementation-preparation").Id;
        Assert.Equal(2, completed.Executions.Count(item => item.WorkflowNodeId == architectureNodeId));
        Assert.Equal(beforeImplementationExecutions, completed.Executions.Count(item => item.WorkflowNodeId == implementationNodeId));
        Assert.True(completed.Validations.Count(item => item.ValidationName == "release-readiness") >= 2);
        Assert.Contains(completed.Artifacts, item => item.SupersedesArtifactId == architecture.Id);
        Assert.Contains(completed.Events, item => item.EventType == "WorkflowCompleted");
    }

    [Fact]
    public async Task DownstreamProviderUsesOnlyEffectiveArtifactAndMatchingDependencyIds()
    {
        var provider = new TrackingProvider("lineage-provider");
        var service = CreateService(provider);
        var original = await CompleteWorkflowAsync(service);
        var architectureV1 = await db.EngineeringArtifacts.SingleAsync(item => item.WorkflowId == original.WorkflowId && item.ArtifactType == "architecture-design" && item.Version == 1);

        var replanned = await service.ReviseArtifactAsync(original.WorkflowId, architectureV1.Id, new BrownfieldArtifactRevisionRequest("architecture/v2", "architecture-hash-v2"), CancellationToken.None);
        var revisedArchitecture = replanned.Artifacts.Single(item => item.ArtifactType == "architecture-design" && item.Version == 2);
        Assert.Contains(replanned.Artifacts, item => item.Id == architectureV1.Id);

        var completed = await AdvanceUntilCompletedAsync(service, replanned.WorkflowId);
        var validationRequest = provider.Requests.Last(request => request.TaskType == "validation");
        var validationArtifact = completed.Artifacts.Where(item => item.ArtifactType == "validation-evidence").MaxBy(item => item.Version)!;
        var validationDependencyIds = completed.ArtifactDependencies.Where(edge => edge.DependentArtifactId == validationArtifact.Id).Select(edge => edge.ArtifactId).ToHashSet();
        var effectiveInputReferences = completed.Artifacts.Where(item => validationDependencyIds.Contains(item.Id)).Select(item => item.ContentReference).ToHashSet();

        Assert.DoesNotContain("architecture/v1", validationRequest.UpstreamArtifactReferences);
        Assert.Equal(effectiveInputReferences.OrderBy(item => item), validationRequest.UpstreamArtifactReferences.OrderBy(item => item));
        Assert.DoesNotContain(validationDependencyIds, id => id == architectureV1.Id);
        Assert.DoesNotContain(validationDependencyIds, id => id == revisedArchitecture.Id);
        Assert.Contains(completed.Artifacts, item => item.ArtifactType == "architecture-design" && item.Version == 3 && item.SupersedesArtifactId == revisedArchitecture.Id);
    }

    [Fact]
    public async Task FallbackProviderUsesTheSameEffectiveInputSelection()
    {
        var primary = new FailingTaskProvider("validation");
        var fallback = new TrackingProvider("fallback");
        var service = new OrchestrationService(db, primary, new FixedTimeProvider(), new IAgentProvider[] { primary, fallback });
        var status = await CompleteWorkflowAsync(service);
        var fallbackRequest = fallback.Requests.Single(request => request.TaskType == "validation");
        var validationArtifact = status.Artifacts.Single(item => item.ArtifactType == "validation-evidence");
        var dependencyReferences = status.Artifacts
            .Where(artifact => status.ArtifactDependencies.Any(edge => edge.DependentArtifactId == validationArtifact.Id && edge.ArtifactId == artifact.Id))
            .Select(artifact => artifact.ContentReference)
            .ToHashSet();

        Assert.Equal(dependencyReferences.OrderBy(item => item), fallbackRequest.UpstreamArtifactReferences.OrderBy(item => item));
    }

    [Fact]
    public async Task HistoricalValidationEvidenceCannotSatisfyReleaseBeforeCurrentValidationSucceeds()
    {
        var service = CreateService();
        var original = await CompleteWorkflowAsync(service);
        var validationEvidenceCount = await db.ValidationResults.CountAsync(item => item.WorkflowId == original.WorkflowId && item.ValidationName == "validation" && item.Passed);
        var architecture = await db.EngineeringArtifacts.SingleAsync(item => item.WorkflowId == original.WorkflowId && item.ArtifactType == "architecture-design" && item.Version == 1);

        var replanned = await service.ReviseArtifactAsync(original.WorkflowId, architecture.Id, new BrownfieldArtifactRevisionRequest("architecture/v2", "architecture-hash-v2"), CancellationToken.None);

        Assert.Equal(WorkflowState.Planning, replanned.State);
        Assert.Equal(WorkflowNodeState.Invalidated, replanned.Nodes.Single(item => item.TaskType == "release-readiness").State);
        Assert.Equal(validationEvidenceCount, await db.ValidationResults.CountAsync(item => item.WorkflowId == original.WorkflowId && item.ValidationName == "validation" && item.Passed));

        var completed = await AdvanceUntilCompletedAsync(service, replanned.WorkflowId);
        Assert.Equal(WorkflowState.Completed, completed.State);
        Assert.True(completed.Validations.Count(item => item.ValidationName == "validation" && item.Passed) >= 2);
        Assert.True(completed.Validations.Count(item => item.ValidationName == "release-readiness" && item.Passed) >= 2);
    }

    [Fact]
    public async Task RollbackRestoresTargetIncomingDependencyLineage()
    {
        var service = CreateService();
        var original = await CompleteWorkflowAsync(service);
        var architectureV1 = await db.EngineeringArtifacts.SingleAsync(item => item.WorkflowId == original.WorkflowId && item.ArtifactType == "architecture-design" && item.Version == 1);
        var originalIncoming = await db.ArtifactDependencies.Where(item => item.DependentArtifactId == architectureV1.Id).Select(item => item.ArtifactId).ToListAsync();
        var revised = await service.ReviseArtifactAsync(original.WorkflowId, architectureV1.Id, new BrownfieldArtifactRevisionRequest("architecture/v2", "architecture-hash-v2"), CancellationToken.None);
        var completed = await AdvanceUntilCompletedAsync(service, revised.WorkflowId);
        var current = completed.Artifacts.Where(item => item.ArtifactType == "architecture-design").MaxBy(item => item.Version)!;

        var rolledBack = await service.RollbackAsync(completed.WorkflowId, new WorkflowRollbackRequest(architectureV1.Id), CancellationToken.None);
        var rollbackArtifact = rolledBack.Artifacts.Where(item => item.ArtifactType == "architecture-design").MaxBy(item => item.Version)!;
        var rollbackIncoming = rolledBack.ArtifactDependencies.Where(item => item.DependentArtifactId == rollbackArtifact.Id).Select(item => item.ArtifactId).ToList();

        Assert.Equal(current.Id, rollbackArtifact.SupersedesArtifactId);
        Assert.Equal(originalIncoming.OrderBy(item => item), rollbackIncoming.OrderBy(item => item));
        Assert.Contains(rolledBack.Artifacts, item => item.Id == architectureV1.Id);
        Assert.Contains(rolledBack.Artifacts, item => item.Id == current.Id);
        Assert.DoesNotContain(rollbackIncoming, item => item == current.Id);
    }

    [Fact]
    public async Task ImpactAnalysisIsScopedToTheRequestedWorkflow()
    {
        var service = CreateService();
        var first = await CompleteWorkflowAsync(service);
        var second = await CompleteWorkflowAsync(service);
        var firstRoot = await db.EngineeringArtifacts.SingleAsync(item => item.WorkflowId == first.WorkflowId && item.ArtifactType == "architecture-design" && item.Version == 1);
        var secondRoot = await db.EngineeringArtifacts.SingleAsync(item => item.WorkflowId == second.WorkflowId && item.ArtifactType == "architecture-design" && item.Version == 1);

        var revised = await service.ReviseArtifactAsync(first.WorkflowId, firstRoot.Id, new BrownfieldArtifactRevisionRequest("first/architecture/v2", "first-architecture-v2"), CancellationToken.None);
        var impactEvent = revised.Events.Last(item => item.EventType == "ImpactAnalysisCompleted");

        Assert.Contains($"artifact={firstRoot.Id}", impactEvent.Details);
        Assert.DoesNotContain(secondRoot.Id.ToString(), impactEvent.Details);
        Assert.Single((await service.GetStatusAsync(second.WorkflowId, CancellationToken.None)).PlanRevisions);
    }

    [Fact]
    public async Task RevisedHighRiskWorkRequiresApprovalUnderRevisionTwo()
    {
        var provider = new TrackingProvider("gate4-provider");
        var service = CreateService(provider);
        var original = await CompleteWorkflowAsync(service);
        var implementation = await db.EngineeringArtifacts.SingleAsync(item => item.WorkflowId == original.WorkflowId && item.ArtifactType == "implementation-preparation" && item.Version == 1);
        var priorPrivilegedApplyCount = provider.Requests.Count(request => request.Mode == AgentExecutionMode.Apply && request.TaskType == "implementation-preparation");

        var replanned = await service.ReviseArtifactAsync(original.WorkflowId, implementation.Id, new BrownfieldArtifactRevisionRequest("implementation/v2", "implementation-hash-v2", true), CancellationToken.None);
        var waiting = await service.AdvanceAsync(replanned.WorkflowId, CancellationToken.None);

        Assert.Equal(WorkflowState.WaitingForApproval, waiting.State);
        Assert.Contains(waiting.Approvals, approval => approval.PlanRevisionId == waiting.PlanRevisions.Single(item => item.Revision == 2).Id && approval.Decision == ApprovalDecision.Pending);
        Assert.Equal(priorPrivilegedApplyCount, provider.Requests.Count(request => request.Mode == AgentExecutionMode.Apply && request.TaskType == "implementation-preparation"));
    }

    [Fact]
    public async Task RevisionOneApprovalCannotAuthorizeRevisionTwoWork()
    {
        var service = CreateService(new TrackingProvider("gate4-provider"));
        var initial = await service.CreateGreenfieldAsync("Create a short URL", CancellationToken.None, true);
        for (var attempt = 0; attempt < 3; attempt++) initial = await service.AdvanceAsync(initial.WorkflowId, CancellationToken.None);
        initial = await service.DecideApprovalAsync(initial.WorkflowId, initial.Approvals.Single().Id, new ApprovalDecisionRequest(true, "Approved revision one"), CancellationToken.None);
        initial = await AdvanceUntilCompletedAsync(service, initial.WorkflowId);
        var implementation = await db.EngineeringArtifacts.SingleAsync(item => item.WorkflowId == initial.WorkflowId && item.ArtifactType == "implementation-preparation" && item.Version == 1);

        var replanned = await service.ReviseArtifactAsync(initial.WorkflowId, implementation.Id, new BrownfieldArtifactRevisionRequest("implementation/v2", "implementation-hash-v2", true), CancellationToken.None);
        var waiting = await service.AdvanceAsync(replanned.WorkflowId, CancellationToken.None);

        Assert.Equal(WorkflowState.WaitingForApproval, waiting.State);
        Assert.Contains(waiting.Approvals, approval => approval.PlanRevisionId == waiting.PlanRevisions.Single(item => item.Revision == 1).Id && approval.Decision == ApprovalDecision.Approved);
        Assert.Contains(waiting.Approvals, approval => approval.PlanRevisionId == waiting.PlanRevisions.Single(item => item.Revision == 2).Id && approval.Decision == ApprovalDecision.Pending);
    }

    [Fact]
    public async Task RollbackPreservesHistoryInvalidatesAffectedWorkAndRecordsEvidence()
    {
        var service = CreateService(new TrackingProvider("gate4-provider"));
        var original = await CompleteWorkflowAsync(service);
        var architectureV1 = await db.EngineeringArtifacts.SingleAsync(item => item.WorkflowId == original.WorkflowId && item.ArtifactType == "architecture-design" && item.Version == 1);
        var revised = await service.ReviseArtifactAsync(original.WorkflowId, architectureV1.Id, new BrownfieldArtifactRevisionRequest("architecture/v2", "architecture-hash-v2"), CancellationToken.None);
        var completedRevision = await AdvanceUntilCompletedAsync(service, revised.WorkflowId);
        var historyCount = await db.EngineeringArtifacts.CountAsync(item => item.WorkflowId == original.WorkflowId);

        var rollback = await service.RollbackAsync(completedRevision.WorkflowId, new WorkflowRollbackRequest(architectureV1.Id), CancellationToken.None);
        Assert.Equal(3, rollback.PlanRevisions.Count);
        Assert.Contains(rollback.Events, item => item.EventType == "RollbackStarted");
        Assert.Contains(rollback.Events, item => item.EventType == "RollbackArtifactSelected");
        Assert.Contains(rollback.Events, item => item.EventType == "NodeInvalidated" && item.Details.Contains("workflow-artifact-rollback"));
        Assert.True(await db.EngineeringArtifacts.CountAsync(item => item.WorkflowId == original.WorkflowId) > historyCount);

        var completed = await AdvanceUntilCompletedAsync(service, rollback.WorkflowId);
        Assert.Equal(WorkflowState.Completed, completed.State);
        Assert.Contains(completed.Artifacts, item => item.SupersedesArtifactId == completedRevision.Artifacts.Where(artifact => artifact.ArtifactType == "architecture-design").MaxBy(artifact => artifact.Version)!.Id);
    }

    private OrchestrationService CreateService(IAgentProvider? provider = null) =>
        new(db, provider ?? new DeterministicAgentProvider(), new FixedTimeProvider());

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

internal sealed class FailingTaskProvider(string taskType) : IAgentProvider
{
    public string Name => "failing-task-primary";
    public IReadOnlySet<string> CompatibleFallbackProviders { get; } = new HashSet<string>(new[] { "fallback" }, StringComparer.OrdinalIgnoreCase);

    public Task<AgenticSoftwareEngineering.Api.Providers.AgentExecutionResponse> ExecuteAsync(AgentExecutionRequest request, CancellationToken cancellationToken) =>
        request.TaskType == taskType
            ? Task.FromResult(new AgenticSoftwareEngineering.Api.Providers.AgentExecutionResponse(false, string.Empty, string.Empty, string.Empty, string.Empty, "controlled failure", FailureClassification.Permanent))
            : new DeterministicAgentProvider().ExecuteAsync(request, cancellationToken);
}