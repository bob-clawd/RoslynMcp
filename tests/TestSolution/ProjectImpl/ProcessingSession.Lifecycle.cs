namespace ProjectImpl;

public partial class ProcessingSession
{
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ChangeState("Starting");
        await Task.Delay(5, cancellationToken);
        ChangeState("Running");
    }

    public void Stop()
    {
        ChangeState("Stopped");
    }
}
