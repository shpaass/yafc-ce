using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Serilog;
using Yafc.UI;

namespace Yafc.Parser;

internal partial class FactorioDataDeserializer {
    /// <summary>
    /// Class for parsing and estimating 2.0 noise expressions.
    /// </summary>
    internal sealed partial class Noise {
        private static readonly ILogger logger = Logging.GetLogger<Noise>();

        // The local_expressions table corresponding to the current root or global expression
        private readonly LuaTable? localExpressions;
        // The local_functions table corresponding to the current global function, if applicable
        private readonly LuaTable? localFunctions;
        // data.raw, for looking up global functions and expressions 
        private readonly LuaTable raw;
        // The parameter names for the current global function, or empty
        private readonly string[] globalParameterNames;
        // A method that will lazily estimate the specified function parameter. The name and index must both be specified. The called function
        // doesn't know which call format was used, and the call site doesn't know how to translate between names and indexes.
        private readonly Func<string, int, float>? estimateGlobalParameter;

        internal Noise(LuaTable generation, LuaTable raw, Func<string, int, float>? estimateGlobalParameter) {
            localExpressions = generation.Get<LuaTable>("local_expressions");
            localFunctions = generation.Get<LuaTable>("local_functions");
            this.raw = raw;
            globalParameterNames = [.. generation.Get<LuaTable>("parameters").ArrayElements<string>()];
            this.estimateGlobalParameter = estimateGlobalParameter;
        }

        // The default values for some optional function parameters.
        private static readonly Dictionary<string, float> defaultValues = new() {
            ["input_scale"] = 1,
            ["output_scale"] = 1,
            ["offset_x"] = 0,
            ["offset_y"] = 0,
            ["maximum_distance"] = float.PositiveInfinity,
            ["octave_input_scale_multiplier"] = 0.5f,
            ["octave_output_scale_multiplier"] = 2,
            ["octave_seed0_shift"] = 1,
            ["seed"] = 1,
            ["amplitude"] = 1,
            ["region_size"] = 512,
            ["skip_offset"] = 0,
            ["skip_span"] = 1,
            ["hard_region_target_quantity"] = 1,
            ["jitter"] = 0.5f,
        };

        // The index of the output_scale parameter for noise functions.
        private static readonly Dictionary<string, int> outputScaleIndexes = new() {
            ["basis_noise"] = 5,
            ["multioctave_noise"] = 7,
            ["quick_multioctave_noise"] = 6,
            ["variable_persistence_multioctave_noise"] = 7,
        };

        public static float Estimate(LuaTable generation, string key, LuaTable raw) {
            if (generation.Get(key, out bool b)) {
                return b ? 1 : 0;
            }
            if (generation.Get(key, out float f)) {
                return f;
            }
            if (generation.Get(key, out string? expression)) {
                return new Noise(generation, raw, null).EstimateRootExpression(expression);
            }
            return 1;
        }

        private static float EstimateGlobalFunction(LuaTable function, Func<string, int, float> estimateParameter, LuaTable raw) {
            if (function.Get("expression", out string? expression)) {
                return new Noise(function, raw, estimateParameter).EstimateRootExpression(expression);
            }
            return 1;
        }

        // Root and local expressions are parsed identically, but separate methods make it easier to keep track of what's happening.
        internal float EstimateRootExpression(string expression) => EstimateLocalExpression(expression);

        private float EstimateLocalExpression(string expression) {
            if (Parse(expression) is not SyntaxNode node) {
                return 1;
            }
            return EstimateSyntax(node, [], null);
        }

        private float EstimateLocalFunction(LuaTable function, Func<string, int, float> estimateLocalParameter) {
            if (!function.Get("expression", out string? expression) || Parse(expression) is not SyntaxNode node) {
                return 1;
            }
            return EstimateSyntax(node, function.Get<LuaTable>("parameters").ArrayElements<string>()?.ToArray() ?? [], estimateLocalParameter);
        }

