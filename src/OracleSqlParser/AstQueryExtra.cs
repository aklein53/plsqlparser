namespace OracleSqlParser;

// ---------- Table reference modifiers ----------

public sealed partial class SampleClause : SqlNode
{
    public bool Block { get; set; }
    public Expression Percent { get; set; } = null!;
    public Expression? Seed { get; set; }
}

public enum FlashbackMode { AsOf, VersionsBetween }
public enum FlashbackUnit { Scn, Timestamp, Period }

/// <summary>AS OF SCN|TIMESTAMP|PERIOD FOR p expr, or VERSIONS BETWEEN SCN|TIMESTAMP a AND b.</summary>
public sealed partial class FlashbackClause : SqlNode
{
    public FlashbackMode Mode { get; set; }
    public FlashbackUnit Unit { get; set; }
    /// <summary>The valid-time period name for PERIOD FOR.</summary>
    public Identifier? Period { get; set; }
    public Expression Value { get; set; } = null!;
    public Expression? ToValue { get; set; }
}

public sealed partial class PivotTable : TableReference
{
    public TableReference Source { get; set; } = null!;
    public bool Xml { get; set; }
    public List<PivotAggregate> Aggregates { get; set; } = [];
    public List<ColumnReference> ForColumns { get; set; } = [];
    public List<PivotInItem> InItems { get; set; } = [];
    public bool InAny { get; set; }
    public SelectStatement? InSubquery { get; set; }
}

public sealed partial class PivotAggregate : SqlNode
{
    public Expression Function { get; set; } = null!;
    public Identifier? Alias { get; set; }
}

public sealed partial class PivotInItem : SqlNode
{
    public Expression Value { get; set; } = null!;
    public Identifier? Alias { get; set; }
}

public sealed partial class UnpivotTable : TableReference
{
    public TableReference Source { get; set; } = null!;
    public bool IncludeNulls { get; set; }
    public List<ColumnReference> ValueColumns { get; set; } = [];
    public List<ColumnReference> ForColumns { get; set; } = [];
    public List<UnpivotInItem> InItems { get; set; } = [];
}

public sealed partial class UnpivotInItem : SqlNode
{
    public List<ColumnReference> Columns { get; set; } = [];
    public Expression? AliasValue { get; set; }
}

public sealed partial class XmlNamespace : SqlNode
{
    public Expression Uri { get; set; } = null!;
    public Identifier? Alias { get; set; }
    /// <summary>DEFAULT 'uri'.</summary>
    public bool IsDefault { get; set; }
}

/// <summary>XMLTABLE(...) in a FROM clause.</summary>
public sealed partial class XmlTable : TableReference
{
    public List<XmlNamespace> Namespaces { get; set; } = [];
    public Expression XQuery { get; set; } = null!;
    public PassingClause? Passing { get; set; }
    public List<PathColumn> Columns { get; set; } = [];
}

/// <summary>JSON_TABLE(...) in a FROM clause.</summary>
public sealed partial class JsonTable : TableReference
{
    public Expression Input { get; set; } = null!;
    public FormatClause? InputFormat { get; set; }
    public Expression Path { get; set; } = null!;
    /// <summary>ERROR/EMPTY ON ... clauses after the path.</summary>
    public List<FunctionClause> Clauses { get; set; } = [];
    public List<PathColumn> Columns { get; set; } = [];
}

/// <summary>Column definition inside XMLTABLE/JSON_TABLE COLUMNS (...).</summary>
public sealed partial class PathColumn : SqlNode
{
    public Identifier? Name { get; set; }
    public bool ForOrdinality { get; set; }
    public DataType? Type { get; set; }
    /// <summary>EXISTS PATH column.</summary>
    public bool Exists { get; set; }
    /// <summary>FORMAT JSON, TRUNCATE, WITH WRAPPER, ON ERROR/EMPTY, ... (in source order).</summary>
    public List<FunctionClause> Clauses { get; set; } = [];
    public Expression? Path { get; set; }
    public Expression? Default { get; set; }
    /// <summary>True for NESTED [PATH] 'p' COLUMNS (...); Path holds the nested path.</summary>
    public bool Nested { get; set; }
    public List<PathColumn> Columns { get; set; } = [];
}

