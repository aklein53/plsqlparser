using System.Collections;
using System.Reflection;

namespace OracleSqlParser;

/// <summary>Base class of every AST node. Start/End are character offsets into the source text.</summary>
public abstract class SqlNode
{
    public int Start { get; set; }
    public int End { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }

    /// <summary>Direct child nodes, in property declaration order (nested lists are flattened).</summary>
    public IEnumerable<SqlNode> Children()
    {
        foreach (var p in GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.DeclaringType == typeof(SqlNode)) continue;
            foreach (var n in Nodes(p.GetValue(this))) yield return n;
        }
    }

    private static IEnumerable<SqlNode> Nodes(object? v)
    {
        if (v is SqlNode n) yield return n;
        else if (v is IEnumerable e and not string)
            foreach (var item in e)
                foreach (var c in Nodes(item)) yield return c;
    }

    /// <summary>This node and all descendants, depth-first pre-order.</summary>
    public IEnumerable<SqlNode> Descendants()
    {
        yield return this;
        foreach (var c in Children())
            foreach (var d in c.Descendants()) yield return d;
    }
}

/// <summary>Visitor base: override Visit(T) for node types of interest; call base to keep descending.</summary>
public abstract class SqlVisitor
{
    public virtual void Visit(SqlNode node)
    {
        foreach (var c in node.Children()) Visit(c);
    }
}

// ---------- Script & statements ----------

public sealed class SqlScript : SqlNode
{
    public List<Statement> Statements { get; set; } = [];
}

public abstract class Statement : SqlNode
{
    /// <summary>PL/SQL labels (&lt;&lt;name&gt;&gt;) attached to this statement.</summary>
    public List<string> Labels { get; set; } = [];
}

/// <summary>INSERT / UPDATE / DELETE / MERGE; carries an optional optimizer hint.</summary>
public abstract class DmlStatement : Statement
{
    public HintClause? Hint { get; set; }
    public List<Expression> Returning { get; set; } = [];
    public bool ReturningBulkCollect { get; set; }
    public List<Expression> ReturningInto { get; set; } = [];
    public ErrorLogging? ErrorLogging { get; set; }
}

public sealed class ErrorLogging : SqlNode
{
    public List<Identifier> Table { get; set; } = [];
    public Expression? Tag { get; set; }
    public Expression? RejectLimit { get; set; }
    public bool RejectUnlimited { get; set; }
}

/// <summary>A full query: [WITH] body [ORDER BY] [OFFSET] [FETCH] [FOR UPDATE]. Also used for subqueries.</summary>
public sealed class SelectStatement : Statement
{
    public List<CommonTableExpression> With { get; set; } = [];
    public QueryExpression Body { get; set; } = null!;
    public bool OrderSiblings { get; set; }
    public List<OrderByItem> OrderBy { get; set; } = [];
    public Expression? Offset { get; set; }
    public FetchClause? Fetch { get; set; }
    public ForUpdateClause? ForUpdate { get; set; }
    /// <summary>WITH FUNCTION / WITH PROCEDURE declarations preceding the CTEs.</summary>
    public List<Declaration> WithDeclarations { get; set; } = [];
}

public sealed class CommonTableExpression : SqlNode
{
    public Identifier Name { get; set; } = null!;
    public List<Identifier> Columns { get; set; } = [];
    public SelectStatement Query { get; set; } = null!;
    public SearchClause? Search { get; set; }
    public CycleClause? Cycle { get; set; }
}

public sealed class SearchClause : SqlNode
{
    public bool DepthFirst { get; set; }
    public List<OrderByItem> By { get; set; } = [];
    public Identifier SetColumn { get; set; } = null!;
}

public sealed class CycleClause : SqlNode
{
    public List<Identifier> Columns { get; set; } = [];
    public Identifier SetColumn { get; set; } = null!;
    public Expression? CycleValue { get; set; }
    public Expression? DefaultValue { get; set; }
}

public abstract class QueryExpression : SqlNode { }

