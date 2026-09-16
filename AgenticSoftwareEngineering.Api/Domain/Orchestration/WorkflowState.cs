namespace AgenticSoftwareEngineering.Api.Domain.Orchestration;

public enum WorkflowState
{
    Draft = 1,
    Planning = 2,
    Executing = 3,
    WaitingForApproval = 4,
    Blocked = 5,
    Replanning = 6,
    Recovering = 7,
    Validating = 8,
    ReleaseReadiness = 9,
    Completed = 10,
    Failed = 11,
    SafeStopped = 12,
    Cancelled = 13,
    RollingBack = 14
}

public enum WorkflowNodeState
{
    Pending = 1,
    Ready = 2,
    Executing = 3,
    WaitingForApproval = 4,
    Blocked = 5,
    RetryScheduled = 6,
    Validating = 7,
    Succeeded = 8,
    Failed = 9,
    Invalidated = 10,
    RollingBack = 11,
    Cancelled = 12
}

public static class WorkflowStateRules
{
    public static bool CanTransition(WorkflowState from, WorkflowState to) =>
        (from, to) switch
        {
            (WorkflowState.Draft, WorkflowState.Planning) => true,
            (WorkflowState.Planning, WorkflowState.Executing) => true,
            (WorkflowState.Planning, WorkflowState.Blocked) => true,
            (WorkflowState.Executing, WorkflowState.WaitingForApproval) => true,
            (WorkflowState.Executing, WorkflowState.Blocked) => true,
            (WorkflowState.Executing, WorkflowState.Validating) => true,
            (WorkflowState.Executing, WorkflowState.Failed) => true,
            (WorkflowState.Executing, WorkflowState.SafeStopped) => true,
            (WorkflowState.Executing, WorkflowState.Cancelled) => true,
            (WorkflowState.WaitingForApproval, WorkflowState.Executing) => true,
            (WorkflowState.Blocked, WorkflowState.Replanning) => true,
            (WorkflowState.Replanning, WorkflowState.Planning) => true,
            (WorkflowState.Failed, WorkflowState.Recovering) => true,
            (WorkflowState.Recovering, WorkflowState.Replanning) => true,
            (WorkflowState.Recovering, WorkflowState.RollingBack) => true,
            (WorkflowState.Validating, WorkflowState.ReleaseReadiness) => true,
            (WorkflowState.ReleaseReadiness, WorkflowState.Completed) => true,
            (WorkflowState.ReleaseReadiness, WorkflowState.Blocked) => true,
            (WorkflowState.SafeStopped, WorkflowState.Recovering) => true,
            (WorkflowState.RollingBack, WorkflowState.Recovering) => true,
            _ => false
        };

    public static bool CanTransition(WorkflowNodeState from, WorkflowNodeState to) =>
        (from, to) switch
        {
            (WorkflowNodeState.Pending, WorkflowNodeState.Ready) => true,
            (WorkflowNodeState.Ready, WorkflowNodeState.Executing) => true,
            (WorkflowNodeState.Executing, WorkflowNodeState.WaitingForApproval) => true,
            (WorkflowNodeState.Executing, WorkflowNodeState.Blocked) => true,
            (WorkflowNodeState.Executing, WorkflowNodeState.RetryScheduled) => true,
            (WorkflowNodeState.Executing, WorkflowNodeState.Validating) => true,
            (WorkflowNodeState.Executing, WorkflowNodeState.Failed) => true,
            (WorkflowNodeState.Validating, WorkflowNodeState.Succeeded) => true,
            (WorkflowNodeState.Succeeded, WorkflowNodeState.Invalidated) => true,
            (WorkflowNodeState.Invalidated, WorkflowNodeState.Ready) => true,
            (WorkflowNodeState.RetryScheduled, WorkflowNodeState.Ready) => true,
            (WorkflowNodeState.WaitingForApproval, WorkflowNodeState.Executing) => true,
            (WorkflowNodeState.Blocked, WorkflowNodeState.Ready) => true,
            (WorkflowNodeState.Executing, WorkflowNodeState.RollingBack) => true,
            (WorkflowNodeState.RollingBack, WorkflowNodeState.Invalidated) => true,
            (WorkflowNodeState.Pending, WorkflowNodeState.Cancelled) => true,
            (WorkflowNodeState.Ready, WorkflowNodeState.Cancelled) => true,
            _ => false
        };
}
