using System.Text;

namespace OracleSqlParser;

/// <summary>
/// Prints a query AST back to Oracle SQL. Supports SELECT statements and everything beneath them.
/// Other statement kinds throw <see cref="NotSupportedException"/> naming the node type (never silently dropped).
/// Parentheses are added from operator precedence, so trees edited programmatically print correctly.
/// </summary>
public static class SqlWriter
{
    public static string ToSql(SqlNode node)
    {
        var w = new W();
        w.Node(node);
        return w.ToString();
    }

    private sealed class W
    {
        private readonly StringBuilder _sb = new();
        public override string ToString() => _sb.ToString();

        private static NotSupportedException Unsupported(SqlNode n) => new($"Printing {n.GetType().Name} is not supported");

        private W Add(string s) { _sb.Append(s); return this; }

        private void Sep<T>(IEnumerable<T> items, Action<T> each, string sep = ", ")
        {
            bool first = true;
            foreach (var i in items) { if (!first) Add(sep); first = false; each(i); }
        }

        public void Node(SqlNode n)
        {
            switch (n)
            {
                case SelectStatement s: Select(s); break;
                case QueryExpression q: Query(q); break;
                case TableReference t: Table(t); break;
                case Expression e: Expr(e, 0); break;
                case SelectItem si: Item(si); break;
                case OrderByItem o: OrderItem(o); break;
                case Identifier id: Id(id); break;
                case DataType dt: Type(dt); break;
                default: throw Unsupported(n);
            }
        }

        // ---------- names & literals ----------

        private void Id(Identifier i) => Add(i.Quoted ? "\"" + i.Value.Replace("\"", "\"\"") + "\"" : i.Value);
        private void Name(IEnumerable<Identifier> parts) => Sep(parts, Id, ".");
        private void IdList(IEnumerable<Identifier> ids) => Sep(ids, Id);
        private static string Quote(string s) => "'" + s.Replace("'", "''") + "'";

        private void Type(DataType dt)
        {
            if (dt.Attribute != null) { Add(dt.Name).Add("%").Add(dt.Attribute); return; }
            // name words: the parenthesized arguments follow the first word group that precedes WITH/TO/etc.
            var words = dt.Name.Split(' ');
            int argAt = words.Length;
            for (int i = 1; i < words.Length; i++)
                if (words[i] is "WITH" or "WITHOUT" or "TO" or "PRECISION" or "VARYING") { argAt = i; break; }
            if (dt.Name.StartsWith("INTERVAL")) argAt = 2; // INTERVAL DAY(2) TO SECOND(6)
            Add(string.Join(" ", words.Take(argAt)));
            if (dt.Arguments.Count > 0)
            {
                if (dt.Name.StartsWith("INTERVAL") && dt.Arguments.Count == 2)
                {
                    Add("(").Expr(dt.Arguments[0]).Add(")");
                    Add(" " + string.Join(" ", words.Skip(argAt)));
                    Add("(").Expr(dt.Arguments[1]).Add(")");
                    return;
                }
                Add("(");
                Sep(dt.Arguments, a => Expr(a, 0));
                if (dt.Semantics != null) Add(dt.Semantics == CharSemantics.Char ? " CHAR" : " BYTE");
                Add(")");
            }
            if (argAt < words.Length) Add(" " + string.Join(" ", words.Skip(argAt)));
        }

        private W Expr(Expression e) { Expr(e, 0); return this; }

        // ---------- expressions ----------

        private static int Prec(Expression e) => e switch
        {
            BinaryExpression b => b.Operator switch
            {
                "OR" => 1, "AND" => 2,
                "+" or "-" or "||" => 5,
                "*" or "/" => 6,
                "**" => 8,
                var o when o.StartsWith("MULTISET") => 5,
                _ => 4,
            },
            UnaryExpression u => u.Operator == "NOT" ? 3 : 7,
            BetweenExpression or InExpression or LikeExpression or IsExpression or QuantifiedComparison => 4,
            AtTimeZoneExpression or CollateExpression => 9,
            _ => 100,
        };

        private void Child(Expression e, int minPrec)
        {
            bool paren = Prec(e) < minPrec;
            if (paren) Add("(");
            Expr(e, 0);
            if (paren) Add(")");
        }

