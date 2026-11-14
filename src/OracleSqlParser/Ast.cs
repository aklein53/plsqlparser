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

    /// <summary>The node's own properties (not the span members) in declaration order; generated per node type.</summary>
    internal virtual NodeProperty[] NodeProperties() => [];

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

/// <summary>A parsed script: the statements in source order.</summary>
public sealed partial class SqlScript : SqlNode
{
    /// <summary>The statements, in source order; empty for an empty script.</summary>
    public List<Statement> Statements { get; set; } = [];
}

/// <summary>A statement: the base of every SQL and PL/SQL statement node.</summary>
public abstract partial class Statement : SqlNode
{
    /// <summary>PL/SQL labels (&lt;&lt;name&gt;&gt;) attached to this statement.</summary>
    public List<string> Labels { get; set; } = [];
}

/// <summary>INSERT / UPDATE / DELETE / MERGE; carries an optional optimizer hint.</summary>
public abstract partial class DmlStatement : Statement
{
    /// <summary>The optimizer hint comment after the keyword, such as <c>/*+ APPEND */</c>, or null when absent.</summary>
    public HintClause? Hint { get; set; }
    /// <summary>The items of the <c>RETURNING</c> clause, in source order; empty when absent.</summary>
    public List<ReturningItem> Returning { get; set; } = [];
    /// <summary>True when <c>RETURNING ... BULK COLLECT INTO</c> is written.</summary>
    public bool ReturningBulkCollect { get; set; }
    /// <summary>The PL/SQL targets after <c>INTO</c> in <c>RETURNING ... INTO</c>; empty when absent.</summary>
    public List<Expression> ReturningInto { get; set; } = [];
    /// <summary>The <c>LOG ERRORS</c> clause, or null when absent.</summary>
    public ErrorLogging? ErrorLogging { get; set; }
}

/// <summary>One item of a RETURNING list: a column or expression, optionally qualified with the row image OLD or NEW (23ai).</summary>
public sealed partial class ReturningItem : SqlNode
{
    /// <summary>Null when neither OLD nor NEW is written: the value after the change is returned.</summary>
    public ReturningImage? Image { get; set; }
    /// <summary>The column or expression returned.</summary>
    public required Expression Expression { get; set; }
}

/// <summary>The row image named by a <c>RETURNING OLD</c> or <c>RETURNING NEW</c> item.</summary>
public enum ReturningImage
{
    /// <summary><c>OLD</c>: the row as it was before the change.</summary>
    Old,
    /// <summary><c>NEW</c>: the row as it is after the change.</summary>
    New,
}

/// <summary>A <c>LOG ERRORS [INTO t] [('tag')] [REJECT LIMIT n]</c> clause, which sends row errors of a DML statement to an error table.</summary>
public sealed partial class ErrorLogging : SqlNode
{
    /// <summary>The error table name parts from <c>INTO</c>; empty when absent.</summary>
    public List<Identifier> Table { get; set; } = [];
    /// <summary>The tag expression in parentheses, or null when absent.</summary>
    public Expression? Tag { get; set; }
    /// <summary>The number after <c>REJECT LIMIT</c>, or null when absent.</summary>
    public Expression? RejectLimit { get; set; }
    /// <summary>True for <c>REJECT LIMIT UNLIMITED</c>.</summary>
    public bool RejectUnlimited { get; set; }
}

/// <summary>A full query: [WITH] body [ORDER BY] [OFFSET] [FETCH] [FOR UPDATE]. Also used for subqueries.</summary>
public sealed partial class SelectStatement : Statement
{
    /// <summary>The common table expressions of the <c>WITH</c> clause, in source order; empty when absent.</summary>
    public List<CommonTableExpression> With { get; set; } = [];
    /// <summary>The query body: a query specification, a set operation, a <c>VALUES</c> list or a parenthesized query.</summary>
    public required QueryExpression Body { get; set; }
    /// <summary>True for <c>ORDER SIBLINGS BY</c>, which keeps the hierarchy order.</summary>
    public bool OrderSiblings { get; set; }
    /// <summary>The <c>ORDER BY</c> items, in source order; empty when absent.</summary>
    public List<OrderByItem> OrderBy { get; set; } = [];
    /// <summary>The row count of the <c>OFFSET</c> clause, or null when absent.</summary>
    public Expression? Offset { get; set; }
    /// <summary>The <c>FETCH</c> clause, or null when absent.</summary>
    public FetchClause? Fetch { get; set; }
    /// <summary>The <c>FOR UPDATE</c> clause, or null when absent.</summary>
    public ForUpdateClause? ForUpdate { get; set; }
    /// <summary>WITH FUNCTION / WITH PROCEDURE declarations preceding the CTEs.</summary>
    public List<Declaration> WithDeclarations { get; set; } = [];
}

/// <summary>One named query of a <c>WITH</c> clause, such as <c>x (a) AS (SELECT 1 FROM dual)</c>.</summary>
public sealed partial class CommonTableExpression : SqlNode
{
    /// <summary>The name of the common table expression.</summary>
    public required Identifier Name { get; set; }
    /// <summary>The column aliases in parentheses after the name; empty when absent.</summary>
    public List<Identifier> Columns { get; set; } = [];
    /// <summary>The query that defines the common table expression.</summary>
    public required SelectStatement Query { get; set; }
    /// <summary>The <c>SEARCH</c> clause of a recursive expression, or null when absent.</summary>
    public SearchClause? Search { get; set; }
    /// <summary>The <c>CYCLE</c> clause of a recursive expression, or null when absent.</summary>
    public CycleClause? Cycle { get; set; }
}

/// <summary>A <c>SEARCH DEPTH FIRST BY</c> or <c>SEARCH BREADTH FIRST BY</c> clause, such as <c>SEARCH DEPTH FIRST BY id SET ord</c>.</summary>
public sealed partial class SearchClause : SqlNode
{
    /// <summary>True for <c>DEPTH FIRST</c>; false for <c>BREADTH FIRST</c>.</summary>
    public bool DepthFirst { get; set; }
    /// <summary>The ordering items after <c>BY</c>, in source order.</summary>
    public List<OrderByItem> By { get; set; } = [];
    /// <summary>The pseudo column that receives the sort sequence after <c>SET</c>.</summary>
    public required Identifier SetColumn { get; set; }
}