        private float EstimateSyntax(SyntaxNode node, string[] localParameterNames, Func<string, int, float>? estimateLocalParameter) {
            switch (node) {
                // left {op} right, for supported operators (except exponentiation, which became a RangeExpression)
                case BinaryExpressionSyntax binary: {
                        float left = EstimateSyntax(binary.Left, localParameterNames, estimateLocalParameter);
                        float right = EstimateSyntax(binary.Right, localParameterNames, estimateLocalParameter);
                        switch ((SyntaxKind)binary.RawKind) {
                            case SyntaxKind.AddExpression:
                                return left + right;
                            case SyntaxKind.SubtractExpression:
                                return left - right;
                            case SyntaxKind.MultiplyExpression:
                                return left * right;
                            case SyntaxKind.DivideExpression:
                                return left / right;
                            case SyntaxKind.ModuloExpression:
                                if (binary.OperatorToken.HasTrailingTrivia) {
                                    return MathF.IEEERemainder(left, right);
                                }
                                return left % right;

                            case SyntaxKind.BitwiseAndExpression:
                                return (int)left & (int)right;
                            case SyntaxKind.BitwiseOrExpression:
                                return (int)left | (int)right;
                            case SyntaxKind.ExclusiveOrExpression:
                                return (int)left ^ (int)right;

                            case SyntaxKind.LessThanExpression:
                                return left < right ? 1 : 0;
                            case SyntaxKind.LessThanOrEqualExpression:
                                return left <= right ? 1 : 0;
                            case SyntaxKind.GreaterThanExpression:
                                return left > right ? 1 : 0;
                            case SyntaxKind.GreaterThanOrEqualExpression:
                                return left >= right ? 1 : 0;
                            case SyntaxKind.EqualsExpression:
                                return left == right ? 1 : 0;
                            case SyntaxKind.NotEqualsExpression:
                                return left != right ? 1 : 0;

                            default:
                                logger.Information("Unknown binary expression '{SyntaxKind}' in transpiled C# noise code, found in the C# expression '{Expression}'.", (SyntaxKind)binary.RawKind, binary);
                                return 1;
                        }
                    }

                // An identifier not followed by an argument list. This is a parameter, an expression, or a built-in variable/constant.
                // (https://lua-api.factorio.com/latest/auxiliary/noise-expressions.html#built-in-variables)
                case IdentifierNameSyntax { Identifier.Text: string name }: {
                        int idx = Array.IndexOf(localParameterNames, name);
                        if (idx >= 0 && estimateLocalParameter != null) {
                            return estimateLocalParameter(name, idx);
                        }
                        idx = Array.IndexOf(globalParameterNames, name);
                        if (idx >= 0 && estimateGlobalParameter != null) {
                            return estimateGlobalParameter(name, idx);
                        }
                        return name switch {
                            "e" => MathF.E,
                            "pi" => MathF.PI,
                            "inf" => float.PositiveInfinity,
                            "x" or "y" => EstimationDistanceFromCenter,
                            _ => EstimateIdentifier(name),
                        };
                    }

                // A function call. This is a call to a local, global, or built-in function (including the `var` pseudo-function).
                case InvocationExpressionSyntax { Expression: ExpressionSyntax name_, ArgumentList.Arguments: var args }: {
                        string name = name_.ToString();
                        if (localFunctions.Get(name, out LuaTable? targetLocal)) {
                            return EstimateLocalFunction(targetLocal, estimateOutboundParameter);
                        }
                        if (raw.Get<LuaTable>("noise-function").Get(name, out LuaTable? targetGlobal)) {
                            return EstimateGlobalFunction(targetGlobal, estimateOutboundParameter, raw);
                        }

                        if (name.EndsWith("_noise") || name == "multisample") {
                            // Estimate built-in noise functions as their output_scale.
                            if (outputScaleIndexes.TryGetValue(name, out int idx)) {
                                return estimateOutboundParameter("output_scale", idx);
                            }
                            return 1;
                        }

                        if (name.StartsWith("distance_from_")) {
                            return EstimationDistanceFromCenter;
                        }

                        // Remaining built-in functions (https://lua-api.factorio.com/latest/auxiliary/noise-expressions.html#built-in-functions)
                        switch (name) {
                            case "abs" when args.Count == 1:
                                return MathF.Abs(EstimateSyntax(args[0].Expression, localParameterNames, estimateLocalParameter));
                            case "atan2" when args.Count == 2:
                                return MathF.Atan2(estimateOutboundParameter("y", 0), estimateOutboundParameter("x", 1));
                            case "ceil" when args.Count == 1:
                                return MathF.Ceiling(EstimateSyntax(args[0].Expression, localParameterNames, estimateLocalParameter));
                            case "clamp" when args.Count == 3:
                                return Math.Clamp(estimateOutboundParameter("value", 0),
                                    estimateOutboundParameter("min", 1),
                                    estimateOutboundParameter("max", 2));
                            case "cos" when args.Count == 1:
                                return MathF.Cos(EstimateSyntax(args[0].Expression, localParameterNames, estimateLocalParameter));
                            case "floor" when args.Count == 1:
                                return MathF.Floor(EstimateSyntax(args[0].Expression, localParameterNames, estimateLocalParameter));
                            case "@if" when args.Count == 3:
                                return estimateOutboundParameter("condition", 0) != 0
                                    ? estimateOutboundParameter("true_branch", 1)
                                    : estimateOutboundParameter("false_branch", 2);
                            case "log2" when args.Count == 1:
                                return MathF.Log2(EstimateSyntax(args[0].Expression, localParameterNames, estimateLocalParameter));
                            case "max":
                                return args.Select(a => EstimateSyntax(a.Expression, localParameterNames, estimateLocalParameter)).Max();
                            case "min":
                                return args.Select(a => EstimateSyntax(a.Expression, localParameterNames, estimateLocalParameter)).Min();
                            case "pow" or "pow_precise" when args.Count == 2:
                                return MathF.Pow(estimateOutboundParameter("value", 0), estimateOutboundParameter("exponent", 1));
                            case "random_penalty" when args.Count >= 3:
                                // Matches 1.1 estimation
                                float amplitude = estimateOutboundParameter("amplitude", 4);
                                float value = estimateOutboundParameter("source", 2);
                                if (amplitude > value) {
                                    return value / amplitude;
                                }

                                return (value + value - amplitude) / 2;
                            case "ridge" when args.Count == 3:
                                // Matches 1.1 estimation
                                return (estimateOutboundParameter("min", 1) + estimateOutboundParameter("max", 2)) / 2;
                            case "sin" when args.Count == 1:
                                return MathF.Sin(EstimateSyntax(args[0].Expression, localParameterNames, estimateLocalParameter));
                            case "sqrt" when args.Count == 1:
                                return MathF.Sqrt(EstimateSyntax(args[0].Expression, localParameterNames, estimateLocalParameter));
                            case "terrace":
                                // Matches 1.1 estimation
                                return estimateOutboundParameter("value", 0);
                            case "var" when args.Count == 1:
                                string token = args[0].DescendantTokens().First().ValueText;
                                if (token.StartsWith("control:")) {
                                    return 1; // Assume all map settings are set to their default value.
                                }
                                return EstimateIdentifier(token);
                            default:
                                logger.Information("In a Lua noise expression, '{Function}' is unknown or has the wrong number of arguments. (Found {Count} arguments.)", name, args.Count);
                                return 1;
                        }

                        float estimateOutboundParameter(string name, int idx) {
                            foreach (ArgumentSyntax arg in args) {
                                if (arg.NameColon?.Name.ToString() == name) {
                                    return EstimateSyntax(arg.Expression, localParameterNames, estimateLocalParameter);
                                }
                            }
                            if (idx < args.Count) {
                                return EstimateSyntax(args[idx].Expression, localParameterNames, estimateLocalParameter);
                            }
                            if (defaultValues.TryGetValue(name, out float def)) {
                                return def;
                            }
                            logger.Information("In a Lua noise expression, '{Parameter}' (expected at 0-based index {Index}) was not supplied at the call site. " +
                                "Call site had {Count} arguments, named (if applicable) '{ArgumentNames}'.", name, idx, args.Count, string.Join(",", args.Select(a => a.NameColon?.Name.ToString())));
                            return 1;
                        }
                    }

                // Literal true, false, or number
                case LiteralExpressionSyntax { Token.Value: var number }:
                    try {
                        return Convert.ToSingle(number);
                    }
                    catch {
                        logger.Information("Could not parse '{Number}' from a Lua noise expression as a number.", number);
                        return 1;
                    }

                case ParenthesizedExpressionSyntax expr:
                    return EstimateSyntax(expr.Expression, localParameterNames, estimateLocalParameter);

                // {op} arg, for a supported unary operator
                case PrefixUnaryExpressionSyntax expr:
                    float arg = EstimateSyntax(expr.Operand, localParameterNames, estimateLocalParameter);
                    switch ((SyntaxKind)expr.RawKind) {
                        case SyntaxKind.UnaryPlusExpression:
                            return arg;
                        case SyntaxKind.UnaryMinusExpression:
                            return -arg;
                        case SyntaxKind.BitwiseNotExpression:
                            return ~(int)arg;
                        default:
                            logger.Information("Unknown unary expression '{SyntaxKind}' in transpiled C# noise code, found in the C# expression '{Expression}'.", (SyntaxKind)expr.RawKind, expr);
                            return 1;
                    }

                // left ^ right in Lua, translated to left .. right in C# for the tight binding of the range operator.
                // The range operator is left-associative, though, so we have to invert x^y^z's  Exp(Left:/*x^y*/ Exp(x, y), Right: z)
                case RangeExpressionSyntax range: {
                        Queue<ExpressionSyntax> expressions = [];
                        while (range.LeftOperand is RangeExpressionSyntax leftRange) {
                            expressions.Enqueue(range.RightOperand!);
                            range = leftRange!;
                        }
                        expressions.Enqueue(range.RightOperand!);
                        expressions.Enqueue(range.LeftOperand!);
                        float result = EstimateSyntax(expressions.Dequeue(), localParameterNames, estimateLocalParameter);
                        while (expressions.TryDequeue(out var left)) {
                            result = MathF.Pow(EstimateSyntax(left, localParameterNames, estimateLocalParameter), result);
                        }
                        return result;
                    }
                default:
                    logger.Information("Unknown expression type '{Type}' in transpiled C# noise code, found in the C# expression '{Expression}'.", node?.GetType(), node?.ToString());
                    return 1;
            }
        }

