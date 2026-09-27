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

    public static string GeneratePluginsDts(Dictionary<string, Type> activePlugins)
    {
        var sb = new StringBuilder();
        var customTypes = new HashSet<Type>();

        foreach (var plugin in activePlugins.Values)
        {
            sb.AppendLine($"interface {plugin.Name} {{");
            
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
            sb.AppendLine($"        readonly {Lower(kvp.Key)}: {kvp.Value.Name};");
        }
        sb.AppendLine("    };\n}");

        return sb.ToString();
    }

    public static string GenerateContextVariablesDts(Type contextType)
    {
        var sb = new StringBuilder();
        var customTypes = new HashSet<Type>();
        var propertiesBlock = new StringBuilder();

        foreach (var p in contextType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => !p.Name.Equals("plugins", StringComparison.OrdinalIgnoreCase)))
        {
            var isNullable = NullCtx.Create(p).ReadState == NullabilityState.Nullable;
            propertiesBlock.AppendLine($"    const {Lower(p.Name)}: {Map(p.PropertyType, customTypes)}{(isNullable ? " | null" : "")};");
        }

        sb.Append(GenerateCustomModelInterfaces(customTypes));

        sb.AppendLine("declare global {");
        sb.Append(propertiesBlock.ToString());
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
            if (processed.Contains(type) || type.IsPrimitive || type == typeof(string) || type == typeof(object)) continue;
            processed.Add(type);

            sb.AppendLine($"interface {type.Name} {{");
            
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var propType = p.PropertyType;
                if (!propType.IsPrimitive && propType != typeof(string) && propType != typeof(void) && !typeof(Task).IsAssignableFrom(propType))
                {
                    queue.Enqueue(propType.IsGenericType ? propType.GetGenericArguments().First() : propType);
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
        _ when t == typeof(int) || t == typeof(double) || t == typeof(float) || t == typeof(long) => "number",
        _ when t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>) => Map(Nullable.GetUnderlyingType(t)!, customTypes),
        _ when t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Task<>) => $"Promise<{Map(t.GetGenericArguments().First(), customTypes)}>",
        _ when t.IsGenericType && (t.GetGenericTypeDefinition() == typeof(List<>) || t.GetGenericTypeDefinition() == typeof(IEnumerable<>)) => $"{Map(t.GetGenericArguments().First(), customTypes)}[]",
        _ when typeof(Delegate).IsAssignableFrom(t) => "Function",
        _ => RegisterCustomType(t, customTypes)
    };

    private static string RegisterCustomType(Type t, HashSet<Type> customTypes)
    {
        var actualType = t.IsGenericType ? t.GetGenericArguments().First() : t;
        customTypes.Add(actualType);
        return actualType.Name;
    }
}
