using System.Reflection;
using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

public class SqlScriptGeneratorTests
{
    // ---------- corpus ----------

    /// <summary>Every string passed to an [InlineData] of the smoke corpus and the *ShapeTests files that parses without errors.</summary>
    private static IEnumerable<string> AllInputs()
    {
        var seen = new HashSet<string>();
        foreach (var type in typeof(SmokeCorpusTests).Assembly.GetTypes())
        {
            if (!(type.Name == nameof(SmokeCorpusTests) || type.Name.EndsWith("ShapeTests"))) continue;
            foreach (var m in type.GetMethods())
                foreach (var a in m.GetCustomAttributes<InlineDataAttribute>())
                    foreach (var row in a.GetData(null!))
                        if (row.Length > 0 && row[0] is string sql && seen.Add(sql) && SqlParser.Parse(sql) is { Success: true })
                            yield return sql;
        }
    }

    public static IEnumerable<object[]> Corpus() => AllInputs().Select(s => new object[] { s });

    private static IEnumerable<string> SmokeInputs()
    {
        foreach (var a in typeof(SmokeCorpusTests).GetMethod("Parses")!.GetCustomAttributes<InlineDataAttribute>())
            foreach (var row in a.GetData(null!))
                yield return (string)row[0];
    }

    public static IEnumerable<object[]> SmokeCorpus() => SmokeInputs().Select(s => new object[] { s });

    private static readonly SqlScriptGeneratorOptions Formatted = new()
    {
        NewLineBeforeFromClause = true, NewLineBeforeWhereClause = true, NewLineBeforeHierarchicalClause = true,
        NewLineBeforeGroupByClause = true, NewLineBeforeHavingClause = true, NewLineBeforeOrderByClause = true,
        NewLineBeforeJoinClause = true, NewLineBeforeSetOperator = true, NewLineBeforeSetClause = true,
        NewLineBeforeValuesClause = true, NewLineBeforeReturningClause = true, MultilineSelectElementsList = true,
        MultilineColumnDefinitions = true, MultilinePlsql = true, AlignClauseBodies = true, SqlPlusSlashAfterPlsql = true,
        IndentSize = 2,
    };

    private static readonly SqlScriptGeneratorOptions LowerCase = new()
    {
        KeywordCasing = KeywordCasing.Lowercase, IdentifierCasing = IdentifierCasing.Lowercase,
    };

    private static void AssertRoundTrip(string sql, SqlScriptGeneratorOptions options)
    {
        var first = SqlParser.Parse(sql);
        Assert.True(first.Success, string.Join("; ", first.Errors));
        var printed = new SqlScriptGenerator(options).Generate(first.Script);
        var second = SqlParser.Parse(printed);
        Assert.True(second.Success, $"generated SQL does not parse: {string.Join("; ", second.Errors)}\n--- generated ---\n{printed}");
        Assert.Equal(AstPrinter.Print(first.Script), AstPrinter.Print(second.Script));
    }

    [Fact]
    public void Corpus_IsLarge() => Assert.True(Corpus().Count() > 300, $"only {Corpus().Count()} inputs");

    [Theory]
    [MemberData(nameof(Corpus))]
    public void RoundTrip_Default(string sql) => AssertRoundTrip(sql, new SqlScriptGeneratorOptions());

    [Theory]
    [MemberData(nameof(Corpus))]
    public void RoundTrip_UppercaseIdentifiers(string sql) =>
        AssertRoundTrip(sql, new SqlScriptGeneratorOptions { IdentifierCasing = IdentifierCasing.Uppercase });

    [Theory]
    [MemberData(nameof(Corpus))]
    public void RoundTrip_Formatted(string sql) => AssertRoundTrip(sql, Formatted);

    [Theory]
    [MemberData(nameof(SmokeCorpus))]
    public void RoundTrip_LowerCase(string sql) => AssertRoundTrip(sql, LowerCase);
}
