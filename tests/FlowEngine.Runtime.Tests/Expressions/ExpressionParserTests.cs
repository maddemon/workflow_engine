using FlowEngine.Runtime.Expressions;
using FlowEngine.Runtime.Expressions.Ast;

namespace FlowEngine.Runtime.Tests.Expressions;

public class ExpressionParserTests
{
    private readonly ExpressionParser _parser = new();

    [Fact]
    public void Parse_Input_Id_Returns_Member_Access()
    {
        var segments = _parser.Parse("{{ input.id }}");

        var expression = Assert.IsType<ExpressionSegment>(Assert.Single(segments));
        var memberAccess = Assert.IsType<MemberAccessNode>(expression.Expression);
        Assert.IsType<IdentifierNode>(memberAccess.Target);
        Assert.Equal("id", memberAccess.MemberName);
    }

    [Fact]
    public void Parse_Literal_And_Expression_Returns_Two_Segments()
    {
        var segments = _parser.Parse("hello {{ input.name }}!");

        Assert.Equal(3, segments.Count);
        Assert.IsType<LiteralSegment>(segments[0]);
        Assert.IsType<ExpressionSegment>(segments[1]);
        Assert.IsType<LiteralSegment>(segments[2]);
    }

    [Fact]
    public void Parse_String_Literal_With_Braces_Does_Not_Terminate_Early()
    {
        var segments = _parser.Parse("{{ \"a}}b\" }}");

        var expression = Assert.IsType<ExpressionSegment>(Assert.Single(segments));
        var literal = Assert.IsType<LiteralNode>(expression.Expression);
        Assert.Equal("a}}b", literal.Value);
    }

    [Fact]
    public void Parse_Function_Call_Returns_Function_Node()
    {
        var segments = _parser.Parse("{{ length(input.items) }}");

        var expression = Assert.IsType<ExpressionSegment>(Assert.Single(segments));
        var functionCall = Assert.IsType<FunctionCallNode>(expression.Expression);
        Assert.Equal("length", functionCall.FunctionName);
        Assert.Single(functionCall.Arguments);
    }

    [Fact]
    public void Parse_Indexer_Returns_Indexer_Node()
    {
        var segments = _parser.Parse("{{ items(\"GetUser\")[0] }}");

        var expression = Assert.IsType<ExpressionSegment>(Assert.Single(segments));
        var indexer = Assert.IsType<IndexerNode>(expression.Expression);
        Assert.IsType<FunctionCallNode>(indexer.Target);
        Assert.IsType<LiteralNode>(indexer.Index);
    }

    [Fact]
    public void Parse_Missing_Closing_Braces_Throws_Syntax_Error()
    {
        Assert.Throws<ExpressionEvaluationException>(() => _parser.Parse("{{ input.id "));
    }
}
