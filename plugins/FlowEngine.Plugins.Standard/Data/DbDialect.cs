using System.ComponentModel;

namespace FlowEngine.Plugins.Standard.Data;

/// <summary>
/// 支持的数据库方言。
/// </summary>
public enum DbDialect
{
    /// <summary>PostgreSQL</summary>
    [Description("PostgreSQL")]
    PostgreSQL,

    /// <summary>MySQL</summary>
    [Description("MySQL")]
    MySQL,

    /// <summary>SQL Server</summary>
    [Description("SQL Server")]
    SqlServer,

    /// <summary>SQLite</summary>
    [Description("SQLite")]
    SQLite
}
