using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowEngine.Migrations.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AddApiKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterTable(
                name: "project_members",
                schema: "flow",
                comment: "项目成员（历史兼容）",
                oldComment: "项目成员");

            migrationBuilder.CreateTable(
                name: "api_keys",
                schema: "flow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", maxLength: 36, nullable: false),
                    user_id = table.Column<Guid>(type: "TEXT", nullable: false, comment: "所属用户 ID"),
                    name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false, comment: "令牌名称"),
                    key_hash = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false, comment: "完整 Key 的哈希值"),
                    prefix = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false, comment: "Key 前缀"),
                    expires_at = table.Column<DateTime>(type: "TEXT", nullable: true, comment: "过期时间"),
                    revoked_at = table.Column<DateTime>(type: "TEXT", nullable: true, comment: "吊销时间"),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, comment: "创建时间"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true, comment: "最后更新时间"),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false, comment: "是否删除")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_api_keys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_api_keys_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "API Key");

            migrationBuilder.CreateTable(
                name: "execution_dedup",
                schema: "flow",
                columns: table => new
                {
                    idempotency_key = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false, comment: "幂等键"),
                    execution_id = table.Column<Guid>(type: "TEXT", nullable: false, comment: "执行记录 ID"),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, comment: "创建时间"),
                    expires_at = table.Column<DateTime>(type: "TEXT", nullable: true, comment: "过期时间")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_execution_dedup", x => x.idempotency_key);
                },
                comment: "执行幂等去重表");

            migrationBuilder.CreateIndex(
                name: "IX_api_keys_key_hash",
                schema: "flow",
                table: "api_keys",
                column: "key_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_api_keys_user_id",
                schema: "flow",
                table: "api_keys",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_execution_dedup_idempotency_key",
                schema: "flow",
                table: "execution_dedup",
                column: "idempotency_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_keys",
                schema: "flow");

            migrationBuilder.DropTable(
                name: "execution_dedup",
                schema: "flow");

            migrationBuilder.AlterTable(
                name: "project_members",
                schema: "flow",
                comment: "项目成员",
                oldComment: "项目成员（历史兼容）");
        }
    }
}
