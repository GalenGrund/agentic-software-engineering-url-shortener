namespace AgenticSoftwareEngineering.Api.Domain.Orchestration;

public sealed class WorkflowNode
{
    private WorkflowNode()
    {
    }

    public WorkflowNode(Guid workflowId, string name, DateTimeOffset createdAtUtc)
        : this(workflowId, name, "unspecified", createdAtUtc)
    {
    }

    public WorkflowNode(Guid workflowId, string name, string taskType, DateTimeOffset createdAtUtc)
    {
        if (workflowId == Guid.Empty)
        {
            throw new ArgumentException("A workflow id is required.", nameof(workflowId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A node name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(taskType))
        {
            throw new ArgumentException("A task type is required.", nameof(taskType));
        }

        Id = Guid.NewGuid();
        WorkflowId = workflowId;
        Name = name;
        TaskType = taskType;
        CreatedAtUtc = createdAtUtc;
        State = WorkflowNodeState.Pending;
    }

    public Guid Id { get; private set; }
    public Guid WorkflowId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string TaskType { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public WorkflowNodeState State { get; private set; }
    public Workflow? Workflow { get; private set; }

    public void TransitionTo(WorkflowNodeState nextState)
    {
        if (!WorkflowStateRules.CanTransition(State, nextState))
        {
            throw new InvalidOperationException($"Workflow node cannot transition from {State} to {nextState}.");
        }

        State = nextState;
    }
}
