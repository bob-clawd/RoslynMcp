using RoslynMcp.Core;
using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models;
using RoslynMcp.Infrastructure.Workspace;

namespace RoslynMcp.Infrastructure.Testing;

internal sealed class TestInspectionService(
    IRoslynSolutionAccessor solutionAccessor,
    ITestProcessRunner testProcessRunner,
    ITestResultInterpreter testResultInterpreter,
    ICurrentWorkspaceRootProvider currentWorkspaceRootProvider) : ITestInspectionService
{
    private readonly IRoslynSolutionAccessor _solutionAccessor = solutionAccessor ?? throw new ArgumentNullException(nameof(solutionAccessor));
    private readonly ITestProcessRunner _testProcessRunner = testProcessRunner ?? throw new ArgumentNullException(nameof(testProcessRunner));
    private readonly ITestResultInterpreter _testResultInterpreter = testResultInterpreter ?? throw new ArgumentNullException(nameof(testResultInterpreter));
    private readonly string _workspaceRoot = currentWorkspaceRootProvider?.WorkspaceRoot ?? throw new ArgumentNullException(nameof(currentWorkspaceRootProvider));

    public async Task<RunTestsResult> RunTestsAsync(RunTestsRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        request = request.WithWorkspaceAbsolutePaths(_workspaceRoot);

        try
        {
            ct.ThrowIfCancellationRequested();

            var (solution, solutionError) = await _solutionAccessor.GetCurrentSolutionAsync(ct).ConfigureAwait(false);
            if (solutionError is not null)
            {
                return InfrastructureFailure(solutionError).WithWorkspaceRelativePaths(_workspaceRoot);
            }

            if (solution?.FilePath is not { Length: > 0 } solutionPath)
            {
                return InfrastructureFailure(new ErrorInfo(ErrorCodes.SolutionNotSelected, "No solution has been selected.")).WithWorkspaceRelativePaths(_workspaceRoot);
            }

            var targetResolution = ResolveTarget(solutionPath, request.Target);
            if (targetResolution.Error is not null)
            {
                return InvalidInput(targetResolution.Error).WithWorkspaceRelativePaths(_workspaceRoot);
            }

            var artifacts = CreateArtifacts();

            try
            {
                var processResult = await _testProcessRunner
                    .RunAsync(targetResolution.EffectiveTargetPath!, artifacts.ResultsDirectory, request.Filter, ct)
                    .ConfigureAwait(false);

                var trxReports = DiscoverTrxReports(artifacts.ResultsDirectory);
                return _testResultInterpreter.Interpret(processResult, trxReports).WithWorkspaceRelativePaths(_workspaceRoot);
            }
            finally
            {
                TryDeleteDirectory(artifacts.RootDirectory);
            }
        }
        catch (OperationCanceledException)
        {
            return new RunTestsResult(
                RunTestOutcomes.Cancelled,
                null,
                Array.Empty<TestFailureGroup>(),
                Summary: "Test execution was cancelled.")
                .WithWorkspaceRelativePaths(_workspaceRoot);
        }
        catch (Exception ex)
        {
            return InfrastructureFailure(new ErrorInfo(ErrorCodes.InternalError, ex.Message)).WithWorkspaceRelativePaths(_workspaceRoot);
        }
    }

    private static TargetResolution ResolveTarget(string solutionPath, string? requestedTarget)
    {
        var solutionDirectory = Path.GetDirectoryName(solutionPath)!;
        if (string.IsNullOrWhiteSpace(requestedTarget))
        {
            return new TargetResolution(solutionPath, null);
        }

        var normalizedTarget = Path.GetFullPath(
            Path.IsPathRooted(requestedTarget)
                ? requestedTarget
                : Path.Combine(solutionDirectory, requestedTarget.Trim()));

        if (!IsPathWithinRoot(solutionDirectory, normalizedTarget))
        {
            return new TargetResolution(null,
                new ErrorInfo(ErrorCodes.InvalidInput, "Target must be inside the loaded solution directory."));
        }

        if (File.Exists(normalizedTarget))
        {
            var extension = Path.GetExtension(normalizedTarget);
            if (!string.Equals(extension, ".sln", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".slnx", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase))
            {
                return new TargetResolution(null,
                    new ErrorInfo(ErrorCodes.InvalidInput, "Target must be a .sln, .slnx, .csproj, or directory path."));
            }

            return new TargetResolution(normalizedTarget, null);
        }

        if (Directory.Exists(normalizedTarget))
        {
            return new TargetResolution(normalizedTarget, null);
        }

        return new TargetResolution(null,
            new ErrorInfo(ErrorCodes.InvalidInput, $"Target '{requestedTarget}' does not exist."));
    }

    private static IReadOnlyList<string> DiscoverTrxReports(string resultsDirectory)
    {
        if (!Directory.Exists(resultsDirectory))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(resultsDirectory, "*.trx", SearchOption.AllDirectories)
            .OrderBy(static path => path, GetPathStringComparer())
            .ToArray();
    }

    private static TestRunArtifacts CreateArtifacts()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), "RoslynMcp", "run-tests", Guid.NewGuid().ToString("N"));
        var resultsDirectory = Path.Combine(rootDirectory, "results");
        Directory.CreateDirectory(resultsDirectory);
        return new TestRunArtifacts(rootDirectory, resultsDirectory);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static RunTestsResult InvalidInput(ErrorInfo error)
        => new(
            RunTestOutcomes.InfrastructureError,
            null,
            Array.Empty<TestFailureGroup>(),
            Summary: error.Message,
            Error: error);

    private static RunTestsResult InfrastructureFailure(ErrorInfo error)
        => new(
            RunTestOutcomes.InfrastructureError,
            null,
            Array.Empty<TestFailureGroup>(),
            Summary: error.Message,
            Error: error);

    private static bool IsPathWithinRoot(string rootDirectory, string path)
    {
        var normalizedPath = Path.GetFullPath(path);
        var normalizedRoot = Path.GetFullPath(rootDirectory);
        if (string.Equals(normalizedPath, normalizedRoot, GetPathStringComparison()))
        {
            return true;
        }

        return normalizedPath.StartsWith(EnsureTrailingSeparator(normalizedRoot), GetPathStringComparison());
    }

    private static StringComparer GetPathStringComparer()
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static StringComparison GetPathStringComparison()
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static string EnsureTrailingSeparator(string path)
        => path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;

    private sealed record TargetResolution(string? EffectiveTargetPath, ErrorInfo? Error);

    private sealed record TestRunArtifacts(string RootDirectory, string ResultsDirectory);
}
