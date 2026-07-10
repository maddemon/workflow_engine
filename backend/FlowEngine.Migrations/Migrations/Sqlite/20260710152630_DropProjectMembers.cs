using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowEngine.Migrations.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class DropProjectMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "project_members",
                schema: "flow");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "project_members",
                schema: "flow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", maxLength: 36, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, comment: "创建时间"),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false, comment: "是否删除"),
                    project_id = table.Column<Guid>(type: "TEXT", nullable: false, comment: "项目 ID"),
                    role = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false, comment: "成员角色"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true, comment: "最后更新时间"),
                    user_id = table.Column<Guid>(type: "TEXT", nullable: false, comment: "用户 ID")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_members", x => x.Id);
                },
                comment: "项目成员（历史兼容）");

            migrationBuilder.CreateIndex(
                name: "IX_project_members_project_id_user_id",
                schema: "flow",
                table: "project_members",
                columns: new[] { "project_id", "user_id" },
                unique: true);
        }
    }
}
