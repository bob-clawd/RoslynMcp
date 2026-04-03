namespace ProjectCore;

public class SearchMemberEdgeCases
{
    public event Action? EventA;
    public event Action? EventB;

    public SearchMemberEdgeCases()
    {
    }

    public void Overload(int value)
    {
    }

    public void Overload(string value)
    {
    }

    public void RaiseA()
    {
        EventA?.Invoke();
    }
}
