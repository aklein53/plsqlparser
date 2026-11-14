namespace OracleSqlParser;

// ---------- Transaction control & session ----------

/// <summary>Whether a transaction control statement waits for a lock or log write.</summary>
public enum WaitMode
{
    /// <summary><c>WAIT</c>.</summary>
    Wait,
    /// <summary><c>NOWAIT</c>.</summary>
    NoWait,
}
/// <summary>How the redo of a <c>COMMIT WRITE</c> is written.</summary>
public enum CommitWriteMode
{
    /// <summary><c>IMMEDIATE</c>: written before the commit returns.</summary>
    Immediate,
    /// <summary><c>BATCH</c>: may be buffered.</summary>
    Batch,
}

/// <summary>The <c>WRITE</c> clause of <c>COMMIT</c>, such as <c>COMMIT WRITE BATCH NOWAIT</c>.</summary>
public sealed partial class CommitWrite : SqlNode
{
    /// <summary>The wait setting, or null when absent.</summary>
    public WaitMode? Wait { get; set; }
    /// <summary>The write mode, or null when absent.</summary>
    public CommitWriteMode? Mode { get; set; }
}

/// <summary><c>COMMIT [WORK] [COMMENT 'text'] [WRITE ...] [FORCE ...]</c>.</summary>
public sealed partial class CommitStatement : Statement
{
    /// <summary>True when <c>WORK</c> is written.</summary>
    public bool Work { get; set; }
    /// <summary>The <c>COMMENT</c> expression, or null when absent.</summary>
    public Expression? Comment { get; set; }
    /// <summary>The <c>WRITE</c> clause, or null when absent.</summary>
    public CommitWrite? Write { get; set; }
    /// <summary>The transaction id after <c>COMMIT FORCE</c> (for example <c>'1.2.3'</c>), or null when absent.</summary>
    public Expression? Force { get; set; }
    /// <summary>The SCN after the comma in <c>COMMIT FORCE 'tx_id', scn</c>, or null when absent.</summary>
    public Expression? ForceScn { get; set; }
}

/// <summary><c>ROLLBACK [WORK] [TO SAVEPOINT name] [FORCE 'text']</c>.</summary>
public sealed partial class RollbackStatement : Statement
{
    /// <summary>True when <c>WORK</c> is written.</summary>
    public bool Work { get; set; }
    /// <summary>The savepoint name after <c>TO SAVEPOINT</c>, or null when rolling back the whole transaction.</summary>
    public Identifier? ToSavepoint { get; set; }
    /// <summary>The transaction id after <c>ROLLBACK FORCE</c> for a distributed transaction, or null when absent.</summary>
    public Expression? Force { get; set; }
}

/// <summary><c>SAVEPOINT name</c>: marks a point that a later <c>ROLLBACK TO</c> can return to.</summary>
public sealed partial class SavepointStatement : Statement
{
    /// <summary>The savepoint name.</summary>
    public required Identifier Name { get; set; }
}

/// <summary>The access mode of a transaction.</summary>
public enum TransactionAccess
{
    /// <summary><c>READ ONLY</c>.</summary>
    ReadOnly,
    /// <summary><c>READ WRITE</c>.</summary>
    ReadWrite,
}
/// <summary>The isolation level of a transaction.</summary>
public enum IsolationLevel
{
    /// <summary><c>ISOLATION LEVEL SERIALIZABLE</c>.</summary>
    Serializable,
    /// <summary><c>ISOLATION LEVEL READ COMMITTED</c>.</summary>
    ReadCommitted,
}

/// <summary><c>SET TRANSACTION</c>, such as <c>SET TRANSACTION READ ONLY NAME 'batch'</c>.</summary>
public sealed partial class SetTransactionStatement : Statement
{
    /// <summary>The access mode, or null when not written.</summary>
    public TransactionAccess? Access { get; set; }
    /// <summary>The isolation level, or null when not written.</summary>
    public IsolationLevel? Isolation { get; set; }
    /// <summary>The rollback segment after <c>USE ROLLBACK SEGMENT</c>, or null when absent.</summary>
    public Identifier? RollbackSegment { get; set; }
    /// <summary>The transaction name after <c>NAME</c>, or null when absent.</summary>
    public Expression? Name { get; set; }
}

/// <summary><c>SET CONSTRAINTS</c>, such as <c>SET CONSTRAINTS ALL DEFERRED</c>.</summary>
public sealed partial class SetConstraintsStatement : Statement
{
    /// <summary>True for <c>ALL</c>; false when named constraints are listed.</summary>
    public bool All { get; set; }
    /// <summary>The constraint names, one name list per entry; empty for <c>ALL</c>.</summary>
    public List<List<Identifier>> Constraints { get; set; } = [];
    /// <summary>True for <c>DEFERRED</c>; false for <c>IMMEDIATE</c>.</summary>
    public bool Deferred { get; set; }
}

/// <summary>A role named in a <c>SET ROLE</c> statement, optionally with its password.</summary>
public sealed partial class RoleSpec : SqlNode
{
    /// <summary>The role name.</summary>
    public required Identifier Name { get; set; }
    /// <summary>The password after <c>IDENTIFIED BY</c>, or null when absent.</summary>
    public Expression? IdentifiedBy { get; set; }
}

/// <summary><c>SET ROLE</c>, such as <c>SET ROLE ALL EXCEPT audit_role</c>.</summary>
public sealed partial class SetRoleStatement : Statement
{
    /// <summary>True for <c>SET ROLE NONE</c>.</summary>
    public bool None { get; set; }
    /// <summary>True for <c>SET ROLE ALL</c>.</summary>
    public bool All { get; set; }
    /// <summary>The roles excluded after <c>ALL EXCEPT</c>; empty when absent.</summary>
    public List<Identifier> Except { get; set; } = [];
    /// <summary>The roles enabled by name; empty for <c>ALL</c>, <c>NONE</c> and <c>ALL EXCEPT</c>.</summary>
    public List<RoleSpec> Roles { get; set; } = [];
}

/// <summary>The form of an <c>ALTER SESSION</c> statement.</summary>
public enum AlterSessionKind
{
    /// <summary><c>SET</c> of session parameters.</summary>
    Set,
    /// <summary><c>ENABLE</c> of a feature, such as <c>ENABLE PARALLEL DML</c>.</summary>
    Enable,
    /// <summary><c>DISABLE</c> of a feature.</summary>
    Disable,
    /// <summary><c>FORCE</c> of a feature, such as <c>FORCE PARALLEL QUERY</c>.</summary>
    Force,
    /// <summary><c>CLOSE DATABASE LINK</c>.</summary>
    CloseDatabaseLink,
    /// <summary><c>ADVISE</c> of a commit or rollback.</summary>
    Advise,
    /// <summary><c>SYNC WITH PRIMARY</c>.</summary>
    SyncWithPrimary,
}
/// <summary>The feature that <c>ALTER SESSION ENABLE</c>, <c>DISABLE</c> or <c>FORCE</c> applies to.</summary>
public enum SessionFeature
{
    /// <summary><c>COMMIT IN PROCEDURE</c>.</summary>
    CommitInProcedure,
    /// <summary><c>GUARD</c>.</summary>
    Guard,
    /// <summary><c>PARALLEL DML</c>.</summary>
    ParallelDml,
    /// <summary><c>PARALLEL DDL</c>.</summary>
    ParallelDdl,
    /// <summary><c>PARALLEL QUERY</c>.</summary>
    ParallelQuery,
    /// <summary><c>RESUMABLE</c>.</summary>
    Resumable,
    /// <summary>A feature without a dedicated value; kept as written in the token tree.</summary>
    Other,
}
/// <summary>The action taken by <c>ALTER SESSION ADVISE</c>.</summary>
public enum AdviseMode
{
    /// <summary><c>COMMIT</c>.</summary>
    Commit,
    /// <summary><c>ROLLBACK</c>.</summary>
    Rollback,
    /// <summary><c>NOTHING</c>.</summary>
    Nothing,
}

/// <summary><c>ALTER SESSION</c>, such as <c>ALTER SESSION SET NLS_DATE_FORMAT = 'YYYY'</c>.</summary>
public sealed partial class AlterSessionStatement : Statement
{
    /// <summary>The form of the statement.</summary>
    public AlterSessionKind Kind { get; set; }
    /// <summary>The <c>name = value</c> settings of <c>SET</c>, in source order; empty for the other forms.</summary>
    public List<SessionSetting> Settings { get; set; } = [];
    /// <summary>The feature named by <c>ENABLE</c>, <c>DISABLE</c> or <c>FORCE</c>, or null when absent.</summary>
    public SessionFeature? Feature { get; set; }
    /// <summary>The parallel degree expression, or null when absent.</summary>
    public Expression? Parallel { get; set; }
    /// <summary>The timeout expression of <c>ENABLE RESUMABLE</c>, or null when absent.</summary>
    public Expression? Timeout { get; set; }
    /// <summary>The name after <c>NAME</c> in <c>ENABLE RESUMABLE</c>, or null when absent.</summary>
    public Expression? ResumableName { get; set; }
    /// <summary>The database link name of <c>CLOSE DATABASE LINK</c>, or null when absent.</summary>
    public Identifier? DatabaseLink { get; set; }
    /// <summary>The action of <c>ADVISE</c>, or null when absent.</summary>
    public AdviseMode? Advise { get; set; }
    /// <summary>Feature words/arguments for ENABLE/DISABLE forms not modeled individually (listed in COVERAGE.md).</summary>
    public List<SyntaxItem> FeatureSyntax { get; set; } = [];
}

/// <summary>A <c>name = value</c> setting, such as a session parameter or compiler parameter.</summary>
public sealed partial class SessionSetting : SqlNode
{
    /// <summary>The parameter name parts.</summary>
    public List<Identifier> Name { get; set; } = [];
    /// <summary>The value assigned to the parameter.</summary>
    public required Expression Value { get; set; }
}

/// <summary><c>CALL routine</c>, such as <c>CALL p(1) INTO :x</c>.</summary>
public sealed partial class CallStatement : Statement
{
    /// <summary>The routine call.</summary>
    public required Expression Routine { get; set; }
    /// <summary>The <c>INTO</c> target, or null when absent.</summary>
    public Expression? Into { get; set; }
}

/// <summary><c>EXPLAIN PLAN [SET STATEMENT_ID = 'x'] [INTO t] FOR statement</c>.</summary>
public sealed partial class ExplainPlanStatement : Statement
{
    /// <summary>The <c>STATEMENT_ID</c> expression, or null when absent.</summary>
    public Expression? StatementId { get; set; }
    /// <summary>The plan table name parts after <c>INTO</c>; empty when absent.</summary>
    public List<Identifier> IntoTable { get; set; } = [];
    /// <summary>The statement whose plan is explained.</summary>
    public required Statement Statement { get; set; }
}

/// <summary>The mode of a <c>LOCK TABLE</c> statement.</summary>
public enum LockMode
{
    /// <summary><c>ROW SHARE</c>.</summary>
    RowShare,
    /// <summary><c>ROW EXCLUSIVE</c>.</summary>
    RowExclusive,
    /// <summary><c>SHARE UPDATE</c>.</summary>
    ShareUpdate,
    /// <summary><c>SHARE</c>.</summary>
    Share,
    /// <summary><c>SHARE ROW EXCLUSIVE</c>.</summary>
    ShareRowExclusive,
    /// <summary><c>EXCLUSIVE</c>.</summary>
    Exclusive,
}

/// <summary><c>LOCK TABLE t IN mode MODE [NOWAIT]</c>.</summary>
public sealed partial class LockTableStatement : Statement
{
    /// <summary>The tables to lock, in source order.</summary>
    public List<TableReference> Tables { get; set; } = [];
    /// <summary>The lock mode.</summary>
    public LockMode Mode { get; set; }
    /// <summary>True for <c>NOWAIT</c>.</summary>
    public bool NoWait { get; set; }
    /// <summary>The number of seconds after <c>WAIT</c>, or null when absent.</summary>
    public Expression? Wait { get; set; }
}

/// <summary>What a <c>TRUNCATE</c> statement truncates.</summary>
public enum TruncateTarget
{
    /// <summary><c>TRUNCATE TABLE</c>.</summary>
    Table,
    /// <summary><c>TRUNCATE CLUSTER</c>.</summary>
    Cluster,
}
/// <summary>What happens to a materialized view log when its table is truncated.</summary>
public enum MaterializedViewLogAction
{
    /// <summary><c>PRESERVE MATERIALIZED VIEW LOG</c>.</summary>
    Preserve,
    /// <summary><c>PURGE MATERIALIZED VIEW LOG</c>.</summary>
    Purge,
}
/// <summary>The storage behavior of a <c>TRUNCATE</c> statement.</summary>
public enum TruncateStorage
{
    /// <summary><c>DROP STORAGE</c>.</summary>
    DropStorage,
    /// <summary><c>DROP ALL STORAGE</c>.</summary>
    DropAllStorage,
    /// <summary><c>REUSE STORAGE</c>.</summary>
    ReuseStorage,
}

/// <summary><c>TRUNCATE TABLE|CLUSTER name [...]</c>.</summary>
public sealed partial class TruncateStatement : Statement
{
    /// <summary>Whether a table or a cluster is truncated.</summary>
    public TruncateTarget Target { get; set; }
    /// <summary>The name parts, schema first when qualified.</summary>
    public List<Identifier> Name { get; set; } = [];
    /// <summary>The materialized view log action, or null when absent.</summary>
    public MaterializedViewLogAction? MaterializedViewLog { get; set; }
    /// <summary>The storage option, or null when absent.</summary>
    public TruncateStorage? Storage { get; set; }
    /// <summary>True for <c>CASCADE</c>.</summary>
    public bool Cascade { get; set; }
}

