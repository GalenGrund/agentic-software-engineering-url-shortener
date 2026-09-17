using AgenticSoftwareEngineering.Api.Domain.Orchestration;
using AgenticSoftwareEngineering.Api.Infrastructure.Persistence;
using AgenticSoftwareEngineering.Api.Providers;
using Microsoft.EntityFrameworkCore;

namespace AgenticSoftwareEngineering.Api.Application.Orchestration;

public sealed class OrchestrationService(
    AppDbContext db,
    IAgentProvider provider,
    TimeProvider timeProvider) : IOrchestrationService
{
    private static readonly IReadOnlySet<string> RequiredArtifactTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "normalized-requirement",
        "plan-decomposition",
        "architecture-design",
        "implementation-preparation",
        "validation-evidence"
    };

    private static readonly IReadOnlyDictionary<string, string> TaskInstructions = new Dictionary<string, string>
    {
        ["normalize-requirement"] = "Normalize the supplied URL-shortener requirement.",
        ["decompose-plan"] = "Decompose the normalized requirement into an executable engineering plan.",
        ["architecture-design"] = "Produce the architecture/design output for the planned capability.",
        ["implementation-preparation"] = "Prepare the implementation work context without changing repository files.",
        ["validation"] = "Validate the prepared engineering outputs and record deterministic evidence.",
        ["release-readiness"] = "Evaluate release readiness from required workflow evidence."
    };

    public async Task<WorkflowStatusResponse> CreateGreenfieldAsync(string requirement, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(requirement))
        {
            throw new ArgumentException("A requirement is required.", nameof(requirement));
        }

        var now = timeProvider.GetUtcNow();
        var workflow = new Workflow("greenfield-url-shortener", now);
        workflow.TransitionTo(WorkflowState.Planning);
        var plan = new PlanRevision(workflow.Id, 1, now);
        var nodes = CreateNodes(workflow.Id, now);
        var dependencies = CreateDependencies(nodes);

        db.Workflows.Add(workflow);
        db.PlanRevisions.Add(plan);
        db.WorkflowNodes.AddRange(nodes);
        db.Dependencies.AddRange(dependencies);
        AddEvent(workflow.Id, "WorkflowCreated", requirement, now);
        AddEvent(workflow.Id, "PlanRevisionCreated", $"revision={plan.Revision}", now);

        var initialReady = nodes.Single(node => node.TaskType == "normalize-requirement");
        initialReady.TransitionTo(WorkflowNodeState.Ready);
        AddEvent(workflow.Id, "NodeReady", initialReady.Name, now);
        await db.SaveChangesAsync(cancellationToken);
        return await GetStatusAsync(workflow.Id, cancellationToken);
    }

    public async Task<WorkflowStatusResponse> AdvanceAsync(Guid workflowId, CancellationToken cancellationToken)
    {
        var workflow = await db.Workflows.SingleOrDefaultAsync(item => item.Id == workflowId, cancellationToken)
            ?? throw new KeyNotFoundException($"Workflow '{workflowId}' was not found.");
        var nodes = await db.WorkflowNodes.Where(node => node.WorkflowId == workflowId).ToListAsync(cancellationToken);
        var dependencies = await db.Dependencies
            .Where(dependency => nodes.Select(node => node.Id).Contains(dependency.SuccessorNodeId))
            .ToListAsync(cancellationToken);

        var newlyReady = RefreshReadyNodes(workflowId, nodes, dependencies);
        var readyWave = nodes.Where(node => node.State == WorkflowNodeState.Ready).OrderBy(node => node.Name).ToList();
        if (readyWave.Count == 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            return await GetStatusAsync(workflowId, cancellationToken);
        }

        if (newlyReady)
        {
            await db.SaveChangesAsync(cancellationToken);
            return await GetStatusAsync(workflowId, cancellationToken);
        }

        if (workflow.State == WorkflowState.Planning)
        {
            TransitionWorkflow(workflow, WorkflowState.Executing, "WorkflowStateChanged", "Planning -> Executing");
        }

        foreach (var node in readyWave)
        {
            await ExecuteNodeAsync(workflow, node, nodes, dependencies, cancellationToken);
            if (workflow.State != WorkflowState.Executing)
            {
                break;
            }
        }

        RefreshReadyNodes(workflowId, nodes, dependencies);
        await db.SaveChangesAsync(cancellationToken);
        return await GetStatusAsync(workflowId, cancellationToken);
    }

    public async Task<WorkflowStatusResponse> GetStatusAsync(Guid workflowId, CancellationToken cancellationToken)
    {
        var workflow = await db.Workflows.AsNoTracking().SingleOrDefaultAsync(item => item.Id == workflowId, cancellationToken)
            ?? throw new KeyNotFoundException($"Workflow '{workflowId}' was not found.");
        var nodes = await db.WorkflowNodes.AsNoTracking().Where(node => node.WorkflowId == workflowId).OrderBy(node => node.Name).ToListAsync(cancellationToken);
        var nodeIds = nodes.Select(node => node.Id).ToArray();
        var dependencies = await db.Dependencies.AsNoTracking().Where(item => nodeIds.Contains(item.SuccessorNodeId)).ToListAsync(cancellationToken);
        var executions = (await db.AgentExecutions.AsNoTracking().Where(item => nodeIds.Contains(item.WorkflowNodeId)).ToListAsync(cancellationToken)).OrderBy(item => item.StartedAtUtc).ToList();
        var artifacts = (await db.EngineeringArtifacts.AsNoTracking().Where(item => item.WorkflowId == workflowId).ToListAsync(cancellationToken)).OrderBy(item => item.Version).ToList();
        var validations = (await db.ValidationResults.AsNoTracking().Where(item => item.WorkflowId == workflowId).ToListAsync(cancellationToken)).OrderBy(item => item.ValidatedAtUtc).ToList();
        var events = (await db.WorkflowEvents.AsNoTracking().Where(item => item.WorkflowId == workflowId).ToListAsync(cancellationToken)).OrderBy(item => item.OccurredAtUtc).ToList();

        return new WorkflowStatusResponse(
            workflow.Id,
            workflow.Name,
            workflow.State,
            nodes.Select(node => new NodeStatusResponse(node.Id, node.Name, node.TaskType, node.State)).ToList(),
            dependencies.Select(item => new DependencyStatusResponse(item.PredecessorNodeId, item.SuccessorNodeId)).ToList(),
            executions.Select(item => new AgenticSoftwareEngineering.Api.Application.Orchestration.AgentExecutionResponse(item.Id, item.WorkflowNodeId, item.ProviderName, item.Attempt, item.Status, item.StartedAtUtc, item.CompletedAtUtc)).ToList(),
            artifacts.Select(item => new ArtifactStatusResponse(item.Id, item.ArtifactType, item.Version, item.ContentReference, item.ContentHash, item.ProducerNodeId, item.ProducerExecutionId, item.ValidationStatus)).ToList(),
            validations.Select(item => new ValidationStatusResponse(item.Id, item.ValidationName, item.Passed, item.Details, item.ValidatedAtUtc)).ToList(),
            events.Select(item => new EventStatusResponse(item.Id, item.EventType, item.Details, item.OccurredAtUtc)).ToList());
    }

    private async Task ExecuteNodeAsync(Workflow workflow, WorkflowNode node, IReadOnlyList<WorkflowNode> nodes, IReadOnlyList<Dependency> dependencies, CancellationToken cancellationToken)
    {
        node.TransitionTo(WorkflowNodeState.Executing);
        AddEvent(workflow.Id, "NodeExecutionStarted", node.Name, timeProvider.GetUtcNow());
        var startedAt = timeProvider.GetUtcNow();
        var execution = new AgentExecution(node.Id, provider.Name, 1, startedAt);
        db.AgentExecutions.Add(execution);

        var predecessorNodeIds = dependencies
            .Where(dependency => dependency.SuccessorNodeId == node.Id)
            .Select(dependency => dependency.PredecessorNodeId)
            .ToArray();
        var upstreamReferences = await db.EngineeringArtifacts
            .Where(artifact => artifact.WorkflowId == workflow.Id && artifact.ProducerNodeId.HasValue && predecessorNodeIds.Contains(artifact.ProducerNodeId.Value))
            .Select(artifact => artifact.ContentReference)
            .ToListAsync(cancellationToken);
        var request = new AgentExecutionRequest(workflow.Id, node.Id, node.TaskType, TaskInstructions[node.TaskType], upstreamReferences, execution.Attempt);
        var response = await provider.ExecuteAsync(request, cancellationToken);
        execution.Complete(response.Succeeded ? AgentExecutionStatus.Succeeded : AgentExecutionStatus.Failed, timeProvider.GetUtcNow(), response.Output);
        AddEvent(workflow.Id, response.Succeeded ? "ProviderExecutionCompleted" : "ProviderExecutionFailed", node.Name, timeProvider.GetUtcNow());

        if (!response.Succeeded)
        {
            node.TransitionTo(WorkflowNodeState.Failed);
            TransitionWorkflow(workflow, WorkflowState.Failed, "NodeFailed", node.Name);
            return;
        }

        node.TransitionTo(WorkflowNodeState.Validating);
        EngineeringArtifact? artifact = null;
        var exitGatePassed = HasStructurallyValidOutput(response);
        if (exitGatePassed)
        {
            artifact = EngineeringArtifact.Create(workflow.Id, response.ArtifactType, await NextArtifactVersionAsync(workflow.Id, response.ArtifactType, cancellationToken), response.ContentReference, response.ContentHash, node.Id, execution.Id, null, timeProvider.GetUtcNow());
            db.EngineeringArtifacts.Add(artifact);
            AddEvent(workflow.Id, "ArtifactProduced", response.ArtifactType, timeProvider.GetUtcNow());
            exitGatePassed = artifact.WorkflowId == workflow.Id &&
                artifact.ProducerNodeId == node.Id &&
                artifact.ProducerExecutionId == execution.Id;
        }

        var exitGate = new ValidationResult(workflow.Id, $"{node.TaskType}-exit-gate", exitGatePassed, exitGatePassed ? "Required provider output and producer lineage passed." : "Required provider output or producer lineage was missing.", timeProvider.GetUtcNow());
        db.ValidationResults.Add(exitGate);
        AddEvent(workflow.Id, "NodeGateEvaluated", $"{node.Name}:passed={exitGatePassed}", timeProvider.GetUtcNow());

        if (!exitGatePassed)
        {
            if (artifact is not null)
            {
                artifact.SetValidationStatus(ArtifactValidationStatus.Invalid);
            }
            node.TransitionTo(WorkflowNodeState.Failed);
            TransitionWorkflow(workflow, WorkflowState.Failed, "NodeFailed", $"{node.Name}:exit-gate");
            return;
        }

        if (node.TaskType == "release-readiness")
        {
            var requiredNodesSucceeded = nodes.Where(item => item.TaskType != "release-readiness").All(item => item.State == WorkflowNodeState.Succeeded);
            var validArtifacts = await db.EngineeringArtifacts
                .Where(item => item.WorkflowId == workflow.Id && item.ValidationStatus == ArtifactValidationStatus.Valid)
                .Select(item => item.ArtifactType)
                .ToListAsync(cancellationToken);
            var requiredArtifactsExist = RequiredArtifactTypes.All(validArtifacts.Contains);
            var requiredValidationPassed = await db.ValidationResults.AnyAsync(item => item.WorkflowId == workflow.Id && item.ValidationName == "validation" && item.Passed, cancellationToken);
            var releasePassed = requiredNodesSucceeded && requiredArtifactsExist && requiredValidationPassed;
            var releaseValidation = new ValidationResult(workflow.Id, "release-readiness", releasePassed, releasePassed ? "Release readiness evidence is complete." : "Required release evidence is incomplete.", timeProvider.GetUtcNow());
            db.ValidationResults.Add(releaseValidation);
            AddEvent(workflow.Id, "ReleaseReadinessEvaluated", $"passed={releasePassed}", timeProvider.GetUtcNow());
            if (releasePassed)
            {
                artifact!.SetValidationStatus(ArtifactValidationStatus.Valid);
                node.TransitionTo(WorkflowNodeState.Succeeded);
                AddEvent(workflow.Id, "NodeSucceeded", node.Name, timeProvider.GetUtcNow());
                TransitionWorkflow(workflow, WorkflowState.Validating, "WorkflowStateChanged", "Executing -> Validating");
                TransitionWorkflow(workflow, WorkflowState.ReleaseReadiness, "WorkflowStateChanged", "Validating -> ReleaseReadiness");
                TransitionWorkflow(workflow, WorkflowState.Completed, "WorkflowCompleted", "Release readiness passed");
            }
            else
            {
                artifact!.SetValidationStatus(ArtifactValidationStatus.Invalid);
                node.TransitionTo(WorkflowNodeState.Failed);
                TransitionWorkflow(workflow, WorkflowState.Failed, "WorkflowStateChanged", "Release readiness failed");
            }

            return;
        }

        artifact!.SetValidationStatus(ArtifactValidationStatus.Valid);
        var nodeValidation = new ValidationResult(workflow.Id, node.TaskType == "validation" ? "validation" : $"{node.TaskType}-validation", true, "Deterministic validation passed.", timeProvider.GetUtcNow());
        if (node.TaskType == "validation")
        {
            db.ValidationResults.Add(nodeValidation);
        }
        node.TransitionTo(WorkflowNodeState.Succeeded);
        AddEvent(workflow.Id, "NodeSucceeded", node.Name, timeProvider.GetUtcNow());
    }

    private static bool HasStructurallyValidOutput(AgenticSoftwareEngineering.Api.Providers.AgentExecutionResponse response) =>
        response.Succeeded &&
        !string.IsNullOrWhiteSpace(response.Output) &&
        !string.IsNullOrWhiteSpace(response.ArtifactType) &&
        !string.IsNullOrWhiteSpace(response.ContentReference) &&
        !string.IsNullOrWhiteSpace(response.ContentHash);

    private bool RefreshReadyNodes(Guid workflowId, IReadOnlyList<WorkflowNode> nodes, IReadOnlyList<Dependency> dependencies)
    {
        var changed = false;
        foreach (var node in nodes.Where(item => item.State == WorkflowNodeState.Pending))
        {
            var predecessors = dependencies.Where(dependency => dependency.SuccessorNodeId == node.Id).Select(dependency => nodes.Single(candidate => candidate.Id == dependency.PredecessorNodeId));
            if (predecessors.All(predecessor => predecessor.State == WorkflowNodeState.Succeeded))
            {
                node.TransitionTo(WorkflowNodeState.Ready);
                AddEvent(workflowId, "NodeReady", node.Name, timeProvider.GetUtcNow());
                changed = true;
            }
        }

        return changed;
    }

    private void TransitionWorkflow(Workflow workflow, WorkflowState nextState, string eventType, string details)
    {
        workflow.TransitionTo(nextState);
        AddEvent(workflow.Id, eventType, details, timeProvider.GetUtcNow());
    }

    private void AddEvent(Guid workflowId, string eventType, string details, DateTimeOffset occurredAtUtc) =>
        db.WorkflowEvents.Add(new WorkflowEvent(workflowId, eventType, details, occurredAtUtc));

    private Task<int> NextArtifactVersionAsync(Guid workflowId, string artifactType, CancellationToken cancellationToken) =>
        db.EngineeringArtifacts.Where(item => item.WorkflowId == workflowId && item.ArtifactType == artifactType).Select(item => (int?)item.Version).MaxAsync(cancellationToken).ContinueWith(task => (task.Result ?? 0) + 1, cancellationToken);

    private static List<WorkflowNode> CreateNodes(Guid workflowId, DateTimeOffset now) =>
        new()
        {
            new WorkflowNode(workflowId, "Normalize Requirement", "normalize-requirement", now),
            new WorkflowNode(workflowId, "Decompose / Plan", "decompose-plan", now),
            new WorkflowNode(workflowId, "Architecture / Design", "architecture-design", now),
            new WorkflowNode(workflowId, "Implementation Preparation", "implementation-preparation", now),
            new WorkflowNode(workflowId, "Validation", "validation", now),
            new WorkflowNode(workflowId, "Release Readiness", "release-readiness", now)
        };

    private static List<Dependency> CreateDependencies(IReadOnlyList<WorkflowNode> nodes)
    {
        var normalize = nodes.Single(node => node.TaskType == "normalize-requirement").Id;
        var plan = nodes.Single(node => node.TaskType == "decompose-plan").Id;
        var architecture = nodes.Single(node => node.TaskType == "architecture-design").Id;
        var implementation = nodes.Single(node => node.TaskType == "implementation-preparation").Id;
        var validation = nodes.Single(node => node.TaskType == "validation").Id;
        var release = nodes.Single(node => node.TaskType == "release-readiness").Id;
        return new List<Dependency>
        {
            new(normalize, plan),
            new(plan, architecture),
            new(plan, implementation),
            new(architecture, validation),
            new(implementation, validation),
            new(validation, release)
        };
    }
}
