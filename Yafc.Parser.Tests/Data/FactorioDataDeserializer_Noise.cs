using static Yafc.Parser.FactorioDataDeserializer.Noise;

namespace Yafc.Parser.Data.Tests;

public class FactorioDataDeserializer_Noise {
    // Note that these tests are cumulative. If there are test failures, fix tokenization first, transpilation second, and estimation last.
    [Theory]
    [MemberData(nameof(TokenizationData))]
    public void TestTokenization(string input, List<object?> output)
        => Assert.Equivalent(output, Tokenize(input));

    [Theory]
    [MemberData(nameof(TranspilationData))]
    public void TestTranspilation(string input, string? output)
        => Assert.Equal(output, Transpile(Tokenize(input)));

    [Theory]
    [MemberData(nameof(EstimationData))]
    public void TestEstimation(string input, float output)
        => Assert.Equal(output, new FactorioDataDeserializer.Noise(null!, null!, null).EstimateRootExpression(input));

    public static TheoryData<string, List<object?>> TokenizationData => new() {
        { "a ~ b", ["a", Token.Tilde, "b"] },
        { "~ b", [Token.Tilde, "b"] },
        { "call{x = x, y = y}", ["call", Token.OpenBrace, "x", Token.Equals, "x", Token.Comma, "y", Token.Equals, "y", Token.CloseBrace] },
        { "x^y^z", ["x", Token.Caret, "y", Token.Caret, "z"] },
        { "call1(call2{x = x, y = y}, 3*q)", ["call1", Token.OpenParen, "call2", Token.OpenBrace, "x", Token.Equals, "x", Token.Comma, "y", Token.Equals, "y", Token.CloseBrace, Token.Comma, 3f, Token.Asterisk, "q", Token.CloseParen] },
        { "if(a, b, c)", ["if", Token.OpenParen, "a", Token.Comma, "b", Token.Comma, "c", Token.CloseParen] },
        { "a %% b", ["a", Token.PercentPercent, "b"] },
        { "0x1234 | 123.456", [4660f, Token.Or, 123.456f] },
        { "$", [null] },
    };

    public static TheoryData<string, string?> TranspilationData => new() {
        { "a ~ b", "@a^@b;" },
        { "~ b", "~@b;" },
        { "call{x = x, y = y}", "@call(@x:@x,@y:@y);" },
        { "x^y^z", "@x..@y..@z;" },
        { "call1(call2{x = x, y = y}, 3*q)", "@call1(@call2(@x:@x,@y:@y),3*@q);" },
        { "if(a, b, c)", "@if(@a,@b,@c);" },
        { "a %% b", "@a%/**/@b;" },
        { "0x1234 | 123.456", "4660|123.456;" },
        { "$", null },
    };

    public static TheoryData<string, float> EstimationData => new() {
        { "1+2*3", 7 },
        { "2^3^2", 512 },
        { "2^(2^3)", 256 },
        { "(2^3)^2", 64 },
        { "3~5", 6 },
        { "~1", ~1 },
        { "0x1234 | 123.456", 0x1234 | 123 },
        { "clamp(5, -3, 3)", 3 },
        { "clamp{min=-3, value=5, max=2}", 2 },
    };
}
