using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace RoslynMcp.Features;

public static class FeatureExtensions
{
    public static IEnumerable<Type> GetImplementations<T>() => Assembly.GetExecutingAssembly()
        .GetTypes()
        .Where(type => type.Implements<T>())
        .Distinct();
    
    extension(IServiceCollection services)
    {
        public IServiceCollection AddImplementations<T>()
        {
            foreach (var type in GetImplementations<T>())
                services.AddSingleton(type);
        
            return services;
        }
    }

    private static bool Implements<T>(this Type type) =>
        type is { IsClass: true, IsAbstract: false } && type.IsAssignableTo(typeof(T));
    
}
