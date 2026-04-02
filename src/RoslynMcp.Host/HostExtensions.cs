using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using RoslynMcp.Tools.Extensions;

namespace RoslynMcp.Host;

public static class HostExtensions
{
    internal static string ServerVersion => Assembly.GetExecutingAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(HostExtensions).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    public static void Compose(this IServiceCollection services) => services
        .WithRoslynMcp()
        .AddMcpRuntime();

    private static void AddMcpRuntime(this IServiceCollection services)
    {
        var serializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            WriteIndented = true
        };

        var builder = services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation
            {
                Name = "RoslynMcp",
                Version = ServerVersion
            };
        });

        builder.WithStdioServerTransport();
        builder.WithTools(ServiceExtensions.GetTools(), serializerOptions);
    }
}
