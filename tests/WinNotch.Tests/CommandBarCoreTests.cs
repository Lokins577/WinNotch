using WinNotch.CommandBar;
using Xunit;

namespace WinNotch.Tests;

public sealed class CommandBarCoreTests
{
    [Theory]
    [InlineData("2 + 3 * 4", 14)]
    [InlineData("2^3^2", 512)]
    [InlineData("sqrt(81) + abs(-1)", 10)]
    [InlineData("round(pi)", 3)]
    public void ExpressionEvaluator_UsesSafeDeterministicGrammar(string expression, double expected)
    {
        Assert.True(ExpressionEvaluator.TryEvaluate(expression, out double result));
        Assert.Equal(expected, result, 8);
    }

    [Theory]
    [InlineData("1 / 0")]
    [InlineData("Process.Start('cmd')")]
    [InlineData("2 +")]
    public void ExpressionEvaluator_RejectsInvalidOrUnsafeInput(string expression)
    {
        Assert.False(ExpressionEvaluator.TryEvaluate(expression, out _));
    }

    [Theory]
    [InlineData("10 km to mi", 6.2137119224)]
    [InlineData("32 f to c", 0)]
    [InlineData("1 gib to mib", 1024)]
    [InlineData("2 h in min", 120)]
    public void UnitConverter_ConvertsKnownFamilies(string query, double expected)
    {
        Assert.True(UnitConverter.TryConvert(query, out var conversion));
        Assert.NotNull(conversion);
        Assert.Equal(expected, conversion!.Output, 6);
    }

    [Fact]
    public void EmojiCatalog_SearchesKeywords()
    {
        var result = EmojiCatalog.Search("rocket");

        Assert.Equal("🚀", Assert.Single(result).Emoji);
    }

    [Fact]
    public async Task Engine_MergesProvidersAndIsolatesFailures()
    {
        var engine = new CommandBarEngine(new ICommandProvider[]
        {
            new StaticProvider("low", [new CommandResult("1", "Low", null, CommandResultKind.Command, 1)]),
            new ThrowingProvider(),
            new StaticProvider("high", [new CommandResult("2", "High", null, CommandResultKind.Command, 10)])
        });

        var results = await engine.SearchAsync("anything");

        Assert.Equal(["High", "Low"], results.Select(result => result.Title));
    }

    private sealed class StaticProvider(string id, IReadOnlyList<CommandResult> results) : ICommandProvider
    {
        public string Id => id;
        public ValueTask<IReadOnlyList<CommandResult>> SearchAsync(string query, CancellationToken cancellationToken)
            => ValueTask.FromResult(results);
    }

    private sealed class ThrowingProvider : ICommandProvider
    {
        public string Id => "throwing";
        public ValueTask<IReadOnlyList<CommandResult>> SearchAsync(string query, CancellationToken cancellationToken)
            => ValueTask.FromException<IReadOnlyList<CommandResult>>(new InvalidOperationException("provider failed"));
    }
}
