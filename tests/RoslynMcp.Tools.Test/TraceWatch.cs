using System.Diagnostics;
using System.Reflection;
using Xunit.Sdk;

namespace RoslynMcp.Tools.Test;

public sealed class TraceWatchAttribute : BeforeAfterTestAttribute
{
    private TraceWatch? _traceWatch;
    
    public override void Before(MethodInfo methodUnderTest)
    {
        _traceWatch = new TraceWatch(Console.WriteLine, $"{methodUnderTest.DeclaringType?.Name} - {methodUnderTest.Name}");
    }
    public override void After(MethodInfo methodUnderTest)
    {
        _traceWatch?.Dispose();
    }
}

public class TraceWatch : Stopwatch, IDisposable
{
    private readonly Action<string>? _trace;

    public string Message { get; set; }

    public TraceWatch(Action<string> trace, object? caller = null, string? message = null)
    {
        _trace = trace;
        
        Message = message ?? $"-------------- Method '{caller}'";

        _trace?.Invoke($"{Message} started");

        Start();
    }

    public void Dispose()
    {
        Stop();

        _trace?.Invoke($"{Message} end (Duration={Elapsed.TotalSeconds:F3}s)");
    }
}