/// <summary>The kind of object a <c>COMMENT ON</c> statement annotates.</summary>
public enum CommentTarget
{
    /// <summary><c>TABLE</c>.</summary>
    Table,
    /// <summary><c>COLUMN</c>.</summary>
    Column,
    /// <summary><c>MATERIALIZED VIEW</c>.</summary>
    MaterializedView,
    /// <summary><c>OPERATOR</c>.</summary>
    Operator,
    /// <summary><c>INDEXTYPE</c>.</summary>
    Indextype,
    /// <summary><c>MINING MODEL</c>.</summary>
    MiningModel,
    /// <summary><c>EDITION</c>.</summary>
    Edition,
}

/// <summary><c>COMMENT ON object IS 'text'</c>.</summary>
public sealed partial class CommentStatement : Statement
{
    /// <summary>The kind of object commented.</summary>
    public CommentTarget Target { get; set; }
    /// <summary>The object name parts, schema first when qualified.</summary>
    public List<Identifier> Name { get; set; } = [];
    /// <summary>The comment text, without the quotes.</summary>
    public string Text { get; set; } = "";
}

/// <summary>One privilege of a <c>GRANT</c> or <c>REVOKE</c>, such as <c>SELECT (a, b)</c>.</summary>
public sealed partial class GrantPrivilege : SqlNode
{
    /// <summary>Privilege or role name as a keyword phrase, e.g. SELECT, CREATE ANY TABLE, DBA.</summary>
    public string Name { get; set; } = "";
    /// <summary>The columns the privilege applies to; empty when it applies to the whole object.</summary>
    public List<Identifier> Columns { get; set; } = [];
}

/// <summary>A user, role or <c>PUBLIC</c> that receives or loses a privilege.</summary>
public sealed partial class Grantee : SqlNode
{
    /// <summary>The grantee name, or null for <c>PUBLIC</c>.</summary>
    public Identifier? Name { get; set; }
    /// <summary>True for <c>PUBLIC</c>.</summary>
    public bool Public { get; set; }
}

/// <summary>The kind of object named after <c>ON</c> in a <c>GRANT</c> or <c>REVOKE</c>.</summary>
public enum GrantObjectKind
{
    /// <summary><c>DIRECTORY</c>.</summary>
    Directory,
    /// <summary><c>EDITION</c>.</summary>
    Edition,
    /// <summary><c>MINING MODEL</c>.</summary>
    MiningModel,
    /// <summary><c>JAVA SOURCE</c>.</summary>
    JavaSource,
    /// <summary><c>JAVA RESOURCE</c>.</summary>
    JavaResource,
    /// <summary><c>SQL TRANSLATION PROFILE</c>.</summary>
    SqlTranslationProfile,
    /// <summary><c>USER</c>.</summary>
    User,
    /// <summary><c>SCHEMA</c>.</summary>
    Schema,
}
/// <summary>The container scope of a <c>GRANT</c>, role, profile or audit policy statement.</summary>
public enum GrantContainer
{
    /// <summary><c>CONTAINER = CURRENT</c>.</summary>
    Current,
    /// <summary><c>CONTAINER = ALL</c>.</summary>
    All,
}

/// <summary><c>GRANT</c> or <c>REVOKE</c> of privileges or roles, such as <c>GRANT SELECT ON emp TO scott WITH GRANT OPTION</c>.</summary>
public sealed partial class GrantStatement : Statement
{
    /// <summary>True for <c>REVOKE</c>; false for <c>GRANT</c>.</summary>
    public bool Revoke { get; set; }
    /// <summary>True for <c>ALL PRIVILEGES</c>.</summary>
    public bool AllPrivileges { get; set; }
    /// <summary>The privileges granted or revoked, in source order; empty for the role form and <c>ALL PRIVILEGES</c>.</summary>
    public List<GrantPrivilege> Privileges { get; set; } = [];
    /// <summary>The kind of object named after <c>ON</c>, or null when the object is an ordinary schema object or the statement grants roles.</summary>
    public GrantObjectKind? ObjectKind { get; set; }
    /// <summary>The object name parts after <c>ON</c>; empty for the role form.</summary>
    public List<Identifier> Object { get; set; } = [];
    /// <summary>The users, roles or <c>PUBLIC</c> named after <c>TO</c> or <c>FROM</c>, in source order.</summary>
    public List<Grantee> Grantees { get; set; } = [];
    /// <summary>True for <c>WITH GRANT OPTION</c>.</summary>
    public bool WithGrantOption { get; set; }
    /// <summary>True for <c>WITH ADMIN OPTION</c>.</summary>
    public bool WithAdminOption { get; set; }
    /// <summary>True for <c>WITH HIERARCHY OPTION</c>.</summary>
    public bool WithHierarchyOption { get; set; }
    /// <summary>True for <c>CASCADE CONSTRAINTS</c>.</summary>
    public bool CascadeConstraints { get; set; }
    /// <summary>True for <c>FORCE</c>.</summary>
    public bool Force { get; set; }
    /// <summary>The container scope, or null when absent.</summary>
    public GrantContainer? Container { get; set; }
}

/// <summary><c>RENAME old TO new</c>, such as <c>RENAME emp TO employee</c>.</summary>
public sealed partial class RenameStatement : Statement
{
    /// <summary>The old name parts.</summary>
    public List<Identifier> From { get; set; } = [];
    /// <summary>The new name parts.</summary>
    public List<Identifier> To { get; set; } = [];
}

/// <summary>What a <c>PURGE</c> statement removes from the recycle bin.</summary>
public enum PurgeTarget
{
    /// <summary><c>PURGE RECYCLEBIN</c>.</summary>
    Recyclebin,
    /// <summary><c>PURGE DBA_RECYCLEBIN</c>.</summary>
    DbaRecyclebin,
    /// <summary><c>PURGE TABLE</c>.</summary>
    Table,
    /// <summary><c>PURGE INDEX</c>.</summary>
    Index,
    /// <summary><c>PURGE TABLESPACE</c>.</summary>
    Tablespace,
    /// <summary><c>PURGE TABLESPACE SET</c>.</summary>
    TablespaceSet,
}

/// <summary><c>PURGE</c> of a dropped object from the recycle bin.</summary>
public sealed partial class PurgeStatement : Statement
{
    /// <summary>The kind of object purged.</summary>
    public PurgeTarget Target { get; set; }
    /// <summary>The object name parts; empty for <c>PURGE RECYCLEBIN</c>.</summary>
    public List<Identifier> Name { get; set; } = [];
    /// <summary>The user named after <c>USER</c>, or null when absent.</summary>
    public Identifier? User { get; set; }
}

/// <summary>What a <c>FLASHBACK TABLE</c> statement returns the table to.</summary>
public enum FlashbackTarget
{
    /// <summary><c>TO SCN</c>.</summary>
    Scn,
    /// <summary><c>TO TIMESTAMP</c>.</summary>
    Timestamp,
    /// <summary><c>TO RESTORE POINT</c>.</summary>
    RestorePoint,
    /// <summary><c>TO BEFORE DROP</c>.</summary>
    BeforeDrop,
}

/// <summary><c>FLASHBACK TABLE t [, u] TO ...</c>.</summary>
public sealed partial class FlashbackTableStatement : Statement
{
    /// <summary>The tables to flash back, one name list each.</summary>
    public List<List<Identifier>> Tables { get; set; } = [];
    /// <summary>What the tables are returned to.</summary>
    public FlashbackTarget Target { get; set; }
    /// <summary>The SCN, timestamp or restore point expression, or null for <c>BEFORE DROP</c>.</summary>
    public Expression? Point { get; set; }
    /// <summary>The new name after <c>RENAME TO</c>, or null when absent.</summary>
    public Identifier? RenameTo { get; set; }
    /// <summary>True for <c>BEFORE DROP</c>.</summary>
    public bool Before { get; set; }
    /// <summary>True for <c>ENABLE TRIGGERS</c>.</summary>
    public bool EnableTriggers { get; set; }
}

// ---------- SQL*Plus & script-level ----------

/// <summary>
/// EXEC[UTE] call or EXEC[UTE] target := value. SQL*Plus runs the body as if it were BEGIN body; END;
/// (so EXEC x == BEGIN x; END;). The body is an AssignmentStatement or a ProcedureCallStatement.
/// </summary>
public sealed partial class ExecStatement : Statement
{
    /// <summary>The statement executed, such as the procedure call inside <c>EXEC p</c>.</summary>
    public required Statement Body { get; set; }
}

/// <summary>A SQL*Plus client command (SET, SPOOL, DEFINE, @script, PROMPT, REM, VARIABLE, WHENEVER, ...). Never sent to the server.</summary>
public sealed partial class SqlPlusCommand : Statement
{
    /// <summary>The command text as written, such as <c>PROMPT hello</c>.</summary>
    public string Command { get; set; } = "";
    /// <summary>Remaining tokens on the command's line.</summary>
    public List<SyntaxItem> Arguments { get; set; } = [];
}

// ---------- CREATE / ALTER USER ----------

/// <summary>The kind of a clause of <c>CREATE USER</c> or <c>ALTER USER</c>.</summary>
public enum UserClauseKind
{
    /// <summary><c>IDENTIFIED BY password</c>.</summary>
    IdentifiedBy,
    /// <summary><c>IDENTIFIED EXTERNALLY</c>.</summary>
    IdentifiedExternally,
    /// <summary><c>IDENTIFIED GLOBALLY</c>.</summary>
    IdentifiedGlobally,
    /// <summary><c>NO AUTHENTICATION</c>.</summary>
    NoAuthentication,
    /// <summary><c>DEFAULT TABLESPACE</c>.</summary>
    DefaultTablespace,
    /// <summary><c>TEMPORARY TABLESPACE</c>.</summary>
    TemporaryTablespace,
    /// <summary><c>QUOTA</c> on a tablespace.</summary>
    Quota,
    /// <summary><c>PROFILE</c>.</summary>
    Profile,
    /// <summary><c>PASSWORD EXPIRE</c>.</summary>
    PasswordExpire,
    /// <summary><c>ACCOUNT LOCK</c>.</summary>
    AccountLock,
    /// <summary><c>ACCOUNT UNLOCK</c>.</summary>
    AccountUnlock,
    /// <summary><c>DEFAULT ROLE</c>.</summary>
    DefaultRole,
    /// <summary><c>CONTAINER</c>.</summary>
    Container,
    /// <summary><c>ENABLE EDITIONS</c>.</summary>
    EnableEditions,
    /// <summary><c>DISABLE EDITIONS</c>.</summary>
    DisableEditions,
    /// <summary><c>READ ONLY</c>.</summary>
    ReadOnly,
    /// <summary><c>READ WRITE</c>.</summary>
    ReadWrite,
    /// <summary><c>GRANT CONNECT THROUGH</c>.</summary>
    GrantConnectThrough,
    /// <summary><c>REVOKE CONNECT THROUGH</c>.</summary>
    RevokeConnectThrough,
    /// <summary>A clause without a dedicated value; kept as written in the token tree.</summary>
    Other,
}

/// <summary>The role set of a user's <c>DEFAULT ROLE</c> clause: an explicit list, <c>ALL</c> or <c>NONE</c>.</summary>
public enum RoleSetKind
{
    /// <summary>A list of named roles.</summary>
    Roles,
    /// <summary><c>ALL</c>.</summary>
    All,
    /// <summary><c>NONE</c>.</summary>
    None,
}

/// <summary>
/// One clause of CREATE USER / ALTER USER, in source order. Clauses without a dedicated kind are kept as
/// <see cref="UserClauseKind.Other"/> with their single token (<see cref="Syntax"/>) so the statement still round-trips.
/// </summary>
public sealed partial class UserClause : SqlNode
{
    /// <summary>Which clause this is.</summary>
    public UserClauseKind Kind { get; set; }
    /// <summary>IDENTIFIED BY password. Printed as typed (passwords are case-sensitive even when unquoted).</summary>
    public Identifier? Password { get; set; }
    /// <summary>IDENTIFIED BY password REPLACE old_password.</summary>
    public Identifier? ReplacePassword { get; set; }
    /// <summary>IDENTIFIED EXTERNALLY/GLOBALLY AS 'name'.</summary>
    public Expression? ExternalName { get; set; }
    /// <summary>True for <c>LOCAL TEMPORARY TABLESPACE</c>.</summary>
    public bool Local { get; set; }
    /// <summary>Tablespace of DEFAULT/TEMPORARY TABLESPACE and QUOTA ... ON.</summary>
    public Identifier? Tablespace { get; set; }
    /// <summary>QUOTA size, e.g. 100M (sizes lex as one number token).</summary>
    public Expression? Quota { get; set; }
    /// <summary>True for <c>UNLIMITED</c> in a quota clause.</summary>
    public bool Unlimited { get; set; }
    /// <summary>The profile name after <c>PROFILE</c>, or null when absent.</summary>
    public Identifier? Profile { get; set; }
    /// <summary>The proxy user after <c>CONNECT THROUGH</c> in <c>GRANT</c> or <c>REVOKE</c>, or null when absent.</summary>
    public Identifier? Proxy { get; set; }
    /// <summary>True for <c>ENTERPRISE USERS</c>.</summary>
    public bool EnterpriseUsers { get; set; }
    /// <summary>DEFAULT ROLE list, or WITH ROLE / WITH NO ROLES of a GRANT CONNECT THROUGH.</summary>
    public RoleSetKind? RoleSet { get; set; }
    /// <summary>The roles named in a <c>DEFAULT ROLE</c> clause; empty when absent.</summary>
    public List<Identifier> Roles { get; set; } = [];
    /// <summary>The roles named after <c>EXCEPT</c>; empty when absent.</summary>
    public List<Identifier> ExceptRoles { get; set; } = [];
    /// <summary>True for <c>AUTHENTICATION REQUIRED</c>.</summary>
    public bool AuthenticationRequired { get; set; }
    /// <summary>CONTAINER = CURRENT | ALL.</summary>
    public GrantContainer? Container { get; set; }
    /// <summary>ENABLE EDITIONS FORCE.</summary>
    public bool Force { get; set; }
    /// <summary>The unmodelled token of an <see cref="UserClauseKind.Other"/> clause.</summary>
    public List<SyntaxItem> Syntax { get; set; } = [];
}

