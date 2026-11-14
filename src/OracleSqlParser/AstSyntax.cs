namespace OracleSqlParser;

// ======================================================================
// Token tree: structured (lossless, nested) representation used ONLY for
// long-tail DDL/admin syntax that has no dedicated typed node. Every use
// is listed in COVERAGE.md.
// ======================================================================

/// <summary>One item of a token tree: a fragment of SQL kept as written when no typed node models it.</summary>
public abstract partial class SyntaxItem : SqlNode { }

/// <summary>An unquoted keyword/identifier (Value upper-cased) or a quoted identifier.</summary>
public sealed partial class WordItem : SyntaxItem
{
    /// <summary>The word as written.</summary>
    public string Value { get; set; } = "";
    /// <summary>True when the word was written as a quoted identifier.</summary>
    public bool Quoted { get; set; }
    /// <summary>The word as typed (without quotes); null for synthesized items.</summary>
    [SpellingOnly] public string? Text { get; set; }
}

/// <summary>A literal inside a token tree, such as <c>'x'</c> or <c>42</c>.</summary>
public sealed partial class LiteralItem : SyntaxItem
{
    /// <summary>The kind of literal.</summary>
    public LiteralKind Kind { get; set; }
    /// <summary>The literal text as written.</summary>
    public string Value { get; set; } = "";
}

/// <summary>An operator or punctuation token inside a token tree, such as <c>,</c> or <c>=</c>.</summary>
public sealed partial class SymbolItem : SyntaxItem
{
    /// <summary>The symbol text.</summary>
    public string Symbol { get; set; } = "";
}

/// <summary>A bind variable inside a token tree, such as <c>:b1</c>.</summary>
public sealed partial class BindItem : SyntaxItem
{
    /// <summary>The bind name without the leading colon.</summary>
    public string Name { get; set; } = "";
}

/// <summary>A parenthesized group.</summary>
public sealed partial class GroupItem : SyntaxItem
{
    /// <summary>The items inside the parentheses, in source order.</summary>
    public List<SyntaxItem> Items { get; set; } = [];
}

// ---------- Optimizer hints ----------

/// <summary>An optimizer hint comment, such as <c>/*+ INDEX(e emp_ix) */</c>.</summary>
public sealed partial class HintClause : SqlNode
{
    /// <summary>The hint text between <c>/*+</c> and <c>*/</c>, as written.</summary>
    public string Text { get; set; } = "";
    /// <summary>The individual hints in the text, in source order.</summary>
    public List<HintItem> Items { get; set; } = [];
}

/// <summary>One hint inside a hint comment, such as <c>INDEX(e emp_ix)</c>.</summary>
public sealed partial class HintItem : SqlNode
{
    /// <summary>The hint name, such as <c>INDEX</c>.</summary>
    public string Name { get; set; } = "";
    /// <summary>The arguments in the hint's parentheses; empty when the hint has none.</summary>
    public List<SyntaxItem> Arguments { get; set; } = [];
}

// ---------- Function trailing clauses (JSON / XML / LISTAGG / CAST ...) ----------

/// <summary>A clause written inside a function or JSON/XML call, such as <c>RETURNING VARCHAR2</c> or <c>ON ERROR</c>.</summary>
public abstract partial class FunctionClause : SqlNode { }

/// <summary><c>RETURNING type</c> in a JSON or XML function, such as <c>JSON_VALUE(... RETURNING NUMBER)</c>.</summary>
public sealed partial class ReturningClause : FunctionClause
{
    /// <summary>The return type, or null when the clause has no type.</summary>
    public DataType? Type { get; set; }
    /// <summary>XMLQUERY ... RETURNING CONTENT.</summary>
    public bool Content { get; set; }
}

/// <summary>The format named by a <c>FORMAT</c> clause.</summary>
public enum FormatKind
{
    /// <summary><c>FORMAT JSON</c>.</summary>
    Json,
}

