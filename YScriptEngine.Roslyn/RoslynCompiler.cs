using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.Logging;
using YScriptEngine.Abstractions;

namespace YScriptEngine.Roslyn;

public class RoslynCompiler(ScriptOptions globalOptions, ILogger<RoslynCompiler> logger) : ICompiler
{
    private readonly HashSet<string> _bannedTypesOrNamespaces = new(StringComparer.OrdinalIgnoreCase);
    
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

            var assemblies = types.Select(t => t.Assembly).Append(contextType.Assembly).Distinct();
            
            var namespaces = types.Select(t => t.Namespace).Append(contextType.Namespace)
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct();

            var runtimeOptions = globalOptions
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

            var runDelegate = script.CreateDelegate();
            return Task.FromResult<IScript>(new RoslynScript(runDelegate));
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
}
