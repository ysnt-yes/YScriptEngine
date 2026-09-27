using System.Reflection;
using Acornima.Ast;
using Jint;
using YScriptEngine.Abstractions;

namespace YScriptEngine.Jint;

public class JintScript(Engine engine, Prepared<Script> preparedProgram) : IScript
{
    public async Task ExecuteAsync(IScriptContext context)
    {

        var properties = context.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var prop in properties)
        {
            if (prop.CanRead)
            {
                engine.SetValue(prop.Name, prop.GetValue(context));
            }
        }

        await engine.EvaluateAsync(preparedProgram);
    }
}