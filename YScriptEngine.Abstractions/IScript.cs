using System.Threading.Tasks;

namespace YScriptEngine.Abstractions;

/// <summary>
/// Defines the contract for executing scripts with a specific context.
/// </summary>
public interface IScript
{
    Task ExecuteAsync(IScriptContext context);
}