/// <summary><c>CREATE USER [IF NOT EXISTS] name clauses</c>.</summary>
public sealed partial class CreateUserStatement : Statement
{
    /// <summary>True when <c>IF NOT EXISTS</c> is written.</summary>
    public bool IfNotExists { get; set; }
    /// <summary>The user name.</summary>
    public required Identifier Name { get; set; }
    /// <summary>The clauses, in source order; empty when absent.</summary>
    public List<UserClause> Clauses { get; set; } = [];
}

/// <summary><c>ALTER USER name clauses</c>.</summary>
public sealed partial class AlterUserStatement : Statement
{
    /// <summary>True when <c>IF EXISTS</c> is written.</summary>
    public bool IfExists { get; set; }
    /// <summary>The user name.</summary>
    public required Identifier Name { get; set; }
    /// <summary>The clauses, in source order; empty when absent.</summary>
    public List<UserClause> Clauses { get; set; } = [];
}

// ---------- CREATE / ALTER ROLE ----------

/// <summary>The authentication of a role.</summary>
public enum RoleAuthentication
{
    /// <summary><c>NOT IDENTIFIED</c>.</summary>
    NotIdentified,
    /// <summary><c>IDENTIFIED BY</c> a password.</summary>
    Password,
    /// <summary><c>IDENTIFIED USING</c> a package.</summary>
    Package,
    /// <summary><c>IDENTIFIED EXTERNALLY</c>.</summary>
    External,
    /// <summary><c>IDENTIFIED GLOBALLY</c>.</summary>
    Global,
}

/// <summary><c>CREATE ROLE [IF NOT EXISTS] name</c>.</summary>
public sealed partial class CreateRoleStatement : Statement
{
    /// <summary>True when <c>IF NOT EXISTS</c> is written.</summary>
    public bool IfNotExists { get; set; }
    /// <summary>The role name.</summary>
    public required Identifier Name { get; set; }
    /// <summary>NOT IDENTIFIED, IDENTIFIED BY/USING/EXTERNALLY/GLOBALLY, or null when absent.</summary>
    public RoleAuthentication? Authentication { get; set; }
    /// <summary>IDENTIFIED BY password. Printed as typed (passwords are case-sensitive even when unquoted).</summary>
    public Identifier? Password { get; set; }
    /// <summary>IDENTIFIED USING [schema.]package.</summary>
    public List<Identifier> Package { get; set; } = [];
    /// <summary>The container scope, or null when absent.</summary>
    public GrantContainer? Container { get; set; }
}

/// <summary><c>ALTER ROLE name</c> with an optional new authentication.</summary>
public sealed partial class AlterRoleStatement : Statement
{
    /// <summary>True when <c>IF EXISTS</c> is written.</summary>
    public bool IfExists { get; set; }
    /// <summary>The role name.</summary>
    public required Identifier Name { get; set; }
    /// <summary>The new authentication, or null when absent.</summary>
    public RoleAuthentication? Authentication { get; set; }
    /// <summary>The new password, or null when absent.</summary>
    public Identifier? Password { get; set; }
    /// <summary>The package name after <c>IDENTIFIED USING</c>; empty when absent.</summary>
    public List<Identifier> Package { get; set; } = [];
    /// <summary>The container scope, or null when absent.</summary>
    public GrantContainer? Container { get; set; }
}

// ---------- CREATE / ALTER PROFILE ----------

/// <summary>The keyword value of a profile limit: UNLIMITED, DEFAULT or NULL.</summary>
public enum ProfileKeyword
{
    /// <summary><c>UNLIMITED</c>.</summary>
    Unlimited,
    /// <summary><c>DEFAULT</c>.</summary>
    Default,
    /// <summary><c>NULL</c>.</summary>
    Null,
}

/// <summary>
/// One resource or password parameter of a profile: a name with an integer or expression (<see cref="Value"/>),
/// a keyword (<see cref="Keyword"/>), or a function name (PASSWORD_VERIFY_FUNCTION).
/// </summary>
public sealed partial class ProfileLimit : SqlNode
{
    /// <summary>The resource or password parameter name, such as <c>SESSIONS_PER_USER</c>.</summary>
    public required Identifier Name { get; set; }
    /// <summary>The numeric limit, or null when the keyword form is used.</summary>
    public Expression? Value { get; set; }
    /// <summary>The keyword limit, such as <c>UNLIMITED</c>, or null when the numeric form is used.</summary>
    public ProfileKeyword? Keyword { get; set; }
    /// <summary>PASSWORD_VERIFY_FUNCTION [schema.]function.</summary>
    public List<Identifier> Function { get; set; } = [];
}

/// <summary><c>CREATE PROFILE [IF NOT EXISTS] name LIMIT ...</c>.</summary>
public sealed partial class CreateProfileStatement : Statement
{
    /// <summary>True when <c>IF NOT EXISTS</c> is written.</summary>
    public bool IfNotExists { get; set; }
    /// <summary>The profile name.</summary>
    public required Identifier Name { get; set; }
    /// <summary>The limits, in source order; empty when absent.</summary>
    public List<ProfileLimit> Limits { get; set; } = [];
    /// <summary>The container scope, or null when absent.</summary>
    public GrantContainer? Container { get; set; }
}

/// <summary><c>ALTER PROFILE name LIMIT ...</c>.</summary>
public sealed partial class AlterProfileStatement : Statement
{
    /// <summary>True when <c>IF EXISTS</c> is written.</summary>
    public bool IfExists { get; set; }
    /// <summary>The profile name.</summary>
    public required Identifier Name { get; set; }
    /// <summary>The limits, in source order; empty when absent.</summary>
    public List<ProfileLimit> Limits { get; set; } = [];
    /// <summary>The container scope, or null when absent.</summary>
    public GrantContainer? Container { get; set; }
    /// <summary>ALTER PROFILE ... CASCADE: apply the new limits to users already assigned the profile.</summary>
    public bool Cascade { get; set; }
}

// ---------- CREATE DIRECTORY / DATABASE LINK / CONTEXT ----------

/// <summary><c>CREATE [OR REPLACE] DIRECTORY name AS 'path'</c>.</summary>
public sealed partial class CreateDirectoryStatement : Statement
{
    /// <summary>True when <c>OR REPLACE</c> is written.</summary>
    public bool OrReplace { get; set; }
    /// <summary>True when <c>IF NOT EXISTS</c> is written.</summary>
    public bool IfNotExists { get; set; }
    /// <summary>The directory object name.</summary>
    public required Identifier Name { get; set; }
    /// <summary>The <c>SHARING</c> setting, or null when absent.</summary>
    public SharingKind? Sharing { get; set; }
    /// <summary>AS 'path'.</summary>
    public required Expression Path { get; set; }
}

/// <summary><c>CREATE [SHARED] [PUBLIC] DATABASE LINK name CONNECT TO ... USING 'service'</c>.</summary>
public sealed partial class CreateDatabaseLinkStatement : Statement
{
    /// <summary>True when <c>OR REPLACE</c> is written.</summary>
    public bool OrReplace { get; set; }
    /// <summary>True when <c>IF NOT EXISTS</c> is written.</summary>
    public bool IfNotExists { get; set; }
    /// <summary>True for <c>PUBLIC</c>.</summary>
    public bool Public { get; set; }
    /// <summary>True for <c>SHARED</c>.</summary>
    public bool Shared { get; set; }
    /// <summary>The link name parts, schema first when qualified.</summary>
    public List<Identifier> Name { get; set; } = [];
    /// <summary>CONNECT TO user [IDENTIFIED BY password].</summary>
    public Identifier? ConnectUser { get; set; }
    /// <summary>The password after <c>IDENTIFIED BY</c> in <c>CONNECT TO</c>, or null when absent. Printed as typed (passwords are case-sensitive even when unquoted).</summary>
    public Identifier? ConnectPassword { get; set; }
    /// <summary>CONNECT TO CURRENT_USER.</summary>
    public bool CurrentUser { get; set; }
    /// <summary>The user after <c>AUTHENTICATED BY</c> (shared links), or null when absent.</summary>
    public Identifier? AuthenticatedUser { get; set; }
    /// <summary>The password after <c>IDENTIFIED BY</c> in <c>AUTHENTICATED BY</c>, or null when absent.</summary>
    public Identifier? AuthenticatedPassword { get; set; }
    /// <summary>USING 'connect_string'.</summary>
    public Expression? Using { get; set; }
    /// <summary>Clauses this model does not cover (e.g. CONNECT WITH credential), as a token tree.</summary>
    public List<SyntaxItem> OtherClauses { get; set; } = [];
}

/// <summary>When an application context is initialized.</summary>
public enum ContextInit
{
    /// <summary><c>INITIALIZED EXTERNALLY</c>.</summary>
    Externally,
    /// <summary><c>INITIALIZED GLOBALLY</c>.</summary>
    Globally,
}

/// <summary><c>CREATE CONTEXT name USING package</c>.</summary>
public sealed partial class CreateContextStatement : Statement
{
    /// <summary>True when <c>OR REPLACE</c> is written.</summary>
    public bool OrReplace { get; set; }
    /// <summary>True when <c>IF NOT EXISTS</c> is written.</summary>
    public bool IfNotExists { get; set; }
    /// <summary>The context namespace name.</summary>
    public required Identifier Name { get; set; }
    /// <summary>The package name after <c>USING</c>.</summary>
    public List<Identifier> Package { get; set; } = [];
    /// <summary>The <c>INITIALIZED</c> setting, or null when absent.</summary>
    public ContextInit? Initialized { get; set; }
    /// <summary>True for <c>ACCESSED GLOBALLY</c>.</summary>
    public bool AccessedGlobally { get; set; }
}

// ---------- CREATE / ALTER TABLESPACE ----------

/// <summary>The kind of tablespace.</summary>
public enum TablespaceType
{
    /// <summary><c>PERMANENT</c>.</summary>
    Permanent,
    /// <summary><c>TEMPORARY</c>.</summary>
    Temporary,
    /// <summary><c>UNDO</c>.</summary>
    Undo,
}
/// <summary>How extents are sized in a tablespace.</summary>
public enum ExtentPolicy
{
    /// <summary><c>EXTENT MANAGEMENT LOCAL AUTOALLOCATE</c>.</summary>
    Autoallocate,
    /// <summary><c>EXTENT MANAGEMENT LOCAL UNIFORM</c>.</summary>
    Uniform,
}
/// <summary>How free space is managed in segments of a tablespace.</summary>
public enum SegmentSpaceMode
{
    /// <summary><c>SEGMENT SPACE MANAGEMENT AUTO</c>.</summary>
    Auto,
    /// <summary><c>SEGMENT SPACE MANAGEMENT MANUAL</c>.</summary>
    Manual,
}
/// <summary>The mode of an <c>OFFLINE</c> clause.</summary>
public enum OfflineMode
{
    /// <summary><c>OFFLINE NORMAL</c>.</summary>
    Normal,
    /// <summary><c>OFFLINE TEMPORARY</c>.</summary>
    Temporary,
    /// <summary><c>OFFLINE IMMEDIATE</c>.</summary>
    Immediate,
}

/// <summary>A datafile or tempfile: 'name' [SIZE n] [REUSE] [AUTOEXTEND ON|OFF [NEXT n] [MAXSIZE n|UNLIMITED]].</summary>
public sealed partial class DatafileSpec : SqlNode
{
    /// <summary>TEMPFILE rather than DATAFILE.</summary>
    public bool Tempfile { get; set; }
    /// <summary>The file name, or null for an Oracle-managed file.</summary>
    public Expression? Path { get; set; }
    /// <summary>The size after <c>SIZE</c>, or null when absent.</summary>
    public Expression? Size { get; set; }
    /// <summary>True for <c>REUSE</c>.</summary>
    public bool Reuse { get; set; }
    /// <summary>AUTOEXTEND ON (true) or OFF (false); null when the clause is absent.</summary>
    public bool? Autoextend { get; set; }
    /// <summary>The <c>NEXT</c> size of <c>AUTOEXTEND</c>, or null when absent.</summary>
    public Expression? NextSize { get; set; }
    /// <summary>The <c>MAXSIZE</c> limit, or null when absent.</summary>
    public Expression? MaxSize { get; set; }
    /// <summary>True for <c>MAXSIZE UNLIMITED</c>.</summary>
    public bool MaxSizeUnlimited { get; set; }
}

