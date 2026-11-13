namespace OracleSqlParser;

/// <summary>
/// Edits a tree in place through typed callbacks. For every concrete node type <c>T</c> (generated into
/// <c>Generated/SqlNodeRewriter.g.cs</c>) there is a <c>public virtual SqlNode? Rewrite(T node)</c>. The default
/// rewrites each child (by dispatching to the child's own <c>Rewrite</c> overload) and assigns the result back into the
/// property or list slot, then returns the node. An override may return the same node (edited),
/// a replacement node, or <c>null</c> to remove the node: from a list, or by nulling an optional property.
/// Returning <c>null</c> for a required (non-nullable) property, or a node of a type the property cannot hold,
/// throws <see cref="InvalidOperationException"/> naming the property.
/// To rewrite the children of a replacement as well, call <c>base.Rewrite(...)</c> or rewrite it yourself.
/// Use <see cref="RewriteTree{TNode}(TNode)"/> to start at a root.
/// Rewriting is recursive, like <see cref="SqlNodeVisitor"/>, and mutates the tree it is given: clone first
/// (<see cref="SqlNode.DeepClone{TNode}"/>) to keep the original.
/// </summary>
public abstract partial class SqlNodeRewriter
{
    /// <summary>Called for nodes whose typed overload is not overridden (and for node types unknown to the generated code). Rewrites the children and returns <paramref name="node"/>.</summary>
    public virtual SqlNode? Rewrite(SqlNode node)
    {
        node.RewriteChildren(this);
        return node;
    }

    /// <summary>Rewrites the tree under <paramref name="root"/> (and <paramref name="root"/> itself) and returns the new root. Throws <see cref="InvalidOperationException"/> if the root is removed or replaced by a node of another type.</summary>
    public TNode RewriteTree<TNode>(TNode root) where TNode : SqlNode
    {
        ArgumentNullException.ThrowIfNull(root);
        var r = root.AcceptRewriter(this);
        if (r is null) throw new InvalidOperationException($"The rewriter removed the root {typeof(TNode).Name}.");
        if (r is not TNode t) throw new InvalidOperationException($"The rewriter replaced the root {typeof(TNode).Name} with a {r.GetType().Name}.");
        return t;
    }

    private SqlNode? Dispatch(SqlNode child) => child.AcceptRewriter(this);

    private static T Cast<T>(SqlNode result, string property) where T : SqlNode =>
        result as T ?? throw new InvalidOperationException(
            $"{property}: the rewriter returned a {result.GetType().Name}, which is not a {typeof(T).Name}.");

    // ---- helpers called by generated code ----

    internal static T RewriteRequired<T>(SqlNodeRewriter r, T child, string property) where T : SqlNode
    {
        if (child is null) return child!;
        var res = r.Dispatch(child) ?? throw new InvalidOperationException($"{property}: the rewriter removed a required child.");
        return Cast<T>(res, property);
    }

    internal static T? RewriteOptional<T>(SqlNodeRewriter r, T? child, string property) where T : SqlNode
    {
        if (child is null) return null;
        var res = r.Dispatch(child);
        return res is null ? null : Cast<T>(res, property);
    }

    internal static void RewriteList<T>(SqlNodeRewriter r, List<T>? list, string property) where T : SqlNode
    {
        if (list is null) return;
        int w = 0;
        for (int i = 0; i < list.Count; i++)
        {
            var item = list[i];
            if (item is null) { list[w++] = item!; continue; }
            var res = r.Dispatch(item);
            if (res is null) continue;
            list[w++] = Cast<T>(res, property);
        }
        if (w < list.Count) list.RemoveRange(w, list.Count - w);
    }

    internal static void RewriteNested<T>(SqlNodeRewriter r, List<List<T>>? lists, string property) where T : SqlNode
    {
        if (lists is null) return;
        foreach (var l in lists) RewriteList(r, l, property);
    }
}