/// <summary><c>FORMAT JSON [ENCODING name]</c>, the input or output format of a JSON function.</summary>
public sealed partial class FormatClause : FunctionClause
{
    /// <summary>The format named after <c>FORMAT</c>.</summary>
    public FormatKind Kind { get; set; }
    /// <summary>The encoding named after <c>ENCODING</c>, or null when absent.</summary>
    public Identifier? Encoding { get; set; }
}

/// <summary>NULL ON NULL / ABSENT ON NULL.</summary>
public sealed partial class NullOnClause : FunctionClause
{
    /// <summary>True for <c>ABSENT ON NULL</c>; false for <c>NULL ON NULL</c>.</summary>
    public bool Absent { get; set; }
}

/// <summary>What a JSON or XML function does when an error or an empty result occurs, as in <c>ON ERROR NULL</c>.</summary>
public enum BehaviorKind
{
    /// <summary><c>ERROR</c>: raises an error.</summary>
    Error,
    /// <summary><c>NULL</c>: returns null.</summary>
    Null,
    /// <summary><c>TRUE</c>: returns the JSON value true.</summary>
    True,
    /// <summary><c>FALSE</c>: returns the JSON value false.</summary>
    False,
    /// <summary><c>DEFAULT expr</c>: returns the given value.</summary>
    Default,
    /// <summary><c>EMPTY ARRAY</c>: returns an empty JSON array.</summary>
    EmptyArray,
    /// <summary><c>EMPTY OBJECT</c>: returns an empty JSON object.</summary>
    EmptyObject,
    /// <summary><c>EMPTY</c> without <c>ARRAY</c> or <c>OBJECT</c>.</summary>
    Empty,
    /// <summary><c>IGNORE</c>: ignores the error.</summary>
    Ignore,
    /// <summary><c>REMOVE</c>: removes the member.</summary>
    Remove,
    /// <summary><c>CREATE</c>: creates the member.</summary>
    Create,
    /// <summary><c>REPLACE</c>: replaces the member.</summary>
    Replace,
}
/// <summary>The condition a behavior answers, such as the <c>ON ERROR</c> or <c>ON EMPTY</c> part of a JSON or XML function.</summary>
public enum BehaviorEvent
{
    /// <summary><c>ON ERROR</c>.</summary>
    Error,
    /// <summary><c>ON EMPTY</c>.</summary>
    Empty,
    /// <summary><c>ON MISMATCH</c>.</summary>
    Mismatch,
    /// <summary><c>ON NULL</c>.</summary>
    Null,
    /// <summary><c>ON CONVERSION ERROR</c>.</summary>
    ConversionError,
    /// <summary><c>ON MISSING</c>.</summary>
    Missing,
    /// <summary><c>ON EXISTING</c>.</summary>
    Existing,
}

/// <summary>e.g. ERROR ON ERROR, DEFAULT 0 ON EMPTY, DEFAULT x ON CONVERSION ERROR.</summary>
public sealed partial class OnBehaviorClause : FunctionClause
{
    /// <summary>The behavior to take when the event occurs.</summary>
    public BehaviorKind Behavior { get; set; }
    /// <summary>The value of a <c>DEFAULT expr</c> behavior; null for the other behaviors.</summary>
    public Expression? Value { get; set; }
    /// <summary>The condition the behavior answers.</summary>
    public BehaviorEvent Event { get; set; }
}

/// <summary>The <c>WRAPPER</c> clause of <c>JSON_QUERY</c>, such as <c>WITH UNCONDITIONAL ARRAY WRAPPER</c>.</summary>
public sealed partial class WrapperClause : FunctionClause
{
    /// <summary>True when <c>WITH</c> is written, false for <c>WITHOUT WRAPPER</c>.</summary>
    public bool With { get; set; }
    /// <summary>True for <c>WITH CONDITIONAL WRAPPER</c>.</summary>
    public bool Conditional { get; set; }
    /// <summary>True for <c>WITH UNCONDITIONAL WRAPPER</c>.</summary>
    public bool Unconditional { get; set; }
    /// <summary>True when <c>ARRAY</c> is written before <c>WRAPPER</c>.</summary>
    public bool Array { get; set; }
}

