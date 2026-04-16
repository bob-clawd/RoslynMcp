using Is.Assertions;
using Microsoft.Extensions.DependencyInjection;
using RoslynMcp.Tools.Inspection.FindReferences;
using Xunit.Abstractions;

namespace RoslynMcp.Tools.Test.Inspections;

[TraceWatch]
public sealed class FindReferences(ITestOutputHelper output) : LoadedSolutionTests<McpTool>
{
    [Fact]
    public async Task MissingSymbolId_ReturnsError()
    {
        var result = await Sut.Execute(CancellationToken.None, symbolId: " ");
        output.WriteLine(result.ToJson());

        result.Error?.Message.Is("symbolId is required");
        result.References.Count.Is(0);
    }

    [Fact]
    public async Task UnknownSymbolId_ReturnsError()
    {
        var result = await Sut.Execute(CancellationToken.None, symbolId: "M-99999");
        output.WriteLine(result.ToJson());

        result.Error?.Message.Is("symbol not found");
        result.References.Count.Is(0);
    }

    [Fact]
    public async Task MemberSymbol_ReturnsContainingTypeContext()
    {
        var memberSymbolId = await GetMemberSymbolIdAsync("ProjectApp", "AppOrchestrator", "RunAsync");

        var result = await Sut.Execute(CancellationToken.None, memberSymbolId);
        output.WriteLine(result.ToJson());

        result.Error.IsNull();
        result.References.Count.Is(1);

        var reference = result.References.Single();
        reference.FilePath.Is(Path.Combine("ProjectApp", "AppOrchestrator.cs"));
        reference.ContainingTypeSymbolId.IsNotNull();

        var loadType = ServiceProvider.GetRequiredService<Inspection.LoadType.McpTool>();
        var type = await loadType.Execute(CancellationToken.None, reference.ContainingTypeSymbolId);

        type.Error.IsNull();
        type.Symbol!.DisplayName.Is("AppEntryPoints");
    }

    [Fact]
    public async Task TypeSymbol_DeduplicatesRepeatedReferencesInSameContainingType()
    {
        var typeSymbolId = await GetTypeSymbolIdAsync("ProjectCore", "Documentation");

        var result = await Sut.Execute(CancellationToken.None, typeSymbolId);
        output.WriteLine(result.ToJson());

        result.Error.IsNull();
        result.References.Count.Is(1);

        var reference = result.References.Single();
        reference.FilePath.Is(Path.Combine("ProjectCore", "Documentation.cs"));
        reference.ContainingTypeSymbolId.Is(typeSymbolId);
    }

    private async Task<string> GetTypeSymbolIdAsync(string projectName, string displayName)
    {
        var result = await ServiceProvider.GetRequiredService<Inspection.LoadProject.McpTool>()
            .Execute(CancellationToken.None, projectName);

        return result.Types.Single(type => type.Type?.DisplayName == displayName).Type?.SymbolId ?? string.Empty;
    }

    private async Task<string> GetMemberSymbolIdAsync(string projectName, string typeDisplayName, string memberDisplayName)
    {
        var typeSymbolId = await GetTypeSymbolIdAsync(projectName, typeDisplayName);
        var result = await ServiceProvider.GetRequiredService<Inspection.LoadType.McpTool>()
            .Execute(CancellationToken.None, typeSymbolId);

        return result.Members.Single(member => member.DisplayName.Contains(memberDisplayName)).SymbolId;
    }
}

[TraceWatch]
public sealed class FindReferencesGeneratedSources(ITestOutputHelper output) : SandboxTests<McpTool>
{
    [Fact]
    public async Task GeneratedContexts_AreDroppedWhenHandwrittenContextsExist()
    {
        await File.WriteAllTextAsync(
            Path.Combine(WorkspaceDirectory, "ProjectApp", "GeneratedDocumentationUse.g.cs"),
            """
            using ProjectCore;

            namespace ProjectApp;

            public static class GeneratedDocumentationUse
            {
                public static Documentation Create() => new Documentation();
            }
            """);

        LoadSolution();

        var typeSymbolId = await GetTypeSymbolIdAsync("ProjectCore", "Documentation");
        var result = await Sut.Execute(CancellationToken.None, typeSymbolId);
        output.WriteLine(result.ToJson());

        result.Error.IsNull();
        result.References.Count.Is(1);
        result.References.All(reference => !reference.FilePath.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)).IsTrue();
    }

    private async Task<string> GetTypeSymbolIdAsync(string projectName, string displayName)
    {
        var result = await ServiceProvider.GetRequiredService<Inspection.LoadProject.McpTool>()
            .Execute(CancellationToken.None, projectName);

        return result.Types.Single(type => type.Type?.DisplayName == displayName).Type?.SymbolId ?? string.Empty;
    }
}

[TraceWatch]
public sealed class FindReferencesWithoutSolution(ITestOutputHelper output) : Tests<McpTool>
{
    [Fact]
    public async Task MissingSolution_ReturnsGuidance()
    {
        var result = await Sut.Execute(CancellationToken.None, symbolId: "T-00001");
        output.WriteLine(result.ToJson());

        result.Error?.Message.Is("load solution first");
        result.References.Count.Is(0);
    }
}
