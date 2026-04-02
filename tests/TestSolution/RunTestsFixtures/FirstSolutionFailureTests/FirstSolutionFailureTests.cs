using Xunit;

namespace RunTestsFixtures.MultiProjectFailures;

public sealed class FirstSolutionFailureTests
{
    [Fact]
    public void First_failing_test()
    {
        Assert.True(false, "first failure");
    }
}
