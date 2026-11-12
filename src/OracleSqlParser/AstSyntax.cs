namespace OracleSqlParser;

// ======================================================================
// Token tree: structured (lossless, nested) representation used ONLY for
// long-tail DDL/admin syntax that has no dedicated typed node. Every use
// is listed in COVERAGE.md.
// ======================================================================

public abstract class SyntaxItem : SqlNode { }

/// <summary>An unquoted keyword/identifier (Value upper-cased) or a quoted identifier.</summary>
public sealed class WordItem : SyntaxItem
{
    public string Value { get; set; } = "";
    public bool Quoted { get; set; }
}

public sealed class LiteralItem : SyntaxItem
{
    public LiteralKind Kind { get; set; }
    public string Value { get; set; } = "";
}

public sealed class SymbolItem : SyntaxItem
{
    public string Symbol { get; set; } = "";
}

public sealed class BindItem : SyntaxItem
{
    public string Name { get; set; } = "";
}

/// <summary>A parenthesized group.</summary>
public sealed class GroupItem : SyntaxItem
{
    public List<SyntaxItem> Items { get; set; } = [];
}

// ---------- Optimizer hints ----------

public sealed class HintClause : SqlNode
{
    public string Text { get; set; } = "";
    public List<HintItem> Items { get; set; } = [];
}

public sealed class HintItem : SqlNode
{
    public string Name { get; set; } = "";
    public List<SyntaxItem> Arguments { get; set; } = [];
}

// ---------- Function trailing clauses (JSON / XML / LISTAGG / CAST ...) ----------

public abstract class FunctionClause : SqlNode { }

public sealed class ReturningClause : FunctionClause
{
    public DataType? Type { get; set; }
    /// <summary>XMLQUERY ... RETURNING CONTENT.</summary>
    public bool Content { get; set; }
}

public enum FormatKind { Json }

public sealed class FormatClause : FunctionClause
{
    public FormatKind Kind { get; set; }
    public Identifier? Encoding { get; set; }
}

/// <summary>NULL ON NULL / ABSENT ON NULL.</summary>
public sealed class NullOnClause : FunctionClause
{
    public bool Absent { get; set; }
}

public enum BehaviorKind { Error, Null, True, False, Default, EmptyArray, EmptyObject, Empty, Ignore, Remove, Create, Replace }
public enum BehaviorEvent { Error, Empty, Mismatch, Null, ConversionError, Missing, Existing }

/// <summary>e.g. ERROR ON ERROR, DEFAULT 0 ON EMPTY, DEFAULT x ON CONVERSION ERROR.</summary>
public sealed class OnBehaviorClause : FunctionClause
{
    public BehaviorKind Behavior { get; set; }
    public Expression? Value { get; set; }
    public BehaviorEvent Event { get; set; }
}

public sealed class WrapperClause : FunctionClause
{
    public bool With { get; set; }
    public bool Conditional { get; set; }
    public bool Unconditional { get; set; }
    public bool Array { get; set; }
}

public sealed class QuotesClause : FunctionClause
{
    public bool Keep { get; set; }
    public bool OnScalarString { get; set; }
}

/// <summary>LISTAGG ON OVERFLOW.</summary>
public sealed class OverflowClause : FunctionClause
{
    public bool Truncate { get; set; }
    public Expression? Indicator { get; set; }
    public bool? WithCount { get; set; }
}

public sealed class PassingClause : FunctionClause
{
    public bool ByValue { get; set; }
    public List<ExpressionSelectItem> Items { get; set; } = [];
}

public enum FunctionFlag
{
    Strict, Lax, WithUniqueKeys, WithoutUniqueKeys, Pretty, Ascii, Truncate, Ordered, Wellformed, Indent, NoIndent,
    HideDefaults, ShowDefaults, AllowScalars, DisallowScalars, AllowArray, DisallowArray, Exists, Document, Content,
}

public sealed class FlagClause : FunctionClause
{
    public FunctionFlag Flag { get; set; }
}

public enum ValueClauseKind { Encoding, Version, IndentSize, StandaloneYes, StandaloneNo, StandaloneNoValue }

public sealed class ValueClause : FunctionClause
{
    public ValueClauseKind Kind { get; set; }
    public Expression? Value { get; set; }
}

/// <summary>TRANSLATE(x USING NCHAR_CS) / CHR(n USING NCHAR_CS).</summary>
public sealed class UsingCharsetClause : FunctionClause
{
    public Identifier Charset { get; set; } = null!;
}

/// <summary>PREDICTION(m USING a, b) / FEATURE_VALUE(... USING *).</summary>
public sealed class UsingListClause : FunctionClause
{
    public List<SelectItem> Items { get; set; } = [];
}

public enum JsonTransformOp { Set, Insert, Replace, Append, Prepend, Remove, Keep, Rename, Sort }

public sealed class JsonTransformOperation : SqlNode
{
    public JsonTransformOp Op { get; set; }
    public Expression Path { get; set; } = null!;
    /// <summary>SET/INSERT/... value, or RENAME ... WITH new_name.</summary>
    public Expression? Value { get; set; }
    public FormatClause? Format { get; set; }
    public bool Descending { get; set; }
    /// <summary>ON EXISTING / ON MISSING / ON NULL / ON ERROR handlers.</summary>
    public List<FunctionClause> Clauses { get; set; } = [];
}