/// <summary><c>CREATE [BIGFILE] TABLESPACE name ...</c>.</summary>
public sealed partial class CreateTablespaceStatement : Statement
{
    /// <summary>BIGFILE (true) or SMALLFILE (false); null when neither is given.</summary>
    public bool? Bigfile { get; set; }
    /// <summary>The tablespace type, or null for a permanent tablespace written without the keyword.</summary>
    public TablespaceType? Kind { get; set; }
    /// <summary>True when <c>IF NOT EXISTS</c> is written.</summary>
    public bool IfNotExists { get; set; }
    /// <summary>The tablespace name.</summary>
    public required Identifier Name { get; set; }
    /// <summary>DATAFILE or TEMPFILE specs (one keyword per clause, comma-separated specs).</summary>
    public List<DatafileSpec> Files { get; set; } = [];
    /// <summary>LOGGING (true) or NOLOGGING (false); null when neither is written.</summary>
    public bool? Logging { get; set; }
    /// <summary>True for <c>FORCE LOGGING</c>.</summary>
    public bool ForceLogging { get; set; }
    /// <summary>ONLINE (true) or OFFLINE (false); null when neither is written.</summary>
    public bool? Online { get; set; }
    /// <summary>True when <c>EXTENT MANAGEMENT LOCAL</c> is written; <see cref="ExtentPolicy"/> then holds AUTOALLOCATE or UNIFORM.</summary>
    public bool ExtentManagementLocal { get; set; }
    /// <summary>The extent management policy, or null when absent.</summary>
    public ExtentPolicy? ExtentPolicy { get; set; }
    /// <summary>The size after <c>UNIFORM SIZE</c>, or null when absent.</summary>
    public Expression? UniformSize { get; set; }
    /// <summary>The segment space management mode, or null when absent.</summary>
    public SegmentSpaceMode? SegmentSpace { get; set; }
    /// <summary>True when an <c>ENCRYPTION</c> clause is written.</summary>
    public bool Encryption { get; set; }
    /// <summary>The algorithm after <c>USING</c> in the encryption clause, or null when absent.</summary>
    public Expression? EncryptionUsing { get; set; }
    /// <summary>DEFAULT STORAGE (ENCRYPT).</summary>
    public bool DefaultStorageEncrypt { get; set; }
    /// <summary>Rarely used options (BLOCKSIZE, FLASHBACK, RETENTION, ...) as a token tree.</summary>
    public List<SyntaxItem> OtherClauses { get; set; } = [];
}

/// <summary>The kind of an <c>ALTER TABLESPACE</c> action.</summary>
public enum TablespaceActionKind
{
    /// <summary><c>ADD DATAFILE</c>.</summary>
    AddDatafile,
    /// <summary><c>ADD TEMPFILE</c>.</summary>
    AddTempfile,
    /// <summary><c>RESIZE</c> of a file.</summary>
    Resize,
    /// <summary><c>AUTOEXTEND</c> of a file.</summary>
    Autoextend,
    /// <summary><c>ONLINE</c>.</summary>
    Online,
    /// <summary><c>OFFLINE</c>.</summary>
    Offline,
    /// <summary><c>READ ONLY</c>.</summary>
    ReadOnly,
    /// <summary><c>READ WRITE</c>.</summary>
    ReadWrite,
    /// <summary><c>RENAME TO</c>.</summary>
    RenameTo,
    /// <summary><c>LOGGING</c>.</summary>
    Logging,
    /// <summary><c>NOLOGGING</c>.</summary>
    NoLogging,
    /// <summary><c>BEGIN BACKUP</c>.</summary>
    BeginBackup,
    /// <summary><c>END BACKUP</c>.</summary>
    EndBackup,
    /// <summary>An action without a dedicated value; kept as written in the token tree.</summary>
    Other,
}

/// <summary>One action of ALTER TABLESPACE. File-level actions (RESIZE, AUTOEXTEND, ONLINE/OFFLINE of a file) set <see cref="Target"/>.</summary>
public sealed partial class TablespaceAction : SqlNode
{
    /// <summary>Which action this is.</summary>
    public TablespaceActionKind Kind { get; set; }
    /// <summary>ADD DATAFILE/TEMPFILE specs.</summary>
    public List<DatafileSpec> Files { get; set; } = [];
    /// <summary>The file the action applies to, or null when absent.</summary>
    public DatafileSpec? Target { get; set; }
    /// <summary>OFFLINE [NORMAL | TEMPORARY | IMMEDIATE] of the tablespace.</summary>
    public OfflineMode? Mode { get; set; }
    /// <summary>The new name after <c>RENAME TO</c>, or null for other actions.</summary>
    public Identifier? NewName { get; set; }
    /// <summary>The unmodelled token of an <see cref="TablespaceActionKind.Other"/> action.</summary>
    public List<SyntaxItem> Syntax { get; set; } = [];
}

/// <summary><c>ALTER TABLESPACE [IF EXISTS] name action</c>.</summary>
public sealed partial class AlterTablespaceStatement : Statement
{
    /// <summary>True when <c>IF EXISTS</c> is written.</summary>
    public bool IfExists { get; set; }
    /// <summary>The tablespace name.</summary>
    public required Identifier Name { get; set; }
    /// <summary>The actions, in source order.</summary>
    public List<TablespaceAction> Actions { get; set; } = [];
}

// ---------- AUDIT / NOAUDIT / AUDIT POLICY ----------

/// <summary>A traditional audit option as a keyword phrase: SELECT, CREATE TABLE, ALL.</summary>
public sealed partial class AuditOption : SqlNode
{
    /// <summary>The audited statement or privilege, as written.</summary>
    public string Name { get; set; } = "";
}

/// <summary>The object of ON: [schema.]object, DIRECTORY name, or DEFAULT.</summary>
public sealed partial class AuditObject : SqlNode
{
    /// <summary>True for <c>DEFAULT</c>, which audits every object.</summary>
    public bool Default { get; set; }
    /// <summary>True for <c>DIRECTORY</c>.</summary>
    public bool Directory { get; set; }
    /// <summary>The object name parts; empty when the default is used.</summary>
    public List<Identifier> Name { get; set; } = [];
}

/// <summary>The audit granularity written after <c>BY</c>.</summary>
public enum AuditBy
{
    /// <summary><c>BY ACCESS</c>: audit each access.</summary>
    Access,
    /// <summary><c>BY SESSION</c>: audit once per session.</summary>
    Session,
}

/// <summary>
/// AUDIT / NOAUDIT. Traditional form: options [ON object] [BY users | BY ACCESS|SESSION] [WHENEVER [NOT] SUCCESSFUL].
/// Unified form: POLICY name [BY users | EXCEPT users] or CONTEXT NAMESPACE ns ATTRIBUTES a, b [BY users].
/// </summary>
public sealed partial class AuditStatement : Statement
{
    /// <summary>True for <c>NOAUDIT</c>; false for <c>AUDIT</c>.</summary>
    public bool Noaudit { get; set; }
    /// <summary>AUDIT POLICY name (unified).</summary>
    public Identifier? Policy { get; set; }
    /// <summary>AUDIT CONTEXT NAMESPACE ns (unified).</summary>
    public Identifier? ContextNamespace { get; set; }
    /// <summary>The attributes after <c>CONTEXT NAMESPACE ns ATTRIBUTES</c> (unified), in source order; empty when absent.</summary>
    public List<Identifier> ContextAttributes { get; set; } = [];
    /// <summary>Traditional audit options.</summary>
    public List<AuditOption> Options { get; set; } = [];
    /// <summary>Traditional ON object.</summary>
    public AuditObject? On { get; set; }
    /// <summary>The users named after <c>BY</c>; empty when absent.</summary>
    public List<Identifier> ByUsers { get; set; } = [];
    /// <summary>BY USERS WITH ROLES role, ... (unified).</summary>
    public List<Identifier> WithRoles { get; set; } = [];
    /// <summary>True for <c>WITH GRANTED ROLES</c> (rather than <c>WITH ROLES</c>).</summary>
    public bool GrantedRoles { get; set; }
    /// <summary>EXCEPT user, ... (unified policies).</summary>
    public List<Identifier> ExceptUsers { get; set; } = [];
    /// <summary>BY ACCESS or BY SESSION (traditional).</summary>
    public AuditBy? By { get; set; }
    /// <summary>WHENEVER [NOT] SUCCESSFUL: true for <c>SUCCESSFUL</c>, false for <c>NOT SUCCESSFUL</c>, null when absent.</summary>
    public bool? Successful { get; set; }
}

/// <summary>The kind of list in a unified audit policy.</summary>
public enum AuditPolicyListKind
{
    /// <summary><c>PRIVILEGES</c>.</summary>
    Privileges,
    /// <summary><c>ACTIONS</c>.</summary>
    Actions,
    /// <summary><c>ROLES</c>.</summary>
    Roles,
}

/// <summary>When a unified audit policy condition is evaluated.</summary>
public enum AuditEvaluation
{
    /// <summary><c>EVALUATE PER STATEMENT</c>.</summary>
    Statement,
    /// <summary><c>EVALUATE PER SESSION</c>.</summary>
    Session,
    /// <summary><c>EVALUATE PER INSTANCE</c>.</summary>
    Instance,
}

/// <summary>One item of a PRIVILEGES or ACTIONS list: an optional COMPONENT=name, a keyword phrase, and an optional ON object.</summary>
public sealed partial class AuditPolicyItem : SqlNode
{
    /// <summary>The component name before the dot, such as <c>DATAPUMP</c>, or null when absent.</summary>
    public Identifier? Component { get; set; }
    /// <summary>The action or privilege name.</summary>
    public string Name { get; set; } = "";
    /// <summary>The object after <c>ON</c>, or null when absent.</summary>
    public AuditObject? On { get; set; }
}

/// <summary>A PRIVILEGES, ACTIONS or ROLES list of CREATE/ALTER AUDIT POLICY. ALTER ... DROP sets <see cref="Drop"/>.</summary>
public sealed partial class AuditPolicyList : SqlNode
{
    /// <summary>The list kind: privileges, actions or roles.</summary>
    public AuditPolicyListKind Kind { get; set; }
    /// <summary>True when the list is dropped with <c>DROP</c>.</summary>
    public bool Drop { get; set; }
    /// <summary>The items of the list, in source order.</summary>
    public List<AuditPolicyItem> Items { get; set; } = [];
    /// <summary>The roles named by a roles list; empty for the other kinds.</summary>
    public List<Identifier> Roles { get; set; } = [];
}

/// <summary><c>CREATE AUDIT POLICY [IF NOT EXISTS] name ...</c>.</summary>
public sealed partial class CreateAuditPolicyStatement : Statement
{
    /// <summary>True when <c>IF NOT EXISTS</c> is written.</summary>
    public bool IfNotExists { get; set; }
    /// <summary>The policy name.</summary>
    public required Identifier Name { get; set; }
    /// <summary>The privilege, action and role lists, in source order.</summary>
    public List<AuditPolicyList> Lists { get; set; } = [];
    /// <summary>The <c>WHEN</c> condition string, or null when absent.</summary>
    public Expression? Condition { get; set; }
    /// <summary>The evaluation setting, or null when absent.</summary>
    public AuditEvaluation? Evaluate { get; set; }
    /// <summary>True for <c>ONLY TOPLEVEL</c>.</summary>
    public bool OnlyToplevel { get; set; }
    /// <summary>The container scope, or null when absent.</summary>
    public GrantContainer? Container { get; set; }
}

/// <summary><c>ALTER AUDIT POLICY [IF EXISTS] name ...</c>.</summary>
public sealed partial class AlterAuditPolicyStatement : Statement
{
    /// <summary>True when <c>IF EXISTS</c> is written.</summary>
    public bool IfExists { get; set; }
    /// <summary>The policy name.</summary>
    public required Identifier Name { get; set; }
    /// <summary>The lists added or dropped, in source order.</summary>
    public List<AuditPolicyList> Lists { get; set; } = [];
    /// <summary>The <c>WHEN</c> condition, or null when absent.</summary>
    public Expression? Condition { get; set; }
    /// <summary>The evaluation setting, or null when absent.</summary>
    public AuditEvaluation? Evaluate { get; set; }
    /// <summary>ALTER AUDIT POLICY ... CONDITION DROP.</summary>
    public bool DropCondition { get; set; }
}

// ---------- Generic object DDL (typed header + token-tree body) ----------

