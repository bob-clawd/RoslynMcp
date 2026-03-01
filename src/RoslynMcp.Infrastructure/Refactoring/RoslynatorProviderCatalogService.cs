using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FindSymbols;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;

namespace RoslynMcp.Infrastructure.Refactoring;

internal sealed class RoslynatorProviderCatalogService
{
    private const string RoslynatorAnalyzerPackageId = "roslynator.analyzers";
    private const string RoslynatorAnalyzerPackageVersion = "4.15.0";
    private const string RoslynatorAnalyzerFilename = "Roslynator.CSharp.Analyzers.dll";
    private const string RoslynatorCodeFixesFilename = "Roslynator.CSharp.Analyzers.CodeFixes.dll";
    private const string RoslynatorCodeFixPathEnvVar = "RoslynMcp__RoslynatorCodeFixPath";

    private static readonly string[] RoslynatorAnalyzerRelativePathSegments =
        { "analyzers", "dotnet", "roslyn4.7", "cs", RoslynatorAnalyzerFilename };

    private static readonly string[] RoslynatorCodeFixRelativePathSegments =
        { "analyzers", "dotnet", "roslyn4.7", "cs", RoslynatorCodeFixesFilename };

    private static readonly Lazy<(ImmutableArray<DiagnosticAnalyzer> Analyzers, ImmutableArray<CodeFixProvider> CodeFixProviders, ImmutableArray<CodeRefactoringProvider> RefactoringProviders, Exception? Error)> s_providerCatalog =
        new(LoadProviderCatalog, LazyThreadSafetyMode.ExecutionAndPublication);

    public (ImmutableArray<DiagnosticAnalyzer> Analyzers,
        ImmutableArray<CodeFixProvider> CodeFixProviders,
        ImmutableArray<CodeRefactoringProvider> RefactoringProviders,
        Exception? Error) Catalog => s_providerCatalog.Value;

    private static (ImmutableArray<DiagnosticAnalyzer> Analyzers,
        ImmutableArray<CodeFixProvider> CodeFixProviders,
        ImmutableArray<CodeRefactoringProvider> RefactoringProviders,
        Exception? Error) LoadProviderCatalog()
    {
        try
        {
            var analyzerPath = ResolveRoslynatorAssemblyPath(
                RoslynatorAnalyzerPackageId,
                RoslynatorAnalyzerPackageVersion,
                null,
                RoslynatorAnalyzerRelativePathSegments,
                RoslynatorAnalyzerFilename);
            var codeFixPath = ResolveRoslynatorAssemblyPath(
                RoslynatorAnalyzerPackageId,
                RoslynatorAnalyzerPackageVersion,
                RoslynatorCodeFixPathEnvVar,
                RoslynatorCodeFixRelativePathSegments,
                RoslynatorCodeFixesFilename);

            var loader = new RoslynatorProviderLoader();
            loader.AddDependencyLocation(analyzerPath);
            loader.AddDependencyLocation(codeFixPath);

            var analyzerReference = new AnalyzerFileReference(analyzerPath, loader);
            var analyzers = analyzerReference.GetAnalyzers(LanguageNames.CSharp);

            var codeFixes = LoadCodeFixProviders(codeFixPath);

            var refactorings = LoadRefactoringProviders(codeFixPath);
            return (analyzers, codeFixes, refactorings, null);
        }
        catch (Exception ex)
        {
            return (ImmutableArray<DiagnosticAnalyzer>.Empty,
                ImmutableArray<CodeFixProvider>.Empty,
                ImmutableArray<CodeRefactoringProvider>.Empty,
                ex);
        }
    }

    private static ImmutableArray<CodeRefactoringProvider> LoadRefactoringProviders(string assemblyPath)
    {
        var assembly = Assembly.LoadFrom(assemblyPath);
        var providers = assembly.GetTypes()
            .Where(type => typeof(CodeRefactoringProvider).IsAssignableFrom(type)
                           && !type.IsAbstract)
            .Select(CreateProviderInstance<CodeRefactoringProvider>)
            .Where(static provider => provider != null)
            .Cast<CodeRefactoringProvider>()
            .ToImmutableArray();
        return providers;
    }

    private static ImmutableArray<CodeFixProvider> LoadCodeFixProviders(string assemblyPath)
    {
        var assembly = Assembly.LoadFrom(assemblyPath);
        return assembly.GetTypes()
            .Where(type => typeof(CodeFixProvider).IsAssignableFrom(type)
                           && !type.IsAbstract)
            .Select(CreateProviderInstance<CodeFixProvider>)
            .Where(static provider => provider != null)
            .Cast<CodeFixProvider>()
            .ToImmutableArray();
    }

    private static TProvider? CreateProviderInstance<TProvider>(Type providerType)
        where TProvider : class
    {
        var instanceProperty = providerType.GetProperty(
            "Instance",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (instanceProperty?.GetValue(null) is TProvider shared)
        {
            return shared;
        }

        try
        {
            return Activator.CreateInstance(providerType, nonPublic: true) as TProvider;
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveRoslynatorAssemblyPath(
        string packageId,
        string packageVersion,
        string? overrideEnvVar,
        IReadOnlyList<string> relativeSegments,
        string filename)
    {
        if (!string.IsNullOrWhiteSpace(overrideEnvVar))
        {
            var overridePath = Environment.GetEnvironmentVariable(overrideEnvVar);
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                if (File.Exists(overridePath))
                {
                    return overridePath;
                }

                throw new InvalidOperationException(
                    $"Roslynator assembly path '{overridePath}' configured via '{overrideEnvVar}' could not be found.");
            }
        }

        var packagesRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

        var packageDirectory = Path.Combine(packagesRoot, packageId, packageVersion);
        if (!Directory.Exists(packageDirectory))
        {
            throw new InvalidOperationException(
                $"NuGet package '{packageId}' version '{packageVersion}' was not found under '{packagesRoot}'.");
        }

        var candidate = Path.Combine(packageDirectory, Path.Combine(relativeSegments.ToArray()));
        if (!File.Exists(candidate))
        {
            throw new InvalidOperationException($"Unable to locate '{filename}' under '{packageDirectory}'.");
        }

        return candidate;
    }

    private sealed class RoslynatorProviderLoader : IAnalyzerAssemblyLoader
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, Assembly> _assemblies = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);

        public RoslynatorProviderLoader()
        {
            AssemblyLoadContext.Default.Resolving += ResolveAssembly;
        }

        public void AddDependencyLocation(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                return;
            }

            LoadFromPath(fullPath);
        }

        public Assembly LoadFromPath(string fullPath)
        {
            lock (_gate)
            {
                if (_assemblies.TryGetValue(fullPath, out var existing))
                {
                    return existing;
                }

                var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
                _assemblies[fullPath] = assembly;
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    _directories.Add(directory);
                }

                return assembly;
            }
        }

        private Assembly? ResolveAssembly(AssemblyLoadContext _, AssemblyName name)
        {
            lock (_gate)
            {
                foreach (var directory in _directories)
                {
                    var path = Path.Combine(directory, name.Name + ".dll");
                    if (_assemblies.TryGetValue(path, out var existing))
                    {
                        return existing;
                    }

                    if (File.Exists(path))
                    {
                        return LoadFromPath(path);
                    }
                }
            }

            return null;
        }
    }
}