        private void Expr(Expression e, int _)
        {
            switch (e)
            {
                case Literal l: Lit(l); break;
                case ColumnReference c:
                    Name(c.Parts);
                    if (c.Attribute != null) Add("%" + c.Attribute);
                    if (c.OuterJoin) Add("(+)");
                    break;
                case BindVariable b: Add(":" + b.Name); break;
                case SubstitutionVariable sv: Add(sv.Persistent ? "&&" : "&").Add(sv.Name); break;
                case StarExpression st:
                    if (st.Qualifier.Count > 0) { Name(st.Qualifier); Add("."); }
                    Add("*");
                    break;
                case ParenthesizedExpression p: Add("("); Expr(p.Inner, 0); Add(")"); break;
                case ExpressionList el: Add("("); Sep(el.Items, i => Expr(i, 0)); Add(")"); break;
                case BinaryExpression be: Binary(be); break;
                case UnaryExpression ue:
                {
                    var inner = new W();
                    int op = ue.Operator == "NOT" ? 3 : 7;
                    inner.Child(ue.Operand, op);
                    var t = inner.ToString();
                    if (ue.Operator is "NOT" or "PRIOR" or "CONNECT_BY_ROOT") Add(ue.Operator + " " + t);
                    else Add(ue.Operator + (t.StartsWith('-') || t.StartsWith('+') ? " " : "") + t);
                    break;
                }
                case BetweenExpression bt:
                    Child(bt.Value, 5); Add(bt.Not ? " NOT BETWEEN " : " BETWEEN "); Child(bt.Low, 5); Add(" AND "); Child(bt.High, 5);
                    break;
                case InExpression ie:
                    Child(ie.Value, 5); Add(ie.Not ? " NOT IN (" : " IN (");
                    if (ie.Subquery != null) Select(ie.Subquery); else Sep(ie.List, i => Expr(i, 0));
                    Add(")");
                    break;
                case LikeExpression lk:
                    Child(lk.Value, 5);
                    Add((lk.Not ? " NOT " : " ") + lk.Kind.ToString().ToUpperInvariant() + " ");
                    Child(lk.Pattern, 5);
                    if (lk.Escape != null) { Add(" ESCAPE "); Child(lk.Escape, 5); }
                    break;
                case IsExpression isx: Is(isx); break;
                case QuantifiedComparison qc:
                    Child(qc.Left, 5); Add(" " + qc.Operator + " " + qc.Quantifier.ToString().ToUpperInvariant() + " ");
                    Expr(qc.Right, 0);
                    break;
                case ExistsExpression ex: Add("EXISTS ("); Select(ex.Subquery); Add(")"); break;
                case SubqueryExpression sq: Add("("); Select(sq.Query); Add(")"); break;
                case CursorExpression cu: Add("CURSOR("); Select(cu.Query); Add(")"); break;
                case CaseExpression ce:
                    Add("CASE");
                    if (ce.Operand != null) { Add(" "); Expr(ce.Operand, 0); }
                    foreach (var w in ce.Whens) { Add(" WHEN "); Expr(w.When, 0); Add(" THEN "); Expr(w.Then, 0); }
                    if (ce.Else != null) { Add(" ELSE "); Expr(ce.Else, 0); }
                    Add(" END");
                    break;
                case CastExpression ca: Cast(ca); break;
                case ExtractExpression ex2: Add("EXTRACT(" + ex2.Field.ToString().ToUpperInvariant().Replace("TIMEZONE", "TIMEZONE_") + " FROM "); Expr(ex2.Source, 0); Add(")"); break;
                case TrimExpression tr:
                    Add("TRIM(");
                    if (tr.Spec != null) Add(tr.Spec.ToString()!.ToUpperInvariant() + " ");
                    if (tr.Characters != null) { Expr(tr.Characters, 0); Add(" "); }
                    if (tr.Spec != null || tr.Characters != null) Add("FROM ");
                    Expr(tr.Source, 0); Add(")");
                    break;
                case AtTimeZoneExpression az:
                    Child(az.Value, 9);
                    if (az.Local) Add(" AT LOCAL"); else { Add(" AT TIME ZONE "); Child(az.Zone!, 100); }
                    break;
                case CollateExpression co: Child(co.Value, 9); Add(" COLLATE "); Id(co.Collation); break;
                case IntervalLiteral il: Interval(il); break;
                case FunctionCall fc: Function(fc); break;
                case MemberAccess ma: Expr(ma.Target, 0); Add("."); Id(ma.Member); break;
                case CellReference cr:
                    Expr(cr.Base, 0); Add("[");
                    Sep(cr.Subscripts, s => Expr(s, 0));
                    Add("]");
                    break;
                case ForIterationSubscript fi: ForIteration(fi); break;
                case CurrentOfExpression co2: Add("CURRENT OF "); Id(co2.Cursor); break;
                default: throw Unsupported(e);
            }
        }

        private void Lit(Literal l)
        {
            switch (l.Kind)
            {
                case LiteralKind.String: Add((l.National ? "N" : "") + Quote(l.Value ?? "")); break;
                case LiteralKind.Date: Add("DATE " + Quote(l.Value ?? "")); break;
                case LiteralKind.Timestamp: Add("TIMESTAMP " + Quote(l.Value ?? "")); break;
                case LiteralKind.Null: Add("NULL"); break;
                case LiteralKind.Default: Add("DEFAULT"); break;
                case LiteralKind.Any: Add("ANY"); break;
                default: Add(l.Value ?? ""); break;
            }
        }

        private void Binary(BinaryExpression b)
        {
            int p = Prec(b);
            bool assoc = b.Operator is "AND" or "OR";
            Child(b.Left, p);
            Add(" " + b.Operator + " ");
            Child(b.Right, assoc ? p : p + 1);
        }

