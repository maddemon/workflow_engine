# FlowEngine.Migrations 迁移项目实现计划

## 1. 目标

创建独立的 `FlowEngine.Migrations` 项目，用于：
- 为不同数据库生成迁移文件
- 启动时自动检测并执行迁移
- 支持 SQLite、PostgreSQL、MySQL、达梦、TiDB、OceanBase、KingbaseES

> **迁移模式**：当前仅支持自动迁移（启动时自动执行）。手动迁移 CLI 工具（`flowengine-migrate`）作为后续规划，用于 TiDB/OceanBase 等需要 SQL 审查的场景。
>
> **当前阶段**：项目仍处于开发阶段，无历史迁移兼容负担。旧迁移文件（`FlowEngine.Infrastructure/Migrations/`）已删除，新项目以当前 schema 为基准生成初始迁移。

## 2. 当前状态分析

### 现有结构
- `FlowEngine.Core` 包含 `FlowEngineDbContext` 和实体定义
- `FlowEngine.Infrastructure` 引用 EF Core Design 和 SQLite provider（迁移文件已删除）
- `FlowEngine.Host` 中硬编码使用 SQLite
- 旧的迁移文件已删除，从零开始

### 数据库表（基于当前实体 `[Table]` 属性）

| 表名 | Schema | 实体 | 备注 |
|------|--------|------|------|
| `workflows` | `flow` | Workflow | SQLite 忽略 schema，实际表名 `workflows` |
| `execution_records` | `flow` | ExecutionRecord | 同上 |
| `Credentials` | — | Credential | 无 `[Table]` 属性，使用 DbSet 名 |
| `triggers` | — | Trigger | |
| `webhook_routes` | — | WebhookRoute | |
| `users` | — | User | |
| `user_roles` | — | UserRole | |

> `NodeExecutionRecord` 标记为 `[NotMapped]`，作为 JSON 存储在 `ExecutionRecord.NodeRecords` 中，不生成独立表。

### Schema 跨数据库行为

`Workflow` 和 `ExecutionRecord` 使用了 `Schema = "flow"`：
- **PostgreSQL / 达梦 / KingbaseES**：创建 `flow` schema，表在 `flow.workflows` 下
- **SQLite**：不支持 schema，EF Core SQLite provider 会**静默忽略** schema，表名为 `workflows`
- **MySQL / TiDB / OceanBase**：不支持 schema 概念（MySQL 中 schema = database），EF Core 的 MySQL provider 也会忽略 schema

这意味着**同一套实体在不同数据库下的表名/schema 会有差异**，这是 EF Core 的默认行为，无需额外处理，但需要在文档中明确说明。

## 3. 实现方案

### 3.1 项目结构

```
FlowEngine.sln
├── backend/
│   ├── FlowEngine.Core/           # DbContext + 实体（已有）
│   ├── FlowEngine.Migrations/     # 新建：迁移项目
│   │   ├── FlowEngine.Migrations.csproj
│   │   ├── DesignTimeDbContextFactory.cs
│   │   ├── Migrations/
│   │   │   ├── Sqlite/            # SQLite 迁移
│   │   │   ├── Postgres/          # PostgreSQL 迁移
│   │   │   ├── Mysql/             # MySQL / TiDB / OceanBase 迁移
│   │   │   ├── Dameng/            # 达梦迁移
│   │   │   └── KingbaseES/        # KingbaseES 迁移（可选，见 3.10）
│   │   └── MigrationsExtensions.cs
│   ├── FlowEngine.Infrastructure/
│   ├── FlowEngine.Host/
│   └── ...
```

### 3.2 新建 FlowEngine.Migrations.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\FlowEngine.Core\FlowEngine.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <!-- 设计时工具 -->
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.9">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    
    <!-- SQLite -->
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.9" />
    
    <!-- PostgreSQL (Npgsql) -->
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.0" />
    
    <!-- MySQL (Pomelo - 兼容 TiDB, OceanBase MySQL 模式) -->
    <PackageReference Include="Pomelo.EntityFrameworkCore.MySql" Version="10.0.0" />
    
    <!-- 达梦数据库 -->
    <PackageReference Include="Dm.EntityFrameworkCore" Version="1.1.0" />
    
    <!-- KingbaseES：使用 Npgsql 兼容驱动（见 3.10 节说明） -->
  </ItemGroup>
