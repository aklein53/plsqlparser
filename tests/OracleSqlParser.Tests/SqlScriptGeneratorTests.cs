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

    /// <summary>Inputs that exercise node types the shape tests only reach through [Fact]s (verified by Coverage_ReportUnseenNodeTypes).</summary>
    private static readonly string[] Extras =
    [
        "SET SERVEROUTPUT ON SIZE 1000000\nSELECT 1 FROM dual;\nEXEC dbms_output.put_line('x');\n@scripts/a.sql\nPROMPT hello world\nPRINT :b\nDEFINE x = 5",
        "SELECT &x, &&y FROM t WHERE a = &1",
        "SELECT a COLLATE binary_ci FROM t",
        "DECLARE CURSOR c (p NUMBER) RETURN emp%ROWTYPE IS SELECT * FROM emp WHERE id = p; e EXCEPTION; PRAGMA EXCEPTION_INIT(e, -20001); " +
            "TYPE r IS RECORD (a NUMBER, b VARCHAR2(10) NOT NULL := 'x'); TYPE rc IS REF CURSOR RETURN emp%ROWTYPE; SUBTYPE small IS NUMBER(3) NOT NULL; " +
            "BEGIN RAISE e; EXCEPTION WHEN e THEN RAISE; END;",
        "SELECT * FROM t MODEL DIMENSION BY (a) MEASURES (b) RULES (b[FOR a IN (1, 2)] = 1)",
        "SELECT * FROM t MODEL REFERENCE r ON (SELECT 1 x, 2 y FROM dual) DIMENSION BY (x) MEASURES (y) DIMENSION BY (a) MEASURES (b) RULES (b[1] = r.y[1])",
        "SELECT * FROM GRAPH_TABLE (g MATCH (a) COLUMNS (a.x))",
        "SELECT * FROM t WHERE x IS OF (ONLY t1, t2)",
        "SELECT JSON_TRANSFORM(j, SET '$.a' = 1, REMOVE '$.b') FROM t",
        "SELECT JSON_OBJECT(KEY 'a' VALUE 1 NULL ON NULL) FROM dual",
        "SELECT JSON_OBJECT('a' VALUE 1 WITH UNIQUE KEYS) FROM dual",
        "SELECT JSON_QUERY(j, '$.a' WITH WRAPPER KEEP QUOTES) FROM t",
        "SELECT XMLSERIALIZE(CONTENT x AS CLOB ENCODING 'UTF-8' VERSION '1.0' INDENT SIZE = 2) FROM t",
        "SELECT TRANSLATE(x USING NCHAR_CS) FROM t",
        "SELECT PREDICTION(m USING a, b) FROM t",
        "SELECT x.getclobval().length() FROM t x",
        "SELECT * FROM t MATCH_RECOGNIZE (ORDER BY b MEASURES FIRST(b) AS fb ONE ROW PER MATCH PATTERN (^ a+ (b | c)? $) SUBSET s = (a, b) DEFINE a AS x > 1)",
        "SELECT * FROM t MATCH_RECOGNIZE (ORDER BY b PATTERN (a {- b -} g{2,3}?) DEFINE a AS x > 1)",
        "SELECT * FROM t MATCH_RECOGNIZE (ORDER BY b PATTERN (PERMUTE(e, f)) DEFINE a AS x > 1)",
        "SELECT * FROM (VALUES (1, 2), (3, 4)) v (a, b)",
        "SELECT q'[it's]', N'x', nq'{a}', :\"Bind\", 1.5E3, .5 FROM dual",
        "SELECT /*+ INDEX(e emp_ix) FULL(t) */ a FROM t e",
        "GRANT \"MyRole\" TO bob",
        "CREATE OR REPLACE PACKAGE BODY p IS PROCEDURE q IS BEGIN NULL; END q; BEGIN NULL; EXCEPTION WHEN OTHERS THEN NULL; END p;",
        "CREATE OR REPLACE TRIGGER trg FOR INSERT OR UPDATE OF a, b ON t COMPOUND TRIGGER x NUMBER; BEFORE STATEMENT IS BEGIN NULL; END BEFORE STATEMENT; AFTER EACH ROW IS BEGIN x := 1; END AFTER EACH ROW; END trg;",
        "CREATE TYPE t AS OBJECT (a NUMBER, MEMBER FUNCTION f RETURN NUMBER, CONSTRUCTOR FUNCTION t (SELF IN OUT NOCOPY t, x NUMBER) RETURN SELF AS RESULT) NOT FINAL",
        "CREATE TYPE BODY t IS MEMBER FUNCTION f RETURN NUMBER IS BEGIN RETURN a; END; END;",
        "CREATE FUNCTION f (x NUMBER DEFAULT 1) RETURN NUMBER DETERMINISTIC PARALLEL_ENABLE RESULT_CACHE IS BEGIN RETURN x; END f;",
        "CREATE FUNCTION f (x NUMBER) RETURN NUMBER AS LANGUAGE JAVA NAME 'Foo.bar(int) return int'",
    ];

    public static IEnumerable<object[]> Corpus() => AllInputs().Concat(Extras).Select(s => new object[] { s });

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
    public void Extras_AllParse()
    {
        foreach (var sql in Extras)
        {
            var r = SqlParser.Parse(sql);
            Assert.True(r.Success, sql + "\n" + string.Join("; ", r.Errors));
        }
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

    [Fact]
    public void Coverage_ReportUnseenNodeTypes()
    {
        var seen = new HashSet<Type>();
        foreach (var sql in AllInputs().Concat(Extras))
            foreach (var n in SqlParser.Parse(sql).Script.Descendants()) seen.Add(n.GetType());
        var all = typeof(SqlNode).Assembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract && typeof(SqlNode).IsAssignableFrom(t));
        var unseen = all.Where(t => !seen.Contains(t)).Select(t => t.Name).OrderBy(x => x).ToList();
        Assert.True(unseen.Count == 0, "unseen: " + string.Join(", ", unseen));
    }
}
