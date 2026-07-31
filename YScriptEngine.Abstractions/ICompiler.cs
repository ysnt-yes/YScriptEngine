using System;
using System.Threading.Tasks;

namespace YScriptEngine.Abstractions;

/// <summary>
/// Provides functionality for compiling C# script code into executable script instances.
/// </summary>
public interface ICompiler
{
    /// <summary>
    /// Asynchronously compiles source code into an executable <see cref="IScript"/> instance.
    /// </summary>
    /// <param name="scriptCode">The raw C# script string content to compile.</param>
    /// <param name="contextType">The type used to expose global parameters to the execution context.</param>
    /// <returns>A task that represents the compilation operation, containing the executable <see cref="IScript"/>.</returns>
    Task<IScript> CompileAsync(string scriptCode, Type contextType);
}


public static class CompilerExtensions
{
    public static Task<IScript> CompileAsync<TContext>(this ICompiler compiler, string scriptCode)
        where TContext : IScriptContext
    {
        return compiler.CompileAsync(scriptCode, typeof(TContext));
    }
}