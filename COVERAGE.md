# Coverage

Definition of done: every statement in Oracle's "Types of SQL Statements" index (Oracle Database 23, SQL Language
Reference, fetched 2026-11-10) plus the PL/SQL constructs below. Each row names the AST node and the test file
asserting its tree shape (tests are hand-written expected trees, see `AstPrinter`).

Legend: **typed** = fully modeled nodes. **header+tree** = typed header (verb, object kind, name) with the remaining
clauses as a nested token tree (`WordItem`/`LiteralItem`/`SymbolItem`/`GroupItem`), never a raw string.

## SQL statements (Oracle index)

| Statement | Node | Level | Tests |
|---|---|---|---|
| SELECT (all clauses, set ops, WITH incl. SEARCH/CYCLE/`WITH FUNCTION`, partitioned outer join, `FETCH [EXACT\|APPROX]`, PIVOT/LEFT/SAMPLE/FETCH as aliases where no clause can start; the vector-search `accuracy_clause` is not modeled) | `SelectStatement`, `QuerySpecification` | typed | QueryShape |
| PIVOT / UNPIVOT / MATCH_RECOGNIZE / MODEL / XMLTABLE / JSON_TABLE / flashback / SAMPLE / VALUES table (also as a CTE body) | `PivotTable` … `ValuesTable`, `ValuesQuery` | typed | QueryShape, MiscShape |
| GRAPH_TABLE | `GraphTable` | header+tree (body) | QueryShape |
| INSERT (single, multitable, RETURNING, LOG ERRORS, `VALUES rec`) | `InsertStatement`, `MultiTableInsertStatement` | typed | DmlAndSessionShape, MiscShape |
| UPDATE (incl. `SET ROW`, 23ai `FROM`, RETURNING `OLD`/`NEW`), DELETE (23ai `FROM` direct joins), MERGE (incl. `LOG ERRORS`) | `UpdateStatement` … | typed | DmlAndSessionShape, MiscShape |
| CALL, EXPLAIN PLAN, LOCK TABLE | `CallStatement`, `ExplainPlanStatement`, `LockTableStatement` | typed | DmlAndSessionShape |
| COMMIT, ROLLBACK, SAVEPOINT, SET TRANSACTION, SET CONSTRAINT(S) | `CommitStatement` … `SetConstraintsStatement` | typed | DmlAndSessionShape |
| ALTER SESSION (SET, ENABLE/DISABLE/FORCE, CLOSE DATABASE LINK, ADVISE, SYNC) | `AlterSessionStatement` | typed (`FeatureSyntax` token tree for unmodeled ENABLE/DISABLE features) | DmlAndSessionShape |
| SET ROLE | `SetRoleStatement` | typed | DmlAndSessionShape |
| GRANT / REVOKE | `GrantStatement` | typed | DmlAndSessionShape |
| TRUNCATE, COMMENT, RENAME, PURGE, FLASHBACK TABLE | `TruncateStatement` … `FlashbackTableStatement` | typed | DmlAndSessionShape |
| CREATE TABLE (columns, constraints, identity, virtual, partitioning, storage, CTAS) | `CreateTableStatement` | typed; LOB/external-table/other physical details are `OtherAttribute` token trees | DdlShape |
| CREATE INDEX, VIEW, MATERIALIZED VIEW, SEQUENCE, SYNONYM | `CreateIndexStatement` … | typed | DdlShape |
| ALTER TABLE | `AlterTableStatement` | typed (partition/other maintenance actions: `Syntax` token tree) | DdlShape |
| DROP (any object kind) | `DropStatement` | typed | DdlShape |
| CREATE PROCEDURE/FUNCTION/PACKAGE [BODY]/TRIGGER/TYPE [BODY]/LIBRARY/JAVA | `CreateProgramStatement` … | typed | PlsqlShape, DdlShape |
| CREATE of any other object (PROFILE, TABLESPACE, DATABASE LINK, DIRECTORY, CLUSTER, CONTEXT, DIMENSION, DOMAIN, EDITION, OPERATOR, INDEXTYPE, OUTLINE, PROPERTY GRAPH, JSON DUALITY VIEW, MV LOG, ZONEMAP, ANALYTIC VIEW, ...) | `CreateObjectStatement` | header+tree |
| ALTER of any object other than TABLE/SESSION/USER/ROLE (INDEX, VIEW, SEQUENCE, MV, TRIGGER, PROCEDURE, SYSTEM, DATABASE, ...) | `AlterObjectStatement` | header+tree |
| CREATE USER / ALTER USER (IDENTIFIED, DEFAULT/TEMPORARY TABLESPACE, QUOTA, PROFILE, PASSWORD EXPIRE, ACCOUNT, DEFAULT ROLE, CONTAINER, EDITIONS, READ ONLY/WRITE, GRANT/REVOKE CONNECT THROUGH) | `CreateUserStatement`, `AlterUserStatement` (`UserClause` per clause; unmodelled tokens as `Other`) | typed | DdlShape, SqlScriptGenerator |
| CREATE ROLE / ALTER ROLE (NOT IDENTIFIED, IDENTIFIED BY/USING/EXTERNALLY/GLOBALLY, CONTAINER) | `CreateRoleStatement`, `AlterRoleStatement` | typed | DdlShape, SqlScriptGenerator |
| ANALYZE, AUDIT, NOAUDIT, ASSOCIATE/DISASSOCIATE STATISTICS, ADMINISTER KEY MANAGEMENT, FLASHBACK DATABASE | `AdminStatement` | header+tree |

