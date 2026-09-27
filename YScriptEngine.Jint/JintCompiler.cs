using Jint;
using YScriptEngine.Abstractions;

namespace YScriptEngine.Jint;

public class JintCompiler(Engine engine) : ICompiler
{
    public Task<IScript> CompileAsync(string scriptCode, Type contextType)
    {
        var program = Engine.PrepareScript(scriptCode);
        return Task.FromResult<IScript>(new JintScript(engine, program));
    }
}