using RoslynMcp.Core;
using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models.Agent;
using RoslynMcp.Core.Models.Common;
using RoslynMcp.Infrastructure;
using RoslynMcp.Infrastructure.Workspace;
using Is.Assertions;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace RoslynMcp.Infrastructure.Tests;

[CollectionDefinition("RoslynListDependencies", DisableParallelization = true)]
public sealed class RoslynListDependenciesCollectionDefinition
{
}

[Collection("RoslynListDependencies")]
public sealed class ListDependenciesToolsTests
{
    [Fact]
    public async Task ListDependencies_SelectedProject_SupportsPathNameAndIdSelectors()
    {
        var fixture = CreateDependencySolution();
        var service = CreateService(fixture.Solution);

        var byPath = await service.ListDependenciesAsync(
            new ListDependenciesRequest(ProjectPath: fixture.WebProject.FilePath, Direction: "outgoing"),
            CancellationToken.None);
        var byName = await service.ListDependenciesAsync(
            new ListDependenciesRequest(ProjectName: fixture.WebProject.Name, Direction: "outgoing"),
            CancellationToken.None);
        var byId = await service.ListDependenciesAsync(
            new ListDependenciesRequest(ProjectId: fixture.WebProject.Id.Id.ToString(), Direction: "outgoing"),
            CancellationToken.None);

        byPath.Error.IsNull();
        byName.Error.IsNull();
        byId.Error.IsNull();
        byPath.Edges?.Select(EdgeKey).Is(byName.Edges?.Select(EdgeKey));
        byPath.Edges?.Select(EdgeKey).Is(byId.Edges?.Select(EdgeKey));
        byPath.Dependencies.Select(static d => d.ProjectName).Is(["App", "Common"]);
    }

    [Fact]
    public async Task ListDependencies_RejectsMultipleSelectors()
    {
        var fixture = CreateDependencySolution();
        var service = CreateService(fixture.Solution);

        var result = await service.ListDependenciesAsync(
            new ListDependenciesRequest(
                ProjectPath: fixture.AppProject.FilePath,
                ProjectId: fixture.AppProject.Id.Id.ToString(),
                Direction: "outgoing"),
            CancellationToken.None);

        result.Error.IsNotNull();
        result.Error?.Code.Is(ErrorCodes.InvalidInput);
    }

    [Fact]
    public async Task ListDependencies_RejectsAmbiguousProjectName()
    {
        var fixture = CreateDependencySolution();
        var service = CreateService(fixture.Solution);

        var result = await service.ListDependenciesAsync(
            new ListDependenciesRequest(ProjectName: "App", Direction: "outgoing"),
            CancellationToken.None);

        result.Error.IsNotNull();
        result.Error?.Code.Is(ErrorCodes.AmbiguousSymbol);
        result.Error?.Details?["field"].Is("projectName");
    }

    [Fact]
    public async Task ListDependencies_RejectsInvalidDirection()
    {
        var fixture = CreateDependencySolution();
        var service = CreateService(fixture.Solution);

        var result = await service.ListDependenciesAsync(
            new ListDependenciesRequest(ProjectId: fixture.AppProject.Id.Id.ToString(), Direction: "sideways"),
            CancellationToken.None);

        result.Error.IsNotNull();
        result.Error?.Code.Is(ErrorCodes.InvalidInput);
        result.Error?.Details?["field"].Is("direction");
    }

    [Fact]
    public async Task ListDependencies_SelectedProject_OutgoingIncomingAndBoth_AreEdgeAware()
    {
        var fixture = CreateDependencySolution();
        var service = CreateService(fixture.Solution);
        var selectedId = fixture.AppProject.Id.Id.ToString();

        var outgoing = await service.ListDependenciesAsync(
            new ListDependenciesRequest(ProjectId: selectedId, Direction: "outgoing"),
            CancellationToken.None);
        var incoming = await service.ListDependenciesAsync(
            new ListDependenciesRequest(ProjectId: selectedId, Direction: "incoming"),
            CancellationToken.None);
        var both = await service.ListDependenciesAsync(
            new ListDependenciesRequest(ProjectId: selectedId, Direction: "both"),
            CancellationToken.None);

        outgoing.Error.IsNull();
        incoming.Error.IsNull();
        both.Error.IsNull();

        outgoing.Edges?.Select(EdgeKey).Is([EdgeKey(fixture.AppProject, fixture.CommonProject)]);
        incoming.Edges?.Select(EdgeKey).Is([
            EdgeKey(fixture.ToolsProject, fixture.AppProject),
            EdgeKey(fixture.WebProject, fixture.AppProject)
        ]);
        both.Edges?.Select(EdgeKey).Is([
            EdgeKey(fixture.AppProject, fixture.CommonProject),
            EdgeKey(fixture.ToolsProject, fixture.AppProject),
            EdgeKey(fixture.WebProject, fixture.AppProject)
        ]);
    }

