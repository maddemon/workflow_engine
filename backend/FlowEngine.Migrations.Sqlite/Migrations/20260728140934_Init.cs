using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowEngine.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "flow");

            migrationBuilder.CreateTable(
                name: "credentials",
                schema: "flow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    project_id = table.Column<Guid>(type: "TEXT", nullable: true, comment: "项目 ID"),
                    name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false, comment: "凭据名称"),
                    type = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false, comment: "凭据类型"),
                    data = table.Column<string>(type: "json", nullable: false, comment: "加密字段数据映射"),
                    key_version = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false, comment: "密钥版本"),
                    row_version = table.Column<long>(type: "INTEGER", nullable: false, comment: "乐观并发行版本"),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, comment: "创建时间"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true, comment: "最后更新时间"),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false, comment: "是否删除")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_credentials", x => x.Id);
                },
                comment: "凭据定义");

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

            migrationBuilder.CreateTable(
                name: "execution_records",
                schema: "flow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    workflow_definition_id = table.Column<Guid>(type: "TEXT", nullable: false, comment: "工作流定义 ID"),
                    project_id = table.Column<Guid>(type: "TEXT", nullable: true, comment: "项目 ID"),
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
                name: "projects",
                schema: "flow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false, comment: "项目名称"),
                    description = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true, comment: "项目描述"),
                    created_by = table.Column<string>(type: "TEXT", nullable: false, comment: "创建人"),
                    row_version = table.Column<long>(type: "INTEGER", nullable: false, comment: "乐观并发行版本"),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, comment: "创建时间"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true, comment: "最后更新时间"),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false, comment: "是否删除")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_projects", x => x.Id);
                },
                comment: "项目");

            migrationBuilder.CreateTable(
                name: "stored_files",
                schema: "flow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    file_name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false, comment: "文件名"),
                    content_type = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true, comment: "MIME 类型"),
                    size = table.Column<long>(type: "INTEGER", nullable: false, comment: "文件大小"),
                    storage_path = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false, comment: "存储路径"),
                    project_id = table.Column<Guid>(type: "TEXT", nullable: false, comment: "所属项目"),
                    uploaded_by = table.Column<Guid>(type: "TEXT", nullable: false, comment: "上传者"),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, comment: "创建时间"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true, comment: "最后更新时间"),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false, comment: "是否删除")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stored_files", x => x.Id);
                },
                comment: "存储文件");

            migrationBuilder.CreateTable(
                name: "triggers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    workflow_definition_id = table.Column<Guid>(type: "TEXT", nullable: false, comment: "关联工作流定义 ID"),
                    project_id = table.Column<Guid>(type: "TEXT", nullable: true, comment: "项目 ID"),
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
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
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
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
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
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
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

            migrationBuilder.CreateTable(
                name: "workflows",
                schema: "flow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    project_id = table.Column<Guid>(type: "TEXT", nullable: true, comment: "项目 ID"),
                    name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false, comment: "工作流名称"),
                    version = table.Column<int>(type: "INTEGER", nullable: false, comment: "版本号"),
                    created_by = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false, comment: "创建人"),
                    nodes = table.Column<string>(type: "json", nullable: false, comment: "节点实例列表"),
                    connections = table.Column<string>(type: "json", nullable: false, comment: "连接列表"),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false, comment: "是否激活"),
                    source = table.Column<int>(type: "INTEGER", nullable: false, comment: "工作流来源：人工创建或 AI 生成"),
                    draft_status = table.Column<int>(type: "INTEGER", nullable: true, comment: "草稿审查状态：待审查/已拒绝/已确认"),
                    rejection_reason = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true, comment: "拒绝理由"),
                    diff = table.Column<string>(type: "json", nullable: false, comment: "modify 草稿的结构化差异"),
                    style_settings = table.Column<string>(type: "json", nullable: true, comment: "样式设置"),
                    row_version = table.Column<long>(type: "INTEGER", nullable: false, comment: "乐观并发行版本"),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, comment: "创建时间"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true, comment: "最后更新时间"),
                    Deleted = table.Column<bool>(type: "INTEGER", nullable: false, comment: "是否删除")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflows", x => x.Id);
                },
                comment: "工作流定义");

            migrationBuilder.CreateTable(
                name: "api_keys",
                schema: "flow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
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
                name: "IX_credentials_name_null_project",
                schema: "flow",
                table: "credentials",
                column: "name",
                unique: true,
                filter: "\"project_id\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_credentials_name_project_id_notnull",
                schema: "flow",
                table: "credentials",
                columns: new[] { "name", "project_id" },
                unique: true,
                filter: "\"project_id\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_execution_dedup_idempotency_key",
                schema: "flow",
                table: "execution_dedup",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_execution_records_project_id",
                schema: "flow",
                table: "execution_records",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_execution_records_status_completed_at",
                schema: "flow",
                table: "execution_records",
                columns: new[] { "status", "completed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_execution_records_workflow_definition_id_started_at",
                schema: "flow",
                table: "execution_records",
                columns: new[] { "workflow_definition_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "IX_stored_files_project_id",
                schema: "flow",
                table: "stored_files",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_triggers_project_id",
                table: "triggers",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_triggers_workflow_definition_id",
                table: "triggers",
                column: "workflow_definition_id");

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

            migrationBuilder.CreateIndex(
                name: "IX_workflow_credential_usages_CredentialId",
                schema: "flow",
                table: "workflow_credential_usages",
                column: "CredentialId");

            migrationBuilder.CreateIndex(
                name: "IX_workflows_project_id",
                schema: "flow",
                table: "workflows",
                column: "project_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_keys",
                schema: "flow");

            migrationBuilder.DropTable(
                name: "credentials",
                schema: "flow");

            migrationBuilder.DropTable(
                name: "execution_dedup",
                schema: "flow");

            migrationBuilder.DropTable(
                name: "execution_records",
                schema: "flow");

            migrationBuilder.DropTable(
                name: "projects",
                schema: "flow");

            migrationBuilder.DropTable(
                name: "stored_files",
                schema: "flow");

            migrationBuilder.DropTable(
                name: "triggers");

            migrationBuilder.DropTable(
                name: "user_roles");

            migrationBuilder.DropTable(
                name: "webhook_routes");

            migrationBuilder.DropTable(
                name: "workflow_credential_usages",
                schema: "flow");

            migrationBuilder.DropTable(
                name: "workflows",
                schema: "flow");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
