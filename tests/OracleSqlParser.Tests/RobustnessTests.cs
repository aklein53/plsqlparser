using System.Diagnostics;
using System.Reflection;
using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

/// <summary>Regression tests for ROADMAP Epic 0: hangs, stack exhaustion, undocumented exceptions, recovery, fidelity.</summary>
public class RobustnessTests
{
    // ---------- helpers ----------

    private static T RunOnSmallStack<T>(Func<T> work, int stackBytes = 256 * 1024)
    {
        T? result = default;
        Exception? error = null;
        var t = new Thread(() => { try { result = work(); } catch (Exception e) { error = e; } }, stackBytes);
        t.Start();
        Assert.True(t.Join(TimeSpan.FromSeconds(60)), "work did not finish in time");
        if (error != null) throw new Xunit.Sdk.XunitException("work threw: " + error);
        return result!;
    }

    /// <summary>Runs work on a background thread; returns (finished, result, exception). A hung thread is abandoned.</summary>
    private static (bool Done, T? Result, Exception? Error) RunWithTimeout<T>(Func<T> work, TimeSpan timeout)
    {
        T? result = default;
        Exception? error = null;
        var t = new Thread(() => { try { result = work(); } catch (Exception e) { error = e; } }) { IsBackground = true };
        t.Start();
        bool done = t.Join(timeout);
        return (done, result, error);
    }

    private static ParseResult ParseWithTimeout(string sql, int seconds = 10)
    {
        var (done, result, error) = RunWithTimeout(() => SqlParser.Parse(sql), TimeSpan.FromSeconds(seconds));
        Assert.True(done, "Parse did not finish (hang): " + sql);
        Assert.Null(error);
        return result!;
    }

    private static string Repeat(string s, int n) => string.Concat(Enumerable.Repeat(s, n));

    // ---------- 1. hangs ----------

    [Theory]
    [InlineData("SELECT LAG(x, 1) IGNORE")]
    [InlineData("SELECT LAG(x, 1) IGNORE NUL")]
    [InlineData("SELECT LAG(x, 1) RESPECT")]
    [InlineData("SELECT LAG(x, 1) IGNORE OVER w FROM t")]
    [InlineData("SELECT LAG(x, 1 IGNORE) FROM t")]
    public void Ignore_Respect_WithoutNulls_IsAnErrorNotAHang(string sql)
    {
        var r = ParseWithTimeout(sql);
        Assert.False(r.Success);
    }

    [Fact]
    public void Ignore_Nulls_StillParses()
    {
        Assert.Contains("ignoreNulls", Shape.Of("SELECT LAG(x, 1) IGNORE NULLS OVER w FROM t"));
        Assert.Contains("respectNulls", Shape.Of("SELECT LAG(x, 1) RESPECT NULLS OVER w FROM t"));
    }

    // ---------- 2. nesting depth ----------

    private static string DeepParens(int n) => "SELECT " + Repeat("(", n) + "1" + Repeat(")", n) + " FROM dual";
    private static string DeepSubqueries(int n) => "SELECT " + Repeat("(SELECT ", n) + "1" + Repeat(" FROM dual)", n) + " FROM dual";
    private static string DeepNot(int n) => "SELECT * FROM t WHERE " + Repeat("NOT ", n) + "a";
    private static string DeepMinus(int n) => "SELECT " + Repeat("- ", n) + "1 FROM dual";
    private static string DeepCase(int n) => "SELECT " + Repeat("CASE WHEN ", n) + "1" + Repeat(" THEN 1 END", n) + " FROM dual";

    [Theory]
    [InlineData("parens", 10_000)]
    [InlineData("subqueries", 5_000)]
    [InlineData("not", 20_000)]
    [InlineData("minus", 20_000)]
    [InlineData("case", 5_000)]
    public void DeepNesting_IsACleanParseError_OnSmallStack(string kind, int n)
    {
        string sql = kind switch
        {
            "parens" => DeepParens(n), "subqueries" => DeepSubqueries(n), "not" => DeepNot(n),
            "minus" => DeepMinus(n), _ => DeepCase(n),
        };
        var r = RunOnSmallStack(() => SqlParser.Parse(sql));
        Assert.False(r.Success);
        Assert.Contains(r.Errors, e => e.Message.StartsWith("nesting depth exceeds"));
    }

    [Fact]
    public void DeepPlsqlNesting_IsACleanParseError_OnSmallStack()
    {
        string sql = "BEGIN " + Repeat("BEGIN ", 5_000) + "NULL; " + Repeat("END; ", 5_000) + "END;";
        var r = RunOnSmallStack(() => SqlParser.Parse(sql));
        Assert.False(r.Success);
        Assert.Contains(r.Errors, e => e.Message.StartsWith("nesting depth exceeds"));
    }

