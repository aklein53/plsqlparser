using Xunit;

namespace OracleSqlParser.IntegrationTests;

/// <summary>
/// For each curated query: run the original on Oracle (proves the test SQL is valid), parse + print it with SqlWriter,
/// run the printed SQL, and require identical column names and identical rows in identical order.
/// </summary>
[Collection(OracleCollection.Name)]
public class WriterRoundTripTests(OracleFixture db)
{
    private static readonly Dictionary<string, object?> NoBinds = [];

    public static readonly (string Name, string Sql, Dictionary<string, object?>? Binds)[] Cases =
    [
        ("ansi inner join", "SELECT e.ename, d.dname FROM emp e INNER JOIN dept d ON e.deptno = d.deptno ORDER BY e.empno", null),
        ("ansi left join", "SELECT d.dname, e.ename FROM dept d LEFT OUTER JOIN emp e ON e.deptno = d.deptno ORDER BY d.deptno, e.empno", null),
        ("ansi right join", "SELECT d.dname, e.ename FROM emp e RIGHT JOIN dept d ON e.deptno = d.deptno AND e.sal > 2000 ORDER BY d.deptno, e.empno", null),
        ("ansi full join using", "SELECT deptno, e.ename, d.dname FROM emp e FULL OUTER JOIN dept d USING (deptno) ORDER BY deptno, e.empno", null),
        ("cross join", "SELECT COUNT(*) AS n FROM emp CROSS JOIN dept", null),
        ("natural join", "SELECT ename, dname FROM emp NATURAL JOIN dept ORDER BY empno", null),
        ("oracle (+) join", "SELECT d.dname, e.ename FROM dept d, emp e WHERE d.deptno = e.deptno(+) ORDER BY d.deptno, e.empno", null),
        ("self join", "SELECT e.ename, m.ename AS boss FROM emp e JOIN emp m ON e.mgr = m.empno ORDER BY e.empno", null),
        ("three table join", "SELECT e.ename, d.dname, m.ename mname FROM emp e, dept d, emp m WHERE e.deptno = d.deptno AND e.mgr = m.empno AND d.loc <> 'BOSTON' ORDER BY e.empno", null),
        ("scalar subquery", "SELECT d.dname, (SELECT MAX(e.sal) FROM emp e WHERE e.deptno = d.deptno) AS top_sal FROM dept d ORDER BY d.deptno", null),
        ("correlated subquery", "SELECT ename, sal FROM emp e WHERE sal > (SELECT AVG(sal) FROM emp x WHERE x.deptno = e.deptno) ORDER BY empno", null),
        ("in/not in subquery", "SELECT ename FROM emp WHERE deptno IN (SELECT deptno FROM dept WHERE loc LIKE 'D%') AND empno NOT IN (SELECT mgr FROM emp WHERE mgr IS NOT NULL) ORDER BY empno", null),
        ("exists / not exists", "SELECT dname FROM dept d WHERE EXISTS (SELECT 1 FROM emp e WHERE e.deptno = d.deptno AND e.sal > 2900) OR NOT EXISTS (SELECT 1 FROM emp e WHERE e.deptno = d.deptno) ORDER BY deptno", null),
        ("any / all", "SELECT ename FROM emp WHERE sal > ALL (SELECT sal FROM emp WHERE deptno = 10) OR sal = ANY (SELECT sal FROM emp WHERE job = 'CLERK') ORDER BY empno", null),
        ("tuple in", "SELECT ename FROM emp WHERE (deptno, job) IN ((10, 'CLERK'), (20, 'MANAGER')) ORDER BY empno", null),
        ("inline view", "SELECT v.deptno, v.total FROM (SELECT deptno, SUM(sal) AS total FROM emp GROUP BY deptno) v WHERE v.total > 9000 ORDER BY v.deptno", null),
        ("cte multiple", "WITH a AS (SELECT deptno, SUM(sal) s FROM emp GROUP BY deptno), b (deptno, c) AS (SELECT deptno, COUNT(*) FROM emp GROUP BY deptno) SELECT a.deptno, a.s, b.c FROM a JOIN b ON a.deptno = b.deptno ORDER BY a.deptno", null),
        ("recursive cte", "WITH t (n) AS (SELECT 1 FROM dual UNION ALL SELECT n + 1 FROM t WHERE n < 5) SELECT n FROM t ORDER BY n", null),
        ("recursive cte search depth", "WITH t (empno, ename, mgr) AS (SELECT empno, ename, mgr FROM emp WHERE mgr IS NULL UNION ALL SELECT e.empno, e.ename, e.mgr FROM emp e JOIN t ON e.mgr = t.empno) SEARCH DEPTH FIRST BY empno SET ord SELECT empno, ename FROM t ORDER BY ord", null),
        ("union / union all", "SELECT deptno FROM emp UNION SELECT deptno FROM dept UNION ALL SELECT 99 FROM dual ORDER BY 1", null),
        ("minus / intersect", "(SELECT deptno FROM dept MINUS SELECT deptno FROM emp) UNION ALL (SELECT deptno FROM dept INTERSECT SELECT deptno FROM emp WHERE job = 'CLERK') ORDER BY 1", null),
        ("group by having", "SELECT deptno, job, COUNT(*) c, SUM(sal) s, COUNT(DISTINCT job) dj FROM emp GROUP BY deptno, job HAVING COUNT(*) > 1 ORDER BY deptno, job", null),
        ("rollup", "SELECT deptno, job, SUM(sal) s FROM emp GROUP BY ROLLUP(deptno, job) ORDER BY deptno NULLS LAST, job NULLS LAST", null),
        ("cube with grouping()", "SELECT deptno, job, SUM(sal) s, GROUPING(deptno) gd, GROUPING_ID(deptno, job) gid FROM emp GROUP BY CUBE(deptno, job) ORDER BY gid, deptno NULLS LAST, job NULLS LAST", null),
        ("grouping sets", "SELECT deptno, job, COUNT(*) c FROM emp GROUP BY GROUPING SETS ((deptno, job), (deptno), ()) ORDER BY deptno NULLS LAST, job NULLS LAST", null),
        ("analytic rank/row_number", "SELECT ename, deptno, sal, ROW_NUMBER() OVER (PARTITION BY deptno ORDER BY sal DESC, empno) rn, RANK() OVER (ORDER BY sal DESC) rk, DENSE_RANK() OVER (ORDER BY sal DESC) drk FROM emp ORDER BY empno", null),
        ("analytic rows frame", "SELECT empno, SUM(sal) OVER (ORDER BY empno ROWS BETWEEN 1 PRECEDING AND 1 FOLLOWING) moving, SUM(sal) OVER (ORDER BY empno ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) running FROM emp ORDER BY empno", null),
        ("analytic range frame", "SELECT empno, hiredate, COUNT(*) OVER (ORDER BY hiredate RANGE BETWEEN INTERVAL '30' DAY PRECEDING AND CURRENT ROW) c FROM emp ORDER BY empno", null),
        ("analytic lag/lead/first_value", "SELECT empno, LAG(sal, 1, 0) OVER (ORDER BY empno) prev, LEAD(sal) OVER (ORDER BY empno) nxt, FIRST_VALUE(ename) OVER (PARTITION BY deptno ORDER BY sal DESC, empno ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING) top, NTILE(3) OVER (ORDER BY empno) nt FROM emp ORDER BY empno", null),
        ("analytic ignore nulls / ratio", "SELECT empno, LAST_VALUE(comm) IGNORE NULLS OVER (ORDER BY empno ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) lc, ROUND(RATIO_TO_REPORT(sal) OVER (), 4) r FROM emp ORDER BY empno", null),
        ("listagg within group", "SELECT deptno, LISTAGG(ename, ', ') WITHIN GROUP (ORDER BY ename) names FROM emp GROUP BY deptno ORDER BY deptno", null),
        ("listagg overflow", "SELECT LISTAGG(ename, ',' ON OVERFLOW TRUNCATE '...' WITH COUNT) WITHIN GROUP (ORDER BY ename) names FROM emp", null),
        ("percentile_cont within group", "SELECT deptno, PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY sal) med FROM emp GROUP BY deptno ORDER BY deptno", null),
        ("keep dense_rank", "SELECT deptno, MAX(ename) KEEP (DENSE_RANK FIRST ORDER BY sal DESC, empno) top_earner, MIN(sal) KEEP (DENSE_RANK LAST ORDER BY hiredate) s FROM emp GROUP BY deptno ORDER BY deptno", null),
        ("connect by", "SELECT LEVEL lvl, empno, ename, SYS_CONNECT_BY_PATH(ename, '/') path, CONNECT_BY_ROOT ename root, CONNECT_BY_ISLEAF leaf FROM emp START WITH mgr IS NULL CONNECT BY PRIOR empno = mgr ORDER SIBLINGS BY ename", null),
        ("connect by nocycle prior right", "SELECT ename FROM emp START WITH ename = 'SMITH' CONNECT BY NOCYCLE empno = PRIOR mgr ORDER BY LEVEL", null),
        ("case searched/simple", "SELECT ename, CASE WHEN sal >= 3000 THEN 'high' WHEN sal >= 1500 THEN 'mid' ELSE 'low' END band, CASE deptno WHEN 10 THEN 'A' WHEN 20 THEN 'B' END dp FROM emp ORDER BY empno", null),
        ("cast", "SELECT CAST(sal AS VARCHAR2(10)) s, CAST(empno AS NUMBER(10,2)) n, CAST(hiredate AS TIMESTAMP) ts, CAST('12' AS INTEGER) i FROM emp WHERE deptno = 10 ORDER BY empno", null),
        ("extract / trim / substr", "SELECT EXTRACT(YEAR FROM hiredate) y, EXTRACT(MONTH FROM hiredate) m, TRIM(BOTH 'S' FROM ename) b, TRIM(LEADING 'J' FROM ename) l, TRIM('  x ') t, SUBSTR(ename, 2, 3) sub FROM emp ORDER BY empno", null),
        ("date / timestamp / interval literals", "SELECT DATE '2024-02-29' + 1 d, TIMESTAMP '2024-01-01 10:30:00' ts, INTERVAL '3' DAY iv, INTERVAL '1-2' YEAR TO MONTH ym, DATE '2024-01-01' + INTERVAL '2' MONTH d2, TIMESTAMP '2024-01-01 00:00:00' + INTERVAL '1 02:03:04' DAY TO SECOND ds FROM dual", null),
        ("to_char / to_date / dates", "SELECT TO_CHAR(hiredate, 'YYYY-MM-DD') h, TO_DATE('2024-03-05', 'YYYY-MM-DD') + 1 d, ADD_MONTHS(hiredate, 6) am, TRUNC(hiredate, 'YEAR') ty, MONTHS_BETWEEN(DATE '1982-01-01', hiredate) mb FROM emp WHERE deptno = 10 ORDER BY empno", null),
        ("literals and operators", "SELECT 1 + 2 * 3 - 4 / 2 a, (1 + 2) * 3 b, -(-5) c, 'a' || 'b' || 'c' d, q'[it's]' e, 1.5e3 f, .5 g, 10 - (3 - 2) h, MOD(10, 3) i FROM dual", null),
        ("precedence and/or/not", "SELECT ename FROM emp WHERE NOT (deptno = 10 OR deptno = 20) AND (sal > 1000 OR comm IS NOT NULL) AND NOT job = 'CLERK' ORDER BY empno", null),
        ("offset / fetch", "SELECT empno FROM emp ORDER BY empno OFFSET 3 ROWS FETCH NEXT 4 ROWS ONLY", null),
        ("fetch first with ties", "SELECT ename, sal FROM emp ORDER BY sal DESC FETCH FIRST 2 ROWS WITH TIES", null),
        ("fetch first percent", "SELECT empno FROM emp ORDER BY empno FETCH FIRST 25 PERCENT ROWS ONLY", null),
        ("pivot", "SELECT * FROM (SELECT product, quarter, amount FROM sales) PIVOT (SUM(amount) AS total, COUNT(*) AS cnt FOR quarter IN ('Q1' AS q1, 'Q2' AS q2)) ORDER BY product", null),
        ("unpivot", "SELECT product, quarter, amount FROM sales_wide UNPIVOT INCLUDE NULLS (amount FOR quarter IN (q1 AS 'Q1', q2 AS 'Q2', q3 AS 'Q3', q4 AS 'Q4')) ORDER BY product, quarter", null),
        ("unpivot multi column", "SELECT * FROM (SELECT product, q1, q2, q3, q4 FROM sales_wide) UNPIVOT ((a, b) FOR half IN ((q1, q2) AS 'H1', (q3, q4) AS 'H2')) ORDER BY product, half", null),
        ("model", "SELECT country, product, year, amt FROM (SELECT country, product, year, SUM(amount) amt FROM sales GROUP BY country, product, year) MODEL PARTITION BY (country) DIMENSION BY (product, year) MEASURES (amt) RULES UPSERT (amt['Bike', 2004] = amt['Bike', 2003] * 2, amt['Car', 2004] = SUM(amt)[CV(), year < 2004]) ORDER BY country, product, year", null),
        ("model iterate", "SELECT n, v FROM dual MODEL DIMENSION BY (0 AS n) MEASURES (1 AS v) RULES ITERATE (4) (v[ITERATION_NUMBER + 1] = v[ITERATION_NUMBER] * 2) ORDER BY n", null),
        ("match_recognize", "SELECT * FROM ticker MATCH_RECOGNIZE (PARTITION BY symbol ORDER BY tstamp MEASURES STRT.tstamp AS start_t, LAST(DOWN.tstamp) AS bottom_t, LAST(UP.tstamp) AS end_t, MATCH_NUMBER() AS mno ONE ROW PER MATCH AFTER MATCH SKIP TO LAST UP PATTERN (STRT DOWN+ UP+) DEFINE DOWN AS price < PREV(price), UP AS price > PREV(price)) ORDER BY mno", null),
        ("json_value / json_query", "SELECT id, JSON_VALUE(doc, '$.name') nm, JSON_VALUE(doc, '$.price' RETURNING NUMBER) price, JSON_QUERY(doc, '$.tags' WITH WRAPPER) tags, JSON_VALUE(doc, '$.items[0].nm' DEFAULT 'n/a' ON EMPTY) first_item FROM docs ORDER BY id", null),
        ("json_table", "SELECT d.id, jt.item_id, jt.nm, jt.tag FROM docs d, JSON_TABLE(d.doc, '$.items[*]' COLUMNS (item_id NUMBER PATH '$.id', nm VARCHAR2(20) PATH '$.nm', NESTED PATH '$.tags[*]' COLUMNS (tag VARCHAR2(5) PATH '$'))) jt ORDER BY d.id, jt.item_id", null),
        ("json_table ordinality", "SELECT jt.rn, jt.nm FROM docs d CROSS JOIN JSON_TABLE(d.doc, '$.items[*]' ERROR ON ERROR COLUMNS (rn FOR ORDINALITY, nm VARCHAR2(20) PATH '$.nm')) jt WHERE d.id = 1 ORDER BY jt.rn", null),
        ("json_object / arrayagg", "SELECT JSON_OBJECT(KEY 'dept' VALUE deptno, 'n' VALUE COUNT(*)) j, JSON_ARRAYAGG(ename ORDER BY ename) names FROM emp GROUP BY deptno ORDER BY deptno", null),
        ("bind variables", "SELECT ename FROM emp WHERE deptno = :dept AND sal > :min_sal AND ename LIKE :pat ORDER BY empno", new() { ["dept"] = 30, ["min_sal"] = 1000, ["pat"] = "%E%" }),
        ("bind in fetch/in list", "SELECT empno FROM emp WHERE job IN (:j1, :j2) ORDER BY empno FETCH FIRST :n ROWS ONLY", new() { ["j1"] = "CLERK", ["j2"] = "ANALYST", ["n"] = 4 }),
        ("hints", "SELECT /*+ FULL(e) PARALLEL(e, 1) */ e.empno FROM emp e WHERE e.deptno = 10 ORDER BY e.empno", null),
        ("hint leading/use_nl", "SELECT /*+ LEADING(d e) USE_NL(e) */ e.ename, d.dname FROM dept d JOIN emp e ON e.deptno = d.deptno ORDER BY e.empno", null),
        ("quoted identifiers", "SELECT \"Id\" AS \"My Id\", \"lower col\" FROM \"MixedCase\" m WHERE m.\"Id\" >= 1 ORDER BY \"My Id\"", null),
        ("quoted alias with reserved word", "SELECT ename AS \"select\", sal \"Salary $\" FROM emp WHERE deptno = 10 ORDER BY empno", null),
        ("between / like escape / is null", "SELECT empno FROM emp WHERE sal BETWEEN 1000 AND 2000 AND ename NOT LIKE 'S%' AND comm IS NULL OR ename LIKE 'A\\_%' ESCAPE '\\' OR mgr IS NULL ORDER BY empno", null),
        ("nvl / coalesce / decode / nullif", "SELECT empno, NVL(comm, 0) a, COALESCE(comm, sal, 0) b, DECODE(deptno, 10, 'x', 20, 'y', 'z') c, NULLIF(deptno, 10) d, NVL2(comm, 'y', 'n') e FROM emp ORDER BY empno", null),
        ("order by nulls", "SELECT empno, comm FROM emp ORDER BY comm DESC NULLS LAST, empno DESC", null),
        ("distinct", "SELECT DISTINCT job, deptno FROM emp ORDER BY job, deptno", null),
        ("count star over ()", "SELECT empno, COUNT(*) OVER () total FROM emp WHERE deptno = 10 ORDER BY empno", null),
        ("to_number default on conversion error", "SELECT TO_NUMBER('abc' DEFAULT -1 ON CONVERSION ERROR) a, TO_NUMBER('12.5') b FROM dual", null),
        ("lateral / cross apply", "SELECT d.dname, x.cnt FROM dept d CROSS APPLY (SELECT COUNT(*) cnt FROM emp e WHERE e.deptno = d.deptno) x ORDER BY d.deptno", null),
        ("lateral keyword", "SELECT d.dname, x.top FROM dept d, LATERAL (SELECT MAX(sal) top FROM emp e WHERE e.deptno = d.deptno) x ORDER BY d.deptno", null),
        ("xmltable", "SELECT x.id, x.nm FROM XMLTABLE('/r/i' PASSING XMLTYPE('<r><i id=\"1\"><n>a</n></i><i id=\"2\"><n>b</n></i></r>') COLUMNS id NUMBER PATH '@id', nm VARCHAR2(5) PATH 'n') x ORDER BY x.id", null),
        ("subquery in order by / select", "SELECT ename, (SELECT dname FROM dept d WHERE d.deptno = e.deptno) dn FROM emp e ORDER BY (SELECT loc FROM dept d WHERE d.deptno = e.deptno), empno", null),
        ("nested set ops in subquery", "SELECT ename FROM emp WHERE deptno IN (SELECT deptno FROM dept WHERE loc = 'DALLAS' UNION SELECT deptno FROM dept WHERE loc = 'BOSTON') ORDER BY empno", null),
        ("dual functions", "SELECT UPPER('abc') u, LENGTH('hello') l, REPLACE('a-b', '-', '+') r, INSTR('hello', 'l') i, LPAD('7', 3, '0') p, ROUND(3.14159, 2) rd, TRUNC(9.99) t, CEIL(1.2) c, SIGN(-3) s FROM dual", null),
    ];