## PL/SQL

Typed: anonymous/nested blocks, labels, all declarations (variable, constant, exception, pragma, cursor, record,
collection, ref cursor, subtype, nested subprograms), IF/CASE/LOOP/WHILE/FOR/FORALL/EXIT/CONTINUE/GOTO/RETURN/RAISE/
NULL, OPEN/FETCH/CLOSE, EXECUTE IMMEDIATE, PIPE ROW, assignments, calls, exception handlers, SQL inside PL/SQL
(`SELECT INTO`, DML with `RETURNING BULK COLLECT`), qualified expressions (`arr(1 => v, OTHERS => v, FOR i IN 1..3 => v)`;
`rec(a => v)` stays a `FunctionCall` named argument), chained calls (`f(x)(y)`, `arr(1)(2)`), program options (AUTHID, DETERMINISTIC, PIPELINED, PARALLEL_ENABLE,
RESULT_CACHE, AGGREGATE, SQL_MACRO, ACCESSIBLE BY, DEPRECATE, DEFAULT COLLATION, SHARING), external call specs
(Java/C/MLE), triggers (DML, DDL, database, INSTEAD OF, nested table, compound, cross-edition, ordering).
FOR loops with Oracle 21c+ iteration controls: `ForIterand` (name, type, MUTABLE) plus a list of typed
`IterationControl`s (`RangeControl` with REVERSE/BY, `ExpressionControl`, `RepeatControl`, `ValuesOfControl`,
`IndicesOfControl`, `PairsOfControl`, `CursorControl` for `(SELECT ...)`, `DynamicSqlControl` for the parenthesised
`(EXECUTE IMMEDIATE ...)` form, which Oracle requires), each with optional
WHILE/WHEN. A bare cursor or collection name is an `ExpressionControl`. IMMUTABLE is accepted and not recorded.
Conditional compilation: `$IF`/`$ELSIF`/`$ELSE`/`$END` blocks in statement lists (`ConditionalStatement`) and
declaration lists (`ConditionalDeclaration`), with every branch parsed as typed nodes; `$ERROR 'msg' $END`
(`CompilationErrorStatement`/`CompilationErrorDeclaration`); inquiry directives `$$name` (`InquiryDirective`).
Conditional blocks are not accepted in parameter lists.

## Script level

SQL*Plus commands (`SET`, `SPOOL`, `DEFINE`, `@script`, `PROMPT`, `REM`, `WHENEVER`, ...) → `SqlPlusCommand`
(arguments are a token tree); `EXEC`/`EXECUTE` call or `target := value` → `ExecStatement` whose body is a `ProcedureCallStatement` or `AssignmentStatement` (SQL*Plus runs it as `BEGIN body; END;`); `&var`/`&&var` → `SubstitutionVariable`; `/` terminators.
Error recovery resyncs at `;`, or for PL/SQL units at a `/` alone on its line (else the `;` after `END`).

## Known gaps (each is a parse error today, not a silent mis-parse)

- `JSON_TRANSFORM` `NESTED PATH` and `CASE` operations.
- `IN (subquery, expr)` mixed lists, `CREATE ... SHARDED/DUPLICATED` modifiers beyond those listed.
- Every node can be generated back to SQL by `SqlScriptGenerator` (comments excepted; token-tree bodies print as space-separated tokens). The `AstPrinter` text is for assertions, not SQL.
- Collection indexing vs. function call: `arr(i)` is syntactically identical to a call and is represented as
  `FunctionCall`; only a call or subscript applied to another call's result (`arr(i)(j)`, `f(x)(y)`) is a `ChainedCall`.
  `arr[i]`-style subscripts are `CellReference` (MODEL measures, JSON dot-notation array steps such as `t.c.a[0 to 2].b`).
- Expressions: `IS [NOT] TRUE/FALSE` (`IsExpression`), interval suffixes `(a - b) DAY(p) TO SECOND(fs)` and
  `(a - b) YEAR TO MONTH` (`IntervalExpression`). `**` is accepted only in PL/SQL procedural code; SQL rejects it.
