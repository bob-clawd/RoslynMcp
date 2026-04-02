using Xunit;

namespace RunTestsFixtures.PassingOnly;

public sealed class PassingOnlyTests
{
    [Fact]
    public void Existing_test()
    {
        Assert.True(true);
    }
}
