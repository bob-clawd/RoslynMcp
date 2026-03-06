using Is.Assertions;
using RoslynMcp.Core.Models;

namespace RoslynMcp.Features.Tests;

public static class AssertionsExtensions
{
    public static void ShouldBeNone(this ErrorInfo? error)
    {
        error.IsNull();
    }

    public static void ShouldHaveCode(this ErrorInfo? error, string expectedCode)
    {
        error.IsNotNull();
        error!.Code.Is(expectedCode);
    }
    
    public static void ShouldNotBeEmtpy(this string text)
    {
        string.IsNullOrEmpty(text).IsFalse();
    }
}
