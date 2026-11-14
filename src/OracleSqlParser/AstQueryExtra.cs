namespace OracleSqlParser;

// ---------- JSON constructors (23ai shorthand) and JSON relational duality views ----------

/// <summary>JSON { 'k' : expr [ABSENT|NULL ON NULL] [RETURNING type], ... }; JSON {*} sets Wildcard.</summary>
public sealed partial class JsonObjectConstructor : Expression
{
    /// <summary>True for the <c>*</c> wildcard form of the constructor, which has no explicit members.</summary>
    public bool Wildcard { get; set; }
    /// <summary>The key/value members, in source order; empty when there are none.</summary>
    public List<JsonMember> Members { get; set; } = [];
}

/// <summary>One member of a JSON object constructor. The value is an expression, or a <see cref="JsonArrayConstructor"/>.</summary>
public sealed partial class JsonMember : SqlNode
{
    /// <summary>The member key expression.</summary>
    public required Expression Key { get; set; }
    /// <summary>The member value expression.</summary>
    public required Expression Value { get; set; }
    /// <summary>The <c>ABSENT ON NULL</c> or <c>NULL ON NULL</c> clause, or null when absent.</summary>
    public JsonNullHandling? NullHandling { get; set; }
    /// <summary>The type after <c>RETURNING</c>, or null when absent.</summary>
    public DataType? Returning { get; set; }
}

/// <summary>How a JSON constructor treats a null value.</summary>
public enum JsonNullHandling
{
    /// <summary><c>ABSENT ON NULL</c>: a member whose value is null is omitted.</summary>
    AbsentOnNull,
    /// <summary><c>NULL ON NULL</c>: a member whose value is null is kept as a JSON null.</summary>
    NullOnNull,
}

/// <summary>JSON [ expr, ... ] or JSON [ SELECT ... ]; as a member value the JSON keyword is omitted.</summary>
public sealed partial class JsonArrayConstructor : Expression
{
    /// <summary>The array elements, in source order; empty when there are none.</summary>
    public List<Expression> Elements { get; set; } = [];
    /// <summary>The subquery of the <c>JSON [ SELECT ... ]</c> form, or null for the element-list form.</summary>
    public SelectStatement? Query { get; set; }
}

/// <summary>A JSON relational duality view annotation written after a table, as in <c>WITH INSERT UPDATE DELETE</c>.</summary>
public enum DualityAnnotation
{
    /// <summary><c>WITH INSERT</c>: rows can be inserted through the view.</summary>
    Insert,
    /// <summary><c>WITH UPDATE</c>: rows can be updated through the view.</summary>
    Update,
    /// <summary><c>WITH DELETE</c>: rows can be deleted through the view.</summary>
    Delete,
    /// <summary><c>WITH NOINSERT</c>: inserts through the view are not allowed.</summary>
    NoInsert,
    /// <summary><c>WITH NOUPDATE</c>: updates through the view are not allowed.</summary>
    NoUpdate,
    /// <summary><c>WITH NODELETE</c>: deletes through the view are not allowed.</summary>
    NoDelete,
    /// <summary><c>WITH NOCHECK</c>: the write-time check is disabled for the table.</summary>
    NoCheck,
    /// <summary><c>WITH ETAG</c>: an etag check is applied on write.</summary>
    Etag,
    /// <summary><c>WITH NO ETAG</c>: no etag check is applied.</summary>
    NoEtag,
}

/// <summary>One annotation of a JSON relational duality view table, such as <c>WITH NOCHECK</c>.</summary>
public sealed partial class DualityAnnotationItem : SqlNode
{
    /// <summary>The annotation written.</summary>
    public DualityAnnotation Kind { get; set; }
}

/// <summary>CREATE [OR REPLACE] JSON [RELATIONAL] DUALITY VIEW v AS SELECT JSON {...} FROM ... (SQL form; the GraphQL form is not modelled).</summary>
public sealed partial class CreateDualityViewStatement : Statement
{
    /// <summary>True when <c>OR REPLACE</c> is written.</summary>
    public bool OrReplace { get; set; }
    /// <summary>True when <c>RELATIONAL</c> is written before <c>DUALITY VIEW</c>.</summary>
    public bool Relational { get; set; }
    /// <summary>The view name parts, schema first when the name is qualified.</summary>
    public List<Identifier> Name { get; set; } = [];
    /// <summary>The SQL form's query; null for the GraphQL form (kept in <see cref="OtherClauses"/>).</summary>
    public SelectStatement? Query { get; set; }
    /// <summary>The GraphQL form (v AS t @insert @update { ... }), as a token tree.</summary>
    public List<SyntaxItem> OtherClauses { get; set; } = [];
}

// ---------- Table reference modifiers ----------

