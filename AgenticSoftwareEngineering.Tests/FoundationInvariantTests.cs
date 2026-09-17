using AgenticSoftwareEngineering.Api.Domain.Orchestration;

namespace AgenticSoftwareEngineering.Tests;

public sealed class FoundationInvariantTests
{
    [Fact]
    public void EngineeringArtifact_PreservesVersionAndSupersessionLineage()
    {
        var workflowId = Guid.NewGuid();
        var first = EngineeringArtifact.Create(workflowId, "requirement", 1, "requirements/v1.md", "hash-v1", null, null, null, DateTimeOffset.UtcNow);
        var second = EngineeringArtifact.Create(workflowId, "requirement", 2, "requirements/v2.md", "hash-v2", null, null, first.Id, DateTimeOffset.UtcNow);

        Assert.Equal(1, first.Version);
        Assert.Equal(2, second.Version);
        Assert.Equal(first.Id, second.SupersedesArtifactId);
        Assert.NotEqual(first.ContentHash, second.ContentHash);
        Assert.Equal(ArtifactValidationStatus.Unvalidated, first.ValidationStatus);
    }

    [Fact]
    public void EngineeringArtifact_RejectsInvalidVersion()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EngineeringArtifact.Create(Guid.NewGuid(), "design", 0, "design.md", "hash", null, null, null, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Workflow_RejectsIllegalStateTransition()
    {
        var workflow = new Workflow("baseline", DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => workflow.TransitionTo(WorkflowState.Completed));
        Assert.Equal(WorkflowState.Draft, workflow.State);
    }

    [Fact]
    public void WorkflowNode_RequiresExplicitStateProgression()
    {
        var node = new WorkflowNode(Guid.NewGuid(), "validate", DateTimeOffset.UtcNow);

        node.TransitionTo(WorkflowNodeState.Ready);
        node.TransitionTo(WorkflowNodeState.Executing);
        node.TransitionTo(WorkflowNodeState.Validating);
        node.TransitionTo(WorkflowNodeState.Succeeded);

        Assert.Equal(WorkflowNodeState.Succeeded, node.State);
        Assert.Throws<InvalidOperationException>(() => node.TransitionTo(WorkflowNodeState.Executing));
    }

    [Fact]
    public void SafeStopped_RequiresRecoveryBeforeExecution()
    {
        var workflow = new Workflow("recovery", DateTimeOffset.UtcNow);
        workflow.TransitionTo(WorkflowState.Planning);
        workflow.TransitionTo(WorkflowState.Executing);
        workflow.TransitionTo(WorkflowState.SafeStopped);

        Assert.Throws<InvalidOperationException>(() => workflow.TransitionTo(WorkflowState.Executing));
        workflow.TransitionTo(WorkflowState.Recovering);
        workflow.TransitionTo(WorkflowState.Replanning);
        workflow.TransitionTo(WorkflowState.Planning);
        Assert.Equal(WorkflowState.Planning, workflow.State);
    }

    [Fact]
    public void FoundationalModels_RejectMissingRequiredValues()
    {
        Assert.Throws<ArgumentException>(() => new PlanRevision(Guid.Empty, 1, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlanRevision(Guid.NewGuid(), 0, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => new Dependency(Guid.Empty, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => new ArtifactDependency(Guid.NewGuid(), Guid.Empty));
        Assert.Throws<ArgumentException>(() => new WorkflowEvent(Guid.Empty, "state", "details", DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => new AgentExecution(Guid.Empty, "deterministic", DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => new Decision(Guid.Empty, "question", DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => new Approval(Guid.Empty, "action", "low", "reviewer", DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => new PolicyEvaluation(Guid.Empty, "policy", true, "allowed", DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => new ValidationResult(Guid.Empty, "validation", true, "passed", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ActionProposalRequiresMatchingApprovedPlanBoundAuthorization()
    {
        var workflowId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var planRevisionId = Guid.NewGuid();
        var proposal = new ActionProposal(workflowId, nodeId, planRevisionId, "modify repository", RiskLevel.High, "provider", Guid.NewGuid(), DateTimeOffset.UtcNow);
        var changeSet = new ChangeSet(workflowId, nodeId, planRevisionId, proposal.Id, "provider", proposal.ProposalExecutionId, RiskLevel.High, PrivilegedOperationType.ImplementationPreparation, "content", "summary", "scope", DateTimeOffset.UtcNow);
        proposal.AttachChangeSet(changeSet.Id);
        var pendingApproval = new Approval(workflowId, nodeId, planRevisionId, "modify repository", RiskLevel.High, "reviewer", DateTimeOffset.UtcNow);
        pendingApproval.BindToChangeSet(changeSet);

        Assert.Throws<InvalidOperationException>(() => proposal.Authorize(pendingApproval, changeSet, DateTimeOffset.UtcNow));
        pendingApproval.Decide(ApprovalDecision.Approved, "Approved for test", DateTimeOffset.UtcNow);
        proposal.Authorize(pendingApproval, changeSet, DateTimeOffset.UtcNow);

        Assert.Equal(ActionProposalState.Authorized, proposal.State);
    }

    [Fact]
    public void ActionProposalRejectsApprovalFromAnotherScope()
    {
        var workflowId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var planRevisionId = Guid.NewGuid();
        var proposal = new ActionProposal(workflowId, nodeId, planRevisionId, "modify repository", RiskLevel.High, "provider", Guid.NewGuid(), DateTimeOffset.UtcNow);
        var changeSet = new ChangeSet(workflowId, nodeId, planRevisionId, proposal.Id, "provider", proposal.ProposalExecutionId, RiskLevel.High, PrivilegedOperationType.ImplementationPreparation, "content", "summary", "scope", DateTimeOffset.UtcNow);
        proposal.AttachChangeSet(changeSet.Id);
        var approval = new Approval(workflowId, nodeId, planRevisionId, "modify repository", RiskLevel.High, "reviewer", DateTimeOffset.UtcNow);
        var otherChangeSet = new ChangeSet(workflowId, nodeId, planRevisionId, Guid.NewGuid(), "provider", Guid.NewGuid(), RiskLevel.High, PrivilegedOperationType.ImplementationPreparation, "different-content", "summary", "scope", DateTimeOffset.UtcNow);
        approval.BindToChangeSet(otherChangeSet);
        approval.Decide(ApprovalDecision.Approved, "Wrong scope", DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => proposal.Authorize(approval, changeSet, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ChangeSetRequiresProposalAndScope()
    {
        var proposalId = Guid.NewGuid();
        var changeSet = new ChangeSet(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), proposalId, "provider", Guid.NewGuid(), RiskLevel.Medium, PrivilegedOperationType.ImplementationPreparation, "content", "Update API contract", "orchestration", DateTimeOffset.UtcNow);

        Assert.Equal(proposalId, changeSet.ActionProposalId);
        Assert.Throws<ArgumentException>(() => new ChangeSet(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), proposalId, "provider", Guid.NewGuid(), RiskLevel.Medium, PrivilegedOperationType.ImplementationPreparation, "content", "", "orchestration", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void MediumRiskPolicyAuthorizationPersistsExactFingerprintEvidence()
    {
        var workflowId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var planRevisionId = Guid.NewGuid();
        var proposal = new ActionProposal(workflowId, nodeId, planRevisionId, "prepare implementation", RiskLevel.Medium, "provider", Guid.NewGuid(), DateTimeOffset.UtcNow);
        var changeSet = new ChangeSet(workflowId, nodeId, planRevisionId, proposal.Id, "provider", proposal.ProposalExecutionId, RiskLevel.Medium, PrivilegedOperationType.ImplementationPreparation, "content", "summary", "scope", DateTimeOffset.UtcNow);
        proposal.AttachChangeSet(changeSet.Id);
        var authorization = new ChangeSetAuthorization(workflowId, nodeId, planRevisionId, proposal.Id, changeSet.Id, changeSet.Fingerprint, ChangeSetAuthorizationMechanism.PolicyAuthorized, null, DateTimeOffset.UtcNow);

        proposal.AuthorizeByPolicy(changeSet, authorization, DateTimeOffset.UtcNow);

        Assert.Equal(ActionProposalState.Authorized, proposal.State);
        Assert.Equal(changeSet.Fingerprint, authorization.ChangeSetFingerprint);
        Assert.Null(authorization.ApprovalId);
    }

    [Fact]
    public void ChangeSetFingerprintChangesWhenMeaningfulContentChanges()
    {
        var workflowId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var planRevisionId = Guid.NewGuid();
        var proposalId = Guid.NewGuid();
        var executionId = Guid.NewGuid();
        var first = new ChangeSet(workflowId, nodeId, planRevisionId, proposalId, "provider", executionId, RiskLevel.Medium, PrivilegedOperationType.ImplementationPreparation, "content-v1", "summary", "scope", DateTimeOffset.UtcNow);
        var second = new ChangeSet(workflowId, nodeId, planRevisionId, proposalId, "provider", executionId, RiskLevel.Medium, PrivilegedOperationType.ImplementationPreparation, "content-v2", "summary", "scope", DateTimeOffset.UtcNow);

        Assert.NotEqual(first.Fingerprint, second.Fingerprint);
        Assert.True(first.HasValidFingerprint());
        Assert.True(second.HasValidFingerprint());
    }
}
