using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using Oracle.ManagedDataAccess.Client;
using Testcontainers.Oracle;
using Xunit;

namespace OracleSqlParser.IntegrationTests;

[CollectionDefinition(Name)]
public class OracleCollection : ICollectionFixture<OracleFixture>
{
    public const string Name = "Oracle";
}

/// <summary>One gvenzl/oracle-free container per test run, with a small APP schema. If Docker is unavailable, <see cref="SkipReason"/> is set and tests skip.</summary>
public sealed class OracleFixture : IAsyncLifetime
{
    public const string Image = "gvenzl/oracle-free:23-slim-faststart";
    private const string SysPassword = "oracle";
    private const string AppPassword = "app_pw_1";
    private OracleContainer? _container;
    private string _hostPort = "";

    public string? SkipReason { get; private set; }
    public TimeSpan StartupTime { get; private set; }

    public async Task InitializeAsync()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            _container = new OracleBuilder(Image).WithPassword(SysPassword).Build();
            await _container.StartAsync();
            _hostPort = $"{_container.Hostname}:{_container.GetMappedPublicPort(1521)}";
            await using (var sys = await OpenAsync("SYSTEM", SysPassword))
            {
                await ExecAsync(sys, "CREATE USER app IDENTIFIED BY " + AppPassword + " QUOTA UNLIMITED ON USERS");
                await ExecAsync(sys, "GRANT CREATE SESSION, CREATE TABLE, CREATE VIEW, CREATE SEQUENCE TO app");
            }
            await using var app = await OpenAppAsync();
            foreach (var stmt in Schema.Statements) await ExecAsync(app, stmt);
        }
        catch (Exception e)
        {
            SkipReason = $"Oracle container ({Image}) could not be started; is Docker running? {e.GetType().Name}: {e.Message}";
            try { if (_container != null) await _container.DisposeAsync(); } catch { }
            _container = null;
        }
        StartupTime = sw.Elapsed;
        Console.Error.WriteLine($"[OracleFixture] startup + schema: {StartupTime.TotalSeconds:F1}s, skip={SkipReason ?? "no"}");
    }

    public async Task DisposeAsync() { if (_container != null) await _container.DisposeAsync(); }

    public void SkipIfUnavailable() => Skip.If(SkipReason != null, SkipReason);

    private string ConnectionString(string user, string pw) => $"User Id={user};Password={pw};Data Source={_hostPort}/FREEPDB1;Connection Timeout=60;Pooling=false";

    public async Task<OracleConnection> OpenAsync(string user, string pw)
    {
        var c = new OracleConnection(ConnectionString(user, pw));
        await c.OpenAsync();
        return c;
    }

    public Task<OracleConnection> OpenAppAsync() => OpenAsync("APP", AppPassword);
    public Task<OracleConnection> OpenSystemAsync() => OpenAsync("SYSTEM", SysPassword);

    public static async Task ExecAsync(OracleConnection c, string sql)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Runs a query and returns column names plus every row rendered as strings (NULL as "&lt;null&gt;").</summary>
    public static async Task<ResultSet> QueryAsync(OracleConnection c, string sql, IReadOnlyDictionary<string, object?>? binds = null)
    {
        await using var cmd = c.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = sql;
        if (binds != null)
            foreach (var (k, v) in binds) cmd.Parameters.Add(new OracleParameter(k, v ?? DBNull.Value));
        await using DbDataReader r = await cmd.ExecuteReaderAsync();
        var cols = Enumerable.Range(0, r.FieldCount).Select(r.GetName).ToList();
        var rows = new List<List<string>>();
        while (await r.ReadAsync())
        {
            var row = new List<string>();
            for (int i = 0; i < r.FieldCount; i++)
            {
                if (await r.IsDBNullAsync(i)) { row.Add("<null>"); continue; }
                row.Add(r.GetValue(i) switch
                {
                    DateTime d => d.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture),
                    IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                    var o => o.ToString() ?? "",
                });
            }
            rows.Add(row);
        }
        return new ResultSet(cols, rows);
    }
}

