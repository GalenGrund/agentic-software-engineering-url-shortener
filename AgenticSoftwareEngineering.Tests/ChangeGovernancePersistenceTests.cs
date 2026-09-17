using AgenticSoftwareEngineering.Api.Domain.Orchestration;
using AgenticSoftwareEngineering.Api.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AgenticSoftwareEngineering.Tests;

public sealed class ChangeGovernancePersistenceTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly AppDbContext db;

    public ChangeGovernancePersistenceTests()
    {
        connection.Open();
        db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        db.Database.Migrate();
    }

    [Fact]
    public async Task ActionProposalAndChangeSetPersistTheirTrustBoundaryLineage()
    {
        var workflowId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var planRevisionId = Guid.NewGuid();
        var workflow = new Workflow("gate6", DateTimeOffset.UtcNow);
        workflow.TransitionTo(WorkflowState.Planning);
        var node = new WorkflowNode(workflow.Id, "Implementation Preparation", "implementation-preparation", DateTimeOffset.UtcNow, RiskLevel.High);
        var plan = new PlanRevision(workflow.Id, 1, DateTimeOffset.UtcNow);
        db.Workflows.Add(workflow);
        db.WorkflowNodes.Add(node);
        db.PlanRevisions.Add(plan);
        await db.SaveChangesAsync();
        workflowId = workflow.Id;
        nodeId = node.Id;
        planRevisionId = plan.Id;
        var proposal = new ActionProposal(workflowId, nodeId, planRevisionId, "modify repository", RiskLevel.High, "provider", Guid.NewGuid(), DateTimeOffset.UtcNow);
        var changeSet = new ChangeSet(workflowId, nodeId, planRevisionId, proposal.Id, "provider", proposal.ProposalExecutionId, RiskLevel.High, PrivilegedOperationType.ImplementationPreparation, "content", "Update orchestration contract", "orchestration", DateTimeOffset.UtcNow);
        proposal.AttachChangeSet(changeSet.Id);
        db.ActionProposals.Add(proposal);
        db.ChangeSets.Add(changeSet);
        await db.SaveChangesAsync();

        var persistedProposal = await db.ActionProposals.SingleAsync(item => item.Id == proposal.Id);
        var persistedChangeSet = await db.ChangeSets.SingleAsync(item => item.Id == changeSet.Id);

        Assert.Equal(planRevisionId, persistedProposal.PlanRevisionId);
        Assert.Equal(proposal.Id, persistedChangeSet.ActionProposalId);
        Assert.Equal(ActionProposalState.Proposed, persistedProposal.State);
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }
}
