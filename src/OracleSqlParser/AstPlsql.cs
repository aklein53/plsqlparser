namespace OracleSqlParser;

// ---------- Blocks ----------

public sealed class Block : Statement
{
    public bool HasDeclare { get; set; }
    public List<Declaration> Declarations { get; set; } = [];
    public List<Statement> Statements { get; set; } = [];
    public List<ExceptionHandler> Handlers { get; set; } = [];
    public Identifier? EndName { get; set; }
}

public sealed class ExceptionHandler : SqlNode
{
    public List<List<Identifier>> Exceptions { get; set; } = [];
    public bool Others { get; set; }
    public List<Statement> Statements { get; set; } = [];
}

// ---------- Declarations ----------

public abstract class Declaration : SqlNode { }

public sealed class VariableDeclaration : Declaration
{
    public Identifier Name { get; set; } = null!;
    public bool Constant { get; set; }
    public DataType Type { get; set; } = null!;
    public bool NotNull { get; set; }
    public Expression? Default { get; set; }
}

public sealed class ExceptionDeclaration : Declaration
{
    public Identifier Name { get; set; } = null!;
}

public sealed class PragmaDeclaration : Declaration
{
    public Identifier Name { get; set; } = null!;
    public List<Expression> Arguments { get; set; } = [];
}

public enum ParamMode { In, Out, InOut }

public sealed class Parameter : SqlNode
{
    public Identifier Name { get; set; } = null!;
    public ParamMode? Mode { get; set; }
    public bool NoCopy { get; set; }
    public DataType? Type { get; set; }
    public Expression? Default { get; set; }
}

public sealed class CursorDeclaration : Declaration
{
    public Identifier Name { get; set; } = null!;
    public List<Parameter> Parameters { get; set; } = [];
    public DataType? ReturnType { get; set; }
    public SelectStatement? Query { get; set; }
}

public sealed class RecordTypeDeclaration : Declaration
{
    public Identifier Name { get; set; } = null!;
    public List<VariableDeclaration> Fields { get; set; } = [];
}

public enum CollectionKind { Table, Varray }

public sealed class CollectionTypeDeclaration : Declaration
{
    public Identifier Name { get; set; } = null!;
    public CollectionKind Kind { get; set; }
    public Expression? Limit { get; set; }
    public DataType ElementType { get; set; } = null!;
    public bool NotNull { get; set; }
    public DataType? IndexBy { get; set; }
}

public sealed class RefCursorTypeDeclaration : Declaration
{
    public Identifier Name { get; set; } = null!;
    public DataType? ReturnType { get; set; }
}

public sealed class SubtypeDeclaration : Declaration
{
    public Identifier Name { get; set; } = null!;
    public DataType BaseType { get; set; } = null!;
    public Expression? RangeLow { get; set; }
    public Expression? RangeHigh { get; set; }
    public bool NotNull { get; set; }
}

public enum EditionableKind { Editionable, NonEditionable }
public enum SharingKind { Metadata, Data, Extended, None }
public enum AuthidKind { CurrentUser, Definer }
public enum MacroKind { Scalar, Table }
public enum ParallelPartitionMethod { Any, Hash, Range, Value }
public enum AccessorKind { Function, Procedure, Package, Trigger, Type }

[Flags]
public enum MemberModifiers
{
    None = 0, Member = 1, Static = 2, Constructor = 4, Map = 8, Order = 16, Final = 32, NotFinal = 64,
    Instantiable = 128, NotInstantiable = 256, Overriding = 512,
}

public sealed class ParallelEnableClause : SqlNode
{
    public Identifier? PartitionArgument { get; set; }
    public ParallelPartitionMethod? Method { get; set; }
    public List<Identifier> MethodColumns { get; set; } = [];
    /// <summary>ORDER|CLUSTER BY columns.</summary>
    public bool Cluster { get; set; }
    public bool Order { get; set; }
    public List<Identifier> StreamingColumns { get; set; } = [];
}

public sealed class Accessor : SqlNode
{
    public AccessorKind? Kind { get; set; }
    public List<Identifier> Name { get; set; } = [];
}

