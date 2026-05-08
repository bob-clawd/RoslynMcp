using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace RoslynMcp.Tools.Extensions;

public static class ServiceExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection WithRoslynMcp() => services
            .AddImplementations<Manager>()
            .AddTypes(GetTools());

        private IServiceCollection AddTypes(IEnumerable<Type> types)
        {
            foreach (var type in types)
                services.AddSingleton(type);

            return services;
        }

        private IServiceCollection AddImplementations<T>() => services.AddTypes(GetImplementations<T>());
    }
    
    public static IEnumerable<Type> GetTools() => Assembly.GetExecutingAssembly()
        .GetTypes()
        .Where(type => type.IsTool());
    
    private static IEnumerable<Type> GetImplementations<T>() => Assembly.GetExecutingAssembly()
        .GetTypes()
        .Where(type => type.Implements<T>())
        .Distinct();
    
    private static bool Implements<T>(this Type type) =>
        type is { IsClass: true, IsAbstract: false } && type.IsAssignableTo(typeof(T));

    private static bool IsTool(this Type type) =>
        type is { IsClass: true, IsAbstract: false } &&
        type.GetCustomAttribute<McpServerToolTypeAttribute>(false) is not null;

}
