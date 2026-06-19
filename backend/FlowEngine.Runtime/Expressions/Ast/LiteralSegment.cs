namespace FlowEngine.Runtime.Expressions.Ast;

/// <summary>
/// 字面量文本片段。
/// </summary>
/// <param name="Text">原始文本。</param>
public sealed record LiteralSegment(string Text) : Segment;
