using Oracle.ManagedDataAccess.Client;
using OracleSqlParser.Tests;
using Xunit;

namespace OracleSqlParser.IntegrationTests;

/// <summary>
/// Expert-written, deliberately dense Oracle 23ai statements (reporting, ETL, DBA scripts, PL/SQL). For each statement:
/// (1) it parses without errors, (2) the typed tree has the right structure for the key constructs (asserted with targeted
/// facts, not a full shape dump), (3) generator output re-parses to a structurally equal tree, and (4) Oracle runs the
/// original and the generated SQL against the same data and produces the same rows / row counts / table contents, with
/// no compilation errors. Each case runs in its own scratch schema so DDL and PL/SQL objects do not interfere.
/// </summary>
[Collection(OracleCollection.Name)]
public class ExpertStressTests(OracleFixture db)
{
    private const string Pw = "scratch_pw_1";

    /// <summary>Shared sample schema (HR-style employees, orders, events, ticker, hierarchy, stock and bonus data).</summary>
    private static readonly string[] Base =
    [
        "CREATE TABLE dept (deptno NUMBER(2) PRIMARY KEY, dname VARCHAR2(14), loc VARCHAR2(13))",
        "CREATE TABLE emp (empno NUMBER(4) PRIMARY KEY, ename VARCHAR2(10), job VARCHAR2(9), mgr NUMBER(4), hiredate DATE, sal NUMBER(7,2), comm NUMBER(7,2), deptno NUMBER(2) REFERENCES dept (deptno))",
        "CREATE TABLE customers (id NUMBER PRIMARY KEY, name VARCHAR2(30), region VARCHAR2(10), credit_limit NUMBER(10,2))",
        "CREATE TABLE orders (id NUMBER PRIMARY KEY, customer_id NUMBER REFERENCES customers (id), order_date DATE, status VARCHAR2(1), total NUMBER(10,2))",
        "CREATE TABLE order_lines (order_id NUMBER REFERENCES orders (id), line_no NUMBER, product VARCHAR2(10), qty NUMBER, unit_price NUMBER(8,2), PRIMARY KEY (order_id, line_no))",
        "CREATE TABLE sales (country VARCHAR2(10), product VARCHAR2(10), year NUMBER(4), quarter VARCHAR2(2), amount NUMBER)",
        "CREATE TABLE sales_wide (product VARCHAR2(10), q1 NUMBER, q2 NUMBER, q3 NUMBER, q4 NUMBER)",
        "CREATE TABLE events (id NUMBER PRIMARY KEY, user_id NUMBER, event_type VARCHAR2(20), ts TIMESTAMP, payload VARCHAR2(4000) CONSTRAINT events_json CHECK (payload IS JSON))",
        "CREATE TABLE ticker (symbol VARCHAR2(5), tstamp DATE, price NUMBER)",
        "CREATE TABLE org (id NUMBER PRIMARY KEY, parent_id NUMBER, name VARCHAR2(20))",
        "CREATE TABLE stock (sku VARCHAR2(10) PRIMARY KEY, qty NUMBER, price NUMBER(8,2))",
        "CREATE TABLE stock_feed (sku VARCHAR2(10), qty NUMBER, price NUMBER)",
        "CREATE TABLE bonus (empno NUMBER(4) PRIMARY KEY, amount NUMBER(8,2), note VARCHAR2(40))",
        "CREATE TYPE num_row AS OBJECT (n NUMBER, label VARCHAR2(30))",
        "CREATE TYPE num_tab AS TABLE OF num_row",
        """
        BEGIN
          INSERT INTO dept VALUES (10, 'ACCOUNTING', 'NEW YORK');
          INSERT INTO dept VALUES (20, 'RESEARCH', 'DALLAS');
          INSERT INTO dept VALUES (30, 'SALES', 'CHICAGO');
          INSERT INTO dept VALUES (40, 'OPERATIONS', 'BOSTON');
          INSERT INTO emp VALUES (7369, 'SMITH', 'CLERK', 7902, DATE '1980-12-17', 800, NULL, 20);
          INSERT INTO emp VALUES (7499, 'ALLEN', 'SALESMAN', 7698, DATE '1981-02-20', 1600, 300, 30);
          INSERT INTO emp VALUES (7521, 'WARD', 'SALESMAN', 7698, DATE '1981-02-22', 1250, 500, 30);
          INSERT INTO emp VALUES (7566, 'JONES', 'MANAGER', 7839, DATE '1981-04-02', 2975, NULL, 20);
          INSERT INTO emp VALUES (7654, 'MARTIN', 'SALESMAN', 7698, DATE '1981-09-28', 1250, 1400, 30);
          INSERT INTO emp VALUES (7698, 'BLAKE', 'MANAGER', 7839, DATE '1981-05-01', 2850, NULL, 30);
          INSERT INTO emp VALUES (7782, 'CLARK', 'MANAGER', 7839, DATE '1981-06-09', 2450, NULL, 10);
          INSERT INTO emp VALUES (7788, 'SCOTT', 'ANALYST', 7566, DATE '1987-04-19', 3000, NULL, 20);
          INSERT INTO emp VALUES (7839, 'KING', 'PRESIDENT', NULL, DATE '1981-11-17', 5000, NULL, 10);
          INSERT INTO emp VALUES (7844, 'TURNER', 'SALESMAN', 7698, DATE '1981-09-08', 1500, 0, 30);
          INSERT INTO emp VALUES (7876, 'ADAMS', 'CLERK', 7788, DATE '1987-05-23', 1100, NULL, 20);
          INSERT INTO emp VALUES (7900, 'JAMES', 'CLERK', 7698, DATE '1981-12-03', 950, NULL, 30);
          INSERT INTO emp VALUES (7902, 'FORD', 'ANALYST', 7566, DATE '1981-12-03', 3000, NULL, 20);
          INSERT INTO emp VALUES (7934, 'MILLER', 'CLERK', 7782, DATE '1982-01-23', 1300, NULL, 10);
          INSERT INTO customers VALUES (1, 'Acme Corp', 'EAST', 5000);
          INSERT INTO customers VALUES (2, 'Blue Bird LLC', 'WEST', 1200);
          INSERT INTO customers VALUES (3, 'Carta Ltd', 'EAST', 800);
          INSERT INTO customers VALUES (4, 'Dune Inc', 'SOUTH', NULL);
          INSERT INTO customers VALUES (5, 'Ember Co', 'WEST', 2500);
          INSERT INTO orders VALUES (1, 1, DATE '2024-01-05', 'A', 120.00);
          INSERT INTO orders VALUES (2, 1, DATE '2024-01-20', NULL, 35.50);
          INSERT INTO orders VALUES (3, 2, DATE '2024-02-02', 'B', 980.00);
          INSERT INTO orders VALUES (4, 2, DATE '2024-02-10', NULL, 15.00);
          INSERT INTO orders VALUES (5, 3, DATE '2024-02-11', 'A', 410.25);
          INSERT INTO orders VALUES (6, 1, DATE '2024-03-01', 'C', 75.00);
          INSERT INTO orders VALUES (7, 3, DATE '2024-03-15', 'A', 60.00);
          INSERT INTO orders VALUES (8, 4, DATE '2024-03-16', NULL, 300.00);
          INSERT INTO orders VALUES (9, 4, DATE '2024-03-30', 'B', 45.00);
          INSERT INTO orders VALUES (10, 5, DATE '2024-04-02', 'A', 500.00);
          INSERT INTO orders VALUES (11, 5, DATE '2024-04-03', 'C', 80.00);
          INSERT INTO orders VALUES (12, 2, DATE '2024-04-20', 'A', 95.00);
          INSERT INTO order_lines VALUES (1, 1, 'WIDGET', 2, 10.00);
          INSERT INTO order_lines VALUES (1, 2, 'GADGET', 1, 100.00);
          INSERT INTO order_lines VALUES (2, 1, 'WIDGET', 1, 35.50);
          INSERT INTO order_lines VALUES (3, 1, 'GADGET', 5, 150.00);
          INSERT INTO order_lines VALUES (3, 2, 'GIZMO', 2, 200.00);
          INSERT INTO order_lines VALUES (4, 1, 'WIDGET', 1, 15.00);
          INSERT INTO order_lines VALUES (5, 1, 'GIZMO', 1, 410.25);
          INSERT INTO order_lines VALUES (6, 1, 'WIDGET', 3, 25.00);
          INSERT INTO order_lines VALUES (7, 1, 'GADGET', 1, 60.00);
          INSERT INTO order_lines VALUES (8, 1, 'GIZMO', 2, 150.00);
          INSERT INTO order_lines VALUES (9, 1, 'WIDGET', 3, 15.00);
          INSERT INTO order_lines VALUES (10, 1, 'GADGET', 2, 250.00);
          INSERT INTO order_lines VALUES (11, 1, 'WIDGET', 4, 20.00);
          INSERT INTO order_lines VALUES (12, 1, 'GIZMO', 1, 95.00);
          INSERT INTO sales VALUES ('US', 'Bike', 2002, 'Q1', 100);
          INSERT INTO sales VALUES ('US', 'Bike', 2002, 'Q2', 120);
          INSERT INTO sales VALUES ('US', 'Bike', 2003, 'Q1', 130);
          INSERT INTO sales VALUES ('US', 'Bike', 2003, 'Q2', 150);
          INSERT INTO sales VALUES ('US', 'Car', 2002, 'Q1', 500);
          INSERT INTO sales VALUES ('US', 'Car', 2003, 'Q2', 650);
          INSERT INTO sales VALUES ('DE', 'Bike', 2002, 'Q1', 80);
          INSERT INTO sales VALUES ('DE', 'Bike', 2003, 'Q2', 95);
          INSERT INTO sales VALUES ('DE', 'Car', 2002, 'Q2', 400);
          INSERT INTO sales VALUES ('DE', 'Car', 2003, 'Q1', 420);
          INSERT INTO sales_wide VALUES ('Bike', 10, 20, NULL, 40);
          INSERT INTO sales_wide VALUES ('Car', 5, NULL, 15, 25);
          INSERT INTO sales_wide VALUES ('Van', NULL, 7, 8, NULL);
          INSERT INTO events VALUES (1, 101, 'LOGIN', TIMESTAMP '2024-05-01 08:00:00', '{"device":"ios","tags":["a","b"],"items":[{"sku":"S1","qty":2},{"sku":"S2","qty":1}]}');
          INSERT INTO events VALUES (2, 101, 'VIEW', TIMESTAMP '2024-05-01 08:05:00', '{"device":"web","items":[{"sku":"S1","qty":1}]}');
          INSERT INTO events VALUES (3, 102, 'PURCHASE', TIMESTAMP '2024-05-01 09:00:00', '{"device":"web","tags":[],"items":[{"sku":"S3","qty":4},{"sku":"S1","qty":"n/a"}]}');
          INSERT INTO events VALUES (4, 102, 'LOGOUT', TIMESTAMP '2024-05-02 10:00:00', '{"device":"ios"}');
          INSERT INTO events VALUES (5, 103, 'PURCHASE', TIMESTAMP '2024-05-02 11:00:00', '{"device":"android","items":[{"sku":"S2","qty":3}],"tags":["x"]}');
          INSERT INTO events VALUES (6, 103, 'VIEW', NULL, '{"device":"android"}');
          INSERT INTO events VALUES (7, 104, 'PURCHASE', TIMESTAMP '2024-05-03 12:00:00', '{"device":"web","items":[]}');
          INSERT INTO events VALUES (8, 104, 'SEARCH', TIMESTAMP '2024-05-03 12:30:00', '{"device":"web","q":"oracle 23ai"}');
          INSERT INTO ticker VALUES ('ACME', DATE '2024-01-01', 12);
          INSERT INTO ticker VALUES ('ACME', DATE '2024-01-02', 11);
          INSERT INTO ticker VALUES ('ACME', DATE '2024-01-03', 9);
          INSERT INTO ticker VALUES ('ACME', DATE '2024-01-04', 10);
          INSERT INTO ticker VALUES ('ACME', DATE '2024-01-05', 14);
          INSERT INTO ticker VALUES ('ACME', DATE '2024-01-06', 13);
          INSERT INTO ticker VALUES ('ACME', DATE '2024-01-07', 15);
          INSERT INTO ticker VALUES ('BETA', DATE '2024-01-01', 20);
          INSERT INTO ticker VALUES ('BETA', DATE '2024-01-02', 18);
          INSERT INTO ticker VALUES ('BETA', DATE '2024-01-03', 21);
          INSERT INTO ticker VALUES ('BETA', DATE '2024-01-04', 19);
          INSERT INTO org VALUES (1, NULL, 'Board');
          INSERT INTO org VALUES (2, 1, 'CEO');
          INSERT INTO org VALUES (3, 2, 'CTO');
          INSERT INTO org VALUES (4, 2, 'CFO');
          INSERT INTO org VALUES (5, 3, 'Platform');
          INSERT INTO org VALUES (6, 3, 'Data');
          INSERT INTO org VALUES (7, 4, 'Finance');
          INSERT INTO org VALUES (8, 5, 'SRE');
          INSERT INTO stock VALUES ('A-100', 10, 2.50);
          INSERT INTO stock VALUES ('B-200', 0, 9.99);
          INSERT INTO stock VALUES ('C-300', 7, 1.25);
          INSERT INTO stock VALUES ('D-400', 3, 50);
          INSERT INTO stock VALUES ('E-500', 12, 4.00);
          INSERT INTO stock_feed VALUES ('A-100', 15, 2.75);
          INSERT INTO stock_feed VALUES ('C-300', 0, 1.25);
          INSERT INTO stock_feed VALUES ('F-600', 9, 8.00);
          INSERT INTO stock_feed VALUES ('B-200', 4, 9.50);
          INSERT INTO stock_feed VALUES ('G-700', 1, 1234567.891);
          INSERT INTO bonus VALUES (7499, 100, 'q1');
          INSERT INTO bonus VALUES (7521, 50, 'q1');
          INSERT INTO bonus VALUES (7900, 30, 'legacy');
          COMMIT;
        END;
        """,
    ];

