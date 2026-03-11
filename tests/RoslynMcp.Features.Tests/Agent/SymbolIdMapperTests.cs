using RoslynMcp.Infrastructure.Agent;
using Xunit;

namespace RoslynMcp.Features.Tests.Agent;

public sealed class SymbolIdMapperTests
{
    [Fact]
    public async Task ToExternal_WhenCalledConcurrentlyForSameInternalId_ReturnsSingleExternalId()
    {
        var internalId = $"test-symbol-{Guid.NewGuid():N}";
        using var start = new ManualResetEventSlim(false);

        var tasks = Enumerable.Range(0, 64)
            .Select(_ => Task.Run(() =>
            {
                start.Wait();
                return internalId.ToExternal();
            }))
            .ToArray();

        start.Set();

        var externalIds = await Task.WhenAll(tasks);
        var externalId = Assert.Single(externalIds.Distinct());

        Assert.Equal(externalId, internalId.ToExternal());
        Assert.True(externalId.TryToInternal(out var mappedInternalId));
        Assert.Equal(internalId, mappedInternalId);
    }

    [Fact]
    public void ToInternal_WhenExternalIdIsUnknown_ThrowsKeyNotFoundException()
    {
        var unknownExternalId = $"S+missing-{Guid.NewGuid():N}";

        Assert.Throws<KeyNotFoundException>(() => unknownExternalId.ToInternal());
    }
}
