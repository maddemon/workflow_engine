using System.Globalization;
using System.Text;
using FlowEngine.Runtime.Expressions.Ast;

namespace FlowEngine.Runtime.Expressions;

/// <summary>
/// 表达式解析器，将包含 <c>{{ }}</c> 的模板字符串解析为 AST 片段序列。
/// </summary>
public sealed class ExpressionParser
{
    /// <summary>
    /// 解析模板字符串。
    /// </summary>
    /// <param name="template">模板字符串。</param>
    /// <returns>模板片段序列。</returns>
    /// <exception cref="ExpressionEvaluationException">语法错误时抛出。</exception>
    public IReadOnlyList<Segment> Parse(string template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var segments = new List<Segment>();
        var position = 0;

        while (position < template.Length)
        {
            var startIndex = template.IndexOf("{{", position, StringComparison.Ordinal);
            if (startIndex < 0)
            {
                var remaining = template[position..];
                if (!string.IsNullOrEmpty(remaining))
                {
                    segments.Add(new LiteralSegment(remaining));
                }

                break;
            }

            if (startIndex > position)
            {
                segments.Add(new LiteralSegment(template[position..startIndex]));
            }

            var endIndex = FindExpressionEnd(template, startIndex + 2);
            if (endIndex < 0)
            {
                throw CreateSyntaxError(template, startIndex, "未找到表达式结束标记 '}}'。");
            }

            var expressionText = template[(startIndex + 2)..endIndex].Trim();
            if (string.IsNullOrEmpty(expressionText))
            {
                throw CreateSyntaxError(template, startIndex, "表达式不能为空。");
            }

            var expression = ParseExpression(expressionText);
            segments.Add(new ExpressionSegment(expression));
            position = endIndex + 2;
        }

        return segments;
    }

    private static int FindExpressionEnd(string template, int startPosition)
    {
        var inString = false;
        var stringChar = '\0';
        var escaped = false;

        for (var i = startPosition; i < template.Length - 1; i++)
        {
            var ch = template[i];

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (ch == stringChar)
                {
                    inString = false;
                }

                continue;
            }

            if (ch == '"' || ch == '\'')
            {
                inString = true;
                stringChar = ch;
                continue;
            }

            if (ch == '}' && template[i + 1] == '}')
            {
                return i;
            }
        }