/// <summary>The <c>[KEEP | OMIT] QUOTES [ON SCALAR STRING]</c> clause of <c>JSON_QUERY</c>.</summary>
public sealed partial class QuotesClause : FunctionClause
{
    /// <summary>True for <c>KEEP QUOTES</c>; false for <c>OMIT QUOTES</c>.</summary>
    public bool Keep { get; set; }
    /// <summary>True when <c>ON SCALAR STRING</c> is written.</summary>
    public bool OnScalarString { get; set; }
}

/// <summary>LISTAGG ON OVERFLOW.</summary>
public sealed partial class OverflowClause : FunctionClause
{
    /// <summary>True when <c>TRUNCATE</c> is written in the <c>ON OVERFLOW</c> clause of <c>LISTAGG</c>.</summary>
    public bool Truncate { get; set; }
    /// <summary>The truncation indicator literal written after <c>TRUNCATE</c>, or null when absent.</summary>
    public Expression? Indicator { get; set; }
    /// <summary>True for <c>WITH COUNT</c>, false for <c>WITHOUT COUNT</c>, null when neither is written.</summary>
    public bool? WithCount { get; set; }
}

/// <summary><c>PASSING [BY VALUE] expr AS name, ...</c> in an XML or JSON function.</summary>
public sealed partial class PassingClause : FunctionClause
{
    /// <summary>True when <c>BY VALUE</c> is written.</summary>
    public bool ByValue { get; set; }
    /// <summary>The passed expressions with their names, in source order.</summary>
    public List<ExpressionSelectItem> Items { get; set; } = [];
}

/// <summary>A keyword flag of a JSON or XML function clause.</summary>
public enum FunctionFlag
{
    /// <summary><c>STRICT</c>: path errors are raised.</summary>
    Strict,
    /// <summary><c>LAX</c>: path errors are suppressed.</summary>
    Lax,
    /// <summary><c>WITH UNIQUE KEYS</c>.</summary>
    WithUniqueKeys,
    /// <summary><c>WITHOUT UNIQUE KEYS</c>.</summary>
    WithoutUniqueKeys,
    /// <summary><c>PRETTY</c>: indented output.</summary>
    Pretty,
    /// <summary><c>ASCII</c>: output in ASCII.</summary>
    Ascii,
    /// <summary><c>TRUNCATE</c>.</summary>
    Truncate,
    /// <summary><c>ORDERED</c>: keys keep their order.</summary>
    Ordered,
    /// <summary><c>WELLFORMED</c>.</summary>
    Wellformed,
    /// <summary><c>INDENT</c>.</summary>
    Indent,
    /// <summary><c>NOINDENT</c>.</summary>
    NoIndent,
    /// <summary><c>HIDE DEFAULTS</c>.</summary>
    HideDefaults,
    /// <summary><c>SHOW DEFAULTS</c>.</summary>
    ShowDefaults,
    /// <summary><c>ALLOW SCALARS</c>.</summary>
    AllowScalars,
    /// <summary><c>DISALLOW SCALARS</c>.</summary>
    DisallowScalars,
    /// <summary><c>ALLOW ARRAY</c>.</summary>
    AllowArray,
    /// <summary><c>DISALLOW ARRAY</c>.</summary>
    DisallowArray,
    /// <summary><c>EXISTS</c>.</summary>
    Exists,
    /// <summary><c>DOCUMENT</c>: the XML value is a document.</summary>
    Document,
    /// <summary><c>CONTENT</c>: the XML value is content.</summary>
    Content,
}

