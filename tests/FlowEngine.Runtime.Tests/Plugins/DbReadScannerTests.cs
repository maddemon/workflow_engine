using System.Reflection;
using FlowEngine.Plugins.Standard;
using FlowEngine.Plugins.Standard.Data;
using Xunit;

namespace FlowEngine.Runtime.Tests.Plugins;

/// <summary>
/// CQ-2 回归测试：DbReadNode 的三个只读校验方法（首关键字 / INTO / 尾随语句）现已统一复用
/// 单一 SQL 词法扫描器 <see cref="SqlStatementScanner"/>。本测试直接调用方法，锁定注入防护语义不被重构破坏。
/// </summary>
public sealed class DbReadScannerTests
{
    private static readonly MethodInfo IsReadOnlyMethod = typeof(DbReadNode).GetMethod("IsReadOnlyStatement", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo HasTrailingMethod = typeof(SqlStatementScanner).GetMethod("HasTrailingStatement", BindingFlags.Public | BindingFlags.Static)!;
    private static readonly MethodInfo ContainsKw = typeof(SqlStatementScanner).GetMethod("ContainsKeyword", BindingFlags.Public | BindingFlags.Static)!;

    private static bool IsReadOnly(string sql) => (bool)IsReadOnlyMethod.Invoke(null, new object[] { sql })!;
    private static bool HasTrailing(string sql) => (bool)HasTrailingMethod.Invoke(null, new object[] { sql })!;
    private static bool Contains(string sql, string kw) => (bool)ContainsKw.Invoke(null, new object[] { sql, kw })!;

    [Theory]
    [InlineData("SELECT * FROM users")]
    [InlineData("select 1")]
    [InlineData("WITH cte AS (SELECT 1) SELECT * FROM cte")]
    public void IsReadOnlyStatement_AllowsSelectWith(string sql) => Assert.True(IsReadOnly(sql));

    [Theory]
    [InlineData("INSERT INTO users VALUES (1)")]
    [InlineData("UPDATE users SET x = 1")]
    [InlineData("DELETE FROM users")]
    [InlineData("DROP TABLE users")]
    [InlineData("MERGE INTO t USING s ON 1=1 WHEN MATCHED THEN DELETE")]
    public void IsReadOnlyStatement_RejectsNonSelectWith(string sql) => Assert.False(IsReadOnly(sql));

    [Fact]
    public void IsReadOnlyStatement_RejectsSelectInto() => Assert.False(IsReadOnly("SELECT * INTO other FROM users"));

    [Fact]
    public void IsReadOnlyStatement_RejectsStackedStatement() => Assert.False(IsReadOnly("SELECT 1; DROP TABLE users"));

    [Fact]
    public void IsReadOnlyStatement_RejectsLeadingSemicolon() => Assert.False(IsReadOnly("; SELECT 1"));

    [Fact]
    public void HasTrailingStatement_DetectsSemicolon() => Assert.True(HasTrailing("SELECT 1; DROP"));

    [Fact]
    public void HasTrailingStatement_IgnoresSemicolonInStringLiteral() => Assert.False(HasTrailing("SELECT ';' AS x"));

    [Fact]
    public void HasTrailingStatement_IgnoresSemicolonInBlockComment() => Assert.False(HasTrailing("SELECT 1 /* ; DROP */ FROM users"));

    [Fact]
    public void ContainsKeyword_FindsWordBoundary() => Assert.True(Contains("SELECT INTO x", "INTO"));

    [Fact]
    public void ContainsKeyword_IgnoresSubstring() => Assert.False(Contains("SELECT INToX FROM x", "INTO"));
}