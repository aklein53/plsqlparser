namespace OracleSqlParser;

/// <summary>Marks a property that records source spelling only (not meaning); <see cref="AstPrinter"/> leaves it out so shapes compare semantically.</summary>
[AttributeUsage(AttributeTargets.Property)]
internal sealed partial class SpellingOnlyAttribute : Attribute { }

/// <summary>
/// Base class of every AST node. Start/End are character offsets into the source text; Line/Column are 1-based.
/// Nodes created in code (not by the parser) have zero spans; the span setters are internal.
/// Parsing (<see cref="SqlParser"/>) is thread-safe: every call uses its own state. Trees are mutable and
/// are NOT thread-safe; do not share a tree between threads while any of them may modify it.
/// </summary>
/// <remarks>
/// The per-type members (<c>GetChildren</c>, <c>Accept</c>, <c>AcceptChildren</c>, rewriting, cloning and comparison)
/// are generated into <c>Generated/*.g.cs</c> by <c>tools/AstGen</c>; the virtual members below are only the
/// fallbacks that let the library compile while that code is being (re)generated.
/// </remarks>
public abstract partial class SqlNode
{
    /// <summary>Character offset of the first character of this node in the source text.</summary>
    public int Start { get; internal set; }
    /// <summary>Character offset just past the last character of this node.</summary>
    public int End { get; internal set; }
    /// <summary>1-based line of <see cref="Start"/>.</summary>
    public int Line { get; internal set; }
    /// <summary>1-based column of <see cref="Start"/>.</summary>
    public int Column { get; internal set; }
    /// <summary>
    /// Index into <c>ParseResult.Tokens</c> of the first token this node covers, or -1 when the node was built in code
    /// (or came from a parse that has no token stream). <c>Tokens[FirstTokenIndex].Start == Start</c> when set.
    /// </summary>
    public int FirstTokenIndex { get; internal set; } = -1;
    /// <summary>
    /// Index into <c>ParseResult.Tokens</c> of the last token this node covers, or -1 (see <see cref="FirstTokenIndex"/>).
    /// <c>Tokens[LastTokenIndex].End == End</c> when set.
    /// </summary>
    public int LastTokenIndex { get; internal set; } = -1;

    /// <summary>Direct child nodes, in property declaration order (nested lists are flattened). Generated per node type; no reflection.</summary>
    public virtual IEnumerable<SqlNode> GetChildren() => [];

    /// <summary>Direct child nodes, in property declaration order (nested lists are flattened).</summary>
    public IEnumerable<SqlNode> Children() => GetChildren();

    /// <summary>Calls <c>visitor.ExplicitVisit(this)</c> (the typed overload for this node's concrete type).</summary>
    public virtual void Accept(SqlNodeVisitor visitor) => visitor.Visit(this);

    /// <summary>Accepts <paramref name="visitor"/> on every direct child, in declaration order.</summary>
    public virtual void AcceptChildren(SqlNodeVisitor visitor) { }

    internal virtual SqlNode? AcceptRewriter(SqlNodeRewriter rewriter) => rewriter.Rewrite(this);

    internal virtual void RewriteChildren(SqlNodeRewriter rewriter) { }

    internal virtual SqlNode CloneCore() => (SqlNode)MemberwiseClone();

    internal virtual bool ShapeEquals(SqlNode other, Stack<(SqlNode, SqlNode)> pending) => true;

    /// <summary>Deep copy of this node and everything below it (scalars, spelling-only text, spans and children). Recursive: do not use on chains deeper than a few thousand links.</summary>
    public SqlNode DeepClone() => CloneCore();

    /// <summary>Deep copy typed as <typeparamref name="TNode"/>. Throws <see cref="InvalidCastException"/> if this node is not a <typeparamref name="TNode"/>.</summary>
    public TNode DeepClone<TNode>() where TNode : SqlNode => (TNode)CloneCore();

