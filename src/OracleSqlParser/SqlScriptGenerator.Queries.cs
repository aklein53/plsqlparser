using System.Text;

namespace OracleSqlParser;

public sealed partial class SqlScriptGenerator
{
    private sealed partial class Printer
    {
        // ---------- queries ----------

        private void OrderItem(OrderByItem o)
        {
            Expr(o.Expression);
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
                    Expr(e.Expression);
                    if (e.Alias != null) { Add(" AS "); Id(e.Alias); }
                    break;
                default: throw Unsupported(si);
            }
        }

        private void Items(IEnumerable<ExpressionSelectItem> items) { Add("("); Sep(items, i => Item(i)); Add(")"); }

        private bool Multiline =>
            _o.NewLineBeforeFromClause || _o.NewLineBeforeWhereClause || _o.NewLineBeforeGroupByClause || _o.NewLineBeforeOrderByClause ||
            _o.NewLineBeforeJoinClause || _o.NewLineBeforeSetOperator || _o.MultilineSelectElementsList || _o.AlignClauseBodies;

        /// <summary>"(" query ")": the query goes on its own indented lines when any multi-line option is on.</summary>
        private void ParenQuery(SelectStatement q)
        {
            Add("(");
            if (Multiline) { _indent++; NewLine(); Select(q); _indent--; NewLine(); }
            else Select(q);
            Add(")");
        }

        private void Cte(CommonTableExpression c)
        {
            Id(c.Name);
            if (c.Columns.Count > 0) { Add(" ("); IdList(c.Columns); Add(")"); }
            Add(" AS ");
            ParenQuery(c.Query);
            if (c.Search != null)
            {
                Add(" SEARCH " + (c.Search.DepthFirst ? "DEPTH" : "BREADTH") + " FIRST BY ");
                Sep(c.Search.By, OrderItem); Add(" SET "); Id(c.Search.SetColumn);
            }
            if (c.Cycle != null)
            {
                Add(" CYCLE "); IdList(c.Cycle.Columns); Add(" SET "); Id(c.Cycle.SetColumn);
                if (c.Cycle.CycleValue != null) { Add(" TO "); Expr(c.Cycle.CycleValue); }
                if (c.Cycle.DefaultValue != null) { Add(" DEFAULT "); Expr(c.Cycle.DefaultValue); }
            }
        }

        private void Select(SelectStatement s)
        {
            if (s.WithDeclarations.Count > 0 || s.With.Count > 0)
            {
                Add("WITH");
                foreach (var d in s.WithDeclarations) { Sp(); Declaration(d); Add(";"); }
                if (s.With.Count > 0)
                {
                    Sp();
                    if (Multiline) { Sep(s.With, Cte, ",\n" + new string(' ', _indent * _o.IndentSize)); }
                    else Sep(s.With, Cte);
                }
                if (Multiline) NewLine(); else Sp();
            }
            Query(s.Body);
            if (s.OrderBy.Count > 0)
            {
                ClauseStart(_o.NewLineBeforeOrderByClause);
                Add(s.OrderSiblings ? "ORDER SIBLINGS BY" : "ORDER BY");
                ClauseBody(() => Sep(s.OrderBy, OrderItem));
            }
            if (s.Offset != null) { ClauseStart(_o.NewLineBeforeOrderByClause); Add("OFFSET "); Child(s.Offset, 5); Add(" ROWS"); }
            if (s.Fetch != null)
            {
                ClauseStart(_o.NewLineBeforeOrderByClause);
                Add("FETCH ");
                if (s.Fetch.Accuracy is { } accuracy) Add(accuracy == FetchAccuracy.Exact ? "EXACT " : "APPROXIMATE ");
                Add(s.Fetch.Next ? "NEXT" : "FIRST");
                if (s.Fetch.Count != null) { Add(" "); Child(s.Fetch.Count, 5); if (s.Fetch.Percent) Add(" PERCENT"); Add(" ROWS"); }
                else Add(" ROW");
                Add(s.Fetch.WithTies ? " WITH TIES" : " ONLY");
                if (s.Fetch.TargetAccuracy != null) { Add(" WITH TARGET ACCURACY "); Child(s.Fetch.TargetAccuracy, 5); Add(" PERCENT"); }
            }
            if (s.ForUpdate != null)
            {
                ClauseStart(_o.NewLineBeforeOrderByClause);
                Add("FOR UPDATE");
                if (s.ForUpdate.Of.Count > 0) { Add(" OF "); Sep(s.ForUpdate.Of, c => Expr(c)); }
                if (s.ForUpdate.NoWait) Add(" NOWAIT");
                else if (s.ForUpdate.Wait != null) { Add(" WAIT "); Expr(s.ForUpdate.Wait); }
                else if (s.ForUpdate.SkipLocked) Add(" SKIP LOCKED");
            }
        }

