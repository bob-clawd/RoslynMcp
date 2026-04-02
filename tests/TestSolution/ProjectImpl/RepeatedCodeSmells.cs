namespace ProjectImpl;

public class RepeatedCodeSmells
{
    public bool First()
    {
        return true;
        Console.WriteLine("first");
    }

    public int Second()
    {
        return 2;
        Console.WriteLine("second");
    }

    public string Third()
    {
        return "third";
        Console.WriteLine("third");
    }
}
