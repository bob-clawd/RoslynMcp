namespace ProjectCore;

public interface IWorker
{
    void Work();
}

public class WorkerA : IWorker
{
    public void Work() { }
}

public class WorkerB : IWorker
{
    public void Work() { }
}

public class BaseClass : IWorker
{
    public virtual void Work() { }
}

public class DerivedClass : BaseClass
{
    public override void Work() { }
}

public class LeafClass : DerivedClass
{ }