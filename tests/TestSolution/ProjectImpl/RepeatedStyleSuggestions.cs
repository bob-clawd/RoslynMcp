namespace ProjectImpl;

public sealed class RepeatedStyleSuggestions
{
    public int CountFlags(bool first, bool second)
    {
        var total = 0;

        if (first)
            total++;

        if (second)
            total++;

        return total;
    }
}
