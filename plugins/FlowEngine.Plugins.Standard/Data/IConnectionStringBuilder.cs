using System.Collections.Generic;

namespace FlowEngine.Plugins.Standard.Data;

/// <summary>
/// 按方言从结构化凭据字段生成 ADO.NET 连接字符串。
/// 类比 <see cref="IDbSqlGenerator"/> 按方言生成 SQL，这里按方言生成连接串，
/// 方言相关的字段名映射与拼接规则集中在本层，不让凭据直接持有最终连接串。
/// </summary>
public interface IConnectionStringBuilder
{
    /// <summary>
    /// 该生成器对应的数据库方言。
    /// </summary>
    DbDialect Dialect { get; }

    /// <summary>
    /// 从凭据字段生成连接字符串。
    /// </summary>
    /// <param name="fields">凭据字段（小写键：host/port/database/userid/password/ssl/dataSource 等）。</param>
    /// <returns>可供 <see cref="DbConnectionFactory"/> 使用的连接字符串。</returns>
    /// <exception cref="System.InvalidOperationException">必填字段缺失或字段值非法时抛出。</exception>
    string Build(IReadOnlyDictionary<string, string> fields);
}
