using System.Text.RegularExpressions;
using Oracle.ManagedDataAccess.Client;

namespace OracleSqlParser.IntegrationTests;

public enum Outcome { Executed, SyntaxAcceptedSemanticError, Rejected, NotExecutable }

/// <summary>Shared harness: runs one statement in a fresh scratch user and classifies Oracle's response.</summary>
public static class OracleHarness
{
    // Errors raised after Oracle has parsed the statement (missing objects / privileges / state): syntax was accepted.
    private static readonly HashSet<int> SemanticOra =
    [
        942,   // table or view does not exist (name resolution; runs before most clause-level checks, hence PlsqlOnlyForms uses a real table)
        904,   // invalid identifier (column not found; resolved after the statement parsed)
        1031,  // insufficient privileges
        955,   // name already used by an existing object
        2289,  // sequence does not exist
        1008,  // not all variables bound (execution-time bind check)
        1086,  // savepoint never established (runtime state)
        4043,  // object does not exist
        2443,  // cannot drop constraint - nonexistent constraint
        2292,  // integrity constraint violated - child record found
        2264,  // name already used by an existing constraint
        2270,  // no matching unique or primary key for this column list
        2298,  // cannot validate - parent keys not found
        2303,  // cannot drop or replace a type with type or table dependents
        1418,  // specified index does not exist
        1432,  // public synonym to be dropped does not exist
        1434,  // private synonym to be dropped does not exist
        1435,  // user does not exist
        4042,  // procedure/function/package does not exist
        4080,  // trigger does not exist
        12154, // TNS could not resolve the connect identifier (database link target, looked up at execution)
        1400,  // cannot insert NULL (constraint check on data)
        1722,  // invalid number (data conversion at execution)
        957,   // duplicate column name (semantic check of an already parsed definition)
        959,   // tablespace does not exist
        1917, 1918, // user/role does not exist
        1765,  // specifying owner's name in the object name is not allowed
        1950,  // no privileges on tablespace
        12012, // error on auto execute of job
        12083, // must use ALTER MATERIALIZED VIEW to alter this object
        12081, // update operation not allowed on table
        14501, // object is not partitioned
        14400, // inserted partition key does not map to any partition
        1452,  // cannot create unique index; duplicate keys found
        1451,  // column cannot be modified to NULL (already NULL)
        1017,  // invalid username/password (e.g. CONNECT inside the corpus)
        1426,  // numeric overflow
        1547,  // failed to allocate extent (storage)
        1536,  // space quota exceeded for tablespace
        12899, // value too large for column
        12003, // materialized view does not exist
        38305, // object not in recycle bin
        46401, // traditional AUDIT desupported (policy check after parsing)
        14100, // partition-extended name on remote object (semantic)
        6576,  // not a valid function or procedure name (CALL of a missing routine)
        // NOT listed on purpose: 1741 (illegal zero-length identifier) is a lexical error; 905/923/925/933/3001 are syntax-level.
    ];

    public static bool IsPlsql(string sql) =>
        Regex.IsMatch(sql, @"^\s*(BEGIN|DECLARE)\b", RegexOptions.IgnoreCase) ||
        Regex.IsMatch(sql, @"^\s*CREATE\s+(OR\s+REPLACE\s+)?(EDITIONABLE\s+|NONEDITIONABLE\s+)?(FUNCTION|PROCEDURE|PACKAGE|TRIGGER|TYPE\s+BODY)\b", RegexOptions.IgnoreCase);

    public static string Prepare(string sql)
    {
        sql = sql.Trim();
        if (!IsPlsql(sql)) return sql.EndsWith(';') ? sql[..^1] : sql;
        // PL/SQL needs its terminator (e.g. call specs such as CREATE PROCEDURE ... LANGUAGE JAVA NAME '...' end with ';')
        return sql.EndsWith(';') ? sql : sql + ";";
    }

    public static async Task<(Outcome, string)> RunInScratchSchema(OracleFixture db, string user, string sql, bool withTableT)
    {
        const string pw = "scratch_pw_1";
        await using (var sys = await db.OpenSystemAsync())
        {
            await OracleFixture.ExecAsync(sys, $"CREATE USER {user} IDENTIFIED BY {pw} QUOTA UNLIMITED ON USERS");
            await OracleFixture.ExecAsync(sys, $"GRANT CREATE SESSION, CREATE TABLE, CREATE VIEW, CREATE SEQUENCE, CREATE PROCEDURE, CREATE TRIGGER, CREATE TYPE, CREATE SYNONYM, CREATE MATERIALIZED VIEW, CREATE DATABASE LINK, ALTER SESSION, CREATE ANY INDEX TO {user}");
        }
        try
        {
            await using var c = await db.OpenAsync(user, pw);
            if (withTableT) await OracleFixture.ExecAsync(c, "CREATE TABLE t (id NUMBER, a NUMBER, b NUMBER, col_a NUMBER)");
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

    public static (Outcome, string) Classify(int ora, string message)
    {
        string first = message.Split('\n')[0];
        if (ora == 6550 || message.Contains("PLS-")) return ClassifyPlsql(message.Replace('\n', ' '));
        // ORA-00942-class failures happen during semantic analysis, which runs after the whole statement parsed.
        // ORA-20000..20999 are user-defined exceptions (RAISE_APPLICATION_ERROR): the PL/SQL block parsed and ran.
        if (ora is >= 20000 and <= 20999) return (Outcome.SyntaxAcceptedSemanticError, first);
        return SemanticOra.Contains(ora) ? (Outcome.SyntaxAcceptedSemanticError, first) : (Outcome.Rejected, first);
    }

    public static (Outcome, string) ClassifyPlsql(string text)
    {
        // PLS-00103 "Encountered the symbol X when expecting ..." is the PL/SQL parser's syntax error; PLS-00201/ORA-00942 etc. are name resolution.
        string brief = text.Length > 200 ? text[..200] : text;
        return Regex.IsMatch(text, @"PLS-00103") ? (Outcome.Rejected, brief) : (Outcome.SyntaxAcceptedSemanticError, brief);
    }
}
