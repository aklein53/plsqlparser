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
        "CREATE MLE ENV e",
        "CREATE MLE MODULE m LANGUAGE JAVASCRIPT AS 'export const x = 1;'",
        "SELECT * FROM t WHERE x = :a.b AND y = :\"q\" AND z = :1",
        "INSERT ALL WHEN a > 1 THEN INTO t1 (x) VALUES (a) INTO t2 ELSE INTO t3 SELECT a FROM s",
        "UPDATE t SET (a, b) = (SELECT 1, 2 FROM dual), c = DEFAULT WHERE CURRENT OF cur RETURNING a INTO :x",
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

    // ---------- exact output ----------

    private static string Gen(string sql, SqlScriptGeneratorOptions? options = null, bool script = false)
    {
        var parsed = Shape.Parse(sql);
        var g = new SqlScriptGenerator(options ?? new SqlScriptGeneratorOptions());
        return g.Generate(script ? parsed : parsed.Statements[0]);
    }

    [Fact]
    public void Defaults_AreCompactUppercaseKeywordsPreservedIdentifiers() =>
        Assert.Equal("SELECT a, Emp.b AS x FROM Emp WHERE a IS NOT NULL", Gen("select a, Emp.b x from Emp where a is not null"));

    [Fact]
    public void KeywordCasing_Lowercase() =>
        Assert.Equal("select A, b from Emp where x is not null order by A desc nulls last",
            Gen("SELECT A, b FROM Emp WHERE x IS NOT NULL ORDER BY A DESC NULLS LAST", new() { KeywordCasing = KeywordCasing.Lowercase }));

    [Theory]
    [InlineData(IdentifierCasing.Preserve, "SELECT MyCol, \"Quoted\" FROM mytable t WHERE t.OtherCol = 1")]
    [InlineData(IdentifierCasing.Uppercase, "SELECT MYCOL, \"Quoted\" FROM MYTABLE T WHERE T.OTHERCOL = 1")]
    [InlineData(IdentifierCasing.Lowercase, "SELECT mycol, \"Quoted\" FROM mytable t WHERE t.othercol = 1")]
    public void IdentifierCasing_Modes(IdentifierCasing casing, string expected) =>
        Assert.Equal(expected, Gen("SELECT MyCol, \"Quoted\" FROM mytable t WHERE t.OtherCol = 1", new() { IdentifierCasing = casing }));

    [Fact]
    public void FormattedSelect_ExactText()
    {
        var options = new SqlScriptGeneratorOptions
        {
            NewLineBeforeFromClause = true, NewLineBeforeWhereClause = true, NewLineBeforeGroupByClause = true, NewLineBeforeHavingClause = true,
            NewLineBeforeOrderByClause = true, NewLineBeforeJoinClause = true, MultilineSelectElementsList = true,
        };
        const string sql = "select e.name, d.name dname from emp e left join dept d on e.d = d.id join loc l on l.id = d.l " +
                           "where e.sal > 100 and d.x in (select x from y where z = 1) group by e.name, d.name having count(*) > 1 order by e.name desc";
        Assert.Equal(
            "SELECT\n" +
            "    e.name,\n" +
            "    d.name AS dname\n" +
            "FROM emp e\n" +
            "LEFT OUTER JOIN dept d ON e.d = d.id\n" +
            "JOIN loc l ON l.id = d.l\n" +
            "WHERE e.sal > 100 AND d.x IN (\n" +
            "    SELECT\n" +
            "        x\n" +
            "    FROM y\n" +
            "    WHERE z = 1\n" +
            ")\n" +
            "GROUP BY e.name, d.name\n" +
            "HAVING count(*) > 1\n" +
            "ORDER BY e.name DESC",
            Gen(sql, options));
    }

    [Fact]
    public void ReadmeExample_ExactText()
    {
        var script = SqlParser.Parse("select e.name, d.name dname from emp e left join dept d on e.d = d.id where e.sal > 100 order by e.name").Script;
        var generator = new SqlScriptGenerator(new SqlScriptGeneratorOptions
        {
            NewLineBeforeFromClause = true, NewLineBeforeJoinClause = true, NewLineBeforeWhereClause = true,
            NewLineBeforeOrderByClause = true, MultilineSelectElementsList = true,
        });
        Assert.Equal("SELECT\n    e.name,\n    d.name AS dname\nFROM emp e\nLEFT OUTER JOIN dept d ON e.d = d.id\nWHERE e.sal > 100\nORDER BY e.name;", generator.Generate(script));
    }

    [Fact]
    public void FormattedSelect_AlignClauseBodies_ExactText()
    {
        var options = new SqlScriptGeneratorOptions
        {
            NewLineBeforeFromClause = true, NewLineBeforeWhereClause = true, AlignClauseBodies = true, IndentSize = 2,
        };
        Assert.Equal("SELECT\n  a, b\nFROM\n  t\nWHERE\n  a = 1", Gen("select a, b from t where a = 1", options));
    }

    [Fact]
    public void FormattedPlsqlBlock_ExactText()
    {
        const string sql = "begin if x > 1 then y := 1; else y := 2; end if; for i in 1..3 loop null; end loop; exception when others then raise; end;";
        Assert.Equal(
            "BEGIN\n" +
            "    IF x > 1 THEN\n" +
            "        y := 1;\n" +
            "    ELSE\n" +
            "        y := 2;\n" +
            "    END IF;\n" +
            "    FOR i IN 1 .. 3 LOOP\n" +
            "        NULL;\n" +
            "    END LOOP;\n" +
            "EXCEPTION\n" +
            "    WHEN OTHERS THEN\n" +
            "        RAISE;\n" +
            "END",
            Gen(sql, new() { MultilinePlsql = true }));
    }

    [Fact]
    public void FormattedProcedure_ExactText()
    {
        const string sql = "create or replace procedure p (a in number, b out varchar2) is x number := 1; begin b := 'x'; end p;";
        Assert.Equal(
            "CREATE OR REPLACE PROCEDURE p (a IN NUMBER, b OUT VARCHAR2) IS\n" +
            "  x NUMBER := 1;\n" +
            "BEGIN\n" +
            "  b := 'x';\n" +
            "END p",
            Gen(sql, new() { MultilinePlsql = true, IndentSize = 2 }));
    }

    [Fact]
    public void Script_Terminators()
    {
        const string sql = "select 1 from dual;\nbegin null; end;\n/\ncreate procedure p is begin null; end;\n/\nset serveroutput on\nexec foo;";
        Assert.Equal(
            "SELECT 1 FROM dual;\nBEGIN NULL; END;\nCREATE PROCEDURE p IS BEGIN NULL; END;\nSET serveroutput on\nEXEC foo;",
            Gen(sql, script: true));
        Assert.Equal(
            "SELECT 1 FROM dual;\nBEGIN NULL; END;\n/\nCREATE PROCEDURE p IS BEGIN NULL; END;\n/\nSET serveroutput on\nEXEC foo;",
            Gen(sql, new() { SqlPlusSlashAfterPlsql = true }, script: true));
        Assert.Equal("SELECT 1 FROM dual", Gen("select 1 from dual;", new() { IncludeSemicolons = false }, script: true));
    }

    [Fact]
    public void Script_JavaSource_AlwaysGetsSlash()
    {
        var script = Gen("CREATE JAVA SOURCE NAMED \"Hello\" AS public class Hello { }\n/\nSELECT 1 FROM dual;", script: true);
        Assert.Equal("CREATE JAVA SOURCE NAMED \"Hello\" AS public class Hello { }\n/\nSELECT 1 FROM dual;", script);
    }

    [Fact]
    public void Generate_ToTextWriter_MatchesString()
    {
        var node = Shape.Parse("select a from t").Statements[0];
        var g = new SqlScriptGenerator();
        var sw = new StringWriter();
        g.Generate(node, sw);
        Assert.Equal(g.Generate(node), sw.ToString());
    }

    [Fact]
    public void ProgrammaticTrees_GetParentheses()
    {
        var user = SqlParser.ParseExpression("a = 1 OR b = 2");
        var combined = new BinaryExpression { Left = user, Operator = "AND", Right = SqlParser.ParseExpression("c = 3") };
        Assert.Equal("(a = 1 OR b = 2) AND c = 3", new SqlScriptGenerator().Generate(combined));
    }

    [Fact]
    public void UnknownNode_ThrowsNotSupported_NamingTheType()
    {
        var ex = Assert.Throws<NotSupportedException>(() => new SqlScriptGenerator().Generate(new CustomStatement()));
        Assert.Contains(nameof(CustomStatement), ex.Message);
    }

    private sealed class CustomStatement : Statement { }

    // ---------- preservation ----------

    [Theory]
    [InlineData("SELECT q'[it's]' FROM dual", "SELECT q'[it's]' FROM dual")]
    [InlineData("SELECT Q'{a}' FROM dual", "SELECT q'{a}' FROM dual")]
    [InlineData("SELECT N'x' FROM dual", "SELECT N'x' FROM dual")]
    [InlineData("SELECT nq'!abc!' FROM dual", "SELECT nq'!abc!' FROM dual")]
    [InlineData("SELECT 'it''s' FROM dual", "SELECT 'it''s' FROM dual")]
    [InlineData("SELECT :\"Bind\", :plain FROM dual", "SELECT :\"Bind\", :plain FROM dual")]
    [InlineData("SELECT 1.50E+3, .5, 1e3 FROM dual", "SELECT 1.50E+3, .5, 1e3 FROM dual")]
    [InlineData("SELECT /*+ Index(t  ix_1)   parallel(4) */ a FROM t", "SELECT /*+ Index(t  ix_1)   parallel(4) */ a FROM t")]
    public void SourceSpelling_IsPreserved(string sql, string expected) => Assert.Equal(expected, Gen(sql));

    [Fact]
    public void QLiteral_WithClosingDelimiterInside_FallsBackToPlainQuotes()
    {
        var lit = (Literal)SqlParser.ParseExpression("q'[abc]'");
        lit.Value = "a]'b";
        Assert.Equal("'a]''b'", new SqlScriptGenerator().Generate(lit));
    }

    [Fact]
    public void MixedCaseIdentifiers_PreservedOrUppercased()
    {
        const string sql = "SELECT MixedCol FROM Schema1.MixedTable";
        Assert.Equal("SELECT MixedCol FROM Schema1.MixedTable", Gen(sql));
        Assert.Equal("SELECT MIXEDCOL FROM SCHEMA1.MIXEDTABLE", Gen(sql, new() { IdentifierCasing = IdentifierCasing.Uppercase }));
        // the parsed Value stays normalised
        Assert.Equal("MIXEDCOL", ((ColumnReference)SqlParser.ParseExpression("MixedCol")).Parts[0].Value);
        Assert.Equal("MixedCol", ((ColumnReference)SqlParser.ParseExpression("MixedCol")).Parts[0].Text);
    }

    [Fact]
    public void TokenTreeWords_FollowIdentifierCasing()
    {
        const string sql = "alter index Ix rebuild";
        Assert.Equal("ALTER INDEX Ix rebuild", Gen(sql));
        Assert.Equal("ALTER INDEX IX REBUILD", Gen(sql, new() { IdentifierCasing = IdentifierCasing.Uppercase }));
    }

    [Fact]
    public void AlterAuditPolicy_DropModeIsNotFlipped()
    {
        // ACTIONS inherits DROP from the list before it; the printer must not turn it into ADD.
        Assert.Equal("ALTER AUDIT POLICY p DROP PRIVILEGES CREATE SESSION ACTIONS DELETE ON hr.emp",
            Gen("ALTER AUDIT POLICY p DROP PRIVILEGES CREATE SESSION ACTIONS DELETE ON hr.emp"));
        Assert.Equal("ALTER AUDIT POLICY p DROP PRIVILEGES CREATE SESSION ADD ACTIONS DELETE ON hr.emp",
            Gen("ALTER AUDIT POLICY p DROP PRIVILEGES CREATE SESSION ADD ACTIONS DELETE ON hr.emp"));
    }

    [Fact]
    public void Tablespace_OmfDatafileAndForceLogging_RoundTrip()
    {
        Assert.Equal("ALTER TABLESPACE ts ADD DATAFILE SIZE 10M", Gen("ALTER TABLESPACE ts ADD DATAFILE SIZE 10M"));
        Assert.Equal("CREATE TABLESPACE ts DATAFILE 'f' SIZE 1M FORCE LOGGING", Gen("CREATE TABLESPACE ts DATAFILE 'f' SIZE 1M FORCE LOGGING"));
        Assert.Equal("ALTER TABLESPACE ts RESIZE 80G", Gen("ALTER TABLESPACE ts RESIZE 80G"));
    }

    [Fact]
    public void CreateRole_RoundTripsIdentificationAndContainer()
    {
        const string sql = "create role Ops identified by Secret container = all";
        Assert.Equal("CREATE ROLE Ops IDENTIFIED BY Secret CONTAINER = ALL", Gen(sql));
        Assert.Equal("CREATE ROLE OPS IDENTIFIED BY Secret CONTAINER = ALL", Gen(sql, new() { IdentifierCasing = IdentifierCasing.Uppercase }));
    }

    [Fact]
    public void AlterUser_PasswordIsNeverRecased()
    {
        // Passwords are case-sensitive even when unquoted, so the uppercase option must not touch them.
        const string sql = "alter user Bob identified by Pw account lock";
        Assert.Equal("ALTER USER Bob IDENTIFIED BY Pw ACCOUNT LOCK", Gen(sql));
        Assert.Equal("ALTER USER BOB IDENTIFIED BY Pw ACCOUNT LOCK", Gen(sql, new() { IdentifierCasing = IdentifierCasing.Uppercase }));
    }

    [Fact]
    public void Comments_AreNotPreserved_KnownLimitation() =>
        Assert.Equal("SELECT a FROM t", Gen("SELECT /* inline */ a -- trailing\nFROM t /* more */"));

    [Theory]
    [InlineData("ALTER TABLE t DROP CONSTRAINT pk CASCADE", "ALTER TABLE t DROP CONSTRAINT pk CASCADE")]
    [InlineData("ALTER TABLE t DROP PRIMARY KEY CASCADE", "ALTER TABLE t DROP PRIMARY KEY CASCADE")]
    [InlineData("ALTER TABLE t DROP COLUMN c CASCADE CONSTRAINTS", "ALTER TABLE t DROP COLUMN c CASCADE CONSTRAINTS")]
    [InlineData("SELECT * FROM s PIVOT XML (SUM(a) FOR (x, y) IN (ANY, ANY))", "SELECT * FROM s PIVOT XML (SUM(a) FOR (x, y) IN (ANY, ANY))")]
    [InlineData("SELECT * FROM s PIVOT XML (SUM(a) FOR x IN (ANY))", "SELECT * FROM s PIVOT XML (SUM(a) FOR x IN (ANY))")]
    public void OracleVerifiedForms_ArePrintedAsOracleRequires(string sql, string expected) => Assert.Equal(expected, Gen(sql));

    [Fact]
    public void SqlWriterFacade_IsCompactUppercase() =>
        Assert.Equal("SELECT MYCOL FROM T", SqlWriter.ToSql(Shape.Parse("select MyCol from t").Statements[0]));
}
