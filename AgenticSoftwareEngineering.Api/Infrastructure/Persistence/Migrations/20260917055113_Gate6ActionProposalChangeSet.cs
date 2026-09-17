using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgenticSoftwareEngineering.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Gate6ActionProposalChangeSet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ActionProposalId",
                table: "Orchestration_Approvals",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChangeSetFingerprint",
                table: "Orchestration_Approvals",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ChangeSetId",
                table: "Orchestration_Approvals",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Orchestration_ActionProposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkflowId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkflowNodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanRevisionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Risk = table.Column<int>(type: "INTEGER", nullable: false),
                    ProviderName = table.Column<string>(type: "TEXT", nullable: false),
                    ProposalExecutionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChangeSetId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AppliedExecutionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    ProposedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orchestration_ActionProposals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Orchestration_ActionProposals_Orchestration_PlanRevisions_PlanRevisionId",
                        column: x => x.PlanRevisionId,
                        principalTable: "Orchestration_PlanRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Orchestration_ActionProposals_Orchestration_WorkflowNodes_WorkflowNodeId",
                        column: x => x.WorkflowNodeId,
                        principalTable: "Orchestration_WorkflowNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Orchestration_ActionProposals_Orchestration_Workflows_WorkflowId",
                        column: x => x.WorkflowId,
                        principalTable: "Orchestration_Workflows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Orchestration_ChangeSets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkflowId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkflowNodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanRevisionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActionProposalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProviderName = table.Column<string>(type: "TEXT", nullable: false),
                    ProposalExecutionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Risk = table.Column<int>(type: "INTEGER", nullable: false),
                    OperationType = table.Column<int>(type: "INTEGER", nullable: false),
                    OperationContent = table.Column<string>(type: "TEXT", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Scope = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orchestration_ChangeSets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Orchestration_ChangeSets_Orchestration_ActionProposals_ActionProposalId",
                        column: x => x.ActionProposalId,
                        principalTable: "Orchestration_ActionProposals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Orchestration_ChangeSets_Orchestration_Workflows_WorkflowId",
                        column: x => x.WorkflowId,
                        principalTable: "Orchestration_Workflows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Orchestration_ChangeSetAuthorizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkflowId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkflowNodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanRevisionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActionProposalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChangeSetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChangeSetFingerprint = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Mechanism = table.Column<int>(type: "INTEGER", nullable: false),
                    ApprovalId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AuthorizedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orchestration_ChangeSetAuthorizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Orchestration_ChangeSetAuthorizations_Orchestration_ActionProposals_ActionProposalId",
                        column: x => x.ActionProposalId,
                        principalTable: "Orchestration_ActionProposals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Orchestration_ChangeSetAuthorizations_Orchestration_Approvals_ApprovalId",
                        column: x => x.ApprovalId,
                        principalTable: "Orchestration_Approvals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Orchestration_ChangeSetAuthorizations_Orchestration_ChangeSets_ChangeSetId",
                        column: x => x.ChangeSetId,
                        principalTable: "Orchestration_ChangeSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orchestration_ActionProposals_PlanRevisionId",
                table: "Orchestration_ActionProposals",
                column: "PlanRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_Orchestration_ActionProposals_WorkflowId_WorkflowNodeId_PlanRevisionId",
                table: "Orchestration_ActionProposals",
                columns: new[] { "WorkflowId", "WorkflowNodeId", "PlanRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Orchestration_ActionProposals_WorkflowNodeId",
                table: "Orchestration_ActionProposals",
                column: "WorkflowNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_Orchestration_ChangeSetAuthorizations_ActionProposalId",
                table: "Orchestration_ChangeSetAuthorizations",
                column: "ActionProposalId");

            migrationBuilder.CreateIndex(
                name: "IX_Orchestration_ChangeSetAuthorizations_ApprovalId",
                table: "Orchestration_ChangeSetAuthorizations",
                column: "ApprovalId");

            migrationBuilder.CreateIndex(
                name: "IX_Orchestration_ChangeSetAuthorizations_ChangeSetId",
                table: "Orchestration_ChangeSetAuthorizations",
                column: "ChangeSetId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orchestration_ChangeSetAuthorizations_WorkflowId_WorkflowNodeId_PlanRevisionId",
                table: "Orchestration_ChangeSetAuthorizations",
                columns: new[] { "WorkflowId", "WorkflowNodeId", "PlanRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Orchestration_ChangeSets_ActionProposalId",
                table: "Orchestration_ChangeSets",
                column: "ActionProposalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orchestration_ChangeSets_WorkflowId",
                table: "Orchestration_ChangeSets",
                column: "WorkflowId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Orchestration_ChangeSetAuthorizations");

            migrationBuilder.DropTable(
                name: "Orchestration_ChangeSets");

            migrationBuilder.DropTable(
                name: "Orchestration_ActionProposals");

            migrationBuilder.DropColumn(
                name: "ActionProposalId",
                table: "Orchestration_Approvals");

            migrationBuilder.DropColumn(
                name: "ChangeSetFingerprint",
                table: "Orchestration_Approvals");

            migrationBuilder.DropColumn(
                name: "ChangeSetId",
                table: "Orchestration_Approvals");
        }
    }
}
