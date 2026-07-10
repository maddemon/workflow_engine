using Acornima;
using Acornima.Ast;
using Jint;

namespace FlowEngine.Core.Scripting;

using JintPreparedScript = Jint.Prepared<Acornima.Ast.Script>;

/// <summary>
/// 脚本编译器。根据源码 AST 结构自动决定是否需要 IIFE 包裹，
/// 并生成可复用的 Jint 预编译产物。
/// </summary>
internal static class ScriptCompiler
{
    /// <summary>
    /// 编译 <see cref="Script"/> 为 Jint 预编译产物。
    /// </summary>
    public static JintPreparedScript Compile(Script script)
    {
        if (script.Language != ScriptLanguage.JavaScript)
        {
            throw new NotSupportedException($"脚本语言 '{script.Language}' 暂不支持。");
        }

        var source = script.Source ?? string.Empty;
        if (string.IsNullOrWhiteSpace(source))
        {
            return Prepare("(function(){ return undefined; })()", source);
        }

        // Parser 不是线程安全的，每次编译新建实例。
        // 允许顶层 return，以便识别含 return 的脚本并正确包裹为 IIFE。
        var parser = new Parser(new ParserOptions { AllowReturnOutsideFunction = true });
        var ast = parser.ParseScript(source);

        if (ast.Body.Count == 1 && ast.Body[0] is ExpressionStatement expressionStatement)
        {
            var range = expressionStatement.Expression.Range;
            var expressionSource = source[range.Start..range.End];
            var wrapped = $"return ({expressionSource});";
            return Prepare(wrapped, source);
        }

        if (HasTopLevelReturn(ast))
        {
            var wrapped = $"(function(){{ {source} }})();";
            return Prepare(wrapped, source);
        }

        // 多语句且无顶层 return：统一包裹为 IIFE 并返回 undefined。
        var iifeWrapped = $"(function(){{ {source}; return undefined; }})();";
        return Prepare(iifeWrapped, source);
    }

    private static JintPreparedScript Prepare(string source, string originalSource)
    {
        return Engine.PrepareScript(source, originalSource, strict: true);
    }

    private static bool HasTopLevelReturn(Acornima.Ast.Script ast)
    {
        foreach (var statement in ast.Body)
        {
            if (ContainsReturn(statement))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsReturn(Node node)
    {
        if (node is ReturnStatement)
        {
            return true;
        }

        if (node is FunctionDeclaration or FunctionExpression or ArrowFunctionExpression)
        {
            // 不进入函数体，避免将内部 return 误判为顶层 return。
            return false;
        }

        foreach (var child in node.ChildNodes)
        {
            if (ContainsReturn(child))
            {
                return true;
            }
        }

        return false;
    }
}
