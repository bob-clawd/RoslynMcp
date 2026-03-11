using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Is.Assertions;
using RoslynMcp.Core;
using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models;
using RoslynMcp.Features;
using RoslynMcp.Features.Tools;
using RoslynMcp.Features.Tools.Inspections;
using RoslynMcp.Features.Tools.Mutations;
using RoslynMcp.Infrastructure;
using RoslynMcp.Infrastructure.Workspace;
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

        var result = await sut.ExecuteAsync(
            CancellationToken.None,
            targetMethodSymbolId,
            "var combined = input + priority.ToString();\\r\\nreturn combined;");

        result.Error.ShouldBeNone();
        result.Status.Is("applied");
        result.ChangedFiles.Count.Is(1);
        result.ReplacedMethodBody.IsNotNull();
        result.TargetMethodSymbolId.ShouldBeExternalSymbolId();
        result.ReplacedMethodBody!.MethodSymbolId.ShouldBeExternalSymbolId();
        result.ReplacedMethodBody.MethodSymbolId.Is(targetMethodSymbolId);
        result.ReplacedMethodBody.Signature.Contains("Evaluate", StringComparison.Ordinal).IsTrue();
        result.DiagnosticsDelta.NewErrors.IsEmpty();
        result.DiagnosticsDelta.NewWarnings.IsEmpty();

        var text = await File.ReadAllTextAsync(filePath);
        text.Contains("public string Evaluate(string input, int priority, bool isEnabled)", StringComparison.Ordinal).IsTrue();
        text.Contains("var combined = input + priority.ToString();", StringComparison.Ordinal).IsTrue();
        text.Contains("return combined;", StringComparison.Ordinal).IsTrue();
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
            "var plan = string.Empty;\\nreturn plan;");

        addResult.Error.ShouldBeNone();
        addResult.Status.Is("applied");

        var replaceResult = await replaceMethodBodyTool.ExecuteAsync(
            CancellationToken.None,
            targetMethodSymbolId,
            "var updated = input + \"-updated\";\\r\\nreturn updated;");

        replaceResult.Error.ShouldBeNone();
        replaceResult.Status.Is("applied");
        replaceResult.ReplacedMethodBody.IsNotNull();

        var text = await File.ReadAllTextAsync(filePath);
        text.Contains("public string Plan(string input, int priority, bool isEnabled)", StringComparison.Ordinal).IsTrue();
        text.Contains("var updated = input + \"-updated\";", StringComparison.Ordinal).IsTrue();
        text.Contains("return updated;", StringComparison.Ordinal).IsTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WhenApplyFails_KeepsOriginalSymbolIdUsable()
    {
        await using var context = await ReplaceMethodBodyApplyFailureSandboxContext.CreateAsync();
        var sut = context.GetRequiredService<ReplaceMethodBodyTool>();
        var resolver = context.GetRequiredService<ResolveSymbolTool>();
        var filePath = context.GetFilePath("ProjectApp", "MethodMutationTestTarget");
        var targetMethodSymbolId = await ResolveMethodSymbolIdAsync(context, filePath, 5, 19);
        var originalText = await File.ReadAllTextAsync(filePath);

        var failed = await sut.ExecuteAsync(
            CancellationToken.None,
            targetMethodSymbolId,
            "var updated = input + \"-updated\";\r\nreturn updated;");

        failed.Error.ShouldHaveCode(ErrorCodes.InternalError);
        failed.Status.Is("failed");
        failed.ReplacedMethodBody.IsNull();
        context.ApplyInterceptor.ApplyAttempts.Is(1);

        var resolution = await resolver.ExecuteAsync(CancellationToken.None, symbolId: targetMethodSymbolId);

        resolution.Error.ShouldBeNone();
        resolution.Symbol.IsNotNull();
        resolution.Symbol!.SymbolId.Is(targetMethodSymbolId);

        var currentText = await File.ReadAllTextAsync(filePath);
        currentText.Is(originalText);

        var succeeded = await sut.ExecuteAsync(
            CancellationToken.None,
            targetMethodSymbolId,
            "var updated = input + \"-updated\";\r\nreturn updated;");

        succeeded.Error.ShouldBeNone();
        succeeded.Status.Is("applied");
        succeeded.ReplacedMethodBody.IsNotNull();
        succeeded.ReplacedMethodBody!.MethodSymbolId.Is(targetMethodSymbolId);
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

    private static async Task<string> ResolveMethodSymbolIdAsync(SandboxContext context, string path, int line, int column)
    {
        var resolver = context.GetRequiredService<ResolveSymbolTool>();
        var resolved = await resolver.ExecuteAsync(CancellationToken.None, path: path, line: line, column: column);

        resolved.Error.ShouldBeNone();
        resolved.Symbol.IsNotNull();
        return resolved.Symbol!.SymbolId;
    }

    private static async Task<string> ResolveMethodMutationTestTargetAsync(SandboxContext context)
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

file sealed class ReplaceMethodBodyApplyFailureSandboxContext : SandboxContext
{
    private ReplaceMethodBodyApplyFailureSandboxContext()
    { }

    public FirstApplyFailsReplaceMethodBodySolutionSessionService ApplyInterceptor => GetRequiredService<FirstApplyFailsReplaceMethodBodySolutionSessionService>();

    public static async Task<ReplaceMethodBodyApplyFailureSandboxContext> CreateAsync(CancellationToken cancellationToken = default)
    {
        var context = new ReplaceMethodBodyApplyFailureSandboxContext();
        try
        {
            var sandbox = TestSolutionSandbox.Create(context.CanonicalTestSolutionDirectory);
            await context.InitializeSandboxAsync(sandbox, cancellationToken).ConfigureAwait(false);
            return context;
        }
        catch
        {
            await context.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    protected override ServiceProvider CreateServiceProvider() => new ServiceCollection()
        .AddInfrastructure()
        .AddSingleton<FirstApplyFailsReplaceMethodBodySolutionSessionService>()
        .AddSingleton<ISolutionSessionService>(provider => provider.GetRequiredService<FirstApplyFailsReplaceMethodBodySolutionSessionService>())
        .AddSingleton<IRoslynSolutionAccessor>(provider => provider.GetRequiredService<FirstApplyFailsReplaceMethodBodySolutionSessionService>())
        .AddImplementations<Tool>()
        .BuildServiceProvider();
}

file sealed class FirstApplyFailsReplaceMethodBodySolutionSessionService(RoslynSolutionSessionService inner)
    : ISolutionSessionService, IRoslynSolutionAccessor
{
    private int _remainingApplyFailures = 1;

    public int ApplyAttempts { get; private set; }

    public Task<DiscoverSolutionsResult> DiscoverSolutionsAsync(DiscoverSolutionsRequest request, CancellationToken ct)
        => inner.DiscoverSolutionsAsync(request, ct);

    public Task<SelectSolutionResult> SelectSolutionAsync(SelectSolutionRequest request, CancellationToken ct)
        => inner.SelectSolutionAsync(request, ct);

    public Task<ReloadSolutionResult> ReloadSolutionAsync(ReloadSolutionRequest request, CancellationToken ct)
        => inner.ReloadSolutionAsync(request, ct);

    public Task<(Solution? Solution, ErrorInfo? Error)> GetCurrentSolutionAsync(CancellationToken ct)
        => inner.GetCurrentSolutionAsync(ct);

    public Task<(int Version, ErrorInfo? Error)> GetWorkspaceVersionAsync(CancellationToken ct)
        => inner.GetWorkspaceVersionAsync(ct);

    public Task<(bool Applied, ErrorInfo? Error)> TryApplySolutionAsync(Solution solution, CancellationToken ct)
    {
        ApplyAttempts++;
        if (Interlocked.Exchange(ref _remainingApplyFailures, 0) == 1)
        {
            return Task.FromResult((false, (ErrorInfo?)new ErrorInfo(ErrorCodes.InternalError, "Injected apply failure for replace_method_body coverage.")));
        }

        return inner.TryApplySolutionAsync(solution, ct);
    }
}
