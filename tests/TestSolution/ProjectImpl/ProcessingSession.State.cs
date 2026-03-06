namespace ProjectImpl;

public partial class ProcessingSession
{
    private readonly List<string> _stateHistory = [];

    public event EventHandler<string>? StateChanged;

    public IReadOnlyList<string> StateHistory => _stateHistory;

    private void ChangeState(string state)
    {
        _stateHistory.Add(state);
        StateChanged?.Invoke(this, state);
    }
}