public sealed class QuerySpecification : QueryExpression
{
    public HintClause? Hint { get; set; }
    public QuantifierKind? Quantifier { get; set; }
    public List<SelectItem> SelectList { get; set; } = [];
    public List<TableReference> From { get; set; } = [];
    public Expression? Where { get; set; }
    public Expression? StartWith { get; set; }
    public Expression? ConnectBy { get; set; }
    public bool NoCycle { get; set; }
    public List<Expression> GroupBy { get; set; } = [];
    public Expression? Having { get; set; }
    /// <summary>PL/SQL SELECT ... INTO targets.</summary>
    public List<Expression> Into { get; set; } = [];
    public bool IntoBulkCollect { get; set; }
    public ModelClause? Model { get; set; }
}

public enum QuantifierKind { All, Distinct, Unique }

public sealed class SetOperation : QueryExpression
{
    public QueryExpression Left { get; set; } = null!;
    public SetOperator Operator { get; set; }
    public bool All { get; set; }
    public QueryExpression Right { get; set; } = null!;
}

public enum SetOperator { Union, Intersect, Minus }

public sealed class ParenthesizedQuery : QueryExpression
{
    public SelectStatement Query { get; set; } = null!;
}

public abstract class SelectItem : SqlNode { }

public sealed class ExpressionSelectItem : SelectItem
{
    public Expression Expression { get; set; } = null!;
    public Identifier? Alias { get; set; }
}

/// <summary>`*` or `qualifier.*`.</summary>
public sealed class StarSelectItem : SelectItem
{
    public List<Identifier> Qualifier { get; set; } = [];
}

public sealed class OrderByItem : SqlNode
{
    public Expression Expression { get; set; } = null!;
    public bool Descending { get; set; }
    public NullsOrdering? Nulls { get; set; }
}

public enum NullsOrdering { First, Last }

public sealed class FetchClause : SqlNode
{
    public bool Next { get; set; }
    public Expression? Count { get; set; }
    public bool Percent { get; set; }
    public bool WithTies { get; set; }
}

public sealed class ForUpdateClause : SqlNode
{
    public List<ColumnReference> Of { get; set; } = [];
    public bool NoWait { get; set; }
    public bool SkipLocked { get; set; }
    public Expression? Wait { get; set; }
}

// ---------- DML ----------

public sealed class InsertStatement : DmlStatement
{
    public TableReference Target { get; set; } = null!;
    public List<Identifier> Columns { get; set; } = [];
    public List<List<Expression>> ValueRows { get; set; } = [];
    /// <summary>PL/SQL: INSERT INTO t VALUES record_variable.</summary>
    public Expression? RecordValue { get; set; }
    public SelectStatement? Query { get; set; }
}

public sealed class UpdateStatement : DmlStatement
{
    public TableReference Target { get; set; } = null!;
    public List<SetClause> Set { get; set; } = [];
    /// <summary>23ai: UPDATE ... SET ... FROM table_references.</summary>
    public List<TableReference> From { get; set; } = [];
    public Expression? Where { get; set; }
}

/// <summary>`col = expr` or `(c1, c2) = (subquery)`.</summary>
public sealed class SetClause : SqlNode
{
    /// <summary>PL/SQL: SET ROW = record (Columns is empty).</summary>
    public bool Row { get; set; }
    public List<ColumnReference> Columns { get; set; } = [];
    public Expression Value { get; set; } = null!;
}

public sealed class DeleteStatement : DmlStatement
{
    public TableReference Target { get; set; } = null!;
    public Expression? Where { get; set; }
}

public sealed class MergeStatement : DmlStatement
{
    public TableReference Target { get; set; } = null!;
    public TableReference Source { get; set; } = null!;
    public Expression On { get; set; } = null!;
    public List<MergeClause> Clauses { get; set; } = [];
}

public sealed class MergeClause : SqlNode
{
    public bool Matched { get; set; }
    public Expression? AndCondition { get; set; }
    // WHEN MATCHED THEN UPDATE
    public List<SetClause> Set { get; set; } = [];
    public Expression? UpdateWhere { get; set; }
    public Expression? DeleteWhere { get; set; }
    // WHEN NOT MATCHED THEN INSERT
    public List<Identifier> InsertColumns { get; set; } = [];
    public List<Expression> InsertValues { get; set; } = [];
    public Expression? InsertWhere { get; set; }
}

// ---------- Table references ----------

public abstract class TableReference : SqlNode
{
    public Identifier? Alias { get; set; }
}

