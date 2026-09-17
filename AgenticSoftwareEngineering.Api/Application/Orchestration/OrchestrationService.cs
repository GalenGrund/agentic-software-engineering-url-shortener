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
        var planRevisions = await db.PlanRevisions.AsNoTracking().Where(item => item.WorkflowId == workflowId).OrderBy(item => item.Revision).ToListAsync(cancellationToken);
        var artifactDependencies = await db.ArtifactDependencies.AsNoTracking().Where(item => artifacts.Select(artifact => artifact.Id).Contains(item.DependentArtifactId)).ToListAsync(cancellationToken);

        return new WorkflowStatusResponse(
            workflow.Id,
            workflow.Name,
            workflow.State,
            nodes.Select(node => new NodeStatusResponse(node.Id, node.Name, node.TaskType, node.State)).ToList(),
            dependencies.Select(item => new DependencyStatusResponse(item.PredecessorNodeId, item.SuccessorNodeId)).ToList(),
            executions.Select(item => new AgenticSoftwareEngineering.Api.Application.Orchestration.AgentExecutionResponse(item.Id, item.WorkflowNodeId, item.ProviderName, item.Attempt, item.Status, item.StartedAtUtc, item.CompletedAtUtc)).ToList(),
            artifacts.Select(item => new ArtifactStatusResponse(item.Id, item.ArtifactType, item.Version, item.ContentReference, item.ContentHash, item.ProducerNodeId, item.ProducerExecutionId, item.SupersedesArtifactId, item.ValidationStatus)).ToList(),
            validations.Select(item => new ValidationStatusResponse(item.Id, item.ValidationName, item.Passed, item.Details, item.ValidatedAtUtc)).ToList(),
            policies.Select(item => new PolicyStatusResponse(item.Id, item.WorkflowNodeId, item.PolicyName, item.Risk, item.Allowed, item.Reason, item.EvaluatedAtUtc)).ToList(),
            approvals.Select(item => new ApprovalStatusResponse(item.Id, item.WorkflowNodeId, item.PlanRevisionId, item.Action, item.Risk, item.Decision, item.ApproverRole, item.Rationale, item.RequestedAtUtc, item.DecidedAtUtc)).ToList(),
            events.Select(item => new EventStatusResponse(item.Id, item.EventType, item.Details, item.OccurredAtUtc)).ToList(),
            planRevisions.Select(item => new PlanRevisionStatusResponse(item.Id, item.Revision, item.SupersedesRevisionId, item.CreatedAtUtc)).ToList(),
            artifactDependencies.Select(item => new ArtifactDependencyStatusResponse(item.Id, item.ArtifactId, item.DependentArtifactId)).ToList());
    }

    public async Task<WorkflowStatusResponse> ReviseArtifactAsync(Guid workflowId, Guid artifactId, BrownfieldArtifactRevisionRequest request, CancellationToken cancellationToken)
    {
        var workflow = await GetWorkflowAsync(workflowId, cancellationToken);
        var prior = await db.EngineeringArtifacts.SingleOrDefaultAsync(item => item.Id == artifactId && item.WorkflowId == workflowId, cancellationToken)
            ?? throw new KeyNotFoundException($"Artifact '{artifactId}' was not found.");
        var nodes = await db.WorkflowNodes.Where(item => item.WorkflowId == workflowId).ToListAsync(cancellationToken);
        var dependencies = await GetDependenciesAsync(nodes, cancellationToken);
        var impactedArtifactIds = await GetImpactedArtifactIdsAsync(workflowId, prior.Id, cancellationToken);
        var impactedNodeIds = await GetImpactedNodeIdsAsync(workflowId, impactedArtifactIds, prior.ProducerNodeId, cancellationToken);
        var activePlan = await GetActivePlanRevisionAsync(workflowId, cancellationToken)
            ?? throw new InvalidOperationException("The workflow has no active plan revision.");
        var nextPlan = new PlanRevision(workflowId, activePlan.Revision + 1, timeProvider.GetUtcNow(), activePlan.Id);
        var revised = EngineeringArtifact.Revise(prior, request.ContentReference, request.ContentHash, timeProvider.GetUtcNow());
        revised.SetValidationStatus(ArtifactValidationStatus.Valid);
        db.EngineeringArtifacts.Add(revised);
        await CopyArtifactDependenciesAsync(prior.Id, revised.Id, workflowId, cancellationToken);
        db.PlanRevisions.Add(nextPlan);
        if (request.RequiresHighRiskApproval && prior.ProducerNodeId is Guid revisedNodeId)
        {
            nodes.Single(node => node.Id == revisedNodeId).SetRisk(RiskLevel.High);
        }
        TransitionWorkflow(workflow, WorkflowState.Replanning, "ReplanningStarted", $"artifact={prior.Id};revision={revised.Id}");
        AddEvent(workflowId, "ArtifactSuperseded", $"old={prior.Id};new={revised.Id};version={revised.Version}", timeProvider.GetUtcNow());
        AddEvent(workflowId, "PlanRevisionCreated", $"revision={nextPlan.Revision};supersedes={activePlan.Id}", timeProvider.GetUtcNow());
        InvalidateImpactedNodes(nodes, impactedNodeIds, workflowId, "brownfield-artifact-revision");
        AddEvent(workflowId, "ImpactAnalysisCompleted", $"artifact={prior.Id};impacted-artifacts={impactedArtifactIds.Count};impacted-nodes={impactedNodeIds.Count}", timeProvider.GetUtcNow());
        TransitionWorkflow(workflow, WorkflowState.Planning, "ReplanningCompleted", $"preserved-nodes={nodes.Count(node => node.State == WorkflowNodeState.Succeeded && !impactedNodeIds.Contains(node.Id))}");
        RefreshReadyNodes(workflowId, nodes, dependencies);
        await db.SaveChangesAsync(cancellationToken);
        return await GetStatusAsync(workflowId, cancellationToken);
    }

    public async Task<WorkflowStatusResponse> RollbackAsync(Guid workflowId, WorkflowRollbackRequest request, CancellationToken cancellationToken)
    {
        var workflow = await GetWorkflowAsync(workflowId, cancellationToken);
        var target = await db.EngineeringArtifacts.SingleOrDefaultAsync(item => item.Id == request.ArtifactId && item.WorkflowId == workflowId, cancellationToken)
            ?? throw new KeyNotFoundException($"Rollback artifact '{request.ArtifactId}' was not found.");
        var current = await db.EngineeringArtifacts.Where(item => item.WorkflowId == workflowId && item.ArtifactType == target.ArtifactType).OrderByDescending(item => item.Version).FirstAsync(cancellationToken);
        var nodes = await db.WorkflowNodes.Where(item => item.WorkflowId == workflowId).ToListAsync(cancellationToken);
        var dependencies = await GetDependenciesAsync(nodes, cancellationToken);
        var impactedArtifactIds = await GetImpactedArtifactIdsAsync(workflowId, current.Id, cancellationToken);
        var impactedNodeIds = await GetImpactedNodeIdsAsync(workflowId, impactedArtifactIds, current.ProducerNodeId, cancellationToken);
        var activePlan = await GetActivePlanRevisionAsync(workflowId, cancellationToken)
            ?? throw new InvalidOperationException("The workflow has no active plan revision.");
        var nextPlan = new PlanRevision(workflowId, activePlan.Revision + 1, timeProvider.GetUtcNow(), activePlan.Id);
        var rollbackArtifact = EngineeringArtifact.Create(workflowId, target.ArtifactType, current.Version + 1, target.ContentReference, target.ContentHash, target.ProducerNodeId, target.ProducerExecutionId, current.Id, timeProvider.GetUtcNow());
        rollbackArtifact.SetValidationStatus(ArtifactValidationStatus.Valid);
        db.EngineeringArtifacts.Add(rollbackArtifact);
        await CopyArtifactDependenciesAsync(target.Id, rollbackArtifact.Id, workflowId, cancellationToken);
        db.PlanRevisions.Add(nextPlan);
        TransitionWorkflow(workflow, WorkflowState.RollingBack, "RollbackStarted", $"target-artifact={target.Id};current-artifact={current.Id}");
        AddEvent(workflowId, "RollbackArtifactSelected", $"target={target.Id};new={rollbackArtifact.Id}", timeProvider.GetUtcNow());
        InvalidateImpactedNodes(nodes, impactedNodeIds, workflowId, "workflow-artifact-rollback");
        AddEvent(workflowId, "ImpactAnalysisCompleted", $"rollback-source={current.Id};impacted-artifacts={impactedArtifactIds.Count};impacted-nodes={impactedNodeIds.Count}", timeProvider.GetUtcNow());
        TransitionWorkflow(workflow, WorkflowState.Replanning, "RollbackCompleted", $"plan-revision={nextPlan.Revision}");
        TransitionWorkflow(workflow, WorkflowState.Planning, "ReplanningCompleted", $"preserved-nodes={nodes.Count(node => node.State == WorkflowNodeState.Succeeded && !impactedNodeIds.Contains(node.Id))}");
        RefreshReadyNodes(workflowId, nodes, dependencies);
        await db.SaveChangesAsync(cancellationToken);
        return await GetStatusAsync(workflowId, cancellationToken);
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
        var upstreamArtifacts = await GetEffectiveUpstreamArtifactsAsync(workflow.Id, predecessorNodeIds, cancellationToken);
        var upstreamReferences = upstreamArtifacts.Select(artifact => artifact.ContentReference).ToList();
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
            var previousArtifact = await GetLatestArtifactAsync(workflow.Id, response.ArtifactType, cancellationToken);
            artifact = EngineeringArtifact.Create(workflow.Id, response.ArtifactType, previousArtifact is null ? 1 : previousArtifact.Version + 1, response.ContentReference, response.ContentHash, node.Id, execution.Id, previousArtifact?.Id, timeProvider.GetUtcNow());
            db.EngineeringArtifacts.Add(artifact);
            await AddArtifactDependenciesAsync(workflow.Id, artifact, upstreamArtifacts, cancellationToken);
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
            var effectiveArtifacts = await GetEffectiveArtifactsAsync(workflow.Id, cancellationToken);
            var requiredArtifactsExist = RequiredArtifactTypes.All(requiredType => effectiveArtifacts.Any(artifact => artifact.ArtifactType == requiredType && artifact.ValidationStatus == ArtifactValidationStatus.Valid));
            var validationNode = nodes.Single(item => item.TaskType == "validation");
            var latestValidationExecution = (await db.AgentExecutions.Where(item => item.WorkflowNodeId == validationNode.Id).ToListAsync(cancellationToken))
                .OrderByDescending(item => item.StartedAtUtc)
                .ThenByDescending(item => item.Id)
                .FirstOrDefault();
            var validationEvidence = await db.ValidationResults
                .Where(item => item.WorkflowId == workflow.Id && item.ValidationName == "validation")
                .ToListAsync(cancellationToken);
            var requiredValidationPassed = latestValidationExecution is not null && latestValidationExecution.Status == AgentExecutionStatus.Succeeded &&
                validationEvidence.Any(item => item.Passed && item.ValidatedAtUtc >= latestValidationExecution.StartedAtUtc);
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
        var upstreamArtifacts = await GetEffectiveUpstreamArtifactsAsync(workflow.Id, predecessorNodeIds, cancellationToken);
        var upstreamReferences = upstreamArtifacts.Select(artifact => artifact.ContentReference).ToList();
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
        var previousArtifact = await GetLatestArtifactAsync(workflow.Id, response.ArtifactType, cancellationToken);
        var artifact = EngineeringArtifact.Create(workflow.Id, response.ArtifactType, previousArtifact is null ? 1 : previousArtifact.Version + 1, response.ContentReference, response.ContentHash, node.Id, execution.Id, previousArtifact?.Id, timeProvider.GetUtcNow());
        artifact.SetValidationStatus(ArtifactValidationStatus.Valid);
        db.EngineeringArtifacts.Add(artifact);
        await AddArtifactDependenciesAsync(workflow.Id, artifact, upstreamArtifacts, cancellationToken);
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
        var activePlan = await GetActivePlanRevisionAsync(workflow.Id, cancellationToken)
            ?? throw new InvalidOperationException("The workflow has no active plan revision.");
        var approval = await db.Approvals.SingleOrDefaultAsync(item => item.WorkflowNodeId == node.Id && item.PlanRevisionId == activePlan.Id && item.Decision == ApprovalDecision.Approved, cancellationToken);
        if (approval is null)
        {
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
        foreach (var node in nodes.Where(item => item.State == WorkflowNodeState.Pending || item.State == WorkflowNodeState.Invalidated || (includeRetryScheduled && item.State == WorkflowNodeState.RetryScheduled)))
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

    private Task<EngineeringArtifact?> GetLatestArtifactAsync(Guid workflowId, string artifactType, CancellationToken cancellationToken) =>
        db.EngineeringArtifacts.Where(item => item.WorkflowId == workflowId && item.ArtifactType == artifactType).OrderByDescending(item => item.Version).FirstOrDefaultAsync(cancellationToken);

    private Task<PlanRevision?> GetActivePlanRevisionAsync(Guid workflowId, CancellationToken cancellationToken) =>
        db.PlanRevisions.Where(item => item.WorkflowId == workflowId).OrderByDescending(item => item.Revision).FirstOrDefaultAsync(cancellationToken);

    private Task<Workflow> GetWorkflowAsync(Guid workflowId, CancellationToken cancellationToken) =>
        db.Workflows.SingleOrDefaultAsync(item => item.Id == workflowId, cancellationToken)
            .ContinueWith(task => task.Result ?? throw new KeyNotFoundException($"Workflow '{workflowId}' was not found."), cancellationToken);

    private Task<List<Dependency>> GetDependenciesAsync(IReadOnlyList<WorkflowNode> nodes, CancellationToken cancellationToken) =>
        db.Dependencies.Where(item => nodes.Select(node => node.Id).Contains(item.SuccessorNodeId)).ToListAsync(cancellationToken);

    private async Task<HashSet<Guid>> GetImpactedArtifactIdsAsync(Guid workflowId, Guid rootArtifactId, CancellationToken cancellationToken)
    {
        var workflowArtifactIds = await db.EngineeringArtifacts.AsNoTracking()
            .Where(item => item.WorkflowId == workflowId)
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);
        var edges = await db.ArtifactDependencies.AsNoTracking()
            .Where(item => workflowArtifactIds.Contains(item.ArtifactId) && workflowArtifactIds.Contains(item.DependentArtifactId))
            .OrderBy(item => item.ArtifactId)
            .ThenBy(item => item.DependentArtifactId)
            .ToListAsync(cancellationToken);
        var impacted = new HashSet<Guid> { rootArtifactId };
        var queue = new Queue<Guid>(new[] { rootArtifactId });
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var edge in edges.Where(item => item.ArtifactId == current).OrderBy(item => item.DependentArtifactId))
            {
                if (impacted.Add(edge.DependentArtifactId)) queue.Enqueue(edge.DependentArtifactId);
            }
        }

        return impacted;
    }

    private async Task<HashSet<Guid>> GetImpactedNodeIdsAsync(Guid workflowId, IReadOnlySet<Guid> artifactIds, Guid? rootProducerNodeId, CancellationToken cancellationToken)
    {
        var producerNodeIds = await db.EngineeringArtifacts.AsNoTracking()
            .Where(item => item.WorkflowId == workflowId && artifactIds.Contains(item.Id) && item.ProducerNodeId.HasValue)
            .Select(item => item.ProducerNodeId!.Value)
            .ToListAsync(cancellationToken);
        if (rootProducerNodeId.HasValue) producerNodeIds.Add(rootProducerNodeId.Value);
        return producerNodeIds.ToHashSet();
    }

    private void InvalidateImpactedNodes(IReadOnlyList<WorkflowNode> nodes, IReadOnlySet<Guid> impactedNodeIds, Guid workflowId, string reason)
    {
        foreach (var node in nodes.Where(item => impactedNodeIds.Contains(item.Id)).OrderBy(item => item.Name))
        {
            if (node.State == WorkflowNodeState.Succeeded)
            {
                node.TransitionTo(WorkflowNodeState.Invalidated);
                AddEvent(workflowId, "NodeInvalidated", $"node={node.Id};reason={reason}", timeProvider.GetUtcNow());
            }
            else if (node.State is WorkflowNodeState.Pending or WorkflowNodeState.Ready)
            {
                AddEvent(workflowId, "NodeImpactIdentified", $"node={node.Id};state={node.State};reason={reason}", timeProvider.GetUtcNow());
            }
        }
    }

    private async Task<List<EngineeringArtifact>> GetEffectiveUpstreamArtifactsAsync(Guid workflowId, IReadOnlyCollection<Guid> predecessorNodeIds, CancellationToken cancellationToken)
    {
        var candidates = await db.EngineeringArtifacts
            .Where(item => item.WorkflowId == workflowId && item.ProducerNodeId.HasValue && predecessorNodeIds.Contains(item.ProducerNodeId.Value))
            .OrderBy(item => item.ProducerNodeId)
            .ThenBy(item => item.ArtifactType)
            .ThenByDescending(item => item.Version)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);
        return candidates
            .GroupBy(item => new { ProducerNodeId = item.ProducerNodeId!.Value, item.ArtifactType })
            .Select(group => group.First())
            .OrderBy(item => item.ProducerNodeId)
            .ThenBy(item => item.ArtifactType)
            .ToList();
    }

    private async Task<List<EngineeringArtifact>> GetEffectiveArtifactsAsync(Guid workflowId, CancellationToken cancellationToken)
    {
        var candidates = await db.EngineeringArtifacts
            .Where(item => item.WorkflowId == workflowId)
            .OrderBy(item => item.ArtifactType)
            .ThenByDescending(item => item.Version)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);
        return candidates
            .GroupBy(item => new { item.ProducerNodeId, item.ArtifactType })
            .Select(group => group.First())
            .OrderBy(item => item.ArtifactType)
            .ThenBy(item => item.ProducerNodeId)
            .ToList();
    }

    private async Task AddArtifactDependenciesAsync(Guid workflowId, EngineeringArtifact artifact, IReadOnlyList<EngineeringArtifact> upstreamArtifacts, CancellationToken cancellationToken)
    {
        foreach (var predecessor in upstreamArtifacts.Where(item => item.WorkflowId == workflowId && item.Id != artifact.Id).OrderBy(item => item.Id))
        {
            if (!await db.ArtifactDependencies.AnyAsync(item => item.ArtifactId == predecessor.Id && item.DependentArtifactId == artifact.Id, cancellationToken))
            {
                db.ArtifactDependencies.Add(new ArtifactDependency(predecessor.Id, artifact.Id));
            }
        }
    }

    private async Task CopyArtifactDependenciesAsync(Guid priorArtifactId, Guid revisedArtifactId, Guid workflowId, CancellationToken cancellationToken)
    {
        var incoming = await db.ArtifactDependencies.Where(item => item.DependentArtifactId == priorArtifactId).ToListAsync(cancellationToken);
        foreach (var edge in incoming)
        {
            var predecessor = await db.EngineeringArtifacts.SingleAsync(item => item.Id == edge.ArtifactId, cancellationToken);
            if (predecessor.WorkflowId != workflowId) throw new InvalidOperationException("Artifact dependencies cannot cross workflows.");
            db.ArtifactDependencies.Add(new ArtifactDependency(predecessor.Id, revisedArtifactId));
        }
    }

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
