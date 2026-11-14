# Coverage

Definition of done: every statement in Oracle's "Types of SQL Statements" index (Oracle Database 23, SQL Language
Reference, fetched 2026-11-10) plus the PL/SQL constructs below. Each row names the AST node and the test file
asserting its tree shape (tests are hand-written expected trees, see `AstPrinter`).

Legend: **typed** = fully modeled nodes. **header+tree** = typed header (verb, object kind, name) with the remaining
clauses as a nested token tree (`WordItem`/`LiteralItem`/`SymbolItem`/`GroupItem`), never a raw string.

## SQL statements (Oracle index)

| Statement | Node | Level | Tests |
|---|---|---|---|
| SELECT (all clauses, set ops, WITH incl. SEARCH/CYCLE/`WITH FUNCTION`, partitioned outer join, `FETCH [EXACT\|APPROX] ... [WITH TARGET ACCURACY]`, PIVOT/LEFT/SAMPLE/FETCH as aliases where no clause can start; the vector-search `accuracy_clause` is not modeled) | `SelectStatement`, `QuerySpecification` | typed | QueryShape |
| PIVOT / UNPIVOT / MATCH_RECOGNIZE / MODEL / XMLTABLE / JSON_TABLE / flashback / SAMPLE / VALUES table (also as a CTE body) | `PivotTable` … `ValuesTable`, `ValuesQuery` | typed | QueryShape, MiscShape |
| GRAPH_TABLE (MATCH paths: vertex/edge patterns, IS labels, WHERE, all arrows incl. `<-[..]->`, `{n}`, `{n,m}`, `{,m}` on edges and sub-paths `( ... ){n}`; ONE ROW PER; COLUMNS with optional AS) | `GraphTable` | typed; Oracle-rejected forms (`*`, `+`, `{n,}`, `AS` before the alias, bare sub-path) are rejected |  QueryShape |
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
| CREATE [USECASE] DOMAIN [IF NOT EXISTS] (CHECK, DEFAULT, NOT NULL, DISPLAY, ORDER, ANNOTATIONS), ALTER DOMAIN (DISPLAY, ORDER, ANNOTATIONS), column `DOMAIN d` | `CreateDomainStatement`, `AlterDomainStatement`, `AnnotationsClause`, `ColumnDefinition.Domain` | typed; Oracle-verified (integration GraphAndDomainAcceptance); other ALTER DOMAIN actions (RENAME, ADD CONSTRAINT) kept as tokens | DomainShape |
| CREATE [OR REPLACE] PROPERTY GRAPH (VERTEX/EDGE TABLES, KEY, SOURCE/DESTINATION, LABEL, PROPERTIES incl. AS aliases and PROPERTIES without LABEL) | `CreatePropertyGraphStatement` | typed | PropertyGraphShape |
| CREATE [OR REPLACE] JSON [RELATIONAL] DUALITY VIEW ... AS SELECT JSON {...} FROM t [WITH INSERT|UPDATE|DELETE|NOINSERT|NOUPDATE|NODELETE|NOCHECK|ETAG|NO ETAG]; JSON { } / JSON [ ] constructors (incl. ABSENT/NULL ON NULL, RETURNING, nested subqueries) | `CreateDualityViewStatement`, `JsonObjectConstructor`, `JsonArrayConstructor`, `NamedTable.DualityAnnotations` | typed for the SQL form; the GraphQL form (`v AS t @insert ...`) is kept as tokens in `OtherClauses` | JsonConstructorShape, JsonDualityAcceptance |
| ALTER INDEX (REBUILD, RENAME, UNUSABLE, VISIBLE, MONITORING, COALESCE, ENABLE/DISABLE, attributes, SHRINK SPACE, DEALLOCATE UNUSED) | `AlterIndexStatement` | typed (physical attributes reuse the CREATE nodes) | DdlShape, SmokeCorpus |
| ALTER SEQUENCE (all options; RESTART as an option word) | `AlterSequenceStatement` | typed (shares `SequenceOption` with CREATE) | DdlShape, SmokeCorpus |
| ALTER of PROCEDURE/FUNCTION/PACKAGE [BODY]/TYPE [BODY]/TRIGGER/LIBRARY/JAVA/VIEW/SYNONYM: COMPILE [DEBUG] [SPECIFICATION|BODY|PACKAGE] [REUSE SETTINGS] [param = v], EDITIONABLE/NONEDITIONABLE; ALTER TRIGGER ENABLE/DISABLE/RENAME TO; ALTER VIEW ADD/MODIFY/DROP CONSTRAINT, READ ONLY/WRITE | `AlterCompileStatement`, `AlterTriggerStatement`, `AlterViewStatement` | typed (other clauses stay `AlterObjectStatement`) | DdlShape, SmokeCorpus |
| ALTER MATERIALIZED VIEW (REFRESH options, ENABLE/DISABLE QUERY REWRITE, COMPILE, CONSIDER FRESH, SHRINK SPACE, CACHE/LOGGING/PARALLEL) | `AlterMaterializedViewStatement` | typed (refresh reuses `RefreshClause`) | DdlShape, SmokeCorpus |
| ALTER TABLE | `AlterTableStatement` | typed (ADD/DROP/TRUNCATE/SPLIT/MERGE/EXCHANGE/MOVE/RENAME PARTITION, MODIFY PARTITION ADD/DROP VALUES, SET INTERVAL; other maintenance actions: `Syntax` token tree) | DdlShape, SmokeCorpus |
| DROP (any object kind) | `DropStatement` | typed | DdlShape |
| CREATE PROCEDURE/FUNCTION/PACKAGE [BODY]/TRIGGER/TYPE [BODY]/LIBRARY/JAVA | `CreateProgramStatement` … | typed | PlsqlShape, DdlShape |
| CREATE of any other object (CLUSTER, DIMENSION, EDITION, OPERATOR, INDEXTYPE, OUTLINE, MV LOG, ZONEMAP, ANALYTIC VIEW, ...) | `CreateObjectStatement` | header+tree |
| ALTER of any object other than TABLE/SESSION/INDEX/SEQUENCE/MATERIALIZED VIEW/DOMAIN/USER/ROLE/PROFILE/TABLESPACE/AUDIT POLICY and the typed compiled-unit forms (SYSTEM, DATABASE, TYPE attribute clauses, ...) | `AlterObjectStatement` | header+tree |
| ANALYZE TABLE/INDEX/CLUSTER (COMPUTE/ESTIMATE/DELETE STATISTICS, VALIDATE STRUCTURE, LIST CHAINED ROWS; FOR clauses and other forms in a token tree) | `AnalyzeStatement` | typed (trailing clauses: `OtherClauses` token tree) | DmlAndSessionShape, SmokeCorpus |
| CREATE USER / ALTER USER (IDENTIFIED, DEFAULT/TEMPORARY TABLESPACE, QUOTA, PROFILE, PASSWORD EXPIRE, ACCOUNT, DEFAULT ROLE, CONTAINER, EDITIONS, READ ONLY/WRITE, GRANT/REVOKE CONNECT THROUGH) | `CreateUserStatement`, `AlterUserStatement` (`UserClause` per clause; unmodelled tokens as `Other`) | typed | DdlShape, SqlScriptGenerator |
| CREATE ROLE / ALTER ROLE (NOT IDENTIFIED, IDENTIFIED BY/USING/EXTERNALLY/GLOBALLY, CONTAINER) | `CreateRoleStatement`, `AlterRoleStatement` | typed | DdlShape, SqlScriptGenerator |
| CREATE PROFILE / ALTER PROFILE (LIMIT name value | UNLIMITED | DEFAULT | NULL, PASSWORD_VERIFY_FUNCTION, CONTAINER, CASCADE) | `CreateProfileStatement`, `AlterProfileStatement`, `ProfileLimit` | typed | DdlShape, SqlScriptGenerator |
| CREATE DIRECTORY, CREATE [SHARED] [PUBLIC] DATABASE LINK (CONNECT TO user IDENTIFIED BY / CURRENT_USER, AUTHENTICATED BY, USING), CREATE CONTEXT (INITIALIZED, ACCESSED GLOBALLY) | `CreateDirectoryStatement`, `CreateDatabaseLinkStatement`, `CreateContextStatement` | typed | DdlShape |
| CREATE [BIGFILE|SMALLFILE] [TEMPORARY|UNDO] TABLESPACE (DATAFILE/TEMPFILE specs, LOGGING, ONLINE, EXTENT MANAGEMENT LOCAL, SEGMENT SPACE MANAGEMENT, ENCRYPTION; rarer options in `OtherClauses`), ALTER TABLESPACE (ADD DATAFILE/TEMPFILE, RESIZE, AUTOEXTEND, ONLINE/OFFLINE, READ ONLY/WRITE, RENAME TO, [NO]LOGGING, BEGIN/END BACKUP) | `CreateTablespaceStatement`, `AlterTablespaceStatement`, `TablespaceAction`, `DatafileSpec` | typed | DdlShape |
| ASSOCIATE/DISASSOCIATE STATISTICS, ADMINISTER KEY MANAGEMENT, FLASHBACK DATABASE | `AdminStatement` | header+tree |
| AUDIT / NOAUDIT, traditional (options, ON object, BY users or ACCESS/SESSION, WHENEVER [NOT] SUCCESSFUL) and unified (POLICY ... BY/EXCEPT, CONTEXT NAMESPACE ... ATTRIBUTES, BY USERS WITH ROLES) | `AuditStatement`, `AuditOption`, `AuditObject` | typed | DdlShape, DmlAndSessionShape |
| CREATE AUDIT POLICY / ALTER AUDIT POLICY (PRIVILEGES, ACTIONS incl. COMPONENT= and ON object, ROLES, ADD/DROP lists, WHEN ... EVALUATE PER, ONLY TOPLEVEL, CONTAINER, CONDITION DROP) | `CreateAuditPolicyStatement`, `AlterAuditPolicyStatement`, `AuditPolicyList`, `AuditPolicyItem` | typed | DdlShape |

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
Conditional blocks (``) in parameter lists are typed (`ConditionalParameter`); the branches hold `Parameter` items. `JSON` array steps (`t.c.a[0]`) are rejected in PL/SQL expressions (PLS-00103) and accepted in SQL, including SQL inside PL/SQL.

