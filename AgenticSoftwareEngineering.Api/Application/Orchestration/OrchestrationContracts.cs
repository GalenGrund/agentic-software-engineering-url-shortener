using AgenticSoftwareEngineering.Api.Domain.Orchestration;

namespace AgenticSoftwareEngineering.Api.Application.Orchestration;

public sealed class OrchestrationOptions
{
    public int MaxTotalExecutionAttempts { get; set; } = 2;
    public bool AllowFallback { get; set; } = true;
}

public sealed record CreateWorkflowRequest(string Requirement, bool RequiresHighRiskApproval = false);
public sealed record ApprovalDecisionRequest(bool Approved, string Rationale);
public sealed record BrownfieldArtifactRevisionRequest(string ContentReference, string ContentHash, bool RequiresHighRiskApproval = false);
public sealed record WorkflowRollbackRequest(Guid ArtifactId);
public sealed record ClarificationRequest(string Clarification);

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
    IReadOnlyList<EventStatusResponse> Events,
    IReadOnlyList<PlanRevisionStatusResponse> PlanRevisions,
    IReadOnlyList<ArtifactDependencyStatusResponse> ArtifactDependencies,
    string Scenario,
    string? ClarificationStatus,
    IReadOnlyList<ActionProposalStatusResponse> ActionProposals,
    IReadOnlyList<ChangeSetStatusResponse> ChangeSets,
    IReadOnlyList<ChangeSetAuthorizationStatusResponse> ChangeSetAuthorizations);

public sealed record NodeStatusResponse(Guid Id, string Name, string TaskType, WorkflowNodeState State);
public sealed record DependencyStatusResponse(Guid PredecessorNodeId, Guid SuccessorNodeId);
public sealed record AgentExecutionResponse(Guid Id, Guid WorkflowNodeId, string ProviderName, int Attempt, AgentExecutionStatus Status, DateTimeOffset StartedAtUtc, DateTimeOffset? CompletedAtUtc);
public sealed record ArtifactStatusResponse(Guid Id, string ArtifactType, int Version, string ContentReference, string ContentHash, Guid? ProducerNodeId, Guid? ProducerExecutionId, Guid? SupersedesArtifactId, ArtifactValidationStatus ValidationStatus);
public sealed record ValidationStatusResponse(Guid Id, string ValidationName, bool Passed, string Details, DateTimeOffset ValidatedAtUtc);
public sealed record PolicyStatusResponse(Guid Id, Guid WorkflowNodeId, string PolicyName, RiskLevel Risk, bool Allowed, string Reason, DateTimeOffset EvaluatedAtUtc);
public sealed record ApprovalStatusResponse(Guid Id, Guid WorkflowNodeId, Guid? PlanRevisionId, Guid? ActionProposalId, Guid? ChangeSetId, string? ChangeSetFingerprint, string Action, RiskLevel Risk, ApprovalDecision Decision, string ApproverRole, string? Rationale, DateTimeOffset RequestedAtUtc, DateTimeOffset? DecidedAtUtc);
public sealed record EventStatusResponse(Guid Id, string EventType, string Details, DateTimeOffset OccurredAtUtc);
public sealed record PlanRevisionStatusResponse(Guid Id, int Revision, Guid? SupersedesRevisionId, DateTimeOffset CreatedAtUtc);
public sealed record ArtifactDependencyStatusResponse(Guid Id, Guid ArtifactId, Guid DependentArtifactId);
public sealed record ActionProposalStatusResponse(Guid Id, Guid WorkflowNodeId, Guid PlanRevisionId, Guid? ChangeSetId, Guid? AppliedExecutionId, string ProviderName, Guid ProposalExecutionId, RiskLevel Risk, ActionProposalState State, DateTimeOffset ProposedAtUtc, DateTimeOffset? DecidedAtUtc);
public sealed record ChangeSetStatusResponse(Guid Id, Guid ActionProposalId, Guid WorkflowNodeId, Guid PlanRevisionId, string ProviderName, Guid ProposalExecutionId, RiskLevel Risk, PrivilegedOperationType OperationType, string OperationContent, string Summary, string Scope, string Fingerprint, DateTimeOffset CreatedAtUtc);
public sealed record ChangeSetAuthorizationStatusResponse(Guid Id, Guid ActionProposalId, Guid ChangeSetId, Guid WorkflowNodeId, Guid PlanRevisionId, ChangeSetAuthorizationMechanism Mechanism, Guid? ApprovalId, string ChangeSetFingerprint, DateTimeOffset AuthorizedAtUtc);
public sealed record WorkflowMetricsResponse(
    Guid WorkflowId,
    bool Completed,
    double? WorkflowLatencyMilliseconds,
    int ProviderExecutionCount,
    double? ProviderExecutionLatencyMilliseconds,
    int RetryCount,
    double? RetryRate,
    int FallbackCount,
    double? FallbackRate,
    bool SafeStopped,
    int ApprovalCount,
    double? ApprovalWaitMilliseconds,
    double? MeanRecoveryTimeMilliseconds);

public interface IOrchestrationService
{
    Task<WorkflowStatusResponse> CreateGreenfieldAsync(string requirement, CancellationToken cancellationToken, bool requiresHighRiskApproval = false);
    Task<WorkflowStatusResponse> AdvanceAsync(Guid workflowId, CancellationToken cancellationToken);
    Task<WorkflowStatusResponse> GetStatusAsync(Guid workflowId, CancellationToken cancellationToken);
    Task<WorkflowStatusResponse> DecideApprovalAsync(Guid workflowId, Guid approvalId, ApprovalDecisionRequest request, CancellationToken cancellationToken);
    Task<WorkflowStatusResponse> ReviseArtifactAsync(Guid workflowId, Guid artifactId, BrownfieldArtifactRevisionRequest request, CancellationToken cancellationToken);
    Task<WorkflowStatusResponse> RollbackAsync(Guid workflowId, WorkflowRollbackRequest request, CancellationToken cancellationToken);
    Task<WorkflowStatusResponse> ProvideClarificationAsync(Guid workflowId, ClarificationRequest request, CancellationToken cancellationToken);
    Task<WorkflowMetricsResponse> GetMetricsAsync(Guid workflowId, CancellationToken cancellationToken);
    Task<WorkflowStatusResponse> ApplyAuthorizedProposalAsync(Guid workflowId, Guid proposalId, CancellationToken cancellationToken);
}
