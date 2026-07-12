using System.Collections.Generic;
using FlowEngine.Plugins.Standard.Data;
using Xunit;

namespace FlowEngine.Runtime.Tests.Plugins;

public sealed class ConnectionStringBuilderTests
{
    [Fact]
    public void Postgres_Build_ContainsExpectedKeys()
    {
        var fields = new Dictionary<string, string>
        {
            ["host"] = "localhost",
            ["port"] = "5432",
            ["database"] = "test",
            ["userid"] = "user",
            ["password"] = "pass",
            ["ssl"] = "Require"
        };

        var cs = ConnectionStringBuilderFactory.Get(DbDialect.PostgreSQL).Build(fields);

        Assert.Contains("Host=localhost", cs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Port=5432", cs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Database=test", cs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Username=user", cs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Password=pass", cs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SSL Mode=Require", cs, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MySql_Build_ContainsExpectedKeys()
    {
        var fields = new Dictionary<string, string>
        {
            ["host"] = "localhost",
            ["port"] = "3306",
            ["database"] = "test",
            ["userid"] = "user",
            ["password"] = "pass"
        };

        var cs = ConnectionStringBuilderFactory.Get(DbDialect.MySQL).Build(fields);

        Assert.Contains("Server=localhost", cs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Port=3306", cs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Database=test", cs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("User ID=user", cs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Password=pass", cs, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SqlServer_Build_WithPort_EmbedsPortInDataSource()
    {
        var fields = new Dictionary<string, string>
        {
            ["host"] = "localhost",
            ["port"] = "1433",
            ["database"] = "test",
            ["userid"] = "user",
            ["password"] = "pass",
            ["ssl"] = "true"
        };

        var cs = ConnectionStringBuilderFactory.Get(DbDialect.SqlServer).Build(fields);

        Assert.Contains("Data Source=localhost,1433", cs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Initial Catalog=test", cs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("User ID=user", cs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Password=pass", cs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Encrypt=True", cs, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SqlServer_Build_WithoutPort_OmitsPort()
    {
        var fields = new Dictionary<string, string>
        {
            ["host"] = "localhost",
            ["database"] = "test"
        };

        var cs = ConnectionStringBuilderFactory.Get(DbDialect.SqlServer).Build(fields);

        Assert.Contains("Data Source=localhost", cs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(",", cs);
    }

    [Fact]
    public void SQLite_Build_InMemory_ContainsDataSource()
    {
        var fields = new Dictionary<string, string>
        {
            ["dataSource"] = ":memory:",
            ["mode"] = "Memory",
            ["cache"] = "Shared"
        };

        var cs = ConnectionStringBuilderFactory.Get(DbDialect.SQLite).Build(fields);

        Assert.Contains("Data Source=:memory:", cs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Mode=Memory", cs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Cache=Shared", cs, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Postgres_Build_MissingHost_Throws()
    {
        var fields = new Dictionary<string, string> { ["database"] = "test" };

        var ex = Assert.Throws<System.InvalidOperationException>(
            () => ConnectionStringBuilderFactory.Get(DbDialect.PostgreSQL).Build(fields));
        Assert.Contains("host", ex.Message);
    }

    [Theory]
    [InlineData("postgresql", DbDialect.PostgreSQL)]
    [InlineData("postgres", DbDialect.PostgreSQL)]
    [InlineData("mysql", DbDialect.MySQL)]
    [InlineData("sqlserver", DbDialect.SqlServer)]
    [InlineData("mssql", DbDialect.SqlServer)]
    [InlineData("sqlite", DbDialect.SQLite)]
    public void ParseDbType_KnownAliases_ReturnsExpected(string dbType, DbDialect expected)
    {
        Assert.Equal(expected, DbDialectResolver.ParseDbType(dbType));
    }

    [Fact]
    public void ParseDbType_Empty_Throws()
    {
        Assert.Throws<System.InvalidOperationException>(() => DbDialectResolver.ParseDbType(null));
        Assert.Throws<System.InvalidOperationException>(() => DbDialectResolver.ParseDbType("  "));
    }

    [Fact]
    public void ParseDbType_Unknown_Throws()
    {
        Assert.Throws<System.InvalidOperationException>(() => DbDialectResolver.ParseDbType("oracle"));
    }
}
