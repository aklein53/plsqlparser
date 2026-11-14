using Oracle.ManagedDataAccess.Client;
using Xunit;

namespace OracleSqlParser.IntegrationTests;

/// <summary>
/// Property graph, GRAPH_TABLE and domain forms against Oracle 23ai. Accepted scenarios must parse with typed nodes (no
/// generic CREATE/ALTER fallback), run as written and as generated, and return the same rows. Rejected forms must fail
/// on Oracle and in our parser.
/// </summary>
[Collection(OracleCollection.Name)]
public class GraphAndDomainAcceptanceTests(OracleFixture db)
{
    private static readonly string[] Base =
    [
        "CREATE TABLE person (id NUMBER PRIMARY KEY, name VARCHAR2(20), age NUMBER, city VARCHAR2(20))",
        "CREATE TABLE company (id NUMBER PRIMARY KEY, name VARCHAR2(20))",
        "CREATE TABLE knows (src NUMBER, dst NUMBER, since NUMBER, PRIMARY KEY (src, dst))",
        "INSERT INTO person VALUES (1, 'ann', 30, 'x')",
        "INSERT INTO person VALUES (2, 'bob', 25, 'y')",
        "INSERT INTO person VALUES (3, 'cy', 40, 'x')",
        "INSERT INTO company VALUES (10, 'acme')",
        "INSERT INTO knows VALUES (1, 2, 2001)",
        "INSERT INTO knows VALUES (2, 3, 2005)",
        "INSERT INTO knows VALUES (3, 1, 2010)",
    ];

    private const string GraphA =
        "CREATE PROPERTY GRAPH gA VERTEX TABLES (person KEY (id) LABEL person PROPERTIES (id, name, age, city), " +
        "company KEY (id) LABEL company PROPERTIES (id, name)) " +
        "EDGE TABLES (knows KEY (src, dst) SOURCE KEY (src) REFERENCES person (id) DESTINATION KEY (dst) REFERENCES person (id) " +
        "LABEL knows PROPERTIES (since))";

