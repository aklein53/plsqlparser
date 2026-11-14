namespace OracleSqlParser;

public sealed partial class SqlParser
{
    // ---- helpers ----

    /// <summary>End of a statement-level clause list: EOF, ';' or an unbalanced ')' (closing an enclosing group).</summary>
    private bool AtStatementEnd() => Cur.Type == TokenType.EOF || IsOp(";") || IsOp(")");

    private List<ColumnReference> ParseColumnRefList(bool parenthesized)
    {
        var list = new List<ColumnReference>();
        if (parenthesized && AcceptOp("("))
        {
            do list.Add(ParseColumnReferenceOnly()); while (AcceptOp(","));
            ExpectOp(")");
        }
        else list.Add(ParseColumnReferenceOnly());
        return list;
    }

    // ---- SEARCH / CYCLE ----

    private SearchClause ParseSearchClause()
    {
        var s = Next(); // SEARCH
        var sc = new SearchClause { DepthFirst = Next().Value == "DEPTH" };
        ExpectKw("FIRST"); ExpectKw("BY");
        sc.By = ParseOrderByList();
        ExpectKw("SET");
        sc.SetColumn = ParseIdentifier();
        return Fin(sc, s);
    }

    private CycleClause ParseCycleClause()
    {
        var s = Next(); // CYCLE
        var cc = new CycleClause { Columns = ParseIdentifierList() };
        ExpectKw("SET");
        cc.SetColumn = ParseIdentifier();
        if (AcceptKw("TO")) cc.CycleValue = ParseExpr();
        if (AcceptKw("DEFAULT")) cc.DefaultValue = ParseExpr();
        return Fin(cc, s);
    }

    // ---- table modifiers: sample, flashback, pivot, match_recognize ----

    private TableReference ParseTableModifiers(TableReference t, Token start)
    {
        int prog10 = -1;
        while (Progress(ref prog10))
        {
            if (t is NamedTable nt)
            {
                if (AtDualityWithClause()) { ParseDualityAnnotations(nt); continue; }
                // SAMPLE is a clause only before "(" or BLOCK; otherwise it is an alias (checked below)
                if (IsKw("SAMPLE") && (IsOpAt(1, "(") || IsKwAt(1, "BLOCK")) && nt.Sample == null) { nt.Sample = ParseSample(); continue; }
                if (nt.Flashback == null && ((IsKw("VERSIONS") && IsKwAt(1, "BETWEEN")) || (IsKw("AS") && IsKwAt(1, "OF"))))
                {
                    nt.Flashback = ParseFlashback();
                    continue;
                }
            }
            // Oracle takes a GRAPH_TABLE alias without AS (ORA-03048 for AS g).
            if (t.Alias == null && !(t is GraphTable && IsKw("AS")))
            {
                var a = ParseOptionalAlias();
                if (a != null) { t.Alias = a; continue; }
            }
            if (IsKw("PIVOT") && (IsOpAt(1, "(") || IsKwAt(1, "XML"))) { t = ParsePivot(t, start); continue; }
            if (IsKw("UNPIVOT") && (IsOpAt(1, "(") || IsKwAt(1, "INCLUDE") || IsKwAt(1, "EXCLUDE"))) { t = ParseUnpivot(t, start); continue; }
            if (IsKw("MATCH_RECOGNIZE") && IsOpAt(1, "(")) { t = ParseMatchRecognize(t, start); continue; }
            break;
        }
        return Fin(t, start);
    }

    private SampleClause ParseSample()
    {
        var s = Next(); // SAMPLE
        var sc = new SampleClause { Block = AcceptKw("BLOCK") };
        ExpectOp("(");
        sc.Percent = ParseAdditive();
        ExpectOp(")");
        if (AcceptKw("SEED")) { ExpectOp("("); sc.Seed = ParseAdditive(); ExpectOp(")"); }
        return Fin(sc, s);
    }

    private FlashbackClause ParseFlashback()
    {
        var s = Cur;
        var fb = new FlashbackClause();
        if (IsKw("VERSIONS")) { Next(); Next(); fb.Mode = FlashbackMode.VersionsBetween; }
        else { Next(); Next(); fb.Mode = FlashbackMode.AsOf; }
        if (IsKw("PERIOD")) { Next(); ExpectKw("FOR"); fb.Unit = FlashbackUnit.Period; fb.Period = ParseIdentifier(); }
        else if (AcceptKw("SCN")) fb.Unit = FlashbackUnit.Scn;
        else if (AcceptKw("TIMESTAMP")) fb.Unit = FlashbackUnit.Timestamp;
        else throw Fail($"Expected SCN or TIMESTAMP but found {Describe(Cur)}");
        fb.Value = ParseAdditive();
        if (fb.Mode == FlashbackMode.VersionsBetween) { ExpectKw("AND"); fb.ToValue = ParseAdditive(); }
        return Fin(fb, s);
    }