/// <summary>The kind of schema object or other database object that a generic DDL statement names, such as <c>TABLE</c> or <c>USER</c>.</summary>
public enum ObjectKind
{
    /// <summary><c>TABLE</c>.</summary>
    Table,
    /// <summary><c>VIEW</c>.</summary>
    View,
    /// <summary><c>MATERIALIZED VIEW</c>.</summary>
    MaterializedView,
    /// <summary><c>MATERIALIZED VIEW LOG</c>.</summary>
    MaterializedViewLog,
    /// <summary><c>MATERIALIZED ZONEMAP</c>.</summary>
    MaterializedZonemap,
    /// <summary><c>INDEX</c>.</summary>
    Index,
    /// <summary><c>INDEXTYPE</c>.</summary>
    Indextype,
    /// <summary><c>SEQUENCE</c>.</summary>
    Sequence,
    /// <summary><c>SYNONYM</c>.</summary>
    Synonym,
    /// <summary><c>PROCEDURE</c>.</summary>
    Procedure,
    /// <summary><c>FUNCTION</c>.</summary>
    Function,
    /// <summary><c>PACKAGE</c>.</summary>
    Package,
    /// <summary><c>PACKAGE BODY</c>.</summary>
    PackageBody,
    /// <summary><c>TRIGGER</c>.</summary>
    Trigger,
    /// <summary><c>TYPE</c>.</summary>
    Type,
    /// <summary><c>TYPE BODY</c>.</summary>
    TypeBody,
    /// <summary><c>DATABASE LINK</c>.</summary>
    DatabaseLink,
    /// <summary><c>USER</c>.</summary>
    User,
    /// <summary><c>ROLE</c>.</summary>
    Role,
    /// <summary><c>PROFILE</c>.</summary>
    Profile,
    /// <summary><c>TABLESPACE</c>.</summary>
    Tablespace,
    /// <summary><c>TABLESPACE SET</c>.</summary>
    TablespaceSet,
    /// <summary><c>DIRECTORY</c>.</summary>
    Directory,
    /// <summary><c>LIBRARY</c>.</summary>
    Library,
    /// <summary><c>JAVA SOURCE</c>.</summary>
    JavaSource,
    /// <summary><c>JAVA CLASS</c>.</summary>
    JavaClass,
    /// <summary><c>JAVA RESOURCE</c>.</summary>
    JavaResource,
    /// <summary><c>CLUSTER</c>.</summary>
    Cluster,
    /// <summary><c>CONTEXT</c>.</summary>
    Context,
    /// <summary><c>DIMENSION</c>.</summary>
    Dimension,
    /// <summary><c>DOMAIN</c>.</summary>
    Domain,
    /// <summary><c>EDITION</c>.</summary>
    Edition,
    /// <summary><c>OPERATOR</c>.</summary>
    Operator,
    /// <summary><c>OUTLINE</c>.</summary>
    Outline,
    /// <summary><c>FLASHBACK ARCHIVE</c>.</summary>
    FlashbackArchive,
    /// <summary><c>ROLLBACK SEGMENT</c>.</summary>
    RollbackSegment,
    /// <summary><c>RESTORE POINT</c>.</summary>
    RestorePoint,
    /// <summary><c>DISKGROUP</c>.</summary>
    Diskgroup,
    /// <summary><c>PLUGGABLE DATABASE</c>.</summary>
    PluggableDatabase,
    /// <summary><c>DATABASE</c>.</summary>
    Database,
    /// <summary><c>LOCKDOWN PROFILE</c>.</summary>
    LockdownProfile,
    /// <summary><c>AUDIT POLICY</c>.</summary>
    AuditPolicy,
    /// <summary><c>ANALYTIC VIEW</c>.</summary>
    AnalyticView,
    /// <summary><c>ATTRIBUTE DIMENSION</c>.</summary>
    AttributeDimension,
    /// <summary><c>HIERARCHY</c>.</summary>
    Hierarchy,
    /// <summary><c>INMEMORY JOIN GROUP</c>.</summary>
    InmemoryJoinGroup,
    /// <summary><c>MINING MODEL</c>.</summary>
    MiningModel,
    /// <summary><c>PROPERTY GRAPH</c>.</summary>
    PropertyGraph,
    /// <summary><c>JSON RELATIONAL DUALITY VIEW</c>.</summary>
    JsonRelationalDualityView,
    /// <summary><c>SQL TRANSLATION PROFILE</c>.</summary>
    SqlTranslationProfile,
    /// <summary><c>CONTROLFILE</c>.</summary>
    ControlFile,
    /// <summary><c>PFILE</c>.</summary>
    Pfile,
    /// <summary><c>SPFILE</c>.</summary>
    Spfile,
    /// <summary><c>SCHEMA</c>.</summary>
    Schema,
    /// <summary><c>RESOURCE COST</c>.</summary>
    ResourceCost,
    /// <summary><c>SYSTEM</c>.</summary>
    System,
    /// <summary><c>SESSION</c>.</summary>
    Session,
    /// <summary><c>JSON</c>.</summary>
    Json,
    /// <summary><c>MLE MODULE</c>.</summary>
    MleModule,
    /// <summary><c>MLE ENV</c>.</summary>
    MleEnv,
    /// <summary><c>SHARDING</c>.</summary>
    Sharding,
    /// <summary>An object kind without a dedicated value.</summary>
    Other,
}

/// <summary>CREATE of an object kind that has no dedicated node; the clause body is a token tree.</summary>
public sealed partial class CreateObjectStatement : Statement
{
    /// <summary>The kind of object created.</summary>
    public ObjectKind Kind { get; set; }
    /// <summary>True when <c>OR REPLACE</c> is written.</summary>
    public bool OrReplace { get; set; }
    /// <summary>True when <c>PUBLIC</c> is written (for synonyms and database links).</summary>
    public bool Public { get; set; }
    /// <summary>True when <c>IF NOT EXISTS</c> is written.</summary>
    public bool IfNotExists { get; set; }
    /// <summary>The object name parts, schema first when qualified.</summary>
    public List<Identifier> Name { get; set; } = [];
    /// <summary>The remaining clauses of the statement as a token tree; empty when there are none.</summary>
    public List<SyntaxItem> Body { get; set; } = [];
}

/// <summary>ALTER of an object kind that has no dedicated node; the clause body is a token tree.</summary>
public sealed partial class AlterObjectStatement : Statement
{
    /// <summary>The kind of object altered.</summary>
    public ObjectKind Kind { get; set; }
    /// <summary>True when <c>PUBLIC</c> is written.</summary>
    public bool Public { get; set; }
    /// <summary>True when <c>IF EXISTS</c> is written.</summary>
    public bool IfExists { get; set; }
    /// <summary>The object name parts, schema first when qualified.</summary>
    public List<Identifier> Name { get; set; } = [];
    /// <summary>The clauses of the statement as a token tree; empty when there are none.</summary>
    public List<SyntaxItem> Body { get; set; } = [];
}

/// <summary>Statements such as ASSOCIATE/DISASSOCIATE STATISTICS, ADMINISTER KEY MANAGEMENT, FLASHBACK DATABASE (token-tree body).</summary>
public sealed partial class AdminStatement : Statement
{
    /// <summary>The leading verb of the statement, such as <c>ANALYZE</c> or <c>AUDIT</c>.</summary>
    public string Verb { get; set; } = "";
    /// <summary>The rest of the statement as a token tree.</summary>
    public List<SyntaxItem> Body { get; set; } = [];
}

/// <summary><c>DROP kind [IF EXISTS] name [CASCADE CONSTRAINTS] [PURGE]</c>.</summary>
public sealed partial class DropStatement : Statement
{
    /// <summary>The kind of object dropped.</summary>
    public ObjectKind Kind { get; set; }
    /// <summary>True when <c>PUBLIC</c> is written.</summary>
    public bool Public { get; set; }
    /// <summary>True when <c>IF EXISTS</c> is written.</summary>
    public bool IfExists { get; set; }
    /// <summary>The object name parts, schema first when qualified.</summary>
    public List<Identifier> Name { get; set; } = [];
    /// <summary>DROP MATERIALIZED VIEW LOG ON table.</summary>
    public List<Identifier> OnTable { get; set; } = [];
    /// <summary>True for <c>CASCADE CONSTRAINTS</c>.</summary>
    public bool CascadeConstraints { get; set; }
    /// <summary>True for <c>CASCADE</c>.</summary>
    public bool Cascade { get; set; }
    /// <summary>True for <c>PURGE</c>.</summary>
    public bool Purge { get; set; }
    /// <summary>True for <c>FORCE</c>.</summary>
    public bool Force { get; set; }
    /// <summary>True for <c>INCLUDING CONTENTS</c>.</summary>
    public bool IncludingContents { get; set; }
    /// <summary>True for <c>AND DATAFILES</c>.</summary>
    public bool AndDatafiles { get; set; }
    /// <summary>True for <c>VALIDATE</c>.</summary>
    public bool Validate { get; set; }
    /// <summary>True for <c>INVALIDATE</c>.</summary>
    public bool Invalidate { get; set; }
    /// <summary>Trailing words of the statement that have no flag of their own, kept as tokens.</summary>
    public List<SyntaxItem> Rest { get; set; } = [];
}

// ---------- Physical attributes ----------

/// <summary>A physical storage attribute of a table, index or tablespace definition.</summary>
public abstract partial class PhysicalAttribute : SqlNode { }

/// <summary><c>TABLESPACE name</c>, optionally <c>DEFAULT</c>.</summary>
public sealed partial class TablespaceAttribute : PhysicalAttribute
{
    /// <summary>The tablespace name.</summary>
    public required Identifier Name { get; set; }
    /// <summary>True for <c>DEFAULT TABLESPACE</c>.</summary>
    public bool Default { get; set; }
}

/// <summary>The numeric physical attribute kind.</summary>
public enum NumericAttributeKind
{
    /// <summary><c>PCTFREE</c>.</summary>
    PctFree,
    /// <summary><c>PCTUSED</c>.</summary>
    PctUsed,
    /// <summary><c>INITRANS</c>.</summary>
    InitTrans,
    /// <summary><c>MAXTRANS</c>.</summary>
    MaxTrans,
    /// <summary><c>PCTTHRESHOLD</c>.</summary>
    PctThreshold,
    /// <summary><c>FREELISTS</c>.</summary>
    Freelists,
}

/// <summary>A numeric physical attribute, such as <c>PCTFREE 10</c>.</summary>
public sealed partial class NumericAttribute : PhysicalAttribute
{
    /// <summary>Which attribute this is.</summary>
    public NumericAttributeKind Kind { get; set; }
    /// <summary>The numeric value.</summary>
    public required Expression Value { get; set; }
}

/// <summary><c>STORAGE (...)</c>, with its options as written.</summary>
public sealed partial class StorageAttribute : PhysicalAttribute
{
    /// <summary>The storage options, in source order; empty for <c>STORAGE ()</c>.</summary>
    public List<StorageOption> Options { get; set; } = [];
}

/// <summary>One option of a <c>STORAGE</c> clause, such as <c>INITIAL 64K</c>.</summary>
public sealed partial class StorageOption : SqlNode
{
    /// <summary>The option name, such as <c>INITIAL</c>.</summary>
    public string Name { get; set; } = "";
    /// <summary>The option value, or null for an option without a value.</summary>
    public Expression? Value { get; set; }
}

/// <summary><c>LOGGING</c> or <c>NOLOGGING</c>.</summary>
public sealed partial class LoggingAttribute : PhysicalAttribute
{
    /// <summary>True for <c>LOGGING</c>; false for <c>NOLOGGING</c>.</summary>
    public bool Logging { get; set; }
}
/// <summary><c>CACHE</c> or <c>NOCACHE</c>.</summary>
public sealed partial class CacheAttribute : PhysicalAttribute
{
    /// <summary>True for <c>CACHE</c>; false for <c>NOCACHE</c>.</summary>
    public bool Cache { get; set; }
}
/// <summary><c>ENABLE ROW MOVEMENT</c> or <c>DISABLE ROW MOVEMENT</c>.</summary>
public sealed partial class RowMovementAttribute : PhysicalAttribute
{
    /// <summary>True for <c>ENABLE ROW MOVEMENT</c>.</summary>
    public bool Enabled { get; set; }
}

/// <summary><c>PARALLEL [n]</c> or <c>NOPARALLEL</c>.</summary>
public sealed partial class ParallelAttribute : PhysicalAttribute
{
    /// <summary>True for <c>NOPARALLEL</c>.</summary>
    public bool NoParallel { get; set; }
    /// <summary>The degree of parallelism, or null for <c>PARALLEL</c> without a number.</summary>
    public Expression? Degree { get; set; }
}

/// <summary><c>COMPRESS</c> or <c>NOCOMPRESS</c>.</summary>
public sealed partial class CompressAttribute : PhysicalAttribute
{
    /// <summary>True for <c>COMPRESS</c>; false for <c>NOCOMPRESS</c>.</summary>
    public bool Compress { get; set; }
    /// <summary>The prefix length in <c>COMPRESS (n)</c>, or null when absent.</summary>
    public Expression? PrefixLength { get; set; }
    /// <summary>BASIC | ADVANCED | FOR OLTP | FOR QUERY LOW|HIGH | FOR ARCHIVE LOW|HIGH ...</summary>
    public List<SyntaxItem> Method { get; set; } = [];
}

/// <summary>The organization of a table.</summary>
public enum TableOrganization
{
    /// <summary><c>ORGANIZATION HEAP</c>.</summary>
    Heap,
    /// <summary><c>ORGANIZATION INDEX</c>.</summary>
    Index,
    /// <summary><c>ORGANIZATION EXTERNAL</c>.</summary>
    External,
}

/// <summary><c>ORGANIZATION HEAP | INDEX | EXTERNAL</c>.</summary>
public sealed partial class OrganizationAttribute : PhysicalAttribute
{
    /// <summary>The organization chosen.</summary>
    public TableOrganization Organization { get; set; }
    /// <summary>ORGANIZATION EXTERNAL (...) / INDEX options.</summary>
    public List<SyntaxItem> Details { get; set; } = [];
}

