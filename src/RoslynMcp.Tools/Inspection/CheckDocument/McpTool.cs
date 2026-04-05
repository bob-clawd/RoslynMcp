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

		var absolutePath = Path.GetFullPath(workspaceManager.ToAbsolutePath(documentPath));
		if (string.IsNullOrWhiteSpace(absolutePath))
			return Result.AsError("document not found", new Dictionary<string, string> { ["documentPath"] = documentPath });

		// Resolve the document by file path.
		// We intentionally avoid File.Exists checks here because callers may provide relative paths
		// and the workspace can be a sandbox copy in tests.
		static bool PathEquals(string? left, string? right)
			=> !string.IsNullOrWhiteSpace(left) &&
			   !string.IsNullOrWhiteSpace(right) &&
			   string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

		var documents = solution.Projects.SelectMany(p => p.Documents).Where(d => d.FilePath is not null).ToList();

		var document = documents.FirstOrDefault(d => PathEquals(d.FilePath, absolutePath));

		// Fallback: allow a bare filename only if it uniquely identifies a document in the solution.
		// This avoids accidentally matching common names like Extensions.cs across multiple projects.
		if (document is null)
		{
			var fileName = Path.GetFileName(documentPath.Trim());
			if (!string.IsNullOrWhiteSpace(fileName))
			{
				var candidates = documents
					.Where(d => string.Equals(Path.GetFileName(d.FilePath!), fileName, StringComparison.OrdinalIgnoreCase))
					.Take(2)
					.ToList();

				if (candidates.Count == 1)
					document = candidates[0];
			}
		}

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
			.Where(d => IsDiagnosticInFile(d, absolutePath))
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

	private static bool IsDiagnosticInFile(Microsoft.CodeAnalysis.Diagnostic d, string filePath)
	{
		if (d.Location.SourceTree?.FilePath is { } treePath &&
		    string.Equals(Path.GetFullPath(treePath), filePath, StringComparison.OrdinalIgnoreCase))
			return true;

		var spanPath = d.Location.GetLineSpan().Path;
		if (!string.IsNullOrWhiteSpace(spanPath) &&
		    string.Equals(Path.GetFullPath(spanPath), filePath, StringComparison.OrdinalIgnoreCase))
			return true;

		foreach (var loc in d.AdditionalLocations)
		{
			if (loc.SourceTree?.FilePath is { } addTreePath &&
			    string.Equals(Path.GetFullPath(addTreePath), filePath, StringComparison.OrdinalIgnoreCase))
				return true;

			var addSpanPath = loc.GetLineSpan().Path;
			if (!string.IsNullOrWhiteSpace(addSpanPath) &&
			    string.Equals(Path.GetFullPath(addSpanPath), filePath, StringComparison.OrdinalIgnoreCase))
				return true;
		}

		return false;
	}
}
