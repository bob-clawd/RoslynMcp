using Xunit;

namespace RoslynMcp.Features.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CurrentDirectorySensitiveCollection
{
    public const string Name = "CurrentDirectorySensitive";
}
