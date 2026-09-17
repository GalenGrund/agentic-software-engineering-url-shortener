namespace AgenticSoftwareEngineering.Api.Domain.Orchestration;

public sealed class PlanRevision
{
    private PlanRevision()
    {
    }

    public PlanRevision(Guid workflowId, int revision, DateTimeOffset createdAtUtc, Guid? supersedesRevisionId = null)
    {
        if (workflowId == Guid.Empty) throw new ArgumentException("A workflow id is required.", nameof(workflowId));
        if (revision < 1) throw new ArgumentOutOfRangeException(nameof(revision));
        if (supersedesRevisionId == Guid.Empty) throw new ArgumentException("A supersession identity must be a non-empty id.", nameof(supersedesRevisionId));
        Id = Guid.NewGuid();
        WorkflowId = workflowId;
        Revision = revision;
        CreatedAtUtc = createdAtUtc;
        SupersedesRevisionId = supersedesRevisionId;
    }

    public Guid Id { get; private set; }
    public Guid WorkflowId { get; private set; }
    public int Revision { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public Guid? SupersedesRevisionId { get; private set; }
}

public sealed class Dependency
{
    private Dependency()
    {
    }

    public Dependency(Guid predecessorNodeId, Guid successorNodeId)
    {
        if (predecessorNodeId == Guid.Empty) throw new ArgumentException("A predecessor node id is required.", nameof(predecessorNodeId));
        if (successorNodeId == Guid.Empty) throw new ArgumentException("A successor node id is required.", nameof(successorNodeId));
        if (predecessorNodeId == successorNodeId)
        {
            throw new ArgumentException("A node cannot depend on itself.");
        }

        Id = Guid.NewGuid();
        PredecessorNodeId = predecessorNodeId;
        SuccessorNodeId = successorNodeId;
    }

    public Guid Id { get; private set; }
    public Guid PredecessorNodeId { get; private set; }
    public Guid SuccessorNodeId { get; private set; }
}

public sealed class ArtifactDependency
{
    private ArtifactDependency()
    {
    }

    public ArtifactDependency(Guid artifactId, Guid dependentArtifactId)
    {
        if (artifactId == Guid.Empty) throw new ArgumentException("An artifact id is required.", nameof(artifactId));
        if (dependentArtifactId == Guid.Empty) throw new ArgumentException("A dependent artifact id is required.", nameof(dependentArtifactId));
        if (artifactId == dependentArtifactId)
        {
            throw new ArgumentException("An artifact cannot depend on itself.");
        }

        Id = Guid.NewGuid();
        ArtifactId = artifactId;
        DependentArtifactId = dependentArtifactId;
    }

    public Guid Id { get; private set; }
    public Guid ArtifactId { get; private set; }
    public Guid DependentArtifactId { get; private set; }
}

public sealed class WorkflowEvent
{
    private WorkflowEvent()
    {
    }

    public WorkflowEvent(Guid workflowId, string eventType, string details, DateTimeOffset occurredAtUtc)
    {
        if (workflowId == Guid.Empty) throw new ArgumentException("A workflow id is required.", nameof(workflowId));
        if (string.IsNullOrWhiteSpace(eventType)) throw new ArgumentException("An event type is required.", nameof(eventType));
        if (string.IsNullOrWhiteSpace(details)) throw new ArgumentException("Event details are required.", nameof(details));
        Id = Guid.NewGuid();
        WorkflowId = workflowId;
        EventType = eventType;
        Details = details;
        OccurredAtUtc = occurredAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid WorkflowId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string Details { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; private set; }
}

public enum RiskLevel
{
    Low = 1,
    Medium = 2,
    High = 3
}

public enum FailureClassification
{
    Transient = 1,
    Permanent = 2,
    PolicyBlocked = 3
}

public enum ActionProposalState
{
    Proposed = 1,
    Authorized = 2,
    Rejected = 3,
    Applied = 4
}

public enum ChangeSetAuthorizationMechanism
{
    PolicyAuthorized = 1,
    HumanApproved = 2
}

public enum PrivilegedOperationType
{
    ImplementationPreparation = 1
}

public sealed class ActionProposal
{
    private ActionProposal()
    {
    }

