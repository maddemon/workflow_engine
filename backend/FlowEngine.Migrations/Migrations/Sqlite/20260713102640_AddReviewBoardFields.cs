using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowEngine.Migrations.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddReviewBoardFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "diff",
                schema: "flow",
                table: "workflows",
                type: "json",
                nullable: false,
                defaultValue: "",
                comment: "modify 草稿的结构化差异");

            migrationBuilder.AddColumn<int>(
                name: "draft_status",
                schema: "flow",
                table: "workflows",
                type: "INTEGER",
                nullable: true,
                comment: "草稿审查状态：待审查/已拒绝/已确认");

            migrationBuilder.AddColumn<string>(
                name: "rejection_reason",
                schema: "flow",
                table: "workflows",
                type: "TEXT",
                maxLength: 2000,
                nullable: true,
                comment: "拒绝理由");

            migrationBuilder.AddColumn<int>(
                name: "source",
                schema: "flow",
                table: "workflows",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                comment: "工作流来源：人工创建或 AI 生成");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "diff",
                schema: "flow",
                table: "workflows");

            migrationBuilder.DropColumn(
                name: "draft_status",
                schema: "flow",
                table: "workflows");

            migrationBuilder.DropColumn(
                name: "rejection_reason",
                schema: "flow",
                table: "workflows");

            migrationBuilder.DropColumn(
                name: "source",
                schema: "flow",
                table: "workflows");
        }
    }
}