    public static IEnumerable<object[]> Accepted() =>
    [
        new object[] { "graph: KEY, LABEL, PROPERTIES list, edge SOURCE/DESTINATION KEY ... REFERENCES",
            new[] { GraphA, "SELECT * FROM GRAPH_TABLE(gA MATCH (p IS person)-[k IS knows]->(q IS person) COLUMNS (p.name AS pn, q.name AS qn))" } },
        new object[] { "graph: PROPERTIES ARE ALL COLUMNS EXCEPT, NO PROPERTIES, multiple labels",
            new[] {
                "CREATE PROPERTY GRAPH gB VERTEX TABLES (person KEY (id) LABEL person PROPERTIES ARE ALL COLUMNS EXCEPT (city), company KEY (id) LABEL company NO PROPERTIES) " +
                "EDGE TABLES (knows KEY (src, dst) SOURCE KEY (src) REFERENCES person (id) DESTINATION KEY (dst) REFERENCES person (id) LABEL knows NO PROPERTIES LABEL knows2 PROPERTIES ARE ALL COLUMNS)",
                "SELECT * FROM GRAPH_TABLE(gB MATCH (p IS person)-[k IS knows]->(q) COLUMNS (p.name AS pn, q.id))" } },
        new object[] { "graph: PROPERTIES without LABEL (vertex and edge), property aliases",
            new[] {
                "CREATE PROPERTY GRAPH gC VERTEX TABLES (person PROPERTIES (id AS pid, name)) " +
                "EDGE TABLES (knows KEY (src, dst) SOURCE KEY (src) REFERENCES person (id) DESTINATION KEY (dst) REFERENCES person (id) PROPERTIES (since AS s, src))",
                "SELECT * FROM GRAPH_TABLE(gC MATCH (p)-[k]->(q) COLUMNS (p.pid AS x, q.name AS y))" } },
        new object[] { "graph: path patterns, directions, label alternation, WHERE, quantifiers, COLUMNS",
            new[] {
                GraphA,
                "SELECT * FROM GRAPH_TABLE(gA MATCH (p IS person)<-[k IS knows]-(q IS person) COLUMNS (p.name AS pn, q.name AS qn))",
                "SELECT * FROM GRAPH_TABLE(gA MATCH (p IS person)-[k IS knows]-(q IS person) COLUMNS (p.name AS pn, q.name AS qn))",
                "SELECT * FROM GRAPH_TABLE(gA MATCH (p IS person)<-[k IS knows]->(q IS person) COLUMNS (p.name AS pn))",
                "SELECT * FROM GRAPH_TABLE(gA MATCH (p IS person)->(q IS person) COLUMNS (p.name AS pn, q.name AS qn))",
                "SELECT * FROM GRAPH_TABLE(gA MATCH (p IS person)<-(q IS person) COLUMNS (p.name AS pn, q.name AS qn))",
                "SELECT * FROM GRAPH_TABLE(gA MATCH (p IS person)-(q IS person) COLUMNS (p.name AS pn, q.name AS qn))",
                "SELECT * FROM GRAPH_TABLE(gA MATCH (p IS person|company)-[k]->(q) COLUMNS (p.name AS pn))",
                "SELECT * FROM GRAPH_TABLE(gA MATCH (p IS person WHERE p.age > 26)-[k IS knows]->(q) COLUMNS (p.name AS pn))",
                "SELECT * FROM GRAPH_TABLE(gA MATCH (p IS person)-[k IS knows]->(q) WHERE k.since > 2001 COLUMNS (p.name AS pn))",
                "SELECT * FROM GRAPH_TABLE(gA MATCH (p)-[k IS knows WHERE k.since > 2001]->(q) COLUMNS (p.name AS pn))",
                "SELECT * FROM GRAPH_TABLE(gA MATCH (p IS person)-[k IS knows]->{1,2}(q IS person) COLUMNS (p.name AS pn, q.name AS qn))",
                "SELECT * FROM GRAPH_TABLE(gA MATCH (p IS person)-[k IS knows]->{2}(q IS person) COLUMNS (p.name AS pn, q.name AS qn))",
                "SELECT * FROM GRAPH_TABLE(gA MATCH (p IS person)-[k IS knows]->{,2}(q IS person) COLUMNS (p.name AS pn))",
                "SELECT * FROM GRAPH_TABLE(gA MATCH (p IS person)-[k IS knows]-{2}(q IS person) COLUMNS (p.name AS pn))",
                "SELECT * FROM GRAPH_TABLE(gA MATCH (p IS person) ((x IS person)-[k IS knows]->(y IS person)){2} COLUMNS (vertex_id(p) AS vp))",
                "SELECT * FROM GRAPH_TABLE(gA MATCH (p IS person) ((x IS person)-[k IS knows]->(y IS person)){,2} COLUMNS (vertex_id(p) AS vp))",
                "SELECT * FROM GRAPH_TABLE(gA MATCH (p IS person)-[k IS knows]->(q IS person) COLUMNS (edge_id(k) AS e, matchnum() AS m, vertex_id(q) AS v, p.age * 2 AS dbl))",
                "SELECT * FROM GRAPH_TABLE(gA MATCH (p IS person), (c IS company) COLUMNS (p.name AS pn, c.name AS cn))",
                "SELECT * FROM GRAPH_TABLE(gA MATCH (p IS person)-[IS knows]->(q) COLUMNS (p.name))",
                "SELECT * FROM GRAPH_TABLE(gA MATCH (p IS person)-[k IS knows]->(q IS person) ONE ROW PER MATCH COLUMNS (p.name AS pn)) g",
                "CREATE VIEW vg AS SELECT * FROM GRAPH_TABLE(gA MATCH (p IS person)-[k IS knows]->(q IS person) COLUMNS (p.name AS pn, q.name AS qn))",
            } },
        new object[] { "domain: CHECK, DEFAULT, NOT NULL, column using the domain",
            new[] {
                "CREATE DOMAIN dpos AS NUMBER(3) DEFAULT 1 NOT NULL CONSTRAINT dpos_ck CHECK (VALUE > 0)",
                "CREATE TABLE tdom (c NUMBER DOMAIN dpos, d DOMAIN dpos NOT NULL)",
                "INSERT INTO tdom (c, d) VALUES (5, 6)",
                "SELECT c, d FROM tdom" } },
        new object[] { "domain: DISPLAY, ORDER, ANNOTATIONS; ALTER DOMAIN display/order/annotations",
            new[] {
                "CREATE DOMAIN dpos4 AS NUMBER(3) DISPLAY VALUE ORDER VALUE ANNOTATIONS (label 'Positive', hidden)",
                "ALTER DOMAIN dpos4 DROP ORDER",
                "ALTER DOMAIN dpos4 ADD ORDER VALUE",
                "ALTER DOMAIN dpos4 ANNOTATIONS (ADD IF NOT EXISTS ui 'y', DROP IF EXISTS hidden)",
                "ALTER DOMAIN dpos4 ANNOTATIONS (label 'Pos2')",
                "CREATE TABLE tdom4 (c DOMAIN dpos4)" } },
        new object[] { "domain: USECASE, IF NOT EXISTS, ALTER DOMAIN DISPLAY",
            new[] {
                "CREATE USECASE DOMAIN dusecase AS NUMBER(5)",
                "CREATE DOMAIN IF NOT EXISTS dnine AS NUMBER(9)",
                "CREATE DOMAIN dd AS NUMBER(3) DISPLAY VALUE",
                "ALTER DOMAIN dd DROP DISPLAY",
                "ALTER DOMAIN dd ADD DISPLAY VALUE" } },
    ];

