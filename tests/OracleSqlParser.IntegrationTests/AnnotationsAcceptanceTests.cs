using Oracle.ManagedDataAccess.Client;
using Xunit;

namespace OracleSqlParser.IntegrationTests;

/// <summary>
/// ANNOTATIONS placements on tables, columns, views, materialized views and indexes (Oracle 23ai). Each scenario must
/// parse with typed nodes, run as written and as generated, and leave the same rows in USER_ANNOTATIONS_USAGE, which
/// must contain the expected annotations. Rejected placements must fail on Oracle and in our parser.
/// </summary>
[Collection(OracleCollection.Name)]
public class AnnotationsAcceptanceTests(OracleFixture db)
{
    private const string Pw = "scratch_pw_1";

    private static readonly string[] Base =
    [
        "CREATE TABLE ann_t1 (id NUMBER, name VARCHAR2(20))",
    ];

    public static IEnumerable<object[]> Accepted() =>
    [
        new object[] { "table and column annotations on CREATE TABLE",
            new[] {
                "CREATE TABLE ann_c (id NUMBER ANNOTATIONS (label 'Col'), name VARCHAR2(20) NOT NULL ANNOTATIONS (ui 'n')) ANNOTATIONS (label 'Tbl', hidden)",
                "CREATE TABLE ann_c2 (id NUMBER GENERATED ALWAYS AS IDENTITY ANNOTATIONS (label 'g'), k NUMBER PRIMARY KEY ANNOTATIONS (label 'k')) TABLESPACE users ANNOTATIONS (ADD IF NOT EXISTS label 'x')",
            },
            new[] { "ANN_C|ID|LABEL=Col", "ANN_C|NAME|UI=n", "ANN_C||LABEL=Tbl", "ANN_C||HIDDEN=", "ANN_C2|ID|LABEL=g", "ANN_C2|K|LABEL=k", "ANN_C2||LABEL=x" } },
        new object[] { "ALTER TABLE annotations, ADD column, MODIFY column",
            new[] {
                "CREATE TABLE ann_a (id NUMBER, name VARCHAR2(20))",
                "ALTER TABLE ann_a ANNOTATIONS (label 'Tbl altered', ADD ui 'v')",
                "ALTER TABLE ann_a ADD (extra NUMBER ANNOTATIONS (label 'e'))",
                "ALTER TABLE ann_a MODIFY (name VARCHAR2(30) ANNOTATIONS (label 'm'))",
                "ALTER TABLE ann_a MODIFY id ANNOTATIONS (ADD label 'idc')",
            },
            new[] { "ANN_A||LABEL=Tbl altered", "ANN_A||UI=v", "ANN_A|EXTRA|LABEL=e", "ANN_A|NAME|LABEL=m", "ANN_A|ID|LABEL=idc" } },
        new object[] { "view and view column annotations, ALTER VIEW",
            new[] {
                "CREATE VIEW ann_v1 ANNOTATIONS (label 'View') AS SELECT id, name FROM ann_t1",
                "CREATE VIEW ann_v2 (id ANNOTATIONS (label 'vid'), name) AS SELECT id, name FROM ann_t1",
                "ALTER VIEW ann_v1 ANNOTATIONS (label 'View altered')",
                "ALTER VIEW ann_v2 MODIFY (id ANNOTATIONS (label 'vid2'))",
            },
            new[] { "ANN_V1||LABEL=View altered", "ANN_V2|ID|LABEL=vid2" } },
        new object[] { "materialized view annotations, ALTER MATERIALIZED VIEW",
            new[] {
                "CREATE MATERIALIZED VIEW ann_m1 ANNOTATIONS (label 'MV') AS SELECT id, name FROM ann_t1",
                "CREATE MATERIALIZED VIEW ann_m2 (id ANNOTATIONS (label 'mid'), name) AS SELECT id, name FROM ann_t1",
                "ALTER MATERIALIZED VIEW ann_m1 ANNOTATIONS (ADD ui 'x', DROP IF EXISTS label)",
                "ALTER MATERIALIZED VIEW ann_m2 MODIFY id ANNOTATIONS (label 'mid3')",
            },
            new[] { "ANN_M1||UI=x", "ANN_M2|ID|LABEL=mid3" } },
        new object[] { "index annotations, ALTER INDEX",
            new[] {
                "CREATE INDEX ann_i1 ON ann_t1 (id) ANNOTATIONS (label 'Idx')",
                "CREATE INDEX ann_i2 ON ann_t1 (name) ANNOTATIONS (label 'Idx2') TABLESPACE users",
                "ALTER INDEX ann_i1 ANNOTATIONS (label 'Idx altered')",
                "ALTER INDEX ann_i2 ANNOTATIONS (ADD ui 'y')",
            },
            new[] { "ANN_I1||LABEL=Idx altered", "ANN_I2||LABEL=Idx2", "ANN_I2||UI=y" } },
    ];