/// <summary>The partitioning method of a table.</summary>
public enum PartitionMethod
{
    /// <summary><c>PARTITION BY RANGE</c>.</summary>
    Range,
    /// <summary><c>PARTITION BY LIST</c>.</summary>
    List,
    /// <summary><c>PARTITION BY HASH</c>.</summary>
    Hash,
    /// <summary><c>PARTITION BY REFERENCE</c>.</summary>
    Reference,
    /// <summary><c>PARTITION BY SYSTEM</c>.</summary>
    System,
}

/// <summary><c>PARTITION BY ... (partitions)</c> of a table.</summary>
public sealed partial class PartitionByAttribute : PhysicalAttribute
{
    /// <summary>The partitioning method.</summary>
    public PartitionMethod Method { get; set; }
    /// <summary>The partitioning columns; empty for <c>REFERENCE</c> and <c>SYSTEM</c>.</summary>
    public List<Identifier> Columns { get; set; } = [];
    /// <summary>The foreign key constraint of <c>PARTITION BY REFERENCE</c>, or null when absent.</summary>
    public Identifier? ReferenceConstraint { get; set; }
    /// <summary>The interval expression of <c>INTERVAL</c> partitioning, or null when absent.</summary>
    public Expression? Interval { get; set; }
    /// <summary>The count after <c>PARTITIONS</c> in a hash partitioning, or null when absent.</summary>
    public Expression? PartitionCount { get; set; }
    /// <summary>The partition definitions, in source order; empty when none are listed.</summary>
    public List<PartitionDefinition> Partitions { get; set; } = [];
    /// <summary>SUBPARTITION BY ... template (method + columns).</summary>
    public PartitionMethod? SubpartitionMethod { get; set; }
    /// <summary>The subpartitioning columns, in source order; empty when absent.</summary>
    public List<Identifier> SubpartitionColumns { get; set; } = [];
}

/// <summary>One partition definition, such as <c>PARTITION p1 VALUES LESS THAN (100)</c>.</summary>
public sealed partial class PartitionDefinition : SqlNode
{
    /// <summary>The partition name, or null when absent.</summary>
    public Identifier? Name { get; set; }
    /// <summary>True for <c>VALUES LESS THAN</c>.</summary>
    public bool LessThan { get; set; }
    /// <summary>True for <c>VALUES (DEFAULT)</c>.</summary>
    public bool ValuesDefault { get; set; }
    /// <summary>The values in <c>VALUES (...)</c>, in source order; empty when absent.</summary>
    public List<Expression> Values { get; set; } = [];
    /// <summary>The physical attributes of the partition, in source order; empty when absent.</summary>
    public List<PhysicalAttribute> Attributes { get; set; } = [];
    /// <summary>The subpartition definitions, in source order; empty when absent.</summary>
    public List<PartitionDefinition> Subpartitions { get; set; } = [];
}

/// <summary>Attribute syntax without a dedicated node, as a token tree (see COVERAGE.md).</summary>
public sealed partial class OtherAttribute : PhysicalAttribute
{
    /// <summary>The attribute's tokens as a token tree, for an attribute without a dedicated node.</summary>
    public List<SyntaxItem> Items { get; set; } = [];
}

// ---------- CREATE TABLE & friends ----------

/// <summary>What happens to the rows of a global temporary table at commit.</summary>
public enum OnCommitAction
{
    /// <summary><c>ON COMMIT PRESERVE ROWS</c>.</summary>
    PreserveRows,
    /// <summary><c>ON COMMIT DELETE ROWS</c>.</summary>
    DeleteRows,
    /// <summary><c>ON COMMIT DROP DEFINITION</c>.</summary>
    DropDefinition,
    /// <summary><c>ON COMMIT PRESERVE DEFINITION</c>.</summary>
    PreserveDefinition,
}

/// <summary><c>CREATE TABLE name (columns, constraints) [attributes] [AS query]</c>.</summary>
public sealed partial class CreateTableStatement : Statement
{
    /// <summary>The <c>SHARING</c> setting, or null when absent.</summary>
    public SharingKind? Sharing { get; set; }
    /// <summary>True when <c>OR REPLACE</c> is written.</summary>
    public bool OrReplace { get; set; }
    /// <summary>True when <c>IF NOT EXISTS</c> is written.</summary>
    public bool IfNotExists { get; set; }
    /// <summary>True for <c>GLOBAL TEMPORARY</c>.</summary>
    public bool GlobalTemporary { get; set; }
    /// <summary>True for <c>PRIVATE TEMPORARY</c>.</summary>
    public bool PrivateTemporary { get; set; }
    /// <summary>True for <c>DUPLICATED</c>.</summary>
    public bool Duplicated { get; set; }
    /// <summary>True for <c>IMMUTABLE</c>.</summary>
    public bool Immutable { get; set; }
    /// <summary>True for <c>BLOCKCHAIN</c>.</summary>
    public bool Blockchain { get; set; }
    /// <summary>The table name parts, schema first when qualified.</summary>
    public List<Identifier> Name { get; set; } = [];
    /// <summary>The column definitions, in source order; empty for a table created <c>AS</c> a query.</summary>
    public List<ColumnDefinition> Columns { get; set; } = [];
    /// <summary>The table-level constraints, in source order; empty when absent.</summary>
    public List<TableConstraint> Constraints { get; set; } = [];
    /// <summary>The object type name after <c>OF</c> in <c>CREATE TABLE t OF type</c>; empty when absent.</summary>
    public List<Identifier> OfType { get; set; } = [];
    /// <summary>The <c>ON COMMIT</c> behavior of a temporary table, or null when absent.</summary>
    public OnCommitAction? OnCommit { get; set; }
    /// <summary>The physical attributes, in source order; empty when absent.</summary>
    public List<PhysicalAttribute> Attributes { get; set; } = [];
    /// <summary>The query after <c>AS</c>, or null when the table is defined by its columns.</summary>
    public SelectStatement? AsQuery { get; set; }
}

/// <summary>How an identity column takes its values.</summary>
public enum IdentityMode
{
    /// <summary><c>GENERATED ALWAYS</c>.</summary>
    Always,
    /// <summary><c>GENERATED BY DEFAULT</c>.</summary>
    ByDefault,
}

/// <summary>The DML statements a <c>DEFAULT ON NULL</c> value applies to.</summary>
public enum OnNullScope
{
    /// <summary><c>ON NULL</c> applies to inserts only.</summary>
    InsertOnly,
    /// <summary><c>ON NULL</c> applies to inserts and updates.</summary>
    InsertAndUpdate,
}

/// <summary>The identity specification of a column, such as <c>GENERATED ALWAYS AS IDENTITY (START WITH 1)</c>.</summary>
public sealed partial class IdentityClause : SqlNode
{
    /// <summary>Whether values are always generated or generated by default.</summary>
    public IdentityMode Mode { get; set; }
    /// <summary>True when <c>ON NULL</c> is written with <c>BY DEFAULT</c>.</summary>
    public bool OnNull { get; set; }
    /// <summary>The sequence options in parentheses, in source order; empty when absent.</summary>
    public List<SequenceOption> Options { get; set; } = [];
}

/// <summary>The <c>ENCRYPT</c> clause of a column, such as <c>ENCRYPT USING 'AES256' NO SALT</c>.</summary>
public sealed partial class EncryptClause : SqlNode
{
    /// <summary>The algorithm after <c>USING</c>, or null when absent.</summary>
    public Expression? Algorithm { get; set; }
    /// <summary>The key name after <c>IDENTIFIED BY</c>, or null when absent.</summary>
    public Expression? IdentifiedBy { get; set; }
    /// <summary>True for <c>NO SALT</c>.</summary>
    public bool NoSalt { get; set; }
    /// <summary>True for <c>SALT</c>.</summary>
    public bool Salt { get; set; }
}

/// <summary>One column of a <c>CREATE TABLE</c> or <c>ALTER TABLE</c> statement, such as <c>id NUMBER(10) NOT NULL</c>.</summary>
public sealed partial class ColumnDefinition : SqlNode
{
    /// <summary>The column name.</summary>
    public required Identifier Name { get; set; }
    /// <summary>The data type, or null when the type is inferred from an <c>AS</c> query.</summary>
    public DataType? Type { get; set; }
    /// <summary>The <c>DEFAULT</c> expression, or null when absent.</summary>
    public Expression? Default { get; set; }
    /// <summary>True for <c>DEFAULT ON NULL</c>.</summary>
    public bool DefaultOnNull { get; set; }
    /// <summary>DEFAULT ON NULL FOR INSERT ONLY / FOR INSERT AND UPDATE; null when no FOR clause is given.</summary>
    public OnNullScope? DefaultOnNullFor { get; set; }
    /// <summary>The identity clause, or null when absent.</summary>
    public IdentityClause? Identity { get; set; }
    /// <summary>The expression of a virtual column, or null for a stored column.</summary>
    public Expression? VirtualExpression { get; set; }
    /// <summary>The inline constraints of the column, in source order; empty when absent.</summary>
    public List<TableConstraint> Constraints { get; set; } = [];
    /// <summary>True for <c>VISIBLE</c>, false for <c>INVISIBLE</c>, or null when neither is written.</summary>
    public bool? Visible { get; set; }
    /// <summary>The collation after <c>COLLATE</c>, or null when absent.</summary>
    public Identifier? Collation { get; set; }
    /// <summary>DOMAIN domain_name: the column takes its type and constraints from a domain (empty when none).</summary>
    public List<Identifier> Domain { get; set; } = [];
    /// <summary>The <c>ENCRYPT</c> clause, or null when absent.</summary>
    public EncryptClause? Encrypt { get; set; }
    /// <summary>True for <c>SORT</c>.</summary>
    public bool Sort { get; set; }
    /// <summary>ANNOTATIONS (...); Oracle requires it to be the last column property.</summary>
    public AnnotationsClause? Annotations { get; set; }
    /// <summary>Column properties without a dedicated node (LOB storage, substitutable, ...).</summary>
    public List<SyntaxItem> OtherProperties { get; set; } = [];
}

/// <summary>The kind of a constraint.</summary>
public enum ConstraintKind
{
    /// <summary><c>PRIMARY KEY</c>.</summary>
    PrimaryKey,
    /// <summary><c>UNIQUE</c>.</summary>
    Unique,
    /// <summary><c>FOREIGN KEY ... REFERENCES</c>.</summary>
    ForeignKey,
    /// <summary><c>CHECK (condition)</c>.</summary>
    Check,
    /// <summary><c>NOT NULL</c>.</summary>
    NotNull,
    /// <summary><c>NULL</c>, which allows nulls.</summary>
    Null,
}

/// <summary>The state clauses of a constraint, such as <c>DEFERRABLE INITIALLY DEFERRED ENABLE VALIDATE</c>.</summary>
public sealed partial class ConstraintState : SqlNode
{
    /// <summary>True for <c>DEFERRABLE</c>, false for <c>NOT DEFERRABLE</c>, or null when neither is written.</summary>
    public bool? Deferrable { get; set; }
    /// <summary>True for <c>INITIALLY DEFERRED</c>, false for <c>INITIALLY IMMEDIATE</c>, or null when neither is written.</summary>
    public bool? InitiallyDeferred { get; set; }
    /// <summary>True for <c>ENABLE</c>, false for <c>DISABLE</c>, or null when neither is written.</summary>
    public bool? Enable { get; set; }
    /// <summary>True for <c>VALIDATE</c>, false for <c>NOVALIDATE</c>, or null when neither is written.</summary>
    public bool? Validate { get; set; }
    /// <summary>True for <c>RELY</c>, false for <c>NORELY</c>, or null when neither is written.</summary>
    public bool? Rely { get; set; }
    /// <summary>The <c>USING INDEX</c> clause, or null when absent.</summary>
    public UsingIndexClause? UsingIndex { get; set; }
    /// <summary>The table named after <c>EXCEPTIONS INTO</c>; empty when absent.</summary>
    public List<Identifier> ExceptionsInto { get; set; } = [];
}

/// <summary>The <c>USING INDEX</c> clause of a constraint, which names or creates the index that enforces it.</summary>
public sealed partial class UsingIndexClause : SqlNode
{
    /// <summary>The existing index name, or empty when the clause creates an index.</summary>
    public List<Identifier> IndexName { get; set; } = [];
    /// <summary>The index created by the clause, or null when it names an existing index.</summary>
    public CreateIndexStatement? Create { get; set; }
    /// <summary>The physical attributes of the index; empty when absent.</summary>
    public List<PhysicalAttribute> Attributes { get; set; } = [];
}

/// <summary>The action of a foreign key on delete.</summary>
public enum ReferentialAction
{
    /// <summary><c>ON DELETE CASCADE</c>.</summary>
    Cascade,
    /// <summary><c>ON DELETE SET NULL</c>.</summary>
    SetNull,
}

/// <summary>A table-level or inline constraint, such as <c>CONSTRAINT pk PRIMARY KEY (id)</c>.</summary>
public sealed partial class TableConstraint : SqlNode
{
    /// <summary>The constraint name, or null when unnamed.</summary>
    public Identifier? Name { get; set; }
    /// <summary>Null only for MODIFY CONSTRAINT name ... which identifies a constraint by name.</summary>
    public ConstraintKind? Kind { get; set; }
    /// <summary>The constrained columns, in source order.</summary>
    public List<Identifier> Columns { get; set; } = [];
    /// <summary>The condition of a <c>CHECK</c> constraint, or null for other kinds.</summary>
    public Expression? Check { get; set; }
    /// <summary>The referenced table name parts of a foreign key; empty for other kinds.</summary>
    public List<Identifier> RefTable { get; set; } = [];
    /// <summary>The referenced columns of a foreign key; empty when omitted.</summary>
    public List<Identifier> RefColumns { get; set; } = [];
    /// <summary>The <c>ON DELETE</c> action of a foreign key, or null when absent.</summary>
    public ReferentialAction? OnDelete { get; set; }
    /// <summary>The state clauses of the constraint, or null when absent.</summary>
    public ConstraintState? State { get; set; }
}