/// <summary>The <c>SAMPLE</c> clause of a table reference, such as <c>SAMPLE BLOCK (10) SEED (1)</c>.</summary>
public sealed partial class SampleClause : SqlNode
{
    /// <summary>True when <c>BLOCK</c> is written, sampling whole blocks instead of rows.</summary>
    public bool Block { get; set; }
    /// <summary>The sample percentage in <c>SAMPLE (percent)</c>.</summary>
    public required Expression Percent { get; set; }
    /// <summary>The seed in <c>SEED (n)</c>, or null when absent.</summary>
    public Expression? Seed { get; set; }
}

/// <summary>The form of a flashback clause.</summary>
public enum FlashbackMode
{
    /// <summary><c>AS OF</c>: the rows as they were at a point in time.</summary>
    AsOf,
    /// <summary><c>VERSIONS BETWEEN</c>: every version of the rows between two points.</summary>
    VersionsBetween,
}
/// <summary>What a flashback clause points to.</summary>
public enum FlashbackUnit
{
    /// <summary><c>SCN</c>: a system change number.</summary>
    Scn,
    /// <summary><c>TIMESTAMP</c>: a point in time.</summary>
    Timestamp,
    /// <summary><c>PERIOD FOR</c>: a valid-time period.</summary>
    Period,
}

/// <summary>AS OF SCN|TIMESTAMP|PERIOD FOR p expr, or VERSIONS BETWEEN SCN|TIMESTAMP a AND b.</summary>
public sealed partial class FlashbackClause : SqlNode
{
    /// <summary>Whether the clause is <c>AS OF</c> or <c>VERSIONS BETWEEN</c>.</summary>
    public FlashbackMode Mode { get; set; }
    /// <summary>What the value is: an SCN, a timestamp or a valid-time period.</summary>
    public FlashbackUnit Unit { get; set; }
    /// <summary>The valid-time period name for PERIOD FOR.</summary>
    public Identifier? Period { get; set; }
    /// <summary>The SCN or timestamp expression, or the start of the range for <c>VERSIONS BETWEEN</c>.</summary>
    public required Expression Value { get; set; }
    /// <summary>The end of the range for <c>VERSIONS BETWEEN</c>; null for <c>AS OF</c>.</summary>
    public Expression? ToValue { get; set; }
}

/// <summary>A <c>PIVOT</c> table reference, such as <c>SELECT * FROM t PIVOT (SUM(s) FOR m IN ('a' AS a))</c>.</summary>
public sealed partial class PivotTable : TableReference
{
    /// <summary>The table being pivoted.</summary>
    public required TableReference Source { get; set; }
    /// <summary>True for <c>PIVOT XML</c>.</summary>
    public bool Xml { get; set; }
    /// <summary>The aggregate functions, each with an optional alias, in source order.</summary>
    public List<PivotAggregate> Aggregates { get; set; } = [];
    /// <summary>The column or columns named after <c>FOR</c>.</summary>
    public List<ColumnReference> ForColumns { get; set; } = [];
    /// <summary>The values listed in <c>IN (...)</c>, in source order; empty for <c>IN ANY</c> and <c>IN (subquery)</c>.</summary>
    public List<PivotInItem> InItems { get; set; } = [];
    /// <summary>True for <c>IN (ANY)</c>.</summary>
    public bool InAny { get; set; }
    /// <summary>The subquery of <c>IN (subquery)</c>, or null when the values are listed.</summary>
    public SelectStatement? InSubquery { get; set; }
}

/// <summary>One aggregate in a <c>PIVOT</c> clause, such as <c>SUM(sal) AS total</c>.</summary>
public sealed partial class PivotAggregate : SqlNode
{
    /// <summary>The aggregate function call.</summary>
    public required Expression Function { get; set; }
    /// <summary>The alias after the aggregate, or null when absent.</summary>
    public Identifier? Alias { get; set; }
}

/// <summary>One value in the <c>IN</c> list of a <c>PIVOT</c> clause, such as <c>'a' AS a</c>.</summary>
public sealed partial class PivotInItem : SqlNode
{
    /// <summary>The value compared with the <c>FOR</c> column.</summary>
    public required Expression Value { get; set; }
    /// <summary>The alias of the value, or null when absent.</summary>
    public Identifier? Alias { get; set; }
}

/// <summary>An <c>UNPIVOT</c> table reference, such as <c>SELECT * FROM t UNPIVOT (v FOR m IN (a, b))</c>.</summary>
public sealed partial class UnpivotTable : TableReference
{
    /// <summary>The table being unpivoted.</summary>
    public required TableReference Source { get; set; }
    /// <summary>True for <c>INCLUDE NULLS</c>; false for <c>EXCLUDE NULLS</c> or when neither is written.</summary>
    public bool IncludeNulls { get; set; }
    /// <summary>The new value column or columns named before <c>FOR</c>.</summary>
    public List<ColumnReference> ValueColumns { get; set; } = [];
    /// <summary>The new name column or columns named after <c>FOR</c>.</summary>
    public List<ColumnReference> ForColumns { get; set; } = [];
    /// <summary>The columns listed in <c>IN (...)</c>, in source order.</summary>
    public List<UnpivotInItem> InItems { get; set; } = [];
}

