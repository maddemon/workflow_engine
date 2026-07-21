using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowEngine.Migrations.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddWorkflowCredentialUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "workflow_credential_usages",
                schema: "flow",
                columns: table => new
                {
                    WorkflowId = table.Column<Guid>(type: "TEXT", nullable: false, comment: "所属工作流 ID"),
                    CredentialId = table.Column<Guid>(type: "TEXT", nullable: false, comment: "被引用凭据 ID"),
                    NodeId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false, comment: "引用该凭据的节点 ID（工作流级引用时为空字符串）"),
                    WorkflowName = table.Column<string>(type: "TEXT", nullable: false, comment: "所属工作流名称（冗余存储，便于删除凭据时直接展示引用方，无需回查工作流表）")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_credential_usages", x => new { x.WorkflowId, x.CredentialId, x.NodeId });
                },
                comment: "工作流→凭据引用关系（归一化关联表），用于删除凭据时快速定位引用方");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_credential_usages_CredentialId",
                schema: "flow",
                table: "workflow_credential_usages",
                column: "CredentialId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "workflow_credential_usages",
                schema: "flow");
        }
    }
}
