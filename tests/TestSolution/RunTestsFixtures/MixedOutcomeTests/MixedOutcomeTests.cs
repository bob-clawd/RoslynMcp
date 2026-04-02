using Xunit;

namespace RunTestsFixtures.MixedOutcomes;

public sealed class MixedOutcomeTests
{
    [Fact]
    public void Passing_filter_test()
    {
        Assert.True(true);
    }

    [Fact]
    public void Trx_failure_test()
    {
        Assert.True(false, "plain xunit failure");
    }

    [Fact]
    public async Task Async_failure_test()
    {
        await Task.Yield();
        Assert.True(false, "async failure");
    }

    [Theory]
    [InlineData(2, 3)]
    public void Theory_failure_test(int expected, int actual)
    {
        Assert.Equal(expected, actual);
    }
}
