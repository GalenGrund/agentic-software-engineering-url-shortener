using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgenticSoftwareEngineering.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Orchestration_AgentExecutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkflowNodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProviderName = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orchestration_AgentExecutions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Orchestration_Approvals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkflowId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanRevisionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    Risk = table.Column<string>(type: "TEXT", nullable: false),
                    ApproverRole = table.Column<string>(type: "TEXT", nullable: false),
                    Decision = table.Column<int>(type: "INTEGER", nullable: false),
                    Rationale = table.Column<string>(type: "TEXT", nullable: true),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orchestration_Approvals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Orchestration_ArtifactDependencies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ArtifactId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DependentArtifactId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orchestration_ArtifactDependencies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Orchestration_Decisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkflowId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Question = table.Column<string>(type: "TEXT", nullable: false),
                    Answer = table.Column<string>(type: "TEXT", nullable: true),
                    Outcome = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orchestration_Decisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Orchestration_Dependencies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PredecessorNodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SuccessorNodeId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orchestration_Dependencies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Orchestration_EngineeringArtifacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkflowId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ArtifactType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    ContentReference = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ProducerNodeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProducerExecutionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SupersedesArtifactId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ValidationStatus = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orchestration_EngineeringArtifacts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Orchestration_PolicyEvaluations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkflowId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PolicyName = table.Column<string>(type: "TEXT", nullable: false),
                    Allowed = table.Column<bool>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    EvaluatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orchestration_PolicyEvaluations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Orchestration_ValidationResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkflowId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ValidationName = table.Column<string>(type: "TEXT", nullable: false),
                    Passed = table.Column<bool>(type: "INTEGER", nullable: false),
                    Details = table.Column<string>(type: "TEXT", nullable: false),
                    ValidatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orchestration_ValidationResults", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Orchestration_Workflows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orchestration_Workflows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UrlShortener_ShortLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShortCode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    TargetUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UrlShortener_ShortLinks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Orchestration_PlanRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkflowId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SupersedesRevisionId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orchestration_PlanRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Orchestration_PlanRevisions_Orchestration_Workflows_WorkflowId",
                        column: x => x.WorkflowId,
                        principalTable: "Orchestration_Workflows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Orchestration_WorkflowEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkflowId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", nullable: false),
                    Details = table.Column<string>(type: "TEXT", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orchestration_WorkflowEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Orchestration_WorkflowEvents_Orchestration_Workflows_WorkflowId",
                        column: x => x.WorkflowId,
                        principalTable: "Orchestration_Workflows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Orchestration_WorkflowNodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkflowId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orchestration_WorkflowNodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Orchestration_WorkflowNodes_Orchestration_Workflows_WorkflowId",
                        column: x => x.WorkflowId,
                        principalTable: "Orchestration_Workflows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UrlShortener_ClickEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ShortLinkId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Outcome = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UrlShortener_ClickEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UrlShortener_ClickEvents_UrlShortener_ShortLinks_ShortLinkId",
                        column: x => x.ShortLinkId,
                        principalTable: "UrlShortener_ShortLinks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orchestration_ArtifactDependencies_ArtifactId_DependentArtifactId",
                table: "Orchestration_ArtifactDependencies",
                columns: new[] { "ArtifactId", "DependentArtifactId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orchestration_Dependencies_PredecessorNodeId_SuccessorNodeId",
                table: "Orchestration_Dependencies",
                columns: new[] { "PredecessorNodeId", "SuccessorNodeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orchestration_EngineeringArtifacts_WorkflowId_ArtifactType_Version",
                table: "Orchestration_EngineeringArtifacts",
                columns: new[] { "WorkflowId", "ArtifactType", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orchestration_PlanRevisions_WorkflowId_Revision",
                table: "Orchestration_PlanRevisions",
                columns: new[] { "WorkflowId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orchestration_WorkflowEvents_WorkflowId",
                table: "Orchestration_WorkflowEvents",
                column: "WorkflowId");

            migrationBuilder.CreateIndex(
                name: "IX_Orchestration_WorkflowNodes_WorkflowId",
                table: "Orchestration_WorkflowNodes",
                column: "WorkflowId");

            migrationBuilder.CreateIndex(
                name: "IX_UrlShortener_ClickEvents_ShortLinkId",
                table: "UrlShortener_ClickEvents",
                column: "ShortLinkId");

            migrationBuilder.CreateIndex(
                name: "IX_UrlShortener_ShortLinks_ShortCode",
                table: "UrlShortener_ShortLinks",
                column: "ShortCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Orchestration_AgentExecutions");

            migrationBuilder.DropTable(
                name: "Orchestration_Approvals");

            migrationBuilder.DropTable(
                name: "Orchestration_ArtifactDependencies");

            migrationBuilder.DropTable(
                name: "Orchestration_Decisions");

            migrationBuilder.DropTable(
                name: "Orchestration_Dependencies");

            migrationBuilder.DropTable(
                name: "Orchestration_EngineeringArtifacts");

            migrationBuilder.DropTable(
                name: "Orchestration_PlanRevisions");

            migrationBuilder.DropTable(
                name: "Orchestration_PolicyEvaluations");

            migrationBuilder.DropTable(
                name: "Orchestration_ValidationResults");

            migrationBuilder.DropTable(
                name: "Orchestration_WorkflowEvents");

            migrationBuilder.DropTable(
                name: "Orchestration_WorkflowNodes");

            migrationBuilder.DropTable(
                name: "UrlShortener_ClickEvents");

            migrationBuilder.DropTable(
                name: "Orchestration_Workflows");

            migrationBuilder.DropTable(
                name: "UrlShortener_ShortLinks");
        }
    }
}