/// <summary>A <c>CYCLE</c> clause of a recursive expression, such as <c>CYCLE id SET is_cycle TO 'Y' DEFAULT 'N'</c>.</summary>
public sealed partial class CycleClause : SqlNode
{
    /// <summary>The columns that identify a row when detecting a cycle.</summary>
    public List<Identifier> Columns { get; set; } = [];
    /// <summary>The pseudo column that records whether a row closes a cycle.</summary>
    public required Identifier SetColumn { get; set; }
    /// <summary>The value after <c>TO</c> for a row that closes a cycle, or null when absent.</summary>
    public Expression? CycleValue { get; set; }
    /// <summary>The value after <c>DEFAULT</c> for a row that does not, or null when absent.</summary>
    public Expression? DefaultValue { get; set; }
}

/// <summary>A query body: a query specification, a set operation, a <c>VALUES</c> list or a parenthesized query.</summary>
public abstract partial class QueryExpression : SqlNode { }

/// <summary>A <c>SELECT</c> block, such as <c>SELECT a FROM t WHERE b = 1</c>.</summary>
public sealed partial class QuerySpecification : QueryExpression
{
    /// <summary>The optimizer hint after <c>SELECT</c>, or null when absent.</summary>
    public HintClause? Hint { get; set; }
    /// <summary>The <c>ALL</c>, <c>DISTINCT</c> or <c>UNIQUE</c> keyword after <c>SELECT</c>, or null when absent.</summary>
    public QuantifierKind? Quantifier { get; set; }
    /// <summary>The select items, in source order.</summary>
    public List<SelectItem> SelectList { get; set; } = [];
    /// <summary>The table references of the <c>FROM</c> clause, in source order; empty when absent.</summary>
    public List<TableReference> From { get; set; } = [];
    /// <summary>The <c>WHERE</c> condition, or null when absent.</summary>
    public Expression? Where { get; set; }
    /// <summary>The <c>START WITH</c> condition of a hierarchical query, or null when absent.</summary>
    public Expression? StartWith { get; set; }
    /// <summary>The <c>CONNECT BY</c> condition, or null when absent.</summary>
    public Expression? ConnectBy { get; set; }
    /// <summary>True when <c>NOCYCLE</c> is written in the <c>CONNECT BY</c> clause.</summary>
    public bool NoCycle { get; set; }
    /// <summary>The <c>GROUP BY</c> expressions, in source order; empty when absent.</summary>
    public List<Expression> GroupBy { get; set; } = [];
    /// <summary>The <c>HAVING</c> condition, or null when absent.</summary>
    public Expression? Having { get; set; }
    /// <summary>The PL/SQL variables of <c>SELECT ... INTO</c>; empty when absent.</summary>
    public List<Expression> Into { get; set; } = [];
    /// <summary>True when <c>BULK COLLECT INTO</c> is written in a PL/SQL query.</summary>
    public bool IntoBulkCollect { get; set; }
    /// <summary>The <c>MODEL</c> clause, or null when absent.</summary>
    public ModelClause? Model { get; set; }
}

/// <summary>A set quantifier, such as <c>DISTINCT</c> after <c>SELECT</c> or inside an aggregate.</summary>
public enum QuantifierKind
{
    /// <summary><c>ALL</c>: keep duplicate rows.</summary>
    All,
    /// <summary><c>DISTINCT</c>: remove duplicate rows.</summary>
    Distinct,
    /// <summary><c>UNIQUE</c>: a synonym of <c>DISTINCT</c>.</summary>
    Unique,
}

/// <summary>Two queries combined by a set operator, such as <c>SELECT a FROM t UNION ALL SELECT b FROM u</c>.</summary>
public sealed partial class SetOperation : QueryExpression
{
    /// <summary>The query on the left of the operator.</summary>
    public required QueryExpression Left { get; set; }
    /// <summary>The set operator.</summary>
    public SetOperator Operator { get; set; }
    /// <summary>True when <c>ALL</c> is written, keeping duplicate rows.</summary>
    public bool All { get; set; }
    /// <summary>The query on the right of the operator.</summary>
    public required QueryExpression Right { get; set; }
}

/// <summary>The operator of a set operation.</summary>
public enum SetOperator
{
    /// <summary><c>UNION</c>.</summary>
    Union,
    /// <summary><c>INTERSECT</c>.</summary>
    Intersect,
    /// <summary><c>MINUS</c>, Oracle's name for <c>EXCEPT</c>.</summary>
    Minus,
}

/// <summary>A query in parentheses, such as <c>(SELECT 1 FROM dual)</c>, used as a query body.</summary>
public sealed partial class ParenthesizedQuery : QueryExpression
{
    /// <summary>The query inside the parentheses.</summary>
    public required SelectStatement Query { get; set; }
}

/// <summary>One item of a <c>SELECT</c> list.</summary>
public abstract partial class SelectItem : SqlNode { }

/// <summary>A column or expression in a <c>SELECT</c> list, such as <c>sal * 12 AS annual</c>.</summary>
public sealed partial class ExpressionSelectItem : SelectItem
{
    /// <summary>The expression that produces the value.</summary>
    public required Expression Expression { get; set; }
    /// <summary>The alias after the expression, or null when absent.</summary>
    public Identifier? Alias { get; set; }
}

/// <summary><c>*</c> or <c>qualifier.*</c> in a <c>SELECT</c> list.</summary>
public sealed partial class StarSelectItem : SelectItem
{
    /// <summary>The name parts before <c>.*</c>; empty for a plain <c>*</c>.</summary>
    public List<Identifier> Qualifier { get; set; } = [];
}

/// <summary>One item of an <c>ORDER BY</c> list, such as <c>sal DESC NULLS LAST</c>.</summary>
public sealed partial class OrderByItem : SqlNode
{
    /// <summary>The sort key.</summary>
    public required Expression Expression { get; set; }
    /// <summary>True for <c>DESC</c>; false for <c>ASC</c> or no direction.</summary>
    public bool Descending { get; set; }
    /// <summary>The <c>NULLS FIRST</c> or <c>NULLS LAST</c> setting, or null when absent.</summary>
    public NullsOrdering? Nulls { get; set; }
}

/// <summary>Where nulls sort in an <c>ORDER BY</c> item.</summary>
public enum NullsOrdering
{
    /// <summary><c>NULLS FIRST</c>.</summary>
    First,
    /// <summary><c>NULLS LAST</c>.</summary>
    Last,
}

/// <summary>A row limiting clause, such as <c>FETCH NEXT 10 ROWS ONLY</c> or <c>FETCH FIRST 5 PERCENT ROWS WITH TIES</c>.</summary>
public sealed partial class FetchClause : SqlNode
{
    /// <summary>True for <c>NEXT</c>; false for <c>FIRST</c>.</summary>
    public bool Next { get; set; }
    /// <summary>The row or percent count, or null when absent.</summary>
    public Expression? Count { get; set; }
    /// <summary>True when <c>PERCENT</c> is written.</summary>
    public bool Percent { get; set; }
    /// <summary>True for <c>WITH TIES</c>.</summary>
    public bool WithTies { get; set; }
    /// <summary>23ai vector search: EXACT, or APPROX / APPROXIMATE (synonyms). Null when the keyword is omitted.</summary>
    public FetchAccuracy? Accuracy { get; set; }
    /// <summary>23ai approximate fetch: WITH TARGET ACCURACY p PERCENT (the percentage expression); null when absent.</summary>
    public Expression? TargetAccuracy { get; set; }
}

