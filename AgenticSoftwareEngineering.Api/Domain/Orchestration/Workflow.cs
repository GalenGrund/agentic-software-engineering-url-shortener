namespace AgenticSoftwareEngineering.Api.Domain.Orchestration;

public sealed class Workflow
{
    private Workflow()
    {
    }

    public Workflow(string name, DateTimeOffset createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A workflow name is required.", nameof(name));
        }

        Id = Guid.NewGuid();
        Name = name;
        CreatedAtUtc = createdAtUtc;
        State = WorkflowState.Draft;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public WorkflowState State { get; private set; }
    public ICollection<WorkflowNode> Nodes { get; private set; } = new List<WorkflowNode>();
    public ICollection<PlanRevision> PlanRevisions { get; private set; } = new List<PlanRevision>();
    public ICollection<WorkflowEvent> Events { get; private set; } = new List<WorkflowEvent>();

    public void TransitionTo(WorkflowState nextState)
    {
        if (!WorkflowStateRules.CanTransition(State, nextState))
        {
            throw new InvalidOperationException($"Workflow cannot transition from {State} to {nextState}.");
        }

        State = nextState;
    }
}
