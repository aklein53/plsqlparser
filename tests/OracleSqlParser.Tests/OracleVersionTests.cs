using System.Reflection;
using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

/// <summary>
/// ParseOptions.OracleVersion: constructs introduced after the target release stay in the tree and add one
/// ParseError each. Every gated row below contains exactly one such construct, so the 19c assertion is exact.
/// </summary>
public class OracleVersionTests
{
    /// <summary>Each row: one construct, the feature name the diagnostic must mention, and the first release that accepts it.</summary>
    public static IEnumerable<object[]> GatedConstructs => new object[][]
    {
        // ---- Oracle 21c ----
        ["CREATE TABLE t (j JSON)", "JSON data type", OracleVersion.Oracle21],
        ["CREATE BLOCKCHAIN TABLE t (a NUMBER)", "BLOCKCHAIN table", OracleVersion.Oracle21],
        ["CREATE IMMUTABLE TABLE t (a NUMBER)", "IMMUTABLE table", OracleVersion.Oracle21],
        ["CREATE FUNCTION f (x NUMBER) RETURN NUMBER SQL_MACRO AS BEGIN RETURN 'x'; END;", "SQL_MACRO", OracleVersion.Oracle21],
        ["BEGIN FOR i IN 1..10 BY 2 LOOP NULL; END LOOP; END;", "FOR loop BY", OracleVersion.Oracle21],
        ["BEGIN FOR i IN 1..3, 5..6 LOOP NULL; END LOOP; END;", "multiple FOR loop iteration controls", OracleVersion.Oracle21],
        ["BEGIN FOR i IN 1..10 WHILE i < 5 LOOP NULL; END LOOP; END;", "FOR loop WHILE/WHEN", OracleVersion.Oracle21],
        ["BEGIN FOR i IN REPEAT 'x' LOOP NULL; END LOOP; END;", "FOR loop REPEAT", OracleVersion.Oracle21],
        ["DECLARE TYPE t IS TABLE OF NUMBER; v t := t(1); BEGIN FOR x IN VALUES OF v LOOP NULL; END LOOP; END;", "FOR loop VALUES OF", OracleVersion.Oracle21],
        ["DECLARE TYPE t IS TABLE OF NUMBER; v t := t(1); BEGIN FOR i IN INDICES OF v LOOP NULL; END LOOP; END;", "FOR loop INDICES OF", OracleVersion.Oracle21],
        ["DECLARE TYPE t IS TABLE OF NUMBER; v t := t(1); BEGIN FOR i, x IN PAIRS OF v LOOP NULL; END LOOP; END;", "FOR loop PAIRS OF", OracleVersion.Oracle21],
        ["BEGIN FOR i NUMBER IN 1..3 LOOP NULL; END LOOP; END;", "typed FOR iterand", OracleVersion.Oracle21],
        ["BEGIN FOR i MUTABLE IN 1..3 LOOP NULL; END LOOP; END;", "MUTABLE FOR iterand", OracleVersion.Oracle21],
        ["BEGIN FOR r IN (EXECUTE IMMEDIATE 'SELECT 1 FROM dual') LOOP NULL; END LOOP; END;", "FOR loop EXECUTE IMMEDIATE", OracleVersion.Oracle21],

        // ---- Oracle 23ai ----
        ["SELECT 1 FROM dual WHERE (1 = 1) IS TRUE", "IS TRUE/FALSE", OracleVersion.Oracle23],
        ["SELECT 1 FROM dual WHERE (1 = 1) IS NOT FALSE", "IS TRUE/FALSE", OracleVersion.Oracle23],
        ["CREATE TABLE t (b BOOLEAN)", "BOOLEAN data type", OracleVersion.Oracle23],
        ["SELECT 1 FROM dual WHERE TRUE", "TRUE/FALSE literal in SQL", OracleVersion.Oracle23],
        ["SELECT 1", "SELECT without FROM", OracleVersion.Oracle23],
        ["CREATE TABLE IF NOT EXISTS t (a NUMBER)", "IF [NOT] EXISTS", OracleVersion.Oracle23],
        ["DROP TABLE IF EXISTS t", "IF [NOT] EXISTS", OracleVersion.Oracle23],
        ["UPDATE t SET a = 1 RETURNING OLD a INTO :x", "RETURNING OLD/NEW", OracleVersion.Oracle23],
        ["UPDATE t SET a = s.a FROM s WHERE t.id = s.id", "UPDATE ... FROM", OracleVersion.Oracle23],
        ["DELETE FROM t FROM s WHERE t.id = s.id", "DELETE ... FROM", OracleVersion.Oracle23],
        ["SELECT * FROM (VALUES (1, 'a'))", "VALUES table constructor", OracleVersion.Oracle23],
        ["WITH x (a) AS (VALUES (1)) SELECT * FROM x", "VALUES table constructor", OracleVersion.Oracle23],
        ["INSERT INTO t (a) VALUES (1), (2)", "multi-row INSERT VALUES", OracleVersion.Oracle23],
        ["SELECT * FROM GRAPH_TABLE(g MATCH (a) COLUMNS (a.name)) gt", "GRAPH_TABLE", OracleVersion.Oracle23],
        ["CREATE DOMAIN d AS NUMBER(3)", "CREATE DOMAIN", OracleVersion.Oracle23],
        ["CREATE TABLE t (c NUMBER DOMAIN d)", "column DOMAIN", OracleVersion.Oracle23],
        ["CREATE JSON RELATIONAL DUALITY VIEW dv AS SELECT 1 FROM dual", "CREATE JSON RELATIONAL DUALITY VIEW", OracleVersion.Oracle23],
        ["CREATE PROPERTY GRAPH g VERTEX TABLES (t KEY (id) PROPERTIES (id))", "CREATE PROPERTY GRAPH", OracleVersion.Oracle23],
        ["CREATE TABLE t (v VECTOR(3))", "VECTOR data type", OracleVersion.Oracle23],
        ["CREATE MLE MODULE m LANGUAGE JAVASCRIPT AS export function f() {}", "CREATE MLE MODULE", OracleVersion.Oracle23],
        ["CREATE MLE ENV e", "CREATE MLE ENV", OracleVersion.Oracle23],
    };

