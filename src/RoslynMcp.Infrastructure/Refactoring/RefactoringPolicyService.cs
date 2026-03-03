namespace RoslynMcp.Infrastructure.Refactoring;

internal sealed class RefactoringPolicyService
{
    public PolicyAssessment Evaluate(DiscoveredAction action, string policyProfile)
    {
        var profile = string.IsNullOrWhiteSpace(policyProfile)
            ? "default"
            : policyProfile.Trim();

        if (!string.Equals(profile, "default", StringComparison.OrdinalIgnoreCase))
        {
            return new PolicyAssessment(
                "block",
                "blocked",
                "unknown_profile",
                $"Policy profile '{profile}' is not supported.");
        }

        if (string.Equals(action.ProviderActionKey, RefactoringOperationOrchestrator.RefactoringOperationUseVar, StringComparison.Ordinal))
        {
            return new PolicyAssessment(
                "review_required",
                "review_required",
                "manual_review_required",
                "This refactoring requires manual review before apply.");
        }

        if (string.Equals(action.Origin, RefactoringOperationOrchestrator.OriginRoslynatorCodeFix, StringComparison.Ordinal)
            && string.Equals(action.Category, RefactoringOperationOrchestrator.SupportedFixCategory, StringComparison.Ordinal)
            && action.DiagnosticId != null
            && RefactoringOperationOrchestrator.SupportedFixDiagnosticIds.Contains(action.DiagnosticId))
        {
            return new PolicyAssessment(
                "allow",
                "safe",
                "allowlisted",
                "Action is allowlisted in the default policy profile.");
        }

        return new PolicyAssessment(
            "block",
            "blocked",
            "not_allowlisted",
            "Action is not allowlisted in the default policy profile.");
    }
}