    public ActionProposal(Guid workflowId, Guid workflowNodeId, Guid planRevisionId, string action, RiskLevel risk, string providerName, Guid proposalExecutionId, DateTimeOffset proposedAtUtc)
    {
        if (workflowId == Guid.Empty) throw new ArgumentException("A workflow id is required.", nameof(workflowId));
        if (workflowNodeId == Guid.Empty) throw new ArgumentException("A workflow node id is required.", nameof(workflowNodeId));
        if (planRevisionId == Guid.Empty) throw new ArgumentException("A plan revision id is required.", nameof(planRevisionId));
        if (proposalExecutionId == Guid.Empty) throw new ArgumentException("A proposal execution id is required.", nameof(proposalExecutionId));
        if (string.IsNullOrWhiteSpace(providerName)) throw new ArgumentException("A provider name is required.", nameof(providerName));
        if (string.IsNullOrWhiteSpace(action)) throw new ArgumentException("An action proposal is required.", nameof(action));
        Id = Guid.NewGuid();
        WorkflowId = workflowId;
        WorkflowNodeId = workflowNodeId;
        PlanRevisionId = planRevisionId;
        Action = action;
        Risk = risk;
        ProviderName = providerName;
        ProposalExecutionId = proposalExecutionId;
        ProposedAtUtc = proposedAtUtc;
        State = ActionProposalState.Proposed;
    }

    public Guid Id { get; private set; }
    public Guid WorkflowId { get; private set; }
    public Guid WorkflowNodeId { get; private set; }
    public Guid PlanRevisionId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public RiskLevel Risk { get; private set; }
    public string ProviderName { get; private set; } = string.Empty;
    public Guid ProposalExecutionId { get; private set; }
    public Guid? ChangeSetId { get; private set; }
    public Guid? AppliedExecutionId { get; private set; }
    public ActionProposalState State { get; private set; }
    public DateTimeOffset ProposedAtUtc { get; private set; }
    public DateTimeOffset? DecidedAtUtc { get; private set; }

    public void Authorize(Approval approval, ChangeSet changeSet, DateTimeOffset authorizedAtUtc)
    {
        if (ChangeSetId is null || changeSet.Id != ChangeSetId || approval.ActionProposalId != Id || approval.ChangeSetId is null || approval.ChangeSetId != ChangeSetId || approval.ChangeSetFingerprint != changeSet.Fingerprint || !changeSet.HasValidFingerprint())
        {
            throw new InvalidOperationException("Approval is not bound to the exact action proposal and change set.");
        }

        if (approval.WorkflowId != WorkflowId || approval.WorkflowNodeId != WorkflowNodeId || approval.PlanRevisionId != PlanRevisionId)
        {
            throw new InvalidOperationException("Approval does not match the action proposal scope.");
        }

        if (approval.Decision != ApprovalDecision.Approved)
        {
            throw new InvalidOperationException("An action proposal requires an approved authorization.");
        }

        if (State != ActionProposalState.Proposed)
        {
            throw new InvalidOperationException("The action proposal has already been decided.");
        }

        State = ActionProposalState.Authorized;
        DecidedAtUtc = authorizedAtUtc;
    }

    public void AuthorizeByPolicy(ChangeSet changeSet, ChangeSetAuthorization authorization, DateTimeOffset authorizedAtUtc)
    {
        if (ChangeSetId is null || !changeSet.HasValidFingerprint() || authorization.ActionProposalId != Id || authorization.ChangeSetId != ChangeSetId || authorization.ChangeSetFingerprint != changeSet.Fingerprint || authorization.Mechanism != ChangeSetAuthorizationMechanism.PolicyAuthorized)
        {
            throw new InvalidOperationException("Policy authorization is not bound to the exact action proposal and change set.");
        }

        if (State != ActionProposalState.Proposed) throw new InvalidOperationException("The action proposal has already been decided.");
        State = ActionProposalState.Authorized;
        DecidedAtUtc = authorizedAtUtc;
    }

