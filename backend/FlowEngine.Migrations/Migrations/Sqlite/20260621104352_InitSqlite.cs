using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowEngine.Migrations.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class InitSqlite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "flow");

            migrationBuilder.CreateTable(
                name: "Credentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", maxLength: 36, nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    Data = table.Column<string>(type: "json", nullable: false),
                    KeyVersion = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, comment: "创建时间"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true, comment: "最后更新时间"),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false, comment: "是否删除")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Credentials", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "execution_records",
                schema: "flow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", maxLength: 36, nullable: false),
                    workflow_definition_id = table.Column<Guid>(type: "TEXT", nullable: false, comment: "工作流定义 ID"),
                    parent_execution_id = table.Column<Guid>(type: "TEXT", nullable: true, comment: "父执行 ID"),
                    started_at = table.Column<DateTime>(type: "TEXT", nullable: false, comment: "开始时间"),
                    completed_at = table.Column<DateTime>(type: "TEXT", nullable: true, comment: "完成时间"),
                    status = table.Column<int>(type: "INTEGER", nullable: false, comment: "执行状态"),
                    node_records = table.Column<string>(type: "json", nullable: false, comment: "节点执行记录列表"),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, comment: "创建时间"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true, comment: "最后更新时间"),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false, comment: "是否删除")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_execution_records", x => x.Id);
                },
                comment: "执行记录");

            migrationBuilder.CreateTable(
                name: "triggers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", maxLength: 36, nullable: false),
                    workflow_definition_id = table.Column<Guid>(type: "TEXT", nullable: false, comment: "关联工作流定义 ID"),
                    workflow_version = table.Column<int>(type: "INTEGER", nullable: false, comment: "工作流版本号"),
                    type = table.Column<int>(type: "INTEGER", nullable: false, comment: "触发器类型"),
                    name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false, comment: "触发器名称"),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false, comment: "是否激活"),
                    settings = table.Column<string>(type: "json", nullable: false, comment: "触发器配置"),
                    last_triggered_at = table.Column<DateTime>(type: "TEXT", nullable: true, comment: "最后触发时间"),
                    next_trigger_at = table.Column<DateTime>(type: "TEXT", nullable: true, comment: "下次触发时间"),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, comment: "创建时间"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true, comment: "最后更新时间"),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false, comment: "是否删除")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_triggers", x => x.Id);
                },
                comment: "触发器");

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

            migrationBuilder.CreateTable(
                name: "webhook_routes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", maxLength: 36, nullable: false),
                    path = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false, comment: "Webhook 路径"),
                    method = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false, comment: "HTTP 方法"),
                    workflow_definition_id = table.Column<Guid>(type: "TEXT", nullable: false, comment: "关联工作流定义 ID"),
                    trigger_id = table.Column<Guid>(type: "TEXT", nullable: false, comment: "触发器 ID"),
                    is_static = table.Column<bool>(type: "INTEGER", nullable: false, comment: "是否静态路由"),
                    secret = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true, comment: "签名密钥"),
                    allowed_ips = table.Column<string>(type: "TEXT", nullable: true, comment: "IP 白名单 JSON"),
                    allowed_origins = table.Column<string>(type: "TEXT", nullable: true, comment: "来源域白名单 JSON"),
                    is_sync = table.Column<bool>(type: "INTEGER", nullable: false, comment: "是否同步响应"),
                    max_wait_seconds = table.Column<int>(type: "INTEGER", nullable: false, comment: "同步响应最大等待时间（秒）"),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, comment: "创建时间"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true, comment: "最后更新时间"),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false, comment: "是否删除")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_routes", x => x.Id);
                },
                comment: "Webhook 路由");

            migrationBuilder.CreateTable(
                name: "workflows",
                schema: "flow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", maxLength: 36, nullable: false),
                    project_id = table.Column<Guid>(type: "TEXT", nullable: true, comment: "项目 ID"),
                    name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false, comment: "工作流名称"),
                    version = table.Column<int>(type: "INTEGER", nullable: false, comment: "版本号"),
                    created_by = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false, comment: "创建人"),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false, comment: "是否激活"),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, comment: "创建时间"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true, comment: "最后更新时间"),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false, comment: "是否删除")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflows", x => x.Id);
                },
                comment: "工作流定义");

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
                name: "Credentials");

            migrationBuilder.DropTable(
                name: "execution_records",
                schema: "flow");

            migrationBuilder.DropTable(
                name: "triggers");

            migrationBuilder.DropTable(
                name: "user_roles");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "webhook_routes");

            migrationBuilder.DropTable(
                name: "workflows",
                schema: "flow");
        }
    }
}
