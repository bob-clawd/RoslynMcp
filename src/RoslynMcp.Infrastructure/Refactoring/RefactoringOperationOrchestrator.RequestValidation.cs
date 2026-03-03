using RoslynMcp.Core;
using RoslynMcp.Core.Models.Common;
using RoslynMcp.Core.Models.Refactoring;

namespace RoslynMcp.Infrastructure.Refactoring;

internal static class RefactoringRequestValidator
{
    public static GetRefactoringsAtPositionResult? ValidateGetRefactoringsAtPosition(GetRefactoringsAtPositionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Path) || request.Line < 1 || request.Column < 1)
        {
            return new GetRefactoringsAtPositionResult(
                Array.Empty<RefactoringActionDescriptor>(),
                CreateError(ErrorCodes.InvalidInput,
                    "path, line, and column must be provided and valid.",
                    ("operation", "get_refactorings_at_position")));
        }

        if ((request.SelectionStart.HasValue && request.SelectionStart.Value < 0)
            || (request.SelectionLength.HasValue && request.SelectionLength.Value < 0))
        {
            return new GetRefactoringsAtPositionResult(
                Array.Empty<RefactoringActionDescriptor>(),
                CreateError(ErrorCodes.InvalidInput,
                    "selectionStart and selectionLength must be non-negative when provided.",
                    ("operation", "get_refactorings_at_position")));
        }

        return null;
    }

    public static GetCodeFixesResult? ValidateGetCodeFixes(GetCodeFixesRequest request)
    {
        if (!IsValidScope(request.Scope))
        {
            return new GetCodeFixesResult(Array.Empty<CodeFixDescriptor>(),
                CreateError(ErrorCodes.InvalidRequest,
                    "scope must be one of: document, project, solution.",
                    ("parameter", "scope"),
                    ("operation", "get_code_fixes")));
        }

        if (string.Equals(request.Scope, "document", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(request.Path))
        {
            return new GetCodeFixesResult(Array.Empty<CodeFixDescriptor>(),
                CreateError(ErrorCodes.InvalidRequest,
                    "path is required when scope is document.",
                    ("parameter", "path"),
                    ("operation", "get_code_fixes")));
        }

        return null;
    }

    public static ExecuteCleanupResult? ValidateExecuteCleanup(ExecuteCleanupRequest request)
    {
        if (!IsValidScope(request.Scope))
        {
            return new ExecuteCleanupResult(
                request.Scope,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                CreateError(ErrorCodes.InvalidRequest,
                    "scope must be one of: document, project, solution.",
                    ("operation", "execute_cleanup"),
                    ("field", "scope")));
        }

        if (string.Equals(request.Scope, "document", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(request.Path))
        {
            return new ExecuteCleanupResult(
                request.Scope,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                CreateError(ErrorCodes.InvalidRequest,
                    "path is required when scope is document.",
                    ("operation", "execute_cleanup"),
                    ("field", "path")));
        }

        if (string.Equals(request.Scope, "project", StringComparison.Ordinal) && string.IsNullOrWhiteSpace(request.Path))
        {
            return new ExecuteCleanupResult(
                request.Scope,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                CreateError(ErrorCodes.InvalidRequest,
                    "path is required when scope is project.",
                    ("operation", "execute_cleanup"),
                    ("field", "path")));
        }

        var profile = string.IsNullOrWhiteSpace(request.PolicyProfile) ? "balanced" : request.PolicyProfile.Trim().ToLowerInvariant();
        if (!string.Equals(profile, "balanced", StringComparison.Ordinal))
        {
            return new ExecuteCleanupResult(
                request.Scope,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                CreateError(ErrorCodes.InvalidInput,
                    "policyProfile must be 'balanced' for cleanup.",
                    ("operation", "execute_cleanup"),
                    ("field", "policyProfile")));
        }

        return null;
    }

    private static bool IsValidScope(string scope)
        => string.Equals(scope, "document", StringComparison.Ordinal)
           || string.Equals(scope, "project", StringComparison.Ordinal)
           || string.Equals(scope, "solution", StringComparison.Ordinal);

    private static ErrorInfo CreateError(string code, string message, params (string Key, string? Value)[] details)
    {
        if (details.Length == 0)
        {
            return new ErrorInfo(code, message);
        }

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in details)
        {
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
            {
                map[key] = value;
            }
        }

        return map.Count == 0 ? new ErrorInfo(code, message) : new ErrorInfo(code, message, map);
    }
}