    private TableReference ParsePivot(TableReference source, Token start)
    {
        Next(); // PIVOT
        var pt = new PivotTable { Source = source, Xml = AcceptKw("XML") };
        ExpectOp("(");
        do
        {
            var s = Cur;
            var agg = new PivotAggregate { Function = ParseExpr() };
            agg.Alias = ParseOptionalAlias();
            pt.Aggregates.Add(Fin(agg, s));
        } while (AcceptOp(","));
        ExpectKw("FOR");
        pt.ForColumns = ParseColumnRefList(true);
        ExpectKw("IN");
        ExpectOp("(");
        if (IsKw("ANY"))
        {
            pt.InAny = true;
            Next();
            int prog121 = -1;
            while (AcceptOp(",") && Progress(ref prog121)) ExpectKw("ANY");
        }
        else if (LooksLikeQuery()) pt.InSubquery = ParseSelectStatement();
        else
        {
            do
            {
                var s = Cur;
                var item = new PivotInItem { Value = ParseExpr() };
                item.Alias = ParseOptionalAlias();
                pt.InItems.Add(Fin(item, s));
            } while (AcceptOp(","));
        }
        ExpectOp(")");
        ExpectOp(")");
        return Fin(pt, start);
    }

    private TableReference ParseUnpivot(TableReference source, Token start)
    {
        Next(); // UNPIVOT
        var ut = new UnpivotTable { Source = source };
        if (IsKw("INCLUDE") || IsKw("EXCLUDE"))
        {
            ut.IncludeNulls = Next().Value == "INCLUDE";
            ExpectKw("NULLS");
        }
        ExpectOp("(");
        ut.ValueColumns = ParseColumnRefList(true);
        ExpectKw("FOR");
        ut.ForColumns = ParseColumnRefList(true);
        ExpectKw("IN");
        ExpectOp("(");
        do
        {
            var s = Cur;
            var item = new UnpivotInItem { Columns = ParseColumnRefList(true) };
            if (AcceptKw("AS") || Cur.Type is TokenType.String or TokenType.Number) item.AliasValue = ParsePrimary();
            ut.InItems.Add(Fin(item, s));
        } while (AcceptOp(","));
        ExpectOp(")");
        ExpectOp(")");
        return Fin(ut, start);
    }

    // ---- MATCH_RECOGNIZE ----

    private TableReference ParseMatchRecognize(TableReference source, Token start)
    {
        Next(); Next(); // MATCH_RECOGNIZE (
        var mr = new MatchRecognizeTable { Source = source };
        if (IsKw("PARTITION")) { Next(); ExpectKw("BY"); do mr.PartitionBy.Add(ParseExpr()); while (AcceptOp(",")); }
        if (IsKw("ORDER")) { Next(); ExpectKw("BY"); mr.OrderBy = ParseOrderByList(); }
        if (AcceptKw("MEASURES")) do mr.Measures.Add(ParseExpressionSelectItem()); while (AcceptOp(","));
        if (AcceptKw("ONE")) { ExpectKw("ROW"); ExpectKw("PER"); ExpectKw("MATCH"); mr.RowsPerMatch = RowsPerMatch.OneRow; }
        else if (IsKw("ALL") && IsKwAt(1, "ROWS"))
        {
            Next(); Next(); ExpectKw("PER"); ExpectKw("MATCH");
            mr.RowsPerMatch = RowsPerMatch.AllRows;
            if (AcceptKw("SHOW")) { ExpectKw("EMPTY"); ExpectKw("MATCHES"); mr.AllRowsMode = AllRowsMode.ShowEmptyMatches; }
            else if (AcceptKw("OMIT")) { ExpectKw("EMPTY"); ExpectKw("MATCHES"); mr.AllRowsMode = AllRowsMode.OmitEmptyMatches; }
            else if (IsKw("WITH") && IsKwAt(1, "UNMATCHED")) { Next(); Next(); ExpectKw("ROWS"); mr.AllRowsMode = AllRowsMode.WithUnmatchedRows; }
        }
        if (AcceptKw("AFTER"))
        {
            ExpectKw("MATCH"); ExpectKw("SKIP");
            if (AcceptKw("PAST")) { ExpectKw("LAST"); ExpectKw("ROW"); mr.Skip = SkipKind.PastLastRow; }
            else
            {
                ExpectKw("TO");
                if (AcceptKw("NEXT")) { ExpectKw("ROW"); mr.Skip = SkipKind.ToNextRow; }
                else
                {
                    mr.Skip = AcceptKw("LAST") ? SkipKind.ToLast : (AcceptKw("FIRST") ? SkipKind.ToFirst : SkipKind.ToFirst);
                    mr.SkipVariable = ParseIdentifier();
                }
            }
        }
        if (AcceptKw("SEEK")) mr.Seek = true; else AcceptKw("INITIAL");
        ExpectKw("PATTERN");
        ExpectOp("(");
        mr.Pattern = ParsePatternAlternation();
        ExpectOp(")");
        if (AcceptKw("SUBSET"))
        {
            do
            {
                var s = Cur;
                var ps = new PatternSubset { Name = ParseIdentifier() };
                ExpectOp("=");
                ExpectOp("(");
                ps.Members = ParseIdentifierList();
                ExpectOp(")");
                mr.Subsets.Add(Fin(ps, s));
            } while (AcceptOp(","));
        }
        ExpectKw("DEFINE");
        do
        {
            var s = Cur;
            var pd = new PatternDefine { Variable = ParseIdentifier() };
            ExpectKw("AS");
            pd.Condition = ParseExpr();
            mr.Define.Add(Fin(pd, s));
        } while (AcceptOp(","));
        ExpectOp(")");
        return Fin(mr, start);
    }

