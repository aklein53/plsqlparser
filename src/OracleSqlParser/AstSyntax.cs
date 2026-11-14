namespace OracleSqlParser;

// ======================================================================
// Token tree: structured (lossless, nested) representation used ONLY for
// long-tail DDL/admin syntax that has no dedicated typed node. Every use
// is listed in COVERAGE.md.
// ======================================================================

public abstract partial class SyntaxItem : SqlNode { }

/// <summary>An unquoted keyword/identifier (Value upper-cased) or a quoted identifier.</summary>
public sealed partial class WordItem : SyntaxItem
{
    public string Value { get; set; } = "";
    public bool Quoted { get; set; }
    /// <summary>The word as typed (without quotes); null for synthesized items.</summary>
    [SpellingOnly] public string? Text { get; set; }
}

public sealed partial class LiteralItem : SyntaxItem
{
    public LiteralKind Kind { get; set; }
    public string Value { get; set; } = "";
}

public sealed partial class SymbolItem : SyntaxItem
{
    public string Symbol { get; set; } = "";
}

public sealed partial class BindItem : SyntaxItem
{
    public string Name { get; set; } = "";
}

/// <summary>A parenthesized group.</summary>
public sealed partial class GroupItem : SyntaxItem
{
    public List<SyntaxItem> Items { get; set; } = [];
}

// ---------- Optimizer hints ----------

public sealed partial class HintClause : SqlNode
{
    public string Text { get; set; } = "";
    public List<HintItem> Items { get; set; } = [];
}

public sealed partial class HintItem : SqlNode
{
    public string Name { get; set; } = "";
    public List<SyntaxItem> Arguments { get; set; } = [];
}

// ---------- Function trailing clauses (JSON / XML / LISTAGG / CAST ...) ----------

public abstract partial class FunctionClause : SqlNode { }

public sealed partial class ReturningClause : FunctionClause
{
    public DataType? Type { get; set; }
    /// <summary>XMLQUERY ... RETURNING CONTENT.</summary>
    public bool Content { get; set; }
}

public enum FormatKind { Json }

public sealed partial class FormatClause : FunctionClause
{
    public FormatKind Kind { get; set; }
    public Identifier? Encoding { get; set; }
}

/// <summary>NULL ON NULL / ABSENT ON NULL.</summary>
public sealed partial class NullOnClause : FunctionClause
{
    public bool Absent { get; set; }
}

public enum BehaviorKind { Error, Null, True, False, Default, EmptyArray, EmptyObject, Empty, Ignore, Remove, Create, Replace }
public enum BehaviorEvent { Error, Empty, Mismatch, Null, ConversionError, Missing, Existing }

/// <summary>e.g. ERROR ON ERROR, DEFAULT 0 ON EMPTY, DEFAULT x ON CONVERSION ERROR.</summary>
public sealed partial class OnBehaviorClause : FunctionClause
{
    public BehaviorKind Behavior { get; set; }
    public Expression? Value { get; set; }
    public BehaviorEvent Event { get; set; }
}

public sealed partial class WrapperClause : FunctionClause
{
    public bool With { get; set; }
    public bool Conditional { get; set; }
    public bool Unconditional { get; set; }
    public bool Array { get; set; }
}

public sealed partial class QuotesClause : FunctionClause
{
    public bool Keep { get; set; }
    public bool OnScalarString { get; set; }
}

/// <summary>LISTAGG ON OVERFLOW.</summary>
public sealed partial class OverflowClause : FunctionClause
{
    public bool Truncate { get; set; }
    public Expression? Indicator { get; set; }
    public bool? WithCount { get; set; }
}

public sealed partial class PassingClause : FunctionClause
{
    public bool ByValue { get; set; }
    public List<ExpressionSelectItem> Items { get; set; } = [];
}

public enum FunctionFlag
{
    Strict, Lax, WithUniqueKeys, WithoutUniqueKeys, Pretty, Ascii, Truncate, Ordered, Wellformed, Indent, NoIndent,
    HideDefaults, ShowDefaults, AllowScalars, DisallowScalars, AllowArray, DisallowArray, Exists, Document, Content,
}

public sealed partial class FlagClause : FunctionClause
{
    public FunctionFlag Flag { get; set; }
}

public enum ValueClauseKind { Encoding, Version, IndentSize, StandaloneYes, StandaloneNo, StandaloneNoValue }

public sealed partial class ValueClause : FunctionClause
{
    public ValueClauseKind Kind { get; set; }
    public Expression? Value { get; set; }
}

/// <summary>TRANSLATE(x USING NCHAR_CS) / CHR(n USING NCHAR_CS).</summary>
public sealed partial class UsingCharsetClause : FunctionClause
{
    public required Identifier Charset { get; set; }
}

/// <summary>PREDICTION(m USING a, b) / FEATURE_VALUE(... USING *).</summary>
public sealed partial class UsingListClause : FunctionClause
{
    public List<SelectItem> Items { get; set; } = [];
}

public enum JsonTransformOp { Set, Insert, Replace, Append, Prepend, Remove, Keep, Rename, Sort }

public sealed partial class JsonTransformOperation : SqlNode
{
    public JsonTransformOp Op { get; set; }
    public required Expression Path { get; set; }
    /// <summary>SET/INSERT/... value, or RENAME ... WITH new_name.</summary>
    public Expression? Value { get; set; }
    public FormatClause? Format { get; set; }
    public bool Descending { get; set; }
    /// <summary>ON EXISTING / ON MISSING / ON NULL / ON ERROR handlers.</summary>
    public List<FunctionClause> Clauses { get; set; } = [];
}