        private void Query(QueryExpression q)
        {
            switch (q)
            {
                case QuerySpecification qs: Spec(qs); break;
                case SetOperation so:
                {
                    // iterate the left spine: a 20,000-term UNION chain must not recurse once per link
                    var spine = new List<SetOperation> { so };
                    QueryExpression cur = so.Left;
                    while (cur is SetOperation l) { spine.Add(l); cur = l.Left; }
                    Query(cur);
                    for (int i = spine.Count - 1; i >= 0; i--)
                    {
                        var x = spine[i];
                        ClauseStart(_o.NewLineBeforeSetOperator);
                        Add(x.Operator.ToString().ToUpperInvariant() + (x.All ? " ALL" : ""));
                        ClauseStart(_o.NewLineBeforeSetOperator);
                        Query(x.Right);
                    }
                    break;
                }
                case ParenthesizedQuery pq: ParenQuery(pq.Query); break;
                case ValuesQuery vq:
                    Add("VALUES ");
                    Sep(vq.Rows, r => { Add("("); Sep(r, e => Expr(e)); Add(")"); });
                    break;
                default: throw Unsupported(q);
            }
        }

        private void Hint(HintClause h)
        {
            var text = h.Text.Length > 0 ? h.Text
                : string.Join(" ", h.Items.Select(i => i.Name + (i.Arguments.Count > 0 ? "(" + SyntaxText(i.Arguments) + ")" : "")));
            Raw("/*+ " + text + " */");
        }

        private void Spec(QuerySpecification qs)
        {
            Add("SELECT");
            if (qs.Hint != null) { Sp(); Hint(qs.Hint); }
            if (qs.Quantifier != null) Add(" " + qs.Quantifier.ToString()!.ToUpperInvariant());
            if (_o.MultilineSelectElementsList) Indent(() => SepLines(qs.SelectList, Item));
            else ClauseBody(() => Sep(qs.SelectList, Item));
            if (qs.Into.Count > 0) { Add(qs.IntoBulkCollect ? " BULK COLLECT INTO " : " INTO "); Sep(qs.Into, e => Expr(e)); }
            if (qs.From.Count > 0) { ClauseStart(_o.NewLineBeforeFromClause); Add("FROM"); ClauseBody(() => Sep(qs.From, Table)); }
            if (qs.Where != null) { ClauseStart(_o.NewLineBeforeWhereClause); Add("WHERE"); ClauseBody(() => Expr(qs.Where)); }
            if (qs.StartWith != null) { ClauseStart(_o.NewLineBeforeHierarchicalClause); Add("START WITH "); Expr(qs.StartWith); }
            if (qs.ConnectBy != null) { ClauseStart(_o.NewLineBeforeHierarchicalClause); Add(qs.NoCycle ? "CONNECT BY NOCYCLE " : "CONNECT BY "); Expr(qs.ConnectBy); }
            if (qs.GroupBy.Count > 0) { ClauseStart(_o.NewLineBeforeGroupByClause); Add("GROUP BY"); ClauseBody(() => Sep(qs.GroupBy, e => Expr(e))); }
            if (qs.Having != null) { ClauseStart(_o.NewLineBeforeHavingClause); Add("HAVING"); ClauseBody(() => Expr(qs.Having)); }
            if (qs.Model != null) { Add(" "); Model(qs.Model); }
        }

