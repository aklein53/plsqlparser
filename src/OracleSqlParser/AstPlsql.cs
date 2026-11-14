namespace OracleSqlParser;

// ---------- Blocks ----------

/// <summary>An anonymous or nested PL/SQL block: <c>[DECLARE ...] BEGIN ... [EXCEPTION ...] END [name]</c>.</summary>
public sealed partial class Block : Statement
{
    /// <summary>True when the block starts with <c>DECLARE</c>, even when it declares nothing.</summary>
    public bool HasDeclare { get; set; }
    /// <summary>The declarations between <c>DECLARE</c> and <c>BEGIN</c>, in source order; empty when absent.</summary>
    public List<Declaration> Declarations { get; set; } = [];
    /// <summary>The statements between <c>BEGIN</c> and <c>EXCEPTION</c> or <c>END</c>, in source order.</summary>
    public List<Statement> Statements { get; set; } = [];
    /// <summary>The exception handlers, in source order; empty when there is no <c>EXCEPTION</c> part.</summary>
    public List<ExceptionHandler> Handlers { get; set; } = [];
    /// <summary>The label or name written after <c>END</c>, or null when absent.</summary>
    public Identifier? EndName { get; set; }
}

/// <summary>One <c>WHEN ... THEN</c> handler of an <c>EXCEPTION</c> section, such as <c>WHEN NO_DATA_FOUND THEN NULL</c>.</summary>
public sealed partial class ExceptionHandler : SqlNode
{
    /// <summary>The exception names of the handler, one entry per name separated by <c>OR</c> (each entry a dotted name); empty when the handler is <c>OTHERS</c>.</summary>
    public List<List<Identifier>> Exceptions { get; set; } = [];
    /// <summary>True for <c>WHEN OTHERS</c>.</summary>
    public bool Others { get; set; }
    /// <summary>The statements run by the handler, in source order.</summary>
    public List<Statement> Statements { get; set; } = [];
}

// ---------- Conditional compilation ($IF, $ERROR, $$inquiry directives) ----------

/// <summary>$IF cond $THEN ... [$ELSIF cond $THEN ...] [$ELSE ...] $END where the branches hold statements.</summary>
public sealed partial class ConditionalStatement : Statement
{
    /// <summary>The compile-time condition after <c>$IF</c>.</summary>
    public required Expression Condition { get; set; }
    /// <summary>The statements of the <c>$THEN</c> part, in source order.</summary>
    public List<Statement> ThenStatements { get; set; } = [];
    /// <summary>The <c>$ELSIF</c> branches, in source order; empty when absent.</summary>
    public List<ConditionalStatementBranch> ElsifBranches { get; set; } = [];
    /// <summary>The statements of the <c>$ELSE</c> part, or null when absent.</summary>
    public List<Statement>? ElseStatements { get; set; }
}

/// <summary>One <c>$ELSIF</c> branch of a conditional compilation statement.</summary>
public sealed partial class ConditionalStatementBranch : SqlNode
{
    /// <summary>The compile-time condition of the branch.</summary>
    public required Expression Condition { get; set; }
    /// <summary>The statements of the branch, in source order.</summary>
    public List<Statement> Statements { get; set; } = [];
}

/// <summary>$ERROR 'message' $END in a statement list: a compile-time error, raised when its branch is selected.</summary>
public sealed partial class CompilationErrorStatement : Statement
{
    /// <summary>The message expression after <c>$ERROR</c>.</summary>
    public required Expression Message { get; set; }
}

/// <summary>$IF cond $THEN ... [$ELSIF cond $THEN ...] [$ELSE ...] $END where the branches hold declarations.</summary>
public sealed partial class ConditionalDeclaration : Declaration
{
    /// <summary>The compile-time condition after <c>$IF</c>.</summary>
    public required Expression Condition { get; set; }
    /// <summary>The declarations of the <c>$THEN</c> part, in source order.</summary>
    public List<Declaration> ThenDeclarations { get; set; } = [];
    /// <summary>The <c>$ELSIF</c> branches, in source order; empty when absent.</summary>
    public List<ConditionalDeclarationBranch> ElsifBranches { get; set; } = [];
    /// <summary>The declarations of the <c>$ELSE</c> part, or null when absent.</summary>
    public List<Declaration>? ElseDeclarations { get; set; }
}

/// <summary>One <c>$ELSIF</c> branch of a conditional compilation declaration block.</summary>
public sealed partial class ConditionalDeclarationBranch : SqlNode
{
    /// <summary>The compile-time condition of the branch.</summary>
    public required Expression Condition { get; set; }
    /// <summary>The declarations of the branch, in source order.</summary>
    public List<Declaration> Declarations { get; set; } = [];
}

/// <summary>$ERROR 'message' $END in a declaration list: a compile-time error, raised when its branch is selected.</summary>
public sealed partial class CompilationErrorDeclaration : Declaration
{
    /// <summary>The message expression after <c>$ERROR</c>.</summary>
    public required Expression Message { get; set; }
}

/// <summary>Inquiry directive such as $$PLSQL_UNIT or a $$flag set with PLSQL_CCFLAGS.</summary>
public sealed partial class InquiryDirective : Expression
{
    /// <summary>The directive name without the leading dollar signs.</summary>
    public required Identifier Name { get; set; }
}

// ---------- Declarations ----------

/// <summary>A PL/SQL declaration: a variable, constant, cursor, type, subprogram or pragma.</summary>
public abstract partial class Declaration : SqlNode { }

/// <summary>A variable or constant declaration, such as <c>n CONSTANT NUMBER(10) NOT NULL := 1</c>.</summary>
public sealed partial class VariableDeclaration : Declaration
{
    /// <summary>The variable name.</summary>
    public required Identifier Name { get; set; }
    /// <summary>True for <c>CONSTANT</c>.</summary>
    public bool Constant { get; set; }
    /// <summary>The declared type.</summary>
    public required DataType Type { get; set; }
    /// <summary>True for <c>NOT NULL</c>.</summary>
    public bool NotNull { get; set; }
    /// <summary>The initial value after <c>:=</c> or <c>DEFAULT</c>, or null when absent.</summary>
    public Expression? Default { get; set; }
}

/// <summary>$IF cond $THEN ... [$ELSIF cond $THEN ...] [$ELSE ...] $END around parameters (the commas are inside the branches).</summary>
public sealed partial class ConditionalParameter : ParameterItem
{
    /// <summary>The compile-time condition after <c>$IF</c>.</summary>
    public required Expression Condition { get; set; }
    /// <summary>The parameters of the <c>$THEN</c> part, in source order.</summary>
    public List<ParameterItem> ThenParameters { get; set; } = [];
    /// <summary>The <c>$ELSIF</c> branches, in source order; empty when absent.</summary>
    public List<ConditionalParameterBranch> ElsifBranches { get; set; } = [];
    /// <summary>The parameters of the <c>$ELSE</c> part, or null when absent.</summary>
    public List<ParameterItem>? ElseParameters { get; set; }
}

