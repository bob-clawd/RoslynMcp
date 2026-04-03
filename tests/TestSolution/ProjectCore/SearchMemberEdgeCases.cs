namespace ProjectCore;

public class SearchMemberEdgeCases
{
    public event Action? EventA;
    public event Action? EventB;

    public SearchMemberEdgeCases()
    {
    }

    public void RaiseA()
    {
        EventA?.Invoke();
    }
}

