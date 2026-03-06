namespace RoslynMcp.Host;

public static class Program
{
    public static async Task Main(string[] args)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        await McpServerHost.RunAsync(args, cts.Token).ConfigureAwait(false);
    }
}
