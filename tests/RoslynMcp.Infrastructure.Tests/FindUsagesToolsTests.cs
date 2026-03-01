using RoslynMcp.Core;
using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models.Common;
using RoslynMcp.Core.Models.Navigation;
using RoslynMcp.Infrastructure;
using RoslynMcp.Infrastructure.Workspace;
using Is.Assertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.DependencyInjection;

namespace RoslynMcp.Infrastructure.Tests;

[CollectionDefinition("RoslynFindUsages", DisableParallelization = true)]
public sealed class RoslynFindUsagesCollectionDefinition
{
}

[Collection("RoslynFindUsages")]
public sealed class FindUsagesToolsTests
{
    [Fact]
    public async Task FindUsages_DefaultSolutionScope_MatchesExplicitScopedSolution()
    {
        var service = CreateService(CreateMultiProjectSolution());
        var method = await ResolveMethodAsync(service, "DoWork");

        var unscoped = await service.FindReferencesAsync(new FindReferencesRequest(method.SymbolId), CancellationToken.None);
        var scoped = await service.FindReferencesScopedAsync(
            new FindReferencesScopedRequest(method.SymbolId, ReferenceScopes.Solution),
            CancellationToken.None);

        unscoped.Error.IsNull();
        scoped.Error.IsNull();
        unscoped.Symbol?.SymbolId.Is(scoped.Symbol?.SymbolId);
        unscoped.References.Select(ToLocationKey).Is(scoped.References.Select(ToLocationKey));
    }

    [Fact]
    public async Task FindUsagesScoped_RespectsDocumentProjectAndSolutionBoundaries()
    {
        var service = CreateService(CreateMultiProjectSolution());
        var method = await ResolveMethodAsync(service, "DoWork");

        var solutionScope = await service.FindReferencesScopedAsync(
            new FindReferencesScopedRequest(method.SymbolId, ReferenceScopes.Solution),
            CancellationToken.None);
        var projectScope = await service.FindReferencesScopedAsync(
            new FindReferencesScopedRequest(method.SymbolId, ReferenceScopes.Project),
            CancellationToken.None);
        var documentScope = await service.FindReferencesScopedAsync(
            new FindReferencesScopedRequest(method.SymbolId, ReferenceScopes.Document, "Service.cs"),
            CancellationToken.None);

        solutionScope.Error.IsNull();
        projectScope.Error.IsNull();
        documentScope.Error.IsNull();
        (solutionScope.TotalCount >= projectScope.TotalCount).IsTrue();
        (solutionScope.TotalCount >= documentScope.TotalCount).IsTrue();
        solutionScope.References.Any(r => r.FilePath == "UsageInA.cs").IsTrue();
        solutionScope.References.Any(r => r.FilePath == "Service.cs").IsTrue();
        projectScope.References.All(r => r.FilePath == "UsageInA.cs").IsTrue();
        documentScope.References.All(r => r.FilePath == "Service.cs").IsTrue();
    }

    [Fact]
    public async Task FindUsagesScoped_DocumentScopeWithoutPath_ReturnsValidationError()
    {
        var service = CreateService(CreateMultiProjectSolution());
        var method = await ResolveMethodAsync(service, "DoWork");

        var result = await service.FindReferencesScopedAsync(
            new FindReferencesScopedRequest(method.SymbolId, ReferenceScopes.Document),
            CancellationToken.None);

        result.Error.IsNotNull();
        result.Error?.Code.Is(ErrorCodes.InvalidRequest);
        result.Error?.Details?["parameter"].Is("path");
    }

    [Fact]
    public async Task FindUsages_WithInvalidSymbolId_ReturnsNotFoundError()
    {
        var service = CreateService(CreateMultiProjectSolution());

        var result = await service.FindReferencesAsync(
            new FindReferencesRequest("invalid-symbol-id"),
            CancellationToken.None);

        result.Error.IsNotNull();
        result.Error?.Code.Is(ErrorCodes.SymbolNotFound);
    }

    [Fact]
    public async Task FindUsagesScoped_WithInvalidSymbolId_ReturnsNotFoundError()
    {
        var service = CreateService(CreateMultiProjectSolution());

        var result = await service.FindReferencesScopedAsync(
            new FindReferencesScopedRequest("invalid-symbol-id", ReferenceScopes.Solution),
            CancellationToken.None);

        result.Error.IsNotNull();
        result.Error?.Code.Is(ErrorCodes.SymbolNotFound);
    }

    private static async Task<SymbolDescriptor> ResolveMethodAsync(INavigationService service, string name)
    {
        var candidatePaths = new[] { "Helper.cs", "UsageInA.cs", "Service.cs" };
        foreach (var path in candidatePaths)
        {
            for (var line = 1; line <= 30; line++)
            {
                for (var column = 1; column <= 80; column++)
                {
                    var atPosition = await service.GetSymbolAtPositionAsync(
                        new GetSymbolAtPositionRequest(path, line, column),
                        CancellationToken.None);

                    if (atPosition.Symbol != null && string.Equals(atPosition.Symbol.Name, name, StringComparison.Ordinal))
                    {
                        return atPosition.Symbol;
                    }
                }
            }
        }

        throw new InvalidOperationException($"Unable to resolve method symbol '{name}'.");
    }

    private static string ToLocationKey(SourceLocation location)
        => string.Join('|', location.FilePath, location.Line, location.Column);

    private static Solution CreateMultiProjectSolution()
    {
        var workspace = new AdhocWorkspace();
        var metadataReferences = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
        };

        var projectAId = ProjectId.CreateNewId();
        var projectBId = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution;
        solution = solution.AddProject(ProjectInfo.Create(
            projectAId,
            VersionStamp.Create(),
            "ProjectA",
            "ProjectA",
            LanguageNames.CSharp,
            filePath: "/repo/ProjectA/ProjectA.csproj",
            metadataReferences: metadataReferences));
        solution = solution.AddProject(ProjectInfo.Create(
            projectBId,
            VersionStamp.Create(),
            "ProjectB",
            "ProjectB",
            LanguageNames.CSharp,
            filePath: "/repo/ProjectB/ProjectB.csproj",
            metadataReferences: metadataReferences));

        var helperCode = """
namespace ProjectA;

public static class Helper
{
    public static void DoWork()
    {
    }
}
""";

        var usageInProjectA = """
namespace ProjectA;

public sealed class Runner
{
    public void Run()
    {
        Helper.DoWork();
    }
}
""";

        var helperId = DocumentId.CreateNewId(projectAId);
        solution = solution.AddDocument(helperId, "Helper.cs", SourceText.From(helperCode), filePath: "Helper.cs");
        var usageInAId = DocumentId.CreateNewId(projectAId);
        solution = solution.AddDocument(usageInAId, "UsageInA.cs", SourceText.From(usageInProjectA), filePath: "UsageInA.cs");
        solution = solution.AddProjectReference(projectBId, new ProjectReference(projectAId));

        var serviceCode = """
namespace ProjectB;

public sealed class Service
{
    public void Execute()
    {
        ProjectA.Helper.DoWork();
    }
}
""";

        var serviceId = DocumentId.CreateNewId(projectBId);
        solution = solution.AddDocument(serviceId, "Service.cs", SourceText.From(serviceCode), filePath: "Service.cs");

        workspace.TryApplyChanges(solution);
        return workspace.CurrentSolution;
    }

    private static INavigationService CreateService(Solution solution)
    {
        var services = new ServiceCollection();
        services.AddRoslynMcpInfrastructure();
        services.AddSingleton<IRoslynSolutionAccessor>(new TestSolutionAccessor(solution));
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<INavigationService>();
    }

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