/// <summary>One entry of the <c>IN</c> list of an <c>UNPIVOT</c> clause, such as <c>(a, b) AS 'x'</c>.</summary>
public sealed partial class UnpivotInItem : SqlNode
{
    /// <summary>The source column or columns of the entry.</summary>
    public List<ColumnReference> Columns { get; set; } = [];
    /// <summary>The value after <c>AS</c> that labels the entry, or null when absent.</summary>
    public Expression? AliasValue { get; set; }
}

/// <summary>One namespace declaration of <c>XMLTABLE</c>, such as <c>XMLNAMESPACES ('urn:x' AS x)</c>.</summary>
public sealed partial class XmlNamespace : SqlNode
{
    /// <summary>The namespace URI expression.</summary>
    public required Expression Uri { get; set; }
    /// <summary>The prefix after <c>AS</c>, or null for the default namespace.</summary>
    public Identifier? Alias { get; set; }
    /// <summary>True for <c>DEFAULT 'uri'</c>, which declares the default namespace; Alias is then null.</summary>
    public bool IsDefault { get; set; }
}

/// <summary>XMLTABLE(...) in a FROM clause.</summary>
public sealed partial class XmlTable : TableReference
{
    /// <summary>The namespace declarations, in source order; empty when there are none.</summary>
    public List<XmlNamespace> Namespaces { get; set; } = [];
    /// <summary>The XQuery expression that selects the rows.</summary>
    public required Expression XQuery { get; set; }
    /// <summary>The <c>PASSING</c> clause, or null when absent.</summary>
    public PassingClause? Passing { get; set; }
    /// <summary>The column definitions in <c>COLUMNS</c>, in source order.</summary>
    public List<PathColumn> Columns { get; set; } = [];
}

/// <summary>JSON_TABLE(...) in a FROM clause.</summary>
public sealed partial class JsonTable : TableReference
{
    /// <summary>The JSON input expression.</summary>
    public required Expression Input { get; set; }
    /// <summary>The <c>FORMAT JSON</c> clause on the input, or null when absent.</summary>
    public FormatClause? InputFormat { get; set; }
    /// <summary>The JSON path expression that selects the rows.</summary>
    public required Expression Path { get; set; }
    /// <summary>ERROR/EMPTY ON ... clauses after the path.</summary>
    public List<FunctionClause> Clauses { get; set; } = [];
    /// <summary>The column definitions in <c>COLUMNS</c>, in source order.</summary>
    public List<PathColumn> Columns { get; set; } = [];
}

/// <summary>Column definition inside XMLTABLE/JSON_TABLE COLUMNS (...).</summary>
public sealed partial class PathColumn : SqlNode
{
    /// <summary>The column name, or null for a nested path.</summary>
    public Identifier? Name { get; set; }
    /// <summary>True for <c>FOR ORDINALITY</c>, a row number column.</summary>
    public bool ForOrdinality { get; set; }
    /// <summary>The column's data type, or null when absent.</summary>
    public DataType? Type { get; set; }
    /// <summary>True for an <c>EXISTS PATH</c> column, which yields whether the path matches rather than a value.</summary>
    public bool Exists { get; set; }
    /// <summary>FORMAT JSON, TRUNCATE, WITH WRAPPER, ON ERROR/EMPTY, ... (in source order).</summary>
    public List<FunctionClause> Clauses { get; set; } = [];
    /// <summary>The path that selects the column's value, or null when absent.</summary>
    public Expression? Path { get; set; }
    /// <summary>The <c>DEFAULT</c> value used when there is no match, or null when absent.</summary>
    public Expression? Default { get; set; }
    /// <summary>True for NESTED [PATH] 'p' COLUMNS (...); Path holds the nested path.</summary>
    public bool Nested { get; set; }
    /// <summary>The columns of a <c>NESTED PATH</c> block; empty for an ordinary column.</summary>
    public List<PathColumn> Columns { get; set; } = [];
}

// ---------- MATCH_RECOGNIZE ----------

/// <summary>The row output of <c>MATCH_RECOGNIZE</c>.</summary>
public enum RowsPerMatch
{
    /// <summary><c>ONE ROW PER MATCH</c>.</summary>
    OneRow,
    /// <summary><c>ALL ROWS PER MATCH</c>.</summary>
    AllRows,
}
/// <summary>The handling of empty matches under <c>ALL ROWS PER MATCH</c>.</summary>
public enum AllRowsMode
{
    /// <summary><c>SHOW EMPTY MATCHES</c>.</summary>
    ShowEmptyMatches,
    /// <summary><c>OMIT EMPTY MATCHES</c>.</summary>
    OmitEmptyMatches,
    /// <summary><c>WITH UNMATCHED ROWS</c>.</summary>
    WithUnmatchedRows,
}
/// <summary>Where the next match starts after a match, from <c>AFTER MATCH SKIP</c>.</summary>
public enum SkipKind
{
    /// <summary><c>PAST LAST ROW</c>.</summary>
    PastLastRow,
    /// <summary><c>TO NEXT ROW</c>.</summary>
    ToNextRow,
    /// <summary><c>TO FIRST</c> of a named pattern variable.</summary>
    ToFirst,
    /// <summary><c>TO LAST</c> of a named pattern variable.</summary>
    ToLast,
}

