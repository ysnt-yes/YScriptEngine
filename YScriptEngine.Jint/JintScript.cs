using System.Reflection;
using Acornima.Ast;
using Jint;
using YScriptEngine.Abstractions;

namespace YScriptEngine.Jint;

public class JintScript(Prepared<Script> preparedProgram) : IScript
{

    public async Task ExecuteAsync(IScriptContext context)
    {
        var vm = new Engine();

        var properties = context.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var prop in properties)
        {
            if (prop.CanRead)
            {
                vm.SetValue(prop.Name, prop.GetValue(context));
            }
        }

        await vm.EvaluateAsync(preparedProgram);
    }
}