    private PatternNode ParsePatternAlternation()
    {
        var start = Cur;
        var first = ParsePatternConcat();
        if (!IsOp("|")) return first;
        var alt = new PatternAlternation { Items = [first] };
        int prog122 = -1;
        while (AcceptOp("|") && Progress(ref prog122)) alt.Items.Add(ParsePatternConcat());
        return Fin(alt, start);
    }

    private PatternNode ParsePatternConcat()
    {
        var start = Cur;
        var items = new List<PatternNode>();
        int prog123 = -1;
        while (!IsOp("|") && !IsOp(")") && !IsOp(",") && !(IsOp("-") && IsOpAt(1, "}")) && Cur.Type != TokenType.EOF && Progress(ref prog123))
            items.Add(ParsePatternQuantified());
        if (items.Count == 0) throw Fail("Expected pattern element");
        return items.Count == 1 ? items[0] : Fin(new PatternConcat { Items = items }, start);
    }

    private PatternNode ParsePatternQuantified()
    {
        var start = Cur;
        var p = ParsePatternPrimary();
        int min; int? max;
        if (IsOp("*")) { Next(); min = 0; max = null; }
        else if (IsOp("+")) { Next(); min = 1; max = null; }
        else if (IsOp("?")) { Next(); min = 0; max = 1; }
        else if (IsOp("{") && !IsOpAt(1, "-"))
        {
            Next();
            min = 0; max = null;
            bool hasMin = Cur.Type == TokenType.Number;
            if (hasMin) min = ParseSmallInt();
            if (AcceptOp(","))
            {
                if (Cur.Type == TokenType.Number) max = ParseSmallInt();
            }
            else max = min;
            ExpectOp("}");
        }
        else return p;
        var q = new PatternQuantified { Inner = p, Min = min, Max = max };
        if (IsOp("?")) { Next(); q.Reluctant = true; }
        return Fin(q, start);
    }

    private PatternNode ParsePatternPrimary()
    {
        var s = Cur;
        if (AcceptOp("^")) return Fin(new PatternAnchor { IsStart = true }, s);
        if (AcceptOp("$")) return Fin(new PatternAnchor(), s);
        if (AcceptOp("("))
        {
            var inner = ParsePatternAlternation();
            ExpectOp(")");
            return Fin(new PatternGroup { Inner = inner }, s);
        }
        if (IsKw("PERMUTE") && IsOpAt(1, "("))
        {
            Next(); Next();
            var pp = new PatternPermute();
            do pp.Items.Add(ParsePatternAlternation()); while (AcceptOp(","));
            ExpectOp(")");
            return Fin(pp, s);
        }
        if (IsOp("{") && IsOpAt(1, "-"))
        {
            Next(); Next();
            var inner = ParsePatternAlternation();
            ExpectOp("-"); ExpectOp("}");
            return Fin(new PatternExclusion { Inner = inner }, s);
        }
        return Fin(new PatternVariable { Name = ParseIdentifier() }, s);
    }

    // ---- XMLTABLE / JSON_TABLE / GRAPH_TABLE ----