/// <summary>Clauses between a program header and IS/AS (also package and type headers).</summary>
public sealed class ProgramOptions : SqlNode
{
    public AuthidKind? Authid { get; set; }
    public SharingKind? Sharing { get; set; }
    public bool Deterministic { get; set; }
    public bool Pipelined { get; set; }
    public List<Identifier> PipelinedUsing { get; set; } = [];
    public ParallelEnableClause? ParallelEnable { get; set; }
    public bool ResultCache { get; set; }
    public List<List<Identifier>> RelyOn { get; set; } = [];
    public List<Identifier> AggregateUsing { get; set; } = [];
    public MacroKind? SqlMacro { get; set; }
    public List<Accessor> AccessibleBy { get; set; } = [];
    public bool Deprecate { get; set; }
    public Expression? DeprecateMessage { get; set; }
    public Identifier? DefaultCollation { get; set; }
}

public enum ExternalLanguage { Java, C, JavaScript }
public enum ExternalParameterKind { Context, Self, Return, Named }
public enum ExternalProperty { Indicator, IndicatorStruct, IndicatorTdo, Length, MaxLen, Tdo, CharsetId, CharsetForm }

public sealed class ExternalParameter : SqlNode
{
    public ExternalParameterKind Kind { get; set; }
    public Identifier? Name { get; set; }
    public ExternalProperty? Property { get; set; }
    public bool ByReference { get; set; }
    public DataType? Type { get; set; }
}

/// <summary>LANGUAGE JAVA/C/JAVASCRIPT call specification.</summary>
public sealed class ExternalCall : SqlNode
{
    public ExternalLanguage Language { get; set; }
    public Expression? Name { get; set; }
    public Identifier? Library { get; set; }
    public List<Identifier> AgentIn { get; set; } = [];
    public bool WithContext { get; set; }
    public bool Pascal { get; set; }
    public List<ExternalParameter> Parameters { get; set; } = [];
    /// <summary>MLE module/env/signature (LANGUAGE JAVASCRIPT).</summary>
    public List<Identifier> MleModule { get; set; } = [];
    public List<Identifier> MleEnv { get; set; } = [];
    public Expression? Signature { get; set; }
}

/// <summary>Procedure or function: nested declaration, package member, type member or standalone (via CreateProgramStatement).</summary>
public sealed class SubprogramDeclaration : Declaration
{
    public bool IsFunction { get; set; }
    public MemberModifiers Modifiers { get; set; }
    public List<Identifier> Name { get; set; } = [];
    public List<Parameter> Parameters { get; set; } = [];
    public DataType? ReturnType { get; set; }
    /// <summary>CONSTRUCTOR FUNCTION ... RETURN SELF AS RESULT.</summary>
    public bool ReturnSelfAsResult { get; set; }
    public ProgramOptions? Options { get; set; }
    public Block? Body { get; set; }
    public ExternalCall? External { get; set; }
}

// ---------- Program units ----------

public sealed class CreateProgramStatement : Statement
{
    public bool OrReplace { get; set; }
    public EditionableKind? Editionable { get; set; }
    public SubprogramDeclaration Subprogram { get; set; } = null!;
}

public sealed class CreatePackageStatement : Statement
{
    public bool OrReplace { get; set; }
    public EditionableKind? Editionable { get; set; }
    public List<Identifier> Name { get; set; } = [];
    public ProgramOptions? Options { get; set; }
    public List<Declaration> Declarations { get; set; } = [];
    public Identifier? EndName { get; set; }
}

public sealed class CreatePackageBodyStatement : Statement
{
    public bool OrReplace { get; set; }
    public EditionableKind? Editionable { get; set; }
    public List<Identifier> Name { get; set; } = [];
    public List<Declaration> Declarations { get; set; } = [];
    public List<Statement> InitStatements { get; set; } = [];
    public List<ExceptionHandler> InitHandlers { get; set; } = [];
    public Identifier? EndName { get; set; }
}

public enum TriggerTiming { Before, After, InsteadOf, For }
public enum TriggerEventKind { Insert, Update, Delete, Ddl, Database }
public enum TriggerTargetKind { Table, View, Schema, Database, PluggableDatabase, NestedTable }
public enum ReferencingKind { New, Old, Parent }
public enum TriggerOrdering { Follows, Precedes }
public enum CrossEdition { Forward, Reverse }
public enum TimingPoint { BeforeStatement, BeforeEachRow, AfterStatement, AfterEachRow, InsteadOfEachRow }