        private void Is(IsExpression x)
        {
            Child(x.Value, 5);
            Add(x.Not ? " IS NOT " : " IS ");
            switch (x.Test)
            {
                case IsTest.Null: Add("NULL"); break;
                case IsTest.Nan: Add("NAN"); break;
                case IsTest.Infinite: Add("INFINITE"); break;
                case IsTest.Empty: Add("EMPTY"); break;
                case IsTest.Present: Add("PRESENT"); break;
                case IsTest.Dangling: Add("DANGLING"); break;
                case IsTest.ASet: Add("A SET"); break;
                case IsTest.Json: Add("JSON"); foreach (var c in x.JsonClauses) { Add(" "); Clause(c); } break;
                case IsTest.OfType:
                    Add("OF (");
                    Sep(x.OfTypes, o => { if (o.Only) Add("ONLY "); Type(o.Type); });
                    Add(")");
                    break;
            }
        }

        private void Interval(IntervalLiteral il)
        {
            Add("INTERVAL "); Expr(il.Value, 0); Add(" " + il.From.ToString().ToUpperInvariant());
            if (il.FromPrecision != null) Add($"({il.FromPrecision})");
            if (il.To != null)
            {
                Add(" TO " + il.To.ToString()!.ToUpperInvariant());
                if (il.ToPrecision != null) Add($"({il.ToPrecision})");
            }
        }

        private void Cast(CastExpression c)
        {
            string fn = c.Kind switch { CastKind.XmlCast => "XMLCAST", CastKind.Treat => "TREAT", CastKind.ValidateConversion => "VALIDATE_CONVERSION", CastKind.XmlSerialize => "XMLSERIALIZE", _ => "CAST" };
            Add(fn + "(");
            if (c.Kind == CastKind.XmlSerialize)
                foreach (var f in c.Clauses.OfType<FlagClause>().Where(f => f.Flag is FunctionFlag.Document or FunctionFlag.Content)) { Clause(f); Add(" "); }
            if (c.Multiset) Add("MULTISET");
            Expr(c.Value, 0);
            Add(" AS ");
            if (c.Ref) Add("REF ");
            Type(c.Type);
            if (c.OnConversionError != null) { Add(" "); Clause(c.OnConversionError); }
            if (c.Format != null) { Add(", "); Expr(c.Format, 0); if (c.NlsParam != null) { Add(", "); Expr(c.NlsParam, 0); } }
            foreach (var cl in c.Clauses.Where(x => !(x is FlagClause f && f.Flag is FunctionFlag.Document or FunctionFlag.Content)))
            {
                Add(" "); Clause(cl);
            }
            Add(")");
        }

        private void ForIteration(ForIterationSubscript fi)
        {
            Add("FOR ");
            if (fi.Dimensions.Count == 1) Id(fi.Dimensions[0]); else { Add("("); IdList(fi.Dimensions); Add(")"); }
            if (fi.InQuery != null) { Add(" IN ("); Select(fi.InQuery); Add(")"); }
            else if (fi.From == null) { Add(" IN ("); Sep(fi.InList, i => Expr(i, 0)); Add(")"); }
            else
            {
                if (fi.LikePattern != null) { Add(" LIKE "); Expr(fi.LikePattern, 0); }
                Add(" FROM "); Expr(fi.From, 0); Add(" TO "); Expr(fi.To!, 0);
                if (fi.Step != null) { Add(fi.Decrement ? " DECREMENT " : " INCREMENT "); Expr(fi.Step, 0); }
            }
        }

        // ---------- functions ----------

        private void Function(FunctionCall fc)
        {
            if (fc.New) Add("NEW ");
            Name(fc.Name);
            if (fc.Attribute != null) Add("%" + fc.Attribute);
            Add("(");
            if (fc.Quantifier != null) Add(fc.Quantifier.ToString()!.ToUpperInvariant() + " ");
            Sep(fc.Arguments, Arg);
            if (fc.Operations.Count > 0)
            {
                foreach (var op in fc.Operations) { Add(", "); JsonOp(op); }
            }
            if (fc.OrderBy.Count > 0) { Add(" ORDER BY "); Sep(fc.OrderBy, OrderItem); }
            foreach (var c in fc.Clauses) { Add(" "); Clause(c); }
            Add(")");
            if (fc.WithinGroup.Count > 0) { Add(" WITHIN GROUP (ORDER BY "); Sep(fc.WithinGroup, OrderItem); Add(")"); }
            if (fc.Keep != null) { Add(" KEEP (DENSE_RANK " + (fc.Keep.First ? "FIRST" : "LAST") + " ORDER BY "); Sep(fc.Keep.OrderBy, OrderItem); Add(")"); }
            if (fc.IgnoreNulls) Add(" IGNORE NULLS");
            if (fc.RespectNulls) Add(" RESPECT NULLS");
            if (fc.Over != null) { Add(" OVER "); Window(fc.Over); }
        }

        private void Arg(Argument a)
        {
            if (a.Name != null) { Id(a.Name); Add(" => "); }
            if (a.Keyword != null) Add(a.Keyword.ToString()!.ToUpperInvariant() + " ");
            Expr(a.Value, 0);
            if (a.MappedValue != null) { Add(" VALUE "); Expr(a.MappedValue, 0); }
            if (a.Format != null) { Add(" "); Clause(a.Format); }
            if (a.Alias != null) { Add(" AS "); Id(a.Alias); }
            if (a.OnConversionError != null) { Add(" "); Clause(a.OnConversionError); }
        }

