# Code Review：FlowEngine.Migrations 实现评审

## 总结

实现整体遵循计划，结构正确。但发现 **1 个严重缺陷** 和若干次要问题需要修复。

---

## 严重缺陷：SQLite 迁移缺少 `settings` 列

**文件**：`backend/FlowEngine.Migrations/Migrations/Sqlite/20260621102331_InitSqlite.cs`

`Trigger` 实体（`backend/FlowEngine.Core/entities/Trigger.cs:58-61`）定义了 `Settings` 属性：

```csharp
[Column("settings")]
[Comment("触发器配置")]
[JsonColumn]
public TriggerSettings Settings { get; set; } = new();
```

但生成的迁移中 `triggers` 表**缺少** `settings` 列。迁移只包含：`Id`、`workflow_definition_id`、`workflow_version`、`type`、`name`、`is_active`、`last_triggered_at`、`next_trigger_at`、`CreatedAt`、`UpdatedAt`、`Deleted`。

**影响**：运行时访问 `trigger.Settings`（如 `Program.cs:245-246` 读取 `settings?.CronExpression`）会因列不存在而报错。

**修复**：重新生成 SQLite 迁移，或手动添加缺失列：

```csharp
migrationBuilder.AddColumn<string>(
    name: "settings",
    table: "triggers",
    type: "json",
    nullable: false,
    comment: "触发器配置");
```

---

## 次要问题

### 1. NuGet 包版本与计划不一致

| 包名 | 计划版本 | 实际版本 | 状态 |
|------|---------|---------|------|
| `Npgsql.EntityFrameworkCore.PostgreSQL` | `10.0.0` | `10.0.2` | OK，更新版本 |
| `MySql.EntityFrameworkCore` | `Pomelo.EntityFrameworkCore.MySql 10.0.0` | `MySql.EntityFrameworkCore 10.0.7` | **换成了 Oracle 官方包** |
| `DM.Microsoft.EntityFrameworkCore` | `Dm.EntityFrameworkCore 1.1.0` | `DM.Microsoft.EntityFrameworkCore 9.0.0.43760` | **换成了 DM 官方包** |

代码使用 `MySql.EntityFrameworkCore`（Oracle 官方包）替代了计划中的 `Pomelo.EntityFrameworkCore.MySql`（社区包）。API 略有差异：
- 扩展方法来自 `MySql.EntityFrameworkCore.Extensions`（两个文件中均已正确引用）
- Pomelo 包对 TiDB/OceanBase 使用 `ServerVersion.AutoDetect()`；Oracle 包的 `UseMySQL()` 有仅接受连接字符串的重载，应可正常工作

### 2. `DesignTimeDbContextFactory` 中 `MigrationsAssembly` 冗余

`DesignTimeDbContextFactory.cs` 中每个 provider 都配置了 `MigrationsAssembly("FlowEngine.Migrations")`。但 `dotnet ef` 执行设计时工厂时，迁移程序集就是当前项目本身，`MigrationsAssembly` 指向自身是多余的。不影响功能，但属于无意义代码。

### 3. Snapshot 冲突（计划第 3.5 节）

目前只生成了 SQLite 迁移。后续为 Postgres、MySQL、Dameng 生成迁移时，`FlowEngineDbContextModelSnapshot.cs` 会冲突（每个 provider 各生成一份）。计划推荐"方案 A"（用 `--no-snapshot` 跳过），初始迁移可行，但后续增量迁移需要重新评估。

### 4. 测试尚未实现

计划第 5 节的测试策略未实现，`tests/` 目录下无迁移相关测试文件。

---

## 实现正确的部分

- 项目结构符合计划
- `FlowEngine.sln` 正确注册新项目
- `FlowEngine.Host.csproj` 引用 `FlowEngine.Migrations`
- `Program.cs` 正确配置多数据库 switch + `MigrationsAssembly`
- `appsettings.json` 包含 `Database:Provider` 配置
- `MigrationsExtensions.cs` 正确在启动时检测并执行待处理迁移，含日志
- SQLite WAL 模式在迁移后正确启用
- 迁移覆盖全部 7 张表（除缺失的 `settings` 列外）
- `user_roles(UserId, Role)` 和 `users(Email)` 唯一索引正确

---

## 建议操作

1. **修复严重缺陷**：重新生成 SQLite 迁移，确保包含 `settings` 列
2. **验证 NuGet 包**：确认 `MySql.EntityFrameworkCore 10.0.7` 和 `DM.Microsoft.EntityFrameworkCore 9.0.0.43760` 与 .NET 10 兼容
3. **补充测试**：实现计划第 5.1 节的 SQLite 迁移测试
4. **生成其他数据库迁移**：Postgres、MySQL、Dameng（计划步骤 5）
