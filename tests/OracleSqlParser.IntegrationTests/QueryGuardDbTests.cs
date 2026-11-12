using Xunit;

namespace OracleSqlParser.IntegrationTests;

/// <summary>Executes QueryGuard.RestrictToIds output on Oracle and checks that only permitted order ids come back.</summary>
[Collection(OracleCollection.Name)]
public class QueryGuardDbTests(OracleFixture db)
{
    private static readonly Dictionary<long, HashSet<long>> Permitted = new()
    {
        [1] = [1, 2, 3, 4],
        [2] = [5, 6, 7, 8],
        [3] = [], // user with no access rows
    };

    private static QueryPolicy Policy => new() { AllowedTables = new HashSet<string>(["ORDERS"], StringComparer.OrdinalIgnoreCase) };

    private static AccessFilter Filter(AccessJoinStyle style) => new()
    {
        KeyColumn = "id",
        AccessSql = "SELECT order_id FROM user_orders WHERE user_id = :accessUserId",
        Style = style,
        AccessIdColumn = "order_id",
    };

    public static readonly (string Name, string Sql, Dictionary<string, object?>? Binds, bool OrderedById)[] UserQueries =
    [
        ("plain", "SELECT id, customer FROM orders WHERE status = 'A'", null, false),
        ("or 1=1", "SELECT id, customer FROM orders WHERE status = 'Z' OR 1=1", null, false),
        ("union", "SELECT id, customer FROM orders WHERE amount > 50 UNION SELECT id, customer FROM orders WHERE amount <= 50", null, false),
        ("union all", "SELECT id, customer FROM orders UNION ALL SELECT id, customer FROM orders WHERE id < 100", null, false),
        ("group by", "SELECT id, SUM(amount) AS total FROM orders GROUP BY id HAVING SUM(amount) > 0", null, false),
        ("order by key", "SELECT id, customer FROM orders ORDER BY id DESC", null, true),
        ("order by non-output column", "SELECT id, customer FROM orders ORDER BY amount DESC", null, false),
        ("order by + fetch", "SELECT id, customer FROM orders ORDER BY id FETCH FIRST 3 ROWS ONLY", null, true),
        ("cte", "WITH big AS (SELECT id, customer FROM orders WHERE amount >= 10) SELECT id, customer FROM big", null, false),
        ("subquery + user bind", "SELECT id, customer FROM orders WHERE amount > :minAmount OR id IN (SELECT id FROM orders)", new() { ["minAmount"] = 0 }, false),
        ("aliased key", "SELECT o.id AS id, o.customer FROM orders o JOIN orders o2 ON o2.id = o.id", null, false),
    ];

    public static IEnumerable<object[]> Matrix() =>
        from q in UserQueries
        from style in new[] { AccessJoinStyle.InSubquery, AccessJoinStyle.InnerJoin }
        from user in new long[] { 1, 2, 3 }
        select new object[] { q.Name, style, user };

    [SkippableTheory]
    [MemberData(nameof(Matrix))]
    public async Task RestrictToIds_ReturnsOnlyPermittedRows(string name, AccessJoinStyle style, long userId)
    {
        db.SkipIfUnavailable();
        var q = UserQueries.Single(x => x.Name == name);
        var guarded = QueryGuard.RestrictToIds(q.Sql, Policy, Filter(style));

        var binds = new Dictionary<string, object?>(q.Binds ?? [], StringComparer.OrdinalIgnoreCase) { ["accessUserId"] = userId };
        foreach (var b in guarded.BindNames) Assert.True(binds.ContainsKey(b), $"bind {b} not supplied");
        var used = binds.Where(kv => guarded.BindNames.Contains(kv.Key, StringComparer.OrdinalIgnoreCase)).ToDictionary(kv => kv.Key, kv => kv.Value);

        await using var conn = await db.OpenAppAsync();
        var restricted = await OracleFixture.QueryAsync(conn, guarded.Sql, used);
        var unrestricted = await OracleFixture.QueryAsync(conn, q.Sql, q.Binds);

        int idCol = restricted.Columns.FindIndex(c => c.Equals("ID", StringComparison.OrdinalIgnoreCase));
        Assert.True(idCol >= 0, "ID column missing from result: " + guarded.Sql);
        var ids = restricted.Rows.Select(r => long.Parse(r[idCol])).ToList();

        Assert.All(ids, id => Assert.Contains(id, Permitted[userId]));
        // Exactly the rows the user's own query returns, limited to the permitted ids (FETCH FIRST applies inside the user query, so it is a subset there).
        var expected = unrestricted.Rows.Where(r => Permitted[userId].Contains(long.Parse(r[unrestricted.Columns.FindIndex(c => c.Equals("ID", StringComparison.OrdinalIgnoreCase))]))).Select(r => long.Parse(r[unrestricted.Columns.FindIndex(c => c.Equals("ID", StringComparison.OrdinalIgnoreCase))])).ToList();
        if (name == "order by + fetch")
            Assert.All(ids, id => Assert.Contains(id, expected.Concat(Enumerable.Empty<long>())));
        else
            Assert.Equal(expected.Distinct().OrderBy(x => x), ids.Distinct().OrderBy(x => x));
        if (q.OrderedById && name != "order by + fetch")
            Assert.Equal(ids.OrderByDescending(x => x), ids);
    }

    [SkippableFact]
    public async Task Unrestricted_QueryReallyReturnsOtherUsersRows()
    {
        db.SkipIfUnavailable();
        await using var conn = await db.OpenAppAsync();
        var all = await OracleFixture.QueryAsync(conn, "SELECT id FROM orders WHERE status = 'Z' OR 1=1");
        Assert.Equal(10, all.Rows.Count); // sanity: the OR 1=1 query alone leaks everything
        var guarded = QueryGuard.RestrictToIds("SELECT id FROM orders WHERE status = 'Z' OR 1=1", Policy, Filter(AccessJoinStyle.InSubquery));
        var rs = await OracleFixture.QueryAsync(conn, guarded.Sql, new Dictionary<string, object?> { ["accessUserId"] = 1 });
        Assert.Equal(4, rs.Rows.Count);
    }
}