    [Fact]
    public void DeepTokenTreeGroups_AreACleanParseError_OnSmallStack()
    {
        string sql = "ALTER SESSION SET x = " + Repeat("(", 5_000) + "1" + Repeat(")", 5_000);
        var r = RunOnSmallStack(() => SqlParser.Parse(sql));
        Assert.False(r.Success);
    }

    [Fact]
    public void MaxNestingDepth_IsConfigurable()
    {
        string sql = DeepParens(50);
        Assert.True(SqlParser.Parse(sql).Success);
        var r = SqlParser.Parse(sql, new ParseOptions { MaxNestingDepth = 10 });
        Assert.False(r.Success);
        Assert.Contains("nesting depth exceeds 10", r.Errors[0].Message);
        Assert.Throws<ParseException>(() => SqlParser.ParseExpression(Repeat("(", 50) + "1" + Repeat(")", 50), new ParseOptions { MaxNestingDepth = 10 }));
        Assert.Throws<ParseException>(() => SqlParser.ParseStatement(sql, new ParseOptions { MaxNestingDepth = 10 }));
    }

    [Fact]
    public void ReasonableNesting_StillParses_AtDefaultDepth()
    {
        Assert.True(SqlParser.Parse(DeepParens(60)).Success);
        Assert.True(SqlParser.Parse(DeepCase(40)).Success);
    }

    private const int Terms = 20_000;
    private static string AndChain(int n) => "SELECT 1 FROM t WHERE " + string.Join(" AND ", Enumerable.Range(0, n).Select(i => "a" + i + " = 1"));
    private static string ConcatChain(int n) => "SELECT " + string.Join(" || ", Enumerable.Range(0, n).Select(i => "'x'")) + " FROM dual";
    private static string PlusChain(int n) => "SELECT " + string.Join(" + ", Enumerable.Range(0, n).Select(i => "1")) + " FROM dual";
    private static string UnionChain(int n) => string.Join(" UNION ALL ", Enumerable.Range(0, n).Select(i => "SELECT " + i + " FROM dual"));
    private static string JoinChain(int n) => "SELECT 1 FROM t0 " + string.Concat(Enumerable.Range(1, n).Select(i => $"JOIN t{i} ON t{i}.id = t0.id "));

    public static IEnumerable<object[]> LeftDeepChains() =>
    [
        ["and", AndChain(Terms)], ["concat", ConcatChain(Terms)], ["plus", PlusChain(Terms)],
        ["union", UnionChain(Terms)], ["joins", JoinChain(2_000)],
    ];

    [Theory]
    [MemberData(nameof(LeftDeepChains))]
    public void LeftDeepChains_ParseAndTraverse_WithoutStackOverflow(string name, string sql)
    {
        _ = name;
        var (ok, nodes, printed, json, generated) = RunOnSmallStack(() =>
        {
            var r = SqlParser.Parse(sql);
            int count = r.Script.Descendants().Count();
            string p = AstPrinter.Print(r.Script);
            string j = AstJson.Serialize(r.Script, indented: false);
            string g = new SqlScriptGenerator().Generate(r.Script);
            // the generated text must parse back to the same tree (also on the small stack)
            bool same = AstPrinter.Print(SqlParser.Parse(g).Script) == p;
            return (r.Success && same, count, p.Length, j.Length, g.Length);
        });
        Assert.True(ok);
        Assert.True(nodes > 1000);
        Assert.True(printed > 1000);
        Assert.True(json > 1000);
        Assert.True(generated > 1000);
    }

    [Fact]
    public void Printer_OutputUnchanged_ForSmallSpines()
    {
        Assert.Equal("(AND (AND (= A 1) (= B 2)) (= C 3))", Shape.Expr("a = 1 AND b = 2 AND c = 3"));
        Assert.Contains("(SetOperation left=(SetOperation left=", Shape.Of("SELECT 1 FROM a UNION SELECT 2 FROM b UNION ALL SELECT 3 FROM c"));
    }

    // ---------- 3. undocumented exceptions ----------

