using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using YScriptEngine.Abstractions;

namespace YScriptEngine.Roslyn;

public sealed class RoslynScriptCompiler(IServiceProvider serviceProvider)
{
    private readonly ScriptOptions _globalOptions = 
        serviceProvider.GetService<ScriptOptions>() ?? ScriptOptions.Default;

    private readonly ILogger<RoslynScriptCompiler> _logger = 
        serviceProvider.GetService<ILogger<RoslynScriptCompiler>>() ?? NullLogger<RoslynScriptCompiler>.Instance;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public Task<IScript> CompileAsync(string scriptCode, Type contextType)
    {
        ArgumentNullException.ThrowIfNull(scriptCode);
        ArgumentNullException.ThrowIfNull(contextType);

        try
        {
            var types = new List<Type>();
            if (contextType.IsGenericType)
            {
                types.AddRange(contextType.GetGenericArguments());
            }

            var contextAssemblies = types.Select(t => t.Assembly)
                .Append(contextType.Assembly)
                .Distinct()
                .ToList();

            var namespaces = types.Select(t => t.Namespace)
                .Append(contextType.Namespace)
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct();

            var runtimeOptions = _globalOptions
                .AddReferences(contextAssemblies)
                .AddImports(namespaces!);

            var script = CSharpScript.Create<object>(scriptCode, runtimeOptions, globalsType: contextType);
            var compilation = script.GetCompilation();

            using var peStream = new MemoryStream();
            var emitResult = compilation.Emit(peStream);

            LogCompilationDiagnostics(emitResult.Diagnostics);

            if (!emitResult.Success)
            {
                throw new CompilationErrorException(
                    "Failed to compile user script due to syntax errors.",
                    emitResult.Diagnostics);
            }

            peStream.Seek(0, SeekOrigin.Begin);

            var globalReferencePaths = _globalOptions.MetadataReferences
                .OfType<PortableExecutableReference>()
                .Select(r => r.FilePath)
                .Where(path => !string.IsNullOrEmpty(path))!;

            var loadContext = new CollectibleScriptLoadContext(contextAssemblies, globalReferencePaths!);
            var scriptAssembly = loadContext.LoadFromStream(peStream);

            return Task.FromResult<IScript>(new RoslynScript(scriptAssembly, loadContext));
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
            if (diagnostic.IsSuppressed)
            {
                continue;
            }

            var logLevel = diagnostic.Severity switch
            {
                DiagnosticSeverity.Error => LogLevel.Error,
                DiagnosticSeverity.Warning => LogLevel.Warning,
                DiagnosticSeverity.Info => LogLevel.Information,
                DiagnosticSeverity.Hidden => LogLevel.Trace,
                _ => LogLevel.Debug
            };

            if (!_logger.IsEnabled(logLevel))
            {
                continue;
            }

            var lineSpan = diagnostic.Location.GetLineSpan();
            var line = lineSpan.StartLinePosition.Line + 1;
            var column = lineSpan.StartLinePosition.Character + 1;

            _logger.Log(
                logLevel,
                "Script compilation diagnostic [{DiagnosticId}] at ({Line},{Column}): {Message}",
                diagnostic.Id,
                line,
                column,
                diagnostic.GetMessage());
        }
    }

    private sealed class CollectibleScriptLoadContext : AssemblyLoadContext
    {
        private readonly Dictionary<string, Assembly> _knownAssemblies;
        private readonly Dictionary<string, string> _knownPathsBySimpleName;

        public CollectibleScriptLoadContext(
            IEnumerable<Assembly> knownAssemblies,
            IEnumerable<string> referenceFilePaths)
            : base(isCollectible: true)
        {
            _knownAssemblies = knownAssemblies
                .Where(a => a.FullName is not null)
                .GroupBy(a => a.FullName!)
                .ToDictionary(g => g.Key, g => g.First());

            _knownPathsBySimpleName = referenceFilePaths
                .Where(File.Exists)
                .GroupBy(p => Path.GetFileNameWithoutExtension(p), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (_knownAssemblies.TryGetValue(assemblyName.FullName, out var assembly))
            {
                return assembly;
            }

            try
            {
                return Default.LoadFromAssemblyName(assemblyName);
            }
            catch
            {
                // Not in Default ALC;
            }

            if (assemblyName.Name is not null &&
                _knownPathsBySimpleName.TryGetValue(assemblyName.Name, out var dllPath))
            {
                return Default.LoadFromAssemblyPath(dllPath);
            }

            return null;
        }
    }
}