public sealed class NamedTable : TableReference
{
    public List<Identifier> Name { get; set; } = [];
    /// <summary>Database link name after '@' (dotted parts).</summary>
    public List<Identifier> DbLink { get; set; } = [];
    public Identifier? Partition { get; set; }
    public SampleClause? Sample { get; set; }
    public FlashbackClause? Flashback { get; set; }
}

public sealed class SubqueryTable : TableReference
{
    public bool Lateral { get; set; }
    public SelectStatement Query { get; set; } = null!;
}

/// <summary>TABLE(expr) collection expression.</summary>
public sealed class TableFunction : TableReference
{
    public Expression Expression { get; set; } = null!;
}

public sealed class ParenthesizedTable : TableReference
{
    public TableReference Inner { get; set; } = null!;
}

public sealed class JoinTable : TableReference
{
    public TableReference Left { get; set; } = null!;
    public JoinKind Kind { get; set; }
    public bool Natural { get; set; }
    public TableReference Right { get; set; } = null!;
    public Expression? On { get; set; }
    public List<Identifier> Using { get; set; } = [];
}

public enum JoinKind { Inner, Left, Right, Full, Cross, CrossApply, OuterApply }

// ---------- Expressions ----------

public abstract class Expression : SqlNode { }

public sealed class Identifier : SqlNode
{
    /// <summary>Normalized: unquoted names upper-cased; quoted names verbatim.</summary>
    public string Value { get; set; } = "";
    public bool Quoted { get; set; }
    public override string ToString() => Value;
}

public sealed class ColumnReference : Expression
{
    public List<Identifier> Parts { get; set; } = [];
    /// <summary>Oracle (+) outer-join marker.</summary>
    public bool OuterJoin { get; set; }
    /// <summary>PL/SQL attribute after '%', e.g. FOUND, NOTFOUND, ROWCOUNT, TYPE, ROWTYPE.</summary>
    public string? Attribute { get; set; }
}

public enum LiteralKind { Number, String, Null, Date, Timestamp, Boolean, Default, Any }

public sealed class Literal : Expression
{
    public LiteralKind Kind { get; set; }
    /// <summary>N'...' / NQ'...' national character literal.</summary>
    public bool National { get; set; }
    public string? Value { get; set; }
}

public sealed class IntervalLiteral : Expression
{
    public Expression Value { get; set; } = null!;
    public IntervalUnit From { get; set; }
    public int? FromPrecision { get; set; }
    public IntervalUnit? To { get; set; }
    public int? ToPrecision { get; set; }
}

public enum IntervalUnit { Year, Month, Day, Hour, Minute, Second }

public sealed class BindVariable : Expression
{
    public string Name { get; set; } = "";
}

public sealed class StarExpression : Expression
{
    public List<Identifier> Qualifier { get; set; } = [];
}

public sealed class ParenthesizedExpression : Expression
{
    public Expression Inner { get; set; } = null!;
}

/// <summary>(a, b, c) row/list value.</summary>
public sealed class ExpressionList : Expression
{
    public List<Expression> Items { get; set; } = [];
}

public sealed class BinaryExpression : Expression
{
    public Expression Left { get; set; } = null!;
    /// <summary>Upper-cased operator text: + - * / ** || = &lt;&gt; &lt; &gt; &lt;= &gt;= AND OR LIKE ...</summary>
    public string Operator { get; set; } = "";
    public Expression Right { get; set; } = null!;
}

/// <summary>Comparison against a quantified set, e.g. x = ANY (subquery).</summary>
public enum ComparisonQuantifier { Any, Some, All }

public sealed class QuantifiedComparison : Expression
{
    public Expression Left { get; set; } = null!;
    public string Operator { get; set; } = "";
    public ComparisonQuantifier Quantifier { get; set; }
    public Expression Right { get; set; } = null!;
}

public sealed class UnaryExpression : Expression
{
    /// <summary>+, -, NOT, PRIOR, CONNECT_BY_ROOT, DISTINCT</summary>
    public string Operator { get; set; } = "";
    public Expression Operand { get; set; } = null!;
}

public sealed class BetweenExpression : Expression
{
    public Expression Value { get; set; } = null!;
    public bool Not { get; set; }
    public Expression Low { get; set; } = null!;
    public Expression High { get; set; } = null!;
}

public sealed class InExpression : Expression
{
    public Expression Value { get; set; } = null!;
    public bool Not { get; set; }
    public List<Expression> List { get; set; } = [];
    public SelectStatement? Subquery { get; set; }
}

