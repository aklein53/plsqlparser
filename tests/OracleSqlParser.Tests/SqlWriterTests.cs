using System.Reflection;
using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

public class SqlWriterTests
{
    private static IEnumerable<string> SmokeQueries()
    {
        var attrs = typeof(SmokeCorpusTests).GetMethod("Parses")!.GetCustomAttributes<InlineDataAttribute>();
        foreach (var a in attrs)
            foreach (var sql in a.GetData(null!).Select(d => (string)d[0]))
                yield return sql;
    }

    public static IEnumerable<object[]> QueryCorpus() =>
        SmokeQueries().Where(s => SqlParser.Parse(s) is { Success: true } r && r.Script.Statements.Count == 1 && r.Script.Statements[0] is SelectStatement { WithDeclarations.Count: 0 })
            .Select(s => new object[] { s });

    /// <summary>parse -> print -> parse must give an identical tree (the printed SQL means the same thing).</summary>
    [Theory]
    [MemberData(nameof(QueryCorpus))]
    public void RoundTrip_PreservesTree(string sql)
    {
        var first = Shape.Parse(sql).Statements[0];
        var printed = SqlWriter.ToSql(first);
        var reparsed = SqlParser.Parse(printed);
        Assert.True(reparsed.Success, $"printed SQL does not parse: {printed}\n{string.Join("; ", reparsed.Errors)}");
        Assert.Equal(AstPrinter.Print(first), AstPrinter.Print(reparsed.Script.Statements[0]));
    }

    [Fact]
    public void Corpus_HasQueries() => Assert.True(QueryCorpus().Count() > 40, $"only {QueryCorpus().Count()} queries");

    [Theory]
    [InlineData("SELECT a,b x FROM t WHERE a>1", "SELECT A, B AS X FROM T WHERE A > 1")]
    [InlineData("select e.name from emp e left join dept d on e.d=d.id", "SELECT E.NAME FROM EMP E LEFT OUTER JOIN DEPT D ON E.D = D.ID")]
    [InlineData("SELECT * FROM t WHERE x = 'it''s' AND y IN (1,2)", "SELECT * FROM T WHERE X = 'it''s' AND Y IN (1, 2)")]
    [InlineData("SELECT \"MixedCase\" FROM t", "SELECT \"MixedCase\" FROM T")]
    [InlineData("SELECT * FROM t WHERE a IS NOT NULL ORDER BY a DESC NULLS LAST", "SELECT * FROM T WHERE A IS NOT NULL ORDER BY A DESC NULLS LAST")]
    [InlineData("SELECT N'abc' FROM dual", "SELECT N'abc' FROM DUAL")]
    [InlineData("SELECT /*+ FULL(t) */ a FROM t", "SELECT /*+ FULL(t) */ A FROM T")]
    [InlineData("SELECT a - -1 FROM t", "SELECT A - -1 FROM T")]
    public void ExactOutput(string sql, string expected) => Assert.Equal(expected, SqlWriter.ToSql(Shape.Parse(sql).Statements[0]));

    [Fact]
    public void Precedence_ParenthesesAddedForEditedTrees()
    {
        // user predicate "a = 1 OR b = 2" ANDed with another predicate must keep the OR grouped
        var user = SqlParser.ParseExpression("a = 1 OR b = 2");
        var access = SqlParser.ParseExpression("tenant_id = :t");
        var combined = new BinaryExpression { Left = user, Operator = "AND", Right = access };
        Assert.Equal("(A = 1 OR B = 2) AND TENANT_ID = :t", SqlWriter.ToSql(combined));

        var neg = new UnaryExpression { Operator = "NOT", Operand = user };
        Assert.Equal("NOT (A = 1 OR B = 2)", SqlWriter.ToSql(neg));
        var sub = new BinaryExpression { Left = SqlParser.ParseExpression("a"), Operator = "-", Right = SqlParser.ParseExpression("b - c") };
        Assert.Equal("A - (B - C)", SqlWriter.ToSql(sub));
    }

    [Fact]
    public void WithFunction_IsRejectedExplicitly() =>
        Assert.Throws<NotSupportedException>(() => SqlWriter.ToSql(Shape.Parse("WITH FUNCTION f RETURN NUMBER IS BEGIN RETURN 1; END; SELECT f FROM dual").Statements[0]));

    [Fact]
    public void NonQueryStatements_AreRejectedExplicitly() =>
        Assert.Throws<NotSupportedException>(() => SqlWriter.ToSql(Shape.Parse("DELETE FROM t").Statements[0]));
}