    [Theory]
    [InlineData("SELECT*MODEL DIMENSION BY(a)MEASURES(*")]
    [InlineData("SELECT t FROM t MATCH_RECOGNIZE(PARTITION BY s ORDER BY t MEASURES S.*")]
    [InlineData("SELECT x FROM XMLTABLE(''PASSING*")]
    [InlineData("SELECT INTERVAL''YEAR(3.")]
    [InlineData("SELECT INTERVAL''YEAR(3t")]
    [InlineData("SELECT INTERVAL''YEAR(3999999999")]
    [InlineData("SELECT INTERVAL '1' YEAR(99999999999999999999) FROM dual")]
    [InlineData("SELECT t FROM t MATCH_RECOGNIZE(PATTERN (a{99999999999999}) DEFINE a AS 1=1)")]
    public void Parse_NeverThrows_OnKnownBadInputs(string sql)
    {
        var r = SqlParser.Parse(sql);
        Assert.False(r.Success);
        Assert.DoesNotContain(r.Errors, e => e.Message.StartsWith("internal parser error"));
    }

    // ---------- 4. performance ----------

    [Fact]
    public void Descendants_OnLargeAndChain_IsFast()
    {
        var r = SqlParser.Parse(AndChain(Terms));
        Assert.True(r.Success);
        var sw = Stopwatch.StartNew();
        int n = r.Script.Descendants().Count();
        sw.Stop();
        Assert.True(n > Terms);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"Descendants took {sw.Elapsed}");
    }

    [Fact]
    public void Descendants_IsPreOrder_InDeclarationOrder()
    {
        var script = Shape.Parse("SELECT a, b FROM t WHERE c = 1");
        var names = script.Descendants().OfType<Identifier>().Select(i => i.Value).ToList();
        Assert.Equal(["A", "B", "T", "C"], names);
    }

    // ---------- 5. JSON depth ----------

    [Fact]
    public void AstJson_HandlesDeepTrees()
    {
        var r = SqlParser.Parse(AndChain(500));
        Assert.True(r.Success);
        var json = AstJson.Serialize(r.Script);
        Assert.Contains("\"type\": \"BinaryExpression\"", json);
        Assert.NotNull(AstJson.ToJson(r.Script));
    }

    // ---------- 6. recovery ----------

    [Fact]
    public void Recovery_LoneSlashSeparatesStatementsAfterError()
    {
        var r = SqlParser.Parse("SELECT 1 FROM dual\n/\nSELECT FROM dual\n/\nSELECT 3 FROM dual\n/\nSELECT 4 FROM dual");
        Assert.Equal(3, r.Script.Statements.Count);
        Assert.Single(r.Errors);
    }

    [Fact]
    public void Lexer_UnterminatedString_RecoversAtEndOfLine()
    {
        var r = SqlParser.Parse("SELECT 1 FROM dual;\nSELECT 'abc FROM dual;\nSELECT 3 FROM dual");
        Assert.Single(r.Errors);
        Assert.Contains("Unterminated string", r.Errors[0].Message);
        Assert.Equal(2, r.Errors[0].Line);
        Assert.Equal(8, r.Errors[0].Column);
        Assert.Equal(2, r.Script.Statements.Count);
        Assert.Contains("SELECT 3", SqlWriter.ToSql(r.Script.Statements[1]).ToUpperInvariant());
    }

    [Fact]
    public void Lexer_UnterminatedQuotedIdentifier_Recovers()
    {
        var r = SqlParser.Parse("SELECT 1 FROM dual;\nSELECT \"abc FROM dual;\nSELECT 3 FROM dual");
        Assert.Single(r.Errors);
        Assert.Equal(2, r.Script.Statements.Count);
    }

    [Fact]
    public void Lexer_UnterminatedComment_ReportsErrorAndKeepsEarlierStatements()
    {
        var r = SqlParser.Parse("SELECT 1 FROM dual;\nSELECT 2 FROM dual /* open\nmore");
        Assert.Single(r.Errors);
        Assert.Contains("Unterminated comment", r.Errors[0].Message);
        Assert.Equal(2, r.Script.Statements.Count);
    }

    [Fact]
    public void Lexer_ErrorColumns_AreNeverNegative()
    {
        var r = SqlParser.Parse("SELECT 1 FROM dual;\n/* open\ncomment\nmore");
        var e = Assert.Single(r.Errors);
        Assert.Equal(2, e.Line);
        Assert.Equal(1, e.Column);
        Assert.DoesNotContain("-", e.ToString().Split(')')[0]);

        var r2 = SqlParser.Parse("SELECT 1 /* a\nb\n*/ FROM dual WHERE x = 'oops\n");
        Assert.All(r2.Errors, er => Assert.True(er.Column >= 1 && er.Line >= 1));
    }

    // ---------- 7. fidelity ----------

    [Fact]
    public void MethodCall_ShapeAndChains()
    {
        Assert.Equal("(MethodCall target=(FunctionCall name=[F] arguments=[(Argument value=X)]) name=Y)", Shape.Expr("f(x).y()"));
        Assert.Equal("(MemberAccess target=(MethodCall target=(FunctionCall name=[F] arguments=[(Argument value=X)]) name=Y arguments=[(Argument value=Z)]) member=W)",
            Shape.Expr("f(x).y(z).w"));
        var mc = Shape.Parse("SELECT UTL_HTTP.REQUEST('http://x').foo() AS x FROM orders").Descendants().OfType<MethodCall>().Single();
        Assert.Equal("FOO", mc.Name.Value);
        Assert.Equal("(FunctionCall name=[UTL_HTTP REQUEST] arguments=[(Argument value='http://x')])", AstPrinter.Print(mc.Target));
    }

    [Fact]
    public void MethodCall_RoundTripsThroughWriter()
    {
        var script = Shape.Parse("SELECT UTL_HTTP.REQUEST('http://x').foo(1) AS x FROM orders");
        var sql = SqlWriter.ToSql(script.Statements[0]);
        Assert.Contains(".FOO(1)", sql.ToUpperInvariant());
        Assert.Contains("UTL_HTTP", sql.ToUpperInvariant());
    }

    // ---------- 8. bounded fuzz ----------

    private static IEnumerable<string> CorpusInputs() =>
        typeof(SmokeCorpusTests).GetMethods()
            .SelectMany(m => m.CustomAttributes)
            .Where(a => a.AttributeType.Name == "InlineDataAttribute")
            .Select(a => ((IEnumerable<CustomAttributeTypedArgument>)a.ConstructorArguments[0].Value!).FirstOrDefault().Value as string)
            .Where(s => s != null)!;

    private static List<(int Start, int End)> TokenSpans(string sql)
    {
        var lx = new Lexer(sql);
        var spans = new List<(int, int)>();
        while (true)
        {
            var t = lx.Next();
            if (t.Type == TokenType.EOF) return spans;
            spans.Add((t.Start, t.End));
            if (spans.Count > 10_000) return spans;
        }
    }

    internal static string Mutate(string sql, List<(int Start, int End)> toks, Random rng)
    {
        if (toks.Count == 0) return sql;
        string Tok(int i) => sql[toks[i].Start..toks[i].End];
        string Join(IEnumerable<string> parts) => string.Join(" ", parts);
        var all = Enumerable.Range(0, toks.Count).Select(Tok).ToList();
        int k = rng.Next(toks.Count);
        switch (rng.Next(6))
        {
            case 0: all.RemoveAt(k); return Join(all);
            case 1: all.Insert(k, all[k]); return Join(all);
            case 2:
                if (k + 1 < all.Count) (all[k], all[k + 1]) = (all[k + 1], all[k]);
                return Join(all);
            case 3: return Join(all.Take(k));
            case 4: return sql[..rng.Next(sql.Length + 1)]; // character-level truncation (mid-token)
            default:
                // splice in a hostile token
                string[] hostile = ["(", ")", "*", "'", "\"", "/*", "IGNORE", "RESPECT", ";", "/", "CASE", "NOT", "-", ".", "&", ":"];
                all.Insert(k, hostile[rng.Next(hostile.Length)]);
                return Join(all);
        }
    }

    [Fact]
    [Trait("Category", "Fuzz")]
    public void Fuzz_NeverThrowsOrHangs()
    {
        var rng = new Random(20240607);
        var total = Stopwatch.StartNew();
        int runs = 0;
        foreach (var original in CorpusInputs())
        {
            var toks = TokenSpans(original);
            for (int m = 0; m < 30; m++)
            {
                string input = m == 0 ? original : Mutate(original, toks, rng);
                runs++;
                var (done, _, error) = RunWithTimeout(() =>
                {
                    var r = SqlParser.Parse(input);
                    Assert.DoesNotContain(r.Errors, e => e.Message.StartsWith("internal parser error"));
                    AstPrinter.Print(r.Script);
                    AstJson.Serialize(r.Script, includePositions: true);
                    return r.Script.Descendants().Count();
                }, TimeSpan.FromSeconds(2));
                Assert.True(done, "timeout (hang?) on input: " + input);
                Assert.True(error == null, $"exception on input: {input}\n{error}");
            }
        }
        Assert.True(runs > 1000);
        Assert.True(total.Elapsed < TimeSpan.FromSeconds(60), $"fuzz took {total.Elapsed}");
    }
}
