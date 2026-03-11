using Is.Assertions;
using RoslynMcp.Core;
using RoslynMcp.Core.Models;
using RoslynMcp.Features.Tools;
using RoslynMcp.Features.Tools.Inspections;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.Inspections.Tools;

public sealed class ResolveSymbolToolTests(SharedSandboxFixture fixture, ITestOutputHelper output)
    : SharedToolTests<ResolveSymbolTool>(fixture, output)
{
    [Fact]
    public async Task ResolveSymbolAsync_WithQualifiedName_ReturnsResolvedTypeSymbol()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, qualifiedName: "ProjectApp.AppOrchestrator", projectName: "ProjectApp");

        result.Error.ShouldBeNone();
        result.IsAmbiguous.Is(false);
        result.Candidates.IsEmpty();
        result.Symbol.ShouldMatchResolvedSymbol("AppOrchestrator", "NamedType", Path.Combine("ProjectApp", "AppOrchestrator.cs"));
    }

    [Fact]
    public async Task ResolveSymbolAsync_WithQualifiedNameWithoutProjectScope_ReturnsResolvedTypeSymbol()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, qualifiedName: "ProjectApp.AppOrchestrator");

        result.Error.ShouldBeNone();
        result.IsAmbiguous.IsFalse();
        result.Candidates.IsEmpty();
        result.Symbol.ShouldMatchResolvedSymbol("AppOrchestrator", "NamedType", Path.Combine("ProjectApp", "AppOrchestrator.cs"));
        result.Symbol!.QualifiedDisplayName.Is("ProjectApp.AppOrchestrator");
        result.Symbol.SymbolId.ShouldBeExternalSymbolId();
        
        result.Symbol.Reference.ShouldMatchReference("type:ProjectApp.AppOrchestrator", "ProjectApp.AppOrchestrator",
            Path.Combine("ProjectApp", "AppOrchestrator.cs"), 6);
    }

    [Fact]
    public async Task ResolveSymbolAsync_WithGenericQualifiedName_ReturnsResolvedGenericTypeSymbol()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, qualifiedName: "ProjectCore.OperationBase<TInput>", projectName: "ProjectCore");

        result.Error.ShouldBeNone();
        result.IsAmbiguous.IsFalse();
        result.Candidates.IsEmpty();
        result.Symbol.ShouldMatchResolvedSymbol("OperationBase<TInput>", "NamedType", Path.Combine("ProjectCore", "Contracts.cs"));
        result.Symbol!.QualifiedDisplayName.Is("ProjectCore.OperationBase<TInput>");
    }

    [Fact]
    public async Task ResolveSymbolAsync_WithQualifiedMemberSignature_ReturnsResolvedMethodSymbol()
    {
        var result = await Sut.ExecuteAsync(
            CancellationToken.None,
            qualifiedName: "ProjectImpl.FastWorkItemOperation.ExecuteAsync(Guid, string, CancellationToken)",
            projectName: "ProjectImpl");

        result.Error.ShouldBeNone();
        result.IsAmbiguous.IsFalse();
        result.Candidates.IsEmpty();
        result.Symbol.ShouldMatchResolvedMember("ExecuteAsync", "Method", Path.Combine("ProjectImpl", "WorkItemOperations.cs"), 27);
        result.Symbol!.QualifiedDisplayName.Is("ProjectImpl.FastWorkItemOperation.ExecuteAsync(Guid, string, CancellationToken)");
    }

    [Fact]
    public async Task ResolveSymbolAsync_WithSourcePosition_ReturnsResolvedTypeSymbol()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, path: AppOrchestratorPath, line: 6, column: 21);

        result.Error.ShouldBeNone();
        result.IsAmbiguous.IsFalse();
        result.Candidates.IsEmpty();
        result.Symbol.ShouldMatchResolvedSymbol("AppOrchestrator", "NamedType", Path.Combine("ProjectApp", "AppOrchestrator.cs"));
    }

    [Fact]
    public async Task ResolveSymbolAsync_WithSourcePositionOnMethodDeclaration_ReturnsResolvedMethodSymbol()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, path: AppOrchestratorPath, line: 54, column: 35);

        result.Error.ShouldBeNone();
        result.IsAmbiguous.IsFalse();
        result.Candidates.IsEmpty();
        result.Symbol.ShouldMatchResolvedMember("ExecuteFlowAsync", "Method", Path.Combine("ProjectApp", "AppOrchestrator.cs"), 54);
    }

    [Fact]
    public async Task ResolveSymbolAsync_WithSourcePositionOnMethodCallSite_ReturnsResolvedMethodSymbol()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, path: AppOrchestratorPath, line: 23, column: 34);

        result.Error.ShouldBeNone();
        result.IsAmbiguous.IsFalse();
        result.Candidates.IsEmpty();
        result.Symbol.ShouldMatchResolvedMember("ExecuteFlowAsync", "Method", Path.Combine("ProjectApp", "AppOrchestrator.cs"), 54);
    }

    [Fact]
    public async Task ResolveSymbolAsync_WithSymbolIdRoundtrip_ReturnsSameSymbol()
    {
        var initial = await Sut.ExecuteAsync(CancellationToken.None, qualifiedName: "ProjectApp.AppOrchestrator", projectName: "ProjectApp");

        initial.Error.ShouldBeNone();
        initial.Symbol.ShouldMatchResolvedSymbol("AppOrchestrator", "NamedType", Path.Combine("ProjectApp", "AppOrchestrator.cs"));

        var roundtrip = await Sut.ExecuteAsync(CancellationToken.None, symbolId: initial.Symbol!.SymbolId);

        roundtrip.Error.ShouldBeNone();
        roundtrip.IsAmbiguous.IsFalse();
        roundtrip.Candidates.IsEmpty();
        roundtrip.Symbol.ShouldMatchResolvedSymbol("AppOrchestrator", "NamedType", Path.Combine("ProjectApp", "AppOrchestrator.cs"));
        roundtrip.Symbol!.SymbolId.Is(initial.Symbol.SymbolId);
        roundtrip.Symbol.FilePath.Is(initial.Symbol.FilePath);
        roundtrip.Symbol.Reference.IsNotNull();
        initial.Symbol.Reference.IsNotNull();
        roundtrip.Symbol.Reference!.SymbolId.Is(initial.Symbol.Reference!.SymbolId);
        roundtrip.Symbol.Reference.Handle.Is(initial.Symbol.Reference.Handle);
        roundtrip.Symbol.Reference.QualifiedDisplayName.Is(initial.Symbol.Reference.QualifiedDisplayName);
    }

    [Fact]
    public async Task ResolveSymbolAsync_WithDuplicateProjectViews_ReturnsCanonicalResolvedSymbol()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, qualifiedName: "ProjectImpl.FastWorkItemOperation");

        result.Error.ShouldBeNone();
        result.IsAmbiguous.IsFalse();
        result.Candidates.IsEmpty();
        result.Symbol.ShouldMatchResolvedSymbol("FastWorkItemOperation", "NamedType", Path.Combine("ProjectImpl", "WorkItemOperations.cs"));
    }

    [Fact]
    public async Task ResolveSymbolAsync_WithShortMemberName_ReturnsResolvedMethodSymbol()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, qualifiedName: "RunReflectionPathAsync");

        result.Error.ShouldBeNone();
        result.IsAmbiguous.IsFalse();
        result.Candidates.IsEmpty();
        result.Symbol.ShouldMatchResolvedMember("RunReflectionPathAsync", "Method", Path.Combine("ProjectApp", "AppOrchestrator.cs"), 34);
    }

    [Fact]
    public async Task ResolveSymbolAsync_WithShortNameAndDuplicateProjectViews_ReturnsCanonicalResolvedSymbol()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, qualifiedName: "FastWorkItemOperation");

        result.Error.ShouldBeNone();
        result.IsAmbiguous.IsFalse();
        result.Candidates.IsEmpty();
        result.Symbol.ShouldMatchResolvedSymbol("FastWorkItemOperation", "NamedType", Path.Combine("ProjectImpl", "WorkItemOperations.cs"));
    }

    [Fact]
    public async Task ResolveSymbolAsync_WithInvalidQualifiedName_ReturnsSymbolNotFound()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, qualifiedName: "ProjectApp.DoesNotExist", projectName: "ProjectApp");

        result.Error.ShouldHaveCode(ErrorCodes.SymbolNotFound);
        result.IsAmbiguous.Is(false);
        result.Symbol.IsNull();
        result.Candidates.IsEmpty();
    }

    [Fact]
    public async Task ResolveSymbolAsync_WithInvalidSourcePosition_ReturnsSymbolNotFound()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, path: AppOrchestratorPath, line: 999, column: 1);

        result.Error.ShouldHaveCode(ErrorCodes.SymbolNotFound);
        result.IsAmbiguous.Is(false);
        result.Symbol.IsNull();
        result.Candidates.IsEmpty();
    }

    [Fact]
    public async Task ResolveSymbolAsync_WithProjectScope_DisambiguatesQualifiedName()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, qualifiedName: "ProjectImpl.FastWorkItemOperation", projectName: "ProjectImpl");

        result.Error.ShouldBeNone();
        result.IsAmbiguous.Is(false);
        result.Candidates.IsEmpty();
        result.Symbol.ShouldMatchResolvedSymbol("FastWorkItemOperation", "NamedType", Path.Combine("ProjectImpl", "WorkItemOperations.cs"));
    }

    [Fact]
    public async Task ResolveSymbolAsync_WithAmbiguousQualifiedMemberName_ReturnsStructuredCandidates()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, qualifiedName: "ProjectImpl.FastWorkItemOperation.ExecuteAsync", projectName: "ProjectImpl");

        result.Error.ShouldHaveCode(ErrorCodes.AmbiguousSymbol);
        result.Symbol.IsNull();
        result.IsAmbiguous.IsTrue();
        result.Candidates.Count.Is(3);
        result.Candidates.All(static candidate => candidate.SymbolId.StartsWith('S')).IsTrue();
        result.Candidates.ShouldContainCandidate("ProjectImpl.FastWorkItemOperation.ExecuteAsync(WorkItem, CancellationToken)", "ProjectImpl");
        result.Candidates.ShouldContainCandidate("ProjectImpl.FastWorkItemOperation.ExecuteAsync(Guid, string, CancellationToken)", "ProjectImpl");
        result.Candidates.ShouldContainCandidate("ProjectImpl.FastWorkItemOperation.ExecuteAsync(Guid, string, int, CancellationToken)", "ProjectImpl");
        result.Candidates.All(static candidate => candidate.Reference is not null).IsTrue();
        result.Candidates.Any(static candidate => candidate.Reference!.Handle == "method:ProjectImpl.FastWorkItemOperation.ExecuteAsync(Guid, string, CancellationToken)").IsTrue();
        result.Candidates.Any(static candidate => candidate.Reference!.Handle == "method:ProjectImpl.FastWorkItemOperation.ExecuteAsync(Guid, string, int, CancellationToken)").IsTrue();
        result.Candidates.Any(static candidate => candidate.Reference!.Handle == "method:ProjectImpl.FastWorkItemOperation.ExecuteAsync(WorkItem, CancellationToken)").IsTrue();
    }

    [Fact]
    public async Task ResolveSymbolAsync_WhenNoSelectorProvided_ReturnsValidationError()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None);

        result.Error.ShouldHaveCode(ErrorCodes.InvalidInput);
    }
}