/// <summary>ANNOTATIONS (...) among the physical attributes of CREATE TABLE, CREATE INDEX, CREATE VIEW and CREATE MATERIALIZED VIEW.</summary>
public sealed partial class AnnotationsAttribute : PhysicalAttribute
{
    /// <summary>The annotations clause.</summary>
    public required AnnotationsClause Clause { get; set; }
}

/// <summary>A column (or view column) with its own ANNOTATIONS clause: name [ANNOTATIONS (...)].</summary>
public sealed partial class ViewColumn : SqlNode
{
    /// <summary>The view column name.</summary>
    public required Identifier Name { get; set; }
    /// <summary>The annotations on the column, or null when absent.</summary>
    public AnnotationsClause? Annotations { get; set; }
}

/// <summary>ALTER VIEW | ALTER MATERIALIZED VIEW | ALTER INDEX name ANNOTATIONS (...) or ... MODIFY (column ANNOTATIONS (...), ...).</summary>
public sealed partial class AlterAnnotationsStatement : Statement
{
    /// <summary>The kind of object whose annotations change.</summary>
    public ObjectKind Kind { get; set; }
    /// <summary>The object name parts, schema first when qualified.</summary>
    public List<Identifier> Name { get; set; } = [];
    /// <summary>The table-level annotations clause, or null when absent.</summary>
    public AnnotationsClause? Annotations { get; set; }
    /// <summary>The per-column annotations, in source order; empty when absent.</summary>
    public List<ViewColumn> ColumnAnnotations { get; set; } = [];
}

/// <summary><c>CREATE [UNIQUE|BITMAP] INDEX name ON table (columns)</c>.</summary>
public sealed partial class CreateIndexStatement : Statement
{
    /// <summary>True for <c>UNIQUE</c>.</summary>
    public bool Unique { get; set; }
    /// <summary>True for <c>BITMAP</c>.</summary>
    public bool Bitmap { get; set; }
    /// <summary>True when <c>IF NOT EXISTS</c> is written.</summary>
    public bool IfNotExists { get; set; }
    /// <summary>The index name parts, schema first when qualified.</summary>
    public List<Identifier> Name { get; set; } = [];
    /// <summary>The indexed table name parts, schema first when qualified.</summary>
    public List<Identifier> Table { get; set; } = [];
    /// <summary>ON CLUSTER name (instead of a table).</summary>
    public List<Identifier> Cluster { get; set; } = [];
    /// <summary>The alias of the indexed table, or null when absent.</summary>
    public Identifier? TableAlias { get; set; }
    /// <summary>The indexed columns and expressions, in source order.</summary>
    public List<IndexColumn> Columns { get; set; } = [];
    /// <summary>The indextype name parts after <c>INDEXTYPE IS</c>; empty for a regular index.</summary>
    public List<Identifier> Indextype { get; set; } = [];
    /// <summary>The <c>PARAMETERS</c> string of a domain index, or null when absent.</summary>
    public Expression? IndextypeParameters { get; set; }
    /// <summary>True for <c>VISIBLE</c>, false for <c>INVISIBLE</c>, or null when neither is written.</summary>
    public bool? Visible { get; set; }
    /// <summary>True when <c>ONLINE</c> is written.</summary>
    public bool Online { get; set; }
    /// <summary>True for <c>REVERSE</c>.</summary>
    public bool Reverse { get; set; }
    /// <summary>True for <c>LOCAL</c>, a local partitioned index.</summary>
    public bool Local { get; set; }
    /// <summary>True for <c>GLOBAL</c>, a global partitioned index.</summary>
    public bool Global { get; set; }
    /// <summary>The physical attributes, in source order; empty when absent.</summary>
    public List<PhysicalAttribute> Attributes { get; set; } = [];
}

/// <summary>One column or expression of an index, such as <c>last_name DESC</c>.</summary>
public sealed partial class IndexColumn : SqlNode
{
    /// <summary>The column name or the indexed expression.</summary>
    public required Expression Expression { get; set; }
    /// <summary>True for <c>DESC</c>.</summary>
    public bool Descending { get; set; }
}

/// <summary>The rights a view runs with, from <c>BEQUEATH</c>.</summary>
public enum ViewBequeath
{
    /// <summary><c>BEQUEATH CURRENT_USER</c>.</summary>
    CurrentUser,
    /// <summary><c>BEQUEATH DEFINER</c>.</summary>
    Definer,
}
/// <summary>The refresh method of a materialized view.</summary>
public enum RefreshMethod
{
    /// <summary><c>REFRESH FAST</c>.</summary>
    Fast,
    /// <summary><c>REFRESH COMPLETE</c>.</summary>
    Complete,
    /// <summary><c>REFRESH FORCE</c>.</summary>
    Force,
    /// <summary><c>NEVER REFRESH</c>.</summary>
    Never,
}
/// <summary>When a materialized view is refreshed.</summary>
public enum RefreshMode
{
    /// <summary><c>ON DEMAND</c>.</summary>
    OnDemand,
    /// <summary><c>ON COMMIT</c>.</summary>
    OnCommit,
    /// <summary><c>ON STATEMENT</c>.</summary>
    OnStatement,
}

/// <summary>The <c>REFRESH</c> clause of a materialized view, such as <c>REFRESH FAST ON COMMIT</c>.</summary>
public sealed partial class RefreshClause : SqlNode
{
    /// <summary>The refresh method, or null when absent.</summary>
    public RefreshMethod? Method { get; set; }
    /// <summary>When the refresh runs, or null when absent.</summary>
    public RefreshMode? Mode { get; set; }
    /// <summary>The <c>START WITH</c> expression of a scheduled refresh, or null when absent.</summary>
    public Expression? StartWith { get; set; }
    /// <summary>The <c>NEXT</c> expression of a scheduled refresh, or null when absent.</summary>
    public Expression? Next { get; set; }
    /// <summary>True for <c>WITH PRIMARY KEY</c>.</summary>
    public bool WithPrimaryKey { get; set; }
    /// <summary>True for <c>WITH ROWID</c>.</summary>
    public bool WithRowId { get; set; }
}

/// <summary><c>CREATE [OR REPLACE] [MATERIALIZED] VIEW name AS query</c>.</summary>
public sealed partial class CreateViewStatement : Statement
{
    /// <summary>The <c>SHARING</c> setting, or null when absent.</summary>
    public SharingKind? Sharing { get; set; }
    /// <summary>True when <c>OR REPLACE</c> is written.</summary>
    public bool OrReplace { get; set; }
    /// <summary>True when <c>IF NOT EXISTS</c> is written.</summary>
    public bool IfNotExists { get; set; }
    /// <summary>True for <c>CREATE MATERIALIZED VIEW</c>.</summary>
    public bool Materialized { get; set; }
    /// <summary>True for <c>FORCE</c>.</summary>
    public bool Force { get; set; }
    /// <summary>True for <c>NOFORCE</c>.</summary>
    public bool NoForce { get; set; }
    /// <summary>True for <c>EDITIONING</c>.</summary>
    public bool Editioning { get; set; }
    /// <summary>The view name parts, schema first when qualified.</summary>
    public List<Identifier> Name { get; set; } = [];
    /// <summary>The view column list in parentheses, in source order; empty when absent.</summary>
    public List<ViewColumn> Columns { get; set; } = [];
    /// <summary>The <c>BEQUEATH</c> setting, or null when absent.</summary>
    public ViewBequeath? Bequeath { get; set; }
    // materialized view options
    /// <summary>True for <c>BUILD IMMEDIATE</c>, false for <c>BUILD DEFERRED</c>, or null when neither is written.</summary>
    public bool? BuildImmediate { get; set; }
    /// <summary>The <c>REFRESH</c> clause of a materialized view, or null when absent.</summary>
    public RefreshClause? Refresh { get; set; }
    /// <summary>True for <c>ENABLE QUERY REWRITE</c>, false for <c>DISABLE QUERY REWRITE</c>, or null when neither is written.</summary>
    public bool? QueryRewrite { get; set; }
    /// <summary>The physical attributes, in source order; empty when absent.</summary>
    public List<PhysicalAttribute> Attributes { get; set; } = [];
    /// <summary>The query after <c>AS</c>.</summary>
    public required SelectStatement Query { get; set; }
    /// <summary>True for <c>WITH READ ONLY</c>.</summary>
    public bool ReadOnly { get; set; }
    /// <summary>True for <c>WITH CHECK OPTION</c>.</summary>
    public bool CheckOption { get; set; }
    /// <summary>The constraint name after <c>WITH CHECK OPTION CONSTRAINT</c>, or null when absent.</summary>
    public Identifier? CheckOptionConstraint { get; set; }
}

/// <summary>CREATE [USECASE] DOMAIN [IF NOT EXISTS] d AS type [DEFAULT ...] [constraints] [COLLATE ...] [DISPLAY ...] [ORDER ...] [ANNOTATIONS (...)].</summary>
public sealed partial class CreateDomainStatement : Statement
{
    /// <summary>True when <c>OR REPLACE</c> is written.</summary>
    public bool OrReplace { get; set; }
    /// <summary>True when <c>USECASE</c> is written.</summary>
    public bool UseCase { get; set; }
    /// <summary>True when <c>IF NOT EXISTS</c> is written.</summary>
    public bool IfNotExists { get; set; }
    /// <summary>The domain name parts, schema first when qualified.</summary>
    public List<Identifier> Name { get; set; } = [];
    /// <summary>The domain's data type, or null when absent.</summary>
    public DataType? Type { get; set; }
    /// <summary>The <c>DEFAULT</c> expression, or null when absent.</summary>
    public Expression? Default { get; set; }
    /// <summary>The domain constraints, in source order; empty when absent.</summary>
    public List<TableConstraint> Constraints { get; set; } = [];
    /// <summary>The collation after <c>COLLATE</c>, or null when absent.</summary>
    public Identifier? Collation { get; set; }
    /// <summary>The <c>DISPLAY</c> expression, or null when absent.</summary>
    public Expression? Display { get; set; }
    /// <summary>The <c>ORDER</c> expression, or null when absent.</summary>
    public Expression? Order { get; set; }
    /// <summary>The annotations clause, or null when absent.</summary>
    public AnnotationsClause? Annotations { get; set; }
}

/// <summary>The kind of an <c>ALTER DOMAIN</c> action.</summary>
public enum AlterDomainActionKind
{
    /// <summary><c>ADD DISPLAY</c>.</summary>
    AddDisplay,
    /// <summary><c>DROP DISPLAY</c>.</summary>
    DropDisplay,
    /// <summary><c>ADD ORDER</c>.</summary>
    AddOrder,
    /// <summary><c>DROP ORDER</c>.</summary>
    DropOrder,
}

/// <summary>ALTER DOMAIN d ADD|DROP DISPLAY|ORDER [expr]. Any other action is kept in OtherClauses.</summary>
public sealed partial class AlterDomainStatement : Statement
{
    /// <summary>The domain name parts, schema first when qualified.</summary>
    public List<Identifier> Name { get; set; } = [];
    /// <summary>The action applied to the domain, or null when absent.</summary>
    public AlterDomainAction? Action { get; set; }
    /// <summary>ALTER DOMAIN d ANNOTATIONS (...).</summary>
    public AnnotationsClause? Annotations { get; set; }
    /// <summary>Actions not modelled above (RENAME, ADD CONSTRAINT, ...), as a token tree.</summary>
    public List<SyntaxItem> OtherClauses { get; set; } = [];
}

/// <summary>ANNOTATIONS ( item [, item]... ). In CREATE an item is name ['value'] or ADD [IF NOT EXISTS] name ['value']; in ALTER also DROP [IF EXISTS] name.</summary>
public sealed partial class AnnotationsClause : SqlNode
{
    /// <summary>The annotation items, in source order.</summary>
    public List<AnnotationItem> Items { get; set; } = [];
}

/// <summary>Whether an annotation item adds or drops an annotation.</summary>
public enum AnnotationAction
{
    /// <summary><c>ADD</c>.</summary>
    Add,
    /// <summary><c>DROP</c>.</summary>
    Drop,
}

/// <summary>One item of an <c>ANNOTATIONS (...)</c> clause, such as <c>ADD IF NOT EXISTS 'label' 'text'</c>.</summary>
public sealed partial class AnnotationItem : SqlNode
{
    /// <summary>Null for a plain name ['value'] item.</summary>
    public AnnotationAction? Action { get; set; }
    /// <summary>True when <c>IF NOT EXISTS</c> is written with <c>ADD</c>.</summary>
    public bool IfNotExists { get; set; }
    /// <summary>True when <c>IF EXISTS</c> is written with <c>DROP</c>.</summary>
    public bool IfExists { get; set; }
    /// <summary>The annotation name.</summary>
    public required Identifier Name { get; set; }
    /// <summary>The annotation value, or null when absent.</summary>
    public Expression? Value { get; set; }
}

