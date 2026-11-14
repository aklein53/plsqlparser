namespace OracleSqlParser;

// ---------- Blocks ----------

public sealed partial class Block : Statement
{
    public bool HasDeclare { get; set; }
    public List<Declaration> Declarations { get; set; } = [];
    public List<Statement> Statements { get; set; } = [];
    public List<ExceptionHandler> Handlers { get; set; } = [];
    public Identifier? EndName { get; set; }
}

public sealed partial class ExceptionHandler : SqlNode
{
    public List<List<Identifier>> Exceptions { get; set; } = [];
    public bool Others { get; set; }
    public List<Statement> Statements { get; set; } = [];
}

// ---------- Conditional compilation ($IF, $ERROR, $$inquiry directives) ----------

/// <summary>$IF cond $THEN ... [$ELSIF cond $THEN ...] [$ELSE ...] $END where the branches hold statements.</summary>
public sealed partial class ConditionalStatement : Statement
{
    public Expression Condition { get; set; } = null!;
    public List<Statement> ThenStatements { get; set; } = [];
    public List<ConditionalStatementBranch> ElsifBranches { get; set; } = [];
    public List<Statement>? ElseStatements { get; set; }
}

public sealed partial class ConditionalStatementBranch : SqlNode
{
    public Expression Condition { get; set; } = null!;
    public List<Statement> Statements { get; set; } = [];
}

/// <summary>$ERROR 'message' $END in a statement list: a compile-time error, raised when its branch is selected.</summary>
public sealed partial class CompilationErrorStatement : Statement
{
    public Expression Message { get; set; } = null!;
}

/// <summary>$IF cond $THEN ... [$ELSIF cond $THEN ...] [$ELSE ...] $END where the branches hold declarations.</summary>
public sealed partial class ConditionalDeclaration : Declaration
{
    public Expression Condition { get; set; } = null!;
    public List<Declaration> ThenDeclarations { get; set; } = [];
    public List<ConditionalDeclarationBranch> ElsifBranches { get; set; } = [];
    public List<Declaration>? ElseDeclarations { get; set; }
}

public sealed partial class ConditionalDeclarationBranch : SqlNode
{
    public Expression Condition { get; set; } = null!;
    public List<Declaration> Declarations { get; set; } = [];
}

/// <summary>$ERROR 'message' $END in a declaration list: a compile-time error, raised when its branch is selected.</summary>
public sealed partial class CompilationErrorDeclaration : Declaration
{
    public Expression Message { get; set; } = null!;
}

/// <summary>Inquiry directive such as $$PLSQL_UNIT or a $$flag set with PLSQL_CCFLAGS.</summary>
public sealed partial class InquiryDirective : Expression
{
    public Identifier Name { get; set; } = null!;
}

// ---------- Declarations ----------

public abstract partial class Declaration : SqlNode { }

public sealed partial class VariableDeclaration : Declaration
{
    public Identifier Name { get; set; } = null!;
    public bool Constant { get; set; }
    public DataType Type { get; set; } = null!;
    public bool NotNull { get; set; }
    public Expression? Default { get; set; }
}

/// <summary>$IF cond $THEN ... [$ELSIF cond $THEN ...] [$ELSE ...] $END around parameters (the commas are inside the branches).</summary>
public sealed partial class ConditionalParameter : ParameterItem
{
    public Expression Condition { get; set; } = null!;
    public List<ParameterItem> ThenParameters { get; set; } = [];
    public List<ConditionalParameterBranch> ElsifBranches { get; set; } = [];
    public List<ParameterItem>? ElseParameters { get; set; }
}

public sealed partial class ConditionalParameterBranch : SqlNode
{
    public Expression Condition { get; set; } = null!;
    public List<ParameterItem> Parameters { get; set; } = [];
}

public sealed partial class ExceptionDeclaration : Declaration
{
    public Identifier Name { get; set; } = null!;
}

public sealed partial class PragmaDeclaration : Declaration
{
    public Identifier Name { get; set; } = null!;
    public List<Expression> Arguments { get; set; } = [];
}

