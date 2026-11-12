namespace OracleSqlParser;

// ---------- Transaction control & session ----------

public enum WaitMode { Wait, NoWait }
public enum CommitWriteMode { Immediate, Batch }

public sealed class CommitWrite : SqlNode
{
    public WaitMode? Wait { get; set; }
    public CommitWriteMode? Mode { get; set; }
}

public sealed class CommitStatement : Statement
{
    public bool Work { get; set; }
    public Expression? Comment { get; set; }
    public CommitWrite? Write { get; set; }
    /// <summary>COMMIT FORCE 'tx_id' [, scn].</summary>
    public Expression? Force { get; set; }
    public Expression? ForceScn { get; set; }
}

public sealed class RollbackStatement : Statement
{
    public bool Work { get; set; }
    public Identifier? ToSavepoint { get; set; }
    public Expression? Force { get; set; }
}

public sealed class SavepointStatement : Statement
{
    public Identifier Name { get; set; } = null!;
}

public enum TransactionAccess { ReadOnly, ReadWrite }
public enum IsolationLevel { Serializable, ReadCommitted }

public sealed class SetTransactionStatement : Statement
{
    public TransactionAccess? Access { get; set; }
    public IsolationLevel? Isolation { get; set; }
    public Identifier? RollbackSegment { get; set; }
    public Expression? Name { get; set; }
}

public sealed class SetConstraintsStatement : Statement
{
    public bool All { get; set; }
    public List<List<Identifier>> Constraints { get; set; } = [];
    public bool Deferred { get; set; }
}

public sealed class RoleSpec : SqlNode
{
    public Identifier Name { get; set; } = null!;
    public Expression? IdentifiedBy { get; set; }
}

public sealed class SetRoleStatement : Statement
{
    public bool None { get; set; }
    public bool All { get; set; }
    public List<Identifier> Except { get; set; } = [];
    public List<RoleSpec> Roles { get; set; } = [];
}

public enum AlterSessionKind { Set, Enable, Disable, Force, CloseDatabaseLink, Advise, SyncWithPrimary }
public enum SessionFeature { CommitInProcedure, Guard, ParallelDml, ParallelDdl, ParallelQuery, Resumable, Other }
public enum AdviseMode { Commit, Rollback, Nothing }

public sealed class AlterSessionStatement : Statement
{
    public AlterSessionKind Kind { get; set; }
    public List<SessionSetting> Settings { get; set; } = [];
    public SessionFeature? Feature { get; set; }
    public Expression? Parallel { get; set; }
    public Expression? Timeout { get; set; }
    public Expression? ResumableName { get; set; }
    public Identifier? DatabaseLink { get; set; }
    public AdviseMode? Advise { get; set; }
    /// <summary>Feature words/arguments for ENABLE/DISABLE forms not modeled individually (listed in COVERAGE.md).</summary>
    public List<SyntaxItem> FeatureSyntax { get; set; } = [];
}

public sealed class SessionSetting : SqlNode
{
    public List<Identifier> Name { get; set; } = [];
    public Expression Value { get; set; } = null!;
}

public sealed class CallStatement : Statement
{
    public Expression Routine { get; set; } = null!;
    public Expression? Into { get; set; }
}

public sealed class ExplainPlanStatement : Statement
{
    public Expression? StatementId { get; set; }
    public List<Identifier> IntoTable { get; set; } = [];
    public Statement Statement { get; set; } = null!;
}

public enum LockMode { RowShare, RowExclusive, ShareUpdate, Share, ShareRowExclusive, Exclusive }

public sealed class LockTableStatement : Statement
{
    public List<TableReference> Tables { get; set; } = [];
    public LockMode Mode { get; set; }
    public bool NoWait { get; set; }
    public Expression? Wait { get; set; }
}

public enum TruncateTarget { Table, Cluster }
public enum MaterializedViewLogAction { Preserve, Purge }
public enum TruncateStorage { DropStorage, DropAllStorage, ReuseStorage }

public sealed class TruncateStatement : Statement
{
    public TruncateTarget Target { get; set; }
    public List<Identifier> Name { get; set; } = [];
    public MaterializedViewLogAction? MaterializedViewLog { get; set; }
    public TruncateStorage? Storage { get; set; }
    public bool Cascade { get; set; }
}

public enum CommentTarget { Table, Column, MaterializedView, Operator, Indextype, MiningModel, Edition }