</Project>
```

> **前置验证**：实施步骤 1 之前，需要先验证以上 NuGet 包版本是否存在。特别是 `Npgsql.EntityFrameworkCore.PostgreSQL`、`Pomelo.EntityFrameworkCore.MySql` 和 `Dm.EntityFrameworkCore` 对 .NET 10 的支持情况。如版本不存在，需查找对应 .NET 10 的最新版本。

### 3.3 DesignTimeDbContextFactory 实现

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using FlowEngine.Core.Data;

namespace FlowEngine.Migrations;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<FlowEngineDbContext>
{
    public FlowEngineDbContext CreateDbContext(string[] args)
    {
        var provider = GetProviderFromArgs(args);
        var optionsBuilder = new DbContextOptionsBuilder<FlowEngineDbContext>();
        
        ConfigureProvider(optionsBuilder, provider);
        
        return new FlowEngineDbContext(optionsBuilder.Options);
    }
    
    private static string GetProviderFromArgs(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--provider")
                return args[i + 1];
        }
        
        return Environment.GetEnvironmentVariable("FLOWENGINE_DB_PROVIDER") ?? "sqlite";
    }
    
    private static void ConfigureProvider(
        DbContextOptionsBuilder<FlowEngineDbContext> builder, 
        string provider)
    {
        var connectionString = Environment.GetEnvironmentVariable("FLOWENGINE_CONNECTION_STRING");
        
        switch (provider.ToLowerInvariant())
        {
            case "sqlite":
                connectionString ??= "Data Source=flowengine.db";
                builder.UseSqlite(connectionString, x =>
                    x.MigrationsAssembly("FlowEngine.Migrations")
                     .MigrationsHistoryTable("__ef_migrations_history"));
                break;
                
            case "postgresql":
            case "npgsql":
                connectionString ??= "Host=localhost;Database=flowengine;Username=postgres;Password=password";
                builder.UseNpgsql(connectionString, x =>
                    x.MigrationsAssembly("FlowEngine.Migrations")
                     .MigrationsHistoryTable("__ef_migrations_history", "flow"));
                break;
                
            case "mysql":
            case "pomelo":
                connectionString ??= "Server=localhost;Database=flowengine;User=root;Password=password";
                var serverVersion = new MySqlServerVersion(new Version(8, 0, 0));
                builder.UseMySql(connectionString, serverVersion, x =>
                    x.MigrationsAssembly("FlowEngine.Migrations")
                     .MigrationsHistoryTable("__ef_migrations_history"));
                break;
                
            case "tidb":
                connectionString ??= "Server=localhost;Database=flowengine;User=root;Password=";
                builder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString), x =>
                    x.MigrationsAssembly("FlowEngine.Migrations")
                     .MigrationsHistoryTable("__ef_migrations_history"));
                break;
                
            case "oceanbase":
                connectionString ??= "Server=localhost;Database=flowengine;User=root;Password=password";
                builder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString), x =>
                    x.MigrationsAssembly("FlowEngine.Migrations")
                     .MigrationsHistoryTable("__ef_migrations_history"));
                break;
                
            case "dameng":
            case "dm":
                connectionString ??= "Server=localhost;User Id=SYSDBA;Password=SYSDBA;Port=5236";
                builder.UseDm(connectionString, x =>
                    x.MigrationsAssembly("FlowEngine.Migrations")
                     .MigrationsHistoryTable("__ef_migrations_history"));
                break;
                
            case "kingbasees":
            case "kingbase":
                connectionString ??= "Host=localhost;Database=flowengine;Username=system;Password=123456";
                builder.UseNpgsql(connectionString, x =>
                    x.MigrationsAssembly("FlowEngine.Migrations")
                     .MigrationsHistoryTable("__ef_migrations_history", "flow"));
                break;
                
            default:
                throw new ArgumentException($"Unsupported database provider: {provider}");
        }
    }
}
```

### 3.4 迁移文件组织

每个数据库的迁移文件放在独立文件夹中，**命名需包含数据库后缀以避免类名冲突**：