public enum ParamMode { In, Out, InOut }

/// <summary>An entry in a parameter list: a plain parameter or a conditional group (see <see cref="ConditionalParameter"/>).</summary>
public abstract partial class ParameterItem : SqlNode
{
}

public sealed partial class Parameter : ParameterItem
{
    public Identifier Name { get; set; } = null!;
    public ParamMode? Mode { get; set; }
    public bool NoCopy { get; set; }
    public DataType? Type { get; set; }
    public Expression? Default { get; set; }
}

public sealed partial class CursorDeclaration : Declaration
{
    public Identifier Name { get; set; } = null!;
    public List<ParameterItem> Parameters { get; set; } = [];
    public DataType? ReturnType { get; set; }
    public SelectStatement? Query { get; set; }
}

public sealed partial class RecordTypeDeclaration : Declaration
{
    public Identifier Name { get; set; } = null!;
    public List<VariableDeclaration> Fields { get; set; } = [];
}

public enum CollectionKind { Table, Varray }

public sealed partial class CollectionTypeDeclaration : Declaration
{
    public Identifier Name { get; set; } = null!;
    public CollectionKind Kind { get; set; }
    public Expression? Limit { get; set; }
    public DataType ElementType { get; set; } = null!;
    public bool NotNull { get; set; }
    public DataType? IndexBy { get; set; }
}

public sealed partial class RefCursorTypeDeclaration : Declaration
{
    public Identifier Name { get; set; } = null!;
    public DataType? ReturnType { get; set; }
}

public sealed partial class SubtypeDeclaration : Declaration
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

public sealed partial class ParallelEnableClause : SqlNode
{
    public Identifier? PartitionArgument { get; set; }
    public ParallelPartitionMethod? Method { get; set; }
    public List<Identifier> MethodColumns { get; set; } = [];
    /// <summary>ORDER|CLUSTER BY columns.</summary>
    public bool Cluster { get; set; }
    public bool Order { get; set; }
    public List<Identifier> StreamingColumns { get; set; } = [];
}

public sealed partial class Accessor : SqlNode
{
    public AccessorKind? Kind { get; set; }
    public List<Identifier> Name { get; set; } = [];
}

/// <summary>Clauses between a program header and IS/AS (also package and type headers).</summary>
public sealed partial class ProgramOptions : SqlNode
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

public sealed partial class ExternalParameter : SqlNode
{
    public ExternalParameterKind Kind { get; set; }
    public Identifier? Name { get; set; }
    public ExternalProperty? Property { get; set; }
    public bool ByReference { get; set; }
    public DataType? Type { get; set; }
}

/// <summary>LANGUAGE JAVA/C/JAVASCRIPT call specification.</summary>
public sealed partial class ExternalCall : SqlNode
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
public sealed partial class SubprogramDeclaration : Declaration
{
    public bool IsFunction { get; set; }
    public MemberModifiers Modifiers { get; set; }
    public List<Identifier> Name { get; set; } = [];
    public List<ParameterItem> Parameters { get; set; } = [];
    public DataType? ReturnType { get; set; }
    /// <summary>CONSTRUCTOR FUNCTION ... RETURN SELF AS RESULT.</summary>
    public bool ReturnSelfAsResult { get; set; }
    public ProgramOptions? Options { get; set; }
    public Block? Body { get; set; }
    public ExternalCall? External { get; set; }
}

// ---------- Program units ----------

public sealed partial class CreateProgramStatement : Statement
{
    public bool OrReplace { get; set; }
    public EditionableKind? Editionable { get; set; }
    public SubprogramDeclaration Subprogram { get; set; } = null!;
}

public sealed partial class CreatePackageStatement : Statement
{
    public bool OrReplace { get; set; }
    public EditionableKind? Editionable { get; set; }
    public List<Identifier> Name { get; set; } = [];
    public ProgramOptions? Options { get; set; }
    public List<Declaration> Declarations { get; set; } = [];
    public Identifier? EndName { get; set; }
}

public sealed partial class CreatePackageBodyStatement : Statement
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

