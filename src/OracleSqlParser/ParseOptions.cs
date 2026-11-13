namespace OracleSqlParser;

/// <summary>Options for <see cref="SqlParser"/>.</summary>
public sealed class ParseOptions
{
    /// <summary>
    /// Maximum nesting depth of expressions, subqueries, PL/SQL blocks and token-tree groups. Exceeding it
    /// is reported as a ParseError ("nesting depth exceeds N") instead of overflowing the stack.
    /// </summary>
    public int MaxNestingDepth { get; init; } = 200;

    /// <summary>
    /// When true (the default), <c>&amp;name</c> and <c>&amp;&amp;name</c> are substitution variables. When false, '&amp;' is
    /// an ordinary operator, as after SQL*Plus <c>SET DEFINE OFF</c>. Scripts can also switch it within the input.
    /// </summary>
    public bool SubstitutionEnabled { get; init; } = true;
}
