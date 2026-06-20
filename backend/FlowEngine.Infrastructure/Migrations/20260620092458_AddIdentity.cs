using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowEngine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", maxLength: 36, nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false, comment: "用户 ID"),
                    Role = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false, comment: "角色名称"),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, comment: "创建时间"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true, comment: "最后更新时间"),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false, comment: "是否删除")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_roles", x => x.Id);
                },
                comment: "用户角色");

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", maxLength: 36, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false, comment: "邮箱地址"),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false, comment: "用户名"),
                    PasswordHash = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false, comment: "密码哈希值"),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true, comment: "显示名称"),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false, comment: "是否激活"),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, comment: "创建时间"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true, comment: "最后更新时间"),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false, comment: "是否删除")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                },
                comment: "用户");

            migrationBuilder.CreateIndex(
                name: "IX_user_roles_UserId_Role",
                table: "user_roles",
                columns: new[] { "UserId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_Email",
                table: "users",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_roles");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