/// <summary>A <c>MATCH_RECOGNIZE</c> table reference that finds row patterns in the rows of its source.</summary>
public sealed partial class MatchRecognizeTable : TableReference
{
    /// <summary>The table whose rows are matched.</summary>
    public required TableReference Source { get; set; }
    /// <summary>The <c>PARTITION BY</c> expressions; empty when absent.</summary>
    public List<Expression> PartitionBy { get; set; } = [];
    /// <summary>The <c>ORDER BY</c> items within each partition; empty when absent.</summary>
    public List<OrderByItem> OrderBy { get; set; } = [];
    /// <summary>The <c>MEASURES</c> expressions, each with an optional alias; empty when absent.</summary>
    public List<ExpressionSelectItem> Measures { get; set; } = [];
    /// <summary>The <c>ONE ROW</c> or <c>ALL ROWS PER MATCH</c> setting, or null when absent.</summary>
    public RowsPerMatch? RowsPerMatch { get; set; }
    /// <summary>The empty-match handling under <c>ALL ROWS PER MATCH</c>, or null when absent.</summary>
    public AllRowsMode? AllRowsMode { get; set; }
    /// <summary>The <c>AFTER MATCH SKIP</c> setting, or null when absent.</summary>
    public SkipKind? Skip { get; set; }
    /// <summary>The pattern variable named after <c>TO FIRST</c> or <c>TO LAST</c>, or null otherwise.</summary>
    public Identifier? SkipVariable { get; set; }
    /// <summary>PATTERN is preceded by SEEK instead of INITIAL.</summary>
    public bool Seek { get; set; }
    /// <summary>The row pattern in <c>PATTERN (...)</c>.</summary>
    public required PatternNode Pattern { get; set; }
    /// <summary>The <c>SUBSET</c> definitions, in source order; empty when absent.</summary>
    public List<PatternSubset> Subsets { get; set; } = [];
    /// <summary>The <c>DEFINE</c> conditions, in source order.</summary>
    public List<PatternDefine> Define { get; set; } = [];
}

/// <summary>A node of a <c>MATCH_RECOGNIZE</c> row pattern.</summary>
public abstract partial class PatternNode : SqlNode { }
/// <summary>A pattern variable, such as <c>A</c> in <c>PATTERN (A B+)</c>.</summary>
public sealed partial class PatternVariable : PatternNode
{
    /// <summary>The variable name.</summary>
    public required Identifier Name { get; set; }
}
/// <summary>A row anchor in a pattern: <c>^</c> for the start or <c>$</c> for the end.</summary>
public sealed partial class PatternAnchor : PatternNode
{
    /// <summary>True for <c>^</c> (the start of the partition); false for <c>$</c> (the end).</summary>
    public bool IsStart { get; set; }
}
/// <summary>Adjacent pattern elements in sequence, such as <c>A B</c>.</summary>
public sealed partial class PatternConcat : PatternNode
{
    /// <summary>The elements, in order.</summary>
    public List<PatternNode> Items { get; set; } = [];
}
/// <summary>Alternative pattern elements, such as <c>A | B</c>.</summary>
public sealed partial class PatternAlternation : PatternNode
{
    /// <summary>The alternatives, in source order.</summary>
    public List<PatternNode> Items { get; set; } = [];
}
/// <summary>A parenthesized pattern, such as <c>(A B)</c>.</summary>
public sealed partial class PatternGroup : PatternNode
{
    /// <summary>The grouped pattern.</summary>
    public required PatternNode Inner { get; set; }
}
/// <summary><c>PERMUTE (A, B)</c>: every order of the listed elements.</summary>
public sealed partial class PatternPermute : PatternNode
{
    /// <summary>The elements that are permuted.</summary>
    public List<PatternNode> Items { get; set; } = [];
}
/// <summary>An exclusion, <c>{- A -}</c>: rows matching the inner pattern are not part of the output.</summary>
public sealed partial class PatternExclusion : PatternNode
{
    /// <summary>The excluded pattern.</summary>
    public required PatternNode Inner { get; set; }
}

