using ProjectCore;

namespace ProjectImpl;

public class CodeSmells
{
    // Smell 1: Unused private field
    private readonly IWorker _unusedField = new WorkerA();
    
    // Smell 2: Empty catch block
    public void TestEmptyCatch()
    {
        try
        {
            var x = int.Parse("1");
        }
        catch
        {
        }
    }
    
    // Smell 3: Magic numbers
    public int Calculate(int value)
    {
        return value * 42 + 100 - 7;
    }
    
    // Smell 4: Too many parameters
    public void DoSomething(int a, int b, int c, int d, int e, int f)
    {
    }
    
    // Smell 5: Dead code (unreachable code)
    public bool IsValid()
    {
        return true;
        Console.WriteLine("This is unreachable");
    }
    
    // Smell 6: Naming convention violation
    public int CalculateSumOFNumbers(int A, int B)
    {
        return A + B;
    }
}