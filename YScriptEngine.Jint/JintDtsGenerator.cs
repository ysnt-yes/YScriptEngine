using System.Reflection;
using System.Text;

namespace YScriptEngine.Jint;

public static class JintDtsGenerator
{
    private static readonly NullabilityInfoContext NullCtx = new();

    private static string Lower(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return char.ToLowerInvariant(s[0]) + s[1..];
    }

    private static string CleanTypeName(Type t)
    {
        if (!t.IsGenericType) return t.Name;

        var name = t.Name;
        var backtickIndex = name.IndexOf('`');
        if (backtickIndex > 0)
        {
            name = name[..backtickIndex];
        }

        var argNames = string.Join("_", t.GetGenericArguments().Select(CleanTypeName));
        return $"{name}_{argNames}";
    }

    public static string GeneratePluginsDts(Dictionary<string, Type> activePlugins)
    {
        var sb = new StringBuilder();
        var customTypes = new HashSet<Type>();

        foreach (var plugin in activePlugins.Values)
        {
            var cleanedPluginName = CleanTypeName(plugin);
            sb.AppendLine($"interface {cleanedPluginName} {{");
            
            foreach (var p in plugin.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var isNullable = NullCtx.Create(p).ReadState == NullabilityState.Nullable;
                sb.AppendLine($"    {Lower(p.Name)}: {Map(p.PropertyType, customTypes)}{(isNullable ? " | null" : "")};");
            }

            foreach (var m in plugin.GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => m.DeclaringType != typeof(object)))
            {
                var parameters = string.Join(", ", m.GetParameters().Select(p => {
                    var isParamNullable = NullCtx.Create(p).WriteState == NullabilityState.Nullable;
                    return $"{Lower(p.Name)}: {Map(p.ParameterType, customTypes)}{(isParamNullable ? " | null" : "")}";
                }));
                sb.AppendLine($"    {Lower(m.Name)}({parameters}): {Map(m.ReturnType, customTypes)};");
            }

            sb.AppendLine("}\n");
        }

        sb.Append(GenerateCustomModelInterfaces(customTypes));
        
        sb.AppendLine("declare global {");
        sb.AppendLine("    const plugins: {");
        foreach (var kvp in activePlugins)
        {
            sb.AppendLine($"        readonly {Lower(kvp.Key)}: {CleanTypeName(kvp.Value)};");
        }
        sb.AppendLine("    };\n}");

        return sb.ToString();
    }

    public static string GenerateContextVariablesDts(Type contextType)
    {
        var sb = new StringBuilder();
        var customTypes = new HashSet<Type>();

        var cleanedContextName = CleanTypeName(contextType);
        
        sb.AppendLine($"interface {cleanedContextName} {{");
        foreach (var p in contextType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => !p.Name.Equals("plugins", StringComparison.OrdinalIgnoreCase)))
        {
            var isNullable = NullCtx.Create(p).ReadState == NullabilityState.Nullable;
            sb.AppendLine($"    {Lower(p.Name)}: {Map(p.PropertyType, customTypes)}{(isNullable ? " | null" : "")};");
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

            var cleanedTypeName = CleanTypeName(type);
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
                sb.AppendLine($"    {Lower(p.Name)}: {Map(p.PropertyType, typesToGenerate)}{(isNullable ? " | null" : "")};");
            }

            foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.DeclaringType != typeof(object) && !m.IsSpecialName))
            {
                var parameters = string.Join(", ", m.GetParameters().Select(p => {
                    var isParamNullable = NullCtx.Create(p).WriteState == NullabilityState.Nullable;
                    return $"{Lower(p.Name)}: {Map(p.ParameterType, typesToGenerate)}{(isParamNullable ? " | null" : "")}";
                }));
                sb.AppendLine($"    {Lower(m.Name)}({parameters}): {Map(m.ReturnType, typesToGenerate)};");
            }

            sb.AppendLine("}\n");
        }
        return sb.ToString();
    }

    private static string Map(Type t, HashSet<Type> customTypes) => t switch
    {
        _ when t == typeof(string) => "string",
        _ when t == typeof(bool)   => "boolean",
        _ when t == typeof(void)   => "void",
        _ when t == typeof(Task)   => "Promise<void>",
        _ when t == typeof(int) || t == typeof(double) || t == typeof(float) || t == typeof(long) || t == typeof(decimal) => "number",
        _ when t == typeof(DateTime) || t == typeof(DateTimeOffset) => "string",
        _ when t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>) => Map(Nullable.GetUnderlyingType(t)!, customTypes),
        _ when t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Task<>) => $"Promise<{Map(t.GetGenericArguments().First(), customTypes)}>",
        _ when t.IsGenericType && (t.GetGenericTypeDefinition() == typeof(List<>) || t.GetGenericTypeDefinition() == typeof(IEnumerable<>)) => $"{Map(t.GetGenericArguments().First(), customTypes)}[]",
        _ when t.IsArray => $"{Map(t.GetElementType()!, customTypes)}[]",
        _ when typeof(Delegate).IsAssignableFrom(t) => "Function",
        _ => RegisterCustomType(t, customTypes)
    };

    private static string RegisterCustomType(Type t, HashSet<Type> customTypes)
    {
        var actualType = t.IsGenericType ? t.GetGenericArguments().First() : t;
        customTypes.Add(actualType);
        return CleanTypeName(actualType);
    }
}