/// <summary>A pattern element with a quantifier, such as <c>A+</c> or <c>B{2,3}?</c>.</summary>
public sealed partial class PatternQuantified : PatternNode
{
    /// <summary>The element being repeated.</summary>
    public required PatternNode Inner { get; set; }
    /// <summary>The minimum number of repetitions.</summary>
    public int Min { get; set; }
    /// <summary>The maximum number of repetitions, or null when there is no upper bound (<c>+</c>, <c>*</c>).</summary>
    public int? Max { get; set; }
    /// <summary>True when the quantifier is reluctant (followed by <c>?</c>).</summary>
    public bool Reluctant { get; set; }
}

/// <summary>A <c>SUBSET</c> definition that groups pattern variables under one name.</summary>
public sealed partial class PatternSubset : SqlNode
{
    /// <summary>The subset name.</summary>
    public required Identifier Name { get; set; }
    /// <summary>The variables in the subset.</summary>
    public List<Identifier> Members { get; set; } = [];
}

/// <summary>One <c>DEFINE</c> entry: a pattern variable and the condition that its rows must satisfy.</summary>
public sealed partial class PatternDefine : SqlNode
{
    /// <summary>The pattern variable being defined.</summary>
    public required Identifier Variable { get; set; }
    /// <summary>The condition a row must satisfy to match the variable.</summary>
    public required Expression Condition { get; set; }
}

/// <summary>GRAPH_TABLE(graph MATCH path [, path] [WHERE cond] [ONE ROW PER ...] COLUMNS (expr [AS alias], ...)) (SQL/PGQ).</summary>
public sealed partial class GraphTable : TableReference
{
    /// <summary>The property graph name parts, schema first when qualified.</summary>
    public List<Identifier> Graph { get; set; } = [];
    /// <summary>The path patterns in <c>MATCH</c>, in source order.</summary>
    public List<GraphPathPattern> Match { get; set; } = [];
    /// <summary>The <c>WHERE</c> condition, or null when absent.</summary>
    public Expression? Where { get; set; }
    /// <summary>The <c>ONE ROW PER</c> setting, or null when absent.</summary>
    public GraphRowsPer? RowsPer { get; set; }
    /// <summary>The variables named in ONE ROW PER VERTEX(v) or STEP(v1, e, v2); empty for MATCH.</summary>
    public List<Identifier> RowsPerVariables { get; set; } = [];
    /// <summary>The output columns in <c>COLUMNS (...)</c>, in source order.</summary>
    public List<GraphColumn> Columns { get; set; } = [];
}

/// <summary>The row output of <c>GRAPH_TABLE</c>.</summary>
public enum GraphRowsPer
{
    /// <summary><c>ONE ROW PER MATCH</c>.</summary>
    Match,
    /// <summary><c>ONE ROW PER VERTEX</c>.</summary>
    Vertex,
    /// <summary><c>ONE ROW PER STEP</c>.</summary>
    Step,
}

/// <summary>One comma-separated path pattern; Elements alternate vertex, edge, vertex, ...</summary>
public sealed partial class GraphPathPattern : SqlNode
{
    /// <summary>The vertex, edge and subpattern elements, in path order.</summary>
    public List<GraphPatternElement> Elements { get; set; } = [];
}

/// <summary>A vertex or edge pattern inside a path pattern.</summary>
public abstract partial class GraphPatternElement : SqlNode
{
}

/// <summary>(v IS label1|label2 WHERE cond)</summary>
public sealed partial class GraphVertexPattern : GraphPatternElement
{
    /// <summary>The vertex variable, or null for an anonymous vertex.</summary>
    public Identifier? Variable { get; set; }
    /// <summary>The label expression after <c>IS</c>, or null when absent.</summary>
    public GraphLabelExpression? Label { get; set; }
    /// <summary>The <c>WHERE</c> condition on the vertex, or null when absent.</summary>
    public Expression? Where { get; set; }
}

/// <summary>The direction of an edge pattern.</summary>
public enum GraphEdgeDirection
{
    /// <summary>Directed from left to right, written <c>-&gt;</c>.</summary>
    Right,
    /// <summary>Directed from right to left, written <c>&lt;-</c>.</summary>
    Left,
    /// <summary>Either direction, written <c>-</c>.</summary>
    Any,
    /// <summary>Either direction with brackets, written <c>&lt;-[...]-&gt;</c>.</summary>
    LeftOrRight,
}

/// <summary>-[e IS lbl WHERE cond]-&gt;, &lt;-[...]-, -[...]-; abbreviated -&gt;, &lt;-, - when Bracketed is false.</summary>
public sealed partial class GraphEdgePattern : GraphPatternElement
{
    /// <summary>The direction of the edge.</summary>
    public GraphEdgeDirection Direction { get; set; }
    /// <summary>True when the edge has brackets, <c>-[e]-&gt;</c>; false for the abbreviated form.</summary>
    public bool Bracketed { get; set; }
    /// <summary>The edge variable, or null for an anonymous edge.</summary>
    public Identifier? Variable { get; set; }
    /// <summary>The label expression after <c>IS</c>, or null when absent.</summary>
    public GraphLabelExpression? Label { get; set; }
    /// <summary>The <c>WHERE</c> condition on the edge, or null when absent.</summary>
    public Expression? Where { get; set; }
    /// <summary>The quantifier on the edge, or null when absent.</summary>
    public GraphQuantifier? Quantifier { get; set; }
}

