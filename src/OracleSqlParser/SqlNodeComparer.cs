namespace OracleSqlParser;

/// <summary>Structural comparison of trees.</summary>
public static class SqlNodeComparer
{
    /// <summary>
    /// True when the two trees have the same shape and meaning: same node types, same scalar values and same children
    /// (including list lengths and which properties are null). Source positions (Start/End/Line/Column) and spelling-only
    /// properties (the ones <see cref="AstPrinter"/> hides: for example the case an unquoted identifier was typed in,
    /// or the delimiter of a <c>q'[...]'</c> literal) are ignored. Iterative (explicit stack), so left-deep chains are safe.
    /// </summary>
    public static bool StructurallyEqual(SqlNode a, SqlNode b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        var pending = new Stack<(SqlNode, SqlNode)>();
        pending.Push((a, b));
        while (pending.Count > 0)
        {
            var (x, y) = pending.Pop();
            if (ReferenceEquals(x, y)) continue;
            if (x.GetType() != y.GetType()) return false;
            if (!x.ShapeEquals(y, pending)) return false;
        }
        return true;
    }
}