        private void JsonOp(JsonTransformOperation op)
        {
            Add(op.Op.ToString().ToUpperInvariant() + " "); Expr(op.Path, 0);
            if (op.Op == JsonTransformOp.Rename) { Add(" WITH "); Expr(op.Value!, 0); }
            else if (op.Op == JsonTransformOp.Sort) { if (op.Descending) Add(" DESC"); }
            else if (op.Value != null) { Add(" = "); Expr(op.Value, 0); if (op.Format != null) { Add(" "); Clause(op.Format); } }
            foreach (var c in op.Clauses) { Add(" "); Clause(c); }
        }

        private static string FlagText(FunctionFlag f) => f switch
        {
            FunctionFlag.WithUniqueKeys => "WITH UNIQUE KEYS", FunctionFlag.WithoutUniqueKeys => "WITHOUT UNIQUE KEYS",
            FunctionFlag.NoIndent => "NO INDENT", FunctionFlag.HideDefaults => "HIDE DEFAULTS", FunctionFlag.ShowDefaults => "SHOW DEFAULTS",
            FunctionFlag.AllowScalars => "ALLOW SCALARS", FunctionFlag.DisallowScalars => "DISALLOW SCALARS",
            FunctionFlag.AllowArray => "ALLOW ARRAY", FunctionFlag.DisallowArray => "DISALLOW ARRAY",
            _ => f.ToString().ToUpperInvariant(),
        };

        private void Clause(FunctionClause c)
        {
            switch (c)
            {
                case ReturningClause r: Add("RETURNING "); if (r.Content) Add("CONTENT"); else Type(r.Type!); break;
                case FormatClause f: Add("FORMAT JSON"); if (f.Encoding != null) { Add(" ENCODING "); Id(f.Encoding); } break;
                case NullOnClause n: Add(n.Absent ? "ABSENT ON NULL" : "NULL ON NULL"); break;
                case OnBehaviorClause b:
                    Add(b.Behavior switch
                    {
                        BehaviorKind.Default => "DEFAULT ", BehaviorKind.EmptyArray => "EMPTY ARRAY", BehaviorKind.EmptyObject => "EMPTY OBJECT",
                        _ => b.Behavior.ToString().ToUpperInvariant(),
                    });
                    if (b.Behavior == BehaviorKind.Default) Expr(b.Value!, 0);
                    Add(" ON " + (b.Event == BehaviorEvent.ConversionError ? "CONVERSION ERROR" : b.Event.ToString().ToUpperInvariant()));
                    break;
                case WrapperClause w:
                    Add(w.With ? "WITH " : "WITHOUT ");
                    if (w.Conditional) Add("CONDITIONAL "); else if (w.Unconditional) Add("UNCONDITIONAL ");
                    if (w.Array) Add("ARRAY ");
                    Add("WRAPPER");
                    break;
                case QuotesClause q: Add((q.Keep ? "KEEP" : "OMIT") + " QUOTES" + (q.OnScalarString ? " ON SCALAR STRING" : "")); break;
                case OverflowClause o:
                    Add("ON OVERFLOW ");
                    if (!o.Truncate) Add("ERROR");
                    else
                    {
                        Add("TRUNCATE");
                        if (o.Indicator != null) { Add(" "); Expr(o.Indicator, 0); }
                        if (o.WithCount != null) Add(o.WithCount == true ? " WITH COUNT" : " WITHOUT COUNT");
                    }
                    break;
                case PassingClause p:
                    Add("PASSING " + (p.ByValue ? "BY VALUE " : ""));
                    Sep(p.Items, Item);
                    break;
                case FlagClause f2: Add(FlagText(f2.Flag)); break;
                case ValueClause v:
                    switch (v.Kind)
                    {
                        case ValueClauseKind.Encoding: Add("ENCODING "); Expr(v.Value!, 0); break;
                        case ValueClauseKind.Version: Add("VERSION "); Expr(v.Value!, 0); break;
                        case ValueClauseKind.IndentSize: Add("SIZE = "); Expr(v.Value!, 0); break;
                        case ValueClauseKind.StandaloneYes: Add("STANDALONE YES"); break;
                        case ValueClauseKind.StandaloneNo: Add("STANDALONE NO"); break;
                        default: Add("STANDALONE NO VALUE"); break;
                    }
                    break;
                case UsingCharsetClause u: Add("USING "); Id(u.Charset); break;
                case UsingListClause ul: Add("USING "); Sep(ul.Items, Item); break;
                default: throw Unsupported(c);
            }
        }