    public void AttachChangeSet(Guid changeSetId)
    {
        if (changeSetId == Guid.Empty) throw new ArgumentException("A change set id is required.", nameof(changeSetId));
        if (ChangeSetId is not null) throw new InvalidOperationException("The action proposal already has a change set.");
        ChangeSetId = changeSetId;
    }

    public void MarkApplied(ChangeSet changeSet, Guid executionId, Guid activePlanRevisionId, DateTimeOffset appliedAtUtc)
    {
        if (State != ActionProposalState.Authorized) throw new InvalidOperationException("Only an authorized action proposal can be applied.");
        if (changeSet.Id != ChangeSetId || changeSet.WorkflowId != WorkflowId || changeSet.WorkflowNodeId != WorkflowNodeId || changeSet.PlanRevisionId != activePlanRevisionId || changeSet.ActionProposalId != Id)
        {
            throw new InvalidOperationException("The change set does not match the authorized action proposal scope.");
        }
        if (executionId == Guid.Empty) throw new ArgumentException("An applied execution id is required.", nameof(executionId));
        State = ActionProposalState.Applied;
        AppliedExecutionId = executionId;
        DecidedAtUtc = appliedAtUtc;
    }

    public void Reject(DateTimeOffset rejectedAtUtc)
    {
        if (State != ActionProposalState.Proposed) throw new InvalidOperationException("The action proposal has already been decided.");
        State = ActionProposalState.Rejected;
        DecidedAtUtc = rejectedAtUtc;
    }
}

public sealed class ChangeSet
{
    private ChangeSet()
    {
    }

    public ChangeSet(Guid workflowId, Guid workflowNodeId, Guid planRevisionId, Guid actionProposalId, string providerName, Guid proposalExecutionId, RiskLevel risk, PrivilegedOperationType operationType, string operationContent, string summary, string scope, DateTimeOffset createdAtUtc)
    {
        if (workflowId == Guid.Empty) throw new ArgumentException("A workflow id is required.", nameof(workflowId));
        if (actionProposalId == Guid.Empty) throw new ArgumentException("An action proposal id is required.", nameof(actionProposalId));
        if (workflowNodeId == Guid.Empty) throw new ArgumentException("A workflow node id is required.", nameof(workflowNodeId));
        if (planRevisionId == Guid.Empty) throw new ArgumentException("A plan revision id is required.", nameof(planRevisionId));
        if (proposalExecutionId == Guid.Empty) throw new ArgumentException("A proposal execution id is required.", nameof(proposalExecutionId));
        if (string.IsNullOrWhiteSpace(providerName)) throw new ArgumentException("A provider name is required.", nameof(providerName));
        if (string.IsNullOrWhiteSpace(operationContent)) throw new ArgumentException("Operation content is required.", nameof(operationContent));
        if (string.IsNullOrWhiteSpace(summary)) throw new ArgumentException("A change-set summary is required.", nameof(summary));
        if (string.IsNullOrWhiteSpace(scope)) throw new ArgumentException("A change-set scope is required.", nameof(scope));
        Id = Guid.NewGuid();
        WorkflowId = workflowId;
        WorkflowNodeId = workflowNodeId;
        PlanRevisionId = planRevisionId;
        ActionProposalId = actionProposalId;
        ProviderName = providerName;
        ProposalExecutionId = proposalExecutionId;
        Risk = risk;
        OperationType = operationType;
        OperationContent = operationContent;
        Summary = summary;
        Scope = scope;
        CreatedAtUtc = createdAtUtc;
        Fingerprint = ComputeFingerprint(this);
    }