    [Theory]
    [MemberData(nameof(GatedConstructs))]
    public void Gated_ParsesCleanlyAtDefaultVersion(string sql, string feature, OracleVersion min)
    {
        var r = SqlParser.Parse(sql, new ParseOptions());
        Assert.True(r.Success, $"{feature}: {string.Join("; ", r.Errors)}");
    }

    [Theory]
    [MemberData(nameof(GatedConstructs))]
    public void Gated_ReportsOneDiagnosticBelowRequiredVersion(string sql, string feature, OracleVersion min)
    {
        var r = SqlParser.Parse(sql, new ParseOptions { OracleVersion = OracleVersion.Oracle19 });
        var error = Assert.Single(r.Errors);
        Assert.Contains(feature, error.Message);
        Assert.Contains($"requires Oracle {(int)min} or later", error.Message);
        Assert.Contains("(targeting Oracle 19)", error.Message);
        // The diagnostic is advisory: the statement is still in the tree.
        Assert.Single(r.Script.Statements);
    }

    [Theory]
    [MemberData(nameof(GatedConstructs))]
    public void Gated_TreeIsUnchangedByTargetVersion(string sql, string feature, OracleVersion min)
    {
        var at19 = SqlParser.Parse(sql, new ParseOptions { OracleVersion = OracleVersion.Oracle19 });
        var at23 = SqlParser.Parse(sql, new ParseOptions { OracleVersion = OracleVersion.Oracle23 });
        Assert.Equal(at23.Script.Statements.Count, at19.Script.Statements.Count);
        for (int i = 0; i < at23.Script.Statements.Count; i++)
            Assert.True(SqlNodeComparer.StructurallyEqual(at23.Script.Statements[i], at19.Script.Statements[i]), feature);
    }

    [Theory]
    [MemberData(nameof(GatedConstructs))]
    public void Gated_ClearsAtItsRequiredVersion(string sql, string feature, OracleVersion min)
    {
        var r = SqlParser.Parse(sql, new ParseOptions { OracleVersion = OracleVersion.Oracle21 });
        if (min == OracleVersion.Oracle21)
            Assert.True(r.Success, $"{feature}: {string.Join("; ", r.Errors)}");
        else
            Assert.Single(r.Errors);
    }

