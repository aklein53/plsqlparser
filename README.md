# OracleSqlParser

A hand-written lexer and recursive-descent parser for Oracle SQL that builds an abstract syntax tree
(similar in spirit to Microsoft's `TSqlParser`/ScriptDom). Targets net8.0 and net10.0, no runtime dependencies.

## Installing

```
dotnet add package PlsqlParser --prerelease
```

The NuGet package is `PlsqlParser`, while the C# namespace (and assembly) is `OracleSqlParser`.

Targets `net8.0` and `net10.0`. Versions are published as pre-releases (`0.1.0-preview.N`) until the API stabilises.

## Releasing

Publishing uses nuget.org Trusted Publishing (GitHub OIDC) bound to `.github/workflows/build.yml`; no API key
secret is required.

1. Bump `<Version>` in `src/OracleSqlParser/OracleSqlParser.csproj` (e.g. `0.1.0-preview.2`) and commit.
2. Tag the commit with the matching `v`-prefixed version: `git tag vX.Y.Z-preview.N` (e.g. `v0.1.0-preview.2`).
3. Push the tag: `git push origin vX.Y.Z-preview.N`.

The `Build` workflow runs on every push and pull request; its `publish` job runs only on `v*` tags. It checks that
the tag matches the csproj `<Version>`, then exchanges the GitHub OIDC token for a short-lived key and pushes the
package built by the `build` job to nuget.org.

## Library

```csharp
var result = SqlParser.Parse("SELECT e.name FROM emp e WHERE e.sal > :min");
if (!result.Success) foreach (var err in result.Errors) Console.WriteLine(err); // (line:col) message
foreach (var table in result.Script.Descendants().OfType<NamedTable>()) ...
string json = AstJson.Serialize(result.Script, includePositions: true);
```

- `SqlParser.Parse` handles multi-statement scripts (`;` or `/`), collects errors and resyncs at the next `;`.
- `SqlParser.ParseStatement` / `ParseExpression` throw `ParseException` instead.
- Every `SqlNode` has `Start`/`End`/`Line`/`Column`, `Children()` and `Descendants()`; subclass `SqlNodeVisitor` to walk (see below; `SqlVisitor` is the legacy untyped visitor). Spans are set by the parser; nodes you build in code have zero spans.
- Unquoted identifiers are upper-cased in `Identifier.Value`; quoted ones keep case (`Quoted = true`).

SQL*Plus scripts: `&name` and `&&name` are substitution variables by default. A `SET DEFINE OFF` line (or `SET DEF OFF`) turns substitution off for the rest of the input, so `&` is then ordinary text, as it already is inside string literals; `SET DEFINE ON` turns it back on, and `SET DEFINE <char>` (for example `SET DEFINE ^`) makes that character the prefix instead. Only a `SET` that starts a line and is followed by nothing but the value counts, so `UPDATE t SET define = 1` is unaffected. For plain SQL that is not a script, `new ParseOptions { SubstitutionEnabled = false }` turns substitution off from the start.

## Targeting older Oracle versions

`ParseOptions.OracleVersion` names the release a script must run on: `Oracle19`, `Oracle21` or `Oracle23`. The default is `Oracle23`, so nothing changes unless you set it. For an older target, constructs introduced later are still parsed and kept in the tree, but each one adds a `ParseError`, for example `'IS TRUE/FALSE' requires Oracle 23 or later (targeting Oracle 19)`. Check `Errors` as usual; the tree is still usable for tooling. `ParseStatement` and `ParseExpression` throw on the first such diagnostic, like any other error.

```csharp
var r = SqlParser.Parse("SELECT 1 FROM dual WHERE (1 = 1) IS TRUE", new ParseOptions { OracleVersion = OracleVersion.Oracle19 });
// r.Errors: one error, "'IS TRUE/FALSE' requires Oracle 23 or later (targeting Oracle 19)"
```

SQL_MACRO is gated at 21, although table macros were backported to 19.7 and later release updates. Users on a patched 19c should target `Oracle21` or ignore that diagnostic.

The gated constructs and their versions are listed in [COVERAGE.md](https://github.com/aklein53/plsqlparser/blob/main/COVERAGE.md#version-gates).

## CLI

```
dotnet run --project src/OracleSqlParser.Cli -- [--positions] [--compact] [-f file | -e "sql"]   # or stdin
```
Prints the AST as JSON; syntax errors go to stderr with exit code 1.

## Coverage

See [COVERAGE.md](https://github.com/aklein53/plsqlparser/blob/main/COVERAGE.md): every statement in Oracle's statement index, the node that represents it, whether
it is fully typed or a typed header with a token-tree body, and the known gaps. `dotnet run --project src/OracleSqlParser.Cli -- --shape -e "..."`
prints the compact tree used by the shape tests.

## Tests

`tests/OracleSqlParser.Tests` (and `tests/OracleSqlParser.Guard.Tests` for the guard): hand-written expected trees (`*ShapeTests.cs`, compared via `AstPrinter`) plus a smoke
corpus (`SmokeCorpusTests.cs`) that only checks inputs parse.

## Query guard (experimental, separate package)

An experimental package, `PlsqlParser.Guard`, validates and rewrites user-supplied SELECT queries. It is **not a
security boundary**; read its [README](https://github.com/aklein53/plsqlparser/blob/main/src/OracleSqlParser.Guard/README.md) before using it.


## Generating SQL

`SqlScriptGenerator` (the equivalent of ScriptDom's `SqlScriptGenerator`) prints any node the parser produces back to
Oracle SQL / PL/SQL: scripts, queries, DML, DDL (including the token-tree bodies), transaction/session statements, PL/SQL
blocks and program units, SQL*Plus commands. The output parses back to an identical tree (verified over the whole test
corpus). Parentheses are added from operator precedence, so trees you build or edit print correctly; a node type the
generator does not know throws `NotSupportedException` naming it.

```csharp
var script = SqlParser.Parse("select e.name, d.name dname from emp e left join dept d on e.d = d.id where e.sal > 100 order by e.name").Script;
var generator = new SqlScriptGenerator(new SqlScriptGeneratorOptions
{
    NewLineBeforeFromClause = true, NewLineBeforeJoinClause = true, NewLineBeforeWhereClause = true,
    NewLineBeforeOrderByClause = true, MultilineSelectElementsList = true,
});
Console.WriteLine(generator.Generate(script));
```

```
SELECT
    e.name,
    d.name AS dname
FROM emp e
LEFT OUTER JOIN dept d ON e.d = d.id
WHERE e.sal > 100
ORDER BY e.name;
```

`Generate(node)` returns a string; `Generate(node, textWriter)` writes to a writer. Any node can be generated, not only scripts.
Instances are immutable (options are copied) and thread-safe.

| Option | Default | Effect |
|---|---|---|
| `KeywordCasing` | `Uppercase` | `Uppercase` / `Lowercase` keywords |
| `IdentifierCasing` | `Preserve` | `Preserve` prints unquoted names as typed (`Identifier.Text`), `Uppercase` / `Lowercase` normalise; quoted names are always verbatim |
| `IndentSize` | `4` | Spaces per indentation level |
| `NewLineBeforeFromClause`, `...WhereClause`, `...HierarchicalClause`, `...GroupByClause`, `...HavingClause`, `...OrderByClause`, `...JoinClause`, `...SetOperator` | `false` | Start that clause on a new line (SELECT; FROM/WHERE also apply to UPDATE/DELETE/MERGE; ORDER BY also covers OFFSET/FETCH/FOR UPDATE) |
| `NewLineBeforeSetClause`, `...ValuesClause`, `...ReturningClause` | `false` | UPDATE ... SET / MERGE WHEN, INSERT VALUES/SELECT/INTO branches, RETURNING and LOG ERRORS on a new line |
| `MultilineSelectElementsList` | `false` | One select-list element per line below `SELECT` |
| `MultilineColumnDefinitions` | `false` | One column/constraint per line in `CREATE TABLE` |
| `MultilinePlsql` | `false` | PL/SQL declarations, statements, handlers and member lists each on an indented line |
| `AlignClauseBodies` | `false` | Clause body on its own indented line below `SELECT` / `FROM` / `WHERE` / `GROUP BY` / `HAVING` / `ORDER BY` |
| `IncludeSemicolons` | `true` | Script generation: `;` after each statement |
| `SqlPlusSlashAfterPlsql` | `false` | Script generation: a `/` line after PL/SQL units (blocks, `CREATE PROCEDURE/FUNCTION/PACKAGE/TRIGGER/TYPE`); Java sources always get their required `/` |

The parser keeps the source spelling that is not part of the meaning: `q'[it's]'` / `nq'...'` literals, `N'x'`, `:"Bind"`,
number literals and optimizer hint text print back as written.

Limitations:

- Comments are not part of the AST and are **not printed** (trivia is planned, see the roadmap).
- Token-tree bodies (long-tail DDL/admin syntax: `ALTER USER`, `CREATE TABLESPACE`, `ANALYZE`, SQL*Plus arguments, ...) are printed as their tokens,
  separated by single spaces (no space around `.`, `@`, `%` and before `(`), so their original line layout is lost. Words in them follow `IdentifierCasing`, not `KeywordCasing`.
- Data type names are stored normalised, so they print with the keyword casing, not as typed. Quoted parts of a dotted type name are not preserved.
- Without `IncludeSemicolons` a multi-statement script no longer parses (statements are only separated by newlines).
- Some purely optional spellings are not stored and are normalised: `ASC`, `VIRTUAL`, `1..3` vs `1 .. 3`, `RETURN`/`RETURNING`, `JSON DUALITY VIEW` as `JSON RELATIONAL DUALITY VIEW`, `PIPELINED ROW POLYMORPHIC`.

`SqlWriter.ToSql(node)` remains as a thin compatibility facade: compact output, upper-case keywords and upper-case unquoted
identifiers (it is not marked `[Obsolete]` so that dependent code builds without warnings). New code should use `SqlScriptGenerator`.

## Examining and editing trees

Modelled on ScriptDom: typed visitor callbacks, an in-place rewriter, no reflection.

```csharp
// Examine: override Visit(T) for the node types you care about, then root.Accept(visitor).
sealed class TableCollector : SqlNodeVisitor
{
    public List<string> Names { get; } = [];
    public override void Visit(NamedTable node) => Names.Add(string.Join(".", node.Name.Select(i => i.Value)));
}
var tables = new TableCollector();
SqlParser.Parse("SELECT * FROM a JOIN s.b ON a.x = b.x").Script.Accept(tables);   // A, S.B

// Edit: Rewrite(T) returns the node (edited), a replacement, or null to remove it (from a list, or an optional property).
sealed class TenantFilter(string predicate) : SqlNodeRewriter
{
    public override SqlNode? Rewrite(QuerySpecification node)
    {
        base.Rewrite(node);                                   // rewrite nested subqueries first
        var p = SqlParser.ParseExpression(predicate);
        node.Where = node.Where is null ? p : new BinaryExpression { Left = node.Where, Operator = "AND", Right = p };
        return node;
    }
}
var script = SqlParser.Parse("SELECT a FROM t WHERE x = 1 OR y = 2").Script;
new TenantFilter("tenant = 7").RewriteTree(script);                     
new SqlScriptGenerator().Generate(script);                                       // SELECT a FROM t WHERE (x = 1 OR y = 2) AND tenant = 7;
```

- `ExplicitVisit(T)` (default: `Visit(node)`, then `node.AcceptChildren(this)`) is where to change how the walk descends, as in ScriptDom's `TSqlFragmentVisitor`.
- A rewriter changes the tree in place. Keep the original with `node.DeepClone<T>()` first. A wrong replacement type, or removing a required child, throws `InvalidOperationException` naming the property.
- `SqlNodeComparer.StructurallyEqual(a, b)` compares two trees ignoring source positions and spelling-only properties (`Identifier.Text`, `q'[...]'` delimiters).
- Visitors and rewriters recurse once per tree level, so a very deep left-leaning chain (more than about 5,000 `a AND b AND ...` terms) can exhaust the stack. For such trees use the iterative `node.Descendants()` (which also handles 20,000-term chains in well under a second); `StructurallyEqual` is iterative too.
- `GetChildren()`, `Accept`, `AcceptChildren`, the typed `Visit`/`ExplicitVisit`/`Rewrite` overloads, cloning and comparison are **generated** into `src/OracleSqlParser/Generated/*.g.cs` by `tools/AstGen`.
  After adding or changing an AST node class or property, regenerate and commit the result:

  ```
  dotnet run --project tools/AstGen
  ```

  The tool builds the library without `Generated/` in a temporary copy and reflects over it, so it works even when the generated files are stale. The unit test `GeneratedCode_IsUpToDate` fails CI if you forget.

### Format-preserving edits

`SourceEditor` edits the original text instead of reprinting the tree, so comments, whitespace, line breaks and casing
outside the edited ranges survive. `SqlScriptGenerator` prints only the replacement and inserted text.

```csharp
var parsed = SqlParser.Parse("SELECT /* keep */ a  ,  b\nFROM   orders o -- trailing\nWHERE x = 1");
var query = parsed.Script.Descendants().OfType<QuerySpecification>().First();
var editor = new SourceEditor(parsed);
editor.Replace(((NamedTable)query.From[0]).Name[0], "employees");   // table name only; the alias o stays
editor.Remove(query.SelectList[1]);                                  // removes "  ,  b"
editor.AndWhere(query, SqlParser.ParseExpression("o.active = 1"));   // SourceEdits.AndWhere
Console.WriteLine(editor.Apply());
```

```
SELECT /* keep */ a
FROM   employees o -- trailing
WHERE (x = 1) AND (o.active = 1)
```

- `Replace(node, replacement)` prints a node with the editor's `SqlScriptGeneratorOptions`; `Replace(node, text)` inserts text as written.
  `InsertBefore`, `InsertAfter` and `Remove` work the same way. `Apply()` returns the edited text.
- A node's range runs from its first to its last character. Comments before a node and after its last token stay in place.
- `Remove` on an item of a comma-separated list also removes its comma (see the `Remove` remarks), so the list stays valid.
  Removing several items of one list works in any order and combination: `a, b, c` minus `b` and `c` gives `a`, and minus `a`
  and `c` gives `b`. Removals may overlap; they are merged when `Apply()` runs. Removing every item of a list is not valid SQL and is left to the caller.
- A statement's range excludes its `;`, so `InsertAfter(statement, text)` puts `text` before the terminator.
  Include the terminator in the text to add a statement after another.
- Nodes built in code, or produced by `ParseStatement`/`ParseExpression`, have no source position and throw
  `InvalidOperationException`, as do replacements and inserts whose ranges overlap. Two inserts at one offset are kept in the order they were added.
- `SourceEdits.AndWhere(query, predicate)` adds a condition with AND. An existing WHERE is wrapped in parentheses, so an `OR`
  stays grouped: `WHERE x = 1 OR z = 3` becomes `WHERE (x = 1 OR z = 3) AND (predicate)`. Without a WHERE, the condition is inserted after the FROM clause.

## Tokens and comments

Modelled on ScriptDom's `ScriptTokenStream`: the parse result keeps every token, and every node knows which tokens it covers.

```csharp
var r = SqlParser.Parse("SELECT /* c1 */ a -- c2\nFROM t");
foreach (var tok in r.Tokens)                                     // every token, hints and EOF included, in source order
    Console.WriteLine($"{tok.Text} preceded by {tok.LeadingTrivia.Count} trivia");

var stmt = r.Script.Statements[0];
Console.WriteLine(r.Tokens[stmt.FirstTokenIndex].Text);           // SELECT
Console.WriteLine(r.Tokens[stmt.LastTokenIndex].Text);            // t
```

- `ParseResult.Tokens` is the complete lexer output. Optimizer hints the parser does not use are still listed there, though they are absent from the parser's own view of the stream.
- `Token.LeadingTrivia` lists the whitespace and comments (`TriviaKind.Whitespace`, `LineComment`, `BlockComment`) between the previous token and this one. Each `Trivia` has its `Text` and source `Start`/`End`.
- `SqlNode.FirstTokenIndex` and `LastTokenIndex` are indexes into `ParseResult.Tokens`. `Tokens[node.FirstTokenIndex].Start == node.Start` and `Tokens[node.LastTokenIndex].End == node.End` for every parsed node. -1 means the node has no position in `ParseResult.Tokens`: this covers nodes built in code, nodes from `ParseStatement`/`ParseExpression`, and the argument nodes inside a `/*+ ... */` hint (the hint's own `HintClause`/`HintItem` do get indexes).
- Reconstruction invariant: for every token in order, append its leading trivia text, then `source[token.Start..token.End]`. The result is the original input exactly, so no text between tokens is lost.

## Roadmap

See [ROADMAP.md](https://github.com/aklein53/plsqlparser/blob/main/ROADMAP.md) for planned work.

## Integration tests

`tests/OracleSqlParser.IntegrationTests` runs against a real Oracle database (the free `gvenzl/oracle-free:23-slim-faststart`
image, started with Testcontainers; **Docker is required**):

```
dotnet test tests/OracleSqlParser.IntegrationTests
```

One container is started per test run (first run pulls the image; allow a few minutes). They check that (a) the SQL printed
by `SqlScriptGenerator` is accepted by Oracle and returns the same columns and rows as the original query, (b) the experimental `QueryGuard.RestrictToIds`
output executes and returns only permitted ids in the tested scenarios for both `AccessJoinStyle` values, and (c) every statement of the smoke
corpus passes Oracle's own syntax check inside a scratch schema (PL/SQL-only forms such as `SELECT ... INTO` are asserted to be rejected), and (d) generator verification: every corpus statement that Oracle accepts
is regenerated with `SqlScriptGenerator` using the default, the fully formatted and the lower-case option sets, and each generated text must be classified by Oracle like
the original (never rejected); the 62 result-comparing queries are also printed with all three option sets and must return identical rows
(`GeneratorAcceptanceTests`, `WriterRoundTripTests`; known gaps, if any, are listed with a reason in `KnownGeneratorGaps`). Container startup is capped at 8 minutes (override with `ORACLE_TEST_STARTUP_TIMEOUT_SECONDS`); on timeout the tests are skipped with a clear message. If Docker is not available the tests are skipped, not failed.
The default unit tests (`dotnet test tests/OracleSqlParser.Tests`) do not need Docker.