    public Guid Id { get; private set; }
    public Guid WorkflowId { get; private set; }
    public Guid WorkflowNodeId { get; private set; }
    public Guid PlanRevisionId { get; private set; }
    public Guid ActionProposalId { get; private set; }
    public string ProviderName { get; private set; } = string.Empty;
    public Guid ProposalExecutionId { get; private set; }
    public RiskLevel Risk { get; private set; }
    public PrivilegedOperationType OperationType { get; private set; }
    public string OperationContent { get; private set; } = string.Empty;
    public string Summary { get; private set; } = string.Empty;
    public string Scope { get; private set; } = string.Empty;
    public string Fingerprint { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public bool HasValidFingerprint() => string.Equals(Fingerprint, ComputeFingerprint(this), StringComparison.Ordinal);

    private static string ComputeFingerprint(ChangeSet changeSet)
    {
        var canonical = string.Join("\n", changeSet.ActionProposalId, changeSet.WorkflowId, changeSet.WorkflowNodeId, changeSet.PlanRevisionId, changeSet.ProviderName, changeSet.ProposalExecutionId, (int)changeSet.Risk, (int)changeSet.OperationType, changeSet.OperationContent, changeSet.Summary, changeSet.Scope);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical)));
    }
}

public sealed class ChangeSetAuthorization
{
    private ChangeSetAuthorization()
    {
    }

    public ChangeSetAuthorization(Guid workflowId, Guid workflowNodeId, Guid planRevisionId, Guid actionProposalId, Guid changeSetId, string changeSetFingerprint, ChangeSetAuthorizationMechanism mechanism, Guid? approvalId, DateTimeOffset authorizedAtUtc)
    {
        if (workflowId == Guid.Empty || workflowNodeId == Guid.Empty || planRevisionId == Guid.Empty || actionProposalId == Guid.Empty || changeSetId == Guid.Empty) throw new ArgumentException("Authorization lineage identities are required.");
        if (string.IsNullOrWhiteSpace(changeSetFingerprint)) throw new ArgumentException("A change set fingerprint is required.", nameof(changeSetFingerprint));
        if (mechanism == ChangeSetAuthorizationMechanism.HumanApproved && approvalId is null) throw new ArgumentException("Human authorization requires an approval id.", nameof(approvalId));
        Id = Guid.NewGuid();
        WorkflowId = workflowId;
        WorkflowNodeId = workflowNodeId;
        PlanRevisionId = planRevisionId;
        ActionProposalId = actionProposalId;
        ChangeSetId = changeSetId;
        ChangeSetFingerprint = changeSetFingerprint;
        Mechanism = mechanism;
        ApprovalId = approvalId;
        AuthorizedAtUtc = authorizedAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid WorkflowId { get; private set; }
    public Guid WorkflowNodeId { get; private set; }
    public Guid PlanRevisionId { get; private set; }
    public Guid ActionProposalId { get; private set; }
    public Guid ChangeSetId { get; private set; }
    public string ChangeSetFingerprint { get; private set; } = string.Empty;
    public ChangeSetAuthorizationMechanism Mechanism { get; private set; }
    public Guid? ApprovalId { get; private set; }
    public DateTimeOffset AuthorizedAtUtc { get; private set; }
}

public sealed class AgentExecution
{
    private AgentExecution()
    {
    }

    public AgentExecution(Guid workflowNodeId, string providerName, DateTimeOffset startedAtUtc)
        : this(workflowNodeId, providerName, 1, startedAtUtc)
    {
    }

    public AgentExecution(Guid workflowNodeId, string providerName, int attempt, DateTimeOffset startedAtUtc)
    {
        if (workflowNodeId == Guid.Empty) throw new ArgumentException("A workflow node id is required.", nameof(workflowNodeId));
        if (string.IsNullOrWhiteSpace(providerName)) throw new ArgumentException("A provider name is required.", nameof(providerName));
        if (attempt < 1) throw new ArgumentOutOfRangeException(nameof(attempt));
        Id = Guid.NewGuid();
        WorkflowNodeId = workflowNodeId;
        ProviderName = providerName;
        Attempt = attempt;
        StartedAtUtc = startedAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid WorkflowNodeId { get; private set; }
    public string ProviderName { get; private set; } = string.Empty;
    public int Attempt { get; private set; }
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public AgentExecutionStatus Status { get; private set; } = AgentExecutionStatus.Started;
    public string? OutputSummary { get; private set; }

    public void Complete(AgentExecutionStatus status, DateTimeOffset completedAtUtc, string? outputSummary)
    {
        Status = status;
        CompletedAtUtc = completedAtUtc;
        OutputSummary = outputSummary;
    }
}

public enum AgentExecutionStatus
{
    Started = 1,
    Succeeded = 2,
    Failed = 3
}

public enum DecisionOutcome
{
    Pending = 1,
    Approved = 2,
    Rejected = 3
}

public sealed class Decision
{
    private Decision()
    {
    }

