using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Serilog;
using Yafc.UI;

namespace Yafc.Parser;

/// <summary>
/// Class for parsing and evaluating 2.0 expressions.
/// </summary>
internal sealed partial class MathExpression {
    private static readonly ILogger logger = Logging.GetLogger<MathExpression>();

    public static float Evaluate(string expression, LuaTable? variables) {
        if (Parse(expression) is not ExpressionSyntax node) {
            return 1;
        }
        return Evaluate(node, variables);
    }

    // Walk the syntax tree to evaluate node (and its children)
    private static float Evaluate(ExpressionSyntax node, LuaTable? variables) {
        switch (node) {
            // left {op} right, for supported operators (except exponentiation, which became a RangeExpression)
            case BinaryExpressionSyntax binary: {
                    float left = Evaluate(binary.Left, variables);
                    float right = Evaluate(binary.Right, variables);
                    switch ((SyntaxKind)binary.RawKind) {
                        case SyntaxKind.AddExpression:
                            return left + right;
                        case SyntaxKind.SubtractExpression:
                            return left - right;
                        case SyntaxKind.MultiplyExpression:
                            return left * right;
                        case SyntaxKind.DivideExpression:
                            return left / right;

                        default:
                            logger.Information("Unknown binary expression '{SyntaxKind}' in transpiled C# code, found in the C# expression '{Expression}'.", (SyntaxKind)binary.RawKind, binary);
                            return 1;
                    }
                }

            // An identifier not followed by an argument list. This is a variable from variables.
            case IdentifierNameSyntax { Identifier.Text: string name }:
                return variables.Get(name[1..], 0f);

            // A function call
            case InvocationExpressionSyntax { Expression: ExpressionSyntax name_, ArgumentList.Arguments: var args }: {
                    string name = name_.ToString()[1..];

                    // Built-in functions (https://lua-api.factorio.com/latest/concepts/MathExpression.html)
                    switch (name) {
                        case "abs" when args.Count == 1:
                            return MathF.Abs(Evaluate(args[0].Expression, variables));
                        case "log2" when args.Count == 1:
                            return MathF.Log2(Evaluate(args[0].Expression, variables));
                        case "sign" when args.Count == 1:
                            return MathF.Sign(Evaluate(args[0].Expression, variables));
                        case "max":
                            return args.Select(a => Evaluate(a.Expression, variables)).Max();
                        case "min":
                            return args.Select(a => Evaluate(a.Expression, variables)).Min();
                        default:
                            logger.Information("In a Lua expression, '{Function}' is unknown or has the wrong number of arguments. (Found {Count} arguments.)", name, args.Count);
                            return 1;
                    }
                }

            // Literal number
            case LiteralExpressionSyntax { Token.Value: object number }:
                try {
                    return Convert.ToSingle(number);
                }
                catch {
                    logger.Information("Could not parse '{Number}' from a Lua expression as a number.", number);
                    return 1;
                }

            case ParenthesizedExpressionSyntax expr:
                return Evaluate(expr.Expression, variables);

            // left ^ right in Lua, translated to left .. right in C# for the tight binding of the range operator.
            // The range operator is left-associative, though, so x^y^z became (x^y)^z, but we need to invert that.
            case RangeExpressionSyntax range: {
                    Queue<ExpressionSyntax> expressions = [];
                    while (range.LeftOperand is RangeExpressionSyntax leftRange) {
                        expressions.Enqueue(range.RightOperand!);
                        range = leftRange!;
                    }
                    expressions.Enqueue(range.RightOperand!);
                    expressions.Enqueue(range.LeftOperand!);
                    float result = Evaluate(expressions.Dequeue(), variables);
                    while (expressions.TryDequeue(out var left)) {
                        result = MathF.Pow(Evaluate(left, variables), result);
                    }
                    return result;
                }
            default:
                logger.Information("Unknown expression type '{Type}' in transpiled C# noise code, found in the C# expression '{Expression}'.", node?.GetType(), node?.ToString());
                return 1;
        }
    }