public enum LikeKind { Like, Likec, Like2, Like4 }

public sealed class LikeExpression : Expression
{
    public Expression Value { get; set; } = null!;
    public bool Not { get; set; }
    public LikeKind Kind { get; set; }
    public Expression Pattern { get; set; } = null!;
    public Expression? Escape { get; set; }
}

public enum IsTest { Null, Nan, Infinite, Empty, Present, ASet, Json, OfType, Dangling }

public sealed class IsOfType : SqlNode
{
    public bool Only { get; set; }
    public DataType Type { get; set; } = null!;
}

public sealed class IsExpression : Expression
{
    public Expression Value { get; set; } = null!;
    public bool Not { get; set; }
    public IsTest Test { get; set; }
    /// <summary>IS JSON options (FORMAT JSON, STRICT/LAX, WITH/WITHOUT UNIQUE KEYS ...).</summary>
    public List<FunctionClause> JsonClauses { get; set; } = [];
    /// <summary>IS OF [TYPE] (ONLY t, ...).</summary>
    public List<IsOfType> OfTypes { get; set; } = [];
}

public sealed class ExistsExpression : Expression
{
    public SelectStatement Subquery { get; set; } = null!;
}

public sealed class SubqueryExpression : Expression
{
    public SelectStatement Query { get; set; } = null!;
}

public sealed class CaseExpression : Expression
{
    public Expression? Operand { get; set; }
    public List<WhenClause> Whens { get; set; } = [];
    public Expression? Else { get; set; }
}

public sealed class WhenClause : SqlNode
{
    public Expression When { get; set; } = null!;
    public Expression Then { get; set; } = null!;
}

public enum CastKind { Cast, XmlCast, Treat, ValidateConversion, XmlSerialize }

/// <summary>CAST / XMLCAST / TREAT / VALIDATE_CONVERSION (all of the form f(expr AS type ...)).</summary>
public sealed class CastExpression : Expression
{
    public CastKind Kind { get; set; }
    public bool Multiset { get; set; }
    public Expression Value { get; set; } = null!;
    /// <summary>TREAT(x AS REF type).</summary>
    public bool Ref { get; set; }
    public DataType Type { get; set; } = null!;
    /// <summary>DEFAULT x ON CONVERSION ERROR.</summary>
    public OnBehaviorClause? OnConversionError { get; set; }
    public Expression? Format { get; set; }
    public Expression? NlsParam { get; set; }
    /// <summary>XMLSERIALIZE options (DOCUMENT/CONTENT, ENCODING, VERSION, INDENT, DEFAULTS).</summary>
    public List<FunctionClause> Clauses { get; set; } = [];
}

public enum ExtractField
{
    Year, Month, Day, Hour, Minute, Second, TimezoneHour, TimezoneMinute, TimezoneRegion, TimezoneAbbr,
}

public sealed class ExtractExpression : Expression
{
    public ExtractField Field { get; set; }
    public Expression Source { get; set; } = null!;
}

public enum TrimSpec { Leading, Trailing, Both }

public sealed class TrimExpression : Expression
{
    public TrimSpec? Spec { get; set; }
    public Expression? Characters { get; set; }
    public Expression Source { get; set; } = null!;
}

public sealed class SubstitutionVariable : Expression
{
    public string Name { get; set; } = "";
    /// <summary>&amp;&amp;name (persistent).</summary>
    public bool Persistent { get; set; }
}

public sealed class CollateExpression : Expression
{
    public Expression Value { get; set; } = null!;
    public Identifier Collation { get; set; } = null!;
}

/// <summary>FOR dim IN (...) / FOR dim [LIKE p] FROM a TO b INCREMENT n, used inside MODEL cell subscripts.</summary>
public sealed class ForIterationSubscript : Expression
{
    public List<Identifier> Dimensions { get; set; } = [];
    public List<Expression> InList { get; set; } = [];
    public SelectStatement? InQuery { get; set; }
    public Expression? LikePattern { get; set; }
    public Expression? From { get; set; }
    public Expression? To { get; set; }
    public Expression? Step { get; set; }
    public bool Decrement { get; set; }
}

public enum CharSemantics { Char, Byte }

