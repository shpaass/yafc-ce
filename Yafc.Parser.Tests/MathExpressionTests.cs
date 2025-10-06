using static Yafc.Parser.MathExpression;

namespace Yafc.Parser.Tests;

public class MathExpressionTests {
    // Note that these tests are cumulative. If there are test failures, fix tokenization first, transpilation second, and evaluation last.
    [Theory]
    [MemberData(nameof(TokenizationData))]
    public void TestTokenization(string input, List<object> output)
        => Assert.Equivalent(output, Tokenize(input));

    [Theory]
    [MemberData(nameof(TranspilationData))]
    public void TestTranspilation(string input, string output)
        => Assert.Equal(output, Transpile(Tokenize(input)));

    [Theory]
    [MemberData(nameof(EvaluationData))]
    public void TestEvaluation(string input, float output)
        => Assert.Equal(output, Evaluate(input, null));

    public static TheoryData<string, List<object?>> TokenizationData => new() {
        { "a * b", ["a", Token.Asterisk, "b"] },
        { "call(x, y)", ["call", Token.OpenParen, "x", Token.Comma, "y", Token.CloseParen] },
        { "x^y^z", ["x", Token.Caret, "y", Token.Caret, "z"] },
        { "0x1234 + 123.456", [4660f, Token.Plus, 123.456f] },
        { "log2(5)", ["log2", Token.OpenParen, 5f, Token.CloseParen] },
    };

    public static TheoryData<string, string> TranspilationData => new() {
        { "a * b", "@a*@b;" },
        { "call(x, y)", "@call(@x,@y);" },
        { "x^y^z", "@x..@y..@z;" },
        { "0x1234 + 123.456", "4660+123.456;" },
        { "log2(5)", "@log2(5);" },
    };

    public static TheoryData<string, float> EvaluationData => new() {
        { "1+2*3", 7 },
        { "2^3^2", 512 },
        { "2^(2^3)", 256 },
        { "(2^3)^2", 64 },
        { "0x1234 * 123.456", 0x1234 * 123.456f },
        { "log2(5)", MathF.Log2(5) },
    };
}
