using AgenticSoftwareEngineering.Api.Domain.Orchestration;
using AgenticSoftwareEngineering.Api.Domain.Urls;
using Microsoft.EntityFrameworkCore;

namespace AgenticSoftwareEngineering.Api.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<ShortLink> ShortLinks => Set<ShortLink>();
    public DbSet<ClickEvent> ClickEvents => Set<ClickEvent>();
    public DbSet<Workflow> Workflows => Set<Workflow>();
    public DbSet<WorkflowNode> WorkflowNodes => Set<WorkflowNode>();
    public DbSet<PlanRevision> PlanRevisions => Set<PlanRevision>();
    public DbSet<Dependency> Dependencies => Set<Dependency>();
    public DbSet<EngineeringArtifact> EngineeringArtifacts => Set<EngineeringArtifact>();
    public DbSet<ArtifactDependency> ArtifactDependencies => Set<ArtifactDependency>();
    public DbSet<WorkflowEvent> WorkflowEvents => Set<WorkflowEvent>();
    public DbSet<AgentExecution> AgentExecutions => Set<AgentExecution>();
    public DbSet<Decision> Decisions => Set<Decision>();
    public DbSet<Approval> Approvals => Set<Approval>();
    public DbSet<PolicyEvaluation> PolicyEvaluations => Set<PolicyEvaluation>();
    public DbSet<ValidationResult> ValidationResults => Set<ValidationResult>();
    public DbSet<ActionProposal> ActionProposals => Set<ActionProposal>();
    public DbSet<ChangeSet> ChangeSets => Set<ChangeSet>();
    public DbSet<ChangeSetAuthorization> ChangeSetAuthorizations => Set<ChangeSetAuthorization>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateArtifactDependencies();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ValidateArtifactDependencies();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ValidateArtifactDependencies()
    {
        var pending = ChangeTracker.Entries<ArtifactDependency>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified)
            .Select(entry => entry.Entity)
            .ToList();
        if (pending.Count == 0) return;

        var artifactIds = pending.SelectMany(edge => new[] { edge.ArtifactId, edge.DependentArtifactId }).Distinct().ToArray();
        var workflows = EngineeringArtifacts.Where(artifact => artifactIds.Contains(artifact.Id))
            .AsEnumerable()
            .Concat(ChangeTracker.Entries<EngineeringArtifact>()
                .Where(entry => entry.State is EntityState.Added or EntityState.Modified)
                .Select(entry => entry.Entity))
            .GroupBy(artifact => artifact.Id)
            .ToDictionary(group => group.Key, group => group.First().WorkflowId);
        foreach (var edge in pending)
        {
            if (!workflows.TryGetValue(edge.ArtifactId, out var sourceWorkflow) || !workflows.TryGetValue(edge.DependentArtifactId, out var dependentWorkflow) || sourceWorkflow != dependentWorkflow)
            {
                throw new InvalidOperationException("Artifact dependencies cannot cross workflows.");
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ShortLink>(entity =>
        {
            entity.ToTable("UrlShortener_ShortLinks");
            entity.HasKey(link => link.Id);
            entity.HasIndex(link => link.ShortCode).IsUnique();
            entity.Property(link => link.ShortCode).HasMaxLength(32).IsRequired();
            entity.Property(link => link.TargetUrl).HasMaxLength(2048).IsRequired();
            entity.HasMany(link => link.Clicks)
                .WithOne(click => click.ShortLink)
                .HasForeignKey(click => click.ShortLinkId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ClickEvent>(entity =>
        {
            entity.ToTable("UrlShortener_ClickEvents");
            entity.HasKey(click => click.Id);
        });

        modelBuilder.Entity<Workflow>(entity =>
        {
            entity.ToTable("Orchestration_Workflows");
            entity.HasKey(workflow => workflow.Id);
            entity.Property(workflow => workflow.Name).HasMaxLength(200).IsRequired();
            entity.HasMany(workflow => workflow.Nodes).WithOne(node => node.Workflow).HasForeignKey(node => node.WorkflowId);
            entity.HasMany(workflow => workflow.PlanRevisions).WithOne().HasForeignKey(revision => revision.WorkflowId);
            entity.HasMany(workflow => workflow.Events).WithOne().HasForeignKey(workflowEvent => workflowEvent.WorkflowId);
        });

        modelBuilder.Entity<WorkflowNode>(entity =>
        {
            entity.ToTable("Orchestration_WorkflowNodes");
            entity.HasKey(node => node.Id);
            entity.Property(node => node.Name).HasMaxLength(200).IsRequired();
            entity.Property(node => node.TaskType).HasMaxLength(100).IsRequired();
        });

        modelBuilder.Entity<PlanRevision>(entity =>
        {
            entity.ToTable("Orchestration_PlanRevisions");
            entity.HasKey(revision => revision.Id);
            entity.HasIndex(revision => new { revision.WorkflowId, revision.Revision }).IsUnique();
        });

        modelBuilder.Entity<Dependency>(entity =>
        {
            entity.ToTable("Orchestration_Dependencies");
            entity.HasKey(dependency => dependency.Id);
            entity.HasIndex(dependency => new { dependency.PredecessorNodeId, dependency.SuccessorNodeId }).IsUnique();
        });

        modelBuilder.Entity<EngineeringArtifact>(entity =>
        {
            entity.ToTable("Orchestration_EngineeringArtifacts");
            entity.HasKey(artifact => artifact.Id);
            entity.HasIndex(artifact => new { artifact.WorkflowId, artifact.ArtifactType, artifact.Version }).IsUnique();
            entity.Property(artifact => artifact.ArtifactType).HasMaxLength(100).IsRequired();
            entity.Property(artifact => artifact.ContentReference).HasMaxLength(2048).IsRequired();
            entity.Property(artifact => artifact.ContentHash).HasMaxLength(128).IsRequired();
        });

        modelBuilder.Entity<ArtifactDependency>(entity =>
        {
            entity.ToTable("Orchestration_ArtifactDependencies");
            entity.HasKey(dependency => dependency.Id);
            entity.HasIndex(dependency => new { dependency.ArtifactId, dependency.DependentArtifactId }).IsUnique();
        });

        modelBuilder.Entity<WorkflowEvent>().ToTable("Orchestration_WorkflowEvents");
        modelBuilder.Entity<AgentExecution>(entity =>
        {
            entity.ToTable("Orchestration_AgentExecutions");
            entity.HasKey(execution => execution.Id);
            entity.Property(execution => execution.ProviderName).HasMaxLength(100).IsRequired();
            entity.Property(execution => execution.OutputSummary).HasMaxLength(2000);
        });
        modelBuilder.Entity<Decision>().ToTable("Orchestration_Decisions");
        modelBuilder.Entity<Approval>(entity =>
        {
            entity.ToTable("Orchestration_Approvals");
            entity.HasKey(approval => approval.Id);
            entity.Property(approval => approval.ApproverRole).HasMaxLength(100).IsRequired();
            entity.Property(approval => approval.Rationale).HasMaxLength(2000);
        });
        modelBuilder.Entity<ActionProposal>(entity =>
        {
            entity.ToTable("Orchestration_ActionProposals");
            entity.HasKey(proposal => proposal.Id);
            entity.Property(proposal => proposal.Action).HasMaxLength(1000).IsRequired();
            entity.HasIndex(proposal => new { proposal.WorkflowId, proposal.WorkflowNodeId, proposal.PlanRevisionId });
            entity.HasOne<Workflow>().WithMany().HasForeignKey(proposal => proposal.WorkflowId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<WorkflowNode>().WithMany().HasForeignKey(proposal => proposal.WorkflowNodeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PlanRevision>().WithMany().HasForeignKey(proposal => proposal.PlanRevisionId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ChangeSet>(entity =>
        {
            entity.ToTable("Orchestration_ChangeSets");
            entity.HasKey(changeSet => changeSet.Id);
            entity.Property(changeSet => changeSet.Summary).HasMaxLength(2000).IsRequired();
            entity.Property(changeSet => changeSet.Scope).HasMaxLength(1000).IsRequired();
            entity.HasIndex(changeSet => changeSet.ActionProposalId).IsUnique();
            entity.HasOne<ActionProposal>().WithMany().HasForeignKey(changeSet => changeSet.ActionProposalId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Workflow>().WithMany().HasForeignKey(changeSet => changeSet.WorkflowId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ChangeSetAuthorization>(entity =>
        {
            entity.ToTable("Orchestration_ChangeSetAuthorizations");
            entity.HasKey(authorization => authorization.Id);
            entity.Property(authorization => authorization.ChangeSetFingerprint).HasMaxLength(128).IsRequired();
            entity.HasIndex(authorization => authorization.ChangeSetId).IsUnique();
            entity.HasIndex(authorization => new { authorization.WorkflowId, authorization.WorkflowNodeId, authorization.PlanRevisionId });
            entity.HasOne<ActionProposal>().WithMany().HasForeignKey(authorization => authorization.ActionProposalId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ChangeSet>().WithMany().HasForeignKey(authorization => authorization.ChangeSetId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Approval>().WithMany().HasForeignKey(authorization => authorization.ApprovalId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<PolicyEvaluation>(entity =>
        {
            entity.ToTable("Orchestration_PolicyEvaluations");
            entity.HasKey(evaluation => evaluation.Id);
            entity.Property(evaluation => evaluation.PolicyName).HasMaxLength(100).IsRequired();
            entity.Property(evaluation => evaluation.Reason).HasMaxLength(2000).IsRequired();
            entity.Property(evaluation => evaluation.WorkflowNodeId).IsRequired();
        });
        modelBuilder.Entity<ValidationResult>().ToTable("Orchestration_ValidationResults");
    }
}