/// <summary>The accuracy keyword of a <c>FETCH</c> clause, used by 23ai vector search.</summary>
public enum FetchAccuracy
{
    /// <summary><c>EXACT</c>.</summary>
    Exact,
    /// <summary><c>APPROX</c> or <c>APPROXIMATE</c>.</summary>
    Approximate,
}

/// <summary>A <c>FOR UPDATE</c> clause, such as <c>FOR UPDATE OF t.a NOWAIT</c>.</summary>
public sealed partial class ForUpdateClause : SqlNode
{
    /// <summary>The columns after <c>OF</c>; empty when the lock applies to every table.</summary>
    public List<ColumnReference> Of { get; set; } = [];
    /// <summary>True for <c>NOWAIT</c>.</summary>
    public bool NoWait { get; set; }
    /// <summary>True for <c>SKIP LOCKED</c>.</summary>
    public bool SkipLocked { get; set; }
    /// <summary>The number of seconds after <c>WAIT</c>, or null when absent.</summary>
    public Expression? Wait { get; set; }
}

// ---------- DML ----------

/// <summary><c>INSERT INTO target [(columns)] VALUES (...)</c> or <c>INSERT INTO target SELECT ...</c>.</summary>
public sealed partial class InsertStatement : DmlStatement
{
    /// <summary>The table or view that receives the rows.</summary>
    public required TableReference Target { get; set; }
    /// <summary>The column list in parentheses; empty when absent.</summary>
    public List<Identifier> Columns { get; set; } = [];
    /// <summary>The rows of the <c>VALUES</c> clause, each a list of expressions; empty for the other forms.</summary>
    public List<List<Expression>> ValueRows { get; set; } = [];
    /// <summary>PL/SQL: INSERT INTO t VALUES record_variable.</summary>
    public Expression? RecordValue { get; set; }
    /// <summary>The query that supplies the rows of <c>INSERT ... SELECT</c>, or null for the <c>VALUES</c> form.</summary>
    public SelectStatement? Query { get; set; }
}

/// <summary><c>UPDATE target SET col = value [FROM ...] [WHERE condition]</c>.</summary>
public sealed partial class UpdateStatement : DmlStatement
{
    /// <summary>The table or view that is updated.</summary>
    public required TableReference Target { get; set; }
    /// <summary>The <c>SET</c> assignments, in source order.</summary>
    public List<SetClause> Set { get; set; } = [];
    /// <summary>23ai: UPDATE ... SET ... FROM table_references.</summary>
    public List<TableReference> From { get; set; } = [];
    /// <summary>The <c>WHERE</c> condition, or null when absent.</summary>
    public Expression? Where { get; set; }
}

/// <summary>One assignment of a <c>SET</c> clause: <c>col = expr</c> or <c>(c1, c2) = (subquery)</c>.</summary>
public sealed partial class SetClause : SqlNode
{
    /// <summary>PL/SQL: SET ROW = record (Columns is empty).</summary>
    public bool Row { get; set; }
    /// <summary>The assigned columns: one column, or several in parentheses for a row-value assignment.</summary>
    public List<ColumnReference> Columns { get; set; } = [];
    /// <summary>The value assigned to the column or columns.</summary>
    public required Expression Value { get; set; }
}

/// <summary><c>DELETE FROM target [FROM ...] [WHERE condition]</c>.</summary>
public sealed partial class DeleteStatement : DmlStatement
{
    /// <summary>The table or view that rows are deleted from.</summary>
    public required TableReference Target { get; set; }
    /// <summary>23ai: DELETE FROM target FROM table_references (direct joins).</summary>
    public List<TableReference> From { get; set; } = [];
    /// <summary>The <c>WHERE</c> condition, or null when absent.</summary>
    public Expression? Where { get; set; }
}

/// <summary><c>MERGE INTO target USING source ON (condition) WHEN ...</c>.</summary>
public sealed partial class MergeStatement : DmlStatement
{
    /// <summary>The table that is merged into.</summary>
    public required TableReference Target { get; set; }
    /// <summary>The table or query whose rows are merged in.</summary>
    public required TableReference Source { get; set; }
    /// <summary>The join condition after <c>ON</c>.</summary>
    public required Expression On { get; set; }
    /// <summary>The <c>WHEN MATCHED</c> and <c>WHEN NOT MATCHED</c> clauses, in source order.</summary>
    public List<MergeClause> Clauses { get; set; } = [];
}

/// <summary>One <c>WHEN [NOT] MATCHED THEN</c> clause of a <c>MERGE</c> statement.</summary>
public sealed partial class MergeClause : SqlNode
{
    /// <summary>True for <c>WHEN MATCHED</c>; false for <c>WHEN NOT MATCHED</c>.</summary>
    public bool Matched { get; set; }
    /// <summary>The condition after <c>AND</c>, or null when absent.</summary>
    public Expression? AndCondition { get; set; }
    // WHEN MATCHED THEN UPDATE
    /// <summary>The <c>UPDATE SET</c> assignments; empty for the insert and delete forms.</summary>
    public List<SetClause> Set { get; set; } = [];
    /// <summary>The <c>WHERE</c> condition of the update, or null when absent.</summary>
    public Expression? UpdateWhere { get; set; }
    /// <summary>The <c>WHERE</c> condition of the <c>DELETE</c> part, or null when absent.</summary>
    public Expression? DeleteWhere { get; set; }
    // WHEN NOT MATCHED THEN INSERT
    /// <summary>The column list of the insert; empty when absent.</summary>
    public List<Identifier> InsertColumns { get; set; } = [];
    /// <summary>The values of the insert, in source order; empty for the update and delete forms.</summary>
    public List<Expression> InsertValues { get; set; } = [];
    /// <summary>The <c>WHERE</c> condition of the insert, or null when absent.</summary>
    public Expression? InsertWhere { get; set; }
}

// ---------- Table references ----------

/// <summary>A table source in a <c>FROM</c> clause: a named table, a subquery, a join, a function or another table form.</summary>
public abstract partial class TableReference : SqlNode
{
    /// <summary>The alias after the table source, or null when absent.</summary>
    public Identifier? Alias { get; set; }
}

