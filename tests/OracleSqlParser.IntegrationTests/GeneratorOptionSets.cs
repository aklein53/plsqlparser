namespace OracleSqlParser.IntegrationTests;

/// <summary>The SqlScriptGenerator option sets that are verified against Oracle.</summary>
public static class GeneratorOptionSets
{
    public static SqlScriptGeneratorOptions Defaults() => new();

    public static SqlScriptGeneratorOptions Formatted() => new()
    {
        NewLineBeforeFromClause = true,
        NewLineBeforeWhereClause = true,
        NewLineBeforeHierarchicalClause = true,
        NewLineBeforeGroupByClause = true,
        NewLineBeforeHavingClause = true,
        NewLineBeforeOrderByClause = true,
        NewLineBeforeJoinClause = true,
        NewLineBeforeSetOperator = true,
        NewLineBeforeSetClause = true,
        NewLineBeforeValuesClause = true,
        NewLineBeforeReturningClause = true,
        MultilineSelectElementsList = true,
        MultilinePlsql = true,
        IndentSize = 2,
    };

    public static SqlScriptGeneratorOptions Lowercase() => new()
    {
        KeywordCasing = KeywordCasing.Lowercase,
        IdentifierCasing = IdentifierCasing.Lowercase,
    };

    public static readonly (string Name, Func<SqlScriptGeneratorOptions> Make)[] All =
    [
        ("defaults", Defaults),
        ("formatted", Formatted),
        ("lowercase", Lowercase),
    ];

    public static Func<SqlScriptGeneratorOptions> Get(string name) => All.Single(a => a.Name == name).Make;
}