public sealed record ResultSet(List<string> Columns, List<List<string>> Rows)
{
    public string Render() => string.Join(" | ", Columns) + "\n" + string.Join("\n", Rows.Select(r => string.Join(" | ", r)));
}

public static class Schema
{
    public static readonly string[] Statements =
    [
        "CREATE TABLE dept (deptno NUMBER(2) PRIMARY KEY, dname VARCHAR2(14), loc VARCHAR2(13))",
        "CREATE TABLE emp (empno NUMBER(4) PRIMARY KEY, ename VARCHAR2(10), job VARCHAR2(9), mgr NUMBER(4), hiredate DATE, sal NUMBER(7,2), comm NUMBER(7,2), deptno NUMBER(2))",
        "INSERT INTO dept VALUES (10, 'ACCOUNTING', 'NEW YORK')",
        "INSERT INTO dept VALUES (20, 'RESEARCH', 'DALLAS')",
        "INSERT INTO dept VALUES (30, 'SALES', 'CHICAGO')",
        "INSERT INTO dept VALUES (40, 'OPERATIONS', 'BOSTON')",
        "INSERT INTO emp VALUES (7369, 'SMITH', 'CLERK', 7902, DATE '1980-12-17', 800, NULL, 20)",
        "INSERT INTO emp VALUES (7499, 'ALLEN', 'SALESMAN', 7698, DATE '1981-02-20', 1600, 300, 30)",
        "INSERT INTO emp VALUES (7521, 'WARD', 'SALESMAN', 7698, DATE '1981-02-22', 1250, 500, 30)",
        "INSERT INTO emp VALUES (7566, 'JONES', 'MANAGER', 7839, DATE '1981-04-02', 2975, NULL, 20)",
        "INSERT INTO emp VALUES (7654, 'MARTIN', 'SALESMAN', 7698, DATE '1981-09-28', 1250, 1400, 30)",
        "INSERT INTO emp VALUES (7698, 'BLAKE', 'MANAGER', 7839, DATE '1981-05-01', 2850, NULL, 30)",
        "INSERT INTO emp VALUES (7782, 'CLARK', 'MANAGER', 7839, DATE '1981-06-09', 2450, NULL, 10)",
        "INSERT INTO emp VALUES (7788, 'SCOTT', 'ANALYST', 7566, DATE '1987-04-19', 3000, NULL, 20)",
        "INSERT INTO emp VALUES (7839, 'KING', 'PRESIDENT', NULL, DATE '1981-11-17', 5000, NULL, 10)",
        "INSERT INTO emp VALUES (7844, 'TURNER', 'SALESMAN', 7698, DATE '1981-09-08', 1500, 0, 30)",
        "INSERT INTO emp VALUES (7876, 'ADAMS', 'CLERK', 7788, DATE '1987-05-23', 1100, NULL, 20)",
        "INSERT INTO emp VALUES (7900, 'JAMES', 'CLERK', 7698, DATE '1981-12-03', 950, NULL, 30)",
        "INSERT INTO emp VALUES (7902, 'FORD', 'ANALYST', 7566, DATE '1981-12-03', 3000, NULL, 20)",
        "INSERT INTO emp VALUES (7934, 'MILLER', 'CLERK', 7782, DATE '1982-01-23', 1300, NULL, 10)",

        "CREATE TABLE docs (id NUMBER PRIMARY KEY, doc VARCHAR2(4000) CONSTRAINT docs_json CHECK (doc IS JSON))",
        "INSERT INTO docs VALUES (1, '{\"name\":\"pen\",\"price\":10,\"tags\":[\"a\",\"b\"],\"items\":[{\"id\":1,\"nm\":\"one\"},{\"id\":2,\"nm\":\"two\"}]}')",
        "INSERT INTO docs VALUES (2, '{\"name\":\"ink\",\"price\":25.5,\"tags\":[\"c\"],\"items\":[{\"id\":3,\"nm\":\"three\"}]}')",

        "CREATE TABLE sales (country VARCHAR2(10), product VARCHAR2(10), year NUMBER(4), quarter VARCHAR2(2), amount NUMBER)",
        "INSERT INTO sales VALUES ('US', 'Bike', 2002, 'Q1', 100)",
        "INSERT INTO sales VALUES ('US', 'Bike', 2002, 'Q2', 120)",
        "INSERT INTO sales VALUES ('US', 'Bike', 2003, 'Q1', 130)",
        "INSERT INTO sales VALUES ('US', 'Bike', 2003, 'Q2', 150)",
        "INSERT INTO sales VALUES ('US', 'Car', 2002, 'Q1', 500)",
        "INSERT INTO sales VALUES ('US', 'Car', 2003, 'Q2', 650)",
        "INSERT INTO sales VALUES ('DE', 'Bike', 2002, 'Q1', 80)",
        "INSERT INTO sales VALUES ('DE', 'Bike', 2003, 'Q2', 95)",
        "INSERT INTO sales VALUES ('DE', 'Car', 2002, 'Q2', 400)",
        "INSERT INTO sales VALUES ('DE', 'Car', 2003, 'Q1', 420)",

        "CREATE TABLE sales_wide (product VARCHAR2(10), q1 NUMBER, q2 NUMBER, q3 NUMBER, q4 NUMBER)",
        "INSERT INTO sales_wide VALUES ('Bike', 10, 20, NULL, 40)",
        "INSERT INTO sales_wide VALUES ('Car', 5, NULL, 15, 25)",

        "CREATE TABLE ticker (symbol VARCHAR2(5), tstamp DATE, price NUMBER)",
        "INSERT INTO ticker VALUES ('ACME', DATE '2024-01-01', 12)",
        "INSERT INTO ticker VALUES ('ACME', DATE '2024-01-02', 11)",
        "INSERT INTO ticker VALUES ('ACME', DATE '2024-01-03', 9)",
        "INSERT INTO ticker VALUES ('ACME', DATE '2024-01-04', 10)",
        "INSERT INTO ticker VALUES ('ACME', DATE '2024-01-05', 14)",
        "INSERT INTO ticker VALUES ('ACME', DATE '2024-01-06', 13)",
        "INSERT INTO ticker VALUES ('ACME', DATE '2024-01-07', 15)",

        "CREATE TABLE \"MixedCase\" (\"Id\" NUMBER, \"lower col\" VARCHAR2(10))",
        "INSERT INTO \"MixedCase\" VALUES (1, 'x')",
        "INSERT INTO \"MixedCase\" VALUES (2, 'y')",

        "CREATE TABLE orders (id NUMBER PRIMARY KEY, customer VARCHAR2(20), amount NUMBER, status VARCHAR2(1))",
        "INSERT INTO orders VALUES (1, 'alice', 50, 'A')",
        "INSERT INTO orders VALUES (2, 'bob', 75, 'A')",
        "INSERT INTO orders VALUES (3, 'carol', 20, 'B')",
        "INSERT INTO orders VALUES (4, 'dave', 90, 'A')",
        "INSERT INTO orders VALUES (5, 'erin', 10, 'A')",
        "INSERT INTO orders VALUES (6, 'frank', 60, 'B')",
        "INSERT INTO orders VALUES (7, 'grace', 30, 'A')",
        "INSERT INTO orders VALUES (8, 'heidi', 85, 'B')",
        "INSERT INTO orders VALUES (9, 'ivan', 40, 'A')",
        "INSERT INTO orders VALUES (10, 'judy', 55, 'A')",
        "CREATE TABLE user_orders (user_id NUMBER, order_id NUMBER)",
        "INSERT INTO user_orders VALUES (1, 1)", "INSERT INTO user_orders VALUES (1, 2)",
        "INSERT INTO user_orders VALUES (1, 3)", "INSERT INTO user_orders VALUES (1, 4)",
        "INSERT INTO user_orders VALUES (2, 5)", "INSERT INTO user_orders VALUES (2, 6)",
        "INSERT INTO user_orders VALUES (2, 7)", "INSERT INTO user_orders VALUES (2, 8)",
        "COMMIT",
    ];
}