/// <summary>A table or view name in a <c>FROM</c> clause, such as <c>scott.emp e PARTITION (p1)</c>.</summary>
public sealed partial class NamedTable : TableReference
{
    /// <summary>The table name parts, schema first when qualified.</summary>
    public List<Identifier> Name { get; set; } = [];
    /// <summary>Database link name after '@' (dotted parts).</summary>
    public List<Identifier> DbLink { get; set; } = [];
    /// <summary>The partition name in <c>PARTITION (p)</c>, or null when absent.</summary>
    public Identifier? Partition { get; set; }
    /// <summary>The <c>SAMPLE</c> clause, or null when absent.</summary>
    public SampleClause? Sample { get; set; }
    /// <summary>The <c>AS OF</c> or <c>VERSIONS BETWEEN</c> clause, or null when absent.</summary>
    public FlashbackClause? Flashback { get; set; }
    /// <summary>JSON duality view annotations written after the table (WITH INSERT UPDATE DELETE, WITH NOCHECK, ...).</summary>
    public List<DualityAnnotationItem> DualityAnnotations { get; set; } = [];
}

/// <summary>A subquery in a <c>FROM</c> clause, such as <c>(SELECT 1 FROM dual) s</c>.</summary>
public sealed partial class SubqueryTable : TableReference
{
    /// <summary>True for <c>LATERAL</c>.</summary>
    public bool Lateral { get; set; }
    /// <summary>The subquery.</summary>
    public required SelectStatement Query { get; set; }
}

/// <summary>TABLE(expr) collection expression.</summary>
public sealed partial class TableFunction : TableReference
{
    /// <summary>The collection expression, such as <c>f(1)</c> in <c>TABLE(f(1))</c>.</summary>
    public required Expression Expression { get; set; }
}

/// <summary>A table reference in parentheses, such as <c>(a JOIN b ON a.id = b.id)</c>.</summary>
public sealed partial class ParenthesizedTable : TableReference
{
    /// <summary>The table reference inside the parentheses.</summary>
    public required TableReference Inner { get; set; }
}

/// <summary>A join of two table references, such as <c>a LEFT JOIN b ON a.id = b.id</c>.</summary>
public sealed partial class JoinTable : TableReference
{
    /// <summary>The table reference on the left of the join.</summary>
    public required TableReference Left { get; set; }
    /// <summary>The kind of join.</summary>
    public JoinKind Kind { get; set; }
    /// <summary>True for a <c>NATURAL</c> join.</summary>
    public bool Natural { get; set; }
    /// <summary>The table reference on the right of the join.</summary>
    public required TableReference Right { get; set; }
    /// <summary>The <c>ON</c> condition, or null when absent.</summary>
    public Expression? On { get; set; }
    /// <summary>The columns of a <c>USING</c> clause; empty when absent.</summary>
    public List<Identifier> Using { get; set; } = [];
    /// <summary>Partitioned outer join: the <c>PARTITION BY (...)</c> expressions written before the join keyword on the left operand; empty when absent.</summary>
    public List<Expression> LeftPartitionBy { get; set; } = [];
    /// <summary>Partitioned outer join: the <c>PARTITION BY (...)</c> expressions written after the join keyword, on the right operand; empty when absent.</summary>
    public List<Expression> RightPartitionBy { get; set; } = [];
}

/// <summary>The kind of a join.</summary>
public enum JoinKind
{
    /// <summary><c>[INNER] JOIN</c>.</summary>
    Inner,
    /// <summary><c>LEFT [OUTER] JOIN</c>.</summary>
    Left,
    /// <summary><c>RIGHT [OUTER] JOIN</c>.</summary>
    Right,
    /// <summary><c>FULL [OUTER] JOIN</c>.</summary>
    Full,
    /// <summary><c>CROSS JOIN</c>.</summary>
    Cross,
    /// <summary><c>CROSS APPLY</c>.</summary>
    CrossApply,
    /// <summary><c>OUTER APPLY</c>.</summary>
    OuterApply,
}

// ---------- Expressions ----------

/// <summary>An expression: a value, a column, an operator application, a function call or another expression form.</summary>
public abstract partial class Expression : SqlNode { }

/// <summary>A name, such as a column, table or alias.</summary>
public sealed partial class Identifier : SqlNode
{
    /// <summary>Normalized: unquoted names upper-cased; quoted names verbatim.</summary>
    public string Value { get; set; } = "";
    /// <summary>True when the name was written in double quotes.</summary>
    public bool Quoted { get; set; }
    /// <summary>The name exactly as typed (without quotes); null for synthesized identifiers. Used by <see cref="SqlScriptGenerator"/> to preserve case.</summary>
    [SpellingOnly] public string? Text { get; set; }
    /// <summary>The normalized name.</summary>
    public override string ToString() => Value;
}

/// <summary>A column reference, such as <c>e.ename</c>.</summary>
public sealed partial class ColumnReference : Expression
{
    /// <summary>The name parts, qualifier first: for <c>e.ename</c>, the table alias and then the column name.</summary>
    public List<Identifier> Parts { get; set; } = [];
    /// <summary>Oracle (+) outer-join marker.</summary>
    public bool OuterJoin { get; set; }
    /// <summary>PL/SQL attribute after '%', e.g. FOUND, NOTFOUND, ROWCOUNT, TYPE, ROWTYPE.</summary>
    public string? Attribute { get; set; }
}

/// <summary>The kind of a literal value.</summary>
public enum LiteralKind
{
    /// <summary>A number, such as <c>42</c> or <c>1.5e3</c>.</summary>
    Number,
    /// <summary>A character string, such as <c>'text'</c>.</summary>
    String,
    /// <summary>The <c>NULL</c> literal.</summary>
    Null,
    /// <summary>A <c>DATE</c> literal, such as <c>DATE '2020-01-01'</c>.</summary>
    Date,
    /// <summary>A <c>TIMESTAMP</c> literal, such as <c>TIMESTAMP '2020-01-01 10:00:00'</c>.</summary>
    Timestamp,
    /// <summary><c>TRUE</c> or <c>FALSE</c>.</summary>
    Boolean,
    /// <summary>The <c>DEFAULT</c> keyword used as a value.</summary>
    Default,
    /// <summary>The <c>ANY</c> keyword used as a value.</summary>
    Any,
}

/// <summary>A literal value, such as <c>'x'</c>, <c>42</c>, <c>DATE '2020-01-01'</c> or <c>NULL</c>.</summary>
public sealed partial class Literal : Expression
{
    /// <summary>The kind of literal.</summary>
    public LiteralKind Kind { get; set; }
    /// <summary>N'...' / NQ'...' national character literal.</summary>
    public bool National { get; set; }
    /// <summary>Opening delimiter of a q'…' alternative-quoting literal (e.g. '[' for q'[it's]'); null for ordinary 'strings'.</summary>
    [SpellingOnly] public char? QuoteDelimiter { get; set; }
    /// <summary>The literal text as written, without the quotes of a string; null for <c>NULL</c>.</summary>
    public string? Value { get; set; }
}

