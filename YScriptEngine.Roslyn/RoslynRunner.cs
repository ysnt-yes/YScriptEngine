using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using YScriptEngine.Abstractions;

namespace YScriptEngine.Roslyn;

public sealed class RoslynScript : IScript
{
    private Func<object?[], Task<object>>? _factoryDelegate;
    private AssemblyLoadContext? _loadContext;
    private bool _disposed;

    public RoslynScript(Assembly scriptAssembly, AssemblyLoadContext loadContext)
    {
        _loadContext = loadContext;

        var submissionType = scriptAssembly.GetType("Submission#0")
                             ?? throw new InvalidOperationException(
                                 "Compiled script assembly is missing 'Submission#0'.");

        var factoryMethod = submissionType.GetMethod("<Factory>", BindingFlags.Static | BindingFlags.Public)
                            ?? throw new InvalidOperationException(
                                "Compiled script assembly is missing '<Factory>' method.");

        _factoryDelegate = (Func<object?[], Task<object>>)Delegate.CreateDelegate(
            typeof(Func<object?[], Task<object>>),
            factoryMethod);
    }

    public async Task ExecuteAsync(IScriptContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(context);

        if (_factoryDelegate is not null)
        {
            // Roslyn's <Factory> expects [globals, submissionState]
            var submissionStates = new object?[] { context, null };
            await _factoryDelegate(submissionStates);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _factoryDelegate = null;
        _loadContext?.Unload();
        _loadContext = null;
    }
}