using System.Runtime.Loader;
using Microsoft.CodeAnalysis.Scripting;
using YScriptEngine.Abstractions;

namespace YScriptEngine.Roslyn;

public sealed class RoslynScript(
    ScriptRunner<object>? runDelegate, 
    AssemblyLoadContext loadContext) : IScript, IDisposable
{
    private ScriptRunner<object>? _runDelegate = runDelegate;
    private bool _disposed;

    public async Task ExecuteAsync(IScriptContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(context);

        if (_runDelegate is not null)
        {
            await _runDelegate(context);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Drop the delegate reference so the GC can collect the Submission#0 assembly
        _runDelegate = null;
        loadContext.Unload();
    }
}