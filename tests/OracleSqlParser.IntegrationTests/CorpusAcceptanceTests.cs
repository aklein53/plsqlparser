using System.Reflection;
using System.Text.RegularExpressions;
using Oracle.ManagedDataAccess.Client;
using OracleSqlParser.Tests;
using Xunit;
using Xunit.Abstractions;

namespace OracleSqlParser.IntegrationTests;

/// <summary>
/// Runs every statement of the unit-test smoke corpus on Oracle, as a fresh, empty scratch user, and checks that Oracle's
/// parser accepts it. Because the scratch schema is empty, many statements fail afterwards with a name-resolution error
/// (ORA-00942 table does not exist, PLS-00201, ...). Those count as "syntax accepted" and are recorded as such (the reason
/// is part of the outcome). Only errors from Oracle's syntax analysis fail the test.
/// </summary>
[Collection(OracleCollection.Name)]
public class CorpusAcceptanceTests(OracleFixture db, ITestOutputHelper output)
{
    public static IEnumerable<string> Corpus()
    {
        var attrs = typeof(SmokeCorpusTests).GetMethod("Parses")!.GetCustomAttributes<InlineDataAttribute>();
        foreach (var a in attrs)
            foreach (var sql in a.GetData(null!).Select(d => (string)d[0]))
                yield return sql;
    }

    public static IEnumerable<object[]> Statements() => Corpus().Select((s, i) => new object[] { i, s });

    /// <summary>
    /// Corpus statements that Oracle rejects when sent standalone. They are valid only inside PL/SQL (or SQL*Plus),
    /// so the corpus entry is a PL/SQL fragment rather than a SQL statement. Kept in the corpus; the test asserts that
    /// Oracle really does reject each one (so this list cannot silently go stale).
    /// </summary>
    public static readonly Dictionary<string, string> KnownOracleRejects = new()
    {
        ["SELECT ROW_NUMBER() OVER (PARTITION BY d ORDER BY s DESC ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) rn FROM t"] =
            "ORA-03097: Oracle does not allow a windowing clause on ROW_NUMBER (rank/numbering functions take no frame)",
        ["EXEC :x := 1"] = "SQL*Plus client command, not a server statement (ORA-00900); EXEC is only valid in SQL*Plus",
        ["EXECUTE :x := f(1)"] = "SQL*Plus client command, not a server statement (ORA-00900); EXECUTE is only valid in SQL*Plus",
    };

    /// <summary>
    /// PL/SQL-only forms. Against a missing table Oracle reports ORA-00942 first (name resolution precedes the INTO / CURRENT OF
    /// checks), which would look like "syntax accepted". So these run with a pre-created table T(id, a, b, col_a) in the scratch
    /// schema, where Oracle reports the real error (ORA-00905 / ORA-03001 / ORA-00925). The test asserts the rejection.
    /// </summary>
    public static readonly Dictionary<string, string> PlsqlOnlyForms = new()
    {
        ["SELECT col_a INTO v_a FROM t WHERE id = 1"] = "PL/SQL-only form: SELECT INTO is not SQL (ORA-00905)",
        ["SELECT col_a BULK COLLECT INTO v_tab FROM t"] = "PL/SQL-only form: BULK COLLECT INTO (ORA-03001)",
        ["UPDATE t SET a = 1 WHERE CURRENT OF c1"] = "PL/SQL-only form: WHERE CURRENT OF cursor (ORA-03001)",
        ["DELETE FROM t WHERE id = 1 RETURNING id BULK COLLECT INTO v"] = "PL/SQL-only form: RETURNING ... BULK COLLECT INTO (ORA-00925)",
    };

    /// <summary>Multi-statement scripts cannot be sent as one command.</summary>
    public static readonly Dictionary<string, string> NotExecutable = new()
    {
        ["SELECT 1 FROM dual; SELECT 2 FROM dual;\n/\nSELECT 3 FROM dual"] = "SQL*Plus-style multi-statement script (';' and '/' separators); not a single Oracle statement",
        ["AUDIT POLICY scratch_pol BY scratch_u WHENEVER NOT SUCCESSFUL"] = "refers to an audit policy that a fresh scratch schema does not have",
        ["CREATE TEMPORARY TABLESPACE scratch_tmp TEMPFILE 'scratch_tmp.dbf' SIZE 5M AUTOEXTEND ON NEXT 1M MAXSIZE UNLIMITED"] = "creates a tempfile on the database server filesystem; not run as part of the scratch-schema corpus",
    };

    private static readonly List<string> Report = [];

    [SkippableTheory]
    [MemberData(nameof(Statements))]
    public async Task OracleAcceptsCorpusStatement(int index, string sql)
    {
        db.SkipIfUnavailable();
        Skip.If(NotExecutable.TryGetValue(sql, out var why), why);

        var plsqlOnly = PlsqlOnlyForms.TryGetValue(sql, out var plsqlReason);
        var (outcome, detail) = await OracleHarness.RunInScratchSchema(db, $"SCR{index}", OracleHarness.Prepare(sql), plsqlOnly);
        lock (Report) Report.Add($"{outcome}\t{detail}\t{sql}");
        output.WriteLine($"OUTCOME={outcome} DETAIL={detail} SQL={sql}");

        if (plsqlOnly)
        {
            Assert.True(outcome == Outcome.Rejected, $"expected Oracle to reject this ({plsqlReason}) but outcome was {outcome}: {detail}");
            return;
        }
        if (KnownOracleRejects.TryGetValue(sql, out var reason))
        {
            Assert.True(outcome == Outcome.Rejected, $"expected Oracle to reject this ({reason}) but outcome was {outcome}: {detail}");
            return;
        }
        Assert.True(outcome != Outcome.Rejected, $"Oracle rejected corpus statement:\n{sql}\n{detail}");
    }

    [SkippableFact]
    public void Corpus_IsLarge() => Assert.True(Corpus().Count() > 150);
}