    /// <summary>Forms that Oracle rejects; our parser must reject them too.</summary>
    public static IEnumerable<object[]> Rejected() =>
    [
        ["CREATE TABLE ann_r (id NUMBER ANNOTATIONS (label 'a') NOT NULL)"],
        ["CREATE TABLE ann_r (id NUMBER, name VARCHAR2(20)) ANNOTATIONS (DROP label)"],
        ["CREATE TABLE ann_r AS SELECT 1 AS id FROM dual ANNOTATIONS (label 'a')"],
        ["CREATE TABLE ann_r (id NUMBER, name VARCHAR2(20)) ANNOTATIONS (label 'a', label 'a')"],
        ["ALTER TABLE ann_t1 ADD ANNOTATIONS (label 'nope')"],
        ["CREATE VIEW ann_rv AS SELECT id FROM ann_t1 ANNOTATIONS (label 'x')"],
        ["CREATE VIEW ann_rv ANNOTATIONS (DROP label) AS SELECT id FROM ann_t1"],
        ["CREATE INDEX ann_ri ON ann_t1 (id) ANNOTATIONS (DROP label)"],
        ["ALTER VIEW ann_v1 MODIFY id ANNOTATIONS (label 'x')"],
    ];

    [SkippableTheory]
    [MemberData(nameof(Accepted))]
    public async Task Accepted_ParsesTyped_RunsAsWrittenAndAsGenerated(string name, string[] statements, string[] expected)
    {
        db.SkipIfUnavailable();
        foreach (var sql in statements)
        {
            var parsed = SqlParser.Parse(sql);
            Assert.True(parsed.Success, $"[{name}] parser rejected: {string.Join("; ", parsed.Errors)}\n{sql}");
        }
        var asWritten = await RunScenario(name, statements, generated: false);
        var asGenerated = await RunScenario(name, statements, generated: true);
        Assert.Equal(asWritten, asGenerated);
        foreach (var e in expected)
            Assert.Contains(e, asWritten);
    }

    [SkippableTheory]
    [MemberData(nameof(Rejected))]
    public async Task Rejected_FailsInOurParserAndOnOracle(string sql)
    {
        db.SkipIfUnavailable();
        Assert.False(SqlParser.Parse(sql).Success, $"parser accepted a form Oracle rejects:\n{sql}");
        var user = NewUser();
        await using (var sys = await db.OpenSystemAsync()) await CreateUser(sys, user);
        try
        {
            await using var c = await db.OpenAsync(user, Pw);
            foreach (var s in Base) await OracleFixture.ExecAsync(c, s);
            await OracleFixture.ExecAsync(c, "CREATE VIEW ann_v1 AS SELECT id, name FROM ann_t1");
            await OracleFixture.ExecAsync(c, "CREATE INDEX ann_i0 ON ann_t1 (id)");
            var ex = await Assert.ThrowsAsync<OracleException>(() => OracleFixture.ExecAsync(c, sql.TrimEnd(';')));
            Assert.NotNull(ex);
        }
        finally { await Drop(user); }
    }

    private static string NewUser() => "ANN" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private static async Task CreateUser(OracleConnection sys, string user)
    {
        await OracleFixture.ExecAsync(sys, $"CREATE USER {user} IDENTIFIED BY {Pw} QUOTA UNLIMITED ON USERS");
        await OracleFixture.ExecAsync(sys, $"GRANT CREATE SESSION, CREATE TABLE, CREATE VIEW, CREATE SEQUENCE, CREATE MATERIALIZED VIEW TO {user}");
    }

    private async Task Drop(string user)
    {
        await using var sys = await db.OpenSystemAsync();
        try { await OracleFixture.ExecAsync(sys, $"DROP USER {user} CASCADE"); } catch (OracleException) { }
    }

    /// <summary>Runs the statements in a fresh user and returns the USER_ANNOTATIONS_USAGE rows as "NAME|COLUMN|ANNOTATION=VALUE".</summary>
    private async Task<List<string>> RunScenario(string name, string[] statements, bool generated)
    {
        var user = NewUser();
        await using (var sys = await db.OpenSystemAsync()) await CreateUser(sys, user);
        try
        {
            await using var c = await db.OpenAsync(user, Pw);
            foreach (var s in Base) await OracleFixture.ExecAsync(c, s);
            var generator = new SqlScriptGenerator(new SqlScriptGeneratorOptions());
            foreach (var sql in statements)
            {
                var text = (generated ? generator.Generate(SqlParser.Parse(sql).Script) : sql).TrimEnd(';');
                try { await OracleFixture.ExecAsync(c, text); }
                catch (OracleException e) { throw new Xunit.Sdk.XunitException($"[{name}] {(generated ? "generated" : "original")} failed: {e.Message}\n{text}"); }
            }
            var rs = await OracleFixture.QueryAsync(c,
                "SELECT object_name, column_name, annotation_name, annotation_value FROM user_annotations_usage ORDER BY 1, 2, 3, 4");
            return rs.Rows.Select(r => $"{r[0]}|{(r[1] == "<null>" ? "" : r[1])}|{r[2]}={(r[3] == "<null>" ? "" : r[3])}").ToList();
        }
        finally { await Drop(user); }
    }
}