        private void Window(WindowSpec w)
        {
            if (w.Name != null && w.PartitionBy.Count == 0 && w.OrderBy.Count == 0 && w.Frame == null) { Id(w.Name); return; }
            Add("(");
            bool any = false;
            if (w.Name != null) { Id(w.Name); any = true; }
            if (w.PartitionBy.Count > 0) { if (any) Add(" "); Add("PARTITION BY "); Sep(w.PartitionBy, e => Expr(e, 0)); any = true; }
            if (w.OrderBy.Count > 0) { if (any) Add(" "); Add("ORDER BY "); Sep(w.OrderBy, OrderItem); any = true; }
            if (w.Frame != null)
            {
                if (any) Add(" ");
                Add(w.Frame.Unit.ToString().ToUpperInvariant() + " ");
                if (w.Frame.End != null) { Add("BETWEEN "); Bound(w.Frame.Start); Add(" AND "); Bound(w.Frame.End); }
                else Bound(w.Frame.Start);
            }
            Add(")");
        }

        private void Bound(FrameBound b)
        {
            switch (b.Kind)
            {
                case FrameBoundKind.UnboundedPreceding: Add("UNBOUNDED PRECEDING"); break;
                case FrameBoundKind.UnboundedFollowing: Add("UNBOUNDED FOLLOWING"); break;
                case FrameBoundKind.CurrentRow: Add("CURRENT ROW"); break;
                case FrameBoundKind.Preceding: Child(b.Offset!, 5); Add(" PRECEDING"); break;
                default: Child(b.Offset!, 5); Add(" FOLLOWING"); break;
            }
        }

        // ---------- queries ----------

        private void OrderItem(OrderByItem o)
        {
            Expr(o.Expression, 0);
            if (o.Descending) Add(" DESC");
            if (o.Nulls != null) Add(o.Nulls == NullsOrdering.First ? " NULLS FIRST" : " NULLS LAST");
        }

        private void Item(SelectItem si)
        {
            switch (si)
            {
                case StarSelectItem st:
                    if (st.Qualifier.Count > 0) { Name(st.Qualifier); Add("."); }
                    Add("*");
                    break;
                case ExpressionSelectItem e:
                    Expr(e.Expression, 0);
                    if (e.Alias != null) { Add(" AS "); Id(e.Alias); }
                    break;
                default: throw Unsupported(si);
            }
        }

        private void Items(IEnumerable<ExpressionSelectItem> items) { Add("("); Sep(items, i => Item(i)); Add(")"); }

        private void Select(SelectStatement s)
        {
            if (s.WithDeclarations.Count > 0) throw Unsupported(s.WithDeclarations[0]);
            if (s.With.Count > 0)
            {
                Add("WITH ");
                Sep(s.With, c =>
                {
                    Id(c.Name);
                    if (c.Columns.Count > 0) { Add(" ("); IdList(c.Columns); Add(")"); }
                    Add(" AS ("); Select(c.Query); Add(")");
                    if (c.Search != null)
                    {
                        Add(" SEARCH " + (c.Search.DepthFirst ? "DEPTH" : "BREADTH") + " FIRST BY ");
                        Sep(c.Search.By, OrderItem); Add(" SET "); Id(c.Search.SetColumn);
                    }
                    if (c.Cycle != null)
                    {
                        Add(" CYCLE "); IdList(c.Cycle.Columns); Add(" SET "); Id(c.Cycle.SetColumn);
                        if (c.Cycle.CycleValue != null) { Add(" TO "); Expr(c.Cycle.CycleValue, 0); }
                        if (c.Cycle.DefaultValue != null) { Add(" DEFAULT "); Expr(c.Cycle.DefaultValue, 0); }
                    }
                });
                Add(" ");
            }
            Query(s.Body);
            if (s.OrderBy.Count > 0) { Add(s.OrderSiblings ? " ORDER SIBLINGS BY " : " ORDER BY "); Sep(s.OrderBy, OrderItem); }
            if (s.Offset != null) { Add(" OFFSET "); Child(s.Offset, 5); Add(" ROWS"); }
            if (s.Fetch != null)
            {
                Add(s.Fetch.Next ? " FETCH NEXT" : " FETCH FIRST");
                if (s.Fetch.Count != null) { Add(" "); Child(s.Fetch.Count, 5); if (s.Fetch.Percent) Add(" PERCENT"); Add(" ROWS"); }
                else Add(" ROW");
                Add(s.Fetch.WithTies ? " WITH TIES" : " ONLY");
            }
            if (s.ForUpdate != null)
            {
                Add(" FOR UPDATE");
                if (s.ForUpdate.Of.Count > 0) { Add(" OF "); Sep(s.ForUpdate.Of, c => Expr(c, 0)); }
                if (s.ForUpdate.NoWait) Add(" NOWAIT");
                else if (s.ForUpdate.Wait != null) { Add(" WAIT "); Expr(s.ForUpdate.Wait, 0); }
                else if (s.ForUpdate.SkipLocked) Add(" SKIP LOCKED");
            }
        }

        private void Query(QueryExpression q)
        {
            switch (q)
            {
                case QuerySpecification qs: Spec(qs); break;
                case SetOperation so:
                    Query(so.Left);
                    Add(" " + so.Operator.ToString().ToUpperInvariant() + (so.All ? " ALL " : " "));
                    Query(so.Right);
                    break;
                case ParenthesizedQuery pq: Add("("); Select(pq.Query); Add(")"); break;
                default: throw Unsupported(q);
            }
        }

