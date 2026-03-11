using Is.Assertions;
using RoslynMcp.Core;
using RoslynMcp.Features.Tools.Inspections;
using RoslynMcp.Features.Tools.Mutations;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.Mutations.Tools;

public sealed class ReplaceMethodToolTests(ITestOutputHelper output)
    : IsolatedToolTests<ReplaceMethodTool>(output)
{
    [Fact]
    public async Task ExecuteAsync_WithValidReplacement_ReplacesMethodAndReturnsNewSymbol()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var resolver = context.GetRequiredService<ResolveSymbolTool>();
        var filePath = context.GetFilePath("ProjectApp", "MethodMutationTestTarget");
        var targetMethodSymbolId = await ResolveMethodSymbolIdAsync(context, filePath, 5, 19);

        var result = await sut.ExecuteAsync(
            CancellationToken.None,
            targetMethodSymbolId,
            "Assess",
            "string",
            "public",
            Array.Empty<string>(),
            ["string input", "int priority", "bool isEnabled", "string tag"],
            "var resultTag = tag;\\r\\nreturn resultTag;");

        result.Error.ShouldBeNone();
        result.Status.Is("applied");
        result.ChangedFiles.Count.Is(1);
        result.ReplacedMethod.IsNotNull();
        result.TargetMethodSymbolId.ShouldBeExternalSymbolId();
        result.ReplacedMethod!.OriginalSymbolId.Is(targetMethodSymbolId);
        result.ReplacedMethod.NewSymbolId.ShouldBeExternalSymbolId();
        string.Equals(result.ReplacedMethod.NewSymbolId, targetMethodSymbolId, StringComparison.Ordinal).IsFalse();
        result.ReplacedMethod.NewSignature.Contains("Assess", StringComparison.Ordinal).IsTrue();
        result.DiagnosticsDelta.NewErrors.IsEmpty();
        result.DiagnosticsDelta.NewWarnings.IsEmpty();

        var text = await File.ReadAllTextAsync(filePath);
        text.Contains("public string Assess(string input, int priority, bool isEnabled, string tag)", StringComparison.Ordinal).IsTrue();
        text.Contains("var resultTag = tag;", StringComparison.Ordinal).IsTrue();
        text.Contains("return resultTag;", StringComparison.Ordinal).IsTrue();
        text.Contains("Evaluate", StringComparison.Ordinal).IsFalse();

        var oldResolution = await resolver.ExecuteAsync(CancellationToken.None, symbolId: targetMethodSymbolId);
        oldResolution.Error.ShouldHaveCode(ErrorCodes.SymbolNotFound);

        var newResolution = await resolver.ExecuteAsync(CancellationToken.None, symbolId: result.ReplacedMethod.NewSymbolId);
        newResolution.Error.ShouldBeNone();
    }

    [Fact]
    public async Task ExecuteAsync_WithGenericReturnType_ReplacesMethod()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var filePath = context.GetFilePath("ProjectApp", "MethodMutationTestTarget");
        var targetMethodSymbolId = await ResolveMethodSymbolIdAsync(context, filePath, 5, 19);

        var result = await sut.ExecuteAsync(
            CancellationToken.None,
            targetMethodSymbolId,
            "AssessAsync",
            "Task<int>",
            "public",
            Array.Empty<string>(),
            ["int priority"],
            "var task = Task.FromResult(priority);\\nreturn task;");

        result.Error.ShouldBeNone();
        result.Status.Is("applied");
        result.ReplacedMethod.IsNotNull();
        result.DiagnosticsDelta.NewErrors.IsEmpty();
        result.DiagnosticsDelta.NewWarnings.IsEmpty();

        var text = await File.ReadAllTextAsync(filePath);
        text.Contains("public Task<int> AssessAsync(int priority)", StringComparison.Ordinal).IsTrue();
        text.Contains("var task = Task.FromResult(priority);", StringComparison.Ordinal).IsTrue();
        text.Contains("return task;", StringComparison.Ordinal).IsTrue();
        text.Contains("public string Evaluate", StringComparison.Ordinal).IsFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithEscapedGenericTypeSyntax_ReplacesMethod()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var filePath = context.GetFilePath("ProjectApp", "MethodMutationTestTarget");
        var targetMethodSymbolId = await ResolveMethodSymbolIdAsync(context, filePath, 5, 19);

        var result = await sut.ExecuteAsync(
            CancellationToken.None,
            targetMethodSymbolId,
            "AssessEscapedAsync",
            "Task&lt;int&gt;",
            "public",
            Array.Empty<string>(),
            ["Task&lt;int&gt; task"],
            "var current = task;\\rreturn current;");

        result.Error.ShouldBeNone();
        result.Status.Is("applied");
        result.ReplacedMethod.IsNotNull();
        result.DiagnosticsDelta.NewErrors.IsEmpty();
        result.DiagnosticsDelta.NewWarnings.IsEmpty();

        var text = await File.ReadAllTextAsync(filePath);
        text.Contains("public Task<int> AssessEscapedAsync(Task<int> task)", StringComparison.Ordinal).IsTrue();
        text.Contains("var current = task;", StringComparison.Ordinal).IsTrue();
        text.Contains("return current;", StringComparison.Ordinal).IsTrue();
        text.Contains("public string Evaluate", StringComparison.Ordinal).IsFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithUnknownTargetSymbolId_ReturnsSymbolNotFoundWithoutChanges()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);

        var result = await sut.ExecuteAsync(
            CancellationToken.None,
            "not-a-real-symbol-id",
            "Assess",
            "string",
            "public",
            Array.Empty<string>(),
            ["string input"],
            "return input;");

        result.Error.ShouldHaveCode(ErrorCodes.SymbolNotFound);
        result.Status.Is("failed");
        result.ReplacedMethod.IsNull();
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

        var result = await sut.ExecuteAsync(
            CancellationToken.None,
            resolvedType.Symbol!.SymbolId,
            "Assess",
            "string",
            "public",
            Array.Empty<string>(),
            ["string input"],
            "return input;");

        result.Error.ShouldHaveCode(ErrorCodes.UnsupportedSymbolKind);
        result.Status.Is("failed");
        result.ReplacedMethod.IsNull();
        result.ChangedFiles.IsEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithIntroducedCompilerDiagnostic_ReturnsChangedDocumentDelta()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var filePath = context.GetFilePath("ProjectApp", "MethodMutationTestTarget");
        var targetMethodSymbolId = await ResolveMethodSymbolIdAsync(context, filePath, 5, 19);

        var result = await sut.ExecuteAsync(
            CancellationToken.None,
            targetMethodSymbolId,
            "Evaluate",
            "string",
            "public",
            Array.Empty<string>(),
            ["string input", "int priority", "bool isEnabled"],
            "return missingName;");

        result.Error.ShouldBeNone();
        result.Status.Is("applied");
        result.ReplacedMethod.IsNotNull();
        result.DiagnosticsDelta.NewErrors.Any(static diagnostic => diagnostic.Id == "CS0103").IsTrue();
        result.DiagnosticsDelta.NewWarnings.IsEmpty();
        result.DiagnosticsDelta.NewErrors.All(diagnostic => diagnostic.FilePath.HasPathSuffix(Path.Combine("ProjectApp", "MethodMutationTestTarget.cs"))).IsTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithMetadataMethod_ReturnsTargetNotSourceEditable()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var filePath = context.GetFilePath("ProjectApp", "AppOrchestrator");
        var metadataMethodSymbolId = await ResolveMethodSymbolIdAsync(context, filePath, 61, 21);

        var result = await sut.ExecuteAsync(
            CancellationToken.None,
            metadataMethodSymbolId,
            "Assess",
            "string",
            "public",
            Array.Empty<string>(),
            ["string input"],
            "return input;");

        result.Error.ShouldHaveCode(ErrorCodes.TargetNotSourceEditable);
        result.Status.Is("failed");
        result.ReplacedMethod.IsNull();
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
}
