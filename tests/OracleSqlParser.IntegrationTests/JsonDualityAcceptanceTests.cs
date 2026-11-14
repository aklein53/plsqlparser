using Oracle.ManagedDataAccess.Client;
using Xunit;

namespace OracleSqlParser.IntegrationTests;

/// <summary>
/// JSON constructors and JSON relational duality views (SQL form) on Oracle 23ai. Each scenario must parse with typed
/// nodes, run as written and as generated, and return the same rows. Queries return JSON as text via JSON_SERIALIZE.
/// </summary>
[Collection(OracleCollection.Name)]
public class JsonDualityAcceptanceTests(OracleFixture db)
{
    private static readonly string[] Base =
    [
        "CREATE TABLE person (id NUMBER PRIMARY KEY, name VARCHAR2(20), age NUMBER)",
        "CREATE TABLE knows (src NUMBER, dst NUMBER, since NUMBER, PRIMARY KEY (src, dst))",
        "INSERT INTO person VALUES (1, 'ann', 30)",
        "INSERT INTO person VALUES (2, 'bob', 25)",
        "INSERT INTO person VALUES (3, 'cy', NULL)",
        "INSERT INTO knows VALUES (1, 2, 2001)",
        "INSERT INTO knows VALUES (2, 3, 2005)",
    ];

    public static IEnumerable<object[]> Accepted() =>
    [
        new object[] { "JSON constructors in plain SQL: object, array, wildcard, null handling, RETURNING, nested subquery",
            new[] {
                "SELECT JSON_SERIALIZE(JSON {'a' : 1, 'b' : 'x'}) AS j FROM dual",
                "SELECT JSON_SERIALIZE(JSON ['a', 1]) AS j FROM dual",
                "SELECT JSON_SERIALIZE(JSON {'id' : p.id, 'name' : p.name ABSENT ON NULL}) AS j FROM person p ORDER BY p.id",
                "SELECT JSON_SERIALIZE(JSON {'id' : p.id, 'age' : p.age NULL ON NULL}) AS j FROM person p ORDER BY p.id",
                "SELECT JSON_SERIALIZE(JSON {'id' : p.id RETURNING VARCHAR2(20)}) AS j FROM person p ORDER BY p.id",
                "SELECT JSON_SERIALIZE(JSON {'id' : p.id, 'friends' : [SELECT JSON {'dst' : k.dst} FROM knows k WHERE k.src = p.id]}) AS j FROM person p ORDER BY p.id",
                "SELECT JSON_SERIALIZE(JSON {*}) AS j FROM person p WHERE p.id = 1",
            } },
        new object[] { "JSON relational duality view with nested array and table annotations",
            new[] {
                "CREATE JSON RELATIONAL DUALITY VIEW dv1 AS SELECT JSON {'_id' : p.id, 'name' : p.name} FROM person p WITH INSERT UPDATE DELETE",
                "CREATE OR REPLACE JSON RELATIONAL DUALITY VIEW dv2 AS SELECT JSON {'_id' : p.id, 'name' : p.name, 'friends' : [SELECT JSON {'_id' : k.dst, 'since' : k.since} FROM knows k WITH INSERT UPDATE DELETE WHERE k.src = p.id]} FROM person p WITH INSERT UPDATE DELETE",
                "SELECT JSON_SERIALIZE(d.data) AS j FROM dv1 d ORDER BY d.data.\"_id\".number()",
                "SELECT JSON_SERIALIZE(d.data) AS j FROM dv2 d ORDER BY d.data.\"_id\".number()",
            } },
        new object[] { "duality view with read-only table annotations and the non-relational spelling",
            new[] {
                "CREATE JSON RELATIONAL DUALITY VIEW dv3 AS SELECT JSON {'_id' : p.id, 'name' : p.name} FROM person p WITH NOINSERT NOUPDATE NODELETE",
                "CREATE JSON DUALITY VIEW dv4 AS SELECT JSON {'_id' : p.id} FROM person p WITH INSERT",
                "SELECT JSON_SERIALIZE(d.data) AS j FROM dv3 d ORDER BY d.data.\"_id\".number()",
            } },
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
            Assert.DoesNotContain(parsed.Script.Descendants(), n => n is CreateObjectStatement);
        }

        var asWritten = await RunScenario(name, statements, generated: false);
        var asGenerated = await RunScenario(name, statements, generated: true);
        for (int i = 0; i < asWritten.Count; i++)
            Assert.True(asWritten[i] == asGenerated[i], $"[{name}] result #{i} differs for: {statements[i]}\n--- as written:\n{asWritten[i]}\n--- generated:\n{asGenerated[i]}");
    }

    private const string Pw = "scratch_pw_1";

    private static string NewUser() => "JDA" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private static async Task CreateUser(OracleConnection sys, string user)
    {
        await OracleFixture.ExecAsync(sys, $"CREATE USER {user} IDENTIFIED BY {Pw} QUOTA UNLIMITED ON USERS");
        await OracleFixture.ExecAsync(sys, $"GRANT CREATE SESSION, CREATE TABLE, CREATE VIEW TO {user}");
    }

    private async Task Drop(string user)
    {
        await using var sys = await db.OpenSystemAsync();
        try { await OracleFixture.ExecAsync(sys, $"DROP USER {user} CASCADE"); } catch (OracleException) { }
    }

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
                var text = (generated ? generator.Generate(SqlParser.Parse(sql).Script) : sql).TrimEnd(';');
                var label = generated ? "generated" : "original";
                if (sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var rs = await OracleFixture.QueryAsync(c, text);
                        var text2 = string.Join("\n", new[] { string.Join(" | ", rs.Columns) }.Concat(rs.Rows.Select(r => string.Join(" | ", r))));
                        // The duality metadata SCN ("asof") differs between runs; everything else must match.
                        results.Add(System.Text.RegularExpressions.Regex.Replace(text2, "\"asof\":\"[0-9A-F]+\"", "\"asof\":\"X\""));
                    }
                    catch (OracleException e) { throw new Xunit.Sdk.XunitException($"[{name}] {label} query failed: {e.Message}\n{text}"); }
                }
                else
                {
                    try { await OracleFixture.ExecAsync(c, text); }
                    catch (OracleException e) { throw new Xunit.Sdk.XunitException($"[{name}] {label} failed: {e.Message}\n{text}"); }
                    results.Add("ok");
                }
            }
        }
        finally { await Drop(user); }
        return results;
    }
}