    private TableReference ParseXmlTable()
    {
        var start = Cur;
        Next(); Next();
        var xt = new XmlTable();
        if (IsKw("XMLNAMESPACES"))
        {
            Next(); ExpectOp("(");
            do
            {
                var s = Cur;
                var ns = new XmlNamespace();
                if (AcceptKw("DEFAULT")) { ns.IsDefault = true; ns.Uri = ParsePrimary(); }
                else
                {
                    ns.Uri = ParsePrimary();
                    ExpectKw("AS");
                    ns.Alias = ParseIdentifier();
                }
                xt.Namespaces.Add(Fin(ns, s));
            } while (AcceptOp(","));
            ExpectOp(")");
            ExpectOp(",");
        }
        xt.XQuery = ParseExpr();
        if (IsKw("PASSING")) xt.Passing = (PassingClause)ParseFunctionClause();
        if (AcceptKw("COLUMNS"))
            do xt.Columns.Add(ParsePathColumn()); while (AcceptOp(","));
        ExpectOp(")");
        return Fin(xt, start);
    }

    private TableReference ParseJsonTable()
    {
        var start = Cur;
        Next(); Next();
        var jt = new JsonTable { Input = ParseExpr() };
        if (IsKw("FORMAT")) jt.InputFormat = (FormatClause)ParseFunctionClause();
        ExpectOp(",");
        jt.Path = ParseExpr();
        int prog124 = -1;
        while (!IsKw("COLUMNS") && Cur.Type != TokenType.EOF && Progress(ref prog124)) jt.Clauses.Add(ParseFunctionClause());
        ExpectKw("COLUMNS");
        ExpectOp("(");
        do jt.Columns.Add(ParsePathColumn()); while (AcceptOp(","));
        ExpectOp(")");
        ExpectOp(")");
        return Fin(jt, start);
    }

    private TableReference ParseGraphTable()
    {
        var start = Cur;
        Next(); Next();
        var gt = new GraphTable { Graph = ParseDottedName() };
        ExpectKw("MATCH");
        do gt.Match.Add(ParseGraphPathPattern()); while (AcceptOp(","));
        if (AcceptKw("WHERE")) gt.Where = ParseExpr();
        if (IsKw("ONE") && IsKwAt(1, "ROW"))
        {
            Next(); Next(); ExpectKw("PER");
            if (AcceptKw("MATCH")) gt.RowsPer = GraphRowsPer.Match;
            else
            {
                if (AcceptKw("VERTEX")) gt.RowsPer = GraphRowsPer.Vertex;
                else { ExpectKw("STEP"); gt.RowsPer = GraphRowsPer.Step; }
                ExpectOp("(");
                gt.RowsPerVariables = ParseIdentifierList();
                ExpectOp(")");
            }
        }
        ExpectKw("COLUMNS");
        ExpectOp("(");
        do gt.Columns.Add(ParseGraphColumn()); while (AcceptOp(","));
        ExpectOp(")");
        ExpectOp(")");
        return Fin(gt, start);
    }

    private GraphPathPattern ParseGraphPathPattern()
    {
        var s = Cur;
        var p = new GraphPathPattern();
        p.Elements.Add(ParseGraphVertexOrSubpattern());
        while (true)
        {
            if (IsOp("-") || IsOp("->") || IsOp("<"))
            {
                p.Elements.Add(ParseGraphEdgePattern());
                p.Elements.Add(ParseGraphVertexOrSubpattern());
            }
            else if (IsOp("(") && IsOpAt(1, "(")) p.Elements.Add(ParseGraphSubpattern());
            else break;
        }
        return Fin(p, s);
    }

    private GraphPatternElement ParseGraphVertexOrSubpattern() =>
        IsOp("(") && IsOpAt(1, "(") ? ParseGraphSubpattern() : ParseGraphVertexPattern();

    /// <summary>( path ) [quantifier]: a sub-path pattern (its first element is itself parenthesised).</summary>
    private GraphSubpatternElement ParseGraphSubpattern()
    {
        var s = Cur;
        ExpectOp("(");
        var sp = new GraphSubpatternElement { Path = ParseGraphPathPattern() };
        ExpectOp(")");
        // Oracle rejects a bare parenthesised sub-path (ORA-40951): it must repeat.
        sp.Quantifier = ParseGraphQuantifier() ?? throw Fail("A parenthesised GRAPH_TABLE path pattern needs a quantifier");
        return Fin(sp, s);
    }

    /// <summary>A variable name inside (...) or [...], i.e. an identifier that is not the start of IS or WHERE.</summary>
    private bool AtGraphVariable() =>
        (Cur.Type is TokenType.Identifier or TokenType.QuotedIdentifier) && !IsKw("IS") && !IsKw("WHERE");

    private GraphVertexPattern ParseGraphVertexPattern()
    {
        var s = Cur;
        ExpectOp("(");
        var v = new GraphVertexPattern();
        if (AtGraphVariable()) v.Variable = ParseIdentifier();
        v.Label = ParseGraphLabel();
        if (AcceptKw("WHERE")) v.Where = ParseExpr();
        ExpectOp(")");
        return Fin(v, s);
    }