// ---------- MATCH_RECOGNIZE ----------

public enum RowsPerMatch { OneRow, AllRows }
public enum AllRowsMode { ShowEmptyMatches, OmitEmptyMatches, WithUnmatchedRows }
public enum SkipKind { PastLastRow, ToNextRow, ToFirst, ToLast }

public sealed partial class MatchRecognizeTable : TableReference
{
    public TableReference Source { get; set; } = null!;
    public List<Expression> PartitionBy { get; set; } = [];
    public List<OrderByItem> OrderBy { get; set; } = [];
    public List<ExpressionSelectItem> Measures { get; set; } = [];
    public RowsPerMatch? RowsPerMatch { get; set; }
    public AllRowsMode? AllRowsMode { get; set; }
    public SkipKind? Skip { get; set; }
    public Identifier? SkipVariable { get; set; }
    /// <summary>PATTERN is preceded by SEEK instead of INITIAL.</summary>
    public bool Seek { get; set; }
    public PatternNode Pattern { get; set; } = null!;
    public List<PatternSubset> Subsets { get; set; } = [];
    public List<PatternDefine> Define { get; set; } = [];
}

public abstract partial class PatternNode : SqlNode { }
public sealed partial class PatternVariable : PatternNode { public Identifier Name { get; set; } = null!; }
public sealed partial class PatternAnchor : PatternNode { public bool IsStart { get; set; } }
public sealed partial class PatternConcat : PatternNode { public List<PatternNode> Items { get; set; } = []; }
public sealed partial class PatternAlternation : PatternNode { public List<PatternNode> Items { get; set; } = []; }
public sealed partial class PatternGroup : PatternNode { public PatternNode Inner { get; set; } = null!; }
public sealed partial class PatternPermute : PatternNode { public List<PatternNode> Items { get; set; } = []; }
public sealed partial class PatternExclusion : PatternNode { public PatternNode Inner { get; set; } = null!; }

public sealed partial class PatternQuantified : PatternNode
{
    public PatternNode Inner { get; set; } = null!;
    public int Min { get; set; }
    /// <summary>null = unbounded.</summary>
    public int? Max { get; set; }
    public bool Reluctant { get; set; }
}

public sealed partial class PatternSubset : SqlNode
{
    public Identifier Name { get; set; } = null!;
    public List<Identifier> Members { get; set; } = [];
}

public sealed partial class PatternDefine : SqlNode
{
    public Identifier Variable { get; set; } = null!;
    public Expression Condition { get; set; } = null!;
}

/// <summary>GRAPH_TABLE(graph MATCH path [, path] [WHERE cond] [ONE ROW PER ...] COLUMNS (expr [AS alias], ...)) (SQL/PGQ).</summary>
public sealed partial class GraphTable : TableReference
{
    public List<Identifier> Graph { get; set; } = [];
    public List<GraphPathPattern> Match { get; set; } = [];
    public Expression? Where { get; set; }
    public GraphRowsPer? RowsPer { get; set; }
    /// <summary>The variables named in ONE ROW PER VERTEX(v) or STEP(v1, e, v2); empty for MATCH.</summary>
    public List<Identifier> RowsPerVariables { get; set; } = [];
    public List<GraphColumn> Columns { get; set; } = [];
}

public enum GraphRowsPer { Match, Vertex, Step }

/// <summary>One comma-separated path pattern; Elements alternate vertex, edge, vertex, ...</summary>
public sealed partial class GraphPathPattern : SqlNode
{
    public List<GraphPatternElement> Elements { get; set; } = [];
}

/// <summary>A vertex or edge pattern inside a path pattern.</summary>
public abstract partial class GraphPatternElement : SqlNode
{
}