        private void Spec(QuerySpecification qs)
        {
            Add("SELECT ");
            if (qs.Hint != null)
            {
                var text = qs.Hint.Text.Length > 0 ? qs.Hint.Text
                    : string.Join(" ", qs.Hint.Items.Select(i => i.Name + (i.Arguments.Count > 0 ? "(" + string.Join(" ", i.Arguments.Select(a => Print(a))) + ")" : "")));
                Add("/*+ " + text + " */ ");
            }
            if (qs.Quantifier != null) Add(qs.Quantifier.ToString()!.ToUpperInvariant() + " ");
            Sep(qs.SelectList, Item);
            if (qs.Into.Count > 0) { Add(qs.IntoBulkCollect ? " BULK COLLECT INTO " : " INTO "); Sep(qs.Into, e => Expr(e, 0)); }
            if (qs.From.Count > 0) { Add(" FROM "); Sep(qs.From, Table); }
            if (qs.Where != null) { Add(" WHERE "); Expr(qs.Where, 0); }
            if (qs.StartWith != null) { Add(" START WITH "); Expr(qs.StartWith, 0); }
            if (qs.ConnectBy != null) { Add(qs.NoCycle ? " CONNECT BY NOCYCLE " : " CONNECT BY "); Expr(qs.ConnectBy, 0); }
            if (qs.GroupBy.Count > 0) { Add(" GROUP BY "); Sep(qs.GroupBy, e => Expr(e, 0)); }
            if (qs.Having != null) { Add(" HAVING "); Expr(qs.Having, 0); }
            if (qs.Model != null) { Add(" "); Model(qs.Model); }
        }

        private static string Print(SyntaxItem i)
        {
            var w = new W();
            w.Syntax(i);
            return w.ToString();
        }

        private void Syntax(SyntaxItem i)
        {
            switch (i)
            {
                case WordItem w: Id(new Identifier { Value = w.Value, Quoted = w.Quoted }); break;
                case LiteralItem l: Add(l.Kind == LiteralKind.String ? Quote(l.Value) : l.Value); break;
                case SymbolItem s: Add(s.Symbol); break;
                case BindItem b: Add(":" + b.Name); break;
                case GroupItem g: Add("("); Sep(g.Items, Syntax, " "); Add(")"); break;
                default: throw Unsupported(i);
            }
        }

        // ---------- table references ----------

        private void Alias(TableReference t) { if (t.Alias != null) { Add(" "); Id(t.Alias); } }

        private void Table(TableReference t)
        {
            switch (t)
            {
                case NamedTable nt:
                    Name(nt.Name);
                    if (nt.DbLink.Count > 0) { Add("@"); Name(nt.DbLink); }
                    if (nt.Partition != null) { Add(" PARTITION ("); Id(nt.Partition); Add(")"); }
                    if (nt.Sample != null)
                    {
                        Add(nt.Sample.Block ? " SAMPLE BLOCK (" : " SAMPLE ("); Expr(nt.Sample.Percent, 0); Add(")");
                        if (nt.Sample.Seed != null) { Add(" SEED ("); Expr(nt.Sample.Seed, 0); Add(")"); }
                    }
                    if (nt.Flashback != null) { Add(" "); Flashback(nt.Flashback); }
                    Alias(nt);
                    break;
                case SubqueryTable sq: if (sq.Lateral) Add("LATERAL "); Add("("); Select(sq.Query); Add(")"); Alias(sq); break;
                case TableFunction tf: Add("TABLE("); Expr(tf.Expression, 0); Add(")"); Alias(tf); break;
                case ParenthesizedTable pt: Add("("); Table(pt.Inner); Add(")"); Alias(pt); break;
                case JoinTable jt: Join(jt); break;
                case ValuesTable vt:
                    Add("(VALUES ");
                    Sep(vt.Rows, r => { Add("("); Sep(r, e => Expr(e, 0)); Add(")"); });
                    Add(")");
                    Alias(vt);
                    if (vt.ColumnAliases.Count > 0) { Add(" ("); IdList(vt.ColumnAliases); Add(")"); }
                    break;
                case PivotTable pv: Pivot(pv); break;
                case UnpivotTable up: Unpivot(up); break;
                case MatchRecognizeTable mr: Match(mr); break;
                case XmlTable xt: Xml(xt); break;
                case JsonTable jtb: Json(jtb); break;
                case GraphTable gt:
                    Add("GRAPH_TABLE("); Name(gt.Graph); Add(" "); Sep(gt.Body, Syntax, " "); Add(")"); Alias(gt);
                    break;
                default: throw Unsupported(t);
            }
        }

        private void Flashback(FlashbackClause f)
        {
            Add(f.Mode == FlashbackMode.AsOf ? "AS OF " : "VERSIONS BETWEEN ");
            if (f.Unit == FlashbackUnit.Period) { Add("PERIOD FOR "); Id(f.Period!); Add(" "); }
            else Add(f.Unit == FlashbackUnit.Scn ? "SCN " : "TIMESTAMP ");
            Child(f.Value, 5);
            if (f.ToValue != null) { Add(" AND "); Child(f.ToValue, 5); }
        }