public sealed class CommentStatement : Statement
{
    public CommentTarget Target { get; set; }
    public List<Identifier> Name { get; set; } = [];
    public string Text { get; set; } = "";
}

public sealed class GrantPrivilege : SqlNode
{
    /// <summary>Privilege or role name as a keyword phrase, e.g. SELECT, CREATE ANY TABLE, DBA.</summary>
    public string Name { get; set; } = "";
    public List<Identifier> Columns { get; set; } = [];
}

public sealed class Grantee : SqlNode
{
    public Identifier? Name { get; set; }
    public bool Public { get; set; }
}

public enum GrantObjectKind { Directory, Edition, MiningModel, JavaSource, JavaResource, SqlTranslationProfile, User }
public enum GrantContainer { Current, All }

public sealed class GrantStatement : Statement
{
    public bool Revoke { get; set; }
    public bool AllPrivileges { get; set; }
    public List<GrantPrivilege> Privileges { get; set; } = [];
    public GrantObjectKind? ObjectKind { get; set; }
    public List<Identifier> Object { get; set; } = [];
    public List<Grantee> Grantees { get; set; } = [];
    public bool WithGrantOption { get; set; }
    public bool WithAdminOption { get; set; }
    public bool WithHierarchyOption { get; set; }
    public bool CascadeConstraints { get; set; }
    public bool Force { get; set; }
    public GrantContainer? Container { get; set; }
}

public sealed class RenameStatement : Statement
{
    public List<Identifier> From { get; set; } = [];
    public List<Identifier> To { get; set; } = [];
}

public enum PurgeTarget { Recyclebin, DbaRecyclebin, Table, Index, Tablespace, TablespaceSet }

public sealed class PurgeStatement : Statement
{
    public PurgeTarget Target { get; set; }
    public List<Identifier> Name { get; set; } = [];
    public Identifier? User { get; set; }
}

public enum FlashbackTarget { Scn, Timestamp, RestorePoint, BeforeDrop }

public sealed class FlashbackTableStatement : Statement
{
    public List<List<Identifier>> Tables { get; set; } = [];
    public FlashbackTarget Target { get; set; }
    public Expression? Point { get; set; }
    public Identifier? RenameTo { get; set; }
    public bool Before { get; set; }
    public bool EnableTriggers { get; set; }
}

// ---------- SQL*Plus & script-level ----------

/// <summary>EXEC[UTE] procedure(args) (SQL*Plus shorthand for an anonymous block).</summary>
public sealed class ExecStatement : Statement
{
    public Expression Call { get; set; } = null!;
}

/// <summary>A SQL*Plus client command (SET, SPOOL, DEFINE, @script, PROMPT, REM, VARIABLE, WHENEVER, ...). Never sent to the server.</summary>
public sealed class SqlPlusCommand : Statement
{
    public string Command { get; set; } = "";
    /// <summary>Remaining tokens on the command's line.</summary>
    public List<SyntaxItem> Arguments { get; set; } = [];
}

// ---------- Generic object DDL (typed header + token-tree body) ----------

public enum ObjectKind
{
    Table, View, MaterializedView, MaterializedViewLog, MaterializedZonemap, Index, Indextype, Sequence, Synonym,
    Procedure, Function, Package, PackageBody, Trigger, Type, TypeBody, DatabaseLink, User, Role, Profile, Tablespace,
    TablespaceSet, Directory, Library, JavaSource, JavaClass, JavaResource, Cluster, Context, Dimension, Domain, Edition,
    Operator, Outline, FlashbackArchive, RollbackSegment, RestorePoint, Diskgroup, PluggableDatabase, Database,
    LockdownProfile, AuditPolicy, AnalyticView, AttributeDimension, Hierarchy, InmemoryJoinGroup, MiningModel,
    PropertyGraph, JsonRelationalDualityView, SqlTranslationProfile, ControlFile, Pfile, Spfile, Schema, ResourceCost,
    System, Session, Json, Mle, Sharding, Other,
}

/// <summary>CREATE of an object kind that has no dedicated node; the clause body is a token tree.</summary>
public sealed class CreateObjectStatement : Statement
{
    public ObjectKind Kind { get; set; }
    public bool OrReplace { get; set; }
    public bool Public { get; set; }
    public bool IfNotExists { get; set; }
    public List<Identifier> Name { get; set; } = [];
    public List<SyntaxItem> Body { get; set; } = [];
}