    /// <summary>This node and all descendants, depth-first pre-order. Uses an explicit stack (safe on very deep trees).</summary>
    public IEnumerable<SqlNode> Descendants()
    {
        var stack = new Stack<SqlNode>();
        stack.Push(this);
        var tmp = new List<SqlNode>();
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            yield return n;
            tmp.Clear();
            tmp.AddRange(n.GetChildren());
            for (int i = tmp.Count - 1; i >= 0; i--) stack.Push(tmp[i]);
        }
    }

    // ---- helpers called by generated code ----

    internal static void CopySpan(SqlNode from, SqlNode to)
    {
        to.Start = from.Start; to.End = from.End; to.Line = from.Line; to.Column = from.Column;
        to.FirstTokenIndex = from.FirstTokenIndex; to.LastTokenIndex = from.LastTokenIndex;
    }

    internal static T? CloneChild<T>(T? node) where T : SqlNode => node is null ? null : (T)node.CloneCore();

    internal static List<T> CloneList<T>(List<T>? list) where T : SqlNode
    {
        if (list is null) return null!;
        var r = new List<T>(list.Count);
        foreach (var n in list) r.Add(CloneChild(n)!);
        return r;
    }

    internal static List<List<T>> CloneNested<T>(List<List<T>>? list) where T : SqlNode
    {
        if (list is null) return null!;
        var r = new List<List<T>>(list.Count);
        foreach (var l in list) r.Add(CloneList(l));
        return r;
    }

    internal static List<string> CloneStrings(List<string>? list) => list is null ? null! : [.. list];

    internal static bool SameStrings(List<string>? a, List<string>? b) =>
        a is null || b is null ? a is null && b is null : a.SequenceEqual(b);

    internal static bool Pair(SqlNode? a, SqlNode? b, Stack<(SqlNode, SqlNode)> pending)
    {
        if (a is null || b is null) return a is null && b is null;
        pending.Push((a, b));
        return true;
    }

    internal static bool PairList<T>(List<T>? a, List<T>? b, Stack<(SqlNode, SqlNode)> pending) where T : SqlNode
    {
        if (a is null || b is null) return a is null && b is null;
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++) if (!Pair(a[i], b[i], pending)) return false;
        return true;
    }

    internal static bool PairNested<T>(List<List<T>>? a, List<List<T>>? b, Stack<(SqlNode, SqlNode)> pending) where T : SqlNode
    {
        if (a is null || b is null) return a is null && b is null;
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++) if (!PairList(a[i], b[i], pending)) return false;
        return true;
    }
}

/// <summary>
/// LEGACY visitor: one untyped <c>Visit(SqlNode)</c> that recurses through <see cref="SqlNode.Children"/>.
/// Prefer <see cref="SqlNodeVisitor"/> (typed <c>Visit(T)</c> / <c>ExplicitVisit(T)</c> callbacks, ScriptDom's model).
/// Kept for backward compatibility.
/// </summary>
public abstract class SqlVisitor
{
    /// <summary>Visits <paramref name="node"/>; the default visits every child. Call the base to keep descending.</summary>
    public virtual void Visit(SqlNode node)
    {
        foreach (var c in node.Children()) Visit(c);
    }
}

// ---------- Script & statements ----------

public sealed partial class SqlScript : SqlNode
{
    public List<Statement> Statements { get; set; } = [];
}

public abstract partial class Statement : SqlNode
{
    /// <summary>PL/SQL labels (&lt;&lt;name&gt;&gt;) attached to this statement.</summary>
    public List<string> Labels { get; set; } = [];
}

/// <summary>INSERT / UPDATE / DELETE / MERGE; carries an optional optimizer hint.</summary>
public abstract partial class DmlStatement : Statement
{
    public HintClause? Hint { get; set; }
    public List<ReturningItem> Returning { get; set; } = [];
    public bool ReturningBulkCollect { get; set; }
    public List<Expression> ReturningInto { get; set; } = [];
    public ErrorLogging? ErrorLogging { get; set; }
}