public sealed class DataType : SqlNode
{
    /// <summary>E.g. "VARCHAR2", "NUMBER", "TIMESTAMP WITH TIME ZONE", or a dotted user-defined type.</summary>
    public string Name { get; set; } = "";
    /// <summary>Parenthesized length/precision/scale arguments (numbers, or * for NUMBER(*,2)).</summary>
    public List<Expression> Arguments { get; set; } = [];
    public CharSemantics? Semantics { get; set; }
    /// <summary>"TYPE" or "ROWTYPE" for anchored declarations (tbl.col%TYPE).</summary>
    public string? Attribute { get; set; }
}

public sealed class AtTimeZoneExpression : Expression
{
    public Expression Value { get; set; } = null!;
    /// <summary>AT LOCAL (Zone is null).</summary>
    public bool Local { get; set; }
    public Expression? Zone { get; set; }
}

public sealed class FunctionCall : Expression
{
    public List<Identifier> Name { get; set; } = [];
    /// <summary>NEW type(...) constructor call.</summary>
    public bool New { get; set; }
    /// <summary>For SQL%BULK_ROWCOUNT(i): the attribute after '%'.</summary>
    public string? Attribute { get; set; }
    /// <summary>Trailing clauses: RETURNING, ON ERROR, FORMAT JSON, ON OVERFLOW, PASSING, ...</summary>
    public List<FunctionClause> Clauses { get; set; } = [];
    /// <summary>JSON_TRANSFORM operations (SET/INSERT/REMOVE/...).</summary>
    public List<JsonTransformOperation> Operations { get; set; } = [];
    public QuantifierKind? Quantifier { get; set; }
    public List<Argument> Arguments { get; set; } = [];
    public bool IgnoreNulls { get; set; }
    public bool RespectNulls { get; set; }
    /// <summary>LISTAGG etc. WITHIN GROUP (ORDER BY ...)</summary>
    public List<OrderByItem> WithinGroup { get; set; } = [];
    /// <summary>KEEP (DENSE_RANK FIRST|LAST ORDER BY ...)</summary>
    public KeepClause? Keep { get; set; }
    public WindowSpec? Over { get; set; }
    /// <summary>Arguments in ORDER BY inside the call (e.g. XMLAGG(... ORDER BY ...)).</summary>
    public List<OrderByItem> OrderBy { get; set; } = [];
}

public enum ArgumentKeyword { Name, EvalName, Key, Document, Content }

public sealed class Argument : SqlNode
{
    public Identifier? Name { get; set; }  // named notation: name => value
    /// <summary>Leading keyword in XML/JSON functions (XMLELEMENT(NAME x), JSON_OBJECT(KEY k ...)).</summary>
    public ArgumentKeyword? Keyword { get; set; }
    public Expression Value { get; set; } = null!;
    /// <summary>JSON_OBJECT-style value in `KEY k VALUE v` / `k : v`.</summary>
    public Expression? MappedValue { get; set; }
    /// <summary>AS alias (XMLATTRIBUTES, XMLFOREST, XMLSERIALIZE's target excluded).</summary>
    public Identifier? Alias { get; set; }
    public FormatClause? Format { get; set; }
    /// <summary>TO_NUMBER(x DEFAULT d ON CONVERSION ERROR, ...)</summary>
    public OnBehaviorClause? OnConversionError { get; set; }
}

public sealed class KeepClause : SqlNode
{
    public bool First { get; set; }
    public List<OrderByItem> OrderBy { get; set; } = [];
}

public sealed class WindowSpec : SqlNode
{
    /// <summary>OVER window_name form.</summary>
    public Identifier? Name { get; set; }
    public List<Expression> PartitionBy { get; set; } = [];
    public List<OrderByItem> OrderBy { get; set; } = [];
    public WindowFrame? Frame { get; set; }
}

public sealed class WindowFrame : SqlNode
{
    public FrameUnit Unit { get; set; }
    public new FrameBound Start { get; set; } = null!;
    public new FrameBound? End { get; set; }
}

public enum FrameUnit { Rows, Range, Groups }
public enum FrameBoundKind { UnboundedPreceding, UnboundedFollowing, CurrentRow, Preceding, Following }

public sealed class FrameBound : SqlNode
{
    public FrameBoundKind Kind { get; set; }
    public Expression? Offset { get; set; }
}
