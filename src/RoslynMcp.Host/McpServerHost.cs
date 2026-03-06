using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Host;

public static class McpServerHost
{
    public static async Task RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(consoleOptions =>
        {
            consoleOptions.SingleLine = true;
            consoleOptions.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] ";
        });

        builder.Services.Compose();

        var host = builder.Build();
        await host.RunAsync(cancellationToken).ConfigureAwait(false);
    }
}