```
Migrations/
├── Sqlite/
│   ├── 20260621000000_InitSqlite.cs
│   ├── 20260621000000_InitSqlite.Designer.cs
│   └── FlowEngineDbContextModelSnapshot.cs
├── Postgres/
│   ├── 20260621000000_InitPostgres.cs
│   ├── 20260621000000_InitPostgres.Designer.cs
│   └── FlowEngineDbContextModelSnapshot.cs
├── Mysql/
│   ├── 20260621000000_InitMysql.cs
│   ├── 20260621000000_InitMysql.Designer.cs
│   └── FlowEngineDbContextModelSnapshot.cs
├── Dameng/
│   ├── 20260621000000_InitDameng.cs
│   ├── 20260621000000_InitDameng.Designer.cs
│   └── FlowEngineDbContextModelSnapshot.cs
└── KingbaseES/           （可选，见 3.10）
    ├── 20260621000000_InitKingbaseES.cs
    ├── 20260621000000_InitKingbaseES.Designer.cs
    └── FlowEngineDbContextModelSnapshot.cs
```

> **注意**：每个子文件夹会有独立的 `FlowEngineDbContextModelSnapshot.cs`。EF Core 的 snapshot 是按 migrations assembly 全局唯一的，多 snapshot 文件会有编译冲突。解决方案见 3.5 节。

### 3.5 Snapshot 冲突处理

EF Core 每次生成迁移都会更新一个 `ModelSnapshot.cs`。多个数据库各自生成 snapshot 会冲突。处理方式：

**方案 A（推荐）**：每次生成迁移时，用 `--no-snapshot` 跳过 snapshot 生成（仅当迁移是新增表/列等幂等操作时可行）。但 EF Core 后续迁移依赖 snapshot 检测差异，此方案仅适用于"每次全量重新生成"的场景。

**方案 B**：为每个数据库维护独立的 migration assembly。即每种数据库一个 class library 项目（如 `FlowEngine.Migrations.Sqlite`、`FlowEngine.Migrations.Postgres` 等）。结构更清晰，但项目数量增加。

**方案 C**：在单一项目中，通过条件编译（`#if`）或文件夹组织避免 snapshot 冲突。生成迁移后手动将 snapshot 移到对应子文件夹并修改命名空间，运行时通过 `MigrationsAssembly` 指向同一程序集。

> **建议**：先用方案 A 验证可行性。如果后续迁移依赖 snapshot（如增删列、改名等增量迁移），再切换到方案 B。

### 3.6 迁移生成命令

```bash
# SQLite
dotnet ef migrations add InitSqlite \
  --project backend/FlowEngine.Migrations \
  --startup-project backend/FlowEngine.Host \
  --context FlowEngineDbContext \
  --output-dir Migrations/Sqlite

# PostgreSQL
dotnet ef migrations add InitPostgres \
  --project backend/FlowEngine.Migrations \
  --startup-project backend/FlowEngine.Host \
  --context FlowEngineDbContext \
  --output-dir Migrations/Postgres \
  -- --provider postgresql

# MySQL
dotnet ef migrations add InitMysql \
  --project backend/FlowEngine.Migrations \
  --startup-project backend/FlowEngine.Host \
  --context FlowEngineDbContext \
  --output-dir Migrations/Mysql \
  -- --provider mysql

# TiDB（复用 MySQL provider，独立迁移名以便区分）
dotnet ef migrations add InitTidb \
  --project backend/FlowEngine.Migrations \
  --startup-project backend/FlowEngine.Host \
  --context FlowEngineDbContext \
  --output-dir Migrations/Mysql \
  -- --provider tidb

# 达梦
dotnet ef migrations add InitDameng \
  --project backend/FlowEngine.Migrations \
  --startup-project backend/FlowEngine.Host \
  --context FlowEngineDbContext \
  --output-dir Migrations/Dameng \
  -- --provider dameng
```

> **注意**：`dotnet ef` 的 `--` 后面的参数会传递给 `DesignTimeDbContextFactory.CreateDbContext(string[] args)`，用于选择 provider。

### 3.7 迁移执行扩展方法

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using FlowEngine.Core.Data;

namespace FlowEngine.Migrations;

public static class MigrationsExtensions
{
    public static async Task ApplyFlowEngineMigrationsAsync(
        this IServiceProvider serviceProvider,
        string provider,
        ILogger? logger = null)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();
        