        /// <summary>
        /// Estimate the value for an identifier, either directly from the noise expression or found within a <c>var()</c> pseudo-call.
        /// These are always variables, constants, or expressions. Function names can only appear in method-call contexts, and the parameter names
        /// have already been checked.
        /// </summary>
        private float EstimateIdentifier(string name) {
            if (localExpressions.Get(name, out string? targetLocal)) {
                return EstimateLocalExpression(targetLocal);
            }
            if (localExpressions.Get(name, out float f)) {
                return f;
            }
            if (raw.Get<LuaTable>("noise-expression").Get(name, out LuaTable? targetGlobal)) {
                return Estimate(targetGlobal, "expression", raw);
            }
            if (name == "map_seed_normalized") {
                return 0.5f;
            }
            return 1;
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
                // If anything goes wrong, use fall-back estimation.
                logger.Information(ex, "Failed to transpile noise expression '{Expression}' into C#.", expression);
                return null;
            }
        }

        internal enum Token {
            Caret = '^',
            Plus = '+', Minus = '-', Tilde = '~',
            Asterisk = '*', Slash = '/', Percent = '%', PercentPercent = '%' | 0x80,
            Less = '<', LessOrEqual = '<' | 0x80, Greater = '>', GreaterOrEqual = '>' | 0x80,
            Equal = '=' | 0x80, NotEqual1 = '!' | 0x80, NotEqual2 = '~' | 0x80,
            And = '&', Xor = '^' | 0x80, Or = '|',