/// <summary>One item of a RETURNING list: a column or expression, optionally qualified with the row image OLD or NEW (23ai).</summary>
public sealed partial class ReturningItem : SqlNode
{
    /// <summary>Null when neither OLD nor NEW is written: the value after the change is returned.</summary>
    public ReturningImage? Image { get; set; }
    public Expression Expression { get; set; } = null!;
}

public enum ReturningImage { Old, New }

public sealed partial class ErrorLogging : SqlNode
{
    public List<Identifier> Table { get; set; } = [];
    public Expression? Tag { get; set; }
    public Expression? RejectLimit { get; set; }
    public bool RejectUnlimited { get; set; }
}

/// <summary>A full query: [WITH] body [ORDER BY] [OFFSET] [FETCH] [FOR UPDATE]. Also used for subqueries.</summary>
public sealed partial class SelectStatement : Statement
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

public sealed partial class CommonTableExpression : SqlNode
{
    public Identifier Name { get; set; } = null!;
    public List<Identifier> Columns { get; set; } = [];
    public SelectStatement Query { get; set; } = null!;
    public SearchClause? Search { get; set; }
    public CycleClause? Cycle { get; set; }
}

public sealed partial class SearchClause : SqlNode
{
    public bool DepthFirst { get; set; }
    public List<OrderByItem> By { get; set; } = [];
    public Identifier SetColumn { get; set; } = null!;
}

public sealed partial class CycleClause : SqlNode
{
    public List<Identifier> Columns { get; set; } = [];
    public Identifier SetColumn { get; set; } = null!;
    public Expression? CycleValue { get; set; }
    public Expression? DefaultValue { get; set; }
}

public abstract partial class QueryExpression : SqlNode { }

public sealed partial class QuerySpecification : QueryExpression
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

public sealed partial class SetOperation : QueryExpression
{
    public QueryExpression Left { get; set; } = null!;
    public SetOperator Operator { get; set; }
    public bool All { get; set; }
    public QueryExpression Right { get; set; } = null!;
}

public enum SetOperator { Union, Intersect, Minus }

public sealed partial class ParenthesizedQuery : QueryExpression
{
    public SelectStatement Query { get; set; } = null!;
}

public abstract partial class SelectItem : SqlNode { }

public sealed partial class ExpressionSelectItem : SelectItem
{
    public Expression Expression { get; set; } = null!;
    public Identifier? Alias { get; set; }
}

/// <summary>`*` or `qualifier.*`.</summary>
public sealed partial class StarSelectItem : SelectItem
{
    public List<Identifier> Qualifier { get; set; } = [];
}

public sealed partial class OrderByItem : SqlNode
{
    public Expression Expression { get; set; } = null!;
    public bool Descending { get; set; }
    public NullsOrdering? Nulls { get; set; }
}

public enum NullsOrdering { First, Last }

public sealed partial class FetchClause : SqlNode
{
    public bool Next { get; set; }
    public Expression? Count { get; set; }
    public bool Percent { get; set; }
    public bool WithTies { get; set; }
    /// <summary>23ai vector search: EXACT, or APPROX / APPROXIMATE (synonyms). Null when the keyword is omitted.</summary>
    public FetchAccuracy? Accuracy { get; set; }
}

public enum FetchAccuracy { Exact, Approximate }

public sealed partial class ForUpdateClause : SqlNode
{
    public List<ColumnReference> Of { get; set; } = [];
    public bool NoWait { get; set; }
    public bool SkipLocked { get; set; }
    public Expression? Wait { get; set; }
}

// ---------- DML ----------

public sealed partial class InsertStatement : DmlStatement
{
    public TableReference Target { get; set; } = null!;
    public List<Identifier> Columns { get; set; } = [];
    public List<List<Expression>> ValueRows { get; set; } = [];
    /// <summary>PL/SQL: INSERT INTO t VALUES record_variable.</summary>
    public Expression? RecordValue { get; set; }
    public SelectStatement? Query { get; set; }
}

