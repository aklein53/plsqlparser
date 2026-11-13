namespace OracleSqlParser;

/// <summary>
/// Compatibility facade over <see cref="SqlScriptGenerator"/>: compact single-line output with upper-case keywords and
/// upper-case unquoted identifiers (the normalized form), for any node the generator supports.
/// New code should use <see cref="SqlScriptGenerator"/>, which also offers formatting options and identifier-case preservation.
/// </summary>
public static class SqlWriter
{
    private static readonly SqlScriptGenerator Generator = new(new SqlScriptGeneratorOptions { IdentifierCasing = IdentifierCasing.Uppercase });

    public static string ToSql(SqlNode node) => Generator.Generate(node);
}