/// <summary>An <c>INTERVAL</c> literal, such as <c>INTERVAL '3' DAY</c> or <c>INTERVAL '1-2' YEAR(3) TO MONTH</c>.</summary>
public sealed partial class IntervalLiteral : Expression
{
    /// <summary>The interval string expression.</summary>
    public required Expression Value { get; set; }
    /// <summary>The leading datetime field, such as <c>DAY</c>.</summary>
    public IntervalUnit From { get; set; }
    /// <summary>The leading precision, such as the <c>3</c> in <c>YEAR(3)</c>, or null when absent.</summary>
    public int? FromPrecision { get; set; }
    /// <summary>The trailing field after <c>TO</c>, or null for a single-field interval.</summary>
    public IntervalUnit? To { get; set; }
    /// <summary>The precision of the trailing field, or null when absent.</summary>
    public int? ToPrecision { get; set; }
}

/// <summary>
/// Postfix interval over a parenthesized datetime difference: <c>(a - b) DAY(2) TO SECOND(6)</c> or
/// <c>(a - b) YEAR TO MONTH</c>. Unlike <see cref="IntervalLiteral"/> there is no INTERVAL keyword or string.
/// </summary>
public sealed partial class IntervalExpression : Expression
{
    /// <summary>The parenthesized datetime difference, such as <c>(a - b)</c>.</summary>
    public required Expression Value { get; set; }
    /// <summary>The leading datetime field, such as <c>DAY</c>.</summary>
    public IntervalUnit From { get; set; }
    /// <summary>The leading precision, or null when absent.</summary>
    public int? FromPrecision { get; set; }
    /// <summary>The trailing datetime field after <c>TO</c>, such as <c>SECOND</c>.</summary>
    public IntervalUnit To { get; set; }
    /// <summary>The precision of the trailing field, or null when absent.</summary>
    public int? ToPrecision { get; set; }
}

/// <summary>A datetime field of an interval.</summary>
public enum IntervalUnit
{
    /// <summary><c>YEAR</c>.</summary>
    Year,
    /// <summary><c>MONTH</c>.</summary>
    Month,
    /// <summary><c>DAY</c>.</summary>
    Day,
    /// <summary><c>HOUR</c>.</summary>
    Hour,
    /// <summary><c>MINUTE</c>.</summary>
    Minute,
    /// <summary><c>SECOND</c>.</summary>
    Second,
}

/// <summary>A bind variable, such as <c>:id</c>.</summary>
public sealed partial class BindVariable : Expression
{
    /// <summary>The bind name without the colon.</summary>
    public string Name { get; set; } = "";
    /// <summary>The name was written quoted (:"name").</summary>
    public bool Quoted { get; set; }
}

/// <summary>A star, such as the <c>*</c> in <c>COUNT(*)</c> or <c>t.*</c>.</summary>
public sealed partial class StarExpression : Expression
{
    /// <summary>The name parts before <c>.*</c>; empty for a plain <c>*</c>.</summary>
    public List<Identifier> Qualifier { get; set; } = [];
}

/// <summary>An expression in parentheses, such as <c>(a + b)</c>; kept so that the written shape is preserved.</summary>
public sealed partial class ParenthesizedExpression : Expression
{
    /// <summary>The expression inside the parentheses.</summary>
    public required Expression Inner { get; set; }
}

/// <summary>(a, b, c) row/list value.</summary>
public sealed partial class ExpressionList : Expression
{
    /// <summary>The expressions in the list, in source order.</summary>
    public List<Expression> Items { get; set; } = [];
}

/// <summary>A binary operator application, such as <c>a + b</c> or <c>a || b</c>.</summary>
public sealed partial class BinaryExpression : Expression
{
    /// <summary>The left operand.</summary>
    public required Expression Left { get; set; }
    /// <summary>Upper-cased operator text: + - * / ** || = &lt;&gt; &lt; &gt; &lt;= &gt;= AND OR LIKE ...</summary>
    public string Operator { get; set; } = "";
    /// <summary>The right operand.</summary>
    public required Expression Right { get; set; }
}

/// <summary>The quantifier of a quantified comparison, such as <c>= ANY (...)</c>.</summary>
public enum ComparisonQuantifier
{
    /// <summary><c>ANY</c>.</summary>
    Any,
    /// <summary><c>SOME</c>, a synonym of <c>ANY</c>.</summary>
    Some,
    /// <summary><c>ALL</c>.</summary>
    All,
}

/// <summary>A comparison with a quantified list or subquery, such as <c>x = ANY (SELECT y FROM u)</c>.</summary>
public sealed partial class QuantifiedComparison : Expression
{
    /// <summary>The value on the left of the comparison.</summary>
    public required Expression Left { get; set; }
    /// <summary>The comparison operator, such as = or &lt;&gt;.</summary>
    public string Operator { get; set; } = "";
    /// <summary>The quantifier: <c>ANY</c>, <c>SOME</c> or <c>ALL</c>.</summary>
    public ComparisonQuantifier Quantifier { get; set; }
    /// <summary>The subquery or list on the right.</summary>
    public required Expression Right { get; set; }
}

/// <summary>A prefix operator applied to one operand, such as <c>NOT a</c>, <c>-a</c> or <c>PRIOR a</c>.</summary>
public sealed partial class UnaryExpression : Expression
{
    /// <summary>Upper-cased prefix operator: + - NOT PRIOR CONNECT_BY_ROOT DISTINCT.</summary>
    public string Operator { get; set; } = "";
    /// <summary>The operand.</summary>
    public required Expression Operand { get; set; }
}

/// <summary>A <c>BETWEEN</c> test, such as <c>a BETWEEN 1 AND 2</c>.</summary>
public sealed partial class BetweenExpression : Expression
{
    /// <summary>The tested value.</summary>
    public required Expression Value { get; set; }
    /// <summary>True for <c>NOT BETWEEN</c>.</summary>
    public bool Not { get; set; }
    /// <summary>The lower bound.</summary>
    public required Expression Low { get; set; }
    /// <summary>The upper bound.</summary>
    public required Expression High { get; set; }
}