    public static IEnumerable<object[]> CaseNames() => Cases.Select(c => new object[] { c.Name });

    [Fact]
    public void Has_AtLeast40Cases() => Assert.True(Cases.Length >= 40, $"only {Cases.Length} cases");

    [SkippableTheory]
    [MemberData(nameof(CaseNames))]
    public async Task PrintedSql_ReturnsSameRowsAsOriginal(string name)
    {
        db.SkipIfUnavailable();
        var (_, sql, binds) = Cases.Single(c => c.Name == name);
        await using var conn = await db.OpenAppAsync();

        var original = await OracleFixture.QueryAsync(conn, sql, binds); // throws if the test SQL itself is invalid

        var parsed = SqlParser.Parse(sql);
        Assert.True(parsed.Success, $"parser rejected: {string.Join("; ", parsed.Errors)}");
        var printed = SqlWriter.ToSql(parsed.Script.Statements[0]);

        ResultSet reprinted;
        try { reprinted = await OracleFixture.QueryAsync(conn, printed, binds); }
        catch (Exception e) { throw new Xunit.Sdk.XunitException($"Oracle rejected printed SQL.\nORIGINAL: {sql}\nPRINTED:  {printed}\n{e.Message}"); }

        Assert.True(original.Rows.Count > 0, "test query should return rows");
        Assert.True(original.Columns.SequenceEqual(reprinted.Columns), $"column names differ\nORIGINAL: {sql}\nPRINTED:  {printed}\n{string.Join(",", original.Columns)}\nvs\n{string.Join(",", reprinted.Columns)}");
        Assert.True(original.Rows.Count == reprinted.Rows.Count && original.Rows.Zip(reprinted.Rows).All(p => p.First.SequenceEqual(p.Second)),
            $"rows differ\nORIGINAL: {sql}\nPRINTED:  {printed}\n--- original\n{original.Render()}\n--- printed\n{reprinted.Render()}");
    }