    private GraphEdgePattern ParseGraphEdgePattern()
    {
        var s = Cur;
        var e = new GraphEdgePattern { Direction = GraphEdgeDirection.Any };
        if (IsOp("<")) { Next(); ExpectOp("-"); e.Direction = GraphEdgeDirection.Left; }
        else if (IsOp("->")) { Next(); e.Direction = GraphEdgeDirection.Right; return Fin(e, s); }
        else ExpectOp("-");
        if (AcceptOp("["))
        {
            e.Bracketed = true;
            if (AtGraphVariable()) e.Variable = ParseIdentifier();
            e.Label = ParseGraphLabel();
            if (AcceptKw("WHERE")) e.Where = ParseExpr();
            ExpectOp("]");
            if (e.Direction == GraphEdgeDirection.Left)
            {
                if (AcceptOp("->")) e.Direction = GraphEdgeDirection.LeftOrRight;
                else ExpectOp("-");
            }
            else if (AcceptOp("->")) e.Direction = GraphEdgeDirection.Right;
            else ExpectOp("-");
            e.Quantifier = ParseGraphQuantifier();
        }
        return Fin(e, s);
    }

    private GraphLabelExpression? ParseGraphLabel()
    {
        if (!AcceptKw("IS")) return null;
        var l = new GraphLabelExpression();
        do l.Labels.Add(ParseIdentifier()); while (AcceptOp("|"));
        return l;
    }

    /// <summary>
    /// Quantifier after an edge or sub-path. Oracle accepts {n}, {n,m} and {,m}; it rejects * and + (ORA-40985) and {n,} (ORA-40984).
    /// </summary>
    private GraphQuantifier? ParseGraphQuantifier()
    {
        var s = Cur;
        if (IsOp("*") || IsOp("+")) throw Fail($"Unsupported quantifier {Cur.Value} in GRAPH_TABLE (use {{n}}, {{n,m}} or {{,m}})");
        if (!AcceptOp("{")) return null;
        var q = new GraphQuantifier { Kind = GraphQuantifierKind.Exact };
        if (!IsOp(",")) q.Min = ParseExpr();
        if (AcceptOp(","))
        {
            q.Kind = GraphQuantifierKind.Range;
            if (IsOp("}")) throw Fail("A GRAPH_TABLE quantifier with no upper bound ({n,}) is not supported");
            q.Max = ParseExpr();
        }
        else if (q.Min == null) throw Fail($"Expected a quantifier bound but found {Describe(Cur)}");
        ExpectOp("}");
        return Fin(q, s);
    }

    private GraphColumn ParseGraphColumn()
    {
        var s = Cur;
        var c = new GraphColumn { Value = ParseExpr() };
        if (AcceptKw("AS")) c.Alias = ParseIdentifier();
        return Fin(c, s);
    }

    private bool AtPathColumnClause() =>
        AtBehaviorClause() ||
        (Cur.Type == TokenType.Identifier && Cur.Value is "FORMAT" or "TRUNCATE" or "WITH" or "WITHOUT" or "KEEP" or "OMIT" or "ASCII" or "PRETTY" or "STRICT");

    private PathColumn ParsePathColumn()
    {
        var s = Cur;
        var pc = new PathColumn();
        if (IsKw("NESTED"))
        {
            Next();
            AcceptKw("PATH");
            pc.Nested = true;
            pc.Path = ParseExpr();
            ExpectKw("COLUMNS");
            ExpectOp("(");
            do pc.Columns.Add(ParsePathColumn()); while (AcceptOp(","));
            ExpectOp(")");
            return Fin(pc, s);
        }
        pc.Name = ParseIdentifier();
        if (IsKw("FOR") && IsKwAt(1, "ORDINALITY")) { Next(); Next(); pc.ForOrdinality = true; return Fin(pc, s); }
        if (Cur.Type == TokenType.Identifier && !IsKw("PATH") && !IsKw("EXISTS") && !AtPathColumnClause())
            pc.Type = ParseDataType();
        int prog125 = -1;
        while (!IsOp(",") && !IsOp(")") && Cur.Type != TokenType.EOF && Progress(ref prog125))
        {
            if (IsKw("EXISTS")) { Next(); pc.Exists = true; }
            else if (IsKw("PATH")) { Next(); pc.Path = ParseExpr(); }
            else if (IsKw("DEFAULT"))
            {
                int save = _p;
                Next();
                var e = ParseExpr();
                if (IsKw("ON")) { _p = save; pc.Clauses.Add(ParseBehaviorClause()); }
                else pc.Default = e;
            }
            else pc.Clauses.Add(ParseFunctionClause());
        }
        return Fin(pc, s);
    }

