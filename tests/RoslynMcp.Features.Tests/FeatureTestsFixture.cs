using Microsoft.Extensions.DependencyInjection;
using RoslynMcp.Infrastructure;
using RoslynMcp.Core.Models;
using RoslynMcp.Features.Tools;
using Xunit;

namespace RoslynMcp.Features.Tests;

[CollectionDefinition(Name)]
public sealed class FeatureTestsCollection : ICollectionFixture<FeatureTestsFixture>
{
    public const string Name = "FeatureTests";
}

public sealed class FeatureTestsFixture : IAsyncLifetime
{
    private readonly ServiceProvider _provider = new ServiceCollection()
        .AddInfrastructure()
        .AddImplementations<Tool>()
        .BuildServiceProvider();

    public string SolutionPath { get; } = Path.GetFullPath(Path.Combine(GetRepositoryRoot(), "tests", "TestSolution", "TestSolution.sln"));

    public LoadSolutionResult LoadedSolution { get; private set; } = default!;

    public T GetRequiredService<T>() where T : notnull => _provider.GetRequiredService<T>();

    public ProjectSummary GetProject(string projectName) => LoadedSolution.Projects.Single(project => project.Name == projectName);

    public async Task InitializeAsync()
    {
        LoadedSolution = await GetRequiredService<LoadSolutionTool>().ExecuteAsync(CancellationToken.None, SolutionPath);

        LoadedSolution.Error.ShouldBeNone();
    }

    public Task DisposeAsync() => _provider.DisposeAsync().AsTask();

    private static string GetRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var markerPath = Path.Combine(current.FullName, "RoslynMcp.slnx");
            if (File.Exists(markerPath))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from AppContext.BaseDirectory.");
    }
}