file static class AssertionExtensions
{
    extension(ResolvedSymbolSummary? symbol)
    {
        internal void ShouldMatchResolvedSymbol(string expectedDisplayName, string expectedKind, string expectedFileName)
        {
            symbol.IsNotNull();
            symbol!.DisplayName.Is(expectedDisplayName);
            symbol.Kind.Is(expectedKind);
            symbol.FilePath.ShouldEndWithPathSuffix(expectedFileName);
            symbol.SymbolId.ShouldNotBeEmpty();
        }
    }
    
    extension(ResolvedSymbolSummary? symbol)
    {
        internal void ShouldMatchResolvedMember(string expectedName, string expectedKind, string expectedFileName, int expectedLine)
        {
            symbol.IsNotNull();
            symbol!.DisplayName.Contains(expectedName, StringComparison.Ordinal).IsTrue();
            symbol.Kind.Is(expectedKind);
            symbol.FilePath.ShouldEndWithPathSuffix(expectedFileName);
            symbol.Line.Is(expectedLine);
            symbol.SymbolId.ShouldNotBeEmpty();
        }
    }

    extension(IReadOnlyList<ResolveSymbolCandidate> candidates)
    {
        internal void ShouldContainCandidate(string expectedQualifiedDisplayName, string expectedProjectName)
        {
            candidates.Any(candidate =>
                    string.Equals(candidate.QualifiedDisplayName, expectedQualifiedDisplayName, StringComparison.Ordinal)
                    && string.Equals(candidate.ProjectName, expectedProjectName, StringComparison.Ordinal))
                .IsTrue();
        }
    }

    extension(SymbolReference? reference)
    {
        internal void ShouldMatchReference(string expectedHandle, string expectedQualifiedDisplayName, string expectedFileName, int expectedLine)
        {
            reference.IsNotNull();
            reference!.Handle.Is(expectedHandle);
            reference.QualifiedDisplayName.Is(expectedQualifiedDisplayName);
            reference.DeclarationLocation.IsNotNull();
            reference.DeclarationLocation!.FilePath.ShouldEndWithPathSuffix(expectedFileName);
            reference.DeclarationLocation.Line.Is(expectedLine);
            reference.SymbolId.ShouldBeExternalSymbolId();
        }
    }
}