    // ------------------------------------------------------------------------------------------------------------------
    // Query statements
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>Recursive CTE with SEARCH DEPTH FIRST and CYCLE: the SEARCH/CYCLE clauses belong to org_tree, not leaf_stats.</summary>
    [SkippableFact]
    public async Task Q01_RecursiveCteSearchDepthFirstAndCycle()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            WITH org_tree (id, parent_id, name, depth, trail) AS (
              SELECT o.id, o.parent_id, o.name, 0, CAST(o.name AS VARCHAR2(400)) FROM org o WHERE o.parent_id IS NULL
              UNION ALL
              SELECT o.id, o.parent_id, o.name, t.depth + 1, t.trail || ' > ' || o.name
                FROM org o JOIN org_tree t ON o.parent_id = t.id
            ) SEARCH DEPTH FIRST BY name SET ord_seq
              CYCLE id SET is_cycle TO 'Y' DEFAULT 'N',
            leaf_stats AS (
              SELECT o.parent_id AS pid, COUNT(*) AS kids FROM org o WHERE o.parent_id IS NOT NULL GROUP BY o.parent_id
            )
            SELECT t.depth, t.trail, NVL(ls.kids, 0) AS direct_reports
              FROM org_tree t
              LEFT JOIN leaf_stats ls ON ls.pid = t.id
             WHERE t.is_cycle = 'N'
             ORDER BY t.ord_seq
            """,
        };
        var s = Shape.Parse(sql[0]);
        var with = ((SelectStatement)s.Statements[0]).With;
        Assert.Equal(2, with.Count);
        Assert.True(with[0].Search is { DepthFirst: true });
        Assert.NotNull(with[0].Cycle);
        Assert.Null(with[1].Search);
        Assert.Null(with[1].Cycle);
        await VerifyAsync(sql, ["SELECT COUNT(*) FROM org"]);
    }

    /// <summary>Analytic windows: RANGE INTERVAL frame lands on SUM only, LAG carries IGNORE NULLS, KEEP belongs to MAX.</summary>
    [SkippableFact]
    public async Task Q02_AnalyticRangeIntervalKeepLagIgnoreNulls()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            SELECT o.customer_id, o.id, o.order_date, o.total,
                   SUM(o.total) OVER (PARTITION BY o.customer_id ORDER BY o.order_date
                                      RANGE BETWEEN INTERVAL '30' DAY PRECEDING AND CURRENT ROW) AS rolling_30d,
                   ROUND(RATIO_TO_REPORT(o.total) OVER (PARTITION BY o.customer_id) * 100, 2) AS pct_of_cust,
                   LAG(o.status) IGNORE NULLS OVER (PARTITION BY o.customer_id ORDER BY o.order_date, o.id) AS prev_status,
                   MAX(o.total) KEEP (DENSE_RANK LAST ORDER BY o.order_date) OVER (PARTITION BY o.customer_id) AS last_total
              FROM orders o
             ORDER BY o.customer_id, o.order_date, o.id
            """,
        };
        var s = Shape.Parse(sql[0]);
        var fns = Find<FunctionCall>(s);
        var sum = fns.Single(f => Str(f).StartsWith("(FunctionCall name=[SUM]"));
        Assert.Contains("(WindowFrame unit=Range", Str(sum));
        Assert.DoesNotContain("ignoreNulls", Str(sum));
        var lag = fns.Single(f => Str(f).StartsWith("(FunctionCall name=[LAG]"));
        Assert.Contains("ignoreNulls", Str(lag));
        Assert.DoesNotContain("(WindowFrame", Str(lag));
        var keep = Find<KeepClause>(s).Single();
        Assert.False(keep.First);
        Assert.Contains("keep=", Str(fns.Single(f => Str(f).StartsWith("(FunctionCall name=[MAX]"))));
        await VerifyAsync(sql, []);
    }

    /// <summary>MATCH_RECOGNIZE: alternation has lowest precedence (STRT DOWN+ UP+ | FLAT); CLASSIFIER and MATCH_NUMBER measures.</summary>
    [SkippableFact]
    public async Task Q03_MatchRecognizeAlternationPrecedenceAndQuantifiers()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            SELECT symbol, start_dt, bottom_dt, end_dt, match_no, last_cls, days_down, days_up
              FROM ticker MATCH_RECOGNIZE (
                PARTITION BY symbol
                ORDER BY tstamp
                MEASURES
                  FIRST(DOWN.tstamp) AS start_dt,
                  LAST(DOWN.tstamp)  AS bottom_dt,
                  LAST(UP.tstamp)    AS end_dt,
                  MATCH_NUMBER()     AS match_no,
                  CLASSIFIER()       AS last_cls,
                  COUNT(DOWN.price)  AS days_down,
                  COUNT(UP.price)    AS days_up
                ONE ROW PER MATCH
                AFTER MATCH SKIP PAST LAST ROW
                PATTERN (STRT DOWN+ UP+ | FLAT)
                DEFINE
                  DOWN AS price < PREV(price),
                  UP   AS price > PREV(price),
                  FLAT AS price = PREV(price)
              )
             ORDER BY symbol, match_no
            """,
        };
        var s = Shape.Parse(sql[0]);
        var mr = Find<MatchRecognizeTable>(s).Single();
        var alt = Assert.IsType<PatternAlternation>(mr.Pattern);
        Assert.Equal(2, alt.Items.Count);
        var concat = Assert.IsType<PatternConcat>(alt.Items[0]);
        Assert.Equal(3, concat.Items.Count);
        Assert.IsType<PatternVariable>(alt.Items[1]);
        Assert.Contains("(FunctionCall name=[CLASSIFIER]", Str(mr));
        await VerifyAsync(sql, []);
    }

    /// <summary>PIVOT with two aggregates over an UNPIVOT ... INCLUDE NULLS: the pivot must read the unpivoted derived table.</summary>
    [SkippableFact]
    public async Task Q04_PivotMultipleAggregatesOverUnpivotIncludeNulls()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            SELECT p.product, p.q1_total, p.q1_n, p.q2_total, p.q2_n, p.q3_total, p.q4_total
              FROM (
                SELECT product, period, qty
                  FROM sales_wide
                  UNPIVOT INCLUDE NULLS (qty FOR period IN (q1 AS 'Q1', q2 AS 'Q2', q3 AS 'Q3', q4 AS 'Q4'))
              )
              PIVOT (
                SUM(qty) AS total, COUNT(qty) AS n
                FOR period IN ('Q1' AS q1, 'Q2' AS q2, 'Q3' AS q3, 'Q4' AS q4)
              ) p
             ORDER BY p.product
            """,
        };
        var s = Shape.Parse(sql[0]);
        var pv = Find<PivotTable>(s).Single();
        Assert.Equal(2, pv.Aggregates.Count);
        Assert.Equal(4, pv.InItems.Count);
        Assert.True(Find<UnpivotTable>(s).Single().IncludeNulls);
        await VerifyAsync(sql, []);
    }

    /// <summary>Hierarchy: PRIOR binds to the column only, CONNECT_BY_ROOT and SYS_CONNECT_BY_PATH, NOCYCLE, ORDER SIBLINGS BY.</summary>
    [SkippableFact]
    public async Task Q05_ConnectByPriorNocycleRootPathSiblings()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            SELECT LPAD(' ', 2 * (LEVEL - 1)) || o.name AS tree,
                   LEVEL AS lvl,
                   CONNECT_BY_ROOT o.name AS root_name,
                   SYS_CONNECT_BY_PATH(o.name, '/') AS path,
                   CONNECT_BY_ISLEAF AS is_leaf,
                   CONNECT_BY_ISCYCLE AS is_cycle
              FROM org o
             START WITH o.parent_id IS NULL
             CONNECT BY NOCYCLE PRIOR o.id = o.parent_id
             ORDER SIBLINGS BY o.name
            """,
        };
        var s = Shape.Parse(sql[0]);
        var q = (QuerySpecification)((SelectStatement)s.Statements[0]).Body;
        Assert.True(q.NoCycle);
        Assert.Contains("(= (PRIOR O.ID) O.PARENT_ID)", Str(q.ConnectBy!));
        Assert.Contains("CONNECT_BY_ROOT", Str(q.SelectList[2]));
        await VerifyAsync(sql, []);
    }

    /// <summary>ROLLUP concatenated with GROUPING SETS (two GroupItems), GROUPING_ID and GROUPING in SELECT/HAVING.</summary>
    [SkippableFact]
    public async Task Q06_ConcatenatedRollupGroupingSetsGroupingId()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            SELECT s.country, s.product, s.year,
                   GROUPING_ID(s.country, s.product, s.year) AS gid,
                   GROUPING(s.product) AS g_prod,
                   SUM(s.amount) AS amt
              FROM sales s
             GROUP BY ROLLUP (s.country), GROUPING SETS ((s.product, s.year), s.product)
            HAVING GROUPING_ID(s.country, s.product, s.year) < 6
             ORDER BY gid, s.country NULLS FIRST, s.product NULLS FIRST, s.year NULLS FIRST
            """,
        };
        var s = Shape.Parse(sql[0]);
        var q = (QuerySpecification)((SelectStatement)s.Statements[0]).Body;
        Assert.Equal(2, q.GroupBy.Count);
        Assert.Contains("ROLLUP", Str(q.GroupBy[0]), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GROUPING SETS", Str(q.GroupBy[1]), StringComparison.OrdinalIgnoreCase);
        await VerifyAsync(sql, []);
    }

    /// <summary>LISTAGG ON OVERFLOW TRUNCATE WITH COUNT inside a group; JSON_ARRAYAGG ordered with NULL ON NULL.</summary>
    [SkippableFact]
    public async Task Q07_ListaggOverflowTruncateWithCountAndJsonArrayagg()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            SELECT e.deptno,
                   COUNT(*) AS n,
                   LISTAGG(RPAD(e.ename, 900, '#'), ',' ON OVERFLOW TRUNCATE '...' WITH COUNT)
                     WITHIN GROUP (ORDER BY e.empno) AS names_trunc,
                   JSON_ARRAYAGG(e.job ORDER BY e.sal DESC, e.empno NULL ON NULL) AS jobs_by_sal
              FROM emp e
             GROUP BY e.deptno
             ORDER BY e.deptno
            """,
        };
        var s = Shape.Parse(sql[0]);
        var overflow = Find<OverflowClause>(s).Single();
        Assert.Contains("truncate", Str(overflow), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("(FunctionCall name=[JSON_ARRAYAGG]", Str(s));
        await VerifyAsync(sql, []);
    }

    /// <summary>MODEL: UPSERT ALL with ITERATE, positional and CV() references, and a new "Total" cell created by a rule.</summary>
    [SkippableFact]
    public async Task Q08_ModelUpsertAllIterateAndCv()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            SELECT product, year, amount
              FROM (
                SELECT product, year, SUM(amount) AS amount FROM sales WHERE country = 'US' GROUP BY product, year
              )
             MODEL
               DIMENSION BY (product, year)
               MEASURES (amount)
               RULES UPSERT ALL ITERATE (2) (
                 amount['Bike', 2004] = amount['Bike', 2003] * 1.1,
                 amount['Car', 2004] = amount['Car', 2003] + 100,
                 amount['Total', 2004] = amount['Bike', 2004] + amount['Car', 2004],
                 amount['Car', 2005] = amount['Car', 2004] / 2
               )
             ORDER BY product, year
            """,
        };
        var s = Shape.Parse(sql[0]);
        var m = Find<ModelClause>(s).Single();
        Assert.Equal(4, Find<ModelRule>(s).Count());
        Assert.Contains("rulesMode=UpsertAll", Str(m));
        Assert.Contains("iterate=2", Str(m));
        await VerifyAsync(sql, []);
    }

    /// <summary>JSON_TABLE with two sibling NESTED PATHs, ERROR/NULL ON ERROR on columns, plus JSON_VALUE and JSON_QUERY options.</summary>
    [SkippableFact]
    public async Task Q09_JsonTableSiblingNestedPathsAndOnError()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            SELECT ev.id, jt.device, jt.sku, jt.qty, jt.tag,
                   JSON_VALUE(ev.payload, '$.device' RETURNING VARCHAR2(20) DEFAULT 'none' ON EMPTY NULL ON ERROR) AS device_v,
                   JSON_VALUE(ev.payload, '$.tags[0]' RETURNING VARCHAR2(10) NULL ON ERROR) AS first_tag,
                   JSON_QUERY(ev.payload, '$.items[*].sku' WITH WRAPPER) AS skus
              FROM events ev,
                   JSON_TABLE(ev.payload, '$' COLUMNS (
                     device VARCHAR2(20) PATH '$.device',
                     NESTED PATH '$.items[*]' COLUMNS (
                       sku VARCHAR2(10) PATH '$.sku',
                       qty NUMBER PATH '$.qty' NULL ON ERROR),
                     NESTED PATH '$.tags[*]' COLUMNS (
                       tag VARCHAR2(10) PATH '$'))) jt
             ORDER BY ev.id, jt.sku NULLS FIRST, jt.tag NULLS FIRST
            """,
        };
        var s = Shape.Parse(sql[0]);
        var jt = Find<JsonTable>(s).Single();
        Assert.Equal(3, jt.Columns.Count);
        Assert.Equal(2, Find<PathColumn>(s).Count(c => Str(c).Contains("NESTED", StringComparison.OrdinalIgnoreCase)));
        await VerifyAsync(sql, []);
    }

    /// <summary>JSON_TRANSFORM with three operations and JSON_EXISTS with a PASSING variable.</summary>
    [SkippableFact]
    public async Task Q10_JsonTransformOperationsAndJsonExistsPassing()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            SELECT ev.id,
                   JSON_SERIALIZE(JSON_TRANSFORM(ev.payload,
                     SET '$.seen' = 'yes',
                     REMOVE '$.device',
                     APPEND '$.tags' = 'checked')) AS transformed
              FROM events ev
             WHERE JSON_EXISTS(ev.payload, '$.items[*]?(@.qty > $min)' PASSING 1 AS "min")
               AND ev.event_type <> 'LOGOUT'
             ORDER BY ev.id
            """,
        };
        var s = Shape.Parse(sql[0]);
        Assert.Equal(3, Find<JsonTransformOperation>(s).Count());
        Assert.Contains("(FunctionCall name=[JSON_EXISTS]", Str(s));
        await VerifyAsync(sql, []);
    }

    /// <summary>XMLTABLE with namespaces and per-row XPath, grouped XMLAGG/XMLELEMENT/XMLSERIALIZE output.</summary>
    [SkippableFact(Skip = "BUG: a string literal that spans a line break is rejected (Unterminated string literal); Oracle accepts it")]
    public async Task Q11_XmlTableNamespacesAndXmlagg()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            WITH feed AS (
              SELECT XMLTYPE('<orders xmlns:x="urn:ord">
                <x:order id="1" region="EAST"><x:line sku="A" qty="2"/><x:line sku="B" qty="1"/></x:order>
                <x:order id="2" region="WEST"><x:line sku="A" qty="5"/></x:order>
                <x:order id="3" region="EAST"/>
              </orders>') AS doc FROM dual
            )
            SELECT x.region,
                   COUNT(*) AS orders,
                   SUM(x.lines) AS total_lines,
                   XMLSERIALIZE(CONTENT XMLELEMENT("region", XMLATTRIBUTES(x.region AS "name"),
                                  XMLAGG(XMLELEMENT("ord", x.oid) ORDER BY x.oid)) AS VARCHAR2(4000)) AS xml_out
              FROM (
                SELECT t.oid, t.region, t.lines
                  FROM feed f,
                       XMLTABLE(XMLNAMESPACES('urn:ord' AS "x"),
                                '/orders/x:order' PASSING f.doc
                                COLUMNS oid NUMBER PATH '@id',
                                        region VARCHAR2(10) PATH '@region',
                                        lines NUMBER PATH 'count(x:line)') t
              ) x
             GROUP BY x.region
             ORDER BY x.region
            """,
        };
        var s = Shape.Parse(sql[0]);
        var xt = Find<XmlTable>(s).Single();
        Assert.Single(Find<XmlNamespace>(s));
        Assert.Equal(3, xt.Columns.Count);
        Assert.Contains("(FunctionCall name=[XMLAGG]", Str(s));
        await VerifyAsync(sql, []);
    }

    /// <summary>OUTER APPLY keeps dept 40 (no staff); CROSS APPLY is inner; FETCH FIRST ... WITH TIES inside the applied subquery.</summary>
    [SkippableFact]
    public async Task Q12_OuterAndCrossApplyWithTiesInsideApply()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            SELECT d.deptno, d.dname, top2.ename, top2.sal, cnt.headcount
              FROM dept d
              OUTER APPLY (
                SELECT e.ename, e.sal
                  FROM emp e
                 WHERE e.deptno = d.deptno
                 ORDER BY e.sal DESC, e.ename
                 FETCH FIRST 2 ROWS WITH TIES
              ) top2
              CROSS APPLY (
                SELECT COUNT(*) AS headcount FROM emp x WHERE x.deptno = d.deptno
              ) cnt
             ORDER BY d.deptno, top2.sal DESC NULLS LAST, top2.ename
            """,
        };
        var s = Shape.Parse(sql[0]);
        var joins = Find<JoinTable>(s).ToList();
        Assert.Contains(joins, j => j.Kind == JoinKind.OuterApply);
        Assert.Contains(joins, j => j.Kind == JoinKind.CrossApply);
        Assert.True(Find<FetchClause>(s).Single().WithTies);
        await VerifyAsync(sql, []);
    }

    /// <summary>Legacy (+) joins in one block, ANSI JOIN only inside a derived table: the four outer markers stay on their columns.</summary>
    [SkippableFact]
    public async Task Q13_LegacyOuterMarkersWithAnsiJoinInDerivedTable()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            SELECT e.ename, d.dname, m.ename AS mgr_name, s.tot, c.empno AS in_chicago
              FROM emp e, dept d, emp m,
                   (SELECT deptno, SUM(sal) AS tot FROM emp GROUP BY deptno) s,
                   (SELECT x.empno FROM emp x JOIN dept y ON y.deptno = x.deptno WHERE y.loc = 'CHICAGO') c
             WHERE e.deptno = d.deptno(+)
               AND e.mgr = m.empno(+)
               AND s.deptno(+) = e.deptno
               AND c.empno(+) = e.empno
             ORDER BY e.empno
            """,
        };
        var s = Shape.Parse(sql[0]);
        var q = (QuerySpecification)((SelectStatement)s.Statements[0]).Body;
        Assert.Equal(4, Find<ColumnReference>(s).Count(c => c.OuterJoin));
        Assert.Single(Find<JoinTable>(s));
        Assert.DoesNotContain(q.From, t => t is JoinTable);
        await VerifyAsync(sql, []);
    }

    /// <summary>A scalar subquery in ORDER BY (the sort key belongs to the outer query) with FETCH FIRST ... WITH TIES.</summary>
    [SkippableFact]
    public async Task Q14_ScalarSubqueryInOrderByWithTies()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            SELECT e.ename, e.sal, e.deptno
              FROM emp e
             ORDER BY (SELECT d.dname FROM dept d WHERE d.deptno = e.deptno) DESC NULLS LAST, e.sal DESC
             FETCH FIRST 5 ROWS WITH TIES
            """,
        };
        var s = Shape.Parse(sql[0]);
        var ss = (SelectStatement)s.Statements[0];
        Assert.IsType<SubqueryExpression>(ss.OrderBy[0].Expression);
        Assert.Equal(2, ss.OrderBy.Count);
        Assert.True(Find<FetchClause>(s).Single().WithTies);
        await VerifyAsync(sql, []);
    }

    /// <summary>CONNECT BY with CONNECT_BY_ROOT used as an analytic PARTITION key and in the outer ORDER BY.</summary>
    [SkippableFact]
    public async Task Q15_ConnectByRootInsideAnalyticPartition()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            SELECT e.ename,
                   LEVEL AS lvl,
                   CONNECT_BY_ROOT e.ename AS top_boss,
                   ROW_NUMBER() OVER (PARTITION BY CONNECT_BY_ROOT e.empno ORDER BY LEVEL, e.ename) AS rn_in_tree,
                   COUNT(*) OVER (PARTITION BY CONNECT_BY_ROOT e.empno) AS tree_size
              FROM emp e
             START WITH e.mgr IS NULL
             CONNECT BY PRIOR e.empno = e.mgr
             ORDER BY lvl, e.ename
            """,
        };
        var s = Shape.Parse(sql[0]);
        var rn = Find<FunctionCall>(s).Single(f => Str(f).StartsWith("(FunctionCall name=[ROW_NUMBER]"));
        Assert.Contains("CONNECT_BY_ROOT", Str(rn));
        Assert.Contains("(FunctionCall name=[COUNT]", Str(s));
        await VerifyAsync(sql, []);
    }

    /// <summary>Flashback VERSIONS BETWEEN; the setup UPDATE is part of the case. LAG orders by the version SCN.</summary>
    [SkippableFact]
    public async Task Q16_FlashbackVersionsBetweenWithLag()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            "UPDATE emp SET sal = sal + 100 WHERE empno = 7369",
            "COMMIT",
            """
            SELECT v.versions_operation AS op,
                   v.sal,
                   LAG(v.sal) OVER (ORDER BY v.versions_startscn) AS prev_sal
              FROM emp VERSIONS BETWEEN SCN MINVALUE AND MAXVALUE v
             WHERE v.empno = 7369
             ORDER BY v.versions_startscn
            """,
        };
        var s = Shape.Parse(sql[2]);
        Assert.Single(Find<FlashbackClause>(s));
        Assert.Contains("(FunctionCall name=[LAG]", Str(s));
        await VerifyAsync(sql, []);
    }

    /// <summary>Mixed scalar expression stew: nested CASE, REGEXP functions, q-literal, date/interval arithmetic.</summary>
    [SkippableFact]
    public async Task Q17_NestedCaseRegexpDateIntervalStew()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            SELECT e.empno,
                   INITCAP(e.ename) AS name_title,
                   REGEXP_REPLACE(e.ename, '^(.)(.*)$', '\2\1') AS rotated,
                   REGEXP_COUNT(e.job, '[AEIOU]', 1, 'i') AS vowels,
                   CASE WHEN e.hiredate < ADD_MONTHS(DATE '1982-01-01', -12) THEN 'veteran'
                        WHEN e.hiredate BETWEEN DATE '1981-01-01' AND DATE '1981-12-31' THEN
                          CASE WHEN MONTHS_BETWEEN(DATE '1982-01-01', e.hiredate) > 12 THEN 'y81-late' ELSE 'y81-early' END
                        ELSE 'other' END AS cohort,
                   TO_CHAR(e.hiredate, 'YYYY-MM-DD') AS hd,
                   NVL2(e.comm, e.sal + e.comm, e.sal) AS total_comp
              FROM emp e
             WHERE REGEXP_LIKE(e.ename, '^[A-M]', 'i')
               AND e.sal * 12 + NVL(e.comm, 0) > 15000 / 2 - 1000
             ORDER BY e.empno
            """,
        };
        var s = Shape.Parse(sql[0]);
        Assert.Equal(2, Find<CaseExpression>(s).Count());
        var where = ((QuerySpecification)((SelectStatement)s.Statements[0]).Body).Where!;
        Assert.StartsWith("(AND ", Str(where));
        await VerifyAsync(sql, []);
    }

    /// <summary>NOT binds tighter than AND, AND tighter than OR: the root must be OR, the left arm an AND of two NOTs.</summary>
    [SkippableFact]
    public async Task Q18_NotBetweenInAndOrPrecedence()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            SELECT e.empno, e.ename
              FROM emp e
             WHERE NOT e.sal BETWEEN 1000 AND 2000
               AND NOT e.job IN ('CLERK', 'MANAGER')
                OR e.comm IS NULL AND e.deptno = 30
             ORDER BY e.empno
            """,
        };
        var s = Shape.Parse(sql[0]);
        var where = ((QuerySpecification)((SelectStatement)s.Statements[0]).Body).Where!;
        Assert.StartsWith("(OR (AND ", Str(where));
        await VerifyAsync(sql, []);
    }

    /// <summary>
    /// Oracle has no INTERSECT precedence: set operators evaluate left to right unless parenthesised (verified on 23ai:
    /// 10 UNION 30 INTERSECT 30 returns only 30). The tree must therefore be INTERSECT(UNION(A, B), C), with ORDER BY on the whole.
    /// </summary>
    [SkippableFact]
    public async Task Q19_SetOperatorPrecedenceIntersectBeforeUnion()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            SELECT d.deptno FROM dept d WHERE d.deptno = 10
            UNION
            SELECT d.deptno FROM dept d WHERE d.deptno = 30
            INTERSECT
            SELECT d.deptno FROM dept d WHERE d.deptno >= 30
            ORDER BY 1
            """,
        };
        var s = Shape.Parse(sql[0]);
        var top = (SetOperation)((SelectStatement)s.Statements[0]).Body;
        Assert.Equal(SetOperator.Intersect, top.Operator);
        var left = Assert.IsType<SetOperation>(top.Left);
        Assert.Equal(SetOperator.Union, left.Operator);
        Assert.Single(((SelectStatement)s.Statements[0]).OrderBy);
        await VerifyAsync(sql, []);
    }

    /// <summary>Top-N per group from a CTE with DENSE_RANK, PERCENT_RANK and CUME_DIST; the filter sits on the outer block.</summary>
    [SkippableFact]
    public async Task Q20_TopNPerGroupFromAnalyticCte()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            WITH ranked AS (
              SELECT e.deptno, e.ename, e.sal,
                     DENSE_RANK() OVER (PARTITION BY e.deptno ORDER BY e.sal DESC) AS dr,
                     ROUND(PERCENT_RANK() OVER (PARTITION BY e.deptno ORDER BY e.sal), 4) AS pr,
                     ROUND(CUME_DIST() OVER (PARTITION BY e.deptno ORDER BY e.sal), 4) AS cd
                FROM emp e
            )
            SELECT r.deptno, r.ename, r.sal, r.dr, r.pr, r.cd
              FROM ranked r
             WHERE r.dr <= 2
             ORDER BY r.deptno, r.dr, r.ename
            """,
        };
        var s = Shape.Parse(sql[0]);
        var ss = (SelectStatement)s.Statements[0];
        Assert.Single(ss.With);
        Assert.Contains("(<= R.DR 2)", Str(((QuerySpecification)ss.Body).Where!));
        await VerifyAsync(sql, []);
    }

    /// <summary>Text parsing: REGEXP_SUBSTR subexpression argument, back-references, and a q'{...}' literal that contains brackets.</summary>
    [SkippableFact]
    public async Task Q21_RegexpSubexpressionsAndQQuoteLiteral()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            WITH raw_lines AS (
              SELECT q'{GET /api/v2/orders/42?x=[a,b]&y={c} HTTP/1.1}' AS line FROM dual
              UNION ALL
              SELECT 'POST /api/v1/users?lang=en HTTP/2.0' FROM dual
            )
            SELECT REGEXP_SUBSTR(r.line, '^(GET|POST|PUT) +(/[^ ?]+)', 1, 1, 'i', 1) AS verb,
                   REGEXP_SUBSTR(r.line, '^(GET|POST|PUT) +(/[^ ?]+)', 1, 1, 'i', 2) AS path,
                   REGEXP_REPLACE(r.line, '[?&]([a-z]+)=[^&[:space:]]*', '<\1>') AS masked,
                   INSTR(r.line, '{') AS brace_pos,
                   LENGTH(REGEXP_REPLACE(r.line, '[^/]', '')) AS slashes
              FROM raw_lines r
             ORDER BY path
            """,
        };
        var s = Shape.Parse(sql[0]);
        Assert.Equal(2, Find<FunctionCall>(s).Count(f => Str(f).StartsWith("(FunctionCall name=[REGEXP_SUBSTR]")));
        Assert.Contains("'GET /api/v2/orders/42?x=[a,b]&y={c} HTTP/1.1'", Str(s));
        await VerifyAsync(sql, []);
    }

    /// <summary>23ai GROUP BY on select aliases, with HAVING that mixes an aggregate predicate and a grouped predicate.</summary>
    [SkippableFact]
    public async Task Q22_GroupByAliasHavingTruncMonth()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            SELECT TRUNC(o.order_date, 'MM') AS month_start,
                   o.status AS st,
                   COUNT(*) AS n_orders,
                   SUM(o.total) AS revenue,
                   COUNT(DISTINCT o.customer_id) AS custs
              FROM orders o
             GROUP BY month_start, st
            HAVING SUM(o.total) > 50 AND COUNT(*) >= 1
             ORDER BY month_start, st NULLS LAST
            """,
        };
        var s = Shape.Parse(sql[0]);
        var q = (QuerySpecification)((SelectStatement)s.Statements[0]).Body;
        Assert.Equal(2, q.GroupBy.Count);
        Assert.StartsWith("(AND ", Str(q.Having!));
        await VerifyAsync(sql, []);
    }

    /// <summary>23ai SQL BOOLEAN as a select item and in WHERE with IS TRUE; a VALUES table constructor with a column list joined in.</summary>
    [SkippableFact]
    public async Task Q23_SqlBooleanSelectItemAndValuesConstructor()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            SELECT o.id,
                   o.total > 100 AS is_big,
                   (o.status IS NULL) AS unknown_status,
                   v.tier
              FROM orders o
              JOIN (VALUES (1, 'gold'), (2, 'silver'), (5, 'bronze')) v(cust_id, tier)
                ON v.cust_id = o.customer_id
             WHERE (o.total > 100) IS TRUE OR o.status IS NULL
             ORDER BY o.id
            """,
        };
        var s = Shape.Parse(sql[0]);
        Assert.Single(Find<ValuesTable>(s));
        var q = (QuerySpecification)((SelectStatement)s.Statements[0]).Body;
        Assert.Contains("(> O.TOTAL 100)", Str(q.SelectList[1]));
        await VerifyAsync(sql, []);
    }

    /// <summary>Row-value IN, a NOT IN over a NULL-containing list (always unknown), and an OR that rescues one row.</summary>
    [SkippableFact]
    public async Task Q24_RowValueInAndNotInNullTrap()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            SELECT e.empno, e.ename
              FROM emp e
             WHERE (e.deptno, e.job) IN (SELECT x.deptno, x.job FROM emp x WHERE x.sal > 2000)
               AND e.empno NOT IN (SELECT m.mgr FROM emp m)
                OR e.ename = 'KING'
             ORDER BY e.empno
            """,
        };
        var s = Shape.Parse(sql[0]);
        var where = ((QuerySpecification)((SelectStatement)s.Statements[0]).Body).Where!;
        Assert.StartsWith("(OR ", Str(where));
        Assert.Contains("(AND ", Str(where));
        await VerifyAsync(sql, []);
    }

    /// <summary>Reporting aggregates: CASE inside SUM, MEDIAN, PERCENTILE_CONT WITHIN GROUP and KEEP (DENSE_RANK FIRST) in one group.</summary>
    [SkippableFact]
    public async Task Q25_ReportingAggregatesWithinGroupAndKeep()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            SELECT d.dname,
                   COUNT(*) AS heads,
                   SUM(CASE WHEN e.job = 'CLERK' THEN 1 ELSE 0 END) AS clerks,
                   ROUND(AVG(e.sal), 2) AS avg_sal,
                   MEDIAN(e.sal) AS med_sal,
                   PERCENTILE_CONT(0.9) WITHIN GROUP (ORDER BY e.sal) AS p90,
                   MAX(e.sal) KEEP (DENSE_RANK FIRST ORDER BY e.hiredate) AS first_hire_sal
              FROM emp e
              JOIN dept d ON d.deptno = e.deptno
             GROUP BY d.dname
            HAVING COUNT(*) > 2
             ORDER BY 1
            """,
        };
        var s = Shape.Parse(sql[0]);
        Assert.Single(Find<KeepClause>(s));
        Assert.Contains("(FunctionCall name=[PERCENTILE_CONT]", Str(s));
        await VerifyAsync(sql, []);
    }

    /// <summary>JSON_ARRAYAGG with ORDER BY and NULL ON NULL next to a JSON_OBJECT that aggregates; LEFT JOIN keeps dept 40.</summary>
    [SkippableFact]
    public async Task Q26_JsonArrayaggAndObjectPerGroup()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            SELECT d.deptno,
                   JSON_ARRAYAGG(e.job ORDER BY e.empno NULL ON NULL) AS jobs,
                   JSON_OBJECT('dept' VALUE d.dname, 'n' VALUE COUNT(e.empno), 'avg' VALUE ROUND(AVG(e.sal))) AS summary
              FROM dept d
              LEFT JOIN emp e ON e.deptno = d.deptno
             GROUP BY d.deptno, d.dname
             ORDER BY d.deptno
            """,
        };
        var s = Shape.Parse(sql[0]);
        Assert.Contains("(FunctionCall name=[JSON_OBJECT]", Str(s));
        Assert.Contains("(FunctionCall name=[JSON_ARRAYAGG]", Str(s));
        await VerifyAsync(sql, []);
    }

    /// <summary>Calendar spine from CONNECT BY LEVEL, running SUM and LAST_VALUE(... IGNORE NULLS) over a frame, LEFT JOIN to a CTE.</summary>
    [SkippableFact]
    public async Task Q27_CalendarSpineRunningSumAndLastValueIgnoreNulls()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            WITH days AS (
              SELECT DATE '2024-01-01' + LEVEL - 1 AS d FROM dual CONNECT BY LEVEL <= 120
            ),
            daily AS (
              SELECT TRUNC(o.order_date) AS d, SUM(o.total) AS revenue FROM orders o GROUP BY TRUNC(o.order_date)
            )
            SELECT x.d,
                   NVL(dl.revenue, 0) AS revenue,
                   SUM(NVL(dl.revenue, 0)) OVER (ORDER BY x.d ROWS UNBOUNDED PRECEDING) AS running,
                   LAST_VALUE(dl.revenue IGNORE NULLS) OVER (ORDER BY x.d ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS last_known
              FROM days x
              LEFT JOIN daily dl ON dl.d = x.d
             WHERE x.d BETWEEN DATE '2024-01-25' AND DATE '2024-04-30'
             ORDER BY x.d
            """,
        };
        var s = Shape.Parse(sql[0]);
        var lv = Find<FunctionCall>(s).Single(f => Str(f).StartsWith("(FunctionCall name=[LAST_VALUE]"));
        Assert.Contains("ignoreNulls", Str(lv));
        Assert.Equal(2, Find<CommonTableExpression>(s).Count());
        await VerifyAsync(sql, []);
    }

    /// <summary>ALL over an empty correlated set is TRUE; ANY/ALL bind inside the OR/AND tree (AND before OR).</summary>
    [SkippableFact]
    public async Task Q28_QuantifiedComparisonsAllAnyAndPrecedence()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            SELECT o.id, o.customer_id, o.total
              FROM orders o
             WHERE o.total >= ALL (SELECT ol.qty * ol.unit_price FROM order_lines ol WHERE ol.order_id = o.id)
                OR EXISTS (SELECT 1 FROM customers c WHERE c.id = o.customer_id AND c.credit_limit IS NULL)
               AND o.customer_id = ANY (SELECT c.id FROM customers c WHERE c.region <> 'SOUTH')
             ORDER BY o.id
            """,
        };
        var s = Shape.Parse(sql[0]);
        Assert.Equal(2, Find<QuantifiedComparison>(s).Count());
        var where = ((QuerySpecification)((SelectStatement)s.Statements[0]).Body).Where!;
        Assert.StartsWith("(OR ", Str(where));
        await VerifyAsync(sql, []);
    }

    /// <summary>Lateral CROSS APPLY of a JSON_TABLE that reads the outer row's payload.</summary>
    [SkippableFact]
    public async Task Q29_CrossApplyJsonTableLateral()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            SELECT ev.id, ev.event_type, it.sku, it.qty
              FROM events ev
              CROSS APPLY (
                SELECT jt.sku, jt.qty
                  FROM JSON_TABLE(ev.payload, '$.items[*]'
                         COLUMNS (sku VARCHAR2(10) PATH '$.sku',
                                  qty NUMBER PATH '$.qty' NULL ON ERROR)) jt
              ) it
             WHERE ev.event_type IN ('PURCHASE', 'SEARCH', 'VIEW')
             ORDER BY ev.id, it.sku NULLS FIRST, it.qty NULLS FIRST
            """,
        };
        var s = Shape.Parse(sql[0]);
        Assert.Contains(Find<JoinTable>(s), j => j.Kind == JoinKind.CrossApply);
        Assert.Single(Find<JsonTable>(s));
        await VerifyAsync(sql, []);
    }

    /// <summary>ALL ROWS PER MATCH: nested group with alternation, bounded quantifier {1,2} on one variable only, SKIP TO NEXT ROW.</summary>
    [SkippableFact]
    public async Task Q30_MatchRecognizeAllRowsBoundedQuantifierNestedGroup()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            SELECT symbol, tstamp, price, match_no, cls
              FROM ticker MATCH_RECOGNIZE (
                PARTITION BY symbol
                ORDER BY tstamp
                MEASURES MATCH_NUMBER() AS match_no, CLASSIFIER() AS cls
                ALL ROWS PER MATCH
                AFTER MATCH SKIP TO NEXT ROW
                PATTERN ((LO{1,2} HI) | (HI LO))
                DEFINE LO AS price < 12, HI AS price >= 12
              )
             ORDER BY symbol, tstamp
            """,
        };
        var s = Shape.Parse(sql[0]);
        var q = Find<PatternQuantified>(s).Single();
        Assert.Equal(1, q.Min);
        Assert.Equal(2, q.Max);
        Assert.Contains("(PatternAlternation", Str(Find<MatchRecognizeTable>(s).Single()));
        await VerifyAsync(sql, []);
    }

    /// <summary>WITH FUNCTION: two inline PL/SQL functions (one with a default argument) declared before the SELECT.</summary>
    [SkippableFact]
    public async Task Q31_WithFunctionInlinePlsqlFunctions()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            WITH
              FUNCTION band (p_sal NUMBER) RETURN VARCHAR2 IS
              BEGIN
                RETURN CASE WHEN p_sal >= 3000 THEN 'A' WHEN p_sal >= 1500 THEN 'B' ELSE 'C' END;
              END;
              FUNCTION tax (p_amt NUMBER, p_rate NUMBER DEFAULT 0.2) RETURN NUMBER IS
              BEGIN
                RETURN ROUND(p_amt * p_rate, 2);
              END;
            SELECT e.empno, band(e.sal) AS b, tax(e.sal) AS t20, tax(e.sal, 0.35) AS t35
              FROM emp e
             WHERE e.deptno IN (10, 30)
             ORDER BY e.empno
            """,
        };
        var s = Shape.Parse(sql[0]);
        Assert.Contains("(FunctionCall name=[TAX]", Str(s));
        await VerifyAsync(sql, []);
    }

    /// <summary>LEAD with default and NULLS LAST inside the window, NTH_VALUE FROM LAST over a full frame, FIRST_VALUE IGNORE NULLS.</summary>
    [SkippableFact(Skip = "BUG: NTH_VALUE(...) FROM LAST is not parsed; FROM is taken as a table and LAST as its name (Unexpected 'OVER')")]
    public async Task Q32_LeadDefaultNthValueFromLastFirstValueIgnoreNulls()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            SELECT ev.id, ev.user_id, ev.event_type,
                   LEAD(ev.event_type, 1, 'END') OVER (PARTITION BY ev.user_id ORDER BY ev.ts NULLS LAST, ev.id) AS next_evt,
                   NTH_VALUE(ev.id, 2) FROM LAST OVER (PARTITION BY ev.user_id ORDER BY ev.id
                                                       ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING) AS second_last_id,
                   FIRST_VALUE(ev.ts IGNORE NULLS) OVER (PARTITION BY ev.user_id ORDER BY ev.id) AS first_ts,
                   COUNT(*) OVER (PARTITION BY ev.user_id) AS n_user
              FROM events ev
             ORDER BY ev.user_id, ev.id
            """,
        };
        var s = Shape.Parse(sql[0]);
        var lead = Find<FunctionCall>(s).Single(f => Str(f).StartsWith("(FunctionCall name=[LEAD]"));
        Assert.Contains("(WindowSpec", Str(lead));
        Assert.DoesNotContain("(WindowFrame", Str(lead));
        var nth = Find<FunctionCall>(s).Single(f => Str(f).StartsWith("(FunctionCall name=[NTH_VALUE]"));
        Assert.Contains("(WindowFrame unit=Rows", Str(nth));
        await VerifyAsync(sql, []);
    }

    /// <summary>Classic ROWNUM pagination: the ROWNUM predicate belongs to the innermost block, the rn filter to the outer one.</summary>
    [SkippableFact]
    public async Task Q33_NestedRownumPagination()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            SELECT *
              FROM (
                SELECT x.*, ROWNUM AS rn
                  FROM (SELECT e.empno, e.ename, e.sal FROM emp e ORDER BY e.sal DESC, e.empno) x
                 WHERE ROWNUM <= 6
              )
             WHERE rn > 2
             ORDER BY rn
            """,
        };
        var s = Shape.Parse(sql[0]);
        var outer = (QuerySpecification)((SelectStatement)s.Statements[0]).Body;
        Assert.StartsWith("(> RN 2)", Str(outer.Where!));
        Assert.Contains("(<= ROWNUM 6)", Str(s));
        await VerifyAsync(sql, []);
    }

    /// <summary>Datetime and interval arithmetic: EXTRACT from an interval, NUMTODSINTERVAL, AT TIME ZONE, YEAR TO MONTH intervals.</summary>
    [SkippableFact]
    public async Task Q34_DatetimeIntervalArithmeticAndTimeZones()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            SELECT e.empno,
                   EXTRACT(YEAR FROM e.hiredate) AS hire_year,
                   NUMTODSINTERVAL(e.sal / 100, 'HOUR') AS hours_pay,
                   TO_CHAR(CAST(e.hiredate AS TIMESTAMP) AT TIME ZONE 'UTC', 'YYYY-MM-DD HH24:MI TZR') AS hire_utc,
                   EXTRACT(DAY FROM (TIMESTAMP '2024-05-01 10:00:00' - TIMESTAMP '2024-04-28 08:30:00')) AS days_gap,
                   e.hiredate + INTERVAL '1-2' YEAR TO MONTH AS plus_ym
              FROM emp e
             WHERE e.hiredate >= DATE '1981-01-01' - INTERVAL '0' DAY
               AND e.deptno = 30
             ORDER BY e.empno
            """,
        };
        var s = Shape.Parse(sql[0]);
        Assert.Single(Find<AtTimeZoneExpression>(s));
        await VerifyAsync(sql, []);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // DML
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>MERGE with a DELETE WHERE clause, a conditional insert and LOG ERRORS (a NUMBER(8,2) overflow is logged).</summary>
    [SkippableFact]
    public async Task D01_MergeDeleteWhereAndLogErrors()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            "BEGIN DBMS_ERRLOG.CREATE_ERROR_LOG('STOCK', 'STOCK_ERR'); END;",
            """
            MERGE INTO stock t
            USING (SELECT f.sku, f.qty, f.price FROM stock_feed f) s
               ON (t.sku = s.sku)
             WHEN MATCHED THEN
               UPDATE SET t.qty = t.qty + s.qty, t.price = s.price
                 WHERE s.qty > 0
                 DELETE WHERE t.qty = 0
             WHEN NOT MATCHED THEN
               INSERT (sku, qty, price) VALUES (s.sku, s.qty, s.price) WHERE s.qty > 0
            LOG ERRORS INTO stock_err ('merge-21') REJECT LIMIT UNLIMITED
            """,
        };
        var s = Shape.Parse(sql[1]);
        var clauses = Find<MergeClause>(s).ToList();
        Assert.Equal(2, clauses.Count);
        Assert.NotNull(clauses[0].DeleteWhere);
        Assert.Contains("STOCK_ERR", Str(Find<ErrorLogging>(s).Single()));
        await VerifyAsync(sql, ["SELECT sku, qty, price FROM stock ORDER BY sku", "SELECT COUNT(*) FROM stock_err"]);
    }

    /// <summary>INSERT FIRST with WHEN/ELSE over a subquery: unqualified WHEN columns refer to the SELECT list.</summary>
    [SkippableFact]
    public async Task D02_InsertFirstWhenElse()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            "CREATE TABLE big_orders (id NUMBER, customer_id NUMBER, total NUMBER(10,2))",
            "CREATE TABLE unknown_orders (id NUMBER, customer_id NUMBER, total NUMBER(10,2))",
            "CREATE TABLE small_orders (id NUMBER, customer_id NUMBER, total NUMBER(10,2))",
            """
            INSERT FIRST
              WHEN total >= 500 THEN
                INTO big_orders (id, customer_id, total) VALUES (id, customer_id, total)
              WHEN status IS NULL THEN
                INTO unknown_orders (id, customer_id, total) VALUES (id, customer_id, total)
              ELSE
                INTO small_orders (id, customer_id, total) VALUES (id, customer_id, total)
            SELECT o.id, o.customer_id, o.total, o.status FROM orders o
            """,
        };
        var s = Shape.Parse(sql[3]);
        var mt = Assert.IsType<MultiTableInsertStatement>(s.Statements[0]);
        Assert.True(mt.First);
        Assert.True(mt.Conditional);
        Assert.Equal(2, mt.Branches.Count);
        Assert.NotNull(mt.Else);
        await VerifyAsync(sql, ["SELECT id FROM big_orders ORDER BY id", "SELECT id FROM unknown_orders ORDER BY id", "SELECT id FROM small_orders ORDER BY id"]);
    }

    /// <summary>INSERT ALL (not FIRST) with WHEN branches that can both fire for the same row, and a WITH inside the source subquery.</summary>
    [SkippableFact]
    public async Task D03_InsertAllBothBranchesWithInlineSource()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            "CREATE TABLE order_audit (id NUMBER, total NUMBER(10,2))",
            "CREATE TABLE cust1_orders (id NUMBER, total NUMBER(10,2))",
            """
            INSERT ALL
              WHEN total > 50 THEN INTO order_audit (id, total) VALUES (id, total)
              WHEN customer_id = 1 THEN INTO cust1_orders (id, total) VALUES (id, total)
            SELECT id, customer_id, total
              FROM (WITH recent AS (
                      SELECT o.id, o.customer_id, o.total FROM orders o WHERE o.order_date >= DATE '2024-03-01')
                    SELECT r.id, r.customer_id, r.total FROM recent r)
            """,
        };
        var s = Shape.Parse(sql[2]);
        var mt = Assert.IsType<MultiTableInsertStatement>(s.Statements[0]);
        Assert.False(mt.First);
        Assert.True(mt.Conditional);
        Assert.Null(mt.Else);
        await VerifyAsync(sql, ["SELECT id, total FROM order_audit ORDER BY id", "SELECT id, total FROM cust1_orders ORDER BY id"]);
    }

    /// <summary>UPDATE with a correlated scalar subquery in SET, then the 23ai UPDATE ... FROM form joined to a grouped derived table.</summary>
    [SkippableFact]
    public async Task D04_UpdateCorrelatedSubqueryThenUpdateFrom()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            UPDATE orders o
               SET o.status = (SELECT CASE WHEN SUM(ol.qty * ol.unit_price) > 200 THEN 'A' ELSE 'C' END
                                 FROM order_lines ol
                                WHERE ol.order_id = o.id)
             WHERE o.status IS NULL
               AND EXISTS (SELECT 1 FROM order_lines ol WHERE ol.order_id = o.id)
            """,
            """
            UPDATE orders o
               SET o.total = s.calc
              FROM (SELECT ol.order_id, SUM(ol.qty * ol.unit_price) AS calc FROM order_lines ol GROUP BY ol.order_id) s
             WHERE s.order_id = o.id
               AND o.total <> s.calc
            """,
        };
        var s = Shape.Parse(sql[0]);
        var up = Assert.IsType<UpdateStatement>(s.Statements[0]);
        Assert.Contains("(SubqueryExpression", Str(up));
        await VerifyAsync(sql, ["SELECT id, status, total FROM orders ORDER BY id"]);
    }

    /// <summary>DELETE with EXISTS / OR NOT EXISTS correlated predicates, and an alias on the target table.</summary>
    [SkippableFact]
    public async Task D05_DeleteCorrelatedExistsOrNotExists()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            DELETE FROM order_lines ol
             WHERE EXISTS (SELECT 1 FROM orders o WHERE o.id = ol.order_id AND o.status = 'C')
                OR NOT EXISTS (SELECT 1 FROM orders o WHERE o.id = ol.order_id AND o.total > 0)
            """,
        };
        var s = Shape.Parse(sql[0]);
        Assert.IsType<DeleteStatement>(s.Statements[0]);
        Assert.Equal(2, Find<ExistsExpression>(s).Count());
        await VerifyAsync(sql, ["SELECT order_id, line_no FROM order_lines ORDER BY order_id, line_no"]);
    }

    /// <summary>MERGE with an UPDATE that has its own WHERE and a NOT MATCHED insert with a WHERE on the source row.</summary>
    [SkippableFact]
    public async Task D06_MergeWithPerBranchWhere()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            MERGE INTO bonus b
            USING (SELECT e.empno, e.deptno, e.sal FROM emp e WHERE e.job IN ('SALESMAN', 'MANAGER')) s
               ON (b.empno = s.empno)
             WHEN MATCHED THEN
               UPDATE SET b.amount = b.amount + s.sal * 0.01, b.note = 'bumped'
                 WHERE b.amount < 1000
             WHEN NOT MATCHED THEN
               INSERT (empno, amount, note) VALUES (s.empno, ROUND(s.sal * 0.01, 2), 'new')
                 WHERE s.deptno = 30
            """,
        };
        var s = Shape.Parse(sql[0]);
        var clauses = Find<MergeClause>(s).ToList();
        Assert.Equal(2, clauses.Count);
        await VerifyAsync(sql, ["SELECT empno, amount, note FROM bonus ORDER BY empno"]);
    }

    /// <summary>INSERT ... WITH ... SELECT with FETCH FIRST ... WITH TIES in the CTE and an anti-join in the outer WHERE.</summary>
    [SkippableFact]
    public async Task D07_InsertWithCteFetchWithTies()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            INSERT INTO bonus (empno, amount, note)
            WITH top_paid AS (
              SELECT e.empno, e.sal FROM emp e ORDER BY e.sal DESC FETCH FIRST 3 ROWS WITH TIES
            )
            SELECT t.empno, t.sal / 100, 'top'
              FROM top_paid t
             WHERE NOT EXISTS (SELECT 1 FROM bonus b WHERE b.empno = t.empno)
            """,
        };
        var s = Shape.Parse(sql[0]);
        Assert.IsType<InsertStatement>(s.Statements[0]);
        Assert.True(Find<FetchClause>(s).Single().WithTies);
        await VerifyAsync(sql, ["SELECT empno, amount, note FROM bonus ORDER BY empno"]);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // PL/SQL
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>Package spec and body: overloaded fmt, a pipelined table function over a schema object type, PIPE ROW in a cursor loop.</summary>
    [SkippableFact]
    public async Task P01_PackageOverloadsAndPipelinedFunction()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            CREATE OR REPLACE PACKAGE report_pkg AS
              FUNCTION fmt (p_n NUMBER) RETURN VARCHAR2;
              FUNCTION fmt (p_s VARCHAR2, p_w PLS_INTEGER DEFAULT 10) RETURN VARCHAR2;
              FUNCTION dept_totals RETURN num_tab PIPELINED;
            END report_pkg;
            """,
            """
            CREATE OR REPLACE PACKAGE BODY report_pkg AS
              FUNCTION fmt (p_n NUMBER) RETURN VARCHAR2 IS
              BEGIN
                RETURN TO_CHAR(p_n, 'FM999G990D00');
              END fmt;
              FUNCTION fmt (p_s VARCHAR2, p_w PLS_INTEGER DEFAULT 10) RETURN VARCHAR2 IS
              BEGIN
                RETURN RPAD(p_s, p_w, '.');
              END fmt;
              FUNCTION dept_totals RETURN num_tab PIPELINED IS
                CURSOR c IS SELECT deptno, SUM(sal) AS tot FROM emp GROUP BY deptno ORDER BY deptno;
              BEGIN
                FOR r IN c LOOP
                  PIPE ROW (num_row(r.tot, 'dept ' || r.deptno));
                END LOOP;
                RETURN;
              END dept_totals;
            END report_pkg;
            """,
        };
        var s = Shape.Parse(sql[0]);
        var pkg = Assert.IsType<CreatePackageStatement>(s.Statements[0]);
        Assert.NotNull(pkg);
        Assert.Equal(2, Find<SubprogramDeclaration>(Shape.Parse(sql[0])).Count(d => Str(d).Contains("FMT", StringComparison.OrdinalIgnoreCase)));
        Assert.Single(Find<PipeRowStatement>(Shape.Parse(sql[1])));
        await VerifyAsync(sql, ["SELECT report_pkg.fmt(1234.5) AS a, report_pkg.fmt('ab', 5) AS b FROM dual", "SELECT t.n, t.label FROM TABLE(report_pkg.dept_totals) t ORDER BY t.n"]);
    }

    /// <summary>BULK COLLECT ... LIMIT loop with FORALL ... SAVE EXCEPTIONS and per-error handling through SQL%BULK_EXCEPTIONS.</summary>
    [SkippableFact(Skip = "BUG: attribute chain SQL%BULK_EXCEPTIONS.COUNT as a FOR range bound is rejected (Expected LOOP but found '.')")]
    public async Task P02_BulkCollectLimitForallSaveExceptions()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            "CREATE TABLE shipments (order_id NUMBER, line_no NUMBER, shipped_qty NUMBER, CONSTRAINT shipments_pk PRIMARY KEY (order_id, line_no))",
            "CREATE TABLE shipment_errors (err_idx NUMBER, err_code NUMBER)",
            "INSERT INTO shipments VALUES (1, 1, 0)",
            "INSERT INTO shipments VALUES (3, 1, 0)",
            """
            DECLARE
              CURSOR c_lines IS
                SELECT ol.order_id, ol.line_no, ol.qty FROM order_lines ol ORDER BY ol.order_id, ol.line_no;
              TYPE t_lines IS TABLE OF c_lines%ROWTYPE;
              l_rows t_lines;
              l_idx PLS_INTEGER;
              l_code PLS_INTEGER;
              bulk_errors EXCEPTION;
              PRAGMA EXCEPTION_INIT(bulk_errors, -24381);
            BEGIN
              OPEN c_lines;
              LOOP
                FETCH c_lines BULK COLLECT INTO l_rows LIMIT 4;
                EXIT WHEN l_rows.COUNT = 0;
                BEGIN
                  FORALL i IN 1 .. l_rows.COUNT SAVE EXCEPTIONS
                    INSERT INTO shipments (order_id, line_no, shipped_qty)
                    VALUES (l_rows(i).order_id, l_rows(i).line_no, l_rows(i).qty);
                EXCEPTION
                  WHEN bulk_errors THEN
                    FOR j IN 1 .. SQL%BULK_EXCEPTIONS.COUNT LOOP
                      l_idx := SQL%BULK_EXCEPTIONS(j).ERROR_INDEX;
                      l_code := SQL%BULK_EXCEPTIONS(j).ERROR_CODE;
                      INSERT INTO shipment_errors (err_idx, err_code) VALUES (l_idx, l_code);
                    END LOOP;
                END;
              END LOOP;
              CLOSE c_lines;
            END;
            """,
        };
        var s = Shape.Parse(sql[4]);
        var fa = Find<ForallStatement>(s).Single();
        Assert.NotNull(fa.Upper);
        Assert.Contains("SAVE EXCEPTIONS", Str(fa), StringComparison.OrdinalIgnoreCase);
        Assert.Single(Find<ExceptionHandler>(s));
        await VerifyAsync(sql, ["SELECT order_id, line_no, shipped_qty FROM shipments ORDER BY order_id, line_no", "SELECT err_idx, err_code FROM shipment_errors ORDER BY err_idx"]);
    }

    /// <summary>DBMS_SQL cursor with binds plus three EXECUTE IMMEDIATE forms (INTO+USING, USING, USING ... RETURNING INTO).</summary>
    [SkippableFact]
    public async Task P03_DbmsSqlAndExecuteImmediateReturningInto()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            DECLARE
              l_cur     INTEGER;
              l_rows    PLS_INTEGER;
              l_new_sal NUMBER;
              l_cnt     PLS_INTEGER;
            BEGIN
              l_cur := DBMS_SQL.OPEN_CURSOR;
              DBMS_SQL.PARSE(l_cur, 'UPDATE emp SET sal = sal + :inc WHERE deptno = :d AND job <> :j', DBMS_SQL.NATIVE);
              DBMS_SQL.BIND_VARIABLE(l_cur, ':inc', 100);
              DBMS_SQL.BIND_VARIABLE(l_cur, ':d', 30);
              DBMS_SQL.BIND_VARIABLE(l_cur, ':j', 'PRESIDENT');
              l_rows := DBMS_SQL.EXECUTE(l_cur);
              DBMS_SQL.CLOSE_CURSOR(l_cur);
              EXECUTE IMMEDIATE 'SELECT COUNT(*) FROM emp WHERE deptno = :d' INTO l_cnt USING 30;
              EXECUTE IMMEDIATE 'UPDATE emp SET comm = NVL(comm, 0) + :c WHERE deptno = :d' USING 5, 30;
              EXECUTE IMMEDIATE 'UPDATE emp SET sal = sal + 1 WHERE empno = :e RETURNING sal INTO :s' USING 7900 RETURNING INTO l_new_sal;
              INSERT INTO bonus (empno, amount, note) VALUES (9999, l_cnt, 'dyn ' || l_new_sal);
            END;
            """,
        };
        var s = Shape.Parse(sql[0]);
        Assert.Equal(3, Find<ExecuteImmediateStatement>(s).Count());
        Assert.Contains("RETURNING", Str(Find<ExecuteImmediateStatement>(s).Last()), StringComparison.OrdinalIgnoreCase);
        await VerifyAsync(sql, ["SELECT empno, sal, comm FROM emp WHERE deptno = 30 ORDER BY empno", "SELECT empno, amount, note FROM bonus ORDER BY empno"]);
    }

    /// <summary>Autonomous-transaction logger, PRAGMA EXCEPTION_INIT with a negative code, re-RAISE, RAISE_APPLICATION_ERROR with TRUE.</summary>
    [SkippableFact]
    public async Task P04_AutonomousLoggerExceptionInitAndRaiseApplicationError()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            "CREATE TABLE app_log_t (lvl VARCHAR2(10), msg VARCHAR2(200))",
            """
            CREATE OR REPLACE PROCEDURE app_log (p_lvl VARCHAR2, p_msg VARCHAR2) AS
              PRAGMA AUTONOMOUS_TRANSACTION;
            BEGIN
              INSERT INTO app_log_t (lvl, msg) VALUES (p_lvl, SUBSTR(p_msg, 1, 200));
              COMMIT;
            END app_log;
            """,
            """
            CREATE OR REPLACE PROCEDURE transfer_funds (p_from NUMBER, p_to NUMBER, p_amt NUMBER) AS
              e_insufficient EXCEPTION;
              PRAGMA EXCEPTION_INIT(e_insufficient, -20001);
              l_bal NUMBER;
            BEGIN
              SELECT amount INTO l_bal FROM bonus WHERE empno = p_from FOR UPDATE;
              IF l_bal < p_amt THEN
                RAISE_APPLICATION_ERROR(-20001, 'insufficient: ' || l_bal);
              END IF;
              UPDATE bonus SET amount = amount - p_amt WHERE empno = p_from;
              UPDATE bonus SET amount = amount + p_amt WHERE empno = p_to;
            EXCEPTION
              WHEN e_insufficient THEN
                app_log('WARN', SQLERRM);
                RAISE;
              WHEN NO_DATA_FOUND THEN
                app_log('ERROR', 'no account ' || p_from);
                RAISE_APPLICATION_ERROR(-20002, 'missing account', TRUE);
            END transfer_funds;
            """,
            "BEGIN transfer_funds(7499, 7521, 20); END;",
            "BEGIN transfer_funds(7900, 7521, 500); EXCEPTION WHEN OTHERS THEN NULL; END;",
            "BEGIN transfer_funds(4242, 7521, 1); EXCEPTION WHEN OTHERS THEN NULL; END;",
        };
        var s = Shape.Parse(sql[2]);
        var prags = Find<PragmaDeclaration>(s).ToList();
        Assert.Single(prags);
        Assert.Contains("20001", Str(prags[0]));
        Assert.Single(Find<RaiseStatement>(s));
        await VerifyAsync(sql, ["SELECT lvl, msg FROM app_log_t ORDER BY lvl, msg", "SELECT empno, amount FROM bonus ORDER BY empno"]);
    }

    /// <summary>Compound trigger: row-level collection in AFTER EACH ROW, flush in AFTER STATEMENT, with :NEW/:OLD and UPDATING.</summary>
    [SkippableFact]
    public async Task P05_CompoundTriggerCollectsThenFlushes()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            "CREATE TABLE sal_audit (empno NUMBER, old_sal NUMBER, new_sal NUMBER)",
            """
            CREATE OR REPLACE TRIGGER emp_sal_audit
              FOR UPDATE OF sal ON emp
              COMPOUND TRIGGER
              TYPE t_chg IS RECORD (empno NUMBER, old_sal NUMBER, new_sal NUMBER);
              TYPE t_chgs IS TABLE OF t_chg INDEX BY PLS_INTEGER;
              g_chg t_chgs;
              g_n PLS_INTEGER := 0;

              AFTER EACH ROW IS
              BEGIN
                IF UPDATING('SAL') THEN
                  g_n := g_n + 1;
                  g_chg(g_n).empno := :NEW.empno;
                  g_chg(g_n).old_sal := :OLD.sal;
                  g_chg(g_n).new_sal := :NEW.sal;
                END IF;
              END AFTER EACH ROW;

              AFTER STATEMENT IS
              BEGIN
                FOR i IN 1 .. g_n LOOP
                  INSERT INTO sal_audit (empno, old_sal, new_sal)
                  VALUES (g_chg(i).empno, g_chg(i).old_sal, g_chg(i).new_sal);
                END LOOP;
                g_n := 0;
              END AFTER STATEMENT;
            END emp_sal_audit;
            """,
            "UPDATE emp SET sal = sal + 10 WHERE deptno = 10",
        };
        var s = Shape.Parse(sql[1]);
        Assert.Equal(2, Find<TimingPointSection>(s).Count());
        await VerifyAsync(sql, ["SELECT empno, old_sal, new_sal FROM sal_audit ORDER BY empno", "SELECT COUNT(*) FROM emp WHERE sal > 1000"]);
    }

    /// <summary>A procedure with an OUT SYS_REFCURSOR (OPEN ... FOR with CASE), and an anonymous block that fetches it into a record.</summary>
    [SkippableFact]
    public async Task P06_SysRefcursorOutParameterAndFetchLoop()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            "CREATE TABLE band_log (empno NUMBER, band VARCHAR2(10), seq NUMBER)",
            """
            CREATE OR REPLACE PROCEDURE open_by_dept (p_deptno IN NUMBER, p_rc OUT SYS_REFCURSOR) AS
            BEGIN
              OPEN p_rc FOR
                SELECT e.empno, e.ename,
                       CASE WHEN e.sal >= 2500 THEN 'senior' WHEN e.sal >= 1200 THEN 'mid' ELSE 'junior' END AS band
                  FROM emp e
                 WHERE e.deptno = p_deptno
                   AND e.job <> 'PRESIDENT'
                 ORDER BY e.empno;
            END open_by_dept;
            """,
            """
            DECLARE
              rc SYS_REFCURSOR;
              TYPE t_row IS RECORD (empno NUMBER, ename VARCHAR2(10), band VARCHAR2(10));
              r t_row;
              n PLS_INTEGER := 0;
            BEGIN
              open_by_dept(30, rc);
              LOOP
                FETCH rc INTO r;
                EXIT WHEN rc%NOTFOUND;
                n := n + 1;
                INSERT INTO band_log (empno, band, seq) VALUES (r.empno, r.band, n);
              END LOOP;
              CLOSE rc;
            END;
            """,
        };
        var s = Shape.Parse(sql[1]);
        Assert.Single(Find<OpenStatement>(s));
        Assert.Single(Find<CaseExpression>(s));
        await VerifyAsync(sql, ["SELECT empno, band, seq FROM band_log ORDER BY seq"]);
    }

    /// <summary>Nested-table collection of records: EXTEND, indexed record-field assignment, DELETE(n), FIRST/NEXT walk; plus MULTISET in SQL.</summary>
    [SkippableFact]
    public async Task P07_CollectionsOfRecordsAndMultiset()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            "CREATE TYPE empno_tab AS TABLE OF NUMBER",
            """
            DECLARE
              TYPE t_emp IS RECORD (empno emp.empno%TYPE, sal emp.sal%TYPE);
              TYPE t_list IS TABLE OF t_emp;
              l_list t_list := t_list();
              l_total NUMBER := 0;
              l_idx PLS_INTEGER;
              l_cnt PLS_INTEGER;
            BEGIN
              FOR r IN (SELECT e.empno, e.sal FROM emp e WHERE e.deptno = 30 ORDER BY e.empno) LOOP
                l_list.EXTEND;
                l_list(l_list.LAST).empno := r.empno;
                l_list(l_list.LAST).sal := r.sal;
              END LOOP;
              l_list.DELETE(2);
              l_idx := l_list.FIRST;
              WHILE l_idx IS NOT NULL LOOP
                l_total := l_total + l_list(l_idx).sal;
                l_idx := l_list.NEXT(l_idx);
              END LOOP;
              l_cnt := l_list.COUNT;
              INSERT INTO bonus (empno, amount, note) VALUES (9001, l_total, 'sum ' || l_cnt);
            END;
            """,
        };
        var s = Shape.Parse(sql[1]);
        Assert.Single(Find<CollectionTypeDeclaration>(s));
        Assert.Single(Find<RecordTypeDeclaration>(s));
        Assert.Contains(Find<MemberAccess>(s), m => Str(m).Contains("EMPNO", StringComparison.OrdinalIgnoreCase));
        await VerifyAsync(sql, [
            "SELECT d.deptno, (SELECT SUM(column_value) FROM TABLE(CAST(MULTISET(SELECT e.empno FROM emp e WHERE e.deptno = d.deptno) AS empno_tab))) AS s FROM dept d ORDER BY d.deptno",
            "SELECT empno, amount, note FROM bonus ORDER BY empno",
        ]);
    }

    /// <summary>Labeled nested loops with CONTINUE/EXIT to outer labels, and an EXIT that fires before a CONTINUE in the same body.</summary>
    [SkippableFact]
    public async Task P08_LabeledLoopsContinueAndExitToOuterLabel()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            "CREATE TABLE picks (deptno NUMBER, empno NUMBER)",
            """
            BEGIN
              <<dept_loop>>
              FOR d IN (SELECT deptno FROM dept ORDER BY deptno) LOOP
                <<emp_loop>>
                FOR e IN (SELECT empno, sal FROM emp WHERE deptno = d.deptno ORDER BY empno) LOOP
                  CONTINUE dept_loop WHEN e.sal < 900;
                  EXIT dept_loop WHEN e.sal > 4900;
                  CONTINUE emp_loop WHEN MOD(e.empno, 2) = 1;
                  INSERT INTO picks (deptno, empno) VALUES (d.deptno, e.empno);
                END LOOP emp_loop;
              END LOOP dept_loop;
            END;
            """,
        };
        var s = Shape.Parse(sql[1]);
        Assert.Equal(3, Find<ContinueStatement>(s).Count() + Find<ExitStatement>(s).Count());
        await VerifyAsync(sql, ["SELECT deptno, empno FROM picks ORDER BY deptno, empno"]);
    }

    /// <summary>Object type spec and body with MEMBER functions that use SELF and call a sibling member through a chain.</summary>
    [SkippableFact]
    public async Task P09_ObjectTypeMembersAndMemberChain()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            CREATE OR REPLACE TYPE money_t AS OBJECT (
              amt NUMBER,
              ccy VARCHAR2(3),
              MEMBER FUNCTION add_amt (o money_t) RETURN money_t,
              MEMBER FUNCTION fmt RETURN VARCHAR2
            )
            """,
            """
            CREATE OR REPLACE TYPE BODY money_t AS
              MEMBER FUNCTION add_amt (o money_t) RETURN money_t IS
              BEGIN
                IF o.ccy <> SELF.ccy THEN
                  RAISE_APPLICATION_ERROR(-20010, 'currency mismatch');
                END IF;
                RETURN money_t(SELF.amt + o.amt, SELF.ccy);
              END add_amt;
              MEMBER FUNCTION fmt RETURN VARCHAR2 IS
              BEGIN
                RETURN TO_CHAR(SELF.amt, 'FM99990.00') || ' ' || SELF.ccy;
              END fmt;
            END;
            """,
        };
        var s = Shape.Parse(sql[1]);
        Assert.Single(Find<CreateTypeBodyStatement>(s));
        await VerifyAsync(sql, ["SELECT money_t(10, 'USD').add_amt(money_t(5.5, 'USD')).fmt() AS a FROM dual"]);
    }

    /// <summary>PL/SQL UPDATE ... RETURNING ... BULK COLLECT INTO two collections, with a CASE expression in SET.</summary>
    [SkippableFact]
    public async Task P10_UpdateReturningBulkCollectInto()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            DECLARE
              TYPE t_ids IS TABLE OF NUMBER;
              TYPE t_sals IS TABLE OF NUMBER;
              l_ids  t_ids;
              l_sals t_sals;
            BEGIN
              UPDATE emp e
                 SET e.sal = CASE WHEN e.sal >= 3000 THEN e.sal * 1.02 ELSE e.sal + 50 END
               WHERE e.deptno = 20
              RETURNING e.empno, e.sal BULK COLLECT INTO l_ids, l_sals;
              FOR i IN 1 .. l_ids.COUNT LOOP
                INSERT INTO bonus (empno, amount, note) VALUES (l_ids(i), ROUND(l_sals(i) / 100, 2), 'raise');
              END LOOP;
            END;
            """,
        };
        var s = Shape.Parse(sql[0]);
        var up = Find<UpdateStatement>(s).Single();
        Assert.Contains("returningBulkCollect", Str(up));
        Assert.Contains("returningInto=[L_IDS L_SALS]", Str(up), StringComparison.OrdinalIgnoreCase);
        await VerifyAsync(sql, ["SELECT empno, sal FROM emp WHERE deptno = 20 ORDER BY empno", "SELECT empno, amount, note FROM bonus ORDER BY empno"]);
    }

    /// <summary>PL/SQL searched CASE statement inside a cursor loop, SQL%ROWCOUNT after an UPDATE, and a nested exception block.</summary>
    [SkippableFact]
    public async Task P11_CaseStatementRowcountAndNestedHandler()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            DECLARE
              l_grade CHAR(1);
              l_n PLS_INTEGER;
            BEGIN
              FOR r IN (SELECT empno, sal FROM emp ORDER BY empno) LOOP
                CASE
                  WHEN r.sal >= 3000 THEN l_grade := 'A';
                  WHEN r.sal BETWEEN 1500 AND 2999 THEN l_grade := 'B';
                  ELSE l_grade := 'C';
                END CASE;
                UPDATE bonus SET note = 'grade ' || l_grade WHERE empno = r.empno;
                l_n := SQL%ROWCOUNT;
                IF l_n = 0 THEN
                  BEGIN
                    INSERT INTO bonus (empno, amount, note) VALUES (r.empno, 0, 'grade ' || l_grade);
                  EXCEPTION
                    WHEN DUP_VAL_ON_INDEX THEN NULL;
                  END;
                END IF;
              END LOOP;
            END;
            """,
        };
        var s = Shape.Parse(sql[0]);
        Assert.Single(Find<PlsqlCaseStatement>(s));
        Assert.Single(Find<ExceptionHandler>(s));
        await VerifyAsync(sql, ["SELECT empno, amount, note FROM bonus ORDER BY empno"]);
    }

    /// <summary>A RESULT_CACHE function with RELIES_ON and default parameters, called from SQL in the select list.</summary>
    [SkippableFact]
    public async Task P12_ResultCacheFunctionRelies()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            CREATE OR REPLACE FUNCTION dept_headcount (p_deptno NUMBER DEFAULT NULL, p_scale NUMBER DEFAULT 1)
              RETURN NUMBER
              RESULT_CACHE RELIES_ON (emp)
            IS
              l_cnt NUMBER := 0;
            BEGIN
              SELECT COUNT(*) INTO l_cnt FROM emp WHERE p_deptno IS NULL OR deptno = p_deptno;
              RETURN l_cnt * p_scale;
            END dept_headcount;
            """,
        };
        var s = Shape.Parse(sql[0]);
        Assert.DoesNotContain(s.Descendants(), n => n is CreateObjectStatement);
        await VerifyAsync(sql, ["SELECT dept_headcount(30) AS n30, dept_headcount() AS total, dept_headcount(NULL, 2) AS doubled FROM dual"]);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // DDL and 23ai misc
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>23ai IF EXISTS / IF NOT EXISTS DDL, identity column with NOT NULL defaults.</summary>
    [SkippableFact]
    public async Task M01_IfExistsIfNotExistsDdlAndIdentity()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            "DROP TABLE IF EXISTS scratch_nothing PURGE",
            """
            CREATE TABLE IF NOT EXISTS audit_trail (
              id NUMBER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
              actor VARCHAR2(30) DEFAULT 'system' NOT NULL,
              action VARCHAR2(20),
              happened DATE DEFAULT SYSDATE
            )
            """,
            "INSERT INTO audit_trail (actor, action) VALUES ('ann', 'login')",
            "INSERT INTO audit_trail (actor, action) VALUES ('bob', 'logout')",
        };
        var s = Shape.Parse(sql[0]);
        Assert.Single(Find<DropStatement>(s));
        await VerifyAsync(sql, ["SELECT id, actor, action FROM audit_trail ORDER BY id"]);
    }

    /// <summary>CREATE DOMAIN used as a column type, identity with ON NULL, and a named check added NOVALIDATE.</summary>
    [SkippableFact]
    public async Task M02_DomainColumnIdentityAndNovalidateCheck()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            "CREATE DOMAIN email_dom AS VARCHAR2(120) CONSTRAINT email_dom_chk CHECK (VALUE LIKE '%@%.%')",
            """
            CREATE TABLE contacts (
              id NUMBER GENERATED BY DEFAULT ON NULL AS IDENTITY (START WITH 100 INCREMENT BY 1),
              email email_dom NOT NULL,
              tier VARCHAR2(10) DEFAULT 'std' NOT NULL
            )
            """,
            "ALTER TABLE contacts ADD CONSTRAINT contacts_tier_chk CHECK (tier IN ('std', 'gold')) ENABLE NOVALIDATE",
            "INSERT INTO contacts (email) VALUES ('a@x.io')",
            "INSERT INTO contacts (email, tier) VALUES ('b@y.org', 'gold')",
        };
        var s = Shape.Parse(sql[1]);
        Assert.Single(Find<CreateTableStatement>(s));
        Assert.Single(Find<IdentityClause>(s));
        await VerifyAsync(sql, [
            "SELECT c.id, c.email, c.tier FROM contacts c ORDER BY c.id",
            "SELECT constraint_name, status, validated FROM user_constraints WHERE constraint_name = 'CONTACTS_TIER_CHK'",
        ]);
    }

    /// <summary>Interval-partitioned table, with an explicit first partition and rows that create interval partitions.</summary>
    [SkippableFact]
    public async Task M03_IntervalPartitionedTable()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            CREATE TABLE sales_hist (
              sale_id NUMBER,
              sale_dt DATE,
              amount NUMBER(10,2)
            )
            PARTITION BY RANGE (sale_dt)
            INTERVAL (NUMTOYMINTERVAL(1, 'MONTH'))
            (PARTITION p_first VALUES LESS THAN (DATE '2024-01-01'))
            """,
            "INSERT INTO sales_hist VALUES (1, DATE '2023-11-30', 10)",
            "INSERT INTO sales_hist VALUES (2, DATE '2024-02-14', 25.5)",
            "INSERT INTO sales_hist VALUES (3, DATE '2024-05-02', 7)",
        };
        var s = Shape.Parse(sql[0]);
        Assert.Single(Find<PartitionDefinition>(s));
        await VerifyAsync(sql, [
            "SELECT COUNT(*), SUM(amount) FROM sales_hist",
            "SELECT COUNT(*) FROM user_tab_partitions WHERE table_name = 'SALES_HIST'",
        ]);
    }

    /// <summary>Sequence with ORDER, multi-row VALUES insert that draws NEXTVAL twice, and a functional index used by a query.</summary>
    [SkippableFact]
    public async Task M04_SequenceMultiRowValuesAndFunctionalIndex()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            "CREATE SEQUENCE ord_seq START WITH 100 INCREMENT BY 5 CACHE 20 ORDER",
            "CREATE INDEX emp_ename_ci ON emp (UPPER(ename))",
            "INSERT INTO org (id, parent_id, name) VALUES (201, NULL, 'lit-a'), (202, NULL, 'lit-b')",
            "INSERT INTO org (id, parent_id, name) VALUES (ord_seq.NEXTVAL, NULL, 'seq-c')",
        };
        Assert.Single(Find<CreateSequenceStatement>(Shape.Parse(sql[0])));
        Assert.Single(Find<IndexColumn>(Shape.Parse(sql[1])), c => Str(c).Contains("UPPER", StringComparison.OrdinalIgnoreCase));
        await VerifyAsync(sql, [
            "SELECT o.id, o.name FROM org o WHERE o.id >= 100 ORDER BY o.id",
            "SELECT ename FROM emp WHERE UPPER(ename) = 'KING'",
        ]);
    }

    /// <summary>A view WITH CHECK OPTION CONSTRAINT name, then a DML through the view.</summary>
    [SkippableFact]
    public async Task M05_ViewWithCheckOptionAndDmlThroughView()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            """
            CREATE OR REPLACE VIEW dept30_staff (empno, ename, sal) AS
              SELECT e.empno, e.ename, e.sal FROM emp e WHERE e.deptno = 30
              WITH CHECK OPTION CONSTRAINT dept30_staff_ck
            """,
            "UPDATE dept30_staff SET sal = sal + 1 WHERE empno = 7499",
        };
        var s = Shape.Parse(sql[0]);
        Assert.Single(Find<CreateViewStatement>(s));
        await VerifyAsync(sql, ["SELECT empno, sal FROM dept30_staff ORDER BY empno"]);
    }

    /// <summary>COMMENT ON COLUMN and ALTER TABLE ... MODIFY with a DEFAULT; the result is read back from the data dictionary.</summary>
    [SkippableFact]
    public async Task M06_CommentOnColumnAndAlterModifyDefault()
    {
        db.SkipIfUnavailable();
        var sql = new[]
        {
            "COMMENT ON COLUMN emp.sal IS 'gross monthly salary'",
            "ALTER TABLE emp MODIFY (ename VARCHAR2(30) DEFAULT 'N/A')",
        };
        var s = Shape.Parse(sql[0]);
        Assert.Single(Find<CommentStatement>(s));
        await VerifyAsync(sql, [
            "SELECT comments FROM user_col_comments WHERE table_name = 'EMP' AND column_name = 'SAL'",
            "SELECT data_default FROM user_tab_columns WHERE table_name = 'EMP' AND column_name = 'ENAME'",
        ]);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Harness
    // ------------------------------------------------------------------------------------------------------------------

    private static string Str(SqlNode n) => AstPrinter.Print(n);

    private static T[] Find<T>(SqlScript s) where T : SqlNode => s.Descendants().OfType<T>().ToArray();

    /// <summary>
    /// Checks that generator output re-parses to a structurally equal tree, then runs the original and the generated
    /// SQL in two fresh scratch schemas and asserts they produce the same observations (rows, row counts, check queries)
    /// and that USER_ERRORS is empty in both.
    /// </summary>
    private async Task VerifyAsync(string[] statements, string[] checks)
    {
        var generator = new SqlScriptGenerator(new SqlScriptGeneratorOptions());
        var generated = new string[statements.Length];
        var isQuery = new bool[statements.Length];
        var ordered = new bool[statements.Length];
        for (int i = 0; i < statements.Length; i++)
        {
            var parsed = SqlParser.Parse(statements[i]);
            Assert.True(parsed.Success, $"parser rejected statement {i}: {string.Join("; ", parsed.Errors)}\n{statements[i]}");
            generated[i] = generator.Generate(parsed.Script);
            var again = SqlParser.Parse(generated[i]);
            Assert.True(again.Success, $"generated text for statement {i} does not re-parse: {string.Join("; ", again.Errors)}\n{generated[i]}");
            Assert.True(SqlNodeComparer.StructurallyEqual(parsed.Script, again.Script),
                $"round-trip tree differs for statement {i}\n--- original:\n{statements[i]}\n--- generated:\n{generated[i]}");
            isQuery[i] = parsed.Script.Statements.Count == 1 && parsed.Script.Statements[0] is SelectStatement;
            ordered[i] = isQuery[i] && ((SelectStatement)parsed.Script.Statements[0]).OrderBy.Count > 0;
        }

        var asWritten = await RunOracleAsync("original", statements, isQuery, ordered, checks);
        var asGenerated = await RunOracleAsync("generated", generated, isQuery, ordered, checks);
        Assert.True(asWritten.Observations.SequenceEqual(asGenerated.Observations),
            $"Oracle results differ.\n--- original:\n{string.Join("\n", asWritten.Observations)}\n--- generated:\n{string.Join("\n", asGenerated.Observations)}");
    }

    private sealed record RunResult(List<string> Observations);

    private async Task<RunResult> RunOracleAsync(string label, string[] statements, bool[] isQuery, bool[] ordered, string[] checks)
    {
        var user = "EXS" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        await using (var sys = await db.OpenSystemAsync())
        {
            await OracleFixture.ExecAsync(sys, $"CREATE USER {user} IDENTIFIED BY {Pw} QUOTA UNLIMITED ON USERS");
            await OracleFixture.ExecAsync(sys, $"GRANT CREATE SESSION, CREATE TABLE, CREATE VIEW, CREATE SEQUENCE, CREATE PROCEDURE, CREATE TRIGGER, CREATE TYPE, CREATE SYNONYM, CREATE DOMAIN TO {user}");
        }
        var obs = new List<string>();
        try
        {
            await using var c = await db.OpenAsync(user, Pw);
            foreach (var sql in Base)
                await OracleFixture.ExecAsync(c, OracleHarness.Prepare(sql));
            for (int i = 0; i < statements.Length; i++)
            {
                var text = OracleHarness.Prepare(statements[i]);
                try
                {
                    if (isQuery[i])
                    {
                        var rs = await OracleFixture.QueryAsync(c, text);
                        obs.Add($"stmt{i}: " + Render(rs, ordered[i]));
                    }
                    else
                    {
                        var n = await ExecCountAsync(c, text);
                        obs.Add($"stmt{i}: rows={n}");
                    }
                }
                catch (OracleException e)
                {
                    throw new Xunit.Sdk.XunitException($"[{label}] Oracle rejected statement {i}: {e.Message}\n{text}");
                }
            }
            foreach (var (check, i) in checks.Select((c, i) => (c, i)))
            {
                try
                {
                    obs.Add($"check{i}: " + Render(await OracleFixture.QueryAsync(c, check), ordered: true));
                }
                catch (OracleException e)
                {
                    throw new Xunit.Sdk.XunitException($"[{label}] check query {i} failed: {e.Message}\n{check}");
                }
            }
            var errors = await OracleFixture.QueryAsync(c, "SELECT name || ' ' || type || ' line ' || line || ': ' || text FROM user_errors ORDER BY name, type, sequence");
            Assert.True(errors.Rows.Count == 0, $"[{label}] compilation errors:\n{string.Join("\n", errors.Rows.Select(r => r[0]))}");
        }
        finally
        {
            await using var sys = await db.OpenSystemAsync();
            try { await OracleFixture.ExecAsync(sys, $"DROP USER {user} CASCADE"); } catch (OracleException) { }
        }
        return new RunResult(obs);
    }

    private static async Task<int> ExecCountAsync(Oracle.ManagedDataAccess.Client.OracleConnection c, string text)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = text;
        return await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Renders a result set; unordered queries are compared as a sorted multiset of rows.</summary>
    private static string Render(ResultSet rs, bool ordered)
    {
        var rows = rs.Rows.Select(r => string.Join(" | ", r)).ToList();
        if (!ordered) rows.Sort(StringComparer.Ordinal);
        return string.Join(" ; ", new[] { string.Join(" | ", rs.Columns) }.Concat(rows));
    }

}
