using System.Globalization;

namespace FlowEngine.Plugins.Standard.Data;

/// <summary>
/// SQL 语句扫描器（CQ-2）：将 <c>HasTrailingStatement</c> / <c>ExtractFirstKeyword</c> /
/// <c>ContainsKeyword</c> 三处重复的注释/引号跳过逻辑收敛为单一扫描原语，避免漂移。
/// <para>
/// 扫描时跳过行注释（<c>--</c>）、块注释（<c>/* */</c>）与引号字符串（单/双引号，
/// 双引号内支持反斜杠转义），仅产出标识符与标点符号两类 token。
/// 该逻辑与 SEC-0 只读校验共用，确保注入防护一致。
/// </para>
/// </summary>
internal static class SqlStatementScanner
{
    /// <summary>
    /// 枚举 SQL 中的有效 token：跳过注释与引号字符串后，逐个产出标识符（字母/数字/下划线）
    /// 或非标识符散字符号（如 <c>;</c>）。用于关键字提取与堆叠语句检测。
    /// </summary>
    public static IEnumerable<SqlToken> EnumerateTokens(string sql)
    {
        var length = sql.Length;
        for (var i = 0; i < length; i++)
        {
            var c = sql[i];

            // 行注释 -- ：跳到行尾。
            if (c == '-' && i + 1 < length && sql[i + 1] == '-')
            {
                while (i < length && sql[i] != '\n') i++;
                continue;
            }

            // 块注释 /* ... */ ：跳到注释结束。
            if (c == '/' && i + 1 < length && sql[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < length && (sql[i] != '*' || sql[i + 1] != '/')) i++;
                i += 2;
                continue;
            }

            // 引号字符串（双引号支持反斜杠转义）：内容不产出 token。
            if (c == '\'' || c == '"')
            {
                var quote = c;
                i++;
                while (i < length && sql[i] != quote)
                {
                    if (sql[i] == '\\' && quote == '"') i++;
                    i++;
                }

                continue;
            }

            // 标识符（字母/数字/下划线）聚合为单个 token。
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                var start = i;
                while (i < length && (char.IsLetterOrDigit(sql[i]) || sql[i] == '_')) i++;
                yield return new SqlToken(SqlTokenKind.Word, sql.Substring(start, i - start));
                // 上面 i 已越过标识符末尾，循环末尾的 i++ 会多移一位，需回退。
                i--;
                continue;
            }

            // 其它散字符号（如 ';'）作为标点 token 产出。
            yield return new SqlToken(SqlTokenKind.Punctuation, c.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// 提取首个关键字（跳过空白与注释后的第一个标识符）。无可识别关键字时返回 null。
    /// </summary>
    public static string? ExtractFirstKeyword(string sql)
    {
        foreach (var token in EnumerateTokens(sql))
        {
            if (token.Kind == SqlTokenKind.Word)
            {
                return token.Text;
            }

            // 在非注释/引号位置遇到非标识符符号（如 '('、';'）即视为无关键字。
            return null;
        }

        return null;
    }

    /// <summary>
    /// 判断是否存在堆叠语句：注释/引号之外出现任何 ';' 即视为存在后续语句（与原实现一致，
    /// 即便 ';' 位于语句末尾也返回 true，由 <see cref="IsReadOnlyStatement"/> 据此拒绝）。
    /// </summary>
    public static bool HasTrailingStatement(string sql)
    {
        foreach (var token in EnumerateTokens(sql))
        {
            if (token.Kind == SqlTokenKind.Punctuation && token.Text == ";")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 判断语句是否包含指定关键字（词边界匹配，大小写不敏感）。跳过注释与引号字符串。
    /// 与原始实现一致：注释中的关键字不会被匹配（扫描原语已跳过注释）。
    /// </summary>
    public static bool ContainsKeyword(string sql, string keyword)
    {
        var target = keyword.ToUpperInvariant();
        foreach (var token in EnumerateTokens(sql))
        {
            if (token.Kind == SqlTokenKind.Word
                && string.Equals(token.Text, target, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// SQL token 种类与文本。
/// </summary>
internal readonly record struct SqlToken(SqlTokenKind Kind, string Text);

/// <summary>
/// SQL token 种类。
/// </summary>
internal enum SqlTokenKind
{
    /// <summary>标识符/关键字。</summary>
    Word,

    /// <summary>散字符号（如 ';'）。</summary>
    Punctuation,
}