## Script level

SQL*Plus commands (`SET`, `SPOOL`, `DEFINE`, `@script`, `PROMPT`, `REM`, `WHENEVER`, ...) → `SqlPlusCommand`
(arguments are a token tree); `EXEC`/`EXECUTE` call or `target := value` → `ExecStatement` whose body is a `ProcedureCallStatement` or `AssignmentStatement` (SQL*Plus runs it as `BEGIN body; END;`); `&var`/`&&var` → `SubstitutionVariable`; `/` terminators.
Error recovery resyncs at `;`, or for PL/SQL units at a `/` alone on its line (else the `;` after `END`).

## Version gates

`ParseOptions.OracleVersion` (default `Oracle23`) reports constructs newer than the target as a `ParseError` and keeps it in the tree. The table lists the gated constructs by the release that introduced them.

| Release | Constructs |
|---|---|
| 21c | JSON data type (`CREATE`, `ALTER`, `CAST`, `RETURNING`); IMMUTABLE and BLOCKCHAIN tables; SQL_MACRO / SQL_TABLE_MACRO; FOR loop iteration controls `BY`, several controls, `REPEAT`, `WHILE`/`WHEN`, `VALUES OF`/`INDICES OF`/`PAIRS OF`, parenthesised `EXECUTE IMMEDIATE`; typed and `MUTABLE`/`IMMUTABLE` iterands; qualified-expression FOR iterator `t(FOR i IN 1..3 => i)` |
| 23ai | `IS [NOT] TRUE/FALSE`; BOOLEAN type and TRUE/FALSE literals in SQL (PL/SQL is not gated); VECTOR type; `SELECT` without `FROM`; `IF [NOT] EXISTS`; `RETURNING OLD/NEW`; `UPDATE ... FROM` and `DELETE ... FROM`; `VALUES` table constructor (FROM, CTE, bare query); multi-row `INSERT ... VALUES`; `GRAPH_TABLE`; column `DOMAIN`; CREATE/ALTER/DROP of DOMAIN, PROPERTY GRAPH, JSON RELATIONAL DUALITY VIEW, MLE MODULE, MLE ENV; `LANGUAGE MLE` call specs; `JSON {...}` and `JSON [...]` constructors; `ANNOTATIONS` (in the shared clause, so every use site); `FETCH APPROX`/`APPROXIMATE` and `WITH TARGET ACCURACY`; `DEFAULT ON NULL FOR INSERT ...` (plain `DEFAULT ON NULL` is 12c and not gated); `CREATE`/`ALTER USER ... READ ONLY`/`READ WRITE`; `GRANT`/`REVOKE ... ON SCHEMA` |

