using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OracleSqlParser;

/// <summary>Serializes an AST to JSON. Each node gets a "type" property; null/empty/false members are omitted.</summary>
public static class AstJson
{
    public static string Serialize(SqlNode node, bool includePositions = false, bool indented = true) =>
        ToJson(node, includePositions).ToJsonString(new JsonSerializerOptions { WriteIndented = indented });

    public static JsonNode ToJson(SqlNode node, bool includePositions = false)
    {
        var obj = new JsonObject { ["type"] = node.GetType().Name };
        if (includePositions)
            obj["span"] = new JsonObject { ["start"] = node.Start, ["end"] = node.End, ["line"] = node.Line, ["column"] = node.Column };

        foreach (var p in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.DeclaringType == typeof(SqlNode)) continue;
            var v = p.GetValue(node);
            var jv = Convert(v, includePositions);
            if (jv != null) obj[Camel(p.Name)] = jv;
        }
        return obj;
    }

    private static JsonNode? Convert(object? v, bool pos) => v switch
    {
        null => null,
        SqlNode n => ToJson(n, pos),
        string s => JsonValue.Create(s),
        bool b => b ? JsonValue.Create(true) : null,
        Enum e => JsonValue.Create(e.ToString()),
        int or long or double => JsonValue.Create(System.Convert.ToDouble(v)),
        IEnumerable e => ConvertList(e, pos),
        _ => JsonValue.Create(v.ToString()),
    };

    private static JsonNode? ConvertList(IEnumerable e, bool pos)
    {
        var arr = new JsonArray();
        foreach (var item in e) arr.Add(Convert(item, pos));
        return arr.Count == 0 ? null : arr;
    }

    private static string Camel(string s) => char.ToLowerInvariant(s[0]) + s[1..];
}