/// <summary>Label expression: label1 | label2 | ...</summary>
public sealed partial class GraphLabelExpression : SqlNode
{
    /// <summary>The labels, joined by <c>|</c> in the source.</summary>
    public List<Identifier> Labels { get; set; } = [];
}

/// <summary>The form of a graph quantifier.</summary>
public enum GraphQuantifierKind
{
    /// <summary>A single count, such as <c>{3}</c>.</summary>
    Exact,
    /// <summary>A range of counts, such as <c>{1,3}</c>.</summary>
    Range,
}

/// <summary>Repetition of an edge or group: {n} (exact) or {n,m} / {,m} (range). An open-ended {n,} is not supported.</summary>
public sealed partial class GraphQuantifier : SqlNode
{
    /// <summary>Whether the quantifier is an exact count or a range.</summary>
    public GraphQuantifierKind Kind { get; set; }
    /// <summary>The count for Exact, or the lower bound for Range (null when omitted).</summary>
    public Expression? Min { get; set; }
    /// <summary>The upper bound of a range; null for <c>Exact</c>, where <see cref="Min"/> holds the count.</summary>
    public Expression? Max { get; set; }
}

/// <summary>( path ) quantifier: a parenthesised sub-path pattern that repeats.</summary>
public sealed partial class GraphSubpatternElement : GraphPatternElement
{
    /// <summary>The path pattern inside the parentheses.</summary>
    public required GraphPathPattern Path { get; set; }
    /// <summary>The quantifier after the subpattern, or null when absent.</summary>
    public GraphQuantifier? Quantifier { get; set; }
}

/// <summary>One COLUMNS item: expr [AS alias].</summary>
public sealed partial class GraphColumn : SqlNode
{
    /// <summary>The expression for the column's value.</summary>
    public required Expression Value { get; set; }
    /// <summary>The column alias after <c>AS</c>, or null when absent.</summary>
    public Identifier? Alias { get; set; }
}

// ---------- MODEL clause ----------

/// <summary>Whether navigation is applied in a <c>MODEL</c> clause.</summary>
public enum NavMode
{
    /// <summary><c>IGNORE NAV</c>: missing cells are treated as no value.</summary>
    IgnoreNav,
    /// <summary><c>KEEP NAV</c>: missing cells are navigated.</summary>
    KeepNav,
}
/// <summary>The uniqueness rule of a <c>MODEL</c> clause, from <c>UNIQUE</c> or <c>UNIQUE SINGLE REFERENCE</c>.</summary>
public enum ModelUniqueness
{
    /// <summary><c>UNIQUE DIMENSION</c>: each cell is addressed by its dimensions.</summary>
    UniqueDimension,
    /// <summary><c>UNIQUE SINGLE REFERENCE</c>: dimension values are unique across references.</summary>
    UniqueSingleReference,
}
/// <summary>What a <c>MODEL</c> clause returns.</summary>
public enum ModelReturn
{
    /// <summary><c>RETURN UPDATED ROWS</c>.</summary>
    UpdatedRows,
    /// <summary><c>RETURN ALL ROWS</c>.</summary>
    AllRows,
}
/// <summary>The mode of a <c>MODEL</c> rules clause.</summary>
public enum RulesMode
{
    /// <summary><c>UPDATE</c>: only existing cells can be assigned.</summary>
    Update,
    /// <summary><c>UPSERT</c>: existing cells are updated and missing ones are inserted.</summary>
    Upsert,
    /// <summary><c>UPSERT ALL</c>: as <c>UPSERT</c>, for all rules including those without a match.</summary>
    UpsertAll,
}
/// <summary>The evaluation order of a <c>MODEL</c> rules clause.</summary>
public enum RulesOrder
{
    /// <summary><c>AUTOMATIC ORDER</c>: the database orders the rules by dependency.</summary>
    Automatic,
    /// <summary><c>SEQUENTIAL ORDER</c>: the rules run in the order written.</summary>
    Sequential,
}

