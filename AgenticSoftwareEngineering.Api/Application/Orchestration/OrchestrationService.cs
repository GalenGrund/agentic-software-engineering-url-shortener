using AgenticSoftwareEngineering.Api.Domain.Orchestration;
using AgenticSoftwareEngineering.Api.Infrastructure.Persistence;
using AgenticSoftwareEngineering.Api.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AgenticSoftwareEngineering.Api.Application.Orchestration;

public sealed class OrchestrationService(
    AppDbContext db,
    IAgentProvider provider,
    TimeProvider timeProvider,
    IEnumerable<IAgentProvider>? registeredProviders = null,
    IOptions<OrchestrationOptions>? orchestrationOptions = null) : IOrchestrationService
{
    private int MaxTotalExecutionAttempts => Math.Max(1, orchestrationOptions?.Value.MaxTotalExecutionAttempts ?? 2);
    private bool AllowFallback => orchestrationOptions?.Value.AllowFallback ?? true;
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

    public async Task<WorkflowStatusResponse> CreateGreenfieldAsync(string requirement, CancellationToken cancellationToken, bool requiresHighRiskApproval = false)
    {
        if (string.IsNullOrWhiteSpace(requirement))
        {
            throw new ArgumentException("A requirement is required.", nameof(requirement));
        }

        var now = timeProvider.GetUtcNow();
        var workflow = new Workflow("greenfield-url-shortener", now);
        workflow.TransitionTo(WorkflowState.Planning);
        var plan = new PlanRevision(workflow.Id, 1, now);
        var nodes = CreateNodes(workflow.Id, now, requiresHighRiskApproval);
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

    public async Task<WorkflowStatusResponse> DecideApprovalAsync(Guid workflowId, Guid approvalId, ApprovalDecisionRequest request, CancellationToken cancellationToken)
    {
        var workflow = await db.Workflows.SingleOrDefaultAsync(item => item.Id == workflowId, cancellationToken)
            ?? throw new KeyNotFoundException($"Workflow '{workflowId}' was not found.");
        var approval = await db.Approvals.SingleOrDefaultAsync(item => item.Id == approvalId && item.WorkflowId == workflowId, cancellationToken)
            ?? throw new KeyNotFoundException($"Approval '{approvalId}' was not found.");
        var node = await db.WorkflowNodes.SingleAsync(item => item.Id == approval.WorkflowNodeId, cancellationToken);
        var activePlan = await GetActivePlanRevisionAsync(workflowId, cancellationToken)
            ?? throw new InvalidOperationException("The workflow has no active plan revision.");
        if (approval.WorkflowNodeId != node.Id || node.WorkflowId != workflowId || approval.PlanRevisionId != activePlan.Id)
        {
            db.PolicyEvaluations.Add(new PolicyEvaluation(workflowId, node.Id, "gate3-post-approval-authorization", node.Risk, false, "Approval does not belong to the active workflow plan revision.", timeProvider.GetUtcNow()));
            AddEvent(workflowId, "PostApprovalAuthorizationDenied", node.Name, timeProvider.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException("Approval is not valid for the active workflow plan revision.");
        }
        var decision = request.Approved ? ApprovalDecision.Approved : ApprovalDecision.Rejected;
        approval.Decide(decision, request.Rationale, timeProvider.GetUtcNow());
        AddEvent(workflowId, request.Approved ? "ApprovalApproved" : "ApprovalRejected", node.Name, timeProvider.GetUtcNow());
        if (request.Approved)
        {
            db.PolicyEvaluations.Add(new PolicyEvaluation(workflowId, node.Id, "gate3-post-approval-authorization", node.Risk, true, $"Approved by approval {approval.Id} for plan revision {activePlan.Id}.", timeProvider.GetUtcNow()));
            AddEvent(workflowId, "PostApprovalAuthorizationAllowed", $"{node.Name}:plan-revision={activePlan.Id}", timeProvider.GetUtcNow());
            node.TransitionTo(WorkflowNodeState.Executing);
            TransitionWorkflow(workflow, WorkflowState.Executing, "WorkflowStateChanged", "WaitingForApproval -> Executing");
            await db.SaveChangesAsync(cancellationToken);
            var dependencies = await db.Dependencies.Where(item => item.SuccessorNodeId == node.Id).ToListAsync(cancellationToken);
            var nodes = await db.WorkflowNodes.Where(item => item.WorkflowId == workflowId).ToListAsync(cancellationToken);
            await ExecuteNodeAsync(workflow, node, nodes, dependencies, cancellationToken, alreadyExecuting: true);
        }
        else
        {
            node.TransitionTo(WorkflowNodeState.Blocked);
            TransitionWorkflow(workflow, WorkflowState.SafeStopped, "SafeStopped", "High-risk approval rejected");
            AddEvent(workflowId, "HumanEscalationRequired", "Approval rejected; authorized recovery is required.", timeProvider.GetUtcNow());
        }
        await db.SaveChangesAsync(cancellationToken);
        return await GetStatusAsync(workflowId, cancellationToken);
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

        RefreshReadyNodes(workflowId, nodes, dependencies, includeRetryScheduled: false);
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
        var policies = await db.PolicyEvaluations.AsNoTracking().Where(item => item.WorkflowId == workflowId).ToListAsync(cancellationToken);
        var approvals = await db.Approvals.AsNoTracking().Where(item => item.WorkflowId == workflowId).ToListAsync(cancellationToken);
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
            policies.Select(item => new PolicyStatusResponse(item.Id, item.WorkflowNodeId, item.PolicyName, item.Risk, item.Allowed, item.Reason, item.EvaluatedAtUtc)).ToList(),
            approvals.Select(item => new ApprovalStatusResponse(item.Id, item.WorkflowNodeId, item.PlanRevisionId, item.Action, item.Risk, item.Decision, item.ApproverRole, item.Rationale, item.RequestedAtUtc, item.DecidedAtUtc)).ToList(),
            events.Select(item => new EventStatusResponse(item.Id, item.EventType, item.Details, item.OccurredAtUtc)).ToList());
    }

    private async Task ExecuteNodeAsync(Workflow workflow, WorkflowNode node, IReadOnlyList<WorkflowNode> nodes, IReadOnlyList<Dependency> dependencies, CancellationToken cancellationToken, bool alreadyExecuting = false)
    {
        if (!alreadyExecuting)
        {
            await ApplyPolicyGateAsync(workflow, node, cancellationToken);
            if (workflow.State != WorkflowState.Executing)
            {
                return;
            }
            node.TransitionTo(WorkflowNodeState.Executing);
        }
        AddEvent(workflow.Id, "NodeExecutionStarted", node.Name, timeProvider.GetUtcNow());
        var startedAt = timeProvider.GetUtcNow();
        var attempt = await db.AgentExecutions.CountAsync(item => item.WorkflowNodeId == node.Id, cancellationToken) + 1;
        var execution = new AgentExecution(node.Id, provider.Name, attempt, startedAt);
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
            if (response.FailureClassification == FailureClassification.Transient && attempt < MaxTotalExecutionAttempts)
            {
                node.TransitionTo(WorkflowNodeState.RetryScheduled);
                AddEvent(workflow.Id, "RetryScheduled", $"{node.Name}:attempt={attempt}", timeProvider.GetUtcNow());
                return;
            }

            if (response.FailureClassification == FailureClassification.PolicyBlocked)
            {
                node.TransitionTo(WorkflowNodeState.Failed);
                TransitionWorkflow(workflow, WorkflowState.SafeStopped, "SafeStopped", $"{node.Name}:{response.FailureClassification}");
                AddEvent(workflow.Id, "HumanEscalationRequired", $"{node.Name}:attempts={attempt};provider={provider.Name}", timeProvider.GetUtcNow());
                return;
            }

            var fallback = GetCompatibleFallbackProvider();
            var fallbackAllowed = AllowFallback && fallback is not null;
            db.PolicyEvaluations.Add(new PolicyEvaluation(workflow.Id, node.Id, "gate3-fallback-policy", node.Risk, fallbackAllowed, fallbackAllowed ? $"Registered compatible fallback '{fallback!.Name}' is permitted." : GetFallbackDenialReason(fallback), timeProvider.GetUtcNow()));
            AddEvent(workflow.Id, "FallbackPolicyEvaluated", $"{node.Name}:allowed={fallbackAllowed}", timeProvider.GetUtcNow());
            if (fallbackAllowed)
            {
                AddEvent(workflow.Id, "FallbackSelected", $"{node.Name}:{provider.Name}->{fallback!.Name}", timeProvider.GetUtcNow());
                await ExecuteWithProviderAsync(workflow, node, nodes, dependencies, fallback, cancellationToken, attempt + 1);
                return;
            }

            node.TransitionTo(WorkflowNodeState.Failed);
            TransitionWorkflow(workflow, WorkflowState.SafeStopped, "SafeStopped", $"{node.Name}:{response.FailureClassification}");
            AddEvent(workflow.Id, "HumanEscalationRequired", $"{node.Name}:attempts={attempt};provider={provider.Name}", timeProvider.GetUtcNow());
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

    private async Task ExecuteWithProviderAsync(Workflow workflow, WorkflowNode node, IReadOnlyList<WorkflowNode> nodes, IReadOnlyList<Dependency> dependencies, IAgentProvider selectedProvider, CancellationToken cancellationToken, int attempt)
    {
        var execution = new AgentExecution(node.Id, selectedProvider.Name, attempt, timeProvider.GetUtcNow());
        db.AgentExecutions.Add(execution);
        var predecessorNodeIds = dependencies.Where(item => item.SuccessorNodeId == node.Id).Select(item => item.PredecessorNodeId).ToArray();
        var upstreamReferences = await db.EngineeringArtifacts.Where(item => item.WorkflowId == workflow.Id && item.ProducerNodeId.HasValue && predecessorNodeIds.Contains(item.ProducerNodeId.Value)).Select(item => item.ContentReference).ToListAsync(cancellationToken);
        var response = await selectedProvider.ExecuteAsync(new AgentExecutionRequest(workflow.Id, node.Id, node.TaskType, TaskInstructions[node.TaskType], upstreamReferences, attempt), cancellationToken);
        execution.Complete(response.Succeeded ? AgentExecutionStatus.Succeeded : AgentExecutionStatus.Failed, timeProvider.GetUtcNow(), response.Output);
        AddEvent(workflow.Id, response.Succeeded ? "ProviderExecutionCompleted" : "ProviderExecutionFailed", $"{node.Name}:{selectedProvider.Name}", timeProvider.GetUtcNow());
        if (!response.Succeeded)
        {
            node.TransitionTo(WorkflowNodeState.Failed);
            TransitionWorkflow(workflow, WorkflowState.SafeStopped, "SafeStopped", $"fallback:{node.Name}:{response.FailureClassification}");
            AddEvent(workflow.Id, "HumanEscalationRequired", $"{node.Name}:providers={provider.Name},{selectedProvider.Name}", timeProvider.GetUtcNow());
            return;
        }
        node.TransitionTo(WorkflowNodeState.Validating);
        var valid = HasStructurallyValidOutput(response);
        var validation = new ValidationResult(workflow.Id, $"{node.TaskType}-fallback-exit-gate", valid, valid ? "Fallback output passed." : "Fallback output failed.", timeProvider.GetUtcNow());
        db.ValidationResults.Add(validation);
        if (!valid)
        {
            node.TransitionTo(WorkflowNodeState.Failed);
            TransitionWorkflow(workflow, WorkflowState.SafeStopped, "SafeStopped", $"fallback-exit-gate:{node.Name}");
            AddEvent(workflow.Id, "HumanEscalationRequired", $"{node.Name}:fallback-exit-gate", timeProvider.GetUtcNow());
            return;
        }
        var artifact = EngineeringArtifact.Create(workflow.Id, response.ArtifactType, await NextArtifactVersionAsync(workflow.Id, response.ArtifactType, cancellationToken), response.ContentReference, response.ContentHash, node.Id, execution.Id, null, timeProvider.GetUtcNow());
        artifact.SetValidationStatus(ArtifactValidationStatus.Valid);
        db.EngineeringArtifacts.Add(artifact);
        node.TransitionTo(WorkflowNodeState.Succeeded);
        AddEvent(workflow.Id, "NodeSucceeded", node.Name, timeProvider.GetUtcNow());
    }

    private IAgentProvider? GetCompatibleFallbackProvider()
    {
        var providers = registeredProviders?.ToList() ?? new List<IAgentProvider> { provider };
        return providers.FirstOrDefault(candidate => !ReferenceEquals(candidate, provider) && provider.CompatibleFallbackProviders.Contains(candidate.Name));
    }

    private string GetFallbackDenialReason(IAgentProvider? fallback) =>
        !AllowFallback ? "Fallback policy is disabled by orchestration configuration." : fallback is null ? "No registered compatible fallback provider is available." : "Fallback policy denied execution.";

    private async Task ApplyPolicyGateAsync(Workflow workflow, WorkflowNode node, CancellationToken cancellationToken)
    {
        var allowed = node.Risk != RiskLevel.High;
        var policy = new PolicyEvaluation(workflow.Id, node.Id, "gate3-risk-policy", node.Risk, allowed, allowed ? "Risk permitted." : "High-risk action requires approval.", timeProvider.GetUtcNow());
        db.PolicyEvaluations.Add(policy);
        AddEvent(workflow.Id, "PolicyEvaluated", $"{node.Name}:{node.Risk}:allowed={allowed}", timeProvider.GetUtcNow());
        if (allowed) return;
        var approval = await db.Approvals.SingleOrDefaultAsync(item => item.WorkflowNodeId == node.Id && item.Decision == ApprovalDecision.Approved, cancellationToken);
        if (approval is null)
        {
            var activePlan = await GetActivePlanRevisionAsync(workflow.Id, cancellationToken)
                ?? throw new InvalidOperationException("The workflow has no active plan revision.");
            var requested = new Approval(workflow.Id, node.Id, activePlan.Id, node.Name, node.Risk, "human-reviewer", timeProvider.GetUtcNow());
            db.Approvals.Add(requested);
            node.TransitionTo(WorkflowNodeState.Executing);
            node.TransitionTo(WorkflowNodeState.WaitingForApproval);
            TransitionWorkflow(workflow, WorkflowState.WaitingForApproval, "ApprovalRequested", node.Name);
        }
    }

    private static bool HasStructurallyValidOutput(AgenticSoftwareEngineering.Api.Providers.AgentExecutionResponse response) =>
        response.Succeeded &&
        !string.IsNullOrWhiteSpace(response.Output) &&
        !string.IsNullOrWhiteSpace(response.ArtifactType) &&
        !string.IsNullOrWhiteSpace(response.ContentReference) &&
        !string.IsNullOrWhiteSpace(response.ContentHash);

    private bool RefreshReadyNodes(Guid workflowId, IReadOnlyList<WorkflowNode> nodes, IReadOnlyList<Dependency> dependencies, bool includeRetryScheduled = true)
    {
        var changed = false;
        foreach (var node in nodes.Where(item => item.State == WorkflowNodeState.Pending || (includeRetryScheduled && item.State == WorkflowNodeState.RetryScheduled)))
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

    private Task<PlanRevision?> GetActivePlanRevisionAsync(Guid workflowId, CancellationToken cancellationToken) =>
        db.PlanRevisions.Where(item => item.WorkflowId == workflowId).OrderByDescending(item => item.Revision).FirstOrDefaultAsync(cancellationToken);

    private static List<WorkflowNode> CreateNodes(Guid workflowId, DateTimeOffset now, bool requiresHighRiskApproval) =>
        new()
        {
            new WorkflowNode(workflowId, "Normalize Requirement", "normalize-requirement", now),
            new WorkflowNode(workflowId, "Decompose / Plan", "decompose-plan", now),
            new WorkflowNode(workflowId, "Architecture / Design", "architecture-design", now),
            new WorkflowNode(workflowId, "Implementation Preparation", "implementation-preparation", now, requiresHighRiskApproval ? RiskLevel.High : RiskLevel.Medium),
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