    public Decision(Guid workflowId, string question, DateTimeOffset createdAtUtc)
    {
        if (workflowId == Guid.Empty) throw new ArgumentException("A workflow id is required.", nameof(workflowId));
        if (string.IsNullOrWhiteSpace(question)) throw new ArgumentException("A decision question is required.", nameof(question));
        Id = Guid.NewGuid();
        WorkflowId = workflowId;
        Question = question;
        CreatedAtUtc = createdAtUtc;
        Outcome = DecisionOutcome.Pending;
    }

    public Guid Id { get; private set; }
    public Guid WorkflowId { get; private set; }
    public string Question { get; private set; } = string.Empty;
    public string? Answer { get; private set; }
    public DecisionOutcome Outcome { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
}

public enum ApprovalDecision
{
    Pending = 1,
    Approved = 2,
    Rejected = 3
}

public sealed class Approval
{
    private Approval()
    {
    }

    public Approval(Guid workflowId, string action, string risk, string approverRole, DateTimeOffset requestedAtUtc)
        : this(workflowId, Guid.NewGuid(), action, Enum.TryParse<RiskLevel>(risk, true, out var parsed) ? parsed : RiskLevel.Medium, approverRole, requestedAtUtc)
    {
    }

    public Approval(Guid workflowId, Guid workflowNodeId, string action, RiskLevel risk, string approverRole, DateTimeOffset requestedAtUtc)
        : this(workflowId, workflowNodeId, null, action, risk, approverRole, requestedAtUtc)
    {
    }

    public Approval(Guid workflowId, Guid workflowNodeId, Guid planRevisionId, string action, RiskLevel risk, string approverRole, DateTimeOffset requestedAtUtc)
        : this(workflowId, workflowNodeId, (Guid?)planRevisionId, action, risk, approverRole, requestedAtUtc)
    {
    }

    private Approval(Guid workflowId, Guid workflowNodeId, Guid? planRevisionId, string action, RiskLevel risk, string approverRole, DateTimeOffset requestedAtUtc)
    {
        if (workflowId == Guid.Empty) throw new ArgumentException("A workflow id is required.", nameof(workflowId));
        if (workflowNodeId == Guid.Empty) throw new ArgumentException("A workflow node id is required.", nameof(workflowNodeId));
        if (planRevisionId == Guid.Empty) throw new ArgumentException("A plan revision id must be a non-empty id.", nameof(planRevisionId));
        if (string.IsNullOrWhiteSpace(action)) throw new ArgumentException("An approval action is required.", nameof(action));
        if (string.IsNullOrWhiteSpace(approverRole)) throw new ArgumentException("An approver role is required.", nameof(approverRole));
        Id = Guid.NewGuid();
        WorkflowId = workflowId;
        WorkflowNodeId = workflowNodeId;
        PlanRevisionId = planRevisionId;
        Action = action;
        Risk = risk;
        ApproverRole = approverRole;
        RequestedAtUtc = requestedAtUtc;
        Decision = ApprovalDecision.Pending;
    }

