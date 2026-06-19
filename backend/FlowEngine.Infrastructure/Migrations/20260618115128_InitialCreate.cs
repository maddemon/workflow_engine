using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowEngine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "execution_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false, comment: "主键"),
                    workflow_definition_id = table.Column<Guid>(type: "TEXT", nullable: false, comment: "工作流定义 ID"),
                    parent_execution_id = table.Column<Guid>(type: "TEXT", nullable: true, comment: "父执行 ID"),
                    started_at = table.Column<DateTime>(type: "TEXT", nullable: false, comment: "开始时间"),
                    completed_at = table.Column<DateTime>(type: "TEXT", nullable: true, comment: "完成时间"),
                    status = table.Column<int>(type: "INTEGER", nullable: false, comment: "执行状态"),
                    node_records = table.Column<string>(type: "TEXT", nullable: false, comment: "节点执行记录列表 JSON")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_execution_records", x => x.id);
                },
                comment: "执行记录");

            migrationBuilder.CreateTable(
                name: "workflow_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false, comment: "主键"),
                    version = table.Column<int>(type: "INTEGER", nullable: false, comment: "版本号"),
                    project_id = table.Column<Guid>(type: "TEXT", nullable: true, comment: "项目 ID"),
                    name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false, comment: "工作流名称"),
                    created_by = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false, comment: "创建人"),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, comment: "创建时间"),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false, comment: "更新时间"),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false, comment: "是否激活"),
                    nodes = table.Column<string>(type: "TEXT", nullable: false, comment: "节点实例列表 JSON"),
                    connections = table.Column<string>(type: "TEXT", nullable: false, comment: "连接列表 JSON")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_definitions", x => new { x.id, x.version });
                },
                comment: "工作流定义");

            migrationBuilder.CreateTable(
                name: "node_execution_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false, comment: "主键"),
                    execution_id = table.Column<Guid>(type: "TEXT", nullable: false, comment: "执行 ID"),
                    node_definition_id = table.Column<Guid>(type: "TEXT", nullable: false, comment: "节点定义 ID"),
                    run_index = table.Column<int>(type: "INTEGER", nullable: false, comment: "运行索引"),
                    started_at = table.Column<DateTime>(type: "TEXT", nullable: false, comment: "开始时间"),
                    completed_at = table.Column<DateTime>(type: "TEXT", nullable: true, comment: "完成时间"),
                    inputs = table.Column<string>(type: "TEXT", nullable: false, comment: "输入数据批次映射 JSON"),
                    output = table.Column<string>(type: "TEXT", nullable: false, comment: "节点执行结果 JSON"),
                    raw_parameters = table.Column<string>(type: "TEXT", nullable: false, comment: "原始参数映射 JSON"),
                    resolved_parameters = table.Column<string>(type: "TEXT", nullable: false, comment: "解析后的参数映射 JSON")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_node_execution_records", x => x.id);
                    table.ForeignKey(
                        name: "FK_node_execution_records_execution_records_execution_id",
                        column: x => x.execution_id,
                        principalTable: "execution_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "节点执行记录");

            migrationBuilder.CreateIndex(
                name: "IX_execution_records_status",
                table: "execution_records",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_execution_records_workflow_definition_id",
                table: "execution_records",
                column: "workflow_definition_id");

            migrationBuilder.CreateIndex(
                name: "IX_node_execution_records_execution_id",
                table: "node_execution_records",
                column: "execution_id");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_definitions_name",
                table: "workflow_definitions",
                column: "name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "node_execution_records");

            migrationBuilder.DropTable(
                name: "workflow_definitions");

            migrationBuilder.DropTable(
                name: "execution_records");
        }
    }
}