/// <summary>A bare keyword clause of a JSON or XML function, such as <c>STRICT</c> or <c>WITH UNIQUE KEYS</c>.</summary>
public sealed partial class FlagClause : FunctionClause
{
    /// <summary>The keyword flag written.</summary>
    public FunctionFlag Flag { get; set; }
}

/// <summary>The keyword of a clause that takes a value.</summary>
public enum ValueClauseKind
{
    /// <summary><c>ENCODING</c> followed by an encoding name.</summary>
    Encoding,
    /// <summary><c>VERSION</c> followed by a version string.</summary>
    Version,
    /// <summary><c>INDENT SIZE</c> followed by a number of spaces.</summary>
    IndentSize,
    /// <summary><c>STANDALONE YES</c>.</summary>
    StandaloneYes,
    /// <summary><c>STANDALONE NO</c>.</summary>
    StandaloneNo,
    /// <summary><c>STANDALONE NO VALUE</c>.</summary>
    StandaloneNoValue,
}

/// <summary>A keyword clause with an optional value, such as <c>VERSION '1.0'</c>.</summary>
public sealed partial class ValueClause : FunctionClause
{
    /// <summary>The keyword the clause starts with.</summary>
    public ValueClauseKind Kind { get; set; }
    /// <summary>The value after the keyword, or null when the clause has none.</summary>
    public Expression? Value { get; set; }
}

/// <summary>TRANSLATE(x USING NCHAR_CS) / CHR(n USING NCHAR_CS).</summary>
public sealed partial class UsingCharsetClause : FunctionClause
{
    /// <summary>The character set named after <c>USING CHARSET</c>.</summary>
    public required Identifier Charset { get; set; }
}

/// <summary>PREDICTION(m USING a, b) / FEATURE_VALUE(... USING *).</summary>
public sealed partial class UsingListClause : FunctionClause
{
    /// <summary>The items of the <c>USING</c> list, in source order.</summary>
    public List<SelectItem> Items { get; set; } = [];
}

/// <summary>The operation of one step in a <c>JSON_TRANSFORM</c> call.</summary>
public enum JsonTransformOp
{
    /// <summary><c>SET</c>: sets a value.</summary>
    Set,
    /// <summary><c>INSERT</c>: inserts a value that does not exist yet.</summary>
    Insert,
    /// <summary><c>REPLACE</c>: replaces an existing value.</summary>
    Replace,
    /// <summary><c>APPEND</c>: appends to an array.</summary>
    Append,
    /// <summary><c>PREPEND</c>: prepends to an array.</summary>
    Prepend,
    /// <summary><c>REMOVE</c>: removes the value at the path.</summary>
    Remove,
    /// <summary><c>KEEP</c>: keeps only the values that match.</summary>
    Keep,
    /// <summary><c>RENAME</c>: renames a member.</summary>
    Rename,
    /// <summary><c>SORT</c>: sorts an array.</summary>
    Sort,
}

/// <summary>One step of a <c>JSON_TRANSFORM</c> call, such as <c>SET '$.a' = 1</c>.</summary>
public sealed partial class JsonTransformOperation : SqlNode
{
    /// <summary>The operation performed by the step.</summary>
    public JsonTransformOp Op { get; set; }
    /// <summary>The JSON path the step applies to.</summary>
    public required Expression Path { get; set; }
    /// <summary>SET/INSERT/... value, or RENAME ... WITH new_name.</summary>
    public Expression? Value { get; set; }
    /// <summary>The <c>FORMAT</c> clause of the step's value, or null when absent.</summary>
    public FormatClause? Format { get; set; }
    /// <summary>True when <c>DESCENDING</c> is written (for <c>SORT</c>).</summary>
    public bool Descending { get; set; }
    /// <summary>ON EXISTING / ON MISSING / ON NULL / ON ERROR handlers.</summary>
    public List<FunctionClause> Clauses { get; set; } = [];
}
