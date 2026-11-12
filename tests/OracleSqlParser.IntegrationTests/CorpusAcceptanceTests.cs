using System.Reflection;
using System.Text.RegularExpressions;
using Oracle.ManagedDataAccess.Client;
using OracleSqlParser.Tests;
using Xunit;

namespace OracleSqlParser.IntegrationTests;

/// <summary>
/// Runs every statement of the unit-test smoke corpus on Oracle, as a fresh, empty scratch user, and checks that Oracle's
/// parser accepts it. Because the scratch schema is empty, many statements fail afterwards with a name-resolution error
/// (ORA-00942 table does not exist, PLS-00201, ...). Those count as "syntax accepted" and are recorded as such (the reason
/// is part of the outcome). Only errors from Oracle's syntax analysis fail the test.
/// </summary>
[Collection(OracleCollection.Name)]
public class CorpusAcceptanceTests(OracleFixture db)
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
        ["SELECT seq.NEXTVAL, a || b || 'c', -a ** 2, a - -1, SYSDATE - 1 FROM dual"] =
            "Oracle SQL has no ** operator (ORA-00936); exponentiation is POWER() in SQL, ** exists only in PL/SQL",
    };

    /// <summary>Multi-statement scripts cannot be sent as one command.</summary>
    private static readonly Dictionary<string, string> NotExecutable = new()
    {
        ["SELECT 1 FROM dual; SELECT 2 FROM dual;\n/\nSELECT 3 FROM dual"] = "SQL*Plus-style multi-statement script (';' and '/' separators); not a single Oracle statement",
    };

    // Errors raised after Oracle has parsed the statement (missing objects / privileges / state): syntax was accepted.
    private static readonly HashSet<int> SemanticOra =
    [
        942,   // table or view does not exist
        904,   // invalid identifier
        1031,  // insufficient privileges
        955,   // name already used
        2289,  // sequence does not exist
        1008,  // not all variables bound
        1086,  // savepoint never established
        4043,  // object does not exist
        2443, 2292, 2264, 2270, 2298, 2303, 1418, 1432, 1434, 1435, 4042, 4080, 12154, 1400, 1722, 957, 959,
        1917, 1918, // user/role does not exist
        1765, 1950, 12012, 12083, 12081, 14501, 14400, 1452, 1451, 1017,
        1426, 1547, 1536, 12899, 1741,
        12003, // materialized view does not exist
        38305, // not in recycle bin
        46401, // traditional AUDIT desupported (policy check after parsing)
        14100, // partition-extended name on remote object (semantic)
        6576,  // not a valid function or procedure name (CALL of a missing routine)
    ];

    private static bool IsPlsql(string sql) =>
        Regex.IsMatch(sql, @"^\s*(BEGIN|DECLARE)\b", RegexOptions.IgnoreCase) ||
        Regex.IsMatch(sql, @"^\s*CREATE\s+(OR\s+REPLACE\s+)?(EDITIONABLE\s+|NONEDITIONABLE\s+)?(FUNCTION|PROCEDURE|PACKAGE|TRIGGER|TYPE\s+BODY)\b", RegexOptions.IgnoreCase);

    private static string Prepare(string sql)
    {
        sql = sql.Trim();
        if (!IsPlsql(sql)) return sql.EndsWith(';') ? sql[..^1] : sql;
        // PL/SQL needs its terminator (e.g. call specs such as CREATE PROCEDURE ... LANGUAGE JAVA NAME '...' end with ';')
        return sql.EndsWith(';') ? sql : sql + ";";
    }

    public enum Outcome { Executed, SyntaxAcceptedSemanticError, Rejected, NotExecutable }

    private static readonly List<string> Report = [];

    [SkippableTheory]
    [MemberData(nameof(Statements))]
    public async Task OracleAcceptsCorpusStatement(int index, string sql)
    {
        db.SkipIfUnavailable();
        Skip.If(NotExecutable.TryGetValue(sql, out var why), why);

        var (outcome, detail) = await RunInScratchSchema(index, Prepare(sql));
        lock (Report) Report.Add($"{outcome}\t{detail}\t{sql}");

        if (KnownOracleRejects.TryGetValue(sql, out var reason))
        {
            Assert.True(outcome == Outcome.Rejected, $"expected Oracle to reject this ({reason}) but outcome was {outcome}: {detail}");
            return;
        }
        Assert.True(outcome != Outcome.Rejected, $"Oracle rejected corpus statement:\n{sql}\n{detail}");
    }

    private async Task<(Outcome, string)> RunInScratchSchema(int index, string sql)
    {
        var user = $"SCR{index}";
        const string pw = "scratch_pw_1";
        await using (var sys = await db.OpenSystemAsync())
        {
            await OracleFixture.ExecAsync(sys, $"CREATE USER {user} IDENTIFIED BY {pw} QUOTA UNLIMITED ON USERS");
            await OracleFixture.ExecAsync(sys, $"GRANT CREATE SESSION, CREATE TABLE, CREATE VIEW, CREATE SEQUENCE, CREATE PROCEDURE, CREATE TRIGGER, CREATE TYPE, CREATE SYNONYM, CREATE MATERIALIZED VIEW, CREATE DATABASE LINK, ALTER SESSION, CREATE ANY INDEX TO {user}");
        }
        try
        {
            await using var c = await db.OpenAsync(user, pw);
            try
            {
                await OracleFixture.ExecAsync(c, sql);
            }
            catch (OracleException e)
            {
                return Classify(e.Number, e.Message);
            }
            // CREATE PROCEDURE/PACKAGE/TRIGGER/TYPE BODY succeed "with compilation errors"; look them up.
            if (IsPlsql(sql) && Regex.IsMatch(sql, @"^\s*CREATE\b", RegexOptions.IgnoreCase))
            {
                await using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT text FROM user_errors WHERE attribute = 'ERROR' ORDER BY sequence";
                await using var r = await cmd.ExecuteReaderAsync();
                var errs = new List<string>();
                while (await r.ReadAsync()) errs.Add(r.GetString(0));
                if (errs.Count > 0) return ClassifyPlsql(string.Join(" / ", errs));
            }
            return (Outcome.Executed, "ok");
        }
        finally
        {
            await using var sys = await db.OpenSystemAsync();
            try { await OracleFixture.ExecAsync(sys, $"DROP USER {user} CASCADE"); } catch (OracleException) { }
        }
    }

    private static (Outcome, string) Classify(int ora, string message)
    {
        string first = message.Split('\n')[0];
        if (ora == 6550 || message.Contains("PLS-")) return ClassifyPlsql(message.Replace('\n', ' '));
        // ORA-00942-class failures happen during semantic analysis, which runs after the whole statement parsed.
        return SemanticOra.Contains(ora) ? (Outcome.SyntaxAcceptedSemanticError, first) : (Outcome.Rejected, first);
    }

    private static (Outcome, string) ClassifyPlsql(string text)
    {
        // PLS-00103 "Encountered the symbol X when expecting ..." is the PL/SQL parser's syntax error; PLS-00201/ORA-00942 etc. are name resolution.
        string brief = text.Length > 200 ? text[..200] : text;
        return Regex.IsMatch(text, @"PLS-00103") ? (Outcome.Rejected, brief) : (Outcome.SyntaxAcceptedSemanticError, brief);
    }

    [SkippableFact]
    public void Corpus_IsLarge() => Assert.True(Corpus().Count() > 150);
}