/// <summary>An <c>IN</c> test, such as <c>a IN (1, 2)</c> or <c>a IN (SELECT ...)</c>.</summary>
public sealed partial class InExpression : Expression
{
    /// <summary>The tested value.</summary>
    public required Expression Value { get; set; }
    /// <summary>True for <c>NOT IN</c>.</summary>
    public bool Not { get; set; }
    /// <summary>The listed values, in source order; empty when the test uses a subquery.</summary>
    public List<Expression> List { get; set; } = [];
    /// <summary>The subquery of <c>IN (SELECT ...)</c>, or null for a list.</summary>
    public SelectStatement? Subquery { get; set; }
}

/// <summary>The form of a <c>LIKE</c> operator.</summary>
public enum LikeKind
{
    /// <summary><c>LIKE</c>.</summary>
    Like,
    /// <summary><c>LIKEC</c>: character-length semantics.</summary>
    Likec,
    /// <summary><c>LIKE2</c>: UCS2 code-point semantics.</summary>
    Like2,
    /// <summary><c>LIKE4</c>: UCS4 code-point semantics.</summary>
    Like4,
}

/// <summary>A <c>LIKE</c> test, such as <c>name LIKE 'A%' ESCAPE '\'</c>.</summary>
public sealed partial class LikeExpression : Expression
{
    /// <summary>The tested value.</summary>
    public required Expression Value { get; set; }
    /// <summary>True for <c>NOT LIKE</c>.</summary>
    public bool Not { get; set; }
    /// <summary>The form of the operator.</summary>
    public LikeKind Kind { get; set; }
    /// <summary>The pattern expression.</summary>
    public required Expression Pattern { get; set; }
    /// <summary>The <c>ESCAPE</c> character expression, or null when absent.</summary>
    public Expression? Escape { get; set; }
}

/// <summary>The test written after <c>IS</c>, such as <c>IS NULL</c>.</summary>
public enum IsTest
{
    /// <summary><c>NULL</c>.</summary>
    Null,
    /// <summary><c>NAN</c>.</summary>
    Nan,
    /// <summary><c>INFINITE</c>.</summary>
    Infinite,
    /// <summary><c>EMPTY</c>.</summary>
    Empty,
    /// <summary><c>PRESENT</c>.</summary>
    Present,
    /// <summary><c>A SET</c>.</summary>
    ASet,
    /// <summary><c>JSON</c>.</summary>
    Json,
    /// <summary><c>OF [TYPE]</c>.</summary>
    OfType,
    /// <summary><c>DANGLING</c>.</summary>
    Dangling,
    /// <summary><c>TRUE</c>.</summary>
    True,
    /// <summary><c>FALSE</c>.</summary>
    False,
}

/// <summary>One type in an <c>IS OF</c> test, such as <c>ONLY t</c>.</summary>
public sealed partial class IsOfType : SqlNode
{
    /// <summary>True for <c>ONLY</c>: the exact type, not its subtypes.</summary>
    public bool Only { get; set; }
    /// <summary>The type tested.</summary>
    public required DataType Type { get; set; }
}

/// <summary>An <c>IS</c> test, such as <c>a IS NOT NULL</c>.</summary>
public sealed partial class IsExpression : Expression
{
    /// <summary>The tested value.</summary>
    public required Expression Value { get; set; }
    /// <summary>True for <c>IS NOT</c>.</summary>
    public bool Not { get; set; }
    /// <summary>The test written after <c>IS</c>.</summary>
    public IsTest Test { get; set; }
    /// <summary>IS JSON options (FORMAT JSON, STRICT/LAX, WITH/WITHOUT UNIQUE KEYS ...).</summary>
    public List<FunctionClause> JsonClauses { get; set; } = [];
    /// <summary>IS OF [TYPE] (ONLY t, ...).</summary>
    public List<IsOfType> OfTypes { get; set; } = [];
}

/// <summary>An <c>EXISTS</c> test, such as <c>EXISTS (SELECT 1 FROM u)</c>.</summary>
public sealed partial class ExistsExpression : Expression
{
    /// <summary>The subquery.</summary>
    public required SelectStatement Subquery { get; set; }
}

/// <summary>A scalar subquery used as an expression, such as <c>(SELECT MAX(x) FROM u)</c>.</summary>
public sealed partial class SubqueryExpression : Expression
{
    /// <summary>The subquery.</summary>
    public required SelectStatement Query { get; set; }
}

/// <summary>A <c>CASE</c> expression, simple or searched, such as <c>CASE WHEN a &gt; 1 THEN 'x' ELSE 'y' END</c>.</summary>
public sealed partial class CaseExpression : Expression
{
    /// <summary>The operand of a simple <c>CASE</c>, or null for the searched form.</summary>
    public Expression? Operand { get; set; }
    /// <summary>The <c>WHEN</c> branches, in source order.</summary>
    public List<WhenClause> Whens { get; set; } = [];
    /// <summary>The <c>ELSE</c> result, or null when absent.</summary>
    public Expression? Else { get; set; }
}

/// <summary>One <c>WHEN ... THEN</c> branch of a <c>CASE</c> expression.</summary>
public sealed partial class WhenClause : SqlNode
{
    /// <summary>The condition, or the value compared with the operand, after <c>WHEN</c>.</summary>
    public required Expression When { get; set; }
    /// <summary>The result after <c>THEN</c>.</summary>
    public required Expression Then { get; set; }
}

/// <summary>The conversion that a <c>CastExpression</c> performs.</summary>
public enum CastKind
{
    /// <summary><c>CAST (expr AS type)</c>.</summary>
    Cast,
    /// <summary><c>XMLCAST</c>.</summary>
    XmlCast,
    /// <summary><c>TREAT (expr AS type)</c>.</summary>
    Treat,
    /// <summary><c>VALIDATE_CONVERSION</c>.</summary>
    ValidateConversion,
    /// <summary><c>XMLSERIALIZE</c>.</summary>
    XmlSerialize,
}

/// <summary>CAST / XMLCAST / TREAT / VALIDATE_CONVERSION (all of the form f(expr AS type ...)).</summary>
public sealed partial class CastExpression : Expression
{
    /// <summary>The conversion function written.</summary>
    public CastKind Kind { get; set; }
    /// <summary>True for <c>CAST (MULTISET (subquery) AS type)</c>.</summary>
    public bool Multiset { get; set; }
    /// <summary>The expression being converted.</summary>
    public required Expression Value { get; set; }
    /// <summary>TREAT(x AS REF type).</summary>
    public bool Ref { get; set; }
    /// <summary>The target type.</summary>
    public required DataType Type { get; set; }
    /// <summary>DEFAULT x ON CONVERSION ERROR.</summary>
    public OnBehaviorClause? OnConversionError { get; set; }
    /// <summary>The format model written after a comma in the trailing options, or null when absent.</summary>
    public Expression? Format { get; set; }
    /// <summary>The NLS parameter written after the format model, or null when absent.</summary>
    public Expression? NlsParam { get; set; }
    /// <summary>XMLSERIALIZE options (DOCUMENT/CONTENT, ENCODING, VERSION, INDENT, DEFAULTS).</summary>
    public List<FunctionClause> Clauses { get; set; } = [];
}

