using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgenticSoftwareEngineering.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Gate3GovernanceRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Risk",
                table: "Orchestration_WorkflowNodes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Risk",
                table: "Orchestration_PolicyEvaluations",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkflowNodeId",
                table: "Orchestration_PolicyEvaluations",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<int>(
                name: "Risk",
                table: "Orchestration_Approvals",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AddColumn<Guid>(
                name: "WorkflowNodeId",
                table: "Orchestration_Approvals",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Risk",
                table: "Orchestration_WorkflowNodes");

            migrationBuilder.DropColumn(
                name: "Risk",
                table: "Orchestration_PolicyEvaluations");

            migrationBuilder.DropColumn(
                name: "WorkflowNodeId",
                table: "Orchestration_PolicyEvaluations");

            migrationBuilder.DropColumn(
                name: "WorkflowNodeId",
                table: "Orchestration_Approvals");

            migrationBuilder.AlterColumn<string>(
                name: "Risk",
                table: "Orchestration_Approvals",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "INTEGER");
        }
    }
}
