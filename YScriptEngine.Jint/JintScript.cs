using Acornima.Ast;
using Jint;
using YScriptEngine.Abstractions;

namespace YScriptEngine.Jint;

public class JintScript(Engine engine, Prepared<Script> preparedProgram) : IScript
{
    public async Task ExecuteAsync(IScriptContext context)
    {
        engine.SetValue("context", context);
        await engine.EvaluateAsync(preparedProgram);
    }
}