using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgenticSoftwareEngineering.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Gate2Orchestration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TaskType",
                table: "Orchestration_WorkflowNodes",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Attempt",
                table: "Orchestration_AgentExecutions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "OutputSummary",
                table: "Orchestration_AgentExecutions",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Orchestration_AgentExecutions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TaskType",
                table: "Orchestration_WorkflowNodes");

            migrationBuilder.DropColumn(
                name: "Attempt",
                table: "Orchestration_AgentExecutions");

            migrationBuilder.DropColumn(
                name: "OutputSummary",
                table: "Orchestration_AgentExecutions");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Orchestration_AgentExecutions");
        }
    }
}