/// <summary>The <c>MODEL</c> clause of a query: spreadsheet-style rules over multidimensional cells.</summary>
public sealed partial class ModelClause : SqlNode
{
    /// <summary>The navigation mode, or null when absent.</summary>
    public NavMode? Nav { get; set; }
    /// <summary>The uniqueness rule, or null when absent.</summary>
    public ModelUniqueness? Uniqueness { get; set; }
    /// <summary>The <c>RETURN</c> setting, or null when absent.</summary>
    public ModelReturn? Return { get; set; }
    /// <summary>The <c>REFERENCE</c> models, in source order; empty when there are none.</summary>
    public List<ReferenceModel> References { get; set; } = [];
    /// <summary>The name of the main model, or null when absent.</summary>
    public Identifier? MainName { get; set; }
    /// <summary>The <c>PARTITION BY</c> expressions; empty when absent.</summary>
    public List<ExpressionSelectItem> PartitionBy { get; set; } = [];
    /// <summary>The <c>DIMENSION BY</c> expressions; empty when absent.</summary>
    public List<ExpressionSelectItem> DimensionBy { get; set; } = [];
    /// <summary>The <c>MEASURES</c> expressions; empty when absent.</summary>
    public List<ExpressionSelectItem> Measures { get; set; } = [];
    /// <summary>The navigation mode of the main model, or null when absent.</summary>
    public NavMode? MainNav { get; set; }
    /// <summary>The uniqueness rule of the main model, or null when absent.</summary>
    public ModelUniqueness? MainUniqueness { get; set; }
    /// <summary>The mode of the rules clause, or null when absent.</summary>
    public RulesMode? RulesMode { get; set; }
    /// <summary>The evaluation order of the rules, or null when absent.</summary>
    public RulesOrder? RulesOrder { get; set; }
    /// <summary>The count in <c>ITERATE (n)</c>, or null when absent.</summary>
    public Expression? Iterate { get; set; }
    /// <summary>The condition in <c>UNTIL (condition)</c>, or null when absent.</summary>
    public Expression? IterateUntil { get; set; }
    /// <summary>The rules, in source order; empty when there are none.</summary>
    public List<ModelRule> Rules { get; set; } = [];
}

/// <summary>A <c>REFERENCE</c> model of a <c>MODEL</c> clause: a named query whose cells are looked up by the rules.</summary>
public sealed partial class ReferenceModel : SqlNode
{
    /// <summary>The reference model name.</summary>
    public required Identifier Name { get; set; }
    /// <summary>The query that produces the reference cells.</summary>
    public required SelectStatement Query { get; set; }
    /// <summary>The <c>DIMENSION BY</c> expressions; empty when absent.</summary>
    public List<ExpressionSelectItem> DimensionBy { get; set; } = [];
    /// <summary>The <c>MEASURES</c> expressions; empty when absent.</summary>
    public List<ExpressionSelectItem> Measures { get; set; } = [];
    /// <summary>The navigation mode, or null when absent.</summary>
    public NavMode? Nav { get; set; }
    /// <summary>The uniqueness rule, or null when absent.</summary>
    public ModelUniqueness? Uniqueness { get; set; }
}

/// <summary>One rule of a <c>MODEL</c> clause, such as <c>s['x', 2002] = s['x', 2001] * 1.1</c>.</summary>
public sealed partial class ModelRule : SqlNode
{
    /// <summary>The <c>UPDATE</c> or <c>UPSERT</c> mode of the rule, or null when the rules clause sets the mode.</summary>
    public RulesMode? Mode { get; set; }
    /// <summary>The cell or cells the rule assigns, such as <c>s['x', 2002]</c>.</summary>
    public required Expression Target { get; set; }
    /// <summary>The <c>ORDER BY</c> items that choose the cells in a rule; empty when absent.</summary>
    public List<OrderByItem> OrderBy { get; set; } = [];
    /// <summary>The expression assigned to the target cells.</summary>
    public required Expression Value { get; set; }
}

/// <summary>
/// Base[subscripts]: a MODEL measure, e.g. sales['x', 2000] (subscripts may be ForIterationSubscript), a PL/SQL
/// collection element, or a JSON dot-notation array step, e.g. t.c.a[0 to 2] or t.c.a[*].
/// </summary>
public sealed partial class CellReference : Expression
{
    /// <summary>The array or cell being subscripted.</summary>
    public required Expression Base { get; set; }
    /// <summary>The subscripts, in source order.</summary>
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
    /// <summary>True for the <c>OTHERS</c> choice of a PL/SQL qualified expression.</summary>
    public bool Others { get; set; }
    /// <summary>The <c>FOR</c> iterator of the expression, or null when absent.</summary>
    public IndexIterator? Iterator { get; set; }
    /// <summary>The value assigned to the chosen index.</summary>
    public required Expression Value { get; set; }
}

/// <summary>The <c>FOR variable IN low..high</c> index choice of a qualified expression.</summary>
public sealed partial class IndexIterator : SqlNode
{
    /// <summary>The iteration variable.</summary>
    public required Identifier Variable { get; set; }
    /// <summary>The first value of the range.</summary>
    public required Expression Low { get; set; }
    /// <summary>The last value of the range.</summary>
    public required Expression High { get; set; }
}

/// <summary>Inclusive range subscript <c>from TO to</c> in a JSON dot-notation array step, e.g. t.c.a[0 to 2].</summary>
public sealed partial class SubscriptRange : Expression
{
    /// <summary>The first index of the range.</summary>
    public required Expression From { get; set; }
    /// <summary>The last index of the range.</summary>
    public required Expression To { get; set; }
}

