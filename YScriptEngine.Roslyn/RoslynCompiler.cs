using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using YScriptEngine.Abstractions;

namespace YScriptEngine.Roslyn;

public class RoslynCompiler(IServiceProvider serviceProvider, ILogger<RoslynCompiler> logger) : ICompiler
{
    private readonly HashSet<string> _bannedTypesOrNamespaces = new(StringComparer.OrdinalIgnoreCase);
    private ScriptOptions GlobalOptions => serviceProvider.GetRequiredService<ScriptOptions>();

    public void AddBannedTypesOrNamespaces(IEnumerable<string> typeNames)
    {
        foreach (var typeName in typeNames)
        {
            _bannedTypesOrNamespaces.Add(typeName);
        }
    }

    public Task<IScript> CompileAsync(string scriptCode, Type contextType)
    {
        try
        {
            var types = new List<Type>();
            if (contextType.IsGenericType)
            {
                types.AddRange(contextType.GetGenericArguments());
            }

            var assemblies = types.Select(t => t.Assembly).Append(contextType.Assembly).Distinct().ToList();

            var namespaces = types.Select(t => t.Namespace).Append(contextType.Namespace)
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct();

            var runtimeOptions = GlobalOptions
                .AddReferences(assemblies)
                .AddImports(namespaces!);

            var script = CSharpScript.Create<object>(scriptCode, runtimeOptions, globalsType: contextType);
            var compilation = script.GetCompilation();

            var diagnostics = compilation.GetDiagnostics();
            LogCompilationDiagnostics(diagnostics);

            if (diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            {
                throw new CompilationErrorException("Failed to compile user script due to syntax errors.", diagnostics);
            }

            using var peStream = new MemoryStream();
            var emitResult = compilation.Emit(peStream);

            if (!emitResult.Success)
            {
                LogCompilationDiagnostics(emitResult.Diagnostics);
                throw new CompilationErrorException("Failed to emit script assembly.", emitResult.Diagnostics);
            }

            peStream.Position = 0;

            var scriptAlc = new ScriptAssemblyLoadContext(assemblies, runtimeOptions);
            var scriptAssembly = scriptAlc.LoadFromStream(peStream);

            var submissionType = scriptAssembly.GetType("Submission#0")
                ?? throw new InvalidOperationException("Compiled script is missing Submission#0 entry type.");

            var factoryMethod = submissionType.GetMethod("<Factory>", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Compiled script is missing <Factory> entry method.");

            var factoryDelegate = factoryMethod.CreateDelegate<Func<object?[], Task<object>>>();

            ScriptRunner<object> runDelegate = (globals, _) =>
            {
                var submissionArray = new[] { globals, null };
                return factoryDelegate(submissionArray);
            };

            return Task.FromResult<IScript>(new RoslynScript(runDelegate, scriptAlc));
        }
        catch (Exception exception)
        {
            return Task.FromException<IScript>(exception);
        }
    }

    private void LogCompilationDiagnostics(ImmutableArray<Diagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            switch (diagnostic.Severity)
            {
                case DiagnosticSeverity.Info: logger.LogInformation("{Message}", diagnostic.GetMessage()); break;
                case DiagnosticSeverity.Warning: logger.LogWarning("{Message}", diagnostic.GetMessage()); break;
                case DiagnosticSeverity.Error: logger.LogError("{Message}", diagnostic.GetMessage()); break;
                case DiagnosticSeverity.Hidden: break;
                default: logger.LogDebug("{Message}", diagnostic.GetMessage()); break;
            }
        }
    }

    private sealed class ScriptAssemblyLoadContext : AssemblyLoadContext
    {
        private readonly Dictionary<string, Assembly> _knownAssemblies;

        public ScriptAssemblyLoadContext(IEnumerable<Assembly> referencedAssemblies, ScriptOptions options)
            : base($"RoslynScript_{Guid.NewGuid():N}", isCollectible: true)
        {
            var refNames = options.MetadataReferences
                .OfType<PortableExecutableReference>()
                .Select(r => Path.GetFileNameWithoutExtension(r.FilePath))
                .Where(n => !string.IsNullOrEmpty(n))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var optionAssemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name is not null && refNames.Contains(a.GetName().Name!));

            _knownAssemblies = referencedAssemblies
                .Concat(optionAssemblies)
                .Where(a => a.GetName().Name is not null)
                .GroupBy(a => a.GetName().Name!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name is not null && _knownAssemblies.TryGetValue(assemblyName.Name, out var assembly))
            {
                return assembly;
            }

            return null;
        }
    }
}