    /// <summary>
    /// SOURCE/DESTINATION without KEY parses (ours and Oracle's); Oracle then reports ORA-42413 when it cannot infer the
    /// relationship from foreign keys, so this is a parse-only check.
    /// </summary>
    [Fact]
    public void SourceDestinationWithoutKey_ParsesAsTyped()
    {
        var parsed = SqlParser.Parse("CREATE PROPERTY GRAPH gD VERTEX TABLES (person KEY (id) LABEL person) EDGE TABLES (knows SOURCE person DESTINATION person)");
        Assert.True(parsed.Success);
        var edge = parsed.Script.Descendants().OfType<GraphEdgeTable>().Single();
        Assert.Equal("PERSON", edge.Source.Table.Single().Value);
        Assert.Empty(edge.Source.Keys);
    }

    /// <summary>Forms our parser must reject; each is also rejected by Oracle (checked in the test).</summary>
    public static IEnumerable<object[]> Rejected() =>
    [
        ["CREATE OR REPLACE DOMAIN dr AS NUMBER"],
        ["CREATE DOMAIN dr AS NUMBER CONSTRAINT drc NOT NULL"],
        ["CREATE DOMAIN dr AS (street VARCHAR2(30), zip NUMBER)"],
        ["CREATE DOMAIN dr AS NUMBER ANNOTATIONS (DROP hidden)"],
        ["CREATE PROPERTY GRAPH gR VERTEX TABLES (person KEY (id)) EDGE TABLES (knows SOURCE person (id) DESTINATION person (id))"],
        ["CREATE PROPERTY GRAPH gR VERTEX TABLES (person KEY (id)) EDGE TABLES (knows SOURCE KEY (src) REFERENCES person DESTINATION person)"],
        ["SELECT * FROM GRAPH_TABLE(gA MATCH (p IS person)-[k IS knows]->*(q IS person) COLUMNS (p.name AS pn))"],
        ["SELECT * FROM GRAPH_TABLE(gA MATCH (p IS person)-[k IS knows]->+(q IS person) COLUMNS (p.name AS pn))"],
        ["SELECT * FROM GRAPH_TABLE(gA MATCH (p IS person)-[k IS knows]->{1,}(q IS person) COLUMNS (p.name AS pn))"],
        ["SELECT * FROM GRAPH_TABLE(gA MATCH (p IS person)-[k IS knows]->(q IS person) COLUMNS (p.name pn))"],
        ["SELECT * FROM GRAPH_TABLE(gA MATCH (p IS person)-[k IS knows]->(q IS person) COLUMNS (p.name AS pn)) AS g"],
        ["SELECT * FROM GRAPH_TABLE(gA MATCH (p IS person) ((x IS person)-[k IS knows]->(y IS person)) COLUMNS (vertex_id(p) AS vp))"],
    ];