Caveats:

- SQL_MACRO was backported to 19c release updates (19.7 and later RUs), but it is gated at 21 regardless. Users on a patched 19c should target `Oracle21` or ignore that diagnostic.
- The MLE objects and `LANGUAGE MLE` call specs are gated at 23 as the conservative choice. Their 21c release could not be confirmed, so a 21c target may see a false error.
- `TREAT(expr AS JSON)` is not gated, because its release is not confirmed. `CAST(expr AS JSON)` is gated with the JSON data type.

Not gated:

- `GROUP BY` by alias or position. `GROUP BY 1` is valid in 19c, and an alias is an ordinary identifier at parse time, so 19c rejects it semantically, not in the parser.
- Plain `DEFAULT ON NULL` (12c), plain `name => value` and `OTHERS =>` qualified-expression choices (18c), and index choices `1 => x` (18c).
- `FOR` loops over a bare cursor or collection name, and `(SELECT ...)` controls, are classic forms.
- The MODEL `FOR` iteration forms (`FOR d FROM 1 TO 10 INCREMENT 1`, `FOR d IN (...)`) date from 10g.
- `RESERVABLE` (lock-free reservations) and the `VECTOR_*` functions. They have no typed parse, so there is nothing to gate.

## Known gaps (each is a parse error today, not a silent mis-parse)

- `JSON_TRANSFORM` `NESTED PATH` and `CASE` operations.
- `IN (subquery, expr)` mixed lists, `CREATE ... SHARDED/DUPLICATED` modifiers beyond those listed.
- Every node can be generated back to SQL by `SqlScriptGenerator` (comments excepted; token-tree bodies print as space-separated tokens). The `AstPrinter` text is for assertions, not SQL.
- Collection indexing vs. function call: `arr(i)` is syntactically identical to a call and is represented as
  `FunctionCall`; only a call or subscript applied to another call's result (`arr(i)(j)`, `f(x)(y)`) is a `ChainedCall`.
  `arr[i]`-style subscripts are `CellReference` (MODEL measures, JSON dot-notation array steps such as `t.c.a[0 to 2].b`).
- Expressions: `IS [NOT] TRUE/FALSE` (`IsExpression`), interval suffixes `(a - b) DAY(p) TO SECOND(fs)` and
  `(a - b) YEAR TO MONTH` (`IntervalExpression`). `**` is accepted only in PL/SQL procedural code; SQL rejects it.