        try
        {
            var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync();
            
            if (pendingMigrations.Any())
            {
                logger?.LogInformation(
                    "检测到 {Count} 个待执行的数据库迁移", 
                    pendingMigrations.Count());
                
                await dbContext.Database.MigrateAsync();
                
                logger?.LogInformation("数据库迁移执行完成");
            }
            else
            {
                logger?.LogInformation("数据库已是最新状态，无需迁移");
            }
            
            // SQLite 专属优化
            if (provider.Equals("sqlite", StringComparison.OrdinalIgnoreCase))
            {
                await dbContext.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
                logger?.LogDebug("SQLite WAL 模式已启用");
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "数据库迁移执行失败");
            throw;
        }
    }
}
```

> **关于回滚**：EF Core 不提供内置的 `RollbackAsync` API。如果需要回滚，应通过以下方式：
> 1. 生成 SQL 脚本审查：`dotnet ef migrations script <From> <To>`
> 2. 手动执行迁移的 `Down()` 方法生成的 SQL
> 3. 在 CI/CD 中集成迁移前的数据库备份
>
> 本方案不提供自动回滚方法，避免误导。

### 3.8 修改 Program.cs

```csharp
// 替换原来的:
// builder.Services.AddDbContext<FlowEngineDbContext>(options =>
//     options.UseSqlite(builder.Configuration.GetConnectionString("Default")));

var dbProvider = builder.Configuration["Database:Provider"] ?? "sqlite";
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

builder.Services.AddDbContext<FlowEngineDbContext>(options =>
{
    switch (dbProvider.ToLowerInvariant())
    {
        case "sqlite":
            options.UseSqlite(connectionString, x =>
                x.MigrationsAssembly("FlowEngine.Migrations")
                 .MigrationsHistoryTable("__ef_migrations_history"));
            break;
        case "postgresql":
        case "npgsql":
            options.UseNpgsql(connectionString, x =>
                x.MigrationsAssembly("FlowEngine.Migrations")
                 .MigrationsHistoryTable("__ef_migrations_history", "flow"));
            break;
        case "mysql":
        case "pomelo":
            options.UseMySql(connectionString,
                new MySqlServerVersion(new Version(8, 0, 0)), x =>
                x.MigrationsAssembly("FlowEngine.Migrations")
                 .MigrationsHistoryTable("__ef_migrations_history"));
            break;
        case "tidb":
            options.UseMySql(connectionString,
                ServerVersion.AutoDetect(connectionString), x =>
                x.MigrationsAssembly("FlowEngine.Migrations")
                 .MigrationsHistoryTable("__ef_migrations_history"));
            break;
        case "oceanbase":
            options.UseMySql(connectionString,
                ServerVersion.AutoDetect(connectionString), x =>
                x.MigrationsAssembly("FlowEngine.Migrations")
                 .MigrationsHistoryTable("__ef_migrations_history"));
            break;
        case "dameng":
        case "dm":
            options.UseDm(connectionString, x =>
                x.MigrationsAssembly("FlowEngine.Migrations")
                 .MigrationsHistoryTable("__ef_migrations_history"));
            break;
        case "kingbasees":
        case "kingbase":
            options.UseNpgsql(connectionString, x =>
                x.MigrationsAssembly("FlowEngine.Migrations")
                 .MigrationsHistoryTable("__ef_migrations_history", "flow"));
            break;
        default:
            throw new ArgumentException($"Unsupported database provider: {dbProvider}");
    }
});

// 替换原来的自动迁移块:
// using (var scope = app.Services.CreateScope())
// {
//     var dbContext = scope.ServiceProvider.GetRequiredService<FlowEngineDbContext>();
//     dbContext.Database.Migrate();
//     dbContext.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
// }

// 改为:
await app.Services.ApplyFlowEngineMigrationsAsync(
    dbProvider,
    app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("FlowEngine.Migrations"));