/// <summary>One <c>$ELSIF</c> branch of a conditional compilation parameter block.</summary>
public sealed partial class ConditionalParameterBranch : SqlNode
{
    /// <summary>The compile-time condition of the branch.</summary>
    public required Expression Condition { get; set; }
    /// <summary>The parameters of the branch, in source order.</summary>
    public List<ParameterItem> Parameters { get; set; } = [];
}

/// <summary>A user-defined exception declaration, such as <c>e_limit EXCEPTION;</c>.</summary>
public sealed partial class ExceptionDeclaration : Declaration
{
    /// <summary>The exception name.</summary>
    public required Identifier Name { get; set; }
}

/// <summary>A <c>PRAGMA</c> declaration, such as <c>PRAGMA EXCEPTION_INIT(e_limit, -20001);</c>.</summary>
public sealed partial class PragmaDeclaration : Declaration
{
    /// <summary>The pragma name, such as <c>EXCEPTION_INIT</c>.</summary>
    public required Identifier Name { get; set; }
    /// <summary>The arguments in parentheses, in source order; empty when there are none.</summary>
    public List<Expression> Arguments { get; set; } = [];
}

/// <summary>The passing mode of a parameter.</summary>
public enum ParamMode
{
    /// <summary><c>IN</c>: passed into the subprogram.</summary>
    In,
    /// <summary><c>OUT</c>: returned from the subprogram.</summary>
    Out,
    /// <summary><c>IN OUT</c>: passed in and returned.</summary>
    InOut,
}

/// <summary>An entry in a parameter list: a plain parameter or a conditional group (see <see cref="ConditionalParameter"/>).</summary>
public abstract partial class ParameterItem : SqlNode
{
}

/// <summary>A formal parameter of a subprogram, such as <c>p_id IN NUMBER DEFAULT 0</c>.</summary>
public sealed partial class Parameter : ParameterItem
{
    /// <summary>The parameter name.</summary>
    public required Identifier Name { get; set; }
    /// <summary>The passing mode, or null when the default <c>IN</c> applies.</summary>
    public ParamMode? Mode { get; set; }
    /// <summary>True for <c>NOCOPY</c>.</summary>
    public bool NoCopy { get; set; }
    /// <summary>The declared type, or null when the parameter has no type of its own.</summary>
    public DataType? Type { get; set; }
    /// <summary>The default value after <c>DEFAULT</c> or <c>:=</c>, or null when absent.</summary>
    public Expression? Default { get; set; }
}

/// <summary>A cursor declaration, such as <c>CURSOR c (p NUMBER) RETURN t%ROWTYPE IS SELECT ...</c>.</summary>
public sealed partial class CursorDeclaration : Declaration
{
    /// <summary>The cursor name.</summary>
    public required Identifier Name { get; set; }
    /// <summary>The cursor parameters in parentheses, in source order; empty when absent.</summary>
    public List<ParameterItem> Parameters { get; set; } = [];
    /// <summary>The type after <c>RETURN</c>, or null when absent.</summary>
    public DataType? ReturnType { get; set; }
    /// <summary>The query after <c>IS</c>, or null for a cursor declared without its query.</summary>
    public SelectStatement? Query { get; set; }
}

/// <summary>A <c>TYPE ... IS RECORD</c> declaration, such as <c>TYPE r IS RECORD (a NUMBER, b VARCHAR2(10))</c>.</summary>
public sealed partial class RecordTypeDeclaration : Declaration
{
    /// <summary>The record type name.</summary>
    public required Identifier Name { get; set; }
    /// <summary>The fields, in source order.</summary>
    public List<VariableDeclaration> Fields { get; set; } = [];
}

/// <summary>The collection form of a PL/SQL collection type.</summary>
public enum CollectionKind
{
    /// <summary><c>TABLE OF</c>: a nested table or associative array.</summary>
    Table,
    /// <summary><c>VARRAY</c> or <c>VARYING ARRAY</c>.</summary>
    Varray,
}

/// <summary>A collection type, such as <c>TYPE t IS TABLE OF NUMBER INDEX BY PLS_INTEGER</c>.</summary>
public sealed partial class CollectionTypeDeclaration : Declaration
{
    /// <summary>The collection type name.</summary>
    public required Identifier Name { get; set; }
    /// <summary>Whether the collection is a <c>TABLE</c> or a <c>VARRAY</c>.</summary>
    public CollectionKind Kind { get; set; }
    /// <summary>The size limit of a <c>VARRAY</c>, or null for other collections.</summary>
    public Expression? Limit { get; set; }
    /// <summary>The element type after <c>OF</c>.</summary>
    public required DataType ElementType { get; set; }
    /// <summary>True when <c>NOT NULL</c> follows the element type.</summary>
    public bool NotNull { get; set; }
    /// <summary>The key type after <c>INDEX BY</c>, or null when absent.</summary>
    public DataType? IndexBy { get; set; }
}

/// <summary>A <c>REF CURSOR</c> type declaration, such as <c>TYPE rc IS REF CURSOR RETURN t%ROWTYPE</c>.</summary>
public sealed partial class RefCursorTypeDeclaration : Declaration
{
    /// <summary>The ref cursor type name.</summary>
    public required Identifier Name { get; set; }
    /// <summary>The type after <c>RETURN</c>, or null for a weak ref cursor.</summary>
    public DataType? ReturnType { get; set; }
}

/// <summary>A subtype declaration, such as <c>SUBTYPE small IS PLS_INTEGER RANGE 1 .. 10</c>.</summary>
public sealed partial class SubtypeDeclaration : Declaration
{
    /// <summary>The subtype name.</summary>
    public required Identifier Name { get; set; }
    /// <summary>The base type after <c>IS</c>.</summary>
    public required DataType BaseType { get; set; }
    /// <summary>The lower bound of <c>RANGE</c>, or null when absent.</summary>
    public Expression? RangeLow { get; set; }
    /// <summary>The upper bound of <c>RANGE</c>, or null when absent.</summary>
    public Expression? RangeHigh { get; set; }
    /// <summary>True for <c>NOT NULL</c>.</summary>
    public bool NotNull { get; set; }
}

