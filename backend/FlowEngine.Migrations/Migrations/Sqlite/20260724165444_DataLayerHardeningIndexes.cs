using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowEngine.Migrations.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class DataLayerHardeningIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_execution_records_workflow_definition_id",
                schema: "flow",
                table: "execution_records");

            migrationBuilder.DropIndex(
                name: "IX_credentials_name_project_id",
                schema: "flow",
                table: "credentials");

            migrationBuilder.CreateIndex(
                name: "IX_triggers_project_id",
                table: "triggers",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_triggers_workflow_definition_id",
                table: "triggers",
                column: "workflow_definition_id");

            migrationBuilder.CreateIndex(
                name: "IX_stored_files_project_id",
                schema: "flow",
                table: "stored_files",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_execution_records_workflow_definition_id_started_at",
                schema: "flow",
                table: "execution_records",
                columns: new[] { "workflow_definition_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "IX_credentials_name_null_project",
                schema: "flow",
                table: "credentials",
                column: "name",
                unique: true,
                filter: "\"project_id\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_credentials_name_project_id_notnull",
                schema: "flow",
                table: "credentials",
                columns: new[] { "name", "project_id" },
                unique: true,
                filter: "\"project_id\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_triggers_project_id",
                table: "triggers");

            migrationBuilder.DropIndex(
                name: "IX_triggers_workflow_definition_id",
                table: "triggers");

            migrationBuilder.DropIndex(
                name: "IX_stored_files_project_id",
                schema: "flow",
                table: "stored_files");

            migrationBuilder.DropIndex(
                name: "IX_execution_records_workflow_definition_id_started_at",
                schema: "flow",
                table: "execution_records");

            migrationBuilder.DropIndex(
                name: "IX_credentials_name_null_project",
                schema: "flow",
                table: "credentials");

            migrationBuilder.DropIndex(
                name: "IX_credentials_name_project_id_notnull",
                schema: "flow",
                table: "credentials");

            migrationBuilder.CreateIndex(
                name: "IX_execution_records_workflow_definition_id",
                schema: "flow",
                table: "execution_records",
                column: "workflow_definition_id");

            migrationBuilder.CreateIndex(
                name: "IX_credentials_name_project_id",
                schema: "flow",
                table: "credentials",
                columns: new[] { "name", "project_id" },
                unique: true);
        }
    }
}
