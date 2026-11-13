using System.Collections;
using System.Diagnostics;
using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

public class GeneratedCodeTests
{
    internal static string RepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "OracleSqlParser.slnx"))) return d.FullName;
        throw new InvalidOperationException("repository root not found");
    }

    /// <summary>Fails when a node type or property was added/changed without running <c>dotnet run --project tools/AstGen</c>.</summary>
    [Fact]
    public void GeneratedCode_IsUpToDate()
    {
        var generated = AstGen.AstGenerator.Generate(typeof(SqlNode).Assembly);
        var dir = Path.Combine(RepoRoot(), "src", "OracleSqlParser", "Generated");
        foreach (var (name, text) in generated)
        {
            var path = Path.Combine(dir, name);
            Assert.True(File.Exists(path), $"{name} is missing; run: dotnet run --project tools/AstGen");
            Assert.True(File.ReadAllText(path).ReplaceLineEndings("\n") == text,
                $"{name} is stale; run: dotnet run --project tools/AstGen");
        }
        var extra = Directory.GetFiles(dir, "*.g.cs").Select(Path.GetFileName).Except(generated.Keys).ToList();
        Assert.True(extra.Count == 0, "unexpected generated files: " + string.Join(", ", extra));
    }

    internal static string AndChain(int terms) =>
        "SELECT 1 FROM dual WHERE " + string.Join(" AND ", Enumerable.Range(0, terms).Select(i => $"c{i} = {i}"));

    [Fact]
    public void Traversal_Of20000TermAndChain_IsFast()
    {
        var script = Shape.Parse(AndChain(20_000));
        var sw = Stopwatch.StartNew();
        int count = script.Descendants().Count();
        sw.Stop();
        Assert.True(count > 60_000);
        Assert.True(sw.ElapsedMilliseconds < 500, $"traversal took {sw.ElapsedMilliseconds} ms");
    }

    internal static readonly string[] Samples =
    [
        "SELECT e.*, d.dname FROM emp e, dept d WHERE e.deptno = d.deptno(+) AND e.sal > :min GROUP BY ROLLUP(a) ORDER BY 1 DESC NULLS LAST",
        "WITH x (a, b) AS (SELECT 1, 2 FROM dual) SELECT CASE WHEN a > 1 THEN 'x' ELSE 'z' END, CAST(b AS NUMBER(10,2)) FROM x UNION ALL SELECT 1, 2 FROM dual",
        "INSERT INTO t (a, b) VALUES (1, 2), (3, 4)",
        "MERGE INTO t USING s ON (t.id = s.id) WHEN MATCHED THEN UPDATE SET t.a = s.a WHEN NOT MATCHED THEN INSERT (id) VALUES (s.id)",
        "SELECT SUM(x) OVER (PARTITION BY d ORDER BY s ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) FROM t JOIN u ON t.id = u.id",
        "CREATE TABLE t (id NUMBER(10) PRIMARY KEY, name VARCHAR2(20 CHAR) DEFAULT 'x' NOT NULL)",
        "BEGIN FOR i IN 1..10 LOOP IF i > 5 THEN NULL; ELSE dbms_output.put_line(i); END IF; END LOOP; EXCEPTION WHEN OTHERS THEN RAISE; END;",
        "CREATE OR REPLACE PACKAGE BODY p AS FUNCTION f(a IN NUMBER) RETURN NUMBER IS BEGIN RETURN a + 1; END; END p;",
        "UPDATE t SET (a, b) = (SELECT 1, 2 FROM dual) WHERE c IN (1, 2, 3) RETURNING a INTO :x",
        "SELECT JSON_OBJECT('a' VALUE 1), LISTAGG(n, ',') WITHIN GROUP (ORDER BY n) FROM t",
    ];

    private static IEnumerable<SqlNode> ReflectedChildren(SqlNode n)
    {
        foreach (var p in AstReflection.NodeProperties(n.GetType()))
            foreach (var c in Flatten(p.GetValue(n))) yield return c;
    }

    private static IEnumerable<SqlNode> Flatten(object? v)
    {
        if (v is SqlNode n) yield return n;
        else if (v is IEnumerable e and not string)
            foreach (var item in e)
                foreach (var c in Flatten(item)) yield return c;
    }

    [Fact]
    public void GetChildren_MatchesReflectionOrderAndContent()
    {
        foreach (var sql in Samples)
        {
            var r = SqlParser.Parse(sql);
            Assert.True(r.Success, sql + ": " + string.Join("; ", r.Errors));
            foreach (var n in r.Script.Descendants())
                Assert.Equal(ReflectedChildren(n).ToList(), n.GetChildren().ToList());
        }
    }
}