/// <summary>The field named in an <c>EXTRACT</c> expression.</summary>
public enum ExtractField
{
    /// <summary><c>YEAR</c>.</summary>
    Year,
    /// <summary><c>MONTH</c>.</summary>
    Month,
    /// <summary><c>DAY</c>.</summary>
    Day,
    /// <summary><c>HOUR</c>.</summary>
    Hour,
    /// <summary><c>MINUTE</c>.</summary>
    Minute,
    /// <summary><c>SECOND</c>.</summary>
    Second,
    /// <summary><c>TIMEZONE_HOUR</c>.</summary>
    TimezoneHour,
    /// <summary><c>TIMEZONE_MINUTE</c>.</summary>
    TimezoneMinute,
    /// <summary><c>TIMEZONE_REGION</c>.</summary>
    TimezoneRegion,
    /// <summary><c>TIMEZONE_ABBR</c>.</summary>
    TimezoneAbbr,
}

/// <summary>An <c>EXTRACT (field FROM source)</c> expression, such as <c>EXTRACT(YEAR FROM d)</c>.</summary>
public sealed partial class ExtractExpression : Expression
{
    /// <summary>The field extracted.</summary>
    public ExtractField Field { get; set; }
    /// <summary>The datetime or interval expression.</summary>
    public required Expression Source { get; set; }
}

/// <summary>The side of the string that <c>TRIM</c> removes characters from.</summary>
public enum TrimSpec
{
    /// <summary><c>LEADING</c>.</summary>
    Leading,
    /// <summary><c>TRAILING</c>.</summary>
    Trailing,
    /// <summary><c>BOTH</c>.</summary>
    Both,
}

/// <summary>A <c>TRIM</c> expression, such as <c>TRIM(BOTH 'x' FROM s)</c>.</summary>
public sealed partial class TrimExpression : Expression
{
    /// <summary>The side to trim, or null when none is written.</summary>
    public TrimSpec? Spec { get; set; }
    /// <summary>The characters to remove, or null when the default (blanks) applies.</summary>
    public Expression? Characters { get; set; }
    /// <summary>The string being trimmed.</summary>
    public required Expression Source { get; set; }
}

/// <summary>A substitution variable, such as <c>&amp;name</c> in SQL*Plus.</summary>
public sealed partial class SubstitutionVariable : Expression
{
    /// <summary>The variable name without the ampersand.</summary>
    public string Name { get; set; } = "";
    /// <summary>&amp;&amp;name (persistent).</summary>
    public bool Persistent { get; set; }
    /// <summary>The prefix character as typed: '&amp;' by default, or the character set by SQL*Plus <c>SET DEFINE</c> (e.g. '^').</summary>
    [SpellingOnly] public char Prefix { get; set; } = '&';
}

/// <summary>A <c>COLLATE</c> expression, such as <c>name COLLATE BINARY_CI</c>.</summary>
public sealed partial class CollateExpression : Expression
{
    /// <summary>The expression being collated.</summary>
    public required Expression Value { get; set; }
    /// <summary>The collation name.</summary>
    public required Identifier Collation { get; set; }
}

/// <summary>FOR dim IN (...) / FOR dim [LIKE p] FROM a TO b INCREMENT n, used inside MODEL cell subscripts.</summary>
public sealed partial class ForIterationSubscript : Expression
{
    /// <summary>The dimension variables, in source order.</summary>
    public List<Identifier> Dimensions { get; set; } = [];
    /// <summary>The values of an <c>IN (...)</c> form, in source order; empty when absent.</summary>
    public List<Expression> InList { get; set; } = [];
    /// <summary>The subquery of an <c>IN (SELECT ...)</c> form, or null when absent.</summary>
    public SelectStatement? InQuery { get; set; }
    /// <summary>The pattern of a <c>LIKE</c> form, or null when absent.</summary>
    public Expression? LikePattern { get; set; }
    /// <summary>The start of a range, or null for the list and subquery forms.</summary>
    public Expression? From { get; set; }
    /// <summary>The end of a range, or null when absent.</summary>
    public Expression? To { get; set; }
    /// <summary>The <c>INCREMENT</c> or <c>DECREMENT</c> step of a range, or null when absent.</summary>
    public Expression? Step { get; set; }
    /// <summary>True for <c>DECREMENT</c>: the range runs downward.</summary>
    public bool Decrement { get; set; }
}

/// <summary>The unit of a length in a character type.</summary>
public enum CharSemantics
{
    /// <summary><c>CHAR</c>: the length counts characters.</summary>
    Char,
    /// <summary><c>BYTE</c>: the length counts bytes.</summary>
    Byte,
}

/// <summary>A data type, such as <c>NUMBER(10,2)</c> or <c>VARCHAR2(10 CHAR)</c>.</summary>
public sealed partial class DataType : SqlNode
{
    /// <summary>E.g. "VARCHAR2", "NUMBER", "TIMESTAMP WITH TIME ZONE", or a dotted user-defined type.</summary>
    public string Name { get; set; } = "";
    /// <summary>Parenthesized length/precision/scale arguments (numbers, or * for NUMBER(*,2)).</summary>
    public List<Expression> Arguments { get; set; } = [];
    /// <summary>The <c>CHAR</c> or <c>BYTE</c> length semantics, or null when absent.</summary>
    public CharSemantics? Semantics { get; set; }
    /// <summary>"TYPE" or "ROWTYPE" for anchored declarations (tbl.col%TYPE).</summary>
    public string? Attribute { get; set; }
}

/// <summary>An <c>AT TIME ZONE</c> expression, such as <c>ts AT TIME ZONE 'UTC'</c>.</summary>
public sealed partial class AtTimeZoneExpression : Expression
{
    /// <summary>The datetime value.</summary>
    public required Expression Value { get; set; }
    /// <summary>AT LOCAL (Zone is null).</summary>
    public bool Local { get; set; }
    /// <summary>The time zone expression, or null for <c>AT LOCAL</c>.</summary>
    public Expression? Zone { get; set; }
}