public sealed class TriggerEvent : SqlNode
{
    public TriggerEventKind Kind { get; set; }
    /// <summary>UPDATE OF columns.</summary>
    public List<Identifier> Columns { get; set; } = [];
    /// <summary>DDL/database event keyword (CREATE, ALTER, DROP, LOGON, SERVERERROR, STARTUP, ...).</summary>
    public string? EventName { get; set; }
}

public sealed class ReferencingItem : SqlNode
{
    public ReferencingKind Kind { get; set; }
    public Identifier Alias { get; set; } = null!;
}

public sealed class CreateTriggerStatement : Statement
{
    public bool OrReplace { get; set; }
    public EditionableKind? Editionable { get; set; }
    public SharingKind? Sharing { get; set; }
    public List<Identifier> Name { get; set; } = [];
    public TriggerTiming Timing { get; set; }
    public List<TriggerEvent> Events { get; set; } = [];
    public TriggerTargetKind TargetKind { get; set; }
    public List<Identifier> Target { get; set; } = [];
    /// <summary>ON NESTED TABLE column OF view.</summary>
    public Identifier? NestedTableColumn { get; set; }
    public List<ReferencingItem> Referencing { get; set; } = [];
    public bool ForEachRow { get; set; }
    public CrossEdition? CrossEdition { get; set; }
    public TriggerOrdering? Ordering { get; set; }
    public List<List<Identifier>> OrderingTriggers { get; set; } = [];
    public bool? Enabled { get; set; }
    public Expression? When { get; set; }
    public Block? Body { get; set; }
    public CallStatement? Call { get; set; }
    public bool Compound { get; set; }
    public List<Declaration> CompoundDeclarations { get; set; } = [];
    public List<TimingPointSection> Sections { get; set; } = [];
}

public sealed class TimingPointSection : SqlNode
{
    public TimingPoint Point { get; set; }
    public List<Statement> Statements { get; set; } = [];
    public List<ExceptionHandler> Handlers { get; set; } = [];
}

public enum TypeKind { Object, Table, Varray, Under }

public sealed class CreateTypeStatement : Statement
{
    public bool OrReplace { get; set; }
    public EditionableKind? Editionable { get; set; }
    public List<Identifier> Name { get; set; } = [];
    /// <summary>OBJECT, TABLE, VARRAY, or UNDER.</summary>
    public TypeKind Kind { get; set; }
    public ProgramOptions? Options { get; set; }
    public bool Force { get; set; }
    public Expression? Oid { get; set; }
    public bool? Final { get; set; }
    public bool? Instantiable { get; set; }
    public bool? Persistable { get; set; }
    public List<VariableDeclaration> Attributes { get; set; } = [];
    public List<SubprogramDeclaration> Members { get; set; } = [];
    public DataType? ElementType { get; set; }
    public Expression? Limit { get; set; }
    public List<Identifier> SuperType { get; set; } = [];
}

public sealed class CreateTypeBodyStatement : Statement
{
    public bool OrReplace { get; set; }
    public List<Identifier> Name { get; set; } = [];
    public List<SubprogramDeclaration> Members { get; set; } = [];
}

// ---------- Statements ----------

public sealed class NullStatement : Statement { }

public sealed class AssignmentStatement : Statement
{
    public Expression Target { get; set; } = null!;
    public Expression Value { get; set; } = null!;
}

public sealed class ProcedureCallStatement : Statement
{
    public Expression Call { get; set; } = null!;
}

public sealed class IfStatement : Statement
{
    public Expression Condition { get; set; } = null!;
    public List<Statement> ThenStatements { get; set; } = [];
    public List<IfBranch> ElsifBranches { get; set; } = [];
    public List<Statement>? ElseStatements { get; set; }
}

public sealed class IfBranch : SqlNode
{
    public Expression Condition { get; set; } = null!;
    public List<Statement> Statements { get; set; } = [];
}

public sealed class PlsqlCaseStatement : Statement
{
    public Expression? Operand { get; set; }
    public List<CaseStatementWhen> Whens { get; set; } = [];
    public List<Statement>? ElseStatements { get; set; }
}

public sealed class CaseStatementWhen : SqlNode
{
    public Expression When { get; set; } = null!;
    public List<Statement> Statements { get; set; } = [];
}

public enum LoopKind { Basic, While, ForNumeric, ForCursor }