    [Fact]
    public async Task ListDependencies_NoSelector_OutgoingIncomingAndBoth_ReturnDeterministicGraphViews()
    {
        var fixture = CreateDependencySolution();
        var service = CreateService(fixture.Solution);

        var outgoing = await service.ListDependenciesAsync(
            new ListDependenciesRequest(Direction: "outgoing"),
            CancellationToken.None);
        var incoming = await service.ListDependenciesAsync(
            new ListDependenciesRequest(Direction: "incoming"),
            CancellationToken.None);
        var both = await service.ListDependenciesAsync(
            new ListDependenciesRequest(Direction: "both"),
            CancellationToken.None);

        outgoing.Error.IsNull();
        incoming.Error.IsNull();
        both.Error.IsNull();

        outgoing.Edges?.Select(EdgeKey).Is([
            EdgeKey(fixture.AppProject, fixture.CommonProject),
            EdgeKey(fixture.ToolsProject, fixture.AppProject),
            EdgeKey(fixture.WebProject, fixture.AppProject),
            EdgeKey(fixture.WebProject, fixture.CommonProject)
        ]);

        incoming.Edges?.Select(EdgeKey).Is([
            EdgeKey(fixture.AppProject, fixture.ToolsProject),
            EdgeKey(fixture.AppProject, fixture.WebProject),
            EdgeKey(fixture.CommonProject, fixture.AppProject),
            EdgeKey(fixture.CommonProject, fixture.WebProject)
        ]);

        both.Edges?.Select(EdgeKey).Is([
            EdgeKey(fixture.AppProject, fixture.CommonProject),
            EdgeKey(fixture.AppProject, fixture.ToolsProject),
            EdgeKey(fixture.AppProject, fixture.WebProject),
            EdgeKey(fixture.CommonProject, fixture.AppProject),
            EdgeKey(fixture.CommonProject, fixture.WebProject),
            EdgeKey(fixture.ToolsProject, fixture.AppProject),
            EdgeKey(fixture.WebProject, fixture.AppProject),
            EdgeKey(fixture.WebProject, fixture.CommonProject)
        ]);
    }

    private static string EdgeKey(ProjectDependencyEdge edge)
        => $"{edge.Source.ProjectName}:{edge.Source.ProjectId}->{edge.Target.ProjectName}:{edge.Target.ProjectId}";

    private static string EdgeKey(Project from, Project to)
        => $"{from.Name}:{from.Id.Id}->{to.Name}:{to.Id.Id}";

    private static ICodeUnderstandingService CreateService(Solution solution)
    {
        var services = new ServiceCollection();
        services.AddRoslynMcpInfrastructure();
        services.AddSingleton<IRoslynSolutionAccessor>(new TestSolutionAccessor(solution));
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<ICodeUnderstandingService>();
    }

    private static DependencyFixture CreateDependencySolution()
    {
        var workspace = new AdhocWorkspace();
        var metadataReferences = new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) };

        var commonId = ProjectId.CreateNewId();
        var appId = ProjectId.CreateNewId();
        var webId = ProjectId.CreateNewId();
        var toolsId = ProjectId.CreateNewId();
        var duplicateAppId = ProjectId.CreateNewId();

        var solution = workspace.CurrentSolution;
        solution = AddProject(solution, commonId, "Common", "/repo/Common/Common.csproj", metadataReferences);
        solution = AddProject(solution, appId, "App", "/repo/App/App.csproj", metadataReferences);
        solution = AddProject(solution, webId, "Web", "/repo/Web/Web.csproj", metadataReferences);
        solution = AddProject(solution, toolsId, "Tools", "/repo/Tools/Tools.csproj", metadataReferences);
        solution = AddProject(solution, duplicateAppId, "App", "/repo/App.Legacy/App.csproj", metadataReferences);

        solution = solution.AddProjectReference(appId, new ProjectReference(commonId));
        solution = solution.AddProjectReference(webId, new ProjectReference(commonId));
        solution = solution.AddProjectReference(webId, new ProjectReference(appId));
        solution = solution.AddProjectReference(toolsId, new ProjectReference(appId));
        workspace.TryApplyChanges(solution);

        var current = workspace.CurrentSolution;
        var common = current.GetProject(commonId)!;
        var app = current.GetProject(appId)!;
        var web = current.GetProject(webId)!;
        var tools = current.GetProject(toolsId)!;
        var duplicateApp = current.GetProject(duplicateAppId)!;

        return new DependencyFixture(
            current,
            common,
            app,
            web,
            tools,
            duplicateApp);
    }

    private static Solution AddProject(
        Solution solution,
        ProjectId projectId,
        string name,
        string filePath,
        IReadOnlyList<MetadataReference> metadataReferences)
    {
        var info = ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            name,
            name,
            LanguageNames.CSharp,
            filePath: filePath,
            metadataReferences: metadataReferences);

        return solution.AddProject(info);
    }

    private sealed record DependencyFixture(
        Solution Solution,
        Project CommonProject,
        Project AppProject,
        Project WebProject,
        Project ToolsProject,
        Project DuplicateAppProject);

    private sealed class TestSolutionAccessor : IRoslynSolutionAccessor
    {
        private readonly Solution _solution;

        public TestSolutionAccessor(Solution solution)
        {
            _solution = solution;
        }

        public Task<(Solution? Solution, ErrorInfo? Error)> GetCurrentSolutionAsync(CancellationToken ct)
            => Task.FromResult(((Solution?)_solution, (ErrorInfo?)null));

        public Task<(bool Applied, ErrorInfo? Error)> TryApplySolutionAsync(Solution solution, CancellationToken ct)
            => Task.FromResult(((bool)true, (ErrorInfo?)null));

        public Task<(int Version, ErrorInfo? Error)> GetWorkspaceVersionAsync(CancellationToken ct)
            => Task.FromResult((1, (ErrorInfo?)null));
    }
}
