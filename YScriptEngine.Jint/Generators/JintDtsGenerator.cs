using System.Reflection;
using System.Text;

namespace YScriptEngine.Jint.Generators;

public static class JintDtsGenerator
{
    private static readonly NullabilityInfoContext NullCtx = new();

    public static string GeneratePluginsDts(Dictionary<string, Type> activePlugins)
    {
        var sb = new StringBuilder();
        var customTypes = new HashSet<Type>();

        foreach (var plugin in activePlugins.Values)
        {
            var cleanedPluginName = Utils.CleanTypeName(plugin);
            sb.AppendLine($"interface {cleanedPluginName} {{");
            
            foreach (var p in plugin.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var isNullable = NullCtx.Create(p).ReadState == NullabilityState.Nullable;
                sb.AppendLine($"    {Utils.Lower(p.Name)}: {Utils.Map(p.PropertyType, customTypes)}{(isNullable ? " | null" : "")};");
            }

            foreach (var m in plugin.GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => m.DeclaringType != typeof(object)))
            {
                var parameters = string.Join(", ", m.GetParameters().Select(p => {
                    var isParamNullable = NullCtx.Create(p).WriteState == NullabilityState.Nullable;
                    return $"{Utils.Lower(p.Name)}: {Utils.Map(p.ParameterType, customTypes)}{(isParamNullable ? " | null" : "")}";
                }));
                sb.AppendLine($"    {Utils.Lower(m.Name)}({parameters}): {Utils.Map(m.ReturnType, customTypes)};");
            }

            sb.AppendLine("}\n");
        }

        sb.Append(GenerateCustomModelInterfaces(customTypes));
        
        sb.AppendLine("declare global {");
        sb.AppendLine("    const plugins: {");
        foreach (var kvp in activePlugins)
        {
            sb.AppendLine($"        readonly {Utils.Lower(kvp.Key)}: {Utils.CleanTypeName(kvp.Value)};");
        }
        sb.AppendLine("    };\n}");

        return sb.ToString();
    }

    public static string GenerateContextVariablesDts(Type contextType)
    {
        var sb = new StringBuilder();
        var customTypes = new HashSet<Type>();

        var cleanedContextName = Utils.CleanTypeName(contextType);
        
        sb.AppendLine($"interface {cleanedContextName} {{");
        foreach (var p in contextType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => !p.Name.Equals("plugins", StringComparison.OrdinalIgnoreCase)))
        {
            var isNullable = NullCtx.Create(p).ReadState == NullabilityState.Nullable;
            sb.AppendLine($"    {Utils.Lower(p.Name)}: {Utils.Map(p.PropertyType, customTypes)}{(isNullable ? " | null" : "")};");
        }
        sb.AppendLine("}\n");

        sb.Append(GenerateCustomModelInterfaces(customTypes));

        sb.AppendLine("declare global {");
        sb.AppendLine($"    const context: {cleanedContextName};");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string GenerateCustomModelInterfaces(HashSet<Type> typesToGenerate)
    {
        var sb = new StringBuilder();
        var processed = new HashSet<Type>();
        var queue = new Queue<Type>(typesToGenerate);

        while (queue.Count > 0)
        {
            var type = queue.Dequeue();
            if (processed.Contains(type) || type.IsPrimitive || type == typeof(string) || type == typeof(object) || type == typeof(void) || typeof(Task).IsAssignableFrom(type)) continue;
            processed.Add(type);

            var cleanedTypeName = Utils.CleanTypeName(type);
            sb.AppendLine($"interface {cleanedTypeName} {{");
            
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var propType = p.PropertyType;
                var baseType = propType.IsGenericType ? propType.GetGenericArguments().First() : propType;
                if (!baseType.IsPrimitive && baseType != typeof(string) && baseType != typeof(object) && baseType != typeof(void) && !typeof(Task).IsAssignableFrom(baseType))
                {
                    queue.Enqueue(baseType);
                }
                var isNullable = NullCtx.Create(p).ReadState == NullabilityState.Nullable;
                sb.AppendLine($"    {Utils.Lower(p.Name)}: {Utils.Map(p.PropertyType, typesToGenerate)}{(isNullable ? " | null" : "")};");
            }

            foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.DeclaringType != typeof(object) && !m.IsSpecialName))
            {
                var parameters = string.Join(", ", m.GetParameters().Select(p => {
                    var isParamNullable = NullCtx.Create(p).WriteState == NullabilityState.Nullable;
                    return $"{Utils.Lower(p.Name)}: {Utils.Map(p.ParameterType, typesToGenerate)}{(isParamNullable ? " | null" : "")}";
                }));
                sb.AppendLine($"    {Utils.Lower(m.Name)}({parameters}): {Utils.Map(m.ReturnType, typesToGenerate)};");
            }

            sb.AppendLine("}\n");
        }
        return sb.ToString();
    }
}
