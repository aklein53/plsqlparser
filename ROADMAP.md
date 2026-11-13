# Roadmap

Product goal: an Oracle-dialect equivalent of Microsoft's `SqlScriptDom` — a library to **parse** Oracle SQL and
PL/SQL into a typed syntax tree, **examine** and **edit** that tree, and **output** the resulting SQL. Anything beyond
what ScriptDom offers (policy validation, access rewriting) is a nice-to-have outside the core.

Status at 0.1.0-preview.1: parsing is broad and precedence-correct (no mis-parses across ~450 doc-verified probes;
62 printed queries verified row-for-row against Oracle 23 Free). Printing covers SELECT only; there is no token
stream / trivia; the visitor is untyped and reflection-based; fuzzing found one hang, five crash classes and
process-fatal recursion.

Findings come from four independent reviews (DX/API, syntax coverage vs. Oracle 23 docs, security, ~6M-input
fuzz) and were re-verified by hand where cited.

## Epic 0 — Library robustness (table stakes) · ~1 week

| # | Defect (verified) | Fix |
|---|---|---|
| 0.1 | Infinite loop: `SELECT LAG(x, 1) IGNORE` (function-clause loop never advances) | Loop must consume or fail; global no-progress guard |
| 0.2 | Stack overflow kills the process at ~300 nesting levels on a thread-pool thread (parens, subqueries, CASE, calls); writer/printer overflow on 6–7k-term `AND`/`||`/`UNION` chains | Depth counter → `ParseException` (default 200, configurable via `ParseOptions`); iterative traversal in `Descendants`/writer/printer for left-deep chains |
| 0.3 | `Parse` throws undocumented `InvalidCastException` (`*` in MODEL/MATCH_RECOGNIZE/XMLTABLE item lists), `FormatException`/`OverflowException` (`INTERVAL '' YEAR(3.`) | Type checks at the cast sites; `TryParse` for precision; fuzz harness becomes a regression test |
| 0.4 | `Descendants()` is O(n·depth) (12 s on a 20k-term AND chain) | Explicit stack; generated children (Epic 2) |
| 0.5 | `AstJson.Serialize` throws at depth 64 | Raise `MaxDepth` |
| 0.6 | Recovery loses statements after an error when separated only by `/`; a lexer error drops the rest of the script; negative error columns | Resync on lone `/`; resume lexing on the next line after an unterminated literal; fix column math |
| 0.7 | Fidelity: `UTL_HTTP.REQUEST(...).foo()` parses as `FOO()` — the receiver is dropped | `MethodCall { Target, Name, Arguments }` node |

## Epic 1 — Script generation for every statement (ScriptDom's `SqlScriptGenerator`) · ~3 weeks

- `SqlWriter` → `SqlScriptGenerator` covering DML, DDL (incl. token-tree bodies), transaction/session, PL/SQL
  blocks and program units, SQL*Plus commands. Verified by parse → print → parse tree equality over the whole
  corpus and by execution against Oracle in the integration suite.
- `SqlScriptGeneratorOptions`: keyword case, identifier case (**preserve** needs the original lexeme kept on
  `Identifier`), indentation, newline before clauses, alignment, max line width — modelled on ScriptDom's options.
- Keep `q'…'` literal form, `N'…'` prefix, bind quoting (`:"b"`) — today these are normalised away.
- `WITH FUNCTION` selects must print (parses today, throws on print).

## Epic 2 — Typed visitor and generated tree plumbing (ScriptDom's `TSqlFragmentVisitor`) · ~2 weeks

- Source-generate per node type: `Accept(visitor)`, `Visit(T)` + `ExplicitVisit(T)` on an abstract `SqlFragmentVisitor`
  (matching ScriptDom's model), and `GetChildren` (removes reflection: faster, AOT/trim-safe).
- `SqlRewriter`/`ConcreteVisitor` that returns replacements (copy-on-write) for editing scenarios.
- `required` members instead of `= null!` (~120 sites) so a hand-built tree fails at compile time, not in the printer.
- Span setters internal; synthetic nodes marked; `ParseResult.Errors` read-only.
- Thread-safety documented: parsing is thread-safe; trees are mutable and not.

## Epic 3 — Token stream and trivia (ScriptDom's `ScriptTokenStream`) · ~2 weeks

- Lexer retains comments and whitespace as trivia; `ParseResult.Tokens`; every node gets `FirstTokenIndex` /
  `LastTokenIndex` (ScriptDom's model) in addition to character spans.
- Format-preserving rewrite: `SourceEditor` applies edits by span so untouched user text, comments and layout
  survive; generator used only for new/replaced nodes.
- Honour `SET DEFINE OFF` for `&` handling in scripts.

## Epic 4 — Syntax coverage (all rejections today; none are mis-parses) · S/M each

In likelihood order: `IS [NOT] TRUE/FALSE`; interval expressions `(a - b) DAY TO SECOND`; JSON dot-notation after
an array step (`t.c.a[0].b`, `[0 to 2]`); lexer bug `'a':1` in `JSON_OBJECT` (reads `:1` as a bind);
`RETURNING OLD/NEW`; `LOG ERRORS` on MERGE; PL/SQL conditional compilation (`$IF`/`$$`/`$ERROR`);
`DELETE FROM t FROM <join>`; partitioned outer join; `WITH x AS (VALUES …)`; `FETCH APPROX FIRST`;
`FOR i IN 1..10 BY 2` and 21c iterator controls; qualified expressions (dedicated node, not `FunctionCall`);
`arr(1)(2)`; `EXEC :x := 1`; `PERMUTE`; `pivot`/`left`/`sample`/`fetch` as aliases; reject `**`/`%` in SQL context.
Replace remaining token-tree bodies (generic CREATE/ALTER, ANALYZE/AUDIT, GRAPH_TABLE) with typed nodes over time.

Also: a `ParseOptions.OracleVersion` (19/21/23) to reject newer syntax when targeting older servers, mirroring
ScriptDom's versioned parsers.

## Epic 5 — API shape and packaging · S/M

- `ParseError`: code, length, expected-token set, caret formatter; `TryParse`, `ParseSelect`.
- Discovery helpers as a separate `SqlAnalysis` class (`GetTables`, `GetColumns`, `GetBinds`, `GetFunctions`).
- Resolve the three-name problem before 1.0 (package `PlsqlParser`, assembly/namespace `OracleSqlParser`).
- Absolute links in the NuGet README; fix ".NET 10, no dependencies" wording; CLI as a `dotnet tool`;
  XML docs on entry points; `IsPackable=false` on test projects.

## Out of core: QueryGuard

`QueryGuard`/`QueryPolicy`/`AccessFilter` are beyond ScriptDom's scope and carry security claims the reviews
showed to be false (output-only key filtering is bypassed by self-joins, scalar subqueries and `EXISTS`; CTE
names are scoped globally; the default function policy is fail-open; `XMLTABLE`/`CURSOR`/flashback/`SAMPLE`
read around the allowlist; hints pass through). Decision: move to a separate experimental package
(`PlsqlParser.Guard`) or reduce to a sample, and remove the security claims from the README immediately.
The security review's findings are the backlog for that package if it continues.

## Suggested order

1. Epic 0 + README claim fix + QueryGuard relocation → **0.1.0-preview.2**.
2. Epic 1 (generator for all statements, options) → **0.2.0-preview**.
3. Epic 2 (typed visitor, generated plumbing) → **0.3.0-preview**.
4. Epic 3 (token stream/trivia) + Epic 4 top ten → **0.4.0-preview**.
5. Epic 5 → **1.0**.
