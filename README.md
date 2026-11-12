# OracleSqlParser

A hand-written lexer and recursive-descent parser for Oracle SQL that builds an abstract syntax tree
(similar in spirit to Microsoft's `TSqlParser`/ScriptDom). .NET 10, no dependencies.

## Library

```csharp
var result = SqlParser.Parse("SELECT e.name FROM emp e WHERE e.sal > :min");
if (!result.Success) foreach (var err in result.Errors) Console.WriteLine(err); // (line:col) message
foreach (var table in result.Script.Descendants().OfType<NamedTable>()) ...
string json = AstJson.Serialize(result.Script, includePositions: true);
```

- `SqlParser.Parse` handles multi-statement scripts (`;` or `/`), collects errors and resyncs at the next `;`.
- `SqlParser.ParseStatement` / `ParseExpression` throw `ParseException` instead.
- Every `SqlNode` has `Start`/`End`/`Line`/`Column`, `Children()` and `Descendants()`; subclass `SqlVisitor` to walk.
- Unquoted identifiers are upper-cased in `Identifier.Value`; quoted ones keep case (`Quoted = true`).

## CLI

```
dotnet run --project src/OracleSqlParser.Cli -- [--positions] [--compact] [-f file | -e "sql"]   # or stdin
```
Prints the AST as JSON; syntax errors go to stderr with exit code 1.

## Coverage

See [COVERAGE.md](COVERAGE.md): every statement in Oracle's statement index, the node that represents it, whether
it is fully typed or a typed header with a token-tree body, and the known gaps. `dotnet run --project src/OracleSqlParser.Cli -- --shape -e "..."`
prints the compact tree used by the shape tests.

## Tests

`tests/OracleSqlParser.Tests`: hand-written expected trees (`*ShapeTests.cs`, compared via `AstPrinter`) plus a smoke
corpus (`SmokeCorpusTests.cs`) that only checks inputs parse.

## Validating and restricting user-supplied queries

```csharp
var policy = new QueryPolicy
{
    AllowedTables = new HashSet<string>(["ORDERS", "CUSTOMERS"], StringComparer.OrdinalIgnoreCase),
    AllowedFunctions = new HashSet<string>(["UPPER", "LOWER", "TRUNC", "NVL"], StringComparer.OrdinalIgnoreCase), // safest
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

`g.Sql` is `SELECT Q.* FROM (<user query>) Q WHERE Q.ID IN (<access query>)`: the restriction is applied *outside* the
user's query, so `OR`, `UNION` and `GROUP BY` inside it cannot bypass it. `QueryGuard.Validate` checks without wrapping.
`SqlWriter.ToSql(node)` prints any SELECT AST back to Oracle SQL (precedence-aware parentheses, so trees you edit
print correctly); non-query statements throw `NotSupportedException`.

Limits: output is Oracle SQL only (T-SQL needs ScriptDom); the generated SQL is verified by re-parsing, not by running it
against a database; the user's top-level `ORDER BY` is moved outside the wrapper only when expressible on output columns.

## Integration tests

`tests/OracleSqlParser.IntegrationTests` runs against a real Oracle database (the free `gvenzl/oracle-free:23-slim-faststart`
image, started with Testcontainers; **Docker is required**):

```
dotnet test tests/OracleSqlParser.IntegrationTests
```

One container is started per test run (first run pulls the image; allow a few minutes). They check that (a) the SQL printed
by `SqlWriter` is accepted by Oracle and returns the same columns and rows as the original query, (b) `QueryGuard.RestrictToIds`
output executes and only returns permitted ids for both `AccessJoinStyle` values, and (c) every statement of the smoke
corpus passes Oracle's own syntax check inside a scratch schema. If Docker is not available the tests are skipped, not failed.
The default unit tests (`dotnet test tests/OracleSqlParser.Tests`) do not need Docker.
