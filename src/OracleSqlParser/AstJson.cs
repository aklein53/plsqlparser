using System.Collections;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OracleSqlParser;

/// <summary>
/// Serializes an AST to JSON. Each node gets a "type" property; null/empty/false members are omitted.
/// Serialization is iterative (explicit work stack), so very deep trees (e.g. left-deep AND chains) do not
/// overflow the call stack; the nesting limit is <see cref="MaxDepth"/>.
/// </summary>
public static class AstJson
{
    /// <summary>Maximum JSON nesting depth written or read.</summary>
    public const int MaxDepth = 1_000_000;

    /// <summary>Serializes <paramref name="node"/> to a JSON string. <paramref name="includePositions"/> adds the source span fields; <paramref name="indented"/> selects multi-line output.</summary>
    public static string Serialize(SqlNode node, bool includePositions = false, bool indented = true)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = indented, MaxDepth = MaxDepth }))
            Write(w, node, includePositions);
        return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }

    /// <summary>Builds a <see cref="JsonNode"/> tree. Prefer <see cref="Serialize"/> for very deep trees.</summary>
    public static JsonNode ToJson(SqlNode node, bool includePositions = false) =>
        JsonNode.Parse(Serialize(node, includePositions, indented: false), documentOptions: new JsonDocumentOptions { MaxDepth = MaxDepth })!;

    private sealed class EndMarker(bool array) { public bool Array { get; } = array; }
    private static readonly EndMarker EndObject = new(false), EndArray = new(true);
    private sealed record Prop(string Name, object? Value);
    private sealed record Val(object? Value);

    private static void Write(Utf8JsonWriter w, SqlNode root, bool pos)
    {
        var stack = new Stack<object>();
        stack.Push(new Val(root));
        while (stack.Count > 0)
        {
            switch (stack.Pop())
            {
                case EndMarker m:
                    if (m.Array) w.WriteEndArray(); else w.WriteEndObject();
                    break;
                case Prop p:
                    if (!Emitted(p.Value)) break;
                    w.WritePropertyName(p.Name);
                    stack.Push(new Val(p.Value));
                    break;
                case Val { Value: var v }:
                    switch (v)
                    {
                        case null: w.WriteNullValue(); break;
                        case SqlNode n: BeginNode(w, stack, n, pos); break;
                        case string s: w.WriteStringValue(s); break;
                        case bool b: w.WriteBooleanValue(b); break;
                        case Enum e: w.WriteStringValue(e.ToString()); break;
                        case int or long or double: w.WriteNumberValue(System.Convert.ToDouble(v)); break;
                        case IEnumerable e:
                        {
                            w.WriteStartArray();
                            stack.Push(EndArray);
                            var items = new List<object?>();
                            foreach (var item in e) items.Add(item);
                            for (int i = items.Count - 1; i >= 0; i--) stack.Push(new Val(items[i]));
                            break;
                        }
                        default: w.WriteStringValue(v.ToString()); break;
                    }
                    break;
            }
        }
    }

    /// <summary>Whether a member value appears in the output (null, false and empty lists are omitted).</summary>
    private static bool Emitted(object? v) => v switch
    {
        null => false,
        bool b => b,
        string => true,
        IEnumerable e => e.GetEnumerator().MoveNext(),
        _ => true,
    };

    private static void BeginNode(Utf8JsonWriter w, Stack<object> stack, SqlNode node, bool pos)
    {
        w.WriteStartObject();
        w.WriteString("type", node.GetType().Name);
        if (pos)
        {
            w.WriteStartObject("span");
            w.WriteNumber("start", node.Start);
            w.WriteNumber("end", node.End);
            w.WriteNumber("line", node.Line);
            w.WriteNumber("column", node.Column);
            w.WriteEndObject();
        }
        stack.Push(EndObject);
        var props = AstReflection.NodeProperties(node.GetType());
        for (int i = props.Length - 1; i >= 0; i--)
            stack.Push(new Prop(Camel(props[i].Name), props[i].GetValue(node)));
    }

    private static string Camel(string s) => char.ToLowerInvariant(s[0]) + s[1..];
}