/// <summary>ALTER of an object kind that has no dedicated node; the clause body is a token tree.</summary>
public sealed class AlterObjectStatement : Statement
{
    public ObjectKind Kind { get; set; }
    public bool Public { get; set; }
    public bool IfExists { get; set; }
    public List<Identifier> Name { get; set; } = [];
    public List<SyntaxItem> Body { get; set; } = [];
}

/// <summary>Statements such as ANALYZE, AUDIT, NOAUDIT, ASSOCIATE/DISASSOCIATE STATISTICS, ADMINISTER KEY MANAGEMENT (token-tree body).</summary>
public sealed class AdminStatement : Statement
{
    public string Verb { get; set; } = "";
    public List<SyntaxItem> Body { get; set; } = [];
}

public sealed class DropStatement : Statement
{
    public ObjectKind Kind { get; set; }
    public bool Public { get; set; }
    public bool IfExists { get; set; }
    public List<Identifier> Name { get; set; } = [];
    /// <summary>DROP MATERIALIZED VIEW LOG ON table.</summary>
    public List<Identifier> OnTable { get; set; } = [];
    public bool CascadeConstraints { get; set; }
    public bool Cascade { get; set; }
    public bool Purge { get; set; }
    public bool Force { get; set; }
    public bool IncludingContents { get; set; }
    public bool AndDatafiles { get; set; }
    public bool Validate { get; set; }
    public bool Invalidate { get; set; }
    /// <summary>Other trailing clause words, kept structured.</summary>
    public List<SyntaxItem> Rest { get; set; } = [];
}

// ---------- Physical attributes ----------

public abstract class PhysicalAttribute : SqlNode { }

public sealed class TablespaceAttribute : PhysicalAttribute
{
    public Identifier Name { get; set; } = null!;
    public bool Default { get; set; }
}

public enum NumericAttributeKind { PctFree, PctUsed, InitTrans, MaxTrans, PctThreshold, Freelists }

public sealed class NumericAttribute : PhysicalAttribute
{
    public NumericAttributeKind Kind { get; set; }
    public Expression Value { get; set; } = null!;
}

public sealed class StorageAttribute : PhysicalAttribute
{
    public List<StorageOption> Options { get; set; } = [];
}

public sealed class StorageOption : SqlNode
{
    public string Name { get; set; } = "";
    public Expression? Value { get; set; }
}

public sealed class LoggingAttribute : PhysicalAttribute { public bool Logging { get; set; } }
public sealed class CacheAttribute : PhysicalAttribute { public bool Cache { get; set; } }
public sealed class RowMovementAttribute : PhysicalAttribute { public bool Enabled { get; set; } }

public sealed class ParallelAttribute : PhysicalAttribute
{
    public bool NoParallel { get; set; }
    public Expression? Degree { get; set; }
}

public sealed class CompressAttribute : PhysicalAttribute
{
    public bool Compress { get; set; }
    public Expression? PrefixLength { get; set; }
    /// <summary>BASIC | ADVANCED | FOR OLTP | FOR QUERY LOW|HIGH | FOR ARCHIVE LOW|HIGH ...</summary>
    public List<SyntaxItem> Method { get; set; } = [];
}

public enum TableOrganization { Heap, Index, External }

public sealed class OrganizationAttribute : PhysicalAttribute
{
    public TableOrganization Organization { get; set; }
    /// <summary>ORGANIZATION EXTERNAL (...) / INDEX options.</summary>
    public List<SyntaxItem> Details { get; set; } = [];
}

public enum PartitionMethod { Range, List, Hash, Reference, System }

public sealed class PartitionByAttribute : PhysicalAttribute
{
    public PartitionMethod Method { get; set; }
    public List<Identifier> Columns { get; set; } = [];
    public Identifier? ReferenceConstraint { get; set; }
    public Expression? Interval { get; set; }
    public Expression? PartitionCount { get; set; }
    public List<PartitionDefinition> Partitions { get; set; } = [];
    /// <summary>SUBPARTITION BY ... template (method + columns).</summary>
    public PartitionMethod? SubpartitionMethod { get; set; }
    public List<Identifier> SubpartitionColumns { get; set; } = [];
}