            OpenParen = '(', CloseParen = ')',
            OpenBrace = '{', CloseBrace = '}',
            Comma = ',', Equals = '=',
        }

        /// <summary>
        /// Transpile the token stream into a valid C# expression, which will then be parsed into a tree and walked.
        /// </summary>
        internal static string? Transpile(IEnumerable<object?> tokens) {
            StringBuilder cSharp = new();

            bool canHaveBinaryOperator = false;
            foreach (object? token in tokens) {
                switch (token) {
                    case null:
                        return null;
                    case string s when s.StartsWith('"') || s.StartsWith('\''): // Strings
                        s = s.Replace("\\", @"\\").Replace("\n", @"\n").Replace("\r", @"\r").Replace("\t", @"\t");

                        if (s.StartsWith('\'')) {
                            s = '"' + s[1..^1].Replace("\"", @"\""") + '"';
                        }
                        cSharp.Append(s);
                        canHaveBinaryOperator = false; // no string concatenation
                        break;

                    case "if":
                        cSharp.Append("@if");
                        canHaveBinaryOperator = true;
                        break;
                    case string identifier:
                        if (identifier.Contains(':')) {
                            cSharp.Append("var(\"" + identifier + "\")");
                        }
                        else {
                            cSharp.Append(token);
                        }
                        canHaveBinaryOperator = true;
                        break;

                    case float f:
                        cSharp.Append(f);
                        canHaveBinaryOperator = true;
                        break;

                    case Token.Caret:
                        cSharp.Append(".."); // Transpile exponentation into the tightly-binding range operator. Reinterpret it as Pow later.
                        canHaveBinaryOperator = false;
                        break;

                    case Token.Tilde when canHaveBinaryOperator: // xor
                        cSharp.Append('^');
                        canHaveBinaryOperator = false;
                        break;

                    case Token.Plus or Token.Minus or Token.Tilde or Token.Asterisk or Token.Slash or Token.Percent
                        or Token.Less or Token.Greater or Token.And or Token.Or or Token.Comma:
                        cSharp.Append((char)(Token)token);
                        canHaveBinaryOperator = false;
                        break;

                    case Token.PercentPercent:
                        cSharp.Append("%/**/"); // This trailing comment will be detected later to differentiate % from %%.
                        canHaveBinaryOperator = false;
                        break;
                    case Token.Equal:
                        cSharp.Append("==");
                        canHaveBinaryOperator = false;
                        break;
                    case Token.NotEqual1 or Token.NotEqual2:
                        cSharp.Append("!=");
                        canHaveBinaryOperator = false;
                        break;
                    case Token.GreaterOrEqual:
                        cSharp.Append(">=");
                        canHaveBinaryOperator = false;
                        break;
                    case Token.LessOrEqual:
                        cSharp.Append("<=");
                        canHaveBinaryOperator = false;
                        break;

                    case Token.OpenParen:
                    case Token.OpenBrace:
                        cSharp.Append('(');
                        canHaveBinaryOperator = false;
                        break;
                    case Token.CloseParen:
                    case Token.CloseBrace:
                        cSharp.Append(')');
                        canHaveBinaryOperator = true;
                        break;
                    case Token.Equals:
                        cSharp.Append(':');
                        canHaveBinaryOperator = false;
                        break;
                    default:
                        logger.Information("Ignoring unexpected token {Token} found while parsing a noise expression.", token);
                        break;
                }
            }

            return cSharp.Append(';').ToString();
        }

        /// <summary>
        /// Tokenize the input noise expression, so the non-C# tokens (e.g. %%, variables containing :, ~ as xor) can be converted to valid C#.
        /// </summary>
        internal static IEnumerable<object?> Tokenize(string expression) {
            string remainingExpression = expression;
            while (remainingExpression.Length > 0) {
                int startLength = remainingExpression.Length;

                switch (remainingExpression[0]) {
                    // Two-character tokens (all are operators):
                    case '%' when remainingExpression[1] == '%':
                    case '<' or '>' or '=' or '!' or '~' when remainingExpression[1] == '=':
                        yield return (Token)(remainingExpression[0] | 0x80);
                        remainingExpression = remainingExpression[2..];
                        break;

                    // Single-character tokens (operators and function calls):
                    case '^' or '+' or '-' or '~' or '*' or '/' or '%' or '<' or '>' or '&' or '|' or '(' or ')' or '{' or '}' or '=' or ',':
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
                        yield return float.Parse(number);
                    }
                }

                if (String().Match(remainingExpression) is { Success: true } str) {
                    remainingExpression = remainingExpression[str.Length..];
                    yield return str.ToString();
                }

                if (remainingExpression.Length == startLength) {
                    // Don't know how to tokenize this. Bail out, which will eventually cause this to be estimated as 1.
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
}