    // ---- MODEL ----

    private void ParseCellOptions(ref NavMode? nav, ref ModelUniqueness? uniq)
    {
        int prog11 = -1;
        while (Progress(ref prog11))
        {
            if (IsKw("IGNORE") && IsKwAt(1, "NAV")) { Next(); Next(); nav = NavMode.IgnoreNav; }
            else if (IsKw("KEEP") && IsKwAt(1, "NAV")) { Next(); Next(); nav = NavMode.KeepNav; }
            else if (IsKw("UNIQUE") && IsKwAt(1, "DIMENSION")) { Next(); Next(); uniq = ModelUniqueness.UniqueDimension; }
            else if (IsKw("UNIQUE") && IsKwAt(1, "SINGLE")) { Next(); Next(); ExpectKw("REFERENCE"); uniq = ModelUniqueness.UniqueSingleReference; }
            else break;
        }
    }

    private ModelClause ParseModelClause()
    {
        var s = Next(); // MODEL
        var mc = new ModelClause();
        NavMode? nav = null; ModelUniqueness? uniq = null;
        ParseCellOptions(ref nav, ref uniq);
        mc.Nav = nav; mc.Uniqueness = uniq;
        if (AcceptKw("RETURN"))
        {
            mc.Return = AcceptKw("UPDATED") ? ModelReturn.UpdatedRows : (AcceptKw("ALL") ? ModelReturn.AllRows : throw Fail("Expected UPDATED or ALL"));
            ExpectKw("ROWS");
        }
        int prog126 = -1;
        while (IsKw("REFERENCE") && Progress(ref prog126))
        {
            var rs = Next();
            var rm = new ReferenceModel { Name = ParseIdentifier() };
            ExpectKw("ON"); ExpectOp("(");
            rm.Query = ParseSelectStatement();
            ExpectOp(")");
            ExpectKw("DIMENSION"); ExpectKw("BY");
            rm.DimensionBy = ParseModelItems();
            ExpectKw("MEASURES");
            rm.Measures = ParseModelItems();
            NavMode? rn = null; ModelUniqueness? ru = null;
            ParseCellOptions(ref rn, ref ru);
            rm.Nav = rn; rm.Uniqueness = ru;
            mc.References.Add(Fin(rm, rs));
        }
        if (AcceptKw("MAIN")) mc.MainName = ParseIdentifier();
        if (AcceptKw("PARTITION")) { ExpectKw("BY"); mc.PartitionBy = ParseModelItems(); }
        ExpectKw("DIMENSION"); ExpectKw("BY");
        mc.DimensionBy = ParseModelItems();
        ExpectKw("MEASURES");
        mc.Measures = ParseModelItems();
        NavMode? mn = null; ModelUniqueness? mu = null;
        ParseCellOptions(ref mn, ref mu);
        mc.MainNav = mn; mc.MainUniqueness = mu;
        ExpectKw("RULES");
        if (AcceptKw("UPDATE")) mc.RulesMode = RulesMode.Update;
        else if (AcceptKw("UPSERT")) mc.RulesMode = AcceptKw("ALL") ? RulesMode.UpsertAll : RulesMode.Upsert;
        if (AcceptKw("AUTOMATIC")) { ExpectKw("ORDER"); mc.RulesOrder = RulesOrder.Automatic; }
        else if (AcceptKw("SEQUENTIAL")) { ExpectKw("ORDER"); mc.RulesOrder = RulesOrder.Sequential; }
        if (AcceptKw("ITERATE"))
        {
            ExpectOp("("); mc.Iterate = ParseExpr(); ExpectOp(")");
            if (AcceptKw("UNTIL")) { ExpectOp("("); mc.IterateUntil = ParseExpr(); ExpectOp(")"); }
        }
        ExpectOp("(");
        do mc.Rules.Add(ParseModelRule()); while (AcceptOp(","));
        ExpectOp(")");
        return Fin(mc, s);
    }

    private List<ExpressionSelectItem> ParseModelItems()
    {
        var list = new List<ExpressionSelectItem>();
        ExpectOp("(");
        do list.Add(ParseExpressionSelectItem()); while (AcceptOp(","));
        ExpectOp(")");
        return list;
    }