/// <summary>(v IS label1|label2 WHERE cond)</summary>
public sealed partial class GraphVertexPattern : GraphPatternElement
{
    public Identifier? Variable { get; set; }
    public GraphLabelExpression? Label { get; set; }
    public Expression? Where { get; set; }
}

public enum GraphEdgeDirection { Right, Left, Any }

/// <summary>-[e IS lbl WHERE cond]-&gt;, &lt;-[...]-, -[...]-; abbreviated -&gt;, &lt;-, - when Bracketed is false.</summary>
public sealed partial class GraphEdgePattern : GraphPatternElement
{
    public GraphEdgeDirection Direction { get; set; }
    public bool Bracketed { get; set; }
    public Identifier? Variable { get; set; }
    public GraphLabelExpression? Label { get; set; }
    public Expression? Where { get; set; }
    public GraphQuantifier? Quantifier { get; set; }
}

/// <summary>Label expression: label1 | label2 | ...</summary>
public sealed partial class GraphLabelExpression : SqlNode
{
    public List<Identifier> Labels { get; set; } = [];
}

public enum GraphQuantifierKind { Star, Plus, Exact, Range }

/// <summary>Repetition of a bracketed edge: * (zero or more), + (one or more), {n} (exact), {n,m} / {n,} / {,m} (range).</summary>
public sealed partial class GraphQuantifier : SqlNode
{
    public GraphQuantifierKind Kind { get; set; }
    /// <summary>The count for Exact, or the lower bound for Range (null when omitted).</summary>
    public Expression? Min { get; set; }
    public Expression? Max { get; set; }
}

/// <summary>One COLUMNS item: expr [AS alias].</summary>
public sealed partial class GraphColumn : SqlNode
{
    public Expression Value { get; set; } = null!;
    public Identifier? Alias { get; set; }
}

// ---------- MODEL clause ----------

public enum NavMode { IgnoreNav, KeepNav }
public enum ModelUniqueness { UniqueDimension, UniqueSingleReference }
public enum ModelReturn { UpdatedRows, AllRows }
public enum RulesMode { Update, Upsert, UpsertAll }
public enum RulesOrder { Automatic, Sequential }

public sealed partial class ModelClause : SqlNode
{
    public NavMode? Nav { get; set; }
    public ModelUniqueness? Uniqueness { get; set; }
    public ModelReturn? Return { get; set; }
    public List<ReferenceModel> References { get; set; } = [];
    public Identifier? MainName { get; set; }
    public List<ExpressionSelectItem> PartitionBy { get; set; } = [];
    public List<ExpressionSelectItem> DimensionBy { get; set; } = [];
    public List<ExpressionSelectItem> Measures { get; set; } = [];
    public NavMode? MainNav { get; set; }
    public ModelUniqueness? MainUniqueness { get; set; }
    public RulesMode? RulesMode { get; set; }
    public RulesOrder? RulesOrder { get; set; }
    public Expression? Iterate { get; set; }
    public Expression? IterateUntil { get; set; }
    public List<ModelRule> Rules { get; set; } = [];
}

public sealed partial class ReferenceModel : SqlNode
{
    public Identifier Name { get; set; } = null!;
    public SelectStatement Query { get; set; } = null!;
    public List<ExpressionSelectItem> DimensionBy { get; set; } = [];
    public List<ExpressionSelectItem> Measures { get; set; } = [];
    public NavMode? Nav { get; set; }
    public ModelUniqueness? Uniqueness { get; set; }
}

public sealed partial class ModelRule : SqlNode
{
    public RulesMode? Mode { get; set; }
    public Expression Target { get; set; } = null!;
    public List<OrderByItem> OrderBy { get; set; } = [];
    public Expression Value { get; set; } = null!;
}

/// <summary>
/// Base[subscripts]: a MODEL measure, e.g. sales['x', 2000] (subscripts may be ForIterationSubscript), a PL/SQL
/// collection element, or a JSON dot-notation array step, e.g. t.c.a[0 to 2] or t.c.a[*].
/// </summary>
public sealed partial class CellReference : Expression
{
    public Expression Base { get; set; } = null!;
    public List<Expression> Subscripts { get; set; } = [];
}