```

> **关键**：`MigrationsAssembly("FlowEngine.Migrations")` 在 Program.cs 和 DesignTimeDbContextFactory 中**必须一致**，否则 EF Core 找不到迁移文件。

### 3.9 appsettings.json 更新

```json
{
  "Database": {
    "Provider": "sqlite"
  },
  "ConnectionStrings": {
    "Default": "Data Source=flowengine.db;Mode=ReadWriteCreate;Cache=Shared"
  }
}
```

各数据库连接字符串示例：

| Provider | ConnectionString 示例 |
|----------|----------------------|
| `sqlite` | `Data Source=flowengine.db;Mode=ReadWriteCreate;Cache=Shared` |
| `postgresql` | `Host=localhost;Port=5432;Database=flowengine;Username=postgres;Password=secret` |
| `mysql` | `Server=localhost;Port=3306;Database=flowengine;User=root;Password=secret` |
| `tidb` | `Server=localhost;Port=4000;Database=flowengine;User=root;Password=` |
| `oceanbase` | `Server=localhost;Port=2881;Database=flowengine;User=root@mysql_tenant;Password=secret` |
| `dameng` | `Server=localhost;Port=5236;User Id=SYSDBA;Password=SYSDBA` |
| `kingbasees` | `Host=localhost;Port=54321;Database=flowengine;Username=system;Password=secret` |

### 3.10 KingbaseES 迁移策略

KingbaseES 基于 PostgreSQL 内核，Npgsql 驱动可直接连接。策略如下：

- **默认复用 PostgreSQL 迁移**：大多数 DDL（CREATE TABLE、ALTER TABLE）语法兼容，直接使用 `Migrations/Postgres/` 下的迁移文件
- **需要独立迁移的场景**：如果使用了 PostgreSQL 特有功能（如 `jsonb` 操作符、序列、`ON CONFLICT` 等），而 KingbaseES 版本不完全兼容时，需单独生成 `Migrations/KingbaseES/` 迁移
- **实施建议**：初始版本复用 PostgreSQL 迁移，在测试环境验证 KingbaseES 兼容性后再决定是否拆分

### 3.11 TiDB / OceanBase 说明

TiDB 和 OceanBase（MySQL 模式）通过 Pomelo MySQL provider 连接，共用 `Migrations/Mysql/` 目录的迁移文件。

**TiDB**：
- 默认端口 `4000`（非 MySQL 的 `3306`）
- 不支持外键约束（TiDB 6.6+ 支持实验性外键，但建议关闭）
- 部分 MySQL DDL 不兼容（如 `ALTER TABLE ... ALGORITHM=INPLACE`）
- 建议使用 `ServerVersion.AutoDetect()` 自动检测版本

**OceanBase（MySQL 模式）**：
- 默认端口 `2881`
- 用户名格式为 `user@tenant`
- 基本兼容 MySQL 5.7/8.0 DDL，但部分高级特性不支持
- 同样使用 `ServerVersion.AutoDetect()`

**自动迁移兼容性**：

| 迁移类型 | TiDB | OceanBase |
|----------|------|-----------|
| CREATE TABLE（初始迁移） | 没问题 | 没问题 |
| ADD COLUMN | 没问题 | 没问题 |
| DROP COLUMN | 基本没问题 | 基本没问题 |
| ALTER COLUMN TYPE | 不支持 | 部分支持 |
| 修改主键 / 索引 | 可能有问题 | 可能有问题 |

> 初始迁移（CREATE TABLE）自动执行没有问题。后续增量迁移如涉及 ALTER TABLE 等不兼容操作，需等手动迁移 CLI 工具实现后，先用 `dotnet ef migrations script` 生成 SQL 人工审查再执行。当前开发阶段暂无此问题。

## 4. 实施步骤

### 步骤 0：验证 NuGet 包可用性
- 在 nuget.org 验证所有包是否存在对应 .NET 10 的版本
- 特别关注 `Dm.EntityFrameworkCore`（达梦官方包更新较慢）
- 记录实际可用版本号，更新 csproj

### 步骤 1：创建 FlowEngine.Migrations 项目
- 新建 `backend/FlowEngine.Migrations/` 目录
- 创建 `.csproj` 文件，添加验证过的包版本
- 在 `FlowEngine.sln` 中注册项目
- 添加对 `FlowEngine.Core` 的项目引用

### 步骤 2：实现 DesignTimeDbContextFactory
- 创建 `DesignTimeDbContextFactory.cs`
- 支持通过 `--provider` 参数和环境变量选择数据库
- 所有 provider 统一配置 `MigrationsAssembly`

### 步骤 3：修改 Program.cs
- 替换硬编码 SQLite 为多数据库 switch
- 所有 provider 配置 `MigrationsAssembly` 和 `MigrationsHistoryTable`
- 集成 `ApplyFlowEngineMigrationsAsync`
- 保留 SQLite PRAGMA WAL

### 步骤 4：更新 appsettings.json
- 添加 `Database:Provider` 配置节
- 更新 `ConnectionStrings:Default`

### 步骤 5：生成初始迁移
- 按 3.6 节命令为各数据库生成迁移
- 处理 snapshot 冲突（见 3.5 节）
- 验证生成的迁移 SQL 正确性

### 步骤 6：编写测试
- 迁移应用测试（见第 5 节）

### 步骤 7：更新文档
- 数据库配置指南
- 升级迁移指南

## 5. 测试策略

### 5.1 SQLite 迁移测试

```csharp
public class SqliteMigrationTests : IDisposable
{
    private readonly string _dbPath;