public sealed class PartitionDefinition : SqlNode
{
    public Identifier? Name { get; set; }
    public bool LessThan { get; set; }
    public bool ValuesDefault { get; set; }
    public List<Expression> Values { get; set; } = [];
    public List<PhysicalAttribute> Attributes { get; set; } = [];
    public List<PartitionDefinition> Subpartitions { get; set; } = [];
}

/// <summary>Attribute syntax without a dedicated node, as a token tree (see COVERAGE.md).</summary>
public sealed class OtherAttribute : PhysicalAttribute
{
    public List<SyntaxItem> Items { get; set; } = [];
}

// ---------- CREATE TABLE & friends ----------

public enum OnCommitAction { PreserveRows, DeleteRows, DropDefinition, PreserveDefinition }

public sealed class CreateTableStatement : Statement
{
    public SharingKind? Sharing { get; set; }
    public bool OrReplace { get; set; }
    public bool IfNotExists { get; set; }
    public bool GlobalTemporary { get; set; }
    public bool PrivateTemporary { get; set; }
    public bool Duplicated { get; set; }
    public bool Immutable { get; set; }
    public bool Blockchain { get; set; }
    public List<Identifier> Name { get; set; } = [];
    public List<ColumnDefinition> Columns { get; set; } = [];
    public List<TableConstraint> Constraints { get; set; } = [];
    /// <summary>CREATE TABLE t OF object_type.</summary>
    public List<Identifier> OfType { get; set; } = [];
    public OnCommitAction? OnCommit { get; set; }
    public List<PhysicalAttribute> Attributes { get; set; } = [];
    public SelectStatement? AsQuery { get; set; }
}

public enum IdentityMode { Always, ByDefault }

public sealed class IdentityClause : SqlNode
{
    public IdentityMode Mode { get; set; }
    public bool OnNull { get; set; }
    public List<SequenceOption> Options { get; set; } = [];
}

public sealed class EncryptClause : SqlNode
{
    public Expression? Algorithm { get; set; }
    public Expression? IdentifiedBy { get; set; }
    public bool NoSalt { get; set; }
    public bool Salt { get; set; }
}

public sealed class ColumnDefinition : SqlNode
{
    public Identifier Name { get; set; } = null!;
    public DataType? Type { get; set; }
    public Expression? Default { get; set; }
    public bool DefaultOnNull { get; set; }
    public IdentityClause? Identity { get; set; }
    public Expression? VirtualExpression { get; set; }
    public List<TableConstraint> Constraints { get; set; } = [];
    public bool? Visible { get; set; }
    public Identifier? Collation { get; set; }
    public EncryptClause? Encrypt { get; set; }
    public bool Sort { get; set; }
    /// <summary>Column properties without a dedicated node (LOB storage, substitutable, ...).</summary>
    public List<SyntaxItem> OtherProperties { get; set; } = [];
}

public enum ConstraintKind { PrimaryKey, Unique, ForeignKey, Check, NotNull, Null }

public sealed class ConstraintState : SqlNode
{
    public bool? Deferrable { get; set; }
    public bool? InitiallyDeferred { get; set; }
    public bool? Enable { get; set; }
    public bool? Validate { get; set; }
    public bool? Rely { get; set; }
    public UsingIndexClause? UsingIndex { get; set; }
    public List<Identifier> ExceptionsInto { get; set; } = [];
}

public sealed class UsingIndexClause : SqlNode
{
    public List<Identifier> IndexName { get; set; } = [];
    public CreateIndexStatement? Create { get; set; }
    public List<PhysicalAttribute> Attributes { get; set; } = [];
}

public enum ReferentialAction { Cascade, SetNull }

public sealed class TableConstraint : SqlNode
{
    public Identifier? Name { get; set; }
    /// <summary>Null only for MODIFY CONSTRAINT name ... which identifies a constraint by name.</summary>
    public ConstraintKind? Kind { get; set; }
    public List<Identifier> Columns { get; set; } = [];
    public Expression? Check { get; set; }
    public List<Identifier> RefTable { get; set; } = [];
    public List<Identifier> RefColumns { get; set; } = [];
    public ReferentialAction? OnDelete { get; set; }
    public ConstraintState? State { get; set; }
}