/// <summary>A function call, such as <c>COUNT(DISTINCT x)</c>, with its optional null-treatment and window clauses.</summary>
public sealed partial class FunctionCall : Expression
{
    /// <summary>The function name parts, schema first when qualified.</summary>
    public List<Identifier> Name { get; set; } = [];
    /// <summary>NEW type(...) constructor call.</summary>
    public bool New { get; set; }
    /// <summary>For SQL%BULK_ROWCOUNT(i): the attribute after '%'.</summary>
    public string? Attribute { get; set; }
    /// <summary>Trailing clauses: RETURNING, ON ERROR, FORMAT JSON, ON OVERFLOW, PASSING, ...</summary>
    public List<FunctionClause> Clauses { get; set; } = [];
    /// <summary>JSON_TRANSFORM operations (SET/INSERT/REMOVE/...).</summary>
    public List<JsonTransformOperation> Operations { get; set; } = [];
    /// <summary>The <c>DISTINCT</c>, <c>ALL</c> or <c>UNIQUE</c> keyword inside the call, or null when absent.</summary>
    public QuantifierKind? Quantifier { get; set; }
    /// <summary>The arguments, in source order; empty for a call without arguments.</summary>
    public List<Argument> Arguments { get; set; } = [];
    /// <summary>True for <c>IGNORE NULLS</c>.</summary>
    public bool IgnoreNulls { get; set; }
    /// <summary>True for <c>RESPECT NULLS</c>.</summary>
    public bool RespectNulls { get; set; }
    /// <summary>The ordering items of <c>WITHIN GROUP (ORDER BY ...)</c>, as in <c>LISTAGG</c>; empty when absent.</summary>
    public List<OrderByItem> WithinGroup { get; set; } = [];
    /// <summary>KEEP (DENSE_RANK FIRST|LAST ORDER BY ...)</summary>
    public KeepClause? Keep { get; set; }
    /// <summary>The <c>OVER</c> window of an analytic call, or null when absent.</summary>
    public WindowSpec? Over { get; set; }
    /// <summary>The <c>ORDER BY</c> items written inside the call, such as <c>XMLAGG(x ORDER BY y)</c>; empty when absent.</summary>
    public List<OrderByItem> OrderBy { get; set; } = [];
}

/// <summary>The keyword that introduces a special argument of an XML or JSON function.</summary>
public enum ArgumentKeyword
{
    /// <summary><c>NAME</c>.</summary>
    Name,
    /// <summary><c>EVALNAME</c>.</summary>
    EvalName,
    /// <summary><c>KEY</c>.</summary>
    Key,
    /// <summary><c>DOCUMENT</c>.</summary>
    Document,
    /// <summary><c>CONTENT</c>.</summary>
    Content,
}

/// <summary>One argument of a function call, which may be named, such as <c>x =&gt; 1</c>.</summary>
public sealed partial class Argument : SqlNode
{
    /// <summary>The argument name in named notation (<c>name =&gt; value</c>), or null for a positional argument.</summary>
    public Identifier? Name { get; set; }  // named notation: name => value
    /// <summary>Leading keyword in XML/JSON functions (XMLELEMENT(NAME x), JSON_OBJECT(KEY k ...)).</summary>
    public ArgumentKeyword? Keyword { get; set; }
    /// <summary>The argument value.</summary>
    public required Expression Value { get; set; }
    /// <summary>The value of a JSON_OBJECT pair written after <c>VALUE</c> or <c>:</c>; <see cref="Value"/> then holds the key. Null otherwise.</summary>
    public Expression? MappedValue { get; set; }
    /// <summary>The <c>AS</c> alias of an <c>XMLATTRIBUTES</c> or <c>XMLFOREST</c> item, or null when absent.</summary>
    public Identifier? Alias { get; set; }
    /// <summary>The <c>FORMAT JSON</c> clause on the argument, or null when absent.</summary>
    public FormatClause? Format { get; set; }
    /// <summary>TO_NUMBER(x DEFAULT d ON CONVERSION ERROR, ...)</summary>
    public OnBehaviorClause? OnConversionError { get; set; }
}

/// <summary>The <c>KEEP (DENSE_RANK FIRST | LAST ORDER BY ...)</c> clause of an aggregate.</summary>
public sealed partial class KeepClause : SqlNode
{
    /// <summary>True for <c>FIRST</c>; false for <c>LAST</c>.</summary>
    public bool First { get; set; }
    /// <summary>The ordering items, in source order.</summary>
    public List<OrderByItem> OrderBy { get; set; } = [];
}

/// <summary>An <c>OVER</c> window specification, such as <c>(PARTITION BY d ORDER BY s ROWS UNBOUNDED PRECEDING)</c>.</summary>
public sealed partial class WindowSpec : SqlNode
{
    /// <summary>The named window of <c>OVER window_name</c>, or null when the window is written inline.</summary>
    public Identifier? Name { get; set; }
    /// <summary>The <c>PARTITION BY</c> expressions; empty when absent.</summary>
    public List<Expression> PartitionBy { get; set; } = [];
    /// <summary>The <c>ORDER BY</c> items; empty when absent.</summary>
    public List<OrderByItem> OrderBy { get; set; } = [];
    /// <summary>The window frame, or null when absent.</summary>
    public WindowFrame? Frame { get; set; }
}

/// <summary>A window frame, such as <c>ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW</c>.</summary>
public sealed partial class WindowFrame : SqlNode
{
    /// <summary>Whether the frame counts rows, a value range or peer groups.</summary>
    public FrameUnit Unit { get; set; }
    /// <summary>The start of the frame.</summary>
    public required FrameBound StartBound { get; set; }
    /// <summary>The end of the frame, or null when the frame has only a start bound.</summary>
    public FrameBound? EndBound { get; set; }
}

/// <summary>The unit of a window frame.</summary>
public enum FrameUnit
{
    /// <summary><c>ROWS</c>: physical rows.</summary>
    Rows,
    /// <summary><c>RANGE</c>: rows whose ordering value is in range.</summary>
    Range,
    /// <summary><c>GROUPS</c>: peer groups of rows.</summary>
    Groups,
}
/// <summary>The kind of a window frame bound.</summary>
public enum FrameBoundKind
{
    /// <summary><c>UNBOUNDED PRECEDING</c>.</summary>
    UnboundedPreceding,
    /// <summary><c>UNBOUNDED FOLLOWING</c>.</summary>
    UnboundedFollowing,
    /// <summary><c>CURRENT ROW</c>.</summary>
    CurrentRow,
    /// <summary><c>n PRECEDING</c>.</summary>
    Preceding,
    /// <summary><c>n FOLLOWING</c>.</summary>
    Following,
}

/// <summary>One bound of a window frame, such as <c>2 PRECEDING</c>.</summary>
public sealed partial class FrameBound : SqlNode
{
    /// <summary>The kind of bound.</summary>
    public FrameBoundKind Kind { get; set; }
    /// <summary>The row or value offset for <c>PRECEDING</c> and <c>FOLLOWING</c>; null for the unbounded and current-row kinds.</summary>
    public Expression? Offset { get; set; }
}
