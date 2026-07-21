using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowEngine.Migrations.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddHotQueryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_workflows_project_id",
                schema: "flow",
                table: "workflows",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_execution_records_project_id",
                schema: "flow",
                table: "execution_records",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_execution_records_status_completed_at",
                schema: "flow",
                table: "execution_records",
                columns: new[] { "status", "completed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_execution_records_workflow_definition_id",
                schema: "flow",
                table: "execution_records",
                column: "workflow_definition_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_workflows_project_id",
                schema: "flow",
                table: "workflows");

            migrationBuilder.DropIndex(
                name: "IX_execution_records_project_id",
                schema: "flow",
                table: "execution_records");

            migrationBuilder.DropIndex(
                name: "IX_execution_records_status_completed_at",
                schema: "flow",
                table: "execution_records");

            migrationBuilder.DropIndex(
                name: "IX_execution_records_workflow_definition_id",
                schema: "flow",
                table: "execution_records");
        }
    }
}
