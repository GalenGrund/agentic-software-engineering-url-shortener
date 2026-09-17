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
