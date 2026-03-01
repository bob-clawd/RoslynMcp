using RoslynMcp.Core;
using RoslynMcp.Core.Models.Common;
using RoslynMcp.Infrastructure.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Infrastructure.Navigation;

internal sealed class NavigationSolutionProvider
{
    private readonly IRoslynSolutionAccessor _solutionAccessor;
    private readonly ILogger _logger;

    public NavigationSolutionProvider(IRoslynSolutionAccessor solutionAccessor, ILogger logger)
    {
        _solutionAccessor = solutionAccessor ?? throw new ArgumentNullException(nameof(solutionAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<(Solution? Solution, ErrorInfo? Error)> TryGetSolutionAsync(CancellationToken ct)
    {
        try
        {
            return await _solutionAccessor.GetCurrentSolutionAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to access solution state");
            return (null,
                NavigationErrorFactory.CreateError(ErrorCodes.InternalError,
                    "Unable to access the current solution.",
                    ("operation", "navigation")));
        }
    }
}
