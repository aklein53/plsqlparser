using System.Collections;
using System.Reflection;
using System.Text;

namespace OracleSqlParser;

/// <summary>
/// Compact canonical text form of an AST, used for shape assertions.
/// Identifiers/columns/literals/operators print inline; other nodes print as (Type prop=value ...)
/// with null, empty and false members omitted.
/// </summary>
public static class AstPrinter
{
    public static string Print(object? node)
    {
        var sb = new StringBuilder();
        Write(sb, node);
        return sb.ToString();
    }

    private static void Write(StringBuilder sb, object? v)
    {
        switch (v)
        {
            case null: return;
            case Identifier id: sb.Append(id.Quoted ? "\"" + id.Value + "\"" : id.Value); return;
            case ColumnReference cr:
                sb.Append(string.Join(".", cr.Parts.Select(p => p.Quoted ? "\"" + p.Value + "\"" : p.Value)));
                if (cr.Attribute != null) sb.Append('%').Append(cr.Attribute);
                if (cr.OuterJoin) sb.Append("(+)");
                return;
            case Literal l:
                sb.Append(l.Kind switch
                {
                    LiteralKind.String => "'" + l.Value + "'",
                    LiteralKind.Null => "NULL",
                    LiteralKind.Date => "DATE'" + l.Value + "'",
                    LiteralKind.Timestamp => "TIMESTAMP'" + l.Value + "'",
                    LiteralKind.Boolean => l.Value,
                    LiteralKind.Default => "DEFAULT",
                    LiteralKind.Any => "ANY",
                    _ => l.Value,
                });
                return;
            case BindVariable b: sb.Append(':').Append(b.Name); return;
            case StarExpression s: sb.Append(s.Qualifier.Count == 0 ? "*" : string.Join(".", s.Qualifier.Select(q => q.Value)) + ".*"); return;
            case BinaryExpression be:
            {
                // Iterate down the left spine: left-deep chains (a AND b AND c ...) must not recurse once per link.
                var spine = new List<BinaryExpression>();
                Expression cur = be;
                while (cur is BinaryExpression b) { spine.Add(b); cur = b.Left; }
                foreach (var b in spine) sb.Append('(').Append(b.Operator).Append(' ');
                Write(sb, cur);
                for (int i = spine.Count - 1; i >= 0; i--) { sb.Append(' '); Write(sb, spine[i].Right); sb.Append(')'); }
                return;
            }
            case SetOperation or JoinTable:
            {
                // Left is the first printed property of both: iterate down the left spine.
                var spine = new List<SqlNode>();
                SqlNode? cur = (SqlNode)v;
                while (cur is SetOperation or JoinTable) { spine.Add(cur); cur = cur is SetOperation so ? so.Left : ((JoinTable)cur).Left; }
                foreach (var n in spine) sb.Append('(').Append(n.GetType().Name).Append(" left=");
                Write(sb, cur);
                for (int i = spine.Count - 1; i >= 0; i--) WriteProps(sb, spine[i], 1);
                return;
            }
            case UnaryExpression ue:
                sb.Append('(').Append(ue.Operator).Append(' '); Write(sb, ue.Operand); sb.Append(')');
                return;
            case ParenthesizedExpression pe:
                sb.Append("(paren "); Write(sb, pe.Inner); sb.Append(')');
                return;
            case string str: sb.Append(str); return;
            case bool: return;
            case Enum e: sb.Append(e); return;
            case SqlNode n: WriteNode(sb, n); return;
            case IEnumerable list:
                sb.Append('[');
                bool first = true;
                foreach (var item in list)
                {
                    if (!first) sb.Append(' ');
                    first = false;
                    Write(sb, item);
                }
                sb.Append(']');
                return;
            default: sb.Append(v); return;
        }
    }

    private static void WriteNode(StringBuilder sb, SqlNode n)
    {
        sb.Append('(').Append(n.GetType().Name);
        WriteProps(sb, n, 0);
    }

    /// <summary>Writes the members from index <paramref name="skip"/> on, then the closing parenthesis.</summary>
    private static void WriteProps(StringBuilder sb, SqlNode n, int skip)
    {
        var props = SqlNode.NodeProperties(n.GetType());
        for (int pi = skip; pi < props.Length; pi++)
        {
            var p = props[pi];
            if (p.IsDefined(typeof(SpellingOnlyAttribute), false)) continue;
            var v = p.GetValue(n);
            if (v == null) continue;
            if (v is bool bv)
            {
                if (p.PropertyType == typeof(bool?)) sb.Append(' ').Append(Camel(p.Name)).Append('=').Append(bv ? "true" : "false");
                else if (bv) sb.Append(' ').Append(Camel(p.Name));
                continue;
            }
            if (v is Enum ev && ev.GetType().IsDefined(typeof(FlagsAttribute), false) && Convert.ToInt64(ev) == 0) continue;
            if (v is string s0 && s0.Length == 0) continue;
            if (v is IEnumerable e && v is not string)
            {
                if (!e.GetEnumerator().MoveNext()) continue;
            }
            sb.Append(' ').Append(Camel(p.Name)).Append('=');
            Write(sb, v);
        }
        sb.Append(')');
    }

    private static string Camel(string s) => char.ToLowerInvariant(s[0]) + s[1..];
}