/// <summary>Whether an object is editionable.</summary>
public enum EditionableKind
{
    /// <summary><c>EDITIONABLE</c>.</summary>
    Editionable,
    /// <summary><c>NONEDITIONABLE</c>.</summary>
    NonEditionable,
}
/// <summary>The sharing mode of an object in a container database.</summary>
public enum SharingKind
{
    /// <summary><c>SHARING = METADATA</c>.</summary>
    Metadata,
    /// <summary><c>SHARING = DATA</c>.</summary>
    Data,
    /// <summary><c>SHARING = EXTENDED DATA</c>.</summary>
    Extended,
    /// <summary><c>SHARING = NONE</c>.</summary>
    None,
}
/// <summary>The invoker or definer rights of a unit.</summary>
public enum AuthidKind
{
    /// <summary><c>AUTHID CURRENT_USER</c>: runs with the rights of the caller.</summary>
    CurrentUser,
    /// <summary><c>AUTHID DEFINER</c>: runs with the rights of the owner.</summary>
    Definer,
}
/// <summary>The kind of a SQL macro.</summary>
public enum MacroKind
{
    /// <summary><c>SQL_MACRO(SCALAR)</c>.</summary>
    Scalar,
    /// <summary><c>SQL_MACRO(TABLE)</c>.</summary>
    Table,
}
/// <summary>The partitioning method of a <c>PARALLEL_ENABLE</c> clause.</summary>
public enum ParallelPartitionMethod
{
    /// <summary><c>PARTITION BY ANY</c>.</summary>
    Any,
    /// <summary><c>PARTITION BY HASH</c>.</summary>
    Hash,
    /// <summary><c>PARTITION BY RANGE</c>.</summary>
    Range,
    /// <summary><c>PARTITION BY VALUE</c>.</summary>
    Value,
}
/// <summary>The kind of unit named in an <c>ACCESSIBLE BY</c> clause.</summary>
public enum AccessorKind
{
    /// <summary><c>FUNCTION</c>.</summary>
    Function,
    /// <summary><c>PROCEDURE</c>.</summary>
    Procedure,
    /// <summary><c>PACKAGE</c>.</summary>
    Package,
    /// <summary><c>TRIGGER</c>.</summary>
    Trigger,
    /// <summary><c>TYPE</c>.</summary>
    Type,
}

/// <summary>The modifiers of an object type member, such as <c>MEMBER</c>, <c>STATIC</c> or <c>NOT FINAL</c>.</summary>
[Flags]
public enum MemberModifiers
{
    /// <summary>No modifier.</summary>
    None = 0,
    /// <summary><c>MEMBER</c>.</summary>
    Member = 1,
    /// <summary><c>STATIC</c>.</summary>
    Static = 2,
    /// <summary><c>CONSTRUCTOR</c>.</summary>
    Constructor = 4,
    /// <summary><c>MAP</c>.</summary>
    Map = 8,
    /// <summary><c>ORDER</c>.</summary>
    Order = 16,
    /// <summary><c>FINAL</c>.</summary>
    Final = 32,
    /// <summary><c>NOT FINAL</c>.</summary>
    NotFinal = 64,
    /// <summary><c>INSTANTIABLE</c>.</summary>
    Instantiable = 128,
    /// <summary><c>NOT INSTANTIABLE</c>.</summary>
    NotInstantiable = 256,
    /// <summary><c>OVERRIDING</c>.</summary>
    Overriding = 512,
}

/// <summary>A <c>PARALLEL_ENABLE</c> clause of a function, such as <c>PARALLEL_ENABLE (PARTITION p BY HASH (c))</c>.</summary>
public sealed partial class ParallelEnableClause : SqlNode
{
    /// <summary>The argument named after <c>PARTITION</c>, or null when absent.</summary>
    public Identifier? PartitionArgument { get; set; }
    /// <summary>The partitioning method, or null when absent.</summary>
    public ParallelPartitionMethod? Method { get; set; }
    /// <summary>The columns after <c>BY</c> in the partitioning method; empty when absent.</summary>
    public List<Identifier> MethodColumns { get; set; } = [];
    /// <summary>True for the <c>CLUSTER</c> streaming clause (<c>CLUSTER expr BY (cols)</c>).</summary>
    public bool Cluster { get; set; }
    /// <summary>True for the <c>ORDER</c> streaming clause (<c>ORDER expr BY (cols)</c>).</summary>
    public bool Order { get; set; }
    /// <summary>The columns of a <c>CLUSTER ... BY</c> or <c>ORDER ... BY</c> clause; empty when absent.</summary>
    public List<Identifier> StreamingColumns { get; set; } = [];
}

/// <summary>One unit of an <c>ACCESSIBLE BY</c> clause, such as <c>FUNCTION f</c>.</summary>
public sealed partial class Accessor : SqlNode
{
    /// <summary>The kind of unit, or null when the kind is omitted.</summary>
    public AccessorKind? Kind { get; set; }
    /// <summary>The unit name parts, schema first when qualified.</summary>
    public List<Identifier> Name { get; set; } = [];
}

/// <summary>Clauses between a program header and IS/AS (also package and type headers).</summary>
public sealed partial class ProgramOptions : SqlNode
{
    /// <summary>The <c>AUTHID</c> setting, or null when absent.</summary>
    public AuthidKind? Authid { get; set; }
    /// <summary>The <c>SHARING</c> setting, or null when absent.</summary>
    public SharingKind? Sharing { get; set; }
    /// <summary>True for <c>DETERMINISTIC</c>.</summary>
    public bool Deterministic { get; set; }
    /// <summary>True for <c>PIPELINED</c>.</summary>
    public bool Pipelined { get; set; }
    /// <summary>The implementation type named after <c>PIPELINED USING</c>; empty when absent.</summary>
    public List<Identifier> PipelinedUsing { get; set; } = [];
    /// <summary>The <c>PARALLEL_ENABLE</c> clause, or null when absent.</summary>
    public ParallelEnableClause? ParallelEnable { get; set; }
    /// <summary>True for <c>RESULT_CACHE</c>.</summary>
    public bool ResultCache { get; set; }
    /// <summary>The <c>RELIES_ON</c> dependencies, one name list per entry; empty when absent.</summary>
    public List<List<Identifier>> RelyOn { get; set; } = [];
    /// <summary>The implementation type named after <c>AGGREGATE USING</c>; empty when absent.</summary>
    public List<Identifier> AggregateUsing { get; set; } = [];
    /// <summary>The <c>SQL_MACRO</c> kind, or null when absent.</summary>
    public MacroKind? SqlMacro { get; set; }
    /// <summary>The units named in <c>ACCESSIBLE BY</c>, in source order; empty when absent.</summary>
    public List<Accessor> AccessibleBy { get; set; } = [];
    /// <summary>True for <c>DEPRECATE</c>.</summary>
    public bool Deprecate { get; set; }
    /// <summary>The message after <c>DEPRECATE</c>, or null when absent.</summary>
    public Expression? DeprecateMessage { get; set; }
    /// <summary>The collation after <c>DEFAULT COLLATION</c>, or null when absent.</summary>
    public Identifier? DefaultCollation { get; set; }
}

/// <summary>The language of an external routine.</summary>
public enum ExternalLanguage
{
    /// <summary><c>LANGUAGE JAVA</c>.</summary>
    Java,
    /// <summary><c>LANGUAGE C</c>.</summary>
    C,
    /// <summary><c>LANGUAGE JAVASCRIPT</c>.</summary>
    JavaScript,
}
/// <summary>The kind of a parameter in an external call specification.</summary>
public enum ExternalParameterKind
{
    /// <summary><c>CONTEXT</c>: passes the external procedure context.</summary>
    Context,
    /// <summary><c>SELF</c>: passes the object instance.</summary>
    Self,
    /// <summary><c>RETURN</c>: passes the return value.</summary>
    Return,
    /// <summary>A named parameter.</summary>
    Named,
}
/// <summary>A property of an external parameter, such as <c>INDICATOR</c> or <c>LENGTH</c>.</summary>
public enum ExternalProperty
{
    /// <summary><c>INDICATOR</c>.</summary>
    Indicator,
    /// <summary><c>INDICATOR STRUCT</c>.</summary>
    IndicatorStruct,
    /// <summary><c>INDICATOR TDO</c>.</summary>
    IndicatorTdo,
    /// <summary><c>LENGTH</c>.</summary>
    Length,
    /// <summary><c>MAXLEN</c>.</summary>
    MaxLen,
    /// <summary><c>TDO</c>.</summary>
    Tdo,
    /// <summary><c>CHARSETID</c>.</summary>
    CharsetId,
    /// <summary><c>CHARSETFORM</c>.</summary>
    CharsetForm,
}