    public SqliteMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.db");
    }

    [Fact]
    public async Task Should_Apply_Migrations_To_Fresh_Database()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseSqlite($"Data Source={_dbPath}", x =>
                x.MigrationsAssembly("FlowEngine.Migrations"))
            .Options;

        using var context = new FlowEngineDbContext(options);
        
        // 使用 MigrateAsync 而非 EnsureCreatedAsync，以验证迁移逻辑本身
        await context.Database.MigrateAsync();

        // 验证表已创建
        var tables = await context.Database
            .SqlQueryRaw<string>(
                "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name != '__ef_migrations_history'")
            .ToListAsync();

        Assert.Contains("workflows", tables);
        Assert.Contains("execution_records", tables);
        Assert.Contains("Credentials", tables);
        Assert.Contains("triggers", tables);
        Assert.Contains("webhook_routes", tables);
        Assert.Contains("users", tables);
        Assert.Contains("user_roles", tables);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }
}
```

### 5.2 多数据库集成测试

使用 [Testcontainers](https://dotnet.testcontainers.org/) 运行真实的 PostgreSQL / MySQL 容器：

```csharp
public class PostgresMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .Build();

    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    [Fact]
    public async Task Should_Apply_Migrations_To_Postgres()
    {
        var options = new DbContextOptionsBuilder<FlowEngineDbContext>()
            .UseNpgsql(_container.GetConnectionString(), x =>
                x.MigrationsAssembly("FlowEngine.Migrations")
                 .MigrationsHistoryTable("__ef_migrations_history", "flow"))
            .Options;

        using var context = new FlowEngineDbContext(options);
        await context.Database.MigrateAsync();

        // 验证 flow schema 和表
        var schemas = await context.Database
            .SqlQueryRaw<string>("SELECT schema_name FROM information_schema.schemata WHERE schema_name = 'flow'")
            .ToListAsync();
        Assert.Contains("flow", schemas);
    }
}
```

## 6. 关键文件清单

| 文件 | 操作 | 说明 |
|------|------|------|
| `FlowEngine.Migrations/FlowEngine.Migrations.csproj` | 新建 | 迁移项目 |
| `FlowEngine.Migrations/DesignTimeDbContextFactory.cs` | 新建 | 设计时工厂，支持多 provider |
| `FlowEngine.Migrations/MigrationsExtensions.cs` | 新建 | 迁移执行扩展 |
| `FlowEngine.Migrations/Migrations/{Db}/*.cs` | 生成 | 各数据库的迁移文件 |
| `FlowEngine.Host/Program.cs` | 修改 | 多数据库 switch + MigrationsAssembly |
| `FlowEngine.Host/appsettings.json` | 修改 | 添加 Database:Provider 配置 |
| `FlowEngine.sln` | 修改 | 注册新项目 |
| `FlowEngine.Infrastructure/Migrations/` | 删除 | 旧迁移文件（已在开发分支删除） |

## 7. 验证方式

1. **编译验证**：`dotnet build` — 确保新项目编译通过
2. **迁移生成验证**：为每个数据库执行 `dotnet ef migrations add`，检查生成的 `.cs` 和 `.Designer.cs`
3. **SQL 脚本验证**：`dotnet ef migrations script --project FlowEngine.Migrations` 检查生成的 SQL 语法
4. **运行时验证**：启动应用，连接各数据库，验证表结构和基本 CRUD 操作
5. **测试验证**：运行迁移测试套件

---

**计划状态**：待审批
**预计工时**：2-3 天