public sealed partial class UpdateStatement : DmlStatement
{
    public TableReference Target { get; set; } = null!;
    public List<SetClause> Set { get; set; } = [];
    /// <summary>23ai: UPDATE ... SET ... FROM table_references.</summary>
    public List<TableReference> From { get; set; } = [];
    public Expression? Where { get; set; }
}

/// <summary>`col = expr` or `(c1, c2) = (subquery)`.</summary>
public sealed partial class SetClause : SqlNode
{
    /// <summary>PL/SQL: SET ROW = record (Columns is empty).</summary>
    public bool Row { get; set; }
    public List<ColumnReference> Columns { get; set; } = [];
    public Expression Value { get; set; } = null!;
}

public sealed partial class DeleteStatement : DmlStatement
{
    public TableReference Target { get; set; } = null!;
    /// <summary>23ai: DELETE FROM target FROM table_references (direct joins).</summary>
    public List<TableReference> From { get; set; } = [];
    public Expression? Where { get; set; }
}

public sealed partial class MergeStatement : DmlStatement
{
    public TableReference Target { get; set; } = null!;
    public TableReference Source { get; set; } = null!;
    public Expression On { get; set; } = null!;
    public List<MergeClause> Clauses { get; set; } = [];
}

public sealed partial class MergeClause : SqlNode
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

public abstract partial class TableReference : SqlNode
{
    public Identifier? Alias { get; set; }
}

public sealed partial class NamedTable : TableReference
{
    public List<Identifier> Name { get; set; } = [];
    /// <summary>Database link name after '@' (dotted parts).</summary>
    public List<Identifier> DbLink { get; set; } = [];
    public Identifier? Partition { get; set; }
    public SampleClause? Sample { get; set; }
    public FlashbackClause? Flashback { get; set; }
}

public sealed partial class SubqueryTable : TableReference
{
    public bool Lateral { get; set; }
    public SelectStatement Query { get; set; } = null!;
}

/// <summary>TABLE(expr) collection expression.</summary>
public sealed partial class TableFunction : TableReference
{
    public Expression Expression { get; set; } = null!;
}

public sealed partial class ParenthesizedTable : TableReference
{
    public TableReference Inner { get; set; } = null!;
}

public sealed partial class JoinTable : TableReference
{
    public TableReference Left { get; set; } = null!;
    public JoinKind Kind { get; set; }
    public bool Natural { get; set; }
    public TableReference Right { get; set; } = null!;
    public Expression? On { get; set; }
    public List<Identifier> Using { get; set; } = [];
    /// <summary>Partitioned outer join: PARTITION BY (...) on the left operand (only one side may be partitioned).</summary>
    public List<Expression> LeftPartitionBy { get; set; } = [];
    /// <summary>Partitioned outer join: PARTITION BY (...) on the right operand.</summary>
    public List<Expression> RightPartitionBy { get; set; } = [];
}

public enum JoinKind { Inner, Left, Right, Full, Cross, CrossApply, OuterApply }

// ---------- Expressions ----------

public abstract partial class Expression : SqlNode { }

public sealed partial class Identifier : SqlNode
{
    /// <summary>Normalized: unquoted names upper-cased; quoted names verbatim.</summary>
    public string Value { get; set; } = "";
    public bool Quoted { get; set; }
    /// <summary>The name exactly as typed (without quotes); null for synthesized identifiers. Used by <see cref="SqlScriptGenerator"/> to preserve case.</summary>
    [SpellingOnly] public string? Text { get; set; }
    public override string ToString() => Value;
}

public sealed partial class ColumnReference : Expression
{
    public List<Identifier> Parts { get; set; } = [];
    /// <summary>Oracle (+) outer-join marker.</summary>
    public bool OuterJoin { get; set; }
    /// <summary>PL/SQL attribute after '%', e.g. FOUND, NOTFOUND, ROWCOUNT, TYPE, ROWTYPE.</summary>
    public string? Attribute { get; set; }
}

public enum LiteralKind { Number, String, Null, Date, Timestamp, Boolean, Default, Any }