/// <summary>One parameter of an external call specification, such as <c>p BY REFERENCE INDICATOR</c>.</summary>
public sealed partial class ExternalParameter : SqlNode
{
    /// <summary>The kind of parameter.</summary>
    public ExternalParameterKind Kind { get; set; }
    /// <summary>The parameter name, or null for the special kinds.</summary>
    public Identifier? Name { get; set; }
    /// <summary>The property of the parameter, such as <c>LENGTH</c>, or null when absent.</summary>
    public ExternalProperty? Property { get; set; }
    /// <summary>True for <c>BY REFERENCE</c>.</summary>
    public bool ByReference { get; set; }
    /// <summary>The SQL type of the parameter, or null when absent.</summary>
    public DataType? Type { get; set; }
}

/// <summary>LANGUAGE JAVA/C/JAVASCRIPT call specification.</summary>
public sealed partial class ExternalCall : SqlNode
{
    /// <summary>The language of the external routine.</summary>
    public ExternalLanguage Language { get; set; }
    /// <summary>The external name, or null when absent.</summary>
    public Expression? Name { get; set; }
    /// <summary>The library name after <c>LIBRARY</c>, or null when absent.</summary>
    public Identifier? Library { get; set; }
    /// <summary>The names after <c>AGENT IN</c>; empty when absent.</summary>
    public List<Identifier> AgentIn { get; set; } = [];
    /// <summary>True for <c>WITH CONTEXT</c>.</summary>
    public bool WithContext { get; set; }
    /// <summary>True for the <c>PASCAL</c> calling convention.</summary>
    public bool Pascal { get; set; }
    /// <summary>The parameters of the external call specification, in source order.</summary>
    public List<ExternalParameter> Parameters { get; set; } = [];
    /// <summary>The module name after <c>MLE MODULE</c> (LANGUAGE JAVASCRIPT); empty when absent.</summary>
    public List<Identifier> MleModule { get; set; } = [];
    /// <summary>The environment name after <c>MLE ENV</c>; empty when absent.</summary>
    public List<Identifier> MleEnv { get; set; } = [];
    /// <summary>The string after <c>SIGNATURE</c> in an MLE call specification, or null when absent.</summary>
    public Expression? Signature { get; set; }
}

/// <summary>Procedure or function: nested declaration, package member, type member or standalone (via CreateProgramStatement).</summary>
public sealed partial class SubprogramDeclaration : Declaration
{
    /// <summary>True for a function; false for a procedure.</summary>
    public bool IsFunction { get; set; }
    /// <summary>The object type member modifiers; <c>None</c> for a plain subprogram.</summary>
    public MemberModifiers Modifiers { get; set; }
    /// <summary>The subprogram name parts, schema first when qualified.</summary>
    public List<Identifier> Name { get; set; } = [];
    /// <summary>The formal parameters in parentheses, in source order; empty when absent.</summary>
    public List<ParameterItem> Parameters { get; set; } = [];
    /// <summary>The type after <c>RETURN</c>, or null for a procedure.</summary>
    public DataType? ReturnType { get; set; }
    /// <summary>CONSTRUCTOR FUNCTION ... RETURN SELF AS RESULT.</summary>
    public bool ReturnSelfAsResult { get; set; }
    /// <summary>The subprogram options, such as <c>AUTHID</c> or <c>DETERMINISTIC</c>, or null when absent.</summary>
    public ProgramOptions? Options { get; set; }
    /// <summary>The body after <c>IS</c> or <c>AS</c>, or null for a declaration without a body.</summary>
    public Block? Body { get; set; }
    /// <summary>The external call specification after <c>LANGUAGE</c>, or null when absent.</summary>
    public ExternalCall? External { get; set; }
}

// ---------- Program units ----------

/// <summary><c>CREATE [OR REPLACE] PROCEDURE</c> or <c>FUNCTION</c> as a standalone statement.</summary>
public sealed partial class CreateProgramStatement : Statement
{
    /// <summary>True when <c>OR REPLACE</c> is written.</summary>
    public bool OrReplace { get; set; }
    /// <summary>The <c>EDITIONABLE</c> setting, or null when absent.</summary>
    public EditionableKind? Editionable { get; set; }
    /// <summary>The procedure or function being created.</summary>
    public required SubprogramDeclaration Subprogram { get; set; }
}

/// <summary><c>CREATE [OR REPLACE] PACKAGE</c>: the package specification.</summary>
public sealed partial class CreatePackageStatement : Statement
{
    /// <summary>True when <c>OR REPLACE</c> is written.</summary>
    public bool OrReplace { get; set; }
    /// <summary>The <c>EDITIONABLE</c> setting, or null when absent.</summary>
    public EditionableKind? Editionable { get; set; }
    /// <summary>The package name parts, schema first when qualified.</summary>
    public List<Identifier> Name { get; set; } = [];
    /// <summary>The package options, such as <c>AUTHID</c>, or null when absent.</summary>
    public ProgramOptions? Options { get; set; }
    /// <summary>The declarations in the specification, in source order.</summary>
    public List<Declaration> Declarations { get; set; } = [];
    /// <summary>The name written after <c>END</c>, or null when absent.</summary>
    public Identifier? EndName { get; set; }
}

/// <summary><c>CREATE [OR REPLACE] PACKAGE BODY</c>: the package implementation.</summary>
public sealed partial class CreatePackageBodyStatement : Statement
{
    /// <summary>True when <c>OR REPLACE</c> is written.</summary>
    public bool OrReplace { get; set; }
    /// <summary>The <c>EDITIONABLE</c> setting, or null when absent.</summary>
    public EditionableKind? Editionable { get; set; }
    /// <summary>The package name parts, schema first when qualified.</summary>
    public List<Identifier> Name { get; set; } = [];
    /// <summary>The declarations and subprogram bodies, in source order.</summary>
    public List<Declaration> Declarations { get; set; } = [];
    /// <summary>The statements of the initialization section, in source order; empty when absent.</summary>
    public List<Statement> InitStatements { get; set; } = [];
    /// <summary>The exception handlers of the initialization section; empty when absent.</summary>
    public List<ExceptionHandler> InitHandlers { get; set; } = [];
    /// <summary>The name written after <c>END</c>, or null when absent.</summary>
    public Identifier? EndName { get; set; }
}

