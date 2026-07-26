using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlowEngine.Analyzers;
using FlowEngine.Core.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace FlowEngine.Runtime.Tests.Execution.Compliance;

/// <summary>
/// 编译期/静态合规检查：所有派生自 <see cref="NodeBase"/> 的节点不得引用“服务定位器/索取式” API
/// （如 <c>context.ErrorResult</c> / <c>GetParameter</c> / <c>HttpClientPool</c> 等）。
/// 非 NodeBase 节点（仍实现 INodeType）被排除，使测试在迁移过程中即可通过，并随迁移增多而扩大覆盖。
/// </summary>
public sealed class NodeApiComplianceTests
{
    /// <summary>禁止出现的标识符（精确匹配、区分大小写）。</summary>
    private static readonly HashSet<string> ForbiddenNames = new(StringComparer.Ordinal)
    {
        "GetParameter",
        "ErrorResult",
        "HttpClientPool",
        "NodeRegistry",
        "ContextFactory",
        "LlmClientFactory",
        "ScriptCache",
        "ResolveCredentialAsync",
        "ResolvedParameters",
        "RawParameters",
    };

    [Fact]
    public void NodeBase_DerivedNodes_DoNotReferenceForbiddenApis()
    {
        var pluginsDir = FindPluginsDir();
        var files = Directory.GetFiles(pluginsDir, "*.cs", SearchOption.AllDirectories);

        // 累计所有违规（类名 + 违规标识符），便于失败时一次性报告。
        var violations = new List<string>();

        foreach (var file in files)
        {
            var code = File.ReadAllText(file);
            var tree = CSharpSyntaxTree.ParseText(code);
            var root = tree.GetRoot();

            // 收集本文件所有 class 及其直接基类名称。
            var classBases = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
                .ToDictionary(c => c.Identifier.Text, c => (c.BaseList?.Types.FirstOrDefault() as SimpleBaseTypeSyntax)?.Type.ToString());

            // 计算（传递闭包）派生自 NodeBase 的类。
            var nodeBaseDerived = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kvp in classBases)
            {
                if (IsTransitivelyDerivedFrom(kvp.Key, kvp.Value, classBases, "NodeBase", new HashSet<string>()))
                {
                    nodeBaseDerived.Add(kvp.Key);
                }
            }

            foreach (var cd in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (!nodeBaseDerived.Contains(cd.Identifier.Text))
                {
                    continue;
                }

                var span = cd.Span;
                foreach (var token in root.DescendantTokens()
                             .Where(t => t.IsKind(SyntaxKind.IdentifierToken))
                             .Where(t => t.SpanStart >= span.Start && t.Span.End <= span.End))
                {
                    if (ForbiddenNames.Contains(token.Text))
                    {
                        violations.Add($"{Path.GetFileName(file)}:{cd.Identifier.Text} -> {token.Text}");
                    }
                }
            }
        }

        Assert.Empty(violations);
    }

    private static bool IsTransitivelyDerivedFrom(
        string className,
        string? baseName,
        IReadOnlyDictionary<string, string?> classBases,
        string target,
        HashSet<string> visiting)
    {
        if (baseName is null)
        {
            return false;
        }

        if (baseName == target)
        {
            return true;
        }

        if (visiting.Contains(className))
        {
            return false;
        }

        visiting.Add(className);

        // 基类可能是本文件内定义的类，继续向上追溯；否则（如 INodeType）判定为非 NodeBase。
        return classBases.TryGetValue(baseName, out var grandBase)
               && IsTransitivelyDerivedFrom(baseName, grandBase, classBases, target, visiting);
    }

    private static string FindPluginsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "plugins", "FlowEngine.Plugins.Standard");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("无法定位插件源码目录 plugins/FlowEngine.Plugins.Standard。");
    }

    /// <summary>从代码片段中提取首个调用表达式。</summary>
    private static InvocationExpressionSyntax FirstInvocation(string statement)
    {
        var tree = CSharpSyntaxTree.ParseText($"class C {{ void M() {{ {statement} }} }}");
        return tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().First();
    }

    [Theory]
    [InlineData("registry.GetAll().ExecuteAsync(default)")]
    [InlineData("registry.GetAll().ExecuteAsync(cancellationToken: default)")]
    [InlineData("registry.GetAll().Foo().ExecuteAsync(default)")]
    public void DetectsGetAllExecuteAsync_GetAllResult_ReturnsTrue(string statement)
    {
        var invocation = FirstInvocation(statement);
        Assert.True(NodeApiComplianceAnalyzer.DetectsGetAllExecuteAsync(invocation));
    }

    [Theory]
    [InlineData("registry.Get().ExecuteAsync(default)")]
    [InlineData("x.Foo().ExecuteAsync(default)")]
    [InlineData("registry.CreateInstance(\"t\").ExecuteAsync(default)")]
    public void DetectsGetAllExecuteAsync_NonGetAllResult_ReturnsFalse(string statement)
    {
        var invocation = FirstInvocation(statement);
        Assert.False(NodeApiComplianceAnalyzer.DetectsGetAllExecuteAsync(invocation));
    }

    [Fact]
    public void Plugins_DoNotCallExecuteAsyncOnGetAllResult_NoViolations()
    {
        var pluginsDir = FindPluginsDir();
        var files = Directory.GetFiles(pluginsDir, "*.cs", SearchOption.AllDirectories);

        var violations = new List<string>();
        foreach (var file in files)
        {
            var code = File.ReadAllText(file);
            var tree = CSharpSyntaxTree.ParseText(code);
            foreach (var invocation in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (NodeApiComplianceAnalyzer.DetectsGetAllExecuteAsync(invocation))
                {
                    violations.Add($"{Path.GetFileName(file)}:{invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1}");
                }
            }
        }

        Assert.Empty(violations);
    }
}
