using Is.Assertions;
using RoslynMcp.Core.Models;
using RoslynMcp.Features.Tests.Mutations;
using RoslynMcp.Features.Tools;
using RoslynMcp.Features.Tools.Inspections;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.Inspections.Tools;

public sealed class LoadSolutionToolTests(SharedSandboxFixture fixture, ITestOutputHelper output)
    : SharedToolTests<LoadSolutionTool>(fixture, output)
{
    [Fact]
    public void LoadSolutionAsync_WithAbsoluteSolutionPath_LoadsExpectedProjects()
    {
        var result = Context.LoadedSolution;

        result.SelectedSolutionPath.Is(Context.SolutionPath);
        string.Equals(Context.SolutionPath, Context.CanonicalSolutionPath, StringComparison.OrdinalIgnoreCase).IsFalse();
        result.Error.ShouldBeNone();
        result.Readiness.State.Is(WorkspaceReadinessStates.Ready);

        var projectNames = result.Projects.Select(static project => project.Name).ToArray();
        result.Projects.All(static project => !string.IsNullOrWhiteSpace(project.Path)).IsTrue();

        projectNames.IsContaining("ProjectApp");
        projectNames.IsContaining("ProjectCore");
        projectNames.IsContaining("ProjectImpl");
    }
}

[Collection(CurrentDirectorySensitiveCollection.Name)]
public sealed class LoadSolutionToolIsolatedTests(ITestOutputHelper output)
    : IsolatedToolTests<LoadSolutionTool>(output)
{
    [Fact]
    public async Task LoadSolutionAsync_WithWorkspaceRelativeSolutionPath_ReturnsRelativePaths()
    {
        await using var context = await WorkspaceRootSandboxContext.CreateAsync();
        var sut = context.GetRequiredService<LoadSolutionTool>();

        var result = await sut.ExecuteAsync(CancellationToken.None, "TestSolution.sln");

        result.Error.ShouldBeNone();
        result.SelectedSolutionPath.Is("TestSolution.sln");
        result.WorkspaceId.Is("TestSolution.sln");
        result.Projects.Any(project => project.Path == Path.Combine("ProjectApp", "ProjectApp.csproj")).IsTrue();
    }

    [Fact]
    public async Task LoadSolutionAsync_WithSlnxSolutionPath_LoadsRepositorySolution()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var solutionPath = Path.Combine(context.RepositoryRoot, "RoslynMcp.slnx");

        var result = await sut.ExecuteAsync(CancellationToken.None, solutionPath);

        result.Error.ShouldBeNone();
        result.SelectedSolutionPath.Is(solutionPath);
        result.Projects.Any(project => project.Name == "RoslynMcp.Features").IsTrue();
        result.Projects.Any(project => project.Name == "RoslynMcp.Infrastructure").IsTrue();
    }

    [Fact]
    public async Task LoadSolutionAsync_ExcludesGeneratedIntermediateDiagnosticsFromBaselineSummary()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var projectDirectory = Path.Combine(context.TestSolutionDirectory, "ProjectApp");
        var generatedPath = Path.Combine(projectDirectory, "obj", "Debug", "net10.0", "FreshWorktreeNoise.g.cs");
        var projectFilePath = Path.Combine(projectDirectory, "ProjectApp.csproj");

        Directory.CreateDirectory(Path.GetDirectoryName(generatedPath)!);
        await File.WriteAllTextAsync(generatedPath, "namespace ProjectApp; public static class FreshWorktreeNoise { public static void Broken( }", CancellationToken.None);

        var projectFile = await File.ReadAllTextAsync(projectFilePath, CancellationToken.None);
        projectFile = projectFile.Replace(
            "    <Compile Include=\"obj\\Debug\\net10.0\\GeneratedExecutionHooks.g.cs\" />",
            "    <Compile Include=\"obj\\Debug\\net10.0\\GeneratedExecutionHooks.g.cs\" />\n    <Compile Include=\"obj\\Debug\\net10.0\\FreshWorktreeNoise.g.cs\" />",
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(projectFilePath, projectFile, CancellationToken.None);

        var result = await sut.ExecuteAsync(CancellationToken.None, context.SolutionPath);

        result.Error.ShouldBeNone();
        result.BaselineDiagnostics.ErrorCount.Is(0);
        result.Readiness.State.Is(WorkspaceReadinessStates.DegradedRestoreRecommended);
        result.Readiness.DegradedReasons.IsContaining("generated_or_intermediate_diagnostics");
    }

    [Fact]
    public async Task LoadSolutionAsync_WithMissingGeneratedDocument_ReportsMissingArtifactsReadiness()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var generatedPath = Path.Combine(context.TestSolutionDirectory, "ProjectApp", "obj", "Debug", "net10.0", "GeneratedExecutionHooks.g.cs");

        File.Delete(generatedPath);

        var result = await sut.ExecuteAsync(CancellationToken.None, context.SolutionPath);

        result.Error.ShouldBeNone();
        result.Readiness.State.Is(WorkspaceReadinessStates.DegradedMissingArtifacts);
        result.Readiness.DegradedReasons.IsContaining("missing_artifacts");
        result.Readiness.RecommendedNextStep.IsNotNull();
    }

    private sealed class WorkspaceRootSandboxContext : SandboxContext
    {
        public static async Task<WorkspaceRootSandboxContext> CreateAsync(CancellationToken cancellationToken = default)
        {
            var context = new WorkspaceRootSandboxContext();
            try
            {
                var sandbox = TestSolutionSandbox.Create(context.CanonicalTestSolutionDirectory);
                using var currentDirectory = new CurrentDirectoryScope(sandbox.SolutionRoot);
                await context.InitializeSandboxAsync(sandbox, cancellationToken).ConfigureAwait(false);
                return context;
            }
            catch
            {
                await context.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
    }

    private sealed class CurrentDirectoryScope : IDisposable
    {
        private readonly string _originalDirectory;

        public CurrentDirectoryScope(string currentDirectory)
        {
            _originalDirectory = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(currentDirectory);
        }

        public void Dispose()
            => Directory.SetCurrentDirectory(Directory.Exists(_originalDirectory) ? _originalDirectory : AppContext.BaseDirectory);
    }
}