/// <summary>When a trigger fires relative to the triggering event.</summary>
public enum TriggerTiming
{
    /// <summary><c>BEFORE</c>.</summary>
    Before,
    /// <summary><c>AFTER</c>.</summary>
    After,
    /// <summary><c>INSTEAD OF</c>: fires in place of the event on a view.</summary>
    InsteadOf,
    /// <summary><c>FOR</c> (as in <c>FOR INSERT ON t COMPOUND TRIGGER</c>): the timing of a compound trigger, which has its own timing points.</summary>
    For,
}
/// <summary>The event that fires a trigger.</summary>
public enum TriggerEventKind
{
    /// <summary><c>INSERT</c>.</summary>
    Insert,
    /// <summary><c>UPDATE</c>.</summary>
    Update,
    /// <summary><c>DELETE</c>.</summary>
    Delete,
    /// <summary>A DDL event, such as <c>CREATE</c>.</summary>
    Ddl,
    /// <summary>A database event, such as <c>LOGON</c> or <c>STARTUP</c>.</summary>
    Database,
}
/// <summary>The kind of object a trigger is defined on.</summary>
public enum TriggerTargetKind
{
    /// <summary><c>ON TABLE</c>, or the default target.</summary>
    Table,
    /// <summary><c>ON VIEW</c>.</summary>
    View,
    /// <summary><c>ON SCHEMA</c>.</summary>
    Schema,
    /// <summary><c>ON DATABASE</c>.</summary>
    Database,
    /// <summary><c>ON PLUGGABLE DATABASE</c>.</summary>
    PluggableDatabase,
    /// <summary><c>ON NESTED TABLE</c>.</summary>
    NestedTable,
}
/// <summary>The row or table alias a trigger declares with <c>REFERENCING</c>.</summary>
public enum ReferencingKind
{
    /// <summary><c>NEW</c>.</summary>
    New,
    /// <summary><c>OLD</c>.</summary>
    Old,
    /// <summary><c>PARENT</c>.</summary>
    Parent,
}
/// <summary>The ordering of a trigger relative to other triggers.</summary>
public enum TriggerOrdering
{
    /// <summary><c>FOLLOWS</c>.</summary>
    Follows,
    /// <summary><c>PRECEDES</c>.</summary>
    Precedes,
}
/// <summary>The cross-edition direction of a trigger.</summary>
public enum CrossEdition
{
    /// <summary><c>FORWARD CROSSEDITION</c>.</summary>
    Forward,
    /// <summary><c>REVERSE CROSSEDITION</c>.</summary>
    Reverse,
}
/// <summary>A timing point of a compound trigger.</summary>
public enum TimingPoint
{
    /// <summary><c>BEFORE STATEMENT</c>.</summary>
    BeforeStatement,
    /// <summary><c>BEFORE EACH ROW</c>.</summary>
    BeforeEachRow,
    /// <summary><c>AFTER STATEMENT</c>.</summary>
    AfterStatement,
    /// <summary><c>AFTER EACH ROW</c>.</summary>
    AfterEachRow,
    /// <summary><c>INSTEAD OF EACH ROW</c>.</summary>
    InsteadOfEachRow,
}

/// <summary>One triggering event, such as <c>INSERT</c> or <c>UPDATE OF salary</c>.</summary>
public sealed partial class TriggerEvent : SqlNode
{
    /// <summary>The kind of event.</summary>
    public TriggerEventKind Kind { get; set; }
    /// <summary>UPDATE OF columns.</summary>
    public List<Identifier> Columns { get; set; } = [];
    /// <summary>DDL/database event keyword (CREATE, ALTER, DROP, LOGON, SERVERERROR, STARTUP, ...).</summary>
    public string? EventName { get; set; }
}

/// <summary>One alias of a <c>REFERENCING</c> clause, such as <c>OLD AS o</c>.</summary>
public sealed partial class ReferencingItem : SqlNode
{
    /// <summary>Which row or table the alias names.</summary>
    public ReferencingKind Kind { get; set; }
    /// <summary>The alias name.</summary>
    public required Identifier Alias { get; set; }
}

/// <summary><c>CREATE [OR REPLACE] TRIGGER</c>, simple or compound.</summary>
public sealed partial class CreateTriggerStatement : Statement
{
    /// <summary>True when <c>OR REPLACE</c> is written.</summary>
    public bool OrReplace { get; set; }
    /// <summary>The <c>EDITIONABLE</c> setting, or null when absent.</summary>
    public EditionableKind? Editionable { get; set; }
    /// <summary>The <c>SHARING</c> setting, or null when absent.</summary>
    public SharingKind? Sharing { get; set; }
    /// <summary>The trigger name parts, schema first when qualified.</summary>
    public List<Identifier> Name { get; set; } = [];
    /// <summary>When the trigger fires: <c>BEFORE</c>, <c>AFTER</c>, <c>INSTEAD OF</c> or <c>FOR</c>.</summary>
    public TriggerTiming Timing { get; set; }
    /// <summary>The triggering events, in source order.</summary>
    public List<TriggerEvent> Events { get; set; } = [];
    /// <summary>The kind of object the trigger is defined on.</summary>
    public TriggerTargetKind TargetKind { get; set; }
    /// <summary>The target object name parts; empty for database and schema triggers.</summary>
    public List<Identifier> Target { get; set; } = [];
    /// <summary>ON NESTED TABLE column OF view.</summary>
    public Identifier? NestedTableColumn { get; set; }
    /// <summary>The <c>REFERENCING</c> aliases, in source order; empty when absent.</summary>
    public List<ReferencingItem> Referencing { get; set; } = [];
    /// <summary>True for <c>FOR EACH ROW</c>.</summary>
    public bool ForEachRow { get; set; }
    /// <summary>The cross-edition direction, or null when absent.</summary>
    public CrossEdition? CrossEdition { get; set; }
    /// <summary>The <c>FOLLOWS</c> or <c>PRECEDES</c> setting, or null when absent.</summary>
    public TriggerOrdering? Ordering { get; set; }
    /// <summary>The triggers named after <c>FOLLOWS</c> or <c>PRECEDES</c>, one name list each; empty when absent.</summary>
    public List<List<Identifier>> OrderingTriggers { get; set; } = [];
    /// <summary>True for <c>ENABLE</c>, false for <c>DISABLE</c>, or null when neither is written.</summary>
    public bool? Enabled { get; set; }
    /// <summary>The <c>WHEN</c> condition, or null when absent.</summary>
    public Expression? When { get; set; }
    /// <summary>The block of a simple trigger, or null when the trigger calls a procedure or is compound.</summary>
    public Block? Body { get; set; }
    /// <summary>The <c>CALL</c> statement of a trigger that calls a procedure, or null when absent.</summary>
    public CallStatement? Call { get; set; }
    /// <summary>True for a <c>COMPOUND TRIGGER</c>.</summary>
    public bool Compound { get; set; }
    /// <summary>The declarations of a compound trigger, in source order; empty for a simple trigger.</summary>
    public List<Declaration> CompoundDeclarations { get; set; } = [];
    /// <summary>The timing-point sections of a compound trigger, in source order; empty for a simple trigger.</summary>
    public List<TimingPointSection> Sections { get; set; } = [];
}

