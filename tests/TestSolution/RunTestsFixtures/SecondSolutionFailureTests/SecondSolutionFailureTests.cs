using Xunit;

namespace RunTestsFixtures.MultiProjectFailures;

public sealed class SecondSolutionFailureTests
{
    [Fact]
    public void Second_failing_test()
    {
        Assert.True(false, "second failure");
    }
}
