using RoslynMcp.Core.Models;

namespace RoslynMcp.Core;

public static class ToolContractMapperExtensions
{
    private const int DefaultMaxDerived = 200;
    private const int MinimumLineOrColumn = 1;

    extension(string? solutionHintPath)
    {
        public LoadSolutionRequest ToLoadSolutionRequest()
            => new(NormalizeOptionalString(solutionHintPath));

        public UnderstandCodebaseRequest ToUnderstandCodebaseRequest()
            => new(NormalizeOptionalString(solutionHintPath));

        public ExplainSymbolRequest ToExplainSymbolRequest(string? path, int? line, int? column)
            => new(
                NormalizeOptionalString(solutionHintPath),
                NormalizeOptionalString(path),
                line.HasValue ? NormalizePosition(line.Value) : null,
                column.HasValue ? NormalizePosition(column.Value) : null);

        public ListTypesRequest ToListTypesRequest(string? projectName,
            string? projectId,
            string? namespacePrefix,
            string? kind,
            string? accessibility,
            int? limit,
            int? offset)
            => new(
                NormalizeOptionalString(solutionHintPath),
                NormalizeOptionalString(projectName),
                NormalizeOptionalString(projectId),
                NormalizeOptionalString(namespacePrefix),
                NormalizeOptionalString(kind)?.ToLowerInvariant(),
                NormalizeOptionalString(accessibility)?.ToLowerInvariant(),
                NormalizeNonNegative(limit),
                NormalizeNonNegative(offset));

        public ListMembersRequest ToListMembersRequest(string? path,
            int? line,
            int? column,
            string? kind,
            string? accessibility,
            string? binding,
            bool? includeInherited,
            int? limit,
            int? offset)
            => new(
                NormalizeOptionalString(solutionHintPath),
                NormalizeOptionalString(path),
                line.HasValue ? NormalizePosition(line.Value) : null,
                column.HasValue ? NormalizePosition(column.Value) : null,
                NormalizeOptionalString(kind)?.ToLowerInvariant(),
                NormalizeOptionalString(accessibility)?.ToLowerInvariant(),
                NormalizeOptionalString(binding)?.ToLowerInvariant(),
                includeInherited ?? false,
                NormalizeNonNegative(limit),
                NormalizeNonNegative(offset));

        public ResolveSymbolRequest ToResolveSymbolRequest(string? path,
            int? line,
            int? column,
            string? qualifiedName,
            string? projectPath,
            string? projectName,
            string? projectId)
            => new(
                NormalizeOptionalString(solutionHintPath),
                NormalizeOptionalString(path),
                line.HasValue ? NormalizePosition(line.Value) : null,
                column.HasValue ? NormalizePosition(column.Value) : null,
                NormalizeOptionalString(qualifiedName),
                NormalizeOptionalString(projectPath),
                NormalizeOptionalString(projectName),
                NormalizeOptionalString(projectId));

        public TraceFlowRequest ToTraceFlowRequest(string? path, int? line, int? column, string? direction, int? depth)
            => new(
                NormalizeOptionalString(solutionHintPath),
                NormalizeOptionalString(path),
                line.HasValue ? NormalizePosition(line.Value) : null,
                column.HasValue ? NormalizePosition(column.Value) : null,
                NormalizeOptionalString(direction)?.ToLowerInvariant(),
                NormalizeNonNegative(depth));

        public FindCodeSmellsRequest ToFindCodeSmellsRequest()
            => new(NormalizeString(solutionHintPath));

        public ListDependenciesRequest ToListDependenciesRequest(string? projectName,
            string? projectId,
            string? direction)
            => new(
                NormalizeOptionalString(solutionHintPath),
                NormalizeOptionalString(projectName),
                NormalizeOptionalString(projectId),
                NormalizeOptionalString(direction)?.ToLowerInvariant());

        public FindReferencesScopedRequest ToFindReferencesScopedRequest(string? scope, string? path)
            => new(NormalizeSymbolId(solutionHintPath), NormalizeScope(scope), NormalizeOptionalString(path));

        public FindImplementationsRequest ToFindImplementationsRequest()
            => new(NormalizeSymbolId(solutionHintPath));

        public GetTypeHierarchyRequest ToGetTypeHierarchyRequest(bool? includeTransitive, int? maxDerived)
            => new(NormalizeSymbolId(solutionHintPath), includeTransitive ?? true, NormalizeNonNegative(maxDerived) ?? DefaultMaxDerived);
    }

    private static int NormalizePosition(int value)
        => Math.Max(value, MinimumLineOrColumn);

    private static int? NormalizeNonNegative(int? value)
        => value is null ? null : Math.Max(value.Value, 0);

    private static string NormalizeScope(string? input)
        => NormalizeString(input).ToLowerInvariant();

    private static string NormalizeSymbolId(string? input)
        => string.IsNullOrWhiteSpace(input) ? string.Empty : input.Trim();

    private static string? NormalizeOptionalString(string? input)
        => string.IsNullOrWhiteSpace(input) ? null : input.Trim();

    private static string NormalizeString(string? input)
        => string.IsNullOrWhiteSpace(input) ? string.Empty : input.Trim();
}
