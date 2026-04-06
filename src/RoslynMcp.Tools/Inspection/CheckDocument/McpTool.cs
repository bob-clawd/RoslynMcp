using System.ComponentModel;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools.Inspection.CheckDocument;

public sealed record Diagnostic(int Line, int Column, string Id, string Message);

public sealed record Result(IReadOnlyList<Diagnostic> Errors, ErrorInfo? Error = null)
{
	public static Result AsError(string message, IReadOnlyDictionary<string, string>? details = null)
		=> new([], new ErrorInfo(message, details));
}

[McpServerToolType]
public sealed class McpTool(
	WorkspaceManager workspaceManager,
	SolutionManager solutionManager)
	: Tool
{
	private const int MaxErrors = 10;

	[McpServerTool(Name = "check_document", Title = "Check Document", ReadOnly = true, Idempotent = true)]
	[Description("Quick file-level semantic check after edits. Returns up to 10 error diagnostics for the given document.")]
	public async Task<Result> Execute(
		CancellationToken cancellationToken,
		[Description("Path to a source file within the currently loaded solution.")]
		string? documentPath = null)
	{
		if (solutionManager.Solution is not { } solution)
			return Result.AsError("load solution first");

		if (string.IsNullOrWhiteSpace(documentPath))
			return Result.AsError("documentPath is required");

		// Policy (per Moldi): only accept absolute paths or paths relative to the workspace.
		// Path normalization / resolution is delegated to WorkspaceManager.
		var absolutePath = workspaceManager.ToAbsolutePath(documentPath);
		if (string.IsNullOrWhiteSpace(absolutePath))
			return Result.AsError("document not found", new Dictionary<string, string> { ["documentPath"] = documentPath });

		// We compare relative paths inside the workspace to avoid fragile absolute path normalization.
		var relativePath = workspaceManager.ToRelativePathIfPossible(absolutePath);
		var relativeInputPath = workspaceManager.ToRelativePathIfPossible(documentPath);

		// Resolve the document by file path.
		// We intentionally avoid File.Exists checks here because callers may provide relative paths
		// and the workspace can be a sandbox copy in tests.
		var documents = solution.Projects.SelectMany(p => p.Documents).Where(d => d.FilePath is not null).ToList();

		var document = documents.FirstOrDefault(d =>
			string.Equals(d.FilePath, absolutePath, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(workspaceManager.ToRelativePathIfPossible(d.FilePath), relativePath, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(workspaceManager.ToRelativePathIfPossible(d.FilePath), relativeInputPath, StringComparison.OrdinalIgnoreCase));

		if (document is null)
			return Result.AsError("document not in solution", new Dictionary<string, string> { ["documentPath"] = documentPath });

		// SemanticModel.GetDiagnostics() may not force full compilation binding for certain failures.
		// We use the project's compilation diagnostics and then filter down to this document.
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
