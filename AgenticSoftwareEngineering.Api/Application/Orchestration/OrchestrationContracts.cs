using AgenticSoftwareEngineering.Api.Domain.Orchestration;

namespace AgenticSoftwareEngineering.Api.Application.Orchestration;

public sealed class OrchestrationOptions
{
    public int MaxTotalExecutionAttempts { get; set; } = 2;
    public bool AllowFallback { get; set; } = true;
}

public sealed record CreateWorkflowRequest(string Requirement, bool RequiresHighRiskApproval = false);
public sealed record ApprovalDecisionRequest(bool Approved, string Rationale);

public sealed record WorkflowStatusResponse(
    Guid WorkflowId,
    string Name,
    WorkflowState State,
    IReadOnlyList<NodeStatusResponse> Nodes,
    IReadOnlyList<DependencyStatusResponse> Dependencies,
    IReadOnlyList<AgentExecutionResponse> Executions,
    IReadOnlyList<ArtifactStatusResponse> Artifacts,
    IReadOnlyList<ValidationStatusResponse> Validations,
    IReadOnlyList<PolicyStatusResponse> Policies,
    IReadOnlyList<ApprovalStatusResponse> Approvals,
    IReadOnlyList<EventStatusResponse> Events);

public sealed record NodeStatusResponse(Guid Id, string Name, string TaskType, WorkflowNodeState State);
public sealed record DependencyStatusResponse(Guid PredecessorNodeId, Guid SuccessorNodeId);
public sealed record AgentExecutionResponse(Guid Id, Guid WorkflowNodeId, string ProviderName, int Attempt, AgentExecutionStatus Status, DateTimeOffset StartedAtUtc, DateTimeOffset? CompletedAtUtc);
public sealed record ArtifactStatusResponse(Guid Id, string ArtifactType, int Version, string ContentReference, string ContentHash, Guid? ProducerNodeId, Guid? ProducerExecutionId, ArtifactValidationStatus ValidationStatus);
public sealed record ValidationStatusResponse(Guid Id, string ValidationName, bool Passed, string Details, DateTimeOffset ValidatedAtUtc);
public sealed record PolicyStatusResponse(Guid Id, Guid WorkflowNodeId, string PolicyName, RiskLevel Risk, bool Allowed, string Reason, DateTimeOffset EvaluatedAtUtc);
public sealed record ApprovalStatusResponse(Guid Id, Guid WorkflowNodeId, Guid? PlanRevisionId, string Action, RiskLevel Risk, ApprovalDecision Decision, string ApproverRole, string? Rationale, DateTimeOffset RequestedAtUtc, DateTimeOffset? DecidedAtUtc);
public sealed record EventStatusResponse(Guid Id, string EventType, string Details, DateTimeOffset OccurredAtUtc);

public interface IOrchestrationService
{
    Task<WorkflowStatusResponse> CreateGreenfieldAsync(string requirement, CancellationToken cancellationToken, bool requiresHighRiskApproval = false);
    Task<WorkflowStatusResponse> AdvanceAsync(Guid workflowId, CancellationToken cancellationToken);
    Task<WorkflowStatusResponse> GetStatusAsync(Guid workflowId, CancellationToken cancellationToken);
    Task<WorkflowStatusResponse> DecideApprovalAsync(Guid workflowId, Guid approvalId, ApprovalDecisionRequest request, CancellationToken cancellationToken);
}