public sealed class CreateIndexStatement : Statement
{
    public bool Unique { get; set; }
    public bool Bitmap { get; set; }
    public bool IfNotExists { get; set; }
    public List<Identifier> Name { get; set; } = [];
    public List<Identifier> Table { get; set; } = [];
    /// <summary>ON CLUSTER name (instead of a table).</summary>
    public List<Identifier> Cluster { get; set; } = [];
    public Identifier? TableAlias { get; set; }
    public List<IndexColumn> Columns { get; set; } = [];
    public List<Identifier> Indextype { get; set; } = [];
    public Expression? IndextypeParameters { get; set; }
    public bool? Visible { get; set; }
    public bool Online { get; set; }
    public bool Reverse { get; set; }
    public bool Local { get; set; }
    public bool Global { get; set; }
    public List<PhysicalAttribute> Attributes { get; set; } = [];
}

public sealed class IndexColumn : SqlNode
{
    public Expression Expression { get; set; } = null!;
    public bool Descending { get; set; }
}

public enum ViewBequeath { CurrentUser, Definer }
public enum RefreshMethod { Fast, Complete, Force, Never }
public enum RefreshMode { OnDemand, OnCommit }

public sealed class RefreshClause : SqlNode
{
    public RefreshMethod? Method { get; set; }
    public RefreshMode? Mode { get; set; }
    public Expression? StartWith { get; set; }
    public Expression? Next { get; set; }
    public bool WithPrimaryKey { get; set; }
    public bool WithRowId { get; set; }
}

public sealed class CreateViewStatement : Statement
{
    public SharingKind? Sharing { get; set; }
    public bool OrReplace { get; set; }
    public bool IfNotExists { get; set; }
    public bool Materialized { get; set; }
    public bool Force { get; set; }
    public bool NoForce { get; set; }
    public bool Editioning { get; set; }
    public List<Identifier> Name { get; set; } = [];
    public List<Identifier> Columns { get; set; } = [];
    public ViewBequeath? Bequeath { get; set; }
    // materialized view options
    public bool? BuildImmediate { get; set; }
    public RefreshClause? Refresh { get; set; }
    public bool? QueryRewrite { get; set; }
    public List<PhysicalAttribute> Attributes { get; set; } = [];
    public SelectStatement Query { get; set; } = null!;
    public bool ReadOnly { get; set; }
    public bool CheckOption { get; set; }
    public Identifier? CheckOptionConstraint { get; set; }
}

public sealed class CreateSequenceStatement : Statement
{
    public SharingKind? Sharing { get; set; }
    public bool IfNotExists { get; set; }
    public List<Identifier> Name { get; set; } = [];
    public List<SequenceOption> Options { get; set; } = [];
}

public sealed class SequenceOption : SqlNode
{
    /// <summary>Keyword phrase: START WITH, INCREMENT BY, MAXVALUE, NOMAXVALUE, CACHE, CYCLE, ORDER, KEEP, SCALE, ...</summary>
    public string Name { get; set; } = "";
    public Expression? Value { get; set; }
}

public sealed class CreateSynonymStatement : Statement
{
    public SharingKind? Sharing { get; set; }
    public bool OrReplace { get; set; }
    public bool IfNotExists { get; set; }
    public bool Public { get; set; }
    public List<Identifier> Name { get; set; } = [];
    public List<Identifier> Target { get; set; } = [];
    public List<Identifier> DbLink { get; set; } = [];
}

public sealed class AlterTableStatement : Statement
{
    public List<Identifier> Name { get; set; } = [];
    public bool IfExists { get; set; }
    public List<AlterTableAction> Actions { get; set; } = [];
}

public enum AlterTableActionKind
{
    AddColumns, AddConstraint, ModifyColumns, DropColumns, DropConstraint, DropPrimaryKey, DropUnique,
    RenameColumn, RenameConstraint, RenameTo, ModifyConstraint, SetUnused, Attributes, Other,
}

public sealed class AlterTableAction : SqlNode
{
    public AlterTableActionKind Kind { get; set; }
    public List<ColumnDefinition> Columns { get; set; } = [];
    public List<TableConstraint> Constraints { get; set; } = [];
    public List<Identifier> Names { get; set; } = [];
    public Identifier? NewName { get; set; }
    public bool Cascade { get; set; }
    public bool Online { get; set; }
    public List<PhysicalAttribute> Attributes { get; set; } = [];
    /// <summary>Partition/other maintenance actions without a dedicated node, as a token tree.</summary>
    public List<SyntaxItem> Syntax { get; set; } = [];
}
