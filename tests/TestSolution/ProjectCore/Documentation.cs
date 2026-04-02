namespace ProjectCore;

/// <summary>
/// Documentation service for testing XML comment parsing.
/// Provides summary, returns, and parameter documentation.
/// </summary>
public class Documentation
{
    /// <summary>
    /// Processes the given input and returns a processed result.
    /// </summary>
    /// <param name="input">The input string to process.</param>
    /// <returns>A processed string result.</returns>
    public string Process(string input)
    {
        return input.ToUpperInvariant();
    }

    /// <summary>
    /// Calculates the sum of two numbers.
    /// </summary>
    /// <param name="a">The first number.</param>
    /// <param name="b">The second number.</param>
    /// <returns>The sum of the two numbers.</returns>
    public int Add(int a, int b)
    {
        return a + b;
    }

    /// <summary>
    /// Formats a message with the specified name.
    /// </summary>
    /// <param name="name">The name to include in the message.</param>
    /// <returns>A formatted greeting message.</returns>
    public string FormatMessage(string name)
    {
        return $"Hello, {name}!";
    }

    /// <summary>
    /// Method with no documentation.
    /// </summary>
    public string NoDocumentationMethod()
    {
        return "test";
    }

    /// <summary>
    /// Method with <paramref name="input"/> reference.
    /// </summary>
    /// <param name="input">The input parameter.</param>
    /// <returns>The same input.</returns>
    public string WithParamRef(string input)
    {
        return input;
    }

    /// <summary>
    /// Method with <see cref="Documentation"/> reference.
    /// </summary>
    /// <returns>A new Documentation instance.</returns>
    public Documentation CreateDocumentation()
    {
        return new Documentation();
    }

    /// <summary>
    /// Method with both <paramref name="x"/> and <see cref="string"/> references.
    /// </summary>
    /// <param name="x">The x parameter.</param>
    /// <returns>The string representation.</returns>
    public string MixedReferences(string x)
    {
        return x;
    }

    /// <summary>
    /// Method that returns void and has parameters.
    /// </summary>
    /// <param name="value">The value to set.</param>
    public void SetValue(int value)
    {
        // Implementation
    }
    
    public void NoDocumentation()
    { }
}
