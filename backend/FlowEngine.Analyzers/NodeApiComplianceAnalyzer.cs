using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FlowEngine.Analyzers;

/// <summary>
/// 编译期合规检查：所有传递派生自 <c>FlowEngine.Core.Abstractions.NodeBase</c> 的节点，
/// 不得引用“服务定位器/索取式” API（如 <c>context.ErrorResult</c> / <c>GetParameter</c> /
/// <c>HttpClientPool</c> 等）。与 <c>NodeApiComplianceTests</c> 单元测试保持同一规则集合与扫描范围。
/// 非 NodeBase 派生节点（仍实现 INodeType）被排除。
/// 分析器永不向外抛异常——任何异常都会导致整个编译失败，因此一律吞掉并仅做报告。
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NodeApiComplianceAnalyzer : DiagnosticAnalyzer
{
    /// <summary>禁止出现的标识符（精确匹配、区分大小写）。</summary>
    private static readonly HashSet<string> ForbiddenNames = new(StringComparer.Ordinal)
    {
        "GetParameter",
        "ErrorResult",
        "HttpClientPool",
        "NodeRegistry",
        "ContextFactory",
        "WorkflowLoader",
        "LlmClientFactory",
        "ScriptCache",
        "NestingDepth",
        "AllowShellExecution",
        "IsAgentInvocation",
        "ResolveCredentialAsync",
        "ResolvedParameters",
        "RawParameters",
    };

    /// <summary>诊断描述符 FE0001。</summary>
    public static readonly DiagnosticDescriptor Rule = new(
        id: "FE0001",
        title: "NodeBase 派生节点不得引用服务定位器 API",
        messageFormat: "节点 {0} 引用了禁止的 API '{1}'（NodeBase 节点不得读取服务定位器/索取式上下文）",
        category: "Design",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "NodeBase derived nodes may only read their resolved properties, NodeInput, and NodeBase protected capabilities. Service-locator / pull-style context APIs are forbidden.");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        // 若项目未引用 Core（找不到 NodeBase 符号），则无可检查目标，直接跳过。
        context.RegisterCompilationStartAction(compilationStartContext =>
        {
            var nodeBaseSymbol = compilationStartContext.Compilation
                .GetTypeByMetadataName("FlowEngine.Core.Abstractions.NodeBase");
            if (nodeBaseSymbol is null)
            {
                return;
            }

            compilationStartContext.RegisterSyntaxNodeAction(syntaxContext =>
            {
                try
                {
                    var classDecl = (ClassDeclarationSyntax)syntaxContext.Node;
                    var symbol = syntaxContext.SemanticModel.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
                    if (symbol is null)
                    {
                        return;
                    }

                    if (!IsDerivedFromNodeBase(symbol, nodeBaseSymbol))
                    {
                        return;
                    }

                    // 扫描类声明范围内的所有标识符标记（与单元测试行为一致：精确、区分大小写）。
                    foreach (var token in classDecl.DescendantTokens()
                                 .Where(t => t.IsKind(SyntaxKind.IdentifierToken)))
                    {
                        if (ForbiddenNames.Contains(token.Text))
                        {
                            syntaxContext.ReportDiagnostic(
                                Diagnostic.Create(Rule, token.GetLocation(), symbol.Name, token.Text));
                        }
                    }
                }
                catch
                {
                    // 永不破坏构建：任何异常都吞掉，仅停止本次分析。
                }
            }, SyntaxKind.ClassDeclaration);
        });
    }

    private static bool IsDerivedFromNodeBase(INamedTypeSymbol symbol, INamedTypeSymbol nodeBaseSymbol)
    {
        var current = symbol.BaseType;
        while (current is not null)
        {
            if (SymbolEqualityComparer.Default.Equals(current, nodeBaseSymbol))
            {
                return true;
            }

            current = current.BaseType;
        }

        return false;
    }
}