public sealed partial class TriggerEvent : SqlNode
{
    public TriggerEventKind Kind { get; set; }
    /// <summary>UPDATE OF columns.</summary>
    public List<Identifier> Columns { get; set; } = [];
    /// <summary>DDL/database event keyword (CREATE, ALTER, DROP, LOGON, SERVERERROR, STARTUP, ...).</summary>
    public string? EventName { get; set; }
}

public sealed partial class ReferencingItem : SqlNode
{
    public ReferencingKind Kind { get; set; }
    public Identifier Alias { get; set; } = null!;
}

public sealed partial class CreateTriggerStatement : Statement
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

public sealed partial class TimingPointSection : SqlNode
{
    public TimingPoint Point { get; set; }
    public List<Statement> Statements { get; set; } = [];
    public List<ExceptionHandler> Handlers { get; set; } = [];
}

public enum TypeKind { Object, Table, Varray, Under }

public sealed partial class CreateTypeStatement : Statement
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

public sealed partial class CreateTypeBodyStatement : Statement
{
    public bool OrReplace { get; set; }
    public List<Identifier> Name { get; set; } = [];
    public List<SubprogramDeclaration> Members { get; set; } = [];
}

// ---------- Statements ----------

public sealed partial class NullStatement : Statement { }

public sealed partial class AssignmentStatement : Statement
{
    public Expression Target { get; set; } = null!;
    public Expression Value { get; set; } = null!;
}

public sealed partial class ProcedureCallStatement : Statement
{
    public Expression Call { get; set; } = null!;
}

public sealed partial class IfStatement : Statement
{
    public Expression Condition { get; set; } = null!;
    public List<Statement> ThenStatements { get; set; } = [];
    public List<IfBranch> ElsifBranches { get; set; } = [];
    public List<Statement>? ElseStatements { get; set; }
}

public sealed partial class IfBranch : SqlNode
{
    public Expression Condition { get; set; } = null!;
    public List<Statement> Statements { get; set; } = [];
}

public sealed partial class PlsqlCaseStatement : Statement
{
    public Expression? Operand { get; set; }
    public List<CaseStatementWhen> Whens { get; set; } = [];
    public List<Statement>? ElseStatements { get; set; }
}

public sealed partial class CaseStatementWhen : SqlNode
{
    public Expression When { get; set; } = null!;
    public List<Statement> Statements { get; set; } = [];
}

public enum LoopKind { Basic, While, For }

public sealed partial class LoopStatement : Statement
{
    public LoopKind Kind { get; set; }
    /// <summary>WHILE condition (WHILE loops only).</summary>
    public Expression? Condition { get; set; }
    /// <summary>FOR loops: one name, or two for FOR key, value IN PAIRS OF ...</summary>
    public List<ForIterand> Iterands { get; set; } = [];
    /// <summary>FOR loops: the comma-separated iteration controls after IN.</summary>
    public List<IterationControl> Controls { get; set; } = [];
    public List<Statement> Body { get; set; } = [];
    public Identifier? EndLabel { get; set; }
}

/// <summary>A FOR loop variable: name, optional type (FOR i NUMBER(5,1) IN ...), and MUTABLE.</summary>
public sealed partial class ForIterand : SqlNode
{
    public Identifier Name { get; set; } = null!;
    public DataType? Type { get; set; }
    public bool Mutable { get; set; }
}

/// <summary>
/// One comma-separated item of a FOR loop's iteration controls (Oracle 21c+). WHILE and WHEN are optional
/// predicates that end the loop or skip the current value.
/// </summary>
public abstract partial class IterationControl : SqlNode
{
    public Expression? While { get; set; }
    public Expression? When { get; set; }
}

/// <summary>[REVERSE] lower .. upper [BY step].</summary>
public sealed partial class RangeControl : IterationControl
{
    public bool Reverse { get; set; }
    public Expression Lower { get; set; } = null!;
    public Expression Upper { get; set; } = null!;
    public Expression? By { get; set; }
}