    private ModelRule ParseModelRule()
    {
        var s = Cur;
        var r = new ModelRule();
        if (IsKw("UPDATE")) { Next(); r.Mode = RulesMode.Update; }
        else if (IsKw("UPSERT")) { Next(); r.Mode = AcceptKw("ALL") ? RulesMode.UpsertAll : RulesMode.Upsert; }
        r.Target = ParsePostfix();
        if (IsKw("ORDER") && IsKwAt(1, "BY"))
        {
            Next(); Next();
            do
            {
                var os = Cur;
                var item = new OrderByItem { Expression = ParseAdditive() };
                if (AcceptKw("DESC")) item.Descending = true; else AcceptKw("ASC");
                if (AcceptKw("NULLS")) { if (AcceptKw("FIRST")) item.Nulls = NullsOrdering.First; else { ExpectKw("LAST"); item.Nulls = NullsOrdering.Last; } }
                r.OrderBy.Add(Fin(item, os));
            } while (AcceptOp(","));
        }
        ExpectOp("=");
        r.Value = ParseExpr();
        return Fin(r, s);
    }

    private Expression ParseSubscripts(Expression target, Token start)
    {
        ExpectOp("[");
        var cr = new CellReference { Base = target };
        do
        {
            if (IsKw("FOR")) cr.Subscripts.Add(ParseForIteration());
            else
            {
                // JSON dot-notation array range: [0 to 2]
                var s = Cur;
                var sub = ParseExpr();
                cr.Subscripts.Add(AcceptKw("TO") ? Fin(new SubscriptRange { From = sub, To = ParseExpr() }, s) : sub);
            }
        } while (AcceptOp(","));
        ExpectOp("]");
        return Fin(cr, start);
    }

    private Expression ParseForIteration()
    {
        var s = Next(); // FOR
        var fi = new ForIterationSubscript();
        if (AcceptOp("(")) { fi.Dimensions = ParseIdentifierList(); ExpectOp(")"); }
        else fi.Dimensions.Add(ParseIdentifier());
        if (AcceptKw("IN"))
        {
            ExpectOp("(");
            if (LooksLikeQuery()) fi.InQuery = ParseSelectStatement();
            else do fi.InList.Add(ParseExpr()); while (AcceptOp(","));
            ExpectOp(")");
        }
        else
        {
            if (AcceptKw("LIKE")) fi.LikePattern = ParseAdditive();
            ExpectKw("FROM"); fi.From = ParseAdditive();
            ExpectKw("TO"); fi.To = ParseAdditive();
            if (AcceptKw("DECREMENT")) { fi.Decrement = true; fi.Step = ParseAdditive(); }
            else if (AcceptKw("INCREMENT")) fi.Step = ParseAdditive();
        }
        return Fin(fi, s);
    }

    // ---- multitable INSERT ----

    private Statement ParseMultiTableInsert(HintClause? hint, Token start)
    {
        var st = new MultiTableInsertStatement { Hint = hint };
        if (!AcceptKw("ALL") && AcceptKw("FIRST")) st.First = true;
        if (IsKw("WHEN"))
        {
            st.Conditional = true;
            int prog127 = -1;
            while (IsKw("WHEN") && Progress(ref prog127))
            {
                var bs = Next();
                var branch = new MultiInsertBranch { When = ParseExpr() };
                ExpectKw("THEN");
                branch.Targets = ParseInsertIntos();
                st.Branches.Add(Fin(branch, bs));
            }
            if (IsKw("ELSE"))
            {
                var es = Next();
                st.Else = Fin(new MultiInsertBranch { Targets = ParseInsertIntos() }, es);
            }
        }
        else
        {
            var bs = Cur;
            st.Branches.Add(Fin(new MultiInsertBranch { Targets = ParseInsertIntos() }, bs));
        }
        st.Query = ParseSelectStatement();
        return Fin(st, start);
    }

    private List<InsertIntoClause> ParseInsertIntos()
    {
        var list = new List<InsertIntoClause>();
        if (!IsKw("INTO")) throw Fail($"Expected INTO but found {Describe(Cur)}");
        int prog128 = -1;
        while (IsKw("INTO") && Progress(ref prog128))
        {
            var s = Next();
            var ic = new InsertIntoClause { Table = ParseTablePrimary() };
            if (IsOp("(")) { Next(); ic.Columns = ParseIdentifierList(); ExpectOp(")"); }
            if (AcceptKw("VALUES"))
            {
                ExpectOp("(");
                do ic.Values.Add(ParseExpr()); while (AcceptOp(","));
                ExpectOp(")");
            }
            list.Add(Fin(ic, s));
        }
        return list;
    }


    // ---- JSON constructors and duality view annotations ----

    /// <summary>JSON { 'k' : value, ... } or JSON {*}; the JSON keyword is the current token.</summary>
    private Expression ParseJsonObject()
    {
        var start = Next();
        ExpectOp("{");
        var o = new JsonObjectConstructor();
        if (AcceptOp("*")) o.Wildcard = true;
        else if (!IsOp("}")) do o.Members.Add(ParseJsonMember()); while (AcceptOp(","));
        ExpectOp("}");
        return Fin(o, start);
    }

