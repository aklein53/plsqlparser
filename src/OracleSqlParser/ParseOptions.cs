namespace OracleSqlParser;

/// <summary>Options for <see cref="SqlParser"/>.</summary>
public sealed class ParseOptions
{
    /// <summary>
    /// Maximum nesting depth of expressions, subqueries, PL/SQL blocks and token-tree groups. Exceeding it
    /// is reported as a ParseError ("nesting depth exceeds N") instead of overflowing the stack.
    /// </summary>
    public int MaxNestingDepth { get; init; } = 200;
}
