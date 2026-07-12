using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowEngine.Migrations.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class ClearIncompatibleWorkflowData : Migration
    {
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // DSL 重构：NodeDefinition.Id → string, PositionX/Y → int?, Connection.SourcePortName/TargetPortName → string?
        // 旧 JSON 数据中的 Guid ID 和必填位置/端口名称与新模型不兼容，需清空。
        // 如果旧数据需要保留，请先通过导出 API 备份后再执行此迁移。
        // SQLite 不支持 schema 前缀，表名直接使用 workflows（不含 flow. 前缀）。
        // 对于 PostgreSQL/MySQL 请使用 provider-specific migration。
        migrationBuilder.Sql("""
            UPDATE workflows
            SET nodes = '[]',
                connections = '[]'
            WHERE nodes IS NOT NULL AND nodes != '[]';
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // 不可逆：旧 JSON 数据已清空，无法恢复。
        // 如需回滚，请在执行此迁移前从备份恢复。
    }
    }
}