        private void Join(JoinTable j)
        {
            Table(j.Left);
            Add(" ");
            if (j.Natural) Add("NATURAL ");
            Add(j.Kind switch
            {
                JoinKind.Inner => "JOIN", JoinKind.Left => "LEFT OUTER JOIN", JoinKind.Right => "RIGHT OUTER JOIN", JoinKind.Full => "FULL OUTER JOIN",
                JoinKind.Cross => "CROSS JOIN", JoinKind.CrossApply => "CROSS APPLY", _ => "OUTER APPLY",
            });
            Add(" ");
            Table(j.Right);
            if (j.On != null) { Add(" ON "); Expr(j.On, 0); }
            else if (j.Using.Count > 0) { Add(" USING ("); IdList(j.Using); Add(")"); }
        }

        private void ColList(IEnumerable<ColumnReference> cols)
        {
            var l = cols.ToList();
            if (l.Count == 1) Expr(l[0], 0); else { Add("("); Sep(l, c => Expr(c, 0)); Add(")"); }
        }

        private void Pivot(PivotTable pv)
        {
            Table(pv.Source);
            Add(pv.Xml ? " PIVOT XML (" : " PIVOT (");
            Sep(pv.Aggregates, a => { Expr(a.Function, 0); if (a.Alias != null) { Add(" AS "); Id(a.Alias); } });
            Add(" FOR "); ColList(pv.ForColumns); Add(" IN (");
            if (pv.InAny) Add("ANY");
            else if (pv.InSubquery != null) Select(pv.InSubquery);
            else Sep(pv.InItems, i => { Expr(i.Value, 0); if (i.Alias != null) { Add(" AS "); Id(i.Alias); } });
            Add("))");
            Alias(pv);
        }

        private void Unpivot(UnpivotTable up)
        {
            Table(up.Source);
            Add(up.IncludeNulls ? " UNPIVOT INCLUDE NULLS (" : " UNPIVOT (");
            ColList(up.ValueColumns); Add(" FOR "); ColList(up.ForColumns); Add(" IN (");
            Sep(up.InItems, i => { ColList(i.Columns); if (i.AliasValue != null) { Add(" AS "); Expr(i.AliasValue, 0); } });
            Add("))");
            Alias(up);
        }

        private void Xml(XmlTable x)
        {
            Add("XMLTABLE(");
            if (x.Namespaces.Count > 0)
            {
                Add("XMLNAMESPACES(");
                Sep(x.Namespaces, n => { if (n.IsDefault) { Add("DEFAULT "); Expr(n.Uri, 0); } else { Expr(n.Uri, 0); Add(" AS "); Id(n.Alias!); } });
                Add("), ");
            }
            Expr(x.XQuery, 0);
            if (x.Passing != null) { Add(" "); Clause(x.Passing); }
            if (x.Columns.Count > 0) { Add(" COLUMNS "); Sep(x.Columns, PathCol); }
            Add(")");
            Alias(x);
        }

        private void Json(JsonTable j)
        {
            Add("JSON_TABLE("); Expr(j.Input, 0);
            if (j.InputFormat != null) { Add(" "); Clause(j.InputFormat); }
            Add(", "); Expr(j.Path, 0);
            foreach (var c in j.Clauses) { Add(" "); Clause(c); }
            Add(" COLUMNS ("); Sep(j.Columns, PathCol); Add("))");
            Alias(j);
        }

        private void PathCol(PathColumn c)
        {
            if (c.Nested)
            {
                Add("NESTED PATH "); Expr(c.Path!, 0); Add(" COLUMNS ("); Sep(c.Columns, PathCol); Add(")");
                return;
            }
            Id(c.Name!);
            if (c.ForOrdinality) { Add(" FOR ORDINALITY"); return; }
            if (c.Type != null) { Add(" "); Type(c.Type); }
            foreach (var cl in c.Clauses.Where(x => x is not OnBehaviorClause)) { Add(" "); Clause(cl); }
            if (c.Exists) Add(" EXISTS");
            if (c.Path != null) { Add(" PATH "); Expr(c.Path, 0); }
            if (c.Default != null) { Add(" DEFAULT "); Expr(c.Default, 0); }
            foreach (var cl in c.Clauses.OfType<OnBehaviorClause>()) { Add(" "); Clause(cl); }
        }

