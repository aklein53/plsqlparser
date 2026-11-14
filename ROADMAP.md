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
| 0.1 ✅ | Infinite loop: `SELECT LAG(x, 1) IGNORE` (function-clause loop never advances) | Loop must consume or fail; global no-progress guard |
| 0.2 ✅ | Stack overflow kills the process at ~300 nesting levels on a thread-pool thread (parens, subqueries, CASE, calls); writer/printer overflow on 6–7k-term `AND`/`||`/`UNION` chains | Depth counter → `ParseException` (default 200, configurable via `ParseOptions`); iterative traversal in `Descendants`/writer/printer for left-deep chains |
| 0.3 ✅ | `Parse` throws undocumented `InvalidCastException` (`*` in MODEL/MATCH_RECOGNIZE/XMLTABLE item lists), `FormatException`/`OverflowException` (`INTERVAL '' YEAR(3.`) | Type checks at the cast sites; `TryParse` for precision; fuzz harness becomes a regression test |
| 0.4 ✅ | `Descendants()` is O(n·depth) (12 s on a 20k-term AND chain) | Explicit stack; generated children (Epic 2) |
| 0.5 ✅ | `AstJson.Serialize` throws at depth 64 | Raise `MaxDepth` |
| 0.6 ✅ | Recovery loses statements after an error when separated only by `/`; a lexer error drops the rest of the script; negative error columns | Resync on lone `/`; resume lexing on the next line after an unterminated literal; fix column math |
| 0.7 ✅ | Fidelity: `UTL_HTTP.REQUEST(...).foo()` parses as `FOO()` — the receiver is dropped | `MethodCall { Target, Name, Arguments }` node |

## Epic 1 — Script generation for every statement (ScriptDom's `SqlScriptGenerator`) · ~3 weeks

- [x] `SqlWriter` → `SqlScriptGenerator` covering DML, DDL (incl. token-tree bodies), transaction/session, PL/SQL
  blocks and program units, SQL*Plus commands. Verified by parse → print → parse tree equality over the whole
  corpus (default, formatted and lower-case options) — and by execution against Oracle in the integration suite (`GeneratorAcceptanceTests`, `WriterRoundTripTests`; run separately, needs Docker).
- [x] `SqlScriptGeneratorOptions`: keyword case, identifier case (**preserve** keeps the original lexeme on
  `Identifier.Text`), indentation, newline before clauses, clause-body alignment — modelled on ScriptDom's options. [ ] Maximum line width.
- [x] Keep `q'…'` literal form, `N'…'` prefix, bind quoting (`:"b"`), number and hint text.
- [x] `WITH FUNCTION` selects print.
- [x] Iterative generation of left-deep chains (`AND`/`OR`/`||`/`+`, `UNION`, `JOIN`).

## Epic 2 — Typed visitor and generated tree plumbing (ScriptDom's `TSqlFragmentVisitor`) · ~2 weeks

- [x] Generate per node type (checked-in output of `tools/AstGen`, no Roslyn generator): `Accept(visitor)`, `Visit(T)` + `ExplicitVisit(T)` on an abstract `SqlNodeVisitor`
  (matching ScriptDom's model), and `GetChildren` (removes reflection from traversal: faster, AOT/trim-safe).
- [x] `SqlNodeRewriter` with typed `Rewrite(T)` returning replacements (in place; null removes) for editing scenarios; `DeepClone`; `SqlNodeComparer.StructurallyEqual`.
- [ ] `required` members instead of `= null!` (~120 sites) so a hand-built tree fails at compile time, not in the printer.
  Deferred: would force every parser construction site to use initializers; ScriptDom does not do this either.
- [x] Span setters internal (nodes built in code have zero spans); `ParseResult.Errors` read-only.
- [x] Thread-safety documented: parsing is thread-safe; trees are mutable and not.

## Epic 3 — Token stream and trivia (ScriptDom's `ScriptTokenStream`) · ~2 weeks

- [x] Lexer retains comments and whitespace as trivia (`Token.LeadingTrivia`).
- [x] `ParseResult.Tokens`: the complete token stream, including dropped hints and EOF.
- [x] Every node gets `FirstTokenIndex` / `LastTokenIndex` (ScriptDom's model) in addition to character spans.
- [x] Format-preserving rewrite: `SourceEditor` applies edits by span so untouched user text, comments and layout
  survive; generator used only for new/replaced nodes.
- [x] Honour `SET DEFINE OFF` for `&` handling in scripts.

## Epic 4 — Syntax coverage (all rejections today; none are mis-parses) · S/M each

- [x] `IS [NOT] TRUE/FALSE`; interval expressions `(a - b) DAY[(p)] TO SECOND[(p)]` / `YEAR TO MONTH` (`IntervalExpression`).
- [x] JSON dot-notation after array steps (`t.c.a[0].b`, `[*]`, `[0 to 2]`, `[1,3]`); `'a':1` in `JSON_OBJECT` no longer lexes `:1` as a bind.
- [x] `RETURNING OLD/NEW` (`ReturningItem`); `LOG ERRORS` on MERGE; 23ai `UPDATE ... FROM` / `DELETE ... FROM` joins.
- [x] Partitioned outer join (either side, not both, not FULL); `WITH x AS (VALUES ...)` (`ValuesQuery`); `FETCH APPROX[IMATE] FIRST`.
- [x] `pivot`/`unpivot`/`left`/`right`/`full`/`sample`/`fetch` as aliases where unambiguous.
- [x] PL/SQL 21c iteration controls (`BY`, multiple controls, `REPEAT`, `WHILE`/`WHEN`, `VALUES OF`/`INDICES OF`/`PAIRS OF`,
  typed/`MUTABLE` iterands, `(EXECUTE IMMEDIATE ...)` — verified against Oracle: the parenthesised form is the only one accepted).
- [x] Conditional compilation (`$IF`/`$ELSIF`/`$ELSE`/`$END`, `$ERROR`, `$$name`) in statement and declaration lists, all branches typed.
- [x] Qualified expressions with `OTHERS =>`, index choices and `FOR` iterator choices (`QualifiedExpression`; named
  `rec(a => 1)` stays a `FunctionCall`); chained calls `arr(1)(2)`, `f(x)(y)` (`ChainedCall`); `EXEC :x := 1`.
- [x] `**` rejected in SQL context (still parsed in PL/SQL). Note: `SqlParser.ParseExpression("2 ** 3")` now throws.
- [ ] Leftovers: conditional compilation in parameter lists; `FETCH APPROX ... WITH TARGET ACCURACY`; bare identifier
  JSON keys without a space (`JSON_OBJECT(k:1)`); JSON array steps are accepted inside PL/SQL although only SQL allows them.
- [ ] Replace remaining token-tree bodies (generic CREATE/ALTER, ANALYZE/AUDIT, GRAPH_TABLE) with typed nodes over time.
- [ ] `ParseOptions.OracleVersion` (19/21/23) to reject newer syntax when targeting older servers, mirroring
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
