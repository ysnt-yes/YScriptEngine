namespace YScriptEngine.Jint.Generators;

public static class Utils
{
    public static string Lower(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return char.ToLowerInvariant(s[0]) + s[1..];
    }

    public static string CleanTypeName(Type t)
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

    public static object Map(Type t, HashSet<Type> customTypes) => t switch
    {
        _ when t == typeof(string) => "string",
        _ when t == typeof(bool)   => "boolean",
        _ when t == typeof(void)   => "void",
        _ when t == typeof(Task)   => "void",
        _ when t == typeof(int) || t == typeof(double) || t == typeof(float) || t == typeof(long) || t == typeof(decimal) => "number",
        _ when t == typeof(DateTime) || t == typeof(DateTimeOffset) => "string",
        _ when t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>) => Map(Nullable.GetUnderlyingType(t)!, customTypes),
        _ when t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Task<>) => Map(t.GetGenericArguments().First(), customTypes),
        _ when t.IsGenericType && (t.GetGenericTypeDefinition() == typeof(List<>) || t.GetGenericTypeDefinition() == typeof(IEnumerable<>)) => new { arrayOfType = Map(t.GetGenericArguments().First(), customTypes) },
        _ when t.IsArray => new { arrayOfType = Map(t.GetElementType()!, customTypes) },
        _ when typeof(Delegate).IsAssignableFrom(t) => "function",
        _ => RegisterCustomType(t, customTypes)
    };

    private static object RegisterCustomType(Type t, HashSet<Type> customTypes)
    {
        var actualType = t.IsGenericType ? t.GetGenericArguments().First() : t;
        customTypes.Add(actualType);
        return new { referenceType = Utils.CleanTypeName(actualType) };
    }
}