using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowEngine.Migrations.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddConcurrencyTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "row_version",
                schema: "flow",
                table: "workflows",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L,
                comment: "乐观并发行版本");

            migrationBuilder.AddColumn<long>(
                name: "row_version",
                schema: "flow",
                table: "projects",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L,
                comment: "乐观并发行版本");

            migrationBuilder.AddColumn<long>(
                name: "row_version",
                schema: "flow",
                table: "credentials",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L,
                comment: "乐观并发行版本");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "row_version",
                schema: "flow",
                table: "workflows");

            migrationBuilder.DropColumn(
                name: "row_version",
                schema: "flow",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "row_version",
                schema: "flow",
                table: "credentials");
        }
    }
}