public sealed class LoopStatement : Statement
{
    public LoopKind Kind { get; set; }
    public Expression? Condition { get; set; }
    public Identifier? Variable { get; set; }
    public bool Reverse { get; set; }
    public Expression? Lower { get; set; }
    public Expression? Upper { get; set; }
    /// <summary>FOR r IN cursor_name(args) / collection expression.</summary>
    public Expression? CursorCall { get; set; }
    /// <summary>FOR r IN (SELECT ...).</summary>
    public SelectStatement? Query { get; set; }
    public List<Statement> Body { get; set; } = [];
    public Identifier? EndLabel { get; set; }
}

public sealed class ExitStatement : Statement
{
    public Identifier? Label { get; set; }
    public Expression? When { get; set; }
}

public sealed class ContinueStatement : Statement
{
    public Identifier? Label { get; set; }
    public Expression? When { get; set; }
}

public sealed class GotoStatement : Statement
{
    public Identifier Target { get; set; } = null!;
}

public sealed class ReturnStatement : Statement
{
    public Expression? Value { get; set; }
}

public sealed class RaiseStatement : Statement
{
    public List<Identifier> Exception { get; set; } = [];
}

public sealed class UsingArgument : SqlNode
{
    public ParamMode? Mode { get; set; }
    public Expression Value { get; set; } = null!;
}

public sealed class OpenStatement : Statement
{
    public Expression Cursor { get; set; } = null!;
    public SelectStatement? ForQuery { get; set; }
    public Expression? ForExpression { get; set; }
    public List<UsingArgument> Using { get; set; } = [];
}

public sealed class FetchStatement : Statement
{
    public Expression Cursor { get; set; } = null!;
    public bool BulkCollect { get; set; }
    public List<Expression> Into { get; set; } = [];
    public Expression? Limit { get; set; }
}

public sealed class CloseStatement : Statement
{
    public Expression Cursor { get; set; } = null!;
}

public sealed class ExecuteImmediateStatement : Statement
{
    public Expression Sql { get; set; } = null!;
    public bool BulkCollect { get; set; }
    public List<Expression> Into { get; set; } = [];
    public List<UsingArgument> Using { get; set; } = [];
    public List<Expression> ReturningInto { get; set; } = [];
    public bool ReturningBulkCollect { get; set; }
}

public sealed class ForallStatement : Statement
{
    public Identifier Index { get; set; } = null!;
    public Expression? Lower { get; set; }
    public Expression? Upper { get; set; }
    public Expression? IndicesOf { get; set; }
    public Expression? ValuesOf { get; set; }
    public Expression? BetweenLow { get; set; }
    public Expression? BetweenHigh { get; set; }
    public bool SaveExceptions { get; set; }
    public Statement Body { get; set; } = null!;
}

public sealed class PipeRowStatement : Statement
{
    public Expression Value { get; set; } = null!;
}

public enum JavaObjectKind { Source, Class, Resource }
public enum JavaUsingKind { Bfile, Clob, Blob, Key }

/// <summary>CREATE JAVA SOURCE/CLASS/RESOURCE. SourceText is the embedded Java program (not SQL).</summary>
public sealed class CreateJavaStatement : Statement
{
    public bool OrReplace { get; set; }
    public bool AndResolve { get; set; }
    public bool AndCompile { get; set; }
    public bool NoForce { get; set; }
    public JavaObjectKind Kind { get; set; }
    public List<Identifier> Name { get; set; } = [];
    public SharingKind? Sharing { get; set; }
    public AuthidKind? Authid { get; set; }
    public List<SyntaxItem> Resolver { get; set; } = [];
    public JavaUsingKind? UsingKind { get; set; }
    public Expression? UsingDirectory { get; set; }
    public Expression? UsingFile { get; set; }
    public Expression? UsingKey { get; set; }
    public SelectStatement? UsingQuery { get; set; }
    public string? SourceText { get; set; }
}

public sealed class CreateLibraryStatement : Statement
{
    public bool OrReplace { get; set; }
    public EditionableKind? Editionable { get; set; }
    public SharingKind? Sharing { get; set; }
    public List<Identifier> Name { get; set; } = [];
    public Expression Path { get; set; } = null!;
    public Expression? Agent { get; set; }
    public Identifier? Credential { get; set; }
}