    public Guid Id { get; private set; }
    public Guid WorkflowId { get; private set; }
    public Guid WorkflowNodeId { get; private set; }
    public Guid? PlanRevisionId { get; private set; }
    public Guid? ActionProposalId { get; private set; }
    public Guid? ChangeSetId { get; private set; }
    public string? ChangeSetFingerprint { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public RiskLevel Risk { get; private set; }
    public string ApproverRole { get; private set; } = string.Empty;
    public ApprovalDecision Decision { get; private set; }
    public string? Rationale { get; private set; }
    public DateTimeOffset RequestedAtUtc { get; private set; }
    public DateTimeOffset? DecidedAtUtc { get; private set; }

    public void Decide(ApprovalDecision decision, string rationale, DateTimeOffset decidedAtUtc)
    {
        if (Decision != ApprovalDecision.Pending) throw new InvalidOperationException("Approval has already been decided.");
        if (string.IsNullOrWhiteSpace(rationale)) throw new ArgumentException("Approval rationale is required.", nameof(rationale));
        Decision = decision;
        Rationale = rationale;
        DecidedAtUtc = decidedAtUtc;
    }

    public void BindToChangeSet(ChangeSet changeSet)
    {
        if (changeSet.WorkflowId != WorkflowId || changeSet.WorkflowNodeId != WorkflowNodeId || changeSet.PlanRevisionId != PlanRevisionId)
        {
            throw new InvalidOperationException("The change set does not match the approval scope.");
        }
        if (ChangeSetId is not null) throw new InvalidOperationException("Approval is already bound to a change set.");
        ActionProposalId = changeSet.ActionProposalId;
        ChangeSetId = changeSet.Id;
        ChangeSetFingerprint = changeSet.Fingerprint;
    }
}

public sealed class PolicyEvaluation
{
    private PolicyEvaluation()
    {
    }

    public PolicyEvaluation(Guid workflowId, string policyName, bool allowed, string reason, DateTimeOffset evaluatedAtUtc)
        : this(workflowId, Guid.NewGuid(), policyName, RiskLevel.Medium, allowed, reason, evaluatedAtUtc)
    {
    }

    public PolicyEvaluation(Guid workflowId, Guid workflowNodeId, string policyName, RiskLevel risk, bool allowed, string reason, DateTimeOffset evaluatedAtUtc)
    {
        if (workflowId == Guid.Empty) throw new ArgumentException("A workflow id is required.", nameof(workflowId));
        if (workflowNodeId == Guid.Empty) throw new ArgumentException("A workflow node id is required.", nameof(workflowNodeId));
        if (string.IsNullOrWhiteSpace(policyName)) throw new ArgumentException("A policy name is required.", nameof(policyName));
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A policy reason is required.", nameof(reason));
        Id = Guid.NewGuid();
        WorkflowId = workflowId;
        WorkflowNodeId = workflowNodeId;
        PolicyName = policyName;
        Risk = risk;
        Allowed = allowed;
        Reason = reason;
        EvaluatedAtUtc = evaluatedAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid WorkflowId { get; private set; }
    public Guid WorkflowNodeId { get; private set; }
    public string PolicyName { get; private set; } = string.Empty;
    public RiskLevel Risk { get; private set; }
    public bool Allowed { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public DateTimeOffset EvaluatedAtUtc { get; private set; }
}

public sealed class ValidationResult
{
    private ValidationResult()
    {
    }

    public ValidationResult(Guid workflowId, string validationName, bool passed, string details, DateTimeOffset validatedAtUtc)
    {
        if (workflowId == Guid.Empty) throw new ArgumentException("A workflow id is required.", nameof(workflowId));
        if (string.IsNullOrWhiteSpace(validationName)) throw new ArgumentException("A validation name is required.", nameof(validationName));
        if (string.IsNullOrWhiteSpace(details)) throw new ArgumentException("Validation details are required.", nameof(details));
        Id = Guid.NewGuid();
        WorkflowId = workflowId;
        ValidationName = validationName;
        Passed = passed;
        Details = details;
        ValidatedAtUtc = validatedAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid WorkflowId { get; private set; }
    public string ValidationName { get; private set; } = string.Empty;
    public bool Passed { get; private set; }
    public string Details { get; private set; } = string.Empty;
    public DateTimeOffset ValidatedAtUtc { get; private set; }
}
