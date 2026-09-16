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
        modelBuilder.Entity<AgentExecution>().ToTable("Orchestration_AgentExecutions");
        modelBuilder.Entity<Decision>().ToTable("Orchestration_Decisions");
        modelBuilder.Entity<Approval>().ToTable("Orchestration_Approvals");
        modelBuilder.Entity<PolicyEvaluation>().ToTable("Orchestration_PolicyEvaluations");
        modelBuilder.Entity<ValidationResult>().ToTable("Orchestration_ValidationResults");
    }
}