    public static IEnumerable<object[]> GeneratorCaseNames() =>
        Cases.SelectMany(c => GeneratorOptionSets.All.Select(o => new object[] { c.Name, o.Name }));

    /// <summary>Same comparison, but the SQL is printed by SqlScriptGenerator with the defaults, the fully formatted and the lower-case option sets.</summary>
    [SkippableTheory]
    [MemberData(nameof(GeneratorCaseNames))]
    public async Task GeneratedSql_ReturnsSameRowsAsOriginal(string name, string optionSet)
    {
        db.SkipIfUnavailable();
        var (_, sql, binds) = Cases.Single(c => c.Name == name);
        await using var conn = await db.OpenAppAsync();

        var original = await OracleFixture.QueryAsync(conn, sql, binds);

        var parsed = SqlParser.Parse(sql);
        Assert.True(parsed.Success, $"parser rejected: {string.Join("; ", parsed.Errors)}");
        var options = GeneratorOptionSets.Get(optionSet)();
        options.IncludeSemicolons = false; // ExecuteReader takes a bare statement
        var generated = new SqlScriptGenerator(options).Generate(parsed.Script.Statements[0]);

        ResultSet regenerated;
        try { regenerated = await OracleFixture.QueryAsync(conn, generated, binds); }
        catch (Exception e) { throw new Xunit.Sdk.XunitException($"Oracle rejected generated SQL ({optionSet}).\nORIGINAL:  {sql}\nGENERATED: {generated}\n{e.Message}"); }

        Assert.True(original.Columns.SequenceEqual(regenerated.Columns), $"column names differ ({optionSet})\nORIGINAL:  {sql}\nGENERATED: {generated}\n{string.Join(",", original.Columns)}\nvs\n{string.Join(",", regenerated.Columns)}");
        Assert.True(original.Rows.Count == regenerated.Rows.Count && original.Rows.Zip(regenerated.Rows).All(p => p.First.SequenceEqual(p.Second)),
            $"rows differ ({optionSet})\nORIGINAL:  {sql}\nGENERATED: {generated}\n--- original\n{original.Render()}\n--- generated\n{regenerated.Render()}");
    }
}