        private void Match(MatchRecognizeTable m)
        {
            Table(m.Source);
            Add(" MATCH_RECOGNIZE (");
            bool any = false;
            void Gap() { if (any) Add(" "); any = true; }
            if (m.PartitionBy.Count > 0) { Gap(); Add("PARTITION BY "); Sep(m.PartitionBy, e => Expr(e, 0)); }
            if (m.OrderBy.Count > 0) { Gap(); Add("ORDER BY "); Sep(m.OrderBy, OrderItem); }
            if (m.Measures.Count > 0) { Gap(); Add("MEASURES "); Sep(m.Measures, i => Item(i)); }
            if (m.RowsPerMatch == RowsPerMatch.OneRow) { Gap(); Add("ONE ROW PER MATCH"); }
            else if (m.RowsPerMatch == RowsPerMatch.AllRows)
            {
                Gap(); Add("ALL ROWS PER MATCH");
                if (m.AllRowsMode != null) Add(m.AllRowsMode switch { AllRowsMode.ShowEmptyMatches => " SHOW EMPTY MATCHES", AllRowsMode.OmitEmptyMatches => " OMIT EMPTY MATCHES", _ => " WITH UNMATCHED ROWS" });
            }
            if (m.Skip != null)
            {
                Gap(); Add("AFTER MATCH SKIP ");
                switch (m.Skip)
                {
                    case SkipKind.PastLastRow: Add("PAST LAST ROW"); break;
                    case SkipKind.ToNextRow: Add("TO NEXT ROW"); break;
                    case SkipKind.ToFirst: Add("TO FIRST "); Id(m.SkipVariable!); break;
                    default: Add("TO LAST "); Id(m.SkipVariable!); break;
                }
            }
            Gap(); Add(m.Seek ? "SEEK " : ""); Add("PATTERN ("); Pattern(m.Pattern); Add(")");
            if (m.Subsets.Count > 0) { Gap(); Add("SUBSET "); Sep(m.Subsets, s => { Id(s.Name); Add(" = ("); IdList(s.Members); Add(")"); }); }
            Gap(); Add("DEFINE "); Sep(m.Define, d => { Id(d.Variable); Add(" AS "); Expr(d.Condition, 0); });
            Add(")");
            Alias(m);
        }

        private void Pattern(PatternNode p)
        {
            switch (p)
            {
                case PatternVariable v: Id(v.Name); break;
                case PatternAnchor a: Add(a.Start ? "^" : "$"); break;
                case PatternConcat c: Sep(c.Items, Pattern, " "); break;
                case PatternAlternation al: Sep(al.Items, Pattern, "|"); break;
                case PatternGroup g: Add("("); Pattern(g.Inner); Add(")"); break;
                case PatternPermute pp: Add("PERMUTE("); Sep(pp.Items, Pattern); Add(")"); break;
                case PatternExclusion ex: Add("{- "); Pattern(ex.Inner); Add(" -}"); break;
                case PatternQuantified q:
                    Pattern(q.Inner);
                    Add((q.Min, q.Max) switch
                    {
                        (0, null) => "*", (1, null) => "+", (0, 1) => "?",
                        (var mn, null) => $"{{{mn},}}",
                        (var mn, var mx) when mn == mx => $"{{{mn}}}",
                        (0, var mx) => $"{{,{mx}}}",
                        (var mn, var mx) => $"{{{mn},{mx}}}",
                    });
                    if (q.Reluctant) Add("?");
                    break;
                default: throw Unsupported(p);
            }
        }

        private void Model(ModelClause m)
        {
            Add("MODEL");
            void Opts(NavMode? n, ModelUniqueness? u)
            {
                if (n != null) Add(n == NavMode.IgnoreNav ? " IGNORE NAV" : " KEEP NAV");
                if (u != null) Add(u == ModelUniqueness.UniqueDimension ? " UNIQUE DIMENSION" : " UNIQUE SINGLE REFERENCE");
            }
            Opts(m.Nav, m.Uniqueness);
            if (m.Return != null) Add(m.Return == ModelReturn.UpdatedRows ? " RETURN UPDATED ROWS" : " RETURN ALL ROWS");
            foreach (var r in m.References)
            {
                Add(" REFERENCE "); Id(r.Name); Add(" ON ("); Select(r.Query); Add(") DIMENSION BY "); Items(r.DimensionBy);
                Add(" MEASURES "); Items(r.Measures); Opts(r.Nav, r.Uniqueness);
            }
            if (m.MainName != null) { Add(" MAIN "); Id(m.MainName); }
            if (m.PartitionBy.Count > 0) { Add(" PARTITION BY "); Items(m.PartitionBy); }
            Add(" DIMENSION BY "); Items(m.DimensionBy);
            Add(" MEASURES "); Items(m.Measures);
            Opts(m.MainNav, m.MainUniqueness);
            Add(" RULES");
            if (m.RulesMode != null) Add(m.RulesMode switch { RulesMode.Update => " UPDATE", RulesMode.Upsert => " UPSERT", _ => " UPSERT ALL" });
            if (m.RulesOrder != null) Add(m.RulesOrder == RulesOrder.Automatic ? " AUTOMATIC ORDER" : " SEQUENTIAL ORDER");
            if (m.Iterate != null)
            {
                Add(" ITERATE ("); Expr(m.Iterate, 0); Add(")");
                if (m.IterateUntil != null) { Add(" UNTIL ("); Expr(m.IterateUntil, 0); Add(")"); }
            }
            Add(" (");
            Sep(m.Rules, r =>
            {
                if (r.Mode != null) Add(r.Mode switch { RulesMode.Update => "UPDATE ", RulesMode.Upsert => "UPSERT ", _ => "UPSERT ALL " });
                Expr(r.Target, 0);
                if (r.OrderBy.Count > 0) { Add(" ORDER BY "); Sep(r.OrderBy, OrderItem); }
                Add(" = "); Expr(r.Value, 0);
            });
            Add(")");
        }
    }
}