    /// <summary>
    /// Leverage the C# compiler to convert the lua expression into a syntax tree, which is easier than trying to construct a syntax tree with the
    /// correct operator precedence on our own.
    /// </summary>
    private static ExpressionSyntax? Parse(string expression) {
        try {
            string? cSharp = Transpile(Tokenize(expression));
            if (cSharp == null) {
                logger.Information("Failed to transpile noise expression '{Expression}' into C#.", expression);
                return null;
            }
            // Assign to a discard to force the code into an expression context. In a statement context, some expressions will parse incorrectly.
            // (e.g. "foo*bar" in a statement context is "Declare a variable named `bar` of type `pointer to foo`.")
            CompilationUnitSyntax result = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText("_=" + cSharp).GetRoot();
            // Extract the portion of the syntax tree corresponding to the original expression.
            return ((AssignmentExpressionSyntax)((ExpressionStatementSyntax)((GlobalStatementSyntax)result.Members[0]).Statement).Expression).Right;
        }
        catch (Exception ex) {
            // If anything goes wrong, use the fall-back value.
            logger.Information(ex, "Failed to transpile expression '{Expression}' into C#.", expression);
            return null;
        }
    }

    internal enum Token {
        Caret = '^',
        Plus = '+', Minus = '-',
        Asterisk = '*', Slash = '/',

        OpenParen = '(', CloseParen = ')',
        Comma = ',',
    }

    /// <summary>
    /// Transpile the token stream into a valid C# expression, which will then be parsed into a tree and walked.
    /// </summary>
    internal static string? Transpile(IEnumerable<object?> tokens) {
        StringBuilder cSharp = new();

        foreach (object? token in tokens) {
            switch (token) {
                case null:
                    return null;

                case string identifier:
                    // Prefix an @ so the identifier (e.g. 'base') cannot be a keyword.
                    cSharp.Append('@').Append(identifier);
                    break;

                case float f:
                    cSharp.Append(f.ToString(CultureInfo.InvariantCulture));
                    break;

                case Token.Caret:
                    cSharp.Append(".."); // Transpile exponentation into the tightly-binding range operator. Reinterpret it as Pow later.
                    break;
                case Token.Plus or Token.Minus or Token.Asterisk or Token.Slash or Token.Comma or Token.OpenParen or Token.CloseParen:
                    cSharp.Append((char)(Token)token);
                    break;

                default:
                    logger.Information("Ignoring unexpected token {Token} found while parsing a noise expression.", token);
                    break;
            }
        }

        return cSharp.Append(';').ToString();
    }

    /// <summary>
    /// Tokenize the input expression, so the non-C# tokens (e.g. ^ for exponentaion) can be converted to valid C#.
    /// </summary>
    internal static IEnumerable<object?> Tokenize(string expression) {
        string remainingExpression = expression;
        while (remainingExpression.Length > 0) {
            int startLength = remainingExpression.Length;

            switch (remainingExpression[0]) {
                // Single-character tokens:
                case '^' or '+' or '-' or '*' or '/' or '(' or ')' or ',':
                    yield return (Token)remainingExpression[0];
                    remainingExpression = remainingExpression[1..];
                    break;
            }

            remainingExpression = remainingExpression[Whitespace().Match(remainingExpression).Length..];

            if (Identifier().Match(remainingExpression) is { Success: true } identifier) {
                remainingExpression = remainingExpression[identifier.Length..];
                yield return identifier.ToString();
            }

            if (Number().Match(remainingExpression) is { Success: true } num) {
                string number = num.ToString();
                remainingExpression = remainingExpression[number.Length..];
                if (number.StartsWith("0x")) {
                    yield return (float)Convert.ToInt32(number[2..], 16);
                }
                else {
                    yield return float.Parse(number, CultureInfo.InvariantCulture);
                }
            }

            if (String().Match(remainingExpression) is { Success: true } str) {
                remainingExpression = remainingExpression[str.Length..];
                yield return str.ToString();
            }

            if (remainingExpression.Length == startLength) {
                // Don't know how to tokenize this. Bail out, which will eventually cause fallback estimation.
                yield return null;
                yield break;
            }
        }
    }

    [GeneratedRegex(@"^[ \n\r\t]*")]
    private static partial Regex Whitespace();
    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_:]*")]
    private static partial Regex Identifier();
    [GeneratedRegex(@"^(0x[0-9a-f]+|([0-9]+\.?[0-9]*|\.[0-9]+)(e-?[0-9]+)?)", RegexOptions.ExplicitCapture)]
    private static partial Regex Number();
    [GeneratedRegex("""^("[^"]*"|'[^']*')""", RegexOptions.ExplicitCapture)]
    private static partial Regex String();
}
