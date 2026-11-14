# OracleSqlParser.Guard (EXPERIMENTAL)

Policy validation and access-restriction rewriting for user-supplied Oracle SELECT queries, built on
[OracleSqlParser](https://github.com/aklein53/plsqlparser) (namespace `OracleSqlParser.Guard`).

Targets net8.0 and net10.0. It depends on the `OracleSqlParser` package, which NuGet installs automatically at the same
version, so installing this package alone is enough.

All public types are marked `[Experimental("PLSQLGUARD001")]`: using them is a compile error until you opt in with
`<NoWarn>PLSQLGUARD001</NoWarn>` (or `#pragma warning disable PLSQLGUARD001`). The API may change or be removed.

## Limitations / not a security boundary

**Do not rely on this package to protect data from an untrusted SQL author.** The security review found the following
known weaknesses. Treat the guard as a lint / convenience layer on top of a real database-side boundary.

- **`RestrictToIds` filters only the output key column.** The wrapper is
  `SELECT Q.* FROM (<user query>) Q WHERE Q.ID IN (<access query>)`, so rows are restricted only by the value that ends up
  in the output key column. Self-joins, scalar subqueries, correlated `EXISTS`, aliasing (`secret AS id`), and
  `MATCH_RECOGNIZE` / `UNPIVOT` / `MODEL` over the protected table can leak data from other rows. A `FETCH` inside the user
  query pages the rows before the filter is applied.
- **CTE names are resolved globally.** A CTE named like an unlisted table exempts the real table when it is referenced
  outside the CTE's scope.
- **Quoted identifiers are matched case-insensitively**, although Oracle treats `"orders"` and `ORDERS` as different objects.
- **`AllowedFunctions == null` is fail-open.** Unqualified user-defined functions, public synonyms (for example `getxml`),
  `SYS_CONTEXT`, `BFILENAME` and similar pass the default rule. Only an explicit allow-list is restrictive.
- **Not denied:** `XMLTABLE` / `XMLQUERY` XQuery (`ora:view`, `fn:collection`), `CURSOR()`, `CAST(MULTISET(...))`, `TABLE()`,
  `LATERAL`, flashback `AS OF` / `VERSIONS`, `SAMPLE`, and `PARTITION` clauses.
- **Optimizer hints are copied verbatim** into the generated SQL.
- **No cost controls:** cartesian joins, `CONNECT BY` without `PRIOR`, recursive CTEs and huge `FETCH` counts are all accepted.
- **Parser bug:** `pkg.f(x).m()` currently drops the receiver in the AST, so the function check can miss it. This is being
  fixed in the core library.

### Recommended boundary

Use Oracle's own mechanisms as the real security boundary: Virtual Private Database (row-level security policies),
secure views over the permitted data, and a minimally privileged database user with resource limits (profiles / Resource
Manager). Use this package, if at all, as an additional layer.

### Intended fixes

- Per-table filter substitution (replace each protected table reference with a filtered subquery) instead of filtering
  the output.
- Scope-aware CTE resolution.
- Default-deny function policy.
- Hint stripping.
- Re-validation of the generated output.
- Row cap and other cost controls.

## Usage

```csharp
using OracleSqlParser.Guard;

var policy = new QueryPolicy
{
    AllowedTables = new HashSet<string>(["ORDERS", "CUSTOMERS"], StringComparer.OrdinalIgnoreCase),
    AllowedFunctions = new HashSet<string>(["UPPER", "LOWER", "TRUNC", "NVL"], StringComparer.OrdinalIgnoreCase), // use an allow-list
    RequiredColumns = ["ID"],
};
var filter = new AccessFilter
{
    KeyColumn = "ID",
    AccessSql = "SELECT order_id FROM user_orders WHERE user_id = :accessUserId",
};

GuardedSql g = QueryGuard.RestrictToIds(userSql, policy, filter);   // throws QueryRejectedException with all violations
var rows = connection.Query(g.Sql, new { accessUserId = 42, /* the user's own binds */ });
```

`g.Sql` is `SELECT Q.* FROM (<user query>) Q WHERE Q.ID IN (<access query>)`: the restriction is applied outside the user's
query, so `OR`, `UNION` and `GROUP BY` inside it do not remove the filter (but see the limitations above).
`QueryGuard.Validate` checks a query against the policy without wrapping it. The generated SQL is verified by re-parsing and,
in the repository's integration tests, by running it against Oracle 23 Free. The user's top-level `ORDER BY` is moved outside
the wrapper only when expressible on output columns. Output is Oracle SQL only.
