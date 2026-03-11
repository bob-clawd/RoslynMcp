using Is.Assertions;
using RoslynMcp.Core;
using RoslynMcp.Core.Models;
using RoslynMcp.Features.Tools.Inspections;
using RoslynMcp.Features.Tools.Mutations;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.Mutations.Tools;

public sealed class AddMethodToolTests(ITestOutputHelper output)
    : IsolatedToolTests<AddMethodTool>(output)
{
    [Fact]
    public async Task ExecuteAsync_WithValidMethod_AddsMethodAndReturnsCreatedSymbol()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var targetTypeSymbolId = await ResolveMethodMutationTestTargetAsync(context);
        var filePath = context.GetFilePath("ProjectApp", "MethodMutationTestTarget");

        var result = await sut.ExecuteAsync(
            CancellationToken.None,
            targetTypeSymbolId,
            "Plan",
            "string",
            "public",
            Array.Empty<string>(),
            ["string input", "int priority", "bool isEnabled"],
            "var plan = string.Empty;\\r\\nreturn plan;");

        result.Error.ShouldBeNone();
        result.Status.Is("applied");
        result.ChangedFiles.Count.Is(1);
        result.ChangedFiles[0].ShouldEndWithPathSuffix(Path.Combine("ProjectApp", "MethodMutationTestTarget.cs"));
        result.AddedMethod.IsNotNull();
        result.TargetTypeSymbolId.ShouldBeExternalSymbolId();
        result.AddedMethod!.SymbolId.ShouldBeExternalSymbolId();
        result.AddedMethod.Signature.Contains("Plan", StringComparison.Ordinal).IsTrue();
        result.DiagnosticsDelta.NewErrors.IsEmpty();
        result.DiagnosticsDelta.NewWarnings.IsEmpty();

        var text = await File.ReadAllTextAsync(filePath);
        text.Contains("public string Plan(string input, int priority, bool isEnabled)", StringComparison.Ordinal).IsTrue();
        text.Contains("var plan = string.Empty;", StringComparison.Ordinal).IsTrue();
        text.Contains("return plan;", StringComparison.Ordinal).IsTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithGenericReturnType_AddsMethod()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var targetTypeSymbolId = await ResolveMethodMutationTestTargetAsync(context);
        var filePath = context.GetFilePath("ProjectApp", "MethodMutationTestTarget");

        var result = await sut.ExecuteAsync(
            CancellationToken.None,
            targetTypeSymbolId,
            "PlanAsync",
            "Task<int>",
            "public",
            Array.Empty<string>(),
            ["int priority"],
            "var task = Task.FromResult(priority);\\nreturn task;");

        result.Error.ShouldBeNone();
        result.Status.Is("applied");
        result.AddedMethod.IsNotNull();
        result.DiagnosticsDelta.NewErrors.IsEmpty();
        result.DiagnosticsDelta.NewWarnings.IsEmpty();

        var text = await File.ReadAllTextAsync(filePath);
        text.Contains("public Task<int> PlanAsync(int priority)", StringComparison.Ordinal).IsTrue();
        text.Contains("var task = Task.FromResult(priority);", StringComparison.Ordinal).IsTrue();
        text.Contains("return task;", StringComparison.Ordinal).IsTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithEscapedGenericTypeSyntax_AddsMethod()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var targetTypeSymbolId = await ResolveMethodMutationTestTargetAsync(context);
        var filePath = context.GetFilePath("ProjectApp", "MethodMutationTestTarget");

        var result = await sut.ExecuteAsync(
            CancellationToken.None,
            targetTypeSymbolId,
            "PlanEscapedAsync",
            "Task&lt;int&gt;",
            "public",
            Array.Empty<string>(),
            ["Task&lt;int&gt; task"],
            "var current = task;\\rreturn current;");

        result.Error.ShouldBeNone();
        result.Status.Is("applied");
        result.AddedMethod.IsNotNull();
        result.DiagnosticsDelta.NewErrors.IsEmpty();
        result.DiagnosticsDelta.NewWarnings.IsEmpty();

        var text = await File.ReadAllTextAsync(filePath);
        text.Contains("public Task<int> PlanEscapedAsync(Task<int> task)", StringComparison.Ordinal).IsTrue();
        text.Contains("var current = task;", StringComparison.Ordinal).IsTrue();
        text.Contains("return current;", StringComparison.Ordinal).IsTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithEquivalentExistingMethod_ReturnsConflictWithoutChanges()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var targetTypeSymbolId = await ResolveMethodMutationTestTargetAsync(context);
        var filePath = context.GetFilePath("ProjectApp", "MethodMutationTestTarget");
        var before = await File.ReadAllTextAsync(filePath);

        var result = await sut.ExecuteAsync(
            CancellationToken.None,
            targetTypeSymbolId,
            "Evaluate",
            "string",
            "public",
            Array.Empty<string>(),
            ["string input", "int priority", "bool isEnabled"],
            "return \"changed\";");

        result.Error.ShouldHaveCode(ErrorCodes.MethodConflict);
        result.Status.Is("failed");
        result.AddedMethod.IsNull();
        result.ChangedFiles.IsEmpty();
        result.DiagnosticsDelta.NewErrors.IsEmpty();
        result.DiagnosticsDelta.NewWarnings.IsEmpty();

        var after = await File.ReadAllTextAsync(filePath);
        after.Is(before);
    }

    [Fact]
    public async Task ExecuteAsync_WithUnknownTargetSymbolId_ReturnsSymbolNotFoundWithoutChanges()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);

        var result = await sut.ExecuteAsync(
            CancellationToken.None,
            "not-a-real-symbol-id",
            "Plan",
            "string",
            "public",
            Array.Empty<string>(),
            ["string input"],
            "return string.Empty;");

        result.Error.ShouldHaveCode(ErrorCodes.SymbolNotFound);
        result.Status.Is("failed");
        result.AddedMethod.IsNull();
        result.ChangedFiles.IsEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithIntroducedCompilerDiagnostic_ReturnsChangedDocumentDelta()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var targetTypeSymbolId = await ResolveMethodMutationTestTargetAsync(context);
        var filePath = context.GetFilePath("ProjectApp", "MethodMutationTestTarget");

        var result = await sut.ExecuteAsync(
            CancellationToken.None,
            targetTypeSymbolId,
            "Broken",
            "string",
            "public",
            Array.Empty<string>(),
            ["string input", "int priority", "bool isEnabled"],
            "return missingName;");

        result.Error.ShouldBeNone();
        result.Status.Is("applied");
        result.AddedMethod.IsNotNull();
        result.DiagnosticsDelta.NewErrors.Any(static diagnostic => diagnostic.Id == "CS0103").IsTrue();
        result.DiagnosticsDelta.NewWarnings.IsEmpty();
        result.DiagnosticsDelta.NewErrors.All(diagnostic => diagnostic.FilePath.HasPathSuffix(Path.Combine("ProjectApp", "MethodMutationTestTarget.cs"))).IsTrue();

        var text = await File.ReadAllTextAsync(filePath);
        text.Contains("public string Broken(string input, int priority, bool isEnabled)", StringComparison.Ordinal).IsTrue();
        text.Contains("return missingName;", StringComparison.Ordinal).IsTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidFlatParameterString_ReturnsValidationError()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);
        var targetTypeSymbolId = await ResolveMethodMutationTestTargetAsync(context);

        var result = await sut.ExecuteAsync(
            CancellationToken.None,
            targetTypeSymbolId,
            "Plan",
            "string",
            "public",
            Array.Empty<string>(),
            ["string"],
            "return string.Empty;");

        result.Error.ShouldHaveCode(ErrorCodes.InvalidMethodSpecification);
        result.Status.Is("failed");
        result.AddedMethod.IsNull();
        result.ChangedFiles.IsEmpty();
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
