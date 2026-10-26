using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Nekomata.AI.Schemas;

/// <summary>
/// A strict JSON schema built from a plain result class, for structured answers that have no hand-written schema.
/// Every public settable property is required (strict mode requires it) and named in camelCase to match the provider's
/// serializer. string, bool, whole and decimal numbers, enums (as their names), lists/arrays and nested classes are
/// supported; nullable values may also be null. Anything else is described as a string.
/// </summary>
public static partial class ReflectionSchema
{
    public static (string Name, string Json) For(Type type)
    {
        var name = NameRegex().Replace(type.Name, "_").ToLowerInvariant();
        var root = ObjectSchema(type, new HashSet<Type>());
        return (name.Length > 64 ? name[..64] : name, root.ToJsonString());
    }

    private static JsonObject ObjectSchema(Type type, HashSet<Type> seen)
    {
        if (!seen.Add(type)) throw new NotSupportedException($"{type.Name} refers to itself, which a strict schema can't express.");
        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0))
        {
            var key = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            properties[key] = ValueSchema(property.PropertyType, seen);
            required.Add(key);
        }
        seen.Remove(type);
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required,
            ["additionalProperties"] = false
        };
    }

    private static JsonNode ValueSchema(Type type, HashSet<Type> seen)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null) return WithNull(ValueSchema(underlying, seen));
        if (type == typeof(string) || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(Guid))
            return new JsonObject { ["type"] = "string" };
        if (type == typeof(bool)) return new JsonObject { ["type"] = "boolean" };
        if (type == typeof(int) || type == typeof(long) || type == typeof(short)) return new JsonObject { ["type"] = "integer" };
        if (type == typeof(double) || type == typeof(float) || type == typeof(decimal)) return new JsonObject { ["type"] = "number" };
        if (type.IsEnum)
            return new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(Enum.GetNames(type).Select(n => (JsonNode)n).ToArray()) };
        var element = type.IsArray ? type.GetElementType()
            : type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type) ? type.GetGenericArguments()[0] : null;
        if (element is not null) return new JsonObject { ["type"] = "array", ["items"] = ValueSchema(element, seen) };
        if (type.IsClass) return ObjectSchema(type, seen);
        return new JsonObject { ["type"] = "string" };
    }

    private static JsonNode WithNull(JsonNode schema)
    {
        if (schema is JsonObject obj && obj["type"] is JsonValue value)
            obj["type"] = new JsonArray(value.GetValue<string>(), "null");
        return schema;
    }

    [GeneratedRegex("[^A-Za-z0-9_-]+")]
    private static partial Regex NameRegex();
}
