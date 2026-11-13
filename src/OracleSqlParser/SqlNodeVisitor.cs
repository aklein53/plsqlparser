namespace OracleSqlParser;

/// <summary>
/// Typed visitor over the AST, modelled on ScriptDom's <c>TSqlFragmentVisitor</c>.
/// For every concrete node type <c>T</c> (generated into <c>Generated/SqlNodeVisitor.g.cs</c>) there are two virtual methods:
/// <c>Visit(T)</c>, the callback you normally override (default: calls <see cref="Visit(SqlNode)"/>), and
/// <c>ExplicitVisit(T)</c> (default: <c>Visit(node)</c> and then <see cref="SqlNode.AcceptChildren"/>), which you override
/// only to control descent (for example to skip a subtree, or to visit children before the node).
/// Start with <c>root.Accept(visitor)</c>.
/// </summary>
/// <remarks>
/// Traversal is recursive by design (one nested call per tree level, as in ScriptDom). A very deep left-leaning chain
/// (more than roughly 5,000 terms of <c>a AND b AND c ...</c>) can exhaust the stack; for such trees use the
/// iterative <see cref="SqlNode.Descendants"/> instead.
/// </remarks>
public abstract partial class SqlNodeVisitor
{
    /// <summary>Called for every node whose typed <c>Visit(T)</c> is not overridden. The default does nothing.</summary>
    public virtual void Visit(SqlNode node) { }
}