/// <summary>(VALUES (..), (..)) table value constructor, optionally followed by an alias column list.</summary>
public sealed partial class ValuesTable : TableReference
{
    /// <summary>The rows of the <c>VALUES</c> list, each a list of expressions.</summary>
    public List<List<Expression>> Rows { get; set; } = [];
    /// <summary>The column aliases written after the table alias; empty when absent.</summary>
    public List<Identifier> ColumnAliases { get; set; } = [];
}

/// <summary>A bare VALUES row list used as a query body, e.g. the body of `WITH x(a, b) AS (VALUES (1, 2))`.</summary>
public sealed partial class ValuesQuery : QueryExpression
{
    /// <summary>The rows of the <c>VALUES</c> list, each a list of expressions.</summary>
    public List<List<Expression>> Rows { get; set; } = [];
}

// ---------- Misc expressions ----------

/// <summary>An attribute or member access, such as <c>obj.attr</c>.</summary>
public sealed partial class MemberAccess : Expression
{
    /// <summary>The object whose member is read.</summary>
    public required Expression Target { get; set; }
    /// <summary>The member name.</summary>
    public required Identifier Member { get; set; }
}

/// <summary>Method call on an expression: <c>target.name(args)</c>, e.g. <c>UTL_HTTP.REQUEST(url).foo()</c>.</summary>
public sealed partial class MethodCall : Expression
{
    /// <summary>The object the method is called on.</summary>
    public required Expression Target { get; set; }
    /// <summary>The method name.</summary>
    public required Identifier Name { get; set; }
    /// <summary>The arguments, in source order; empty when there are none.</summary>
    public List<Argument> Arguments { get; set; } = [];
}

/// <summary>Call on the result of a call or subscript, e.g. <c>f(x)(y)</c> or <c>arr(1)(2)</c>; the callee is not a plain name.</summary>
public sealed partial class ChainedCall : Expression
{
    /// <summary>The expression the call is chained onto.</summary>
    public required Expression Target { get; set; }
    /// <summary>The arguments, in source order; empty when there are none.</summary>
    public List<Argument> Arguments { get; set; } = [];
}

/// <summary>A nested cursor, <c>CURSOR (SELECT ...)</c>, used as a column value.</summary>
public sealed partial class CursorExpression : Expression
{
    /// <summary>The query of <c>CURSOR (SELECT ...)</c>.</summary>
    public required SelectStatement Query { get; set; }
}

/// <summary>The <c>CURRENT OF cursor</c> condition of a <c>WHERE</c> clause in a positioned update or delete.</summary>
public sealed partial class CurrentOfExpression : Expression
{
    /// <summary>The cursor named in <c>CURRENT OF</c>.</summary>
    public required Identifier Cursor { get; set; }
}

// ---------- Multitable INSERT ----------

/// <summary><c>INSERT ALL</c> or <c>INSERT FIRST</c>: one query inserts into several tables.</summary>
public sealed partial class MultiTableInsertStatement : DmlStatement
{
    /// <summary>True when <c>FIRST</c> is written: only the first matching branch inserts.</summary>
    public bool First { get; set; }
    /// <summary>True for the <c>WHEN ... THEN</c> form; false for <c>INSERT ALL</c> without conditions.</summary>
    public bool Conditional { get; set; }
    /// <summary>The <c>WHEN ... THEN</c> branches in source order; for the unconditional form, the single list of <c>INTO</c> clauses.</summary>
    public List<MultiInsertBranch> Branches { get; set; } = [];
    /// <summary>The <c>ELSE</c> branch, or null when absent.</summary>
    public MultiInsertBranch? Else { get; set; }
    /// <summary>The query that supplies the rows.</summary>
    public required SelectStatement Query { get; set; }
}

/// <summary>One branch of a multitable <c>INSERT</c>, such as <c>WHEN x &gt; 1 THEN INTO t VALUES (x)</c>.</summary>
public sealed partial class MultiInsertBranch : SqlNode
{
    /// <summary>The condition of the branch, or null for the unconditional form and for <c>ELSE</c>.</summary>
    public Expression? When { get; set; }
    /// <summary>The <c>INTO</c> clauses of the branch, in source order.</summary>
    public List<InsertIntoClause> Targets { get; set; } = [];
}

/// <summary>One <c>INTO</c> clause of a multitable <c>INSERT</c>, such as <c>INTO t (a) VALUES (x)</c>.</summary>
public sealed partial class InsertIntoClause : SqlNode
{
    /// <summary>The table inserted into.</summary>
    public required TableReference Table { get; set; }
    /// <summary>The column list, in source order; empty when absent.</summary>
    public List<Identifier> Columns { get; set; } = [];
    /// <summary>The <c>VALUES</c> expressions, in source order; empty when absent.</summary>
    public List<Expression> Values { get; set; } = [];
}
