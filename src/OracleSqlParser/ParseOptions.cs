namespace OracleSqlParser;

/// <summary>
/// The Oracle Database release a script targets. Constructs introduced after the target release are still
/// parsed into the tree, but reported as a ParseError ("... requires Oracle 23 or later (targeting Oracle 19)").
/// The values are the major release numbers, mirroring ScriptDom's versioned parsers.
/// </summary>
public enum OracleVersion
{
    Oracle19 = 19,
    Oracle21 = 21,
    Oracle23 = 23,
}

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

    /// <summary>
    /// The Oracle release the input must run on. The default, Oracle23, accepts every construct the parser knows.
    /// Older targets keep the tree and add one ParseError per construct introduced after the target.
    /// </summary>
    public OracleVersion OracleVersion { get; init; } = OracleVersion.Oracle23;
}