/// <summary>One timing-point section of a compound trigger, such as <c>BEFORE EACH ROW IS BEGIN ... END BEFORE EACH ROW</c>.</summary>
public sealed partial class TimingPointSection : SqlNode
{
    /// <summary>The timing point of the section.</summary>
    public TimingPoint Point { get; set; }
    /// <summary>The statements of the section, in source order.</summary>
    public List<Statement> Statements { get; set; } = [];
    /// <summary>The exception handlers of the section; empty when absent.</summary>
    public List<ExceptionHandler> Handlers { get; set; } = [];
}

/// <summary>The form of a <c>CREATE TYPE</c> statement.</summary>
public enum TypeKind
{
    /// <summary>An object type.</summary>
    Object,
    /// <summary>A nested table type.</summary>
    Table,
    /// <summary>A <c>VARRAY</c> type.</summary>
    Varray,
    /// <summary>A subtype under an object type (<c>UNDER</c>).</summary>
    Under,
}

/// <summary><c>CREATE [OR REPLACE] TYPE</c>: an object, collection or subtype definition.</summary>
public sealed partial class CreateTypeStatement : Statement
{
    /// <summary>True when <c>OR REPLACE</c> is written.</summary>
    public bool OrReplace { get; set; }
    /// <summary>The <c>EDITIONABLE</c> setting, or null when absent.</summary>
    public EditionableKind? Editionable { get; set; }
    /// <summary>The type name parts, schema first when qualified.</summary>
    public List<Identifier> Name { get; set; } = [];
    /// <summary>OBJECT, TABLE, VARRAY, or UNDER.</summary>
    public TypeKind Kind { get; set; }
    /// <summary>The type options, such as <c>AUTHID</c>, or null when absent.</summary>
    public ProgramOptions? Options { get; set; }
    /// <summary>True for <c>FORCE</c>.</summary>
    public bool Force { get; set; }
    /// <summary>The <c>OID</c> string, or null when absent.</summary>
    public Expression? Oid { get; set; }
    /// <summary>True for <c>FINAL</c>, false for <c>NOT FINAL</c>, or null when neither is written.</summary>
    public bool? Final { get; set; }
    /// <summary>True for <c>INSTANTIABLE</c>, false for <c>NOT INSTANTIABLE</c>, or null when neither is written.</summary>
    public bool? Instantiable { get; set; }
    /// <summary>True for <c>PERSISTABLE</c>, false for <c>NOT PERSISTABLE</c>, or null when neither is written.</summary>
    public bool? Persistable { get; set; }
    /// <summary>The attributes of an object type, in source order; empty when absent.</summary>
    public List<VariableDeclaration> Attributes { get; set; } = [];
    /// <summary>The methods of the type specification, in source order; empty when absent.</summary>
    public List<SubprogramDeclaration> Members { get; set; } = [];
    /// <summary>The element type of a collection type, or null for other forms.</summary>
    public DataType? ElementType { get; set; }
    /// <summary>The size limit of a <c>VARRAY</c>, or null when absent.</summary>
    public Expression? Limit { get; set; }
    /// <summary>The supertype name parts after <c>UNDER</c>; empty when absent.</summary>
    public List<Identifier> SuperType { get; set; } = [];
}

/// <summary><c>CREATE [OR REPLACE] TYPE BODY</c>: the implementation of an object type's methods.</summary>
public sealed partial class CreateTypeBodyStatement : Statement
{
    /// <summary>True when <c>OR REPLACE</c> is written.</summary>
    public bool OrReplace { get; set; }
    /// <summary>The type name parts, schema first when qualified.</summary>
    public List<Identifier> Name { get; set; } = [];
    /// <summary>The method implementations, in source order.</summary>
    public List<SubprogramDeclaration> Members { get; set; } = [];
}

// ---------- Statements ----------

/// <summary>The PL/SQL <c>NULL;</c> statement, which does nothing.</summary>
public sealed partial class NullStatement : Statement { }

/// <summary>An assignment, such as <c>x := x + 1</c>.</summary>
public sealed partial class AssignmentStatement : Statement
{
    /// <summary>The variable or element being assigned.</summary>
    public required Expression Target { get; set; }
    /// <summary>The value assigned.</summary>
    public required Expression Value { get; set; }
}

/// <summary>A call to a procedure as a statement, such as <c>p(1)</c>.</summary>
public sealed partial class ProcedureCallStatement : Statement
{
    /// <summary>The call expression.</summary>
    public required Expression Call { get; set; }
}

/// <summary>An <c>IF</c> statement with optional <c>ELSIF</c> and <c>ELSE</c> parts.</summary>
public sealed partial class IfStatement : Statement
{
    /// <summary>The condition after <c>IF</c>.</summary>
    public required Expression Condition { get; set; }
    /// <summary>The statements after <c>THEN</c>, in source order.</summary>
    public List<Statement> ThenStatements { get; set; } = [];
    /// <summary>The <c>ELSIF</c> branches, in source order; empty when absent.</summary>
    public List<IfBranch> ElsifBranches { get; set; } = [];
    /// <summary>The statements after <c>ELSE</c>, or null when absent.</summary>
    public List<Statement>? ElseStatements { get; set; }
}

/// <summary>One <c>ELSIF</c> branch of an <c>IF</c> statement.</summary>
public sealed partial class IfBranch : SqlNode
{
    /// <summary>The condition of the branch.</summary>
    public required Expression Condition { get; set; }
    /// <summary>The statements of the branch, in source order.</summary>
    public List<Statement> Statements { get; set; } = [];
}

/// <summary>A <c>CASE</c> statement, simple or searched, with <c>WHEN</c> branches.</summary>
public sealed partial class PlsqlCaseStatement : Statement
{
    /// <summary>The selector of a simple <c>CASE</c>, or null for the searched form.</summary>
    public Expression? Operand { get; set; }
    /// <summary>The <c>WHEN</c> branches, in source order.</summary>
    public List<CaseStatementWhen> Whens { get; set; } = [];
    /// <summary>The statements after <c>ELSE</c>, or null when absent.</summary>
    public List<Statement>? ElseStatements { get; set; }
}

/// <summary>One <c>WHEN</c> branch of a PL/SQL <c>CASE</c> statement.</summary>
public sealed partial class CaseStatementWhen : SqlNode
{
    /// <summary>The value or condition after <c>WHEN</c>.</summary>
    public required Expression When { get; set; }
    /// <summary>The statements of the branch, in source order.</summary>
    public List<Statement> Statements { get; set; } = [];
}

/// <summary>The form of a PL/SQL loop.</summary>
public enum LoopKind
{
    /// <summary>A plain <c>LOOP ... END LOOP</c>.</summary>
    Basic,
    /// <summary>A <c>WHILE</c> loop.</summary>
    While,
    /// <summary>A <c>FOR</c> loop.</summary>
    For,
}

/// <summary>A PL/SQL loop, such as <c>FOR i IN 1 .. 10 LOOP ... END LOOP</c>.</summary>
public sealed partial class LoopStatement : Statement
{
    /// <summary>The form of the loop.</summary>
    public LoopKind Kind { get; set; }
    /// <summary>WHILE condition (WHILE loops only).</summary>
    public Expression? Condition { get; set; }
    /// <summary>FOR loops: one name, or two for FOR key, value IN PAIRS OF ...</summary>
    public List<ForIterand> Iterands { get; set; } = [];
    /// <summary>FOR loops: the comma-separated iteration controls after IN.</summary>
    public List<IterationControl> Controls { get; set; } = [];
    /// <summary>The statements of the loop body, in source order.</summary>
    public List<Statement> Body { get; set; } = [];
    /// <summary>The label written after <c>END LOOP</c>, or null when absent.</summary>
    public Identifier? EndLabel { get; set; }
}

