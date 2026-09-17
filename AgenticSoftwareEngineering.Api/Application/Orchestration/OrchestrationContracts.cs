using AgenticSoftwareEngineering.Api.Domain.Orchestration;

namespace AgenticSoftwareEngineering.Api.Application.Orchestration;

public sealed record CreateWorkflowRequest(string Requirement);

public sealed record WorkflowStatusResponse(
    Guid WorkflowId,
    string Name,
    WorkflowState State,
    IReadOnlyList<NodeStatusResponse> Nodes,
    IReadOnlyList<DependencyStatusResponse> Dependencies,
    IReadOnlyList<AgentExecutionResponse> Executions,
    IReadOnlyList<ArtifactStatusResponse> Artifacts,
    IReadOnlyList<ValidationStatusResponse> Validations,
    IReadOnlyList<EventStatusResponse> Events);

public sealed record NodeStatusResponse(Guid Id, string Name, string TaskType, WorkflowNodeState State);
public sealed record DependencyStatusResponse(Guid PredecessorNodeId, Guid SuccessorNodeId);
public sealed record AgentExecutionResponse(Guid Id, Guid WorkflowNodeId, string ProviderName, int Attempt, AgentExecutionStatus Status, DateTimeOffset StartedAtUtc, DateTimeOffset? CompletedAtUtc);
public sealed record ArtifactStatusResponse(Guid Id, string ArtifactType, int Version, string ContentReference, string ContentHash, Guid? ProducerNodeId, Guid? ProducerExecutionId, ArtifactValidationStatus ValidationStatus);
public sealed record ValidationStatusResponse(Guid Id, string ValidationName, bool Passed, string Details, DateTimeOffset ValidatedAtUtc);
public sealed record EventStatusResponse(Guid Id, string EventType, string Details, DateTimeOffset OccurredAtUtc);

public interface IOrchestrationService
{
    Task<WorkflowStatusResponse> CreateGreenfieldAsync(string requirement, CancellationToken cancellationToken);
    Task<WorkflowStatusResponse> AdvanceAsync(Guid workflowId, CancellationToken cancellationToken);
    Task<WorkflowStatusResponse> GetStatusAsync(Guid workflowId, CancellationToken cancellationToken);
}
