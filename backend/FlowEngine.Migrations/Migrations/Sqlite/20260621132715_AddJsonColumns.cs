using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowEngine.Migrations.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddJsonColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "connections",
                schema: "flow",
                table: "workflows",
                type: "json",
                nullable: false,
                defaultValue: "",
                comment: "连接列表");

            migrationBuilder.AddColumn<string>(
                name: "nodes",
                schema: "flow",
                table: "workflows",
                type: "json",
                nullable: false,
                defaultValue: "",
                comment: "节点实例列表");

            migrationBuilder.AddColumn<string>(
                name: "style_settings",
                schema: "flow",
                table: "workflows",
                type: "json",
                nullable: true,
                comment: "样式设置");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "connections",
                schema: "flow",
                table: "workflows");

            migrationBuilder.DropColumn(
                name: "nodes",
                schema: "flow",
                table: "workflows");

            migrationBuilder.DropColumn(
                name: "style_settings",
                schema: "flow",
                table: "workflows");
        }
    }
}