/// <summary>A single value, or a bare cursor or collection name (FOR r IN c1 LOOP).</summary>
public sealed partial class ExpressionControl : IterationControl
{
    public Expression Value { get; set; } = null!;
}

/// <summary>REPEAT expr: repeatedly evaluates expr, using the previous value of the loop variable.</summary>
public sealed partial class RepeatControl : IterationControl
{
    public Expression Value { get; set; } = null!;
}

/// <summary>VALUES OF collection: iterates over the collection's values.</summary>
public sealed partial class ValuesOfControl : IterationControl
{
    public Expression Collection { get; set; } = null!;
}

/// <summary>INDICES OF collection: iterates over the collection's index values.</summary>
public sealed partial class IndicesOfControl : IterationControl
{
    public Expression Collection { get; set; } = null!;
}

/// <summary>PAIRS OF collection: iterates over index and value together (two iterands).</summary>
public sealed partial class PairsOfControl : IterationControl
{
    public Expression Collection { get; set; } = null!;
}

/// <summary>FOR r IN (SELECT ...): cursor FOR loop over a query.</summary>
public sealed partial class CursorControl : IterationControl
{
    public SelectStatement Query { get; set; } = null!;
}

/// <summary>EXECUTE IMMEDIATE sql [USING ...]: iterates over the rows of a dynamic query.</summary>
public sealed partial class DynamicSqlControl : IterationControl
{
    public Expression Sql { get; set; } = null!;
    public List<UsingArgument> Using { get; set; } = [];
}

public sealed partial class ExitStatement : Statement
{
    public Identifier? Label { get; set; }
    public Expression? When { get; set; }
}

public sealed partial class ContinueStatement : Statement
{
    public Identifier? Label { get; set; }
    public Expression? When { get; set; }
}

public sealed partial class GotoStatement : Statement
{
    public Identifier Target { get; set; } = null!;
}

public sealed partial class ReturnStatement : Statement
{
    public Expression? Value { get; set; }
}

public sealed partial class RaiseStatement : Statement
{
    public List<Identifier> Exception { get; set; } = [];
}

public sealed partial class UsingArgument : SqlNode
{
    public ParamMode? Mode { get; set; }
    public Expression Value { get; set; } = null!;
}

public sealed partial class OpenStatement : Statement
{
    public Expression Cursor { get; set; } = null!;
    public SelectStatement? ForQuery { get; set; }
    public Expression? ForExpression { get; set; }
    public List<UsingArgument> Using { get; set; } = [];
}

public sealed partial class FetchStatement : Statement
{
    public Expression Cursor { get; set; } = null!;
    public bool BulkCollect { get; set; }
    public List<Expression> Into { get; set; } = [];
    public Expression? Limit { get; set; }
}

public sealed partial class CloseStatement : Statement
{
    public Expression Cursor { get; set; } = null!;
}

public sealed partial class ExecuteImmediateStatement : Statement
{
    public Expression Sql { get; set; } = null!;
    public bool BulkCollect { get; set; }
    public List<Expression> Into { get; set; } = [];
    public List<UsingArgument> Using { get; set; } = [];
    public List<Expression> ReturningInto { get; set; } = [];
    public bool ReturningBulkCollect { get; set; }
}

public sealed partial class ForallStatement : Statement
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

public sealed partial class PipeRowStatement : Statement
{
    public Expression Value { get; set; } = null!;
}

public enum JavaObjectKind { Source, Class, Resource }
public enum JavaUsingKind { Bfile, Clob, Blob, Key }

/// <summary>CREATE JAVA SOURCE/CLASS/RESOURCE. SourceText is the embedded Java program (not SQL).</summary>
public sealed partial class CreateJavaStatement : Statement
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

public sealed partial class CreateLibraryStatement : Statement
{
    public bool OrReplace { get; set; }
    public EditionableKind? Editionable { get; set; }
    public SharingKind? Sharing { get; set; }
    public List<Identifier> Name { get; set; } = [];
    public Expression Path { get; set; } = null!;
    public Expression? Agent { get; set; }
    public Identifier? Credential { get; set; }
}
