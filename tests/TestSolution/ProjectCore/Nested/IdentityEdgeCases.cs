namespace ProjectCore.Nested;

public class GenericFoo<T>
{
}

public class GenericFoo<T1, T2>
{
}

public class OuterA
{
    public enum InnerEnum
    {
        A,
        B
    }

    public delegate void InnerDelegate(int value);
}

public delegate void GenericDelegate<T>(T value);
public delegate void GenericDelegate<T1, T2>(T1 a, T2 b);
