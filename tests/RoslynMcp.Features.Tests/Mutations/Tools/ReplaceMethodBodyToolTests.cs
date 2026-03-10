using Is.Assertions;
using RoslynMcp.Core;
using RoslynMcp.Features.Tools.Inspections;
using RoslynMcp.Features.Tools.Mutations;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.Mutations.Tools;

public sealed class ReplaceMethodBodyToolTests(ITestOutputHelper output)
    : IsolatedToolTests<ReplaceMethodBodyTool>(output)
{
    [Fact]
    public async Task ExecuteAsync_WithValidBody_ReplacesBodyAndPreservesSignature()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var resolver = context.GetRequiredService<ResolveSymbolTool>();
        var filePath = context.GetFilePath("ProjectApp", "MethodMutationTestTarget");
        var targetMethodSymbolId = await ResolveMethodSymbolIdAsync(context, filePath, 5, 19);

        var result = await sut.ExecuteAsync(CancellationToken.None, targetMethodSymbolId, "return input + priority.ToString();");

        result.Error.ShouldBeNone();
        result.Status.Is("applied");
        result.ChangedFiles.Count.Is(1);
        result.ReplacedMethodBody.IsNotNull();
        result.ReplacedMethodBody!.MethodSymbolId.ShouldNotBeEmpty();
        result.ReplacedMethodBody.Signature.Contains("Evaluate", StringComparison.Ordinal).IsTrue();
        result.DiagnosticsDelta.NewErrors.IsEmpty();
        result.DiagnosticsDelta.NewWarnings.IsEmpty();

        var text = await File.ReadAllTextAsync(filePath);
        text.Contains("public string Evaluate(string input, int priority, bool isEnabled)", StringComparison.Ordinal).IsTrue();
        text.Contains("return input + priority.ToString();", StringComparison.Ordinal).IsTrue();
        text.Contains("return string.Empty;", StringComparison.Ordinal).IsFalse();

        var resolved = await resolver.ExecuteAsync(CancellationToken.None, symbolId: result.ReplacedMethodBody.MethodSymbolId);
        resolved.Error.ShouldBeNone();
    }

    [Fact]
    public async Task ExecuteAsync_WithUnknownTargetSymbolId_ReturnsSymbolNotFoundWithoutChanges()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);

        var result = await sut.ExecuteAsync(CancellationToken.None, "not-a-real-symbol-id", "return string.Empty;");

        result.Error.ShouldHaveCode(ErrorCodes.SymbolNotFound);
        result.Status.Is("failed");
        result.ReplacedMethodBody.IsNull();
        result.ChangedFiles.IsEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithNonMethodSymbol_ReturnsUnsupportedSymbolKind()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var resolver = context.GetRequiredService<ResolveSymbolTool>();
        var resolvedType = await resolver.ExecuteAsync(
            CancellationToken.None,
            qualifiedName: "ProjectApp.MethodMutationTestTarget",
            projectName: "ProjectApp");

        resolvedType.Error.ShouldBeNone();
        resolvedType.Symbol.IsNotNull();

        var result = await sut.ExecuteAsync(CancellationToken.None, resolvedType.Symbol!.SymbolId, "return string.Empty;");

        result.Error.ShouldHaveCode(ErrorCodes.UnsupportedSymbolKind);
        result.Status.Is("failed");
        result.ReplacedMethodBody.IsNull();
        result.ChangedFiles.IsEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithIntroducedCompilerDiagnostic_ReturnsChangedDocumentDelta()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var filePath = context.GetFilePath("ProjectApp", "MethodMutationTestTarget");
        var targetMethodSymbolId = await ResolveMethodSymbolIdAsync(context, filePath, 5, 19);

        var result = await sut.ExecuteAsync(CancellationToken.None, targetMethodSymbolId, "return missingName;");

        result.Error.ShouldBeNone();
        result.Status.Is("applied");
        result.ReplacedMethodBody.IsNotNull();
        result.DiagnosticsDelta.NewErrors.Any(static diagnostic => diagnostic.Id == "CS0103").IsTrue();
        result.DiagnosticsDelta.NewWarnings.IsEmpty();
        result.DiagnosticsDelta.NewErrors.All(diagnostic => diagnostic.FilePath.HasPathSuffix(Path.Combine("ProjectApp", "MethodMutationTestTarget.cs"))).IsTrue();
    }

    [Fact]
    public async Task ExecuteAsync_AfterAddMethodInSameSession_ReplacesBodyWithoutApplyFailure()
    {
        await using var context = await CreateContextAsync();
        var replaceMethodBodyTool = GetSut(context);
        var addMethodTool = context.GetRequiredService<AddMethodTool>();
        var targetTypeSymbolId = await ResolveMethodMutationTestTargetAsync(context);
        var filePath = context.GetFilePath("ProjectApp", "MethodMutationTestTarget");
        var targetMethodSymbolId = await ResolveMethodSymbolIdAsync(context, filePath, 5, 19);

        var addResult = await addMethodTool.ExecuteAsync(
            CancellationToken.None,
            targetTypeSymbolId,
            "Plan",
            "string",
            "public",
            Array.Empty<string>(),
            ["string input", "int priority", "bool isEnabled"],
            "return string.Empty;");

        addResult.Error.ShouldBeNone();
        addResult.Status.Is("applied");

        var replaceResult = await replaceMethodBodyTool.ExecuteAsync(
            CancellationToken.None,
            targetMethodSymbolId,
            "return input + \"-updated\";");

        replaceResult.Error.ShouldBeNone();
        replaceResult.Status.Is("applied");
        replaceResult.ReplacedMethodBody.IsNotNull();

        var text = await File.ReadAllTextAsync(filePath);
        text.Contains("public string Plan(string input, int priority, bool isEnabled)", StringComparison.Ordinal).IsTrue();
        text.Contains("return input + \"-updated\";", StringComparison.Ordinal).IsTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithMetadataMethod_ReturnsTargetNotSourceEditable()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var filePath = context.GetFilePath("ProjectApp", "AppOrchestrator");
        var metadataMethodSymbolId = await ResolveMethodSymbolIdAsync(context, filePath, 61, 21);

        var result = await sut.ExecuteAsync(CancellationToken.None, metadataMethodSymbolId, "return string.Empty;");

        result.Error.ShouldHaveCode(ErrorCodes.TargetNotSourceEditable);
        result.Status.Is("failed");
        result.ReplacedMethodBody.IsNull();
        result.ChangedFiles.IsEmpty();
    }

    private static async Task<string> ResolveMethodSymbolIdAsync(IsolatedSandboxContext context, string path, int line, int column)
    {
        var resolver = context.GetRequiredService<ResolveSymbolTool>();
        var resolved = await resolver.ExecuteAsync(CancellationToken.None, path: path, line: line, column: column);

        resolved.Error.ShouldBeNone();
        resolved.Symbol.IsNotNull();
        return resolved.Symbol!.SymbolId;
    }

    private static async Task<string> ResolveMethodMutationTestTargetAsync(IsolatedSandboxContext context)
    {
        var resolver = context.GetRequiredService<ResolveSymbolTool>();
        var resolved = await resolver.ExecuteAsync(
            CancellationToken.None,
            qualifiedName: "ProjectApp.MethodMutationTestTarget",
            projectName: "ProjectApp");

        resolved.Error.ShouldBeNone();
        resolved.Symbol.IsNotNull();
        return resolved.Symbol!.SymbolId;
    }
}