        // ---------- table references ----------

        /// <summary>The table printed immediately before a join keyword (its alias is the one that could be misread).</summary>
        private TableReference? _aliasBeforeJoin;

        private void Alias(TableReference t)
        {
            if (t.Alias == null) return;
            Add(" ");
            // Unquoted LEFT/RIGHT/FULL here would read as the join keyword; quoting it names the same identifier.
            if (ReferenceEquals(t, _aliasBeforeJoin) && !t.Alias.Quoted && t.Alias.Value is "LEFT" or "RIGHT" or "FULL")
                Id(new Identifier { Value = t.Alias.Value, Quoted = true });
            else Id(t.Alias);
        }

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
                        Add(nt.Sample.Block ? " SAMPLE BLOCK (" : " SAMPLE ("); Expr(nt.Sample.Percent); Add(")");
                        if (nt.Sample.Seed != null) { Add(" SEED ("); Expr(nt.Sample.Seed); Add(")"); }
                    }
                    if (nt.Flashback != null) { Add(" "); Flashback(nt.Flashback); }
                    Alias(nt);
                    break;
                case SubqueryTable sq: if (sq.Lateral) Add("LATERAL "); ParenQuery(sq.Query); Alias(sq); break;
                case TableFunction tf: Add("TABLE("); Expr(tf.Expression); Add(")"); Alias(tf); break;
                case ParenthesizedTable pt: Add("("); Table(pt.Inner); Add(")"); Alias(pt); break;
                case JoinTable jt: Join(jt); break;
                case ValuesTable vt:
                    Add("(VALUES ");
                    Sep(vt.Rows, r => { Add("("); Sep(r, e => Expr(e)); Add(")"); });
                    Add(")");
                    Alias(vt);
                    if (vt.ColumnAliases.Count > 0) { Add(" ("); IdList(vt.ColumnAliases); Add(")"); }
                    break;
                case PivotTable pv: Pivot(pv); break;
                case UnpivotTable up: Unpivot(up); break;
                case MatchRecognizeTable mr: Match(mr); break;
                case XmlTable xt: Xml(xt); break;
                case JsonTable jtb: Json(jtb); break;
                case GraphTable gt: GraphTableRef(gt); break;
                default: throw Unsupported(t);
            }
        }

        private void GraphTableRef(GraphTable gt)
        {
            Add("GRAPH_TABLE("); Name(gt.Graph); Add(" MATCH "); Sep(gt.Match, GraphPath);
            if (gt.Where != null) { Add(" WHERE "); Expr(gt.Where); }
            if (gt.RowsPer is { } rp)
            {
                Add(rp switch { GraphRowsPer.Match => " ONE ROW PER MATCH", GraphRowsPer.Vertex => " ONE ROW PER VERTEX(", _ => " ONE ROW PER STEP(" });
                if (rp != GraphRowsPer.Match) { Sep(gt.RowsPerVariables, Id); Add(")"); }
            }
            Add(" COLUMNS ("); Sep(gt.Columns, GraphColumnItem); Add("))"); Alias(gt);
        }

        private void GraphPath(GraphPathPattern p)
        {
            foreach (var el in p.Elements)
            {
                switch (el)
                {
                    case GraphVertexPattern v: Add("("); GraphElementBody(v.Variable, v.Label, v.Where); Add(")"); break;
                    case GraphEdgePattern e: GraphEdge(e); break;
                    case GraphSubpatternElement sp:
                        Add("("); GraphPath(sp.Path); Add(")");
                        if (sp.Quantifier is { } sq) GraphQuantifierText(sq);
                        break;
                    default: throw Unsupported(el);
                }
            }
        }

        private void GraphEdge(GraphEdgePattern e)
        {
            bool leftStart = e.Direction is GraphEdgeDirection.Left or GraphEdgeDirection.LeftOrRight;
            if (!e.Bracketed) { Add(e.Direction switch { GraphEdgeDirection.Right => "->", GraphEdgeDirection.Left => "<-", _ => "-" }); return; }
            Add(leftStart ? "<-[" : "-[");
            GraphElementBody(e.Variable, e.Label, e.Where);
            Add("]");
            Add(e.Direction is GraphEdgeDirection.Right or GraphEdgeDirection.LeftOrRight ? "->" : "-");
            if (e.Quantifier is { } q) GraphQuantifierText(q);
        }

        private void GraphQuantifierText(GraphQuantifier q)
        {
            switch (q.Kind)
            {
                case GraphQuantifierKind.Exact: Add("{"); Expr(q.Min!); Add("}"); break;
                default:
                    Add("{"); if (q.Min != null) Expr(q.Min);
                    Add(","); if (q.Max != null) Expr(q.Max);
                    Add("}"); break;
            }
        }

        /// <summary>The variable, IS label list and WHERE of a vertex or edge pattern, separated by single spaces.</summary>
        private void GraphElementBody(Identifier? variable, GraphLabelExpression? label, Expression? where)
        {
            bool sp = false;
            if (variable != null) { Id(variable); sp = true; }
            if (label != null) { if (sp) Add(" "); Add("IS "); Sep(label.Labels, Id, "|"); sp = true; }
            if (where != null) { if (sp) Add(" "); Add("WHERE "); Expr(where); }
        }

        private void GraphColumnItem(GraphColumn c)
        {
            Expr(c.Value);
            if (c.Alias != null) { Add(" AS "); Id(c.Alias); }
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
            // iterate the left spine: a long chain of JOINs must not recurse once per link
            var spine = new List<JoinTable> { j };
            TableReference cur = j.Left;
            while (cur is JoinTable l) { spine.Add(l); cur = l.Left; }
            _aliasBeforeJoin = cur;
            Table(cur);
            for (int i = spine.Count - 1; i >= 0; i--)
            {
                var x = spine[i];
                ClauseStart(_o.NewLineBeforeJoinClause);
                if (x.LeftPartitionBy.Count > 0) { Add("PARTITION BY ("); Sep(x.LeftPartitionBy, e => Expr(e)); Add(") "); }
                if (x.Natural) Add("NATURAL ");
                Add(x.Kind switch
                {
                    JoinKind.Inner => "JOIN", JoinKind.Left => "LEFT OUTER JOIN", JoinKind.Right => "RIGHT OUTER JOIN", JoinKind.Full => "FULL OUTER JOIN",
                    JoinKind.Cross => "CROSS JOIN", JoinKind.CrossApply => "CROSS APPLY", _ => "OUTER APPLY",
                });
                Add(" ");
                Table(x.Right);
                _aliasBeforeJoin = x.Right; // the next iteration's join keyword follows this table
                if (x.RightPartitionBy.Count > 0) { Add(" PARTITION BY ("); Sep(x.RightPartitionBy, e => Expr(e)); Add(")"); }
                if (x.On != null) { Add(" ON "); Expr(x.On); }
                else if (x.Using.Count > 0) { Add(" USING ("); IdList(x.Using); Add(")"); }
            }
        }

        private void ColList(IEnumerable<ColumnReference> cols)
        {
            var l = cols.ToList();
            if (l.Count == 1) Expr(l[0]); else { Add("("); Sep(l, c => Expr(c)); Add(")"); }
        }

        private void Pivot(PivotTable pv)
        {
            Table(pv.Source);
            Add(pv.Xml ? " PIVOT XML (" : " PIVOT (");
            Sep(pv.Aggregates, a => { Expr(a.Function); if (a.Alias != null) { Add(" AS "); Id(a.Alias); } });
            Add(" FOR "); ColList(pv.ForColumns); Add(" IN (");
            if (pv.InAny) Sep(pv.ForColumns, _ => Add("ANY")); // Oracle wants one ANY per FOR column
            else if (pv.InSubquery != null) Select(pv.InSubquery);
            else Sep(pv.InItems, i => { Expr(i.Value); if (i.Alias != null) { Add(" AS "); Id(i.Alias); } });
            Add("))");
            Alias(pv);
        }

        private void Unpivot(UnpivotTable up)
        {
            Table(up.Source);
            Add(up.IncludeNulls ? " UNPIVOT INCLUDE NULLS (" : " UNPIVOT (");
            ColList(up.ValueColumns); Add(" FOR "); ColList(up.ForColumns); Add(" IN (");
            Sep(up.InItems, i => { ColList(i.Columns); if (i.AliasValue != null) { Add(" AS "); Expr(i.AliasValue); } });
            Add("))");
            Alias(up);
        }

        private void Xml(XmlTable x)
        {
            Add("XMLTABLE(");
            if (x.Namespaces.Count > 0)
            {
                Add("XMLNAMESPACES(");
                Sep(x.Namespaces, n => { if (n.IsDefault) { Add("DEFAULT "); Expr(n.Uri); } else { Expr(n.Uri); Add(" AS "); Id(n.Alias!); } });
                Add("), ");
            }
            Expr(x.XQuery);
            if (x.Passing != null) { Add(" "); Clause(x.Passing); }
            if (x.Columns.Count > 0) { Add(" COLUMNS "); Sep(x.Columns, PathCol); }
            Add(")");
            Alias(x);
        }

        private void Json(JsonTable j)
        {
            Add("JSON_TABLE("); Expr(j.Input);
            if (j.InputFormat != null) { Add(" "); Clause(j.InputFormat); }
            Add(", "); Expr(j.Path);
            foreach (var c in j.Clauses) { Add(" "); Clause(c); }
            Add(" COLUMNS ("); Sep(j.Columns, PathCol); Add("))");
            Alias(j);
        }

        private void PathCol(PathColumn c)
        {
            if (c.Nested)
            {
                Add("NESTED PATH "); Expr(c.Path!); Add(" COLUMNS ("); Sep(c.Columns, PathCol); Add(")");
                return;
            }
            Id(c.Name!);
            if (c.ForOrdinality) { Add(" FOR ORDINALITY"); return; }
            if (c.Type != null) { Add(" "); Type(c.Type); }
            foreach (var cl in c.Clauses.Where(x => x is not OnBehaviorClause)) { Add(" "); Clause(cl); }
            if (c.Exists) Add(" EXISTS");
            if (c.Path != null) { Add(" PATH "); Expr(c.Path); }
            if (c.Default != null) { Add(" DEFAULT "); Expr(c.Default); }
            foreach (var cl in c.Clauses.OfType<OnBehaviorClause>()) { Add(" "); Clause(cl); }
        }

        private void Match(MatchRecognizeTable m)
        {
            Table(m.Source);
            Add(" MATCH_RECOGNIZE (");
            bool any = false;
            void Gap() { if (any) Add(" "); any = true; }
            if (m.PartitionBy.Count > 0) { Gap(); Add("PARTITION BY "); Sep(m.PartitionBy, e => Expr(e)); }
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
            Gap(); Add("DEFINE "); Sep(m.Define, d => { Id(d.Variable); Add(" AS "); Expr(d.Condition); });
            Add(")");
            Alias(m);
        }

        private void Pattern(PatternNode p)
        {
            switch (p)
            {
                case PatternVariable v: Id(v.Name); break;
                case PatternAnchor a: Add(a.IsStart ? "^" : "$"); break;
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
                Add(" ITERATE ("); Expr(m.Iterate); Add(")");
                if (m.IterateUntil != null) { Add(" UNTIL ("); Expr(m.IterateUntil); Add(")"); }
            }
            Add(" (");
            Sep(m.Rules, r =>
            {
                if (r.Mode != null) Add(r.Mode switch { RulesMode.Update => "UPDATE ", RulesMode.Upsert => "UPSERT ", _ => "UPSERT ALL " });
                Expr(r.Target);
                if (r.OrderBy.Count > 0) { Add(" ORDER BY "); Sep(r.OrderBy, OrderItem); }
                Add(" = "); Expr(r.Value);
            });
            Add(")");
        }
    }
}