        return -1;
    }

    private static ExpressionNode ParseExpression(string expressionText)
    {
        var tokenizer = new Tokenizer(expressionText);
        var parser = new ExpressionParserInternal(tokenizer, expressionText);
        return parser.ParseConditional();
    }

    private static ExpressionEvaluationException CreateSyntaxError(
        string template,
        int position,
        string reason)
    {
        return new ExpressionEvaluationException(new ExpressionError
        {
            Type = ExpressionErrorType.SyntaxError,
            Expression = template,
            Position = position,
            Reason = reason
        });
    }

    private enum TokenType
    {
        End,
        Number,
        String,
        Boolean,
        Identifier,
        Dot,
        Comma,
        LeftParen,
        RightParen,
        LeftBracket,
        RightBracket,
        Plus,
        Minus,
        Multiply,
        Divide,
        Modulo,
        Equal,
        NotEqual,
        GreaterThan,
        LessThan,
        GreaterThanOrEqual,
        LessThanOrEqual,
        And,
        Or,
        Not,
        Question,
        Colon
    }

    private sealed record Token(TokenType Type, object? Value, int Position);

    private sealed class Tokenizer
    {
        private readonly string _text;
        private int _position;

        public Tokenizer(string text)
        {
            _text = text;
        }

        public Token NextToken()
        {
            SkipWhitespace();

            if (_position >= _text.Length)
            {
                return new Token(TokenType.End, null, _position);
            }

            var startPosition = _position;
            var ch = _text[_position];

            if (char.IsDigit(ch))
            {
                return ReadNumber(startPosition);
            }

            if (ch == '"' || ch == '\'')
            {
                return ReadString(startPosition, ch);
            }

            if (char.IsLetter(ch) || ch == '_')
            {
                return ReadIdentifier(startPosition);
            }

            return ch switch
            {
                '.' => AdvanceAndReturn(TokenType.Dot, startPosition),
                ',' => AdvanceAndReturn(TokenType.Comma, startPosition),
                '(' => AdvanceAndReturn(TokenType.LeftParen, startPosition),
                ')' => AdvanceAndReturn(TokenType.RightParen, startPosition),
                '[' => AdvanceAndReturn(TokenType.LeftBracket, startPosition),
                ']' => AdvanceAndReturn(TokenType.RightBracket, startPosition),
                '+' => AdvanceAndReturn(TokenType.Plus, startPosition),
                '-' => AdvanceAndReturn(TokenType.Minus, startPosition),
                '*' => AdvanceAndReturn(TokenType.Multiply, startPosition),
                '/' => AdvanceAndReturn(TokenType.Divide, startPosition),
                '%' => AdvanceAndReturn(TokenType.Modulo, startPosition),
                '?' => AdvanceAndReturn(TokenType.Question, startPosition),
                ':' => AdvanceAndReturn(TokenType.Colon, startPosition),
                '!' when PeekNext('=') => AdvanceTwoAndReturn(TokenType.NotEqual, startPosition),
                '!' => AdvanceAndReturn(TokenType.Not, startPosition),
                '=' when PeekNext('=') => AdvanceTwoAndReturn(TokenType.Equal, startPosition),
                '>' when PeekNext('=') => AdvanceTwoAndReturn(TokenType.GreaterThanOrEqual, startPosition),
                '>' => AdvanceAndReturn(TokenType.GreaterThan, startPosition),
                '<' when PeekNext('=') => AdvanceTwoAndReturn(TokenType.LessThanOrEqual, startPosition),
                '<' => AdvanceAndReturn(TokenType.LessThan, startPosition),
                '&' when PeekNext('&') => AdvanceTwoAndReturn(TokenType.And, startPosition),
                '|' when PeekNext('|') => AdvanceTwoAndReturn(TokenType.Or, startPosition),
                _ => throw new ExpressionEvaluationException(new ExpressionError
                {
                    Type = ExpressionErrorType.SyntaxError,
                    Expression = _text,
                    Position = startPosition,
                    Reason = $"未识别的字符 '{ch}'。"
                })
            };
        }

        private Token AdvanceAndReturn(TokenType type, int startPosition)
        {
            _position++;
            return new Token(type, null, startPosition);
        }

        private Token AdvanceTwoAndReturn(TokenType type, int startPosition)
        {
            _position += 2;
            return new Token(type, null, startPosition);
        }

        private bool PeekNext(char expected)
        {
            return _position + 1 < _text.Length && _text[_position + 1] == expected;
        }

        private Token ReadNumber(int startPosition)
        {
            var end = _position;
            while (end < _text.Length && (char.IsDigit(_text[end]) || _text[end] == '.'))
            {
                end++;
            }

            var numberText = _text[startPosition..end];
            _position = end;

            if (numberText.Contains('.') && double.TryParse(numberText, CultureInfo.InvariantCulture, out var doubleValue))
            {
                return new Token(TokenType.Number, doubleValue, startPosition);
            }

            if (int.TryParse(numberText, out var intValue))
            {
                return new Token(TokenType.Number, intValue, startPosition);
            }

            throw new ExpressionEvaluationException(new ExpressionError
            {
                Type = ExpressionErrorType.SyntaxError,
                Expression = _text,
                Position = startPosition,
                Reason = $"无法解析数字 '{numberText}'。"
            });
        }

        private Token ReadString(int startPosition, char quoteChar)
        {
            _position++;
            var sb = new StringBuilder();
            var escaped = false;

            while (_position < _text.Length)
            {
                var ch = _text[_position];
                if (escaped)
                {
                    sb.Append(ch);
                    escaped = false;
                }
                else if (ch == '\\')
                {
                    escaped = true;
                }
                else if (ch == quoteChar)
                {
                    _position++;
                    return new Token(TokenType.String, sb.ToString(), startPosition);
                }
                else
                {
                    sb.Append(ch);
                }

                _position++;
            }

            throw new ExpressionEvaluationException(new ExpressionError
            {
                Type = ExpressionErrorType.SyntaxError,
                Expression = _text,
                Position = startPosition,
                Reason = "字符串未正确结束。"
            });
        }

        private Token ReadIdentifier(int startPosition)
        {
            var end = _position;
            while (end < _text.Length && (char.IsLetterOrDigit(_text[end]) || _text[end] == '_'))
            {
                end++;
            }

            var identifier = _text[startPosition..end];
            _position = end;

            if (identifier.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                return new Token(TokenType.Boolean, true, startPosition);
            }

            if (identifier.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                return new Token(TokenType.Boolean, false, startPosition);
            }

            return new Token(TokenType.Identifier, identifier, startPosition);
        }

        private void SkipWhitespace()
        {
            while (_position < _text.Length && char.IsWhiteSpace(_text[_position]))
            {
                _position++;
            }
        }
    }

    private sealed class ExpressionParserInternal
    {
        private readonly Tokenizer _tokenizer;
        private readonly string _expressionText;
        private Token _currentToken;

        public ExpressionParserInternal(Tokenizer tokenizer, string expressionText)
        {
            _tokenizer = tokenizer;
            _expressionText = expressionText;
            _currentToken = tokenizer.NextToken();
        }

        private ExpressionEvaluationException CreateSyntaxError(int position, string reason)
        {
            return new ExpressionEvaluationException(new ExpressionError
            {
                Type = ExpressionErrorType.SyntaxError,
                Expression = _expressionText,
                Position = position,
                Reason = reason
            });
        }

        public ExpressionNode ParseConditional()
        {
            var condition = ParseOr();

            if (_currentToken.Type == TokenType.Question)
            {
                Advance();
                var trueExpression = ParseConditional();
                Expect(TokenType.Colon, "条件表达式缺少 ':'。");
                var falseExpression = ParseConditional();
                return new TernaryNode(condition, trueExpression, falseExpression);
            }

            return condition;
        }

        private ExpressionNode ParseOr()
        {
            var left = ParseAnd();

            while (_currentToken.Type == TokenType.Or)
            {
                Advance();
                var right = ParseAnd();
                left = new BinaryOperationNode(BinaryOperator.Or, left, right);
            }

            return left;
        }

        private ExpressionNode ParseAnd()
        {
            var left = ParseEquality();

            while (_currentToken.Type == TokenType.And)
            {
                Advance();
                var right = ParseEquality();
                left = new BinaryOperationNode(BinaryOperator.And, left, right);
            }

            return left;
        }

        private ExpressionNode ParseEquality()
        {
            var left = ParseComparison();

            while (_currentToken.Type is TokenType.Equal or TokenType.NotEqual)
            {
                var op = _currentToken.Type == TokenType.Equal ? BinaryOperator.Equal : BinaryOperator.NotEqual;
                Advance();
                var right = ParseComparison();
                left = new BinaryOperationNode(op, left, right);
            }

            return left;
        }

        private ExpressionNode ParseComparison()
        {
            var left = ParseAdditive();

            while (_currentToken.Type is TokenType.GreaterThan or TokenType.LessThan
                   or TokenType.GreaterThanOrEqual or TokenType.LessThanOrEqual)
            {
                var op = _currentToken.Type switch
                {
                    TokenType.GreaterThan => BinaryOperator.GreaterThan,
                    TokenType.LessThan => BinaryOperator.LessThan,
                    TokenType.GreaterThanOrEqual => BinaryOperator.GreaterThanOrEqual,
                    TokenType.LessThanOrEqual => BinaryOperator.LessThanOrEqual,
                    _ => throw new InvalidOperationException()
                };

                Advance();
                var right = ParseAdditive();
                left = new BinaryOperationNode(op, left, right);
            }

            return left;
        }

        private ExpressionNode ParseAdditive()
        {
            var left = ParseMultiplicative();

            while (_currentToken.Type is TokenType.Plus or TokenType.Minus)
            {
                var op = _currentToken.Type == TokenType.Plus ? BinaryOperator.Add : BinaryOperator.Subtract;
                Advance();
                var right = ParseMultiplicative();
                left = new BinaryOperationNode(op, left, right);
            }

            return left;
        }

        private ExpressionNode ParseMultiplicative()
        {
            var left = ParseUnary();

            while (_currentToken.Type is TokenType.Multiply or TokenType.Divide or TokenType.Modulo)
            {
                var op = _currentToken.Type switch
                {
                    TokenType.Multiply => BinaryOperator.Multiply,
                    TokenType.Divide => BinaryOperator.Divide,
                    TokenType.Modulo => BinaryOperator.Modulo,
                    _ => throw new InvalidOperationException()
                };

                Advance();
                var right = ParseUnary();
                left = new BinaryOperationNode(op, left, right);
            }

            return left;
        }

        private ExpressionNode ParseUnary()
        {
            if (_currentToken.Type is TokenType.Not or TokenType.Minus or TokenType.Plus)
            {
                var op = _currentToken.Type switch
                {
                    TokenType.Not => UnaryOperator.Not,
                    TokenType.Minus => UnaryOperator.Negate,
                    TokenType.Plus => UnaryOperator.Plus,
                    _ => throw new InvalidOperationException()
                };

                var position = _currentToken.Position;
                Advance();
                var operand = ParseUnary();
                return new UnaryOperationNode(op, operand);
            }

            return ParsePostfix();
        }

        private ExpressionNode ParsePostfix()
        {
            var node = ParsePrimary();

            while (_currentToken.Type is TokenType.Dot or TokenType.LeftBracket)
            {
                if (_currentToken.Type == TokenType.Dot)
                {
                    Advance();
                    var memberName = ExpectIdentifier("成员访问需要标识符。");
                    node = new MemberAccessNode(node, memberName);
                }
                else
                {
                    Advance();
                    var index = ParseConditional();
                    Expect(TokenType.RightBracket, "索引器缺少 ']'。");
                    node = new IndexerNode(node, index);
                }
            }

            return node;
        }

        private ExpressionNode ParsePrimary()
        {
            return _currentToken.Type switch
            {
                TokenType.Number => AdvanceAndReturn(new LiteralNode(_currentToken.Value)),
                TokenType.String => AdvanceAndReturn(new LiteralNode(_currentToken.Value)),
                TokenType.Boolean => AdvanceAndReturn(new LiteralNode(_currentToken.Value)),
                TokenType.Identifier => ParseIdentifierOrFunctionCall(),
                TokenType.LeftParen => ParseParenthesized(),
                _ => throw CreateSyntaxError(_currentToken.Position, $"未期望的标记 {_currentToken.Type}。")
            };
        }

        private ExpressionNode ParseIdentifierOrFunctionCall()
        {
            var name = (string)_currentToken.Value!;
            var position = _currentToken.Position;
            Advance();

            if (_currentToken.Type == TokenType.LeftParen)
            {
                Advance();
                var arguments = new List<ExpressionNode>();

                if (_currentToken.Type != TokenType.RightParen)
                {
                    arguments.Add(ParseConditional());

                    while (_currentToken.Type == TokenType.Comma)
                    {
                        Advance();
                        arguments.Add(ParseConditional());
                    }
                }

                Expect(TokenType.RightParen, "函数调用缺少 ')'。");
                return new FunctionCallNode(name, arguments);
            }

            return new IdentifierNode(name);
        }

        private ExpressionNode ParseParenthesized()
        {
            Advance();
            var expression = ParseConditional();
            Expect(TokenType.RightParen, "括号表达式缺少 ')'。");
            return expression;
        }

        private string ExpectIdentifier(string errorMessage)
        {
            if (_currentToken.Type != TokenType.Identifier)
            {
                throw CreateSyntaxError(_currentToken.Position, errorMessage);
            }

            var value = (string)_currentToken.Value!;
            Advance();
            return value;
        }

        private void Expect(TokenType type, string errorMessage)
        {
            if (_currentToken.Type != type)
            {
                throw CreateSyntaxError(_currentToken.Position, errorMessage);
            }

            Advance();
        }

        private ExpressionNode AdvanceAndReturn(ExpressionNode node)
        {
            Advance();
            return node;
        }

        private void Advance()
        {
            _currentToken = _tokenizer.NextToken();
        }
    }
}
