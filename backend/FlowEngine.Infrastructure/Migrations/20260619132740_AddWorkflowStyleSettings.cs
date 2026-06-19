using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowEngine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowStyleSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "style_settings",
                table: "workflow_definitions",
                type: "TEXT",
                nullable: true,
                comment: "样式设置");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "style_settings",
                table: "workflow_definitions");
        }
    }
}