    /// <summary>Classic (19c) forms must produce no diagnostics when targeting 19.</summary>
    [Theory]
    [InlineData("SELECT 1 FROM dual")]
    [InlineData("SELECT * FROM emp e, dept d WHERE e.deptno = d.deptno(+) AND e.sal > :min")]
    [InlineData("SELECT JSON_EXISTS(x, '$.a' TRUE ON ERROR) FROM dual")]
    [InlineData("CREATE TABLE t (a NUMBER, b VARCHAR2(10) DEFAULT ON NULL 'x' NOT NULL)")]
    [InlineData("CREATE TABLE t (a TIMESTAMP WITH TIME ZONE, b CLOB)")]
    [InlineData("CREATE TABLE t (a NUMBER)")]
    [InlineData("INSERT INTO t (a) VALUES (1)")]
    [InlineData("UPDATE t SET a = 1 WHERE b = 2 RETURNING a INTO :x")]
    [InlineData("DELETE FROM t WHERE x = 1")]
    [InlineData("DROP TABLE t PURGE")]
    [InlineData("BEGIN FOR i IN 1..10 LOOP NULL; END LOOP; END;")]
    [InlineData("BEGIN FOR i IN REVERSE 1..10 LOOP NULL; END LOOP; END;")]
    [InlineData("BEGIN FOR r IN (SELECT 1 FROM dual) LOOP NULL; END LOOP; END;")]
    [InlineData("DECLARE c SYS_REFCURSOR; BEGIN FOR r IN c LOOP NULL; END LOOP; END;")]
    [InlineData("BEGIN FORALL i IN 1..3 INSERT INTO t VALUES (i); END;")]
    [InlineData("DECLARE TYPE t IS TABLE OF NUMBER INDEX BY PLS_INTEGER; v t; BEGIN FORALL i IN INDICES OF v INSERT INTO t VALUES (v(i)); END;")]
    [InlineData("DECLARE b BOOLEAN := TRUE; BEGIN IF b = TRUE THEN NULL; END IF; END;")]
    [InlineData("CREATE PROCEDURE p (b BOOLEAN) AS BEGIN NULL; END;")]
    [InlineData("EXEC p(TRUE)")]
    [InlineData("EXEC dbms_stats.gather_table_stats(user, 'T', cascade => TRUE)")]
    [InlineData("CREATE PACKAGE p AS g BOOLEAN := TRUE; FUNCTION f RETURN BOOLEAN; END;")]
    [InlineData("CREATE TYPE o AS OBJECT (a NUMBER, MEMBER FUNCTION ok RETURN BOOLEAN)")]
    [InlineData("CREATE TYPE BODY o AS MEMBER FUNCTION ok RETURN BOOLEAN IS BEGIN RETURN TRUE; END; END;")]
    [InlineData("DECLARE TYPE r IS RECORD (b BOOLEAN); TYPE t IS TABLE OF BOOLEAN INDEX BY PLS_INTEGER; SUBTYPE s IS BOOLEAN; BEGIN NULL; END;")]
    [InlineData("CREATE PROCEDURE p (b BOOLEAN DEFAULT TRUE) AS BEGIN NULL; END;")]
    [InlineData("CREATE TRIGGER trg BEFORE INSERT ON t FOR EACH ROW BEGIN :new.f := TRUE; END;")]
    [InlineData("SELECT TREAT(c AS JSON) FROM t")]
    [InlineData("CREATE FUNCTION f RETURN BOOLEAN AS BEGIN RETURN FALSE; END;")]
    [InlineData("CREATE TABLE t (a NUMBER) PARTITION BY RANGE (a) (PARTITION p1 VALUES LESS THAN (10))")]
    public void ClassicForms_ProduceNoDiagnosticsAtOracle19(string sql)
    {
        var r = SqlParser.Parse(sql, new ParseOptions { OracleVersion = OracleVersion.Oracle19 });
        Assert.True(r.Success, string.Join("; ", r.Errors));
    }

    /// <summary>The convenience APIs throw on a version diagnostic, as they do for any other error.</summary>
    [Fact]
    public void ParseStatement_ThrowsOnVersionDiagnostic()
    {
        var ex = Assert.Throws<ParseException>(() => SqlParser.ParseStatement("SELECT 1", new ParseOptions { OracleVersion = OracleVersion.Oracle19 }));
        Assert.Contains("SELECT without FROM", ex.Error.Message);
        Assert.Throws<ParseException>(() => SqlParser.ParseExpression("(1 = 1) IS TRUE", new ParseOptions { OracleVersion = OracleVersion.Oracle19 }));
    }

    /// <summary>The default target is Oracle 23, so every SmokeCorpus statement parses with no diagnostics.</summary>
    [Fact]
    public void SmokeCorpus_ParsesWithoutErrorsAtOracle23()
    {
        var method = typeof(SmokeCorpusTests).GetMethod(nameof(SmokeCorpusTests.Parses))!;
        var sqls = method.GetCustomAttributes<Xunit.InlineDataAttribute>()
            .SelectMany(a => a.GetData(method))
            .Select(row => (string)row[0])
            .ToList();
        Assert.NotEmpty(sqls);
        foreach (var sql in sqls)
        {
            var r = SqlParser.Parse(sql, new ParseOptions { OracleVersion = OracleVersion.Oracle23 });
            Assert.True(r.Success, $"{sql}: {string.Join("; ", r.Errors)}");
        }
    }
}