/// <summary>The action of an <c>ALTER DOMAIN</c> statement, such as <c>ADD DISPLAY expr</c>.</summary>
public sealed partial class AlterDomainAction : SqlNode
{
    /// <summary>The kind of action.</summary>
    public AlterDomainActionKind Kind { get; set; }
    /// <summary>The DISPLAY or ORDER expression for the Add forms.</summary>
    public Expression? Value { get; set; }
}

/// <summary>CREATE [OR REPLACE] PROPERTY GRAPH g VERTEX TABLES (...) [EDGE TABLES (...)] (SQL/PGQ).</summary>
public sealed partial class CreatePropertyGraphStatement : Statement
{
    /// <summary>True when <c>OR REPLACE</c> is written.</summary>
    public bool OrReplace { get; set; }
    /// <summary>The property graph name parts, schema first when qualified.</summary>
    public List<Identifier> Name { get; set; } = [];
    /// <summary>The vertex table definitions, in source order.</summary>
    public List<GraphVertexTable> VertexTables { get; set; } = [];
    /// <summary>The edge table definitions, in source order; empty when absent.</summary>
    public List<GraphEdgeTable> EdgeTables { get; set; } = [];
}

/// <summary>A table used as vertices or edges: table [AS alias] [KEY (cols)] [LABEL ... [PROPERTIES ...]]...</summary>
public abstract partial class GraphElementTable : SqlNode
{
    /// <summary>The table name parts.</summary>
    public List<Identifier> Table { get; set; } = [];
    /// <summary>The alias after <c>AS</c>, or null when absent.</summary>
    public Identifier? Alias { get; set; }
    /// <summary>The key columns in parentheses after <c>KEY</c>; empty when absent.</summary>
    public List<Identifier> Key { get; set; } = [];
    /// <summary>PROPERTIES written without a LABEL: the properties of the default (table-name) label.</summary>
    public GraphPropertiesClause? Properties { get; set; }
    /// <summary>The labels of the element, in source order; empty when absent.</summary>
    public List<GraphElementLabel> Labels { get; set; } = [];
}

/// <summary>A vertex table of a property graph, such as <c>VERTEX TABLES (emp KEY (id) LABEL person)</c>.</summary>
public sealed partial class GraphVertexTable : GraphElementTable
{
}

/// <summary>An edge table of a property graph, with its <c>SOURCE</c> and <c>DESTINATION</c> endpoints.</summary>
public sealed partial class GraphEdgeTable : GraphElementTable
{
    /// <summary>The source endpoint, from <c>SOURCE</c>.</summary>
    public required GraphEdgeEndpoint Source { get; set; }
    /// <summary>The destination endpoint, from <c>DESTINATION</c>.</summary>
    public required GraphEdgeEndpoint Destination { get; set; }
}

/// <summary>SOURCE/DESTINATION of an edge table: [KEY (edge cols) REFERENCES] vertex_table [(vertex cols)].</summary>
public sealed partial class GraphEdgeEndpoint : SqlNode
{
    /// <summary>The edge columns named in <c>KEY (...) REFERENCES</c>; empty when only the vertex table is named.</summary>
    public List<Identifier> Keys { get; set; } = [];
    /// <summary>The vertex table name parts.</summary>
    public List<Identifier> Table { get; set; } = [];
    /// <summary>The vertex columns named in parentheses; empty when absent.</summary>
    public List<Identifier> Columns { get; set; } = [];
}

/// <summary>LABEL name with an optional PROPERTIES clause.</summary>
public sealed partial class GraphElementLabel : SqlNode
{
    /// <summary>The label name.</summary>
    public required Identifier Name { get; set; }
    /// <summary>The <c>PROPERTIES</c> clause of this label, or null when absent.</summary>
    public GraphPropertiesClause? Properties { get; set; }
}

/// <summary>Which properties an element exposes.</summary>
public enum GraphPropertiesKind
{
    /// <summary><c>PROPERTIES (columns)</c>: the listed columns.</summary>
    Columns,
    /// <summary><c>PROPERTIES ARE ALL COLUMNS</c>.</summary>
    AllColumns,
    /// <summary><c>NO PROPERTIES</c>.</summary>
    None,
}

/// <summary>PROPERTIES (cols) | PROPERTIES ARE ALL COLUMNS [EXCEPT (cols)] | NO PROPERTIES.</summary>
public sealed partial class GraphPropertiesClause : SqlNode
{
    /// <summary>Whether the clause lists columns, all columns or none.</summary>
    public GraphPropertiesKind Kind { get; set; }
    /// <summary>The properties listed, in source order; empty for the other kinds.</summary>
    public List<GraphProperty> Columns { get; set; } = [];
    /// <summary>The columns excluded after <c>EXCEPT</c>; empty when absent.</summary>
    public List<Identifier> Except { get; set; } = [];
}

/// <summary>One property: column [AS name].</summary>
public sealed partial class GraphProperty : SqlNode
{
    /// <summary>The column name.</summary>
    public required Identifier Property { get; set; }
    /// <summary>The property name after <c>AS</c>, or null when absent.</summary>
    public Identifier? Alias { get; set; }
}

/// <summary><c>CREATE SEQUENCE [IF NOT EXISTS] name [options]</c>.</summary>
public sealed partial class CreateSequenceStatement : Statement
{
    /// <summary>The <c>SHARING</c> setting, or null when absent.</summary>
    public SharingKind? Sharing { get; set; }
    /// <summary>True when <c>IF NOT EXISTS</c> is written.</summary>
    public bool IfNotExists { get; set; }
    /// <summary>The sequence name parts, schema first when qualified.</summary>
    public List<Identifier> Name { get; set; } = [];
    /// <summary>The sequence options, in source order; empty when absent.</summary>
    public List<SequenceOption> Options { get; set; } = [];
}

/// <summary>One option of a sequence, such as <c>INCREMENT BY 2</c> or <c>NOCACHE</c>.</summary>
public sealed partial class SequenceOption : SqlNode
{
    /// <summary>Keyword phrase: START WITH, INCREMENT BY, MAXVALUE, NOMAXVALUE, CACHE, CYCLE, ORDER, KEEP, SCALE, ...</summary>
    public string Name { get; set; } = "";
    /// <summary>The option value, or null for an option without a value.</summary>
    public Expression? Value { get; set; }
}

/// <summary><c>CREATE [OR REPLACE] [PUBLIC] SYNONYM name FOR target</c>.</summary>
public sealed partial class CreateSynonymStatement : Statement
{
    /// <summary>The <c>SHARING</c> setting, or null when absent.</summary>
    public SharingKind? Sharing { get; set; }
    /// <summary>True when <c>OR REPLACE</c> is written.</summary>
    public bool OrReplace { get; set; }
    /// <summary>True when <c>IF NOT EXISTS</c> is written.</summary>
    public bool IfNotExists { get; set; }
    /// <summary>True for <c>PUBLIC SYNONYM</c>.</summary>
    public bool Public { get; set; }
    /// <summary>The synonym name parts, schema first when qualified.</summary>
    public List<Identifier> Name { get; set; } = [];
    /// <summary>The object the synonym refers to, schema first when qualified.</summary>
    public List<Identifier> Target { get; set; } = [];
    /// <summary>The database link name after <c>@</c>; empty when absent.</summary>
    public List<Identifier> DbLink { get; set; } = [];
}

/// <summary><c>ALTER TABLE name action [, action]...</c>.</summary>
public sealed partial class AlterTableStatement : Statement
{
    /// <summary>The table name parts, schema first when qualified.</summary>
    public List<Identifier> Name { get; set; } = [];
    /// <summary>True when <c>IF EXISTS</c> is written.</summary>
    public bool IfExists { get; set; }
    /// <summary>The actions, in source order.</summary>
    public List<AlterTableAction> Actions { get; set; } = [];
}

/// <summary>The kind of an <c>ALTER TABLE</c> action.</summary>
public enum AlterTableActionKind
{
    /// <summary><c>ADD (columns)</c>.</summary>
    AddColumns,
    /// <summary><c>ADD CONSTRAINT</c>.</summary>
    AddConstraint,
    /// <summary><c>MODIFY (columns)</c>.</summary>
    ModifyColumns,
    /// <summary><c>DROP (columns)</c> or <c>DROP COLUMN</c>.</summary>
    DropColumns,
    /// <summary><c>DROP CONSTRAINT</c>.</summary>
    DropConstraint,
    /// <summary><c>DROP PRIMARY KEY</c>.</summary>
    DropPrimaryKey,
    /// <summary><c>DROP UNIQUE</c>.</summary>
    DropUnique,
    /// <summary><c>RENAME COLUMN</c>.</summary>
    RenameColumn,
    /// <summary><c>RENAME CONSTRAINT</c>.</summary>
    RenameConstraint,
    /// <summary><c>RENAME TO</c>.</summary>
    RenameTo,
    /// <summary><c>MODIFY CONSTRAINT</c>.</summary>
    ModifyConstraint,
    /// <summary><c>SET UNUSED</c>.</summary>
    SetUnused,
    /// <summary>A change of physical attributes.</summary>
    Attributes,
    /// <summary><c>ADD PARTITION</c>.</summary>
    AddPartition,
    /// <summary><c>DROP PARTITION</c>.</summary>
    DropPartition,
    /// <summary><c>TRUNCATE PARTITION</c>.</summary>
    TruncatePartition,
    /// <summary><c>SPLIT PARTITION</c>.</summary>
    SplitPartition,
    /// <summary><c>MERGE PARTITIONS</c>.</summary>
    MergePartitions,
    /// <summary><c>EXCHANGE PARTITION</c>.</summary>
    ExchangePartition,
    /// <summary><c>MOVE PARTITION</c>.</summary>
    MovePartition,
    /// <summary><c>RENAME PARTITION</c>.</summary>
    RenamePartition,
    /// <summary><c>MODIFY PARTITION</c> that changes its values.</summary>
    ModifyPartitionValues,
    /// <summary><c>SET INTERVAL</c>.</summary>
    SetInterval,
    /// <summary>An <c>ANNOTATIONS</c> change.</summary>
    Annotations,
    /// <summary>An action without a dedicated value; kept as written in the token tree.</summary>
    Other,
}

/// <summary>One action of an <c>ALTER TABLE</c> statement, such as <c>ADD (c NUMBER)</c>.</summary>
public sealed partial class AlterTableAction : SqlNode
{
    /// <summary>Which action this is.</summary>
    public AlterTableActionKind Kind { get; set; }
    /// <summary>The column definitions of the action; empty when absent.</summary>
    public List<ColumnDefinition> Columns { get; set; } = [];
    /// <summary>The constraints of the action; empty when absent.</summary>
    public List<TableConstraint> Constraints { get; set; } = [];
    /// <summary>The names the action refers to, such as the columns dropped; empty when absent.</summary>
    public List<Identifier> Names { get; set; } = [];
    /// <summary>The new name of a rename action, or null when absent.</summary>
    public Identifier? NewName { get; set; }
    /// <summary>True for <c>CASCADE</c>.</summary>
    public bool Cascade { get; set; }
    /// <summary>True for <c>ONLINE</c>.</summary>
    public bool Online { get; set; }
    /// <summary>The physical attributes of the action; empty when absent.</summary>
    public List<PhysicalAttribute> Attributes { get; set; } = [];
    /// <summary>Partition or subpartition targets of partition maintenance (DROP, TRUNCATE, SPLIT, MERGE, EXCHANGE, MOVE, RENAME, MODIFY).</summary>
    public List<PartitionReference> PartitionRefs { get; set; } = [];
    /// <summary>ALTER TABLE t ANNOTATIONS (...).</summary>
    public AnnotationsClause? Annotations { get; set; }
    /// <summary>Partitions defined by ADD PARTITION, SPLIT ... INTO (...) or MERGE ... INTO PARTITION.</summary>
    public List<PartitionDefinition> Partitions { get; set; } = [];
    /// <summary>Boundary values of SPLIT ... AT/VALUES (...), MODIFY PARTITION ... ADD/DROP VALUES (...), or SET INTERVAL (...).</summary>
    public List<Expression> Values { get; set; } = [];
    /// <summary>SPLIT PARTITION ... VALUES (...) rather than AT (...).</summary>
    public bool SplitByValues { get; set; }
    /// <summary>MODIFY PARTITION ... ADD VALUES (true) or DROP VALUES (false).</summary>
    public bool? AddValues { get; set; }
    /// <summary>SET INTERVAL (expr); null for the SET INTERVAL () form.</summary>
    public Expression? Interval { get; set; }
    /// <summary>True when an <c>UPDATE [GLOBAL] INDEXES</c> clause is written on partition maintenance.</summary>
    public bool UpdateIndexes { get; set; }
    /// <summary>True for the <c>GLOBAL</c> form, <c>UPDATE GLOBAL INDEXES</c>; meaningful only with <see cref="UpdateIndexes"/>.</summary>
    public bool GlobalIndexes { get; set; }
    /// <summary>EXCHANGE PARTITION ... INCLUDING (true) or EXCLUDING (false) INDEXES.</summary>
    public bool? IncludingIndexes { get; set; }
    /// <summary>EXCHANGE PARTITION ... WITH (true) or WITHOUT (false) VALIDATION.</summary>
    public bool? WithValidation { get; set; }
    /// <summary>TRUNCATE PARTITION ... DROP STORAGE (DropStorage) or REUSE STORAGE (ReuseStorage).</summary>
    public TruncateStorage? Storage { get; set; }
    /// <summary>Partition/other maintenance actions without a dedicated node, as a token tree.</summary>
    public List<SyntaxItem> Syntax { get; set; } = [];
}
