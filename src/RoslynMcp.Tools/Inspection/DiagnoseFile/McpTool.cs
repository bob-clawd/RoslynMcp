using System.ComponentModel;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools.Inspection.DiagnoseFile;

public sealed record Diagnostic(int Line, int Column, string Id, string Message);

public sealed record Result(IReadOnlyList<Diagnostic> Errors, ErrorInfo? Error = null)
{
	public static Result AsError(string message, IReadOnlyDictionary<string, string>? details = null)
		=> new([], new ErrorInfo(message, details));
}

[McpServerToolType]
public sealed class McpTool(WorkspaceManager workspaceManager, SolutionManager solutionManager) : Tool
{
	private const int MaxErrors = 10;

	[McpServerTool(Name = "diagnose_file", Title = "Diagnose File", ReadOnly = true, Idempotent = true)]
	[Description("Get up to 10 file-local compiler error diagnostics for the given file.")]
	public async Task<Result> Execute(
		CancellationToken cancellationToken,
		[Description("Path to a source file within the currently loaded solution.")]
		string? filePath = null)
	{
		if (solutionManager.Solution is not { } solution)
			return Result.AsError("load solution first");

		if (string.IsNullOrWhiteSpace(filePath))
			return Result.AsError("filePath is required");

		var absolutePath = workspaceManager.ToAbsolutePath(filePath);
		if (string.IsNullOrWhiteSpace(absolutePath))
			return Result.AsError("file not found", new Dictionary<string, string> { ["filePath"] = filePath });

		var relativePath = workspaceManager.ToRelativePathIfPossible(absolutePath);
		var relativeInputPath = workspaceManager.ToRelativePathIfPossible(filePath);

		var documents = solution.Projects.SelectMany(p => p.Documents).Where(d => d.FilePath is not null).ToList();

		var document = documents.FirstOrDefault(d =>
			string.Equals(d.FilePath, absolutePath, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(workspaceManager.ToRelativePathIfPossible(d.FilePath), relativePath, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(workspaceManager.ToRelativePathIfPossible(d.FilePath), relativeInputPath, StringComparison.OrdinalIgnoreCase));

		if (document is null)
			return Result.AsError("file not in solution", new Dictionary<string, string> { ["filePath"] = filePath });

		var compilation = await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
		if (compilation is null)
			return Result.AsError("no compilation");

		var diagnostics = compilation
			.GetDiagnostics(cancellationToken)
			.Where(d => d.Severity == DiagnosticSeverity.Error)
			.Where(d => d.Location.IsInSource)
			.Where(d => IsDiagnosticInFile(d, workspaceManager, relativePath))
			.OrderBy(d => d.Location.GetLineSpan().StartLinePosition.Line)
			.ThenBy(d => d.Location.GetLineSpan().StartLinePosition.Character)
			.ThenBy(d => d.Id, StringComparer.Ordinal)
			.Take(MaxErrors)
			.Select(d =>
			{
				var pos = d.Location.GetLineSpan().StartLinePosition;
				return new Diagnostic(
					Line: pos.Line + 1,
					Column: pos.Character + 1,
					Id: d.Id,
					Message: d.GetMessage());
			})
			.ToList();

		return new Result(diagnostics);
	}

	private static bool IsDiagnosticInFile(Microsoft.CodeAnalysis.Diagnostic d, WorkspaceManager workspaceManager, string? targetRelativePath)
	{
		static bool EqualsRel(string? a, string? b)
			=> !string.IsNullOrWhiteSpace(a) &&
			   !string.IsNullOrWhiteSpace(b) &&
			   string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

		var locationPath = d.Location.SourceTree?.FilePath ?? d.Location.GetLineSpan().Path;
		if (EqualsRel(workspaceManager.ToRelativePathIfPossible(locationPath), targetRelativePath))
			return true;

		foreach (var loc in d.AdditionalLocations)
		{
			var addPath = loc.SourceTree?.FilePath ?? loc.GetLineSpan().Path;
			if (EqualsRel(workspaceManager.ToRelativePathIfPossible(addPath), targetRelativePath))
				return true;
		}

		return false;
	}
}