    private JsonMember ParseJsonMember()
    {
        var s = Cur;
        var m = new JsonMember { Key = ParsePrimary() };
        if (Cur.Type == TokenType.Bind && Cur.Text.Length > 1 && char.IsDigit(Cur.Text[1]))
        {
            // 'key':1 with no space lexes ':1' as a bind; here it is a colon and a number (as in JSON_OBJECT)
            var bt = Next();
            m.Value = Fin(new Literal { Kind = LiteralKind.Number, Value = bt.Text[1..] }, bt);
        }
        else
        {
            if (Cur.Text != ":") throw Fail($"Expected ':' in a JSON member but found {Describe(Cur)}");
            Next();
            m.Value = IsOp("[") ? ParseJsonArray(keyword: false) : ParseExpr();
        }
        if (IsKw("ABSENT") && IsKwAt(1, "ON") && IsKwAt(2, "NULL")) { Next(); Next(); Next(); m.NullHandling = JsonNullHandling.AbsentOnNull; }
        else if (IsKw("NULL") && IsKwAt(1, "ON") && IsKwAt(2, "NULL")) { Next(); Next(); Next(); m.NullHandling = JsonNullHandling.NullOnNull; }
        if (AcceptKw("RETURNING")) m.Returning = ParseDataType();
        return Fin(m, s);
    }

    /// <summary>[ expr, ... ] or [ SELECT ... ]; keyword is true for the top-level JSON [ ... ] form.</summary>
    private Expression ParseJsonArray(bool keyword)
    {
        var start = Cur;
        if (keyword) Next();
        ExpectOp("[");
        var a = new JsonArrayConstructor();
        if (IsKw("SELECT") || IsKw("WITH")) a.Query = ParseSelectStatement();
        else if (!IsOp("]")) do a.Elements.Add(ParseExpr()); while (AcceptOp(","));
        ExpectOp("]");
        return Fin(a, start);
    }

    private bool AtDualityWord(int k) =>
        IsKwAt(k, "INSERT") || IsKwAt(k, "UPDATE") || IsKwAt(k, "DELETE") || IsKwAt(k, "NOINSERT") || IsKwAt(k, "NOUPDATE") ||
        IsKwAt(k, "NODELETE") || IsKwAt(k, "NOCHECK") || IsKwAt(k, "ETAG") || IsKwAt(k, "NOETAG") ||
        (IsKwAt(k, "NO") && (IsKwAt(k + 1, "CHECK") || IsKwAt(k + 1, "ETAG")));

    /// <summary>A WITH that opens duality annotations (rather than WITH CHECK OPTION or WITH READ ONLY).</summary>
    private bool AtDualityWithClause() => IsKw("WITH") && AtDualityWord(1);

    /// <summary>WITH word [word]... [WITH word...]: Oracle accepts repeated WITH groups (semantic checks reject conflicts).</summary>
    private void ParseDualityAnnotations(NamedTable nt)
    {
        int prog = -1;
        while (AtDualityWithClause() && Progress(ref prog))
        {
            Next();
            while (AtDualityWord(0))
            {
                var s = Cur;
                var kind = ParseDualityWord();
                nt.DualityAnnotations.Add(Fin(new DualityAnnotationItem { Kind = kind }, s));
            }
        }
    }

    private DualityAnnotation ParseDualityWord()
    {
        var w = Next().Value;
        if (w == "NO") return Next().Value == "CHECK" ? DualityAnnotation.NoCheck : DualityAnnotation.NoEtag;
        return w switch
        {
            "INSERT" => DualityAnnotation.Insert, "UPDATE" => DualityAnnotation.Update, "DELETE" => DualityAnnotation.Delete,
            "NOINSERT" => DualityAnnotation.NoInsert, "NOUPDATE" => DualityAnnotation.NoUpdate, "NODELETE" => DualityAnnotation.NoDelete,
            "NOCHECK" => DualityAnnotation.NoCheck, "ETAG" => DualityAnnotation.Etag, _ => DualityAnnotation.NoEtag,
        };
    }

    /// <summary>CREATE ... JSON [RELATIONAL] DUALITY VIEW name AS query; the header tokens are already consumed.</summary>
    private Statement ParseCreateDualityView(Token start, bool orReplace, bool relational)
    {
        var st = new CreateDualityViewStatement { OrReplace = orReplace, Relational = relational };
        st.Name = ParseDottedName();
        ExpectKw("AS");
        if (IsKw("SELECT") || IsKw("WITH") || IsOp("(")) st.Query = ParseSelectStatement();
        else st.OtherClauses = ParseSyntaxItems(AtStatementEnd);
        return Fin(st, start);
    }
}
