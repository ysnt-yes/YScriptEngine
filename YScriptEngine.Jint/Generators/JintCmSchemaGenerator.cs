using System.Reflection;
using System.Text.Json;

namespace YScriptEngine.Jint.Generators;

public static class JintCmSchemaGenerator
{
    private static readonly NullabilityInfoContext NullCtx = new();

    

    public static string GeneratePluginsSchema(Dictionary<string, Type> activePlugins)
    {
        var customTypes = new HashSet<Type>();
        var pluginsScope = new Dictionary<string, object>();

        foreach (var (key, plugin) in activePlugins)
        {
            var pluginProperties = new Dictionary<string, object>();

            foreach (var p in plugin.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                pluginProperties[Utils.Lower(p.Name)] = Utils.Map(p.PropertyType, customTypes);
            }

            foreach (var m in plugin.GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => m.DeclaringType != typeof(object)))
            {
                var paramDesc = string.Join(", ", m.GetParameters().Select(p => $"{Utils.Lower(p.Name)}"));
                pluginProperties[$"{Utils.Lower(m.Name)}()"] = new
                {
                    isMethod = true,
                    returns = Utils.Map(m.ReturnType, customTypes),
                    detail = $"({paramDesc})"
                };
            }

            pluginsScope[Utils.Lower(key)] = new { referenceType = Utils.CleanTypeName(plugin), inlineMembers = pluginProperties };
        }

        var rootSchema = new Dictionary<string, object>
        {
            { "globals", new Dictionary<string, object> { { "plugins", pluginsScope } } },
            { "types", GenerateCustomModelInterfaces(customTypes) }
        };

        return JsonSerializer.Serialize(rootSchema, new JsonSerializerOptions { WriteIndented = false });
    }

    public static string GenerateContextVariablesSchema(Type contextType)
    {
        var customTypes = new HashSet<Type>();
        var contextScope = new Dictionary<string, object>();

        foreach (var p in contextType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => !p.Name.Equals("plugins", StringComparison.OrdinalIgnoreCase)))
        {
            contextScope[Utils.Lower(p.Name)] = Utils.Map(p.PropertyType, customTypes);
        }

        var rootSchema = new Dictionary<string, object>
        {
            { "globals", new Dictionary<string, object> { { "context", contextScope } } },
            { "types", GenerateCustomModelInterfaces(customTypes) }
        };

        return JsonSerializer.Serialize(rootSchema, new JsonSerializerOptions { WriteIndented = false });
    }

    private static Dictionary<string, object> GenerateCustomModelInterfaces(HashSet<Type> typesToGenerate)
    {
        var serializedTypes = new Dictionary<string, object>();
        var processed = new HashSet<Type>();
        var queue = new Queue<Type>(typesToGenerate);

        while (queue.Count > 0)
        {
            var type = queue.Dequeue();
            if (processed.Contains(type) || type.IsPrimitive || type == typeof(string) || type == typeof(object) || type == typeof(void) || typeof(Task).IsAssignableFrom(type)) continue;
            processed.Add(type);

            var typeDef = new Dictionary<string, object>();
            var cleanedTypeName = Utils.CleanTypeName(type);

            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var propType = p.PropertyType;
                var baseType = propType.IsGenericType ? propType.GetGenericArguments().First() : propType;
                if (!baseType.IsPrimitive && baseType != typeof(string) && baseType != typeof(object) && baseType != typeof(void) && !typeof(Task).IsAssignableFrom(baseType))
                {
                    queue.Enqueue(baseType);
                }
                typeDef[Utils.Lower(p.Name)] = Utils.Map(p.PropertyType, typesToGenerate);
            }

            foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.DeclaringType != typeof(object) && !m.IsSpecialName))
            {
                var paramDesc = string.Join(", ", m.GetParameters().Select(p => $"{Utils.Lower(p.Name)}"));
                typeDef[$"{Utils.Lower(m.Name)}()"] = new
                {
                    isMethod = true,
                    returns = Utils.Map(m.ReturnType, typesToGenerate),
                    detail = $"({paramDesc})"
                };
            }

            serializedTypes[cleanedTypeName] = typeDef;
        }

        return serializedTypes;
    }
}
