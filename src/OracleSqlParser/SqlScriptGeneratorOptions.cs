namespace OracleSqlParser;

/// <summary>How keywords are cased in generated SQL.</summary>
public enum KeywordCasing { Uppercase, Lowercase }

/// <summary>How identifiers are cased in generated SQL. Quoted identifiers are always printed verbatim.</summary>
public enum IdentifierCasing
{
    /// <summary>Unquoted identifiers are printed as typed in the source (falls back to upper case for synthesized nodes).</summary>
    Preserve,
    /// <summary>Unquoted identifiers are printed upper case (Oracle's normal form).</summary>
    Uppercase,
    /// <summary>Unquoted identifiers are printed lower case.</summary>
    Lowercase,
}

/// <summary>
/// Formatting options for <see cref="SqlScriptGenerator"/>, modelled on ScriptDom's SqlScriptGeneratorOptions.
/// The defaults produce compact single-line output (statements of a script go on separate lines).
/// </summary>
public sealed class SqlScriptGeneratorOptions
{
    /// <summary>Case of generated keywords. Default Uppercase.</summary>
    public KeywordCasing KeywordCasing { get; set; } = KeywordCasing.Uppercase;
    /// <summary>Case of unquoted identifiers. Default Preserve.</summary>
    public IdentifierCasing IdentifierCasing { get; set; } = IdentifierCasing.Preserve;
    /// <summary>Spaces per indentation level. Default 4.</summary>
    public int IndentSize { get; set; } = 4;

    /// <summary>Start FROM on a new line.</summary>
    public bool NewLineBeforeFromClause { get; set; }
    /// <summary>Start WHERE on a new line (SELECT, UPDATE, DELETE).</summary>
    public bool NewLineBeforeWhereClause { get; set; }
    /// <summary>Start START WITH / CONNECT BY on a new line.</summary>
    public bool NewLineBeforeHierarchicalClause { get; set; }
    /// <summary>Start GROUP BY on a new line.</summary>
    public bool NewLineBeforeGroupByClause { get; set; }
    /// <summary>Start HAVING on a new line.</summary>
    public bool NewLineBeforeHavingClause { get; set; }
    /// <summary>Start ORDER BY (and OFFSET / FETCH / FOR UPDATE) on a new line.</summary>
    public bool NewLineBeforeOrderByClause { get; set; }
    /// <summary>Start every JOIN of a FROM clause on a new line.</summary>
    public bool NewLineBeforeJoinClause { get; set; }
    /// <summary>Put UNION / INTERSECT / MINUS on their own line.</summary>
    public bool NewLineBeforeSetOperator { get; set; }
    /// <summary>Start UPDATE ... SET and MERGE's WHEN clauses on a new line.</summary>
    public bool NewLineBeforeSetClause { get; set; }
    /// <summary>Start INSERT's VALUES / SELECT on a new line.</summary>
    public bool NewLineBeforeValuesClause { get; set; }
    /// <summary>Start RETURNING ... INTO and LOG ERRORS on a new line.</summary>
    public bool NewLineBeforeReturningClause { get; set; }

    /// <summary>One select-list element per line, indented below SELECT.</summary>
    public bool MultilineSelectElementsList { get; set; }
    /// <summary>One column / constraint definition per line in CREATE TABLE.</summary>
    public bool MultilineColumnDefinitions { get; set; }
    /// <summary>PL/SQL: declarations, statements and exception handlers each on their own indented line.</summary>
    public bool MultilinePlsql { get; set; }
    /// <summary>
    /// Put the body of a SELECT / FROM / WHERE / GROUP BY / HAVING / ORDER BY clause on its own indented line below the keyword
    /// (instead of after it).
    /// </summary>
    public bool AlignClauseBodies { get; set; }

    /// <summary>Script generation: end each statement with ';'. Default true.</summary>
    public bool IncludeSemicolons { get; set; } = true;
    /// <summary>Script generation: follow PL/SQL units (blocks, CREATE PROCEDURE/FUNCTION/PACKAGE/TRIGGER/TYPE, Java sources) with a '/' line, as SQL*Plus requires. Default false.</summary>
    public bool SqlPlusSlashAfterPlsql { get; set; }

    internal SqlScriptGeneratorOptions Clone() => (SqlScriptGeneratorOptions)MemberwiseClone();
}