/// <summary>A FOR loop variable: name, optional type (FOR i NUMBER(5,1) IN ...), and MUTABLE.</summary>
public sealed partial class ForIterand : SqlNode
{
    /// <summary>The loop variable name.</summary>
    public required Identifier Name { get; set; }
    /// <summary>The declared type of the loop variable, or null when absent.</summary>
    public DataType? Type { get; set; }
    /// <summary>True for <c>MUTABLE</c>.</summary>
    public bool Mutable { get; set; }
}

/// <summary>
/// One comma-separated item of a FOR loop's iteration controls (Oracle 21c+). WHILE and WHEN are optional
/// predicates that end the loop or skip the current value.
/// </summary>
public abstract partial class IterationControl : SqlNode
{
    /// <summary>The <c>WHILE</c> condition, or null when absent.</summary>
    public Expression? While { get; set; }
    /// <summary>The <c>WHEN</c> condition, or null when absent.</summary>
    public Expression? When { get; set; }
}

/// <summary>[REVERSE] lower .. upper [BY step].</summary>
public sealed partial class RangeControl : IterationControl
{
    /// <summary>True for <c>REVERSE</c>: the range runs downward.</summary>
    public bool Reverse { get; set; }
    /// <summary>The lower bound of the range.</summary>
    public required Expression Lower { get; set; }
    /// <summary>The upper bound of the range.</summary>
    public required Expression Upper { get; set; }
    /// <summary>The <c>BY</c> step of the range, or null when absent.</summary>
    public Expression? By { get; set; }
}

/// <summary>A single value, or a bare cursor or collection name (FOR r IN c1 LOOP).</summary>
public sealed partial class ExpressionControl : IterationControl
{
    /// <summary>The value of the single-value form.</summary>
    public required Expression Value { get; set; }
}

/// <summary>REPEAT expr: repeatedly evaluates expr, using the previous value of the loop variable.</summary>
public sealed partial class RepeatControl : IterationControl
{
    /// <summary>The value repeated by <c>REPEAT</c>.</summary>
    public required Expression Value { get; set; }
}

/// <summary>VALUES OF collection: iterates over the collection's values.</summary>
public sealed partial class ValuesOfControl : IterationControl
{
    /// <summary>The collection named after <c>VALUES OF</c>.</summary>
    public required Expression Collection { get; set; }
}

/// <summary>INDICES OF collection: iterates over the collection's index values.</summary>
public sealed partial class IndicesOfControl : IterationControl
{
    /// <summary>The collection named after <c>INDICES OF</c>.</summary>
    public required Expression Collection { get; set; }
}

/// <summary>PAIRS OF collection: iterates over index and value together (two iterands).</summary>
public sealed partial class PairsOfControl : IterationControl
{
    /// <summary>The collection named after <c>PAIRS OF</c>.</summary>
    public required Expression Collection { get; set; }
}

/// <summary>FOR r IN (SELECT ...): cursor FOR loop over a query.</summary>
public sealed partial class CursorControl : IterationControl
{
    /// <summary>The query of a cursor-based loop control.</summary>
    public required SelectStatement Query { get; set; }
}

/// <summary>EXECUTE IMMEDIATE sql [USING ...]: iterates over the rows of a dynamic query.</summary>
public sealed partial class DynamicSqlControl : IterationControl
{
    /// <summary>The SQL string of an <c>EXECUTE IMMEDIATE</c> control.</summary>
    public required Expression Sql { get; set; }
    /// <summary>The <c>USING</c> arguments, in source order; empty when absent.</summary>
    public List<UsingArgument> Using { get; set; } = [];
}

/// <summary>An <c>EXIT</c> statement, optionally with a label and a <c>WHEN</c> condition.</summary>
public sealed partial class ExitStatement : Statement
{
    /// <summary>The loop label, or null when absent.</summary>
    public Identifier? Label { get; set; }
    /// <summary>The <c>WHEN</c> condition, or null when absent.</summary>
    public Expression? When { get; set; }
}

/// <summary>A <c>CONTINUE</c> statement, optionally with a label and a <c>WHEN</c> condition.</summary>
public sealed partial class ContinueStatement : Statement
{
    /// <summary>The loop label, or null when absent.</summary>
    public Identifier? Label { get; set; }
    /// <summary>The <c>WHEN</c> condition, or null when absent.</summary>
    public Expression? When { get; set; }
}

/// <summary>A <c>GOTO</c> statement, such as <c>GOTO done</c>.</summary>
public sealed partial class GotoStatement : Statement
{
    /// <summary>The label jumped to.</summary>
    public required Identifier Target { get; set; }
}

/// <summary>A <c>RETURN</c> statement, with a value in a function.</summary>
public sealed partial class ReturnStatement : Statement
{
    /// <summary>The returned value, or null for a bare <c>RETURN</c>.</summary>
    public Expression? Value { get; set; }
}

/// <summary>A <c>RAISE</c> statement, such as <c>RAISE e_limit</c>, or a bare <c>RAISE</c> to re-raise.</summary>
public sealed partial class RaiseStatement : Statement
{
    /// <summary>The exception name parts; empty for a bare <c>RAISE</c>.</summary>
    public List<Identifier> Exception { get; set; } = [];
}

/// <summary>One argument of a <c>USING</c> clause, such as <c>IN OUT :x</c>.</summary>
public sealed partial class UsingArgument : SqlNode
{
    /// <summary>The passing mode, or null when absent.</summary>
    public ParamMode? Mode { get; set; }
    /// <summary>The bound value.</summary>
    public required Expression Value { get; set; }
}

/// <summary>An <c>OPEN</c> statement, such as <c>OPEN c FOR SELECT ...</c>.</summary>
public sealed partial class OpenStatement : Statement
{
    /// <summary>The cursor variable opened.</summary>
    public required Expression Cursor { get; set; }
    /// <summary>The query after <c>FOR</c>, or null when absent.</summary>
    public SelectStatement? ForQuery { get; set; }
    /// <summary>The dynamic SQL expression after <c>FOR</c>, or null when absent.</summary>
    public Expression? ForExpression { get; set; }
    /// <summary>The <c>USING</c> arguments, in source order; empty when absent.</summary>
    public List<UsingArgument> Using { get; set; } = [];
}

/// <summary>A <c>FETCH</c> statement, such as <c>FETCH c BULK COLLECT INTO v LIMIT 100</c>.</summary>
public sealed partial class FetchStatement : Statement
{
    /// <summary>The cursor fetched from.</summary>
    public required Expression Cursor { get; set; }
    /// <summary>True for <c>BULK COLLECT</c>.</summary>
    public bool BulkCollect { get; set; }
    /// <summary>The <c>INTO</c> targets, in source order.</summary>
    public List<Expression> Into { get; set; } = [];
    /// <summary>The <c>LIMIT</c> row count, or null when absent.</summary>
    public Expression? Limit { get; set; }
}