public sealed partial class Literal : Expression
{
    public LiteralKind Kind { get; set; }
    /// <summary>N'...' / NQ'...' national character literal.</summary>
    public bool National { get; set; }
    /// <summary>Opening delimiter of a q'…' alternative-quoting literal (e.g. '[' for q'[it's]'); null for ordinary 'strings'.</summary>
    [SpellingOnly] public char? QuoteDelimiter { get; set; }
    public string? Value { get; set; }
}

public sealed partial class IntervalLiteral : Expression
{
    public Expression Value { get; set; } = null!;
    public IntervalUnit From { get; set; }
    public int? FromPrecision { get; set; }
    public IntervalUnit? To { get; set; }
    public int? ToPrecision { get; set; }
}

public enum IntervalUnit { Year, Month, Day, Hour, Minute, Second }

public sealed partial class BindVariable : Expression
{
    public string Name { get; set; } = "";
    /// <summary>The name was written quoted (:"name").</summary>
    public bool Quoted { get; set; }
}

public sealed partial class StarExpression : Expression
{
    public List<Identifier> Qualifier { get; set; } = [];
}

public sealed partial class ParenthesizedExpression : Expression
{
    public Expression Inner { get; set; } = null!;
}

/// <summary>(a, b, c) row/list value.</summary>
public sealed partial class ExpressionList : Expression
{
    public List<Expression> Items { get; set; } = [];
}

public sealed partial class BinaryExpression : Expression
{
    public Expression Left { get; set; } = null!;
    /// <summary>Upper-cased operator text: + - * / ** || = &lt;&gt; &lt; &gt; &lt;= &gt;= AND OR LIKE ...</summary>
    public string Operator { get; set; } = "";
    public Expression Right { get; set; } = null!;
}

/// <summary>Comparison against a quantified set, e.g. x = ANY (subquery).</summary>
public enum ComparisonQuantifier { Any, Some, All }

public sealed partial class QuantifiedComparison : Expression
{
    public Expression Left { get; set; } = null!;
    public string Operator { get; set; } = "";
    public ComparisonQuantifier Quantifier { get; set; }
    public Expression Right { get; set; } = null!;
}

public sealed partial class UnaryExpression : Expression
{
    /// <summary>+, -, NOT, PRIOR, CONNECT_BY_ROOT, DISTINCT</summary>
    public string Operator { get; set; } = "";
    public Expression Operand { get; set; } = null!;
}

public sealed partial class BetweenExpression : Expression
{
    public Expression Value { get; set; } = null!;
    public bool Not { get; set; }
    public Expression Low { get; set; } = null!;
    public Expression High { get; set; } = null!;
}

public sealed partial class InExpression : Expression
{
    public Expression Value { get; set; } = null!;
    public bool Not { get; set; }
    public List<Expression> List { get; set; } = [];
    public SelectStatement? Subquery { get; set; }
}

public enum LikeKind { Like, Likec, Like2, Like4 }

public sealed partial class LikeExpression : Expression
{
    public Expression Value { get; set; } = null!;
    public bool Not { get; set; }
    public LikeKind Kind { get; set; }
    public Expression Pattern { get; set; } = null!;
    public Expression? Escape { get; set; }
}

public enum IsTest { Null, Nan, Infinite, Empty, Present, ASet, Json, OfType, Dangling }

public sealed partial class IsOfType : SqlNode
{
    public bool Only { get; set; }
    public DataType Type { get; set; } = null!;
}

public sealed partial class IsExpression : Expression
{
    public Expression Value { get; set; } = null!;
    public bool Not { get; set; }
    public IsTest Test { get; set; }
    /// <summary>IS JSON options (FORMAT JSON, STRICT/LAX, WITH/WITHOUT UNIQUE KEYS ...).</summary>
    public List<FunctionClause> JsonClauses { get; set; } = [];
    /// <summary>IS OF [TYPE] (ONLY t, ...).</summary>
    public List<IsOfType> OfTypes { get; set; } = [];
}