    [SkippableTheory]
    [MemberData(nameof(Accepted))]
    public async Task Accepted_ParsesTyped_RunsAsWrittenAndAsGenerated(string name, string[] statements)
    {
        db.SkipIfUnavailable();
        foreach (var sql in statements)
        {
            var parsed = SqlParser.Parse(sql);
            Assert.True(parsed.Success, $"[{name}] parser rejected: {string.Join("; ", parsed.Errors)}\n{sql}");
            Assert.DoesNotContain(parsed.Script.Descendants(), n => n is CreateObjectStatement or AlterObjectStatement);
            Assert.DoesNotContain(parsed.Script.Descendants().OfType<AlterDomainStatement>(), a => a.OtherClauses.Count > 0);
        }

        var asWritten = await RunScenario(name, statements, generated: false);
        var asGenerated = await RunScenario(name, statements, generated: true);
        for (int i = 0; i < asWritten.Count; i++)
            Assert.True(asWritten[i] == asGenerated[i], $"[{name}] result #{i} differs for: {statements[i]}\n--- as written:\n{asWritten[i]}\n--- generated:\n{asGenerated[i]}");
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
            await OracleFixture.ExecAsync(c, GraphA);
            var ex = await Assert.ThrowsAsync<OracleException>(() => OracleFixture.ExecAsync(c, sql.TrimEnd(';')));
            Assert.NotNull(ex);
        }
        finally { await Drop(user); }
    }

    private const string Pw = "scratch_pw_1";

    private static string NewUser() => "GDA" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private static async Task CreateUser(OracleConnection sys, string user)
    {
        await OracleFixture.ExecAsync(sys, $"CREATE USER {user} IDENTIFIED BY {Pw} QUOTA UNLIMITED ON USERS");
        await OracleFixture.ExecAsync(sys, $"GRANT CREATE SESSION, CREATE TABLE, CREATE VIEW, CREATE PROPERTY GRAPH, CREATE DOMAIN TO {user}");
    }

    private async Task Drop(string user)
    {
        await using var sys = await db.OpenSystemAsync();
        try { await OracleFixture.ExecAsync(sys, $"DROP USER {user} CASCADE"); } catch (OracleException) { }
    }

    /// <summary>Runs setup and the statements in a fresh user. Queries return their rows (sorted); DDL returns "ok".</summary>
    private async Task<List<string>> RunScenario(string name, string[] statements, bool generated)
    {
        var user = NewUser();
        await using (var sys = await db.OpenSystemAsync()) await CreateUser(sys, user);
        var results = new List<string>();
        try
        {
            await using var c = await db.OpenAsync(user, Pw);
            foreach (var s in Base) await OracleFixture.ExecAsync(c, s);
            var generator = new SqlScriptGenerator(new SqlScriptGeneratorOptions());
            foreach (var sql in statements)
            {
                var text = generated ? generator.Generate(SqlParser.Parse(sql).Script) : sql.TrimEnd(';');
                if (sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var rs = await OracleFixture.QueryAsync(c, text.TrimEnd(';'));
                        results.Add(string.Join("\n", new[] { string.Join(" | ", rs.Columns) }.Concat(rs.Rows.Select(r => string.Join(" | ", r).Replace(user, "OWNER")).OrderBy(x => x, StringComparer.Ordinal))));
                    }
                    catch (OracleException e) { throw new Xunit.Sdk.XunitException($"[{name}] {(generated ? "generated" : "original")} query failed: {e.Message}\n{text}"); }
                }
                else
                {
                    try { await OracleFixture.ExecAsync(c, text.TrimEnd(';')); }
                    catch (OracleException e) { throw new Xunit.Sdk.XunitException($"[{name}] {(generated ? "generated" : "original")} failed: {e.Message}\n{text}"); }
                    results.Add("ok");
                }
            }
        }
        finally { await Drop(user); }
        return results;
    }
}