/// <summary>
/// Index choice of a PL/SQL qualified expression: <c>index =&gt; value</c>, <c>OTHERS =&gt; value</c> or
/// <c>FOR i IN lo..hi =&gt; value</c>. Only the index is modeled; <c>name =&gt; value</c> with a plain name stays
/// <see cref="Argument.Name"/> on a <see cref="FunctionCall"/>, which is the same syntax for record fields.
/// </summary>
public sealed partial class QualifiedExpression : Expression
{
    /// <summary>The index or key expression; null for OTHERS and for an iterator choice.</summary>
    public Expression? Choice { get; set; }
    public bool Others { get; set; }
    public IndexIterator? Iterator { get; set; }
    public Expression Value { get; set; } = null!;
}

/// <summary>The <c>FOR variable IN low..high</c> index choice of a qualified expression.</summary>
public sealed partial class IndexIterator : SqlNode
{
    public Identifier Variable { get; set; } = null!;
    public Expression Low { get; set; } = null!;
    public Expression High { get; set; } = null!;
}

/// <summary>Inclusive range subscript <c>from TO to</c> in a JSON dot-notation array step, e.g. t.c.a[0 to 2].</summary>
public sealed partial class SubscriptRange : Expression
{
    public Expression From { get; set; } = null!;
    public Expression To { get; set; } = null!;
}

/// <summary>(VALUES (..), (..)) table value constructor, optionally followed by an alias column list.</summary>
public sealed partial class ValuesTable : TableReference
{
    public List<List<Expression>> Rows { get; set; } = [];
    public List<Identifier> ColumnAliases { get; set; } = [];
}

/// <summary>A bare VALUES row list used as a query body, e.g. the body of `WITH x(a, b) AS (VALUES (1, 2))`.</summary>
public sealed partial class ValuesQuery : QueryExpression
{
    public List<List<Expression>> Rows { get; set; } = [];
}

// ---------- Misc expressions ----------

public sealed partial class MemberAccess : Expression
{
    public Expression Target { get; set; } = null!;
    public Identifier Member { get; set; } = null!;
}

/// <summary>Method call on an expression: <c>target.name(args)</c>, e.g. <c>UTL_HTTP.REQUEST(url).foo()</c>.</summary>
public sealed partial class MethodCall : Expression
{
    public Expression Target { get; set; } = null!;
    public Identifier Name { get; set; } = null!;
    public List<Argument> Arguments { get; set; } = [];
}

/// <summary>Call on the result of a call or subscript, e.g. <c>f(x)(y)</c> or <c>arr(1)(2)</c>; the callee is not a plain name.</summary>
public sealed partial class ChainedCall : Expression
{
    public Expression Target { get; set; } = null!;
    public List<Argument> Arguments { get; set; } = [];
}

public sealed partial class CursorExpression : Expression
{
    public SelectStatement Query { get; set; } = null!;
}

public sealed partial class CurrentOfExpression : Expression
{
    public Identifier Cursor { get; set; } = null!;
}

// ---------- Multitable INSERT ----------

public sealed partial class MultiTableInsertStatement : DmlStatement
{
    public bool First { get; set; }
    public bool Conditional { get; set; }
    public List<MultiInsertBranch> Branches { get; set; } = [];
    public MultiInsertBranch? Else { get; set; }
    public SelectStatement Query { get; set; } = null!;
}

public sealed partial class MultiInsertBranch : SqlNode
{
    public Expression? When { get; set; }
    public List<InsertIntoClause> Targets { get; set; } = [];
}

public sealed partial class InsertIntoClause : SqlNode
{
    public TableReference Table { get; set; } = null!;
    public List<Identifier> Columns { get; set; } = [];
    public List<Expression> Values { get; set; } = [];
}