/// <summary>A <c>CLOSE</c> statement, such as <c>CLOSE c</c>.</summary>
public sealed partial class CloseStatement : Statement
{
    /// <summary>The cursor closed.</summary>
    public required Expression Cursor { get; set; }
}

/// <summary>An <c>EXECUTE IMMEDIATE</c> statement, such as <c>EXECUTE IMMEDIATE sql USING x</c>.</summary>
public sealed partial class ExecuteImmediateStatement : Statement
{
    /// <summary>The SQL string expression.</summary>
    public required Expression Sql { get; set; }
    /// <summary>True for <c>BULK COLLECT INTO</c>.</summary>
    public bool BulkCollect { get; set; }
    /// <summary>The <c>INTO</c> targets, in source order; empty when absent.</summary>
    public List<Expression> Into { get; set; } = [];
    /// <summary>The <c>USING</c> arguments, in source order; empty when absent.</summary>
    public List<UsingArgument> Using { get; set; } = [];
    /// <summary>The <c>RETURNING ... INTO</c> targets, in source order; empty when absent.</summary>
    public List<Expression> ReturningInto { get; set; } = [];
    /// <summary>True for <c>RETURNING ... BULK COLLECT INTO</c>.</summary>
    public bool ReturningBulkCollect { get; set; }
}

/// <summary><c>FORALL i IN lower .. upper DML</c>: runs a DML statement for each index value in one call.</summary>
public sealed partial class ForallStatement : Statement
{
    /// <summary>The index variable.</summary>
    public required Identifier Index { get; set; }
    /// <summary>The lower bound of an <c>IN lower .. upper</c> range, or null when absent.</summary>
    public Expression? Lower { get; set; }
    /// <summary>The upper bound of an <c>IN lower .. upper</c> range, or null when absent.</summary>
    public Expression? Upper { get; set; }
    /// <summary>The collection named after <c>INDICES OF</c>, or null when absent.</summary>
    public Expression? IndicesOf { get; set; }
    /// <summary>The collection named after <c>VALUES OF</c>, or null when absent.</summary>
    public Expression? ValuesOf { get; set; }
    /// <summary>The lower bound after <c>BETWEEN</c> in the <c>INDICES OF</c> form, or null when absent.</summary>
    public Expression? BetweenLow { get; set; }
    /// <summary>The upper bound after <c>AND</c> in the <c>INDICES OF</c> form, or null when absent.</summary>
    public Expression? BetweenHigh { get; set; }
    /// <summary>True for <c>SAVE EXCEPTIONS</c>.</summary>
    public bool SaveExceptions { get; set; }
    /// <summary>The DML statement run for each index.</summary>
    public required Statement Body { get; set; }
}

/// <summary>A <c>PIPE ROW</c> statement in a pipelined function, such as <c>PIPE ROW (r)</c>.</summary>
public sealed partial class PipeRowStatement : Statement
{
    /// <summary>The row piped out.</summary>
    public required Expression Value { get; set; }
}

/// <summary>The kind of a <c>CREATE JAVA</c> object.</summary>
public enum JavaObjectKind
{
    /// <summary><c>JAVA SOURCE</c>.</summary>
    Source,
    /// <summary><c>JAVA CLASS</c>.</summary>
    Class,
    /// <summary><c>JAVA RESOURCE</c>.</summary>
    Resource,
}
/// <summary>The form of the <c>USING</c> clause of <c>CREATE JAVA</c>.</summary>
public enum JavaUsingKind
{
    /// <summary><c>USING BFILE</c>.</summary>
    Bfile,
    /// <summary><c>USING CLOB</c>.</summary>
    Clob,
    /// <summary><c>USING BLOB</c>.</summary>
    Blob,
    /// <summary><c>USING KEY</c>.</summary>
    Key,
}

/// <summary>CREATE JAVA SOURCE/CLASS/RESOURCE. SourceText is the embedded Java program (not SQL).</summary>
public sealed partial class CreateJavaStatement : Statement
{
    /// <summary>True when <c>OR REPLACE</c> is written.</summary>
    public bool OrReplace { get; set; }
    /// <summary>True for <c>AND RESOLVE</c>.</summary>
    public bool AndResolve { get; set; }
    /// <summary>True for <c>AND COMPILE</c>.</summary>
    public bool AndCompile { get; set; }
    /// <summary>True for <c>NOFORCE</c>.</summary>
    public bool NoForce { get; set; }
    /// <summary>The kind of Java object created.</summary>
    public JavaObjectKind Kind { get; set; }
    /// <summary>The object name parts, schema first when qualified.</summary>
    public List<Identifier> Name { get; set; } = [];
    /// <summary>The <c>SHARING</c> setting, or null when absent.</summary>
    public SharingKind? Sharing { get; set; }
    /// <summary>The <c>AUTHID</c> setting, or null when absent.</summary>
    public AuthidKind? Authid { get; set; }
    /// <summary>The <c>RESOLVER</c> clause as a token tree; empty when absent.</summary>
    public List<SyntaxItem> Resolver { get; set; } = [];
    /// <summary>The form of the <c>USING</c> clause, or null when absent.</summary>
    public JavaUsingKind? UsingKind { get; set; }
    /// <summary>The directory of a <c>USING BFILE</c> form, or null when absent.</summary>
    public Expression? UsingDirectory { get; set; }
    /// <summary>The file name of a <c>USING BFILE</c> form, or null when absent.</summary>
    public Expression? UsingFile { get; set; }
    /// <summary>The key expression of a <c>USING KEY</c> form, or null when absent.</summary>
    public Expression? UsingKey { get; set; }
    /// <summary>The query of a <c>USING</c> form that selects the content, or null when absent.</summary>
    public SelectStatement? UsingQuery { get; set; }
    /// <summary>The source text of a <c>JAVA SOURCE</c> object, or null when absent.</summary>
    public string? SourceText { get; set; }
}

/// <summary><c>CREATE [OR REPLACE] LIBRARY</c>: an external C library, such as <c>CREATE LIBRARY lib AS '/lib/x.so'</c>.</summary>
public sealed partial class CreateLibraryStatement : Statement
{
    /// <summary>True when <c>OR REPLACE</c> is written.</summary>
    public bool OrReplace { get; set; }
    /// <summary>The <c>EDITIONABLE</c> setting, or null when absent.</summary>
    public EditionableKind? Editionable { get; set; }
    /// <summary>The <c>SHARING</c> setting, or null when absent.</summary>
    public SharingKind? Sharing { get; set; }
    /// <summary>The library name parts, schema first when qualified.</summary>
    public List<Identifier> Name { get; set; } = [];
    /// <summary>The file path after <c>AS</c>.</summary>
    public required Expression Path { get; set; }
    /// <summary>The agent name after <c>AGENT</c>, or null when absent.</summary>
    public Expression? Agent { get; set; }
    /// <summary>The credential name after <c>CREDENTIAL</c>, or null when absent.</summary>
    public Identifier? Credential { get; set; }
}