public sealed partial class ExistsExpression : Expression
{
    public SelectStatement Subquery { get; set; } = null!;
}

public sealed partial class SubqueryExpression : Expression
{
    public SelectStatement Query { get; set; } = null!;
}

public sealed partial class CaseExpression : Expression
{
    public Expression? Operand { get; set; }
    public List<WhenClause> Whens { get; set; } = [];
    public Expression? Else { get; set; }
}

public sealed partial class WhenClause : SqlNode
{
    public Expression When { get; set; } = null!;
    public Expression Then { get; set; } = null!;
}

public enum CastKind { Cast, XmlCast, Treat, ValidateConversion, XmlSerialize }

/// <summary>CAST / XMLCAST / TREAT / VALIDATE_CONVERSION (all of the form f(expr AS type ...)).</summary>
public sealed partial class CastExpression : Expression
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

public sealed partial class ExtractExpression : Expression
{
    public ExtractField Field { get; set; }
    public Expression Source { get; set; } = null!;
}

public enum TrimSpec { Leading, Trailing, Both }

public sealed partial class TrimExpression : Expression
{
    public TrimSpec? Spec { get; set; }
    public Expression? Characters { get; set; }
    public Expression Source { get; set; } = null!;
}

public sealed partial class SubstitutionVariable : Expression
{
    public string Name { get; set; } = "";
    /// <summary>&amp;&amp;name (persistent).</summary>
    public bool Persistent { get; set; }
    /// <summary>The prefix character as typed: '&amp;' by default, or the character set by SQL*Plus <c>SET DEFINE</c> (e.g. '^').</summary>
    [SpellingOnly] public char Prefix { get; set; } = '&';
}

public sealed partial class CollateExpression : Expression
{
    public Expression Value { get; set; } = null!;
    public Identifier Collation { get; set; } = null!;
}

/// <summary>FOR dim IN (...) / FOR dim [LIKE p] FROM a TO b INCREMENT n, used inside MODEL cell subscripts.</summary>
public sealed partial class ForIterationSubscript : Expression
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

public sealed partial class DataType : SqlNode
{
    /// <summary>E.g. "VARCHAR2", "NUMBER", "TIMESTAMP WITH TIME ZONE", or a dotted user-defined type.</summary>
    public string Name { get; set; } = "";
    /// <summary>Parenthesized length/precision/scale arguments (numbers, or * for NUMBER(*,2)).</summary>
    public List<Expression> Arguments { get; set; } = [];
    public CharSemantics? Semantics { get; set; }
    /// <summary>"TYPE" or "ROWTYPE" for anchored declarations (tbl.col%TYPE).</summary>
    public string? Attribute { get; set; }
}

public sealed partial class AtTimeZoneExpression : Expression
{
    public Expression Value { get; set; } = null!;
    /// <summary>AT LOCAL (Zone is null).</summary>
    public bool Local { get; set; }
    public Expression? Zone { get; set; }
}

public sealed partial class FunctionCall : Expression
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

public sealed partial class Argument : SqlNode
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

public sealed partial class KeepClause : SqlNode
{
    public bool First { get; set; }
    public List<OrderByItem> OrderBy { get; set; } = [];
}

public sealed partial class WindowSpec : SqlNode
{
    /// <summary>OVER window_name form.</summary>
    public Identifier? Name { get; set; }
    public List<Expression> PartitionBy { get; set; } = [];
    public List<OrderByItem> OrderBy { get; set; } = [];
    public WindowFrame? Frame { get; set; }
}

public sealed partial class WindowFrame : SqlNode
{
    public FrameUnit Unit { get; set; }
    public FrameBound StartBound { get; set; } = null!;
    public FrameBound? EndBound { get; set; }
}

public enum FrameUnit { Rows, Range, Groups }
public enum FrameBoundKind { UnboundedPreceding, UnboundedFollowing, CurrentRow, Preceding, Following }

public sealed partial class FrameBound : SqlNode
{
    public FrameBoundKind Kind { get; set; }
    public Expression? Offset { get; set; }
}
