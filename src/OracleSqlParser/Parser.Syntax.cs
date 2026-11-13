namespace OracleSqlParser;

public sealed partial class SqlParser
{
    // ---- token tree ----

    /// <summary>Consumes tokens until <paramref name="stop"/> holds at depth 0 (or an unbalanced ')' / EOF) as a token tree.</summary>
    private List<SyntaxItem> ParseSyntaxItems(Func<bool> stop)
    {
        var list = new List<SyntaxItem>();
        int prog12 = -1;
        while (Cur.Type != TokenType.EOF && !IsOp(")") && !stop() && Progress(ref prog12)) list.Add(ParseSyntaxItem());
        return list;
    }

    private SyntaxItem ParseSyntaxItem()
    {
        Enter();
        var r = ParseSyntaxItemCore();
        Leave();
        return r;
    }

    private SyntaxItem ParseSyntaxItemCore()
    {
        var s = Cur;
        switch (s.Type)
        {
            case TokenType.Identifier: Next(); return Fin(new WordItem { Value = s.Value }, s);
            case TokenType.QuotedIdentifier: Next(); return Fin(new WordItem { Value = s.Value, Quoted = true }, s);
            case TokenType.Number: Next(); return Fin(new LiteralItem { Kind = LiteralKind.Number, Value = s.Text }, s);
            case TokenType.String: Next(); return Fin(new LiteralItem { Kind = LiteralKind.String, Value = s.Value }, s);
            case TokenType.Bind: Next(); return Fin(new BindItem { Name = s.Text[1..] }, s);
            case TokenType.Substitution: Next(); return Fin(new BindItem { Name = s.Text }, s);
        }
        if (IsOp("("))
        {
            Next();
            var g = new GroupItem { Items = ParseSyntaxItems(() => false) };
            ExpectOp(")");
            return Fin(g, s);
        }
        Next();
        return Fin(new SymbolItem { Symbol = s.Text }, s);
    }

    private bool OnSameLine(Token t) => Cur.Type != TokenType.EOF && Cur.Line == t.Line;

    // ---- hints ----

    private HintClause ParseHint(Token tok)
    {
        var h = new HintClause { Text = tok.Value, Start = tok.Start, End = tok.End, Line = tok.Line, Column = tok.Column };
        List<Token> toks;
        try { toks = Lexer.Tokenize(tok.Value); }
        catch (ParseException) { return h; }
        var sub = new SqlParser(toks, tok.Value, new ParseOptions { MaxNestingDepth = _maxDepth });
        while (sub.Cur.Type != TokenType.EOF)
        {
            if (sub.Cur.Type == TokenType.Identifier)
            {
                var item = new HintItem { Name = sub.Next().Value, Start = tok.Start, End = tok.End };
                if (sub.IsOp("(")) item.Arguments = ((GroupItem)sub.ParseSyntaxItem()).Items;
                h.Items.Add(item);
            }
            else sub.Next();
        }
        return h;
    }

    // ---- physical attributes ----

    private static readonly HashSet<string> AttributeStarts = new(StringComparer.Ordinal)
    {
        "TABLESPACE","PCTFREE","PCTUSED","INITRANS","MAXTRANS","STORAGE","LOGGING","NOLOGGING","COMPRESS","NOCOMPRESS",
        "PARALLEL","NOPARALLEL","CACHE","NOCACHE","ENABLE","DISABLE","ORGANIZATION","PARTITION",
    };

    /// <summary>Parses physical/storage/partitioning attributes until <paramref name="stop"/>.</summary>
    private List<PhysicalAttribute> ParsePhysicalAttributes(Func<bool> stop)
    {
        var list = new List<PhysicalAttribute>();
        int prog13 = -1;
        while (Cur.Type != TokenType.EOF && !IsOp(")") && !IsOp(";") && !stop() && Progress(ref prog13))
        {
            int before = _p;
            list.Add(ParsePhysicalAttribute(stop));
            if (_p == before) throw Fail($"Unexpected {Describe(Cur)}");
        }
        return list;
    }

    private PhysicalAttribute ParsePhysicalAttribute(Func<bool> stop)
    {
        var s = Cur;
        if (Cur.Type == TokenType.Identifier)
        {
            switch (Cur.Value)
            {
                case "TABLESPACE":
                {
                    Next();
                    var ta = new TablespaceAttribute { Default = AcceptKw("DEFAULT") };
                    ta.Name = ParseIdentifier();
                    return Fin(ta, s);
                }
                case "PCTFREE" or "PCTUSED" or "INITRANS" or "MAXTRANS" or "PCTTHRESHOLD":
                {
                    var kind = Next().Value switch
                    {
                        "PCTFREE" => NumericAttributeKind.PctFree, "PCTUSED" => NumericAttributeKind.PctUsed,
                        "INITRANS" => NumericAttributeKind.InitTrans, "MAXTRANS" => NumericAttributeKind.MaxTrans,
                        _ => NumericAttributeKind.PctThreshold,
                    };
                    return Fin(new NumericAttribute { Kind = kind, Value = ParseAdditive() }, s);
                }
                case "STORAGE" when IsOpAt(1, "("):
                {
                    Next(); Next();
                    var sa = new StorageAttribute();
                    int prog129 = -1;
                    while (!IsOp(")") && Cur.Type != TokenType.EOF && Progress(ref prog129))
                    {
                        var os = Cur;
                        var opt = new StorageOption { Name = ParseIdentifier().Value };
                        if (!IsOp(")") && Cur.Type is TokenType.Number or TokenType.Identifier && !(Cur.Type == TokenType.Identifier && IsStorageWord(Cur.Value)))
                            opt.Value = ParseAdditive();
                        sa.Options.Add(Fin(opt, os));
                    }
                    ExpectOp(")");
                    return Fin(sa, s);
                }
                case "LOGGING": Next(); return Fin(new LoggingAttribute { Logging = true }, s);
                case "NOLOGGING": Next(); return Fin(new LoggingAttribute(), s);
                case "CACHE" when !IsOpAt(1, "("): Next(); return Fin(new CacheAttribute { Cache = true }, s);
                case "NOCACHE": Next(); return Fin(new CacheAttribute(), s);
                case "NOPARALLEL": Next(); return Fin(new ParallelAttribute { NoParallel = true }, s);
                case "PARALLEL":
                {
                    Next();
                    var pa = new ParallelAttribute();
                    if (Cur.Type == TokenType.Number) pa.Degree = ParseAdditive();
                    return Fin(pa, s);
                }
                case "NOCOMPRESS": Next(); return Fin(new CompressAttribute(), s);
                case "COMPRESS":
                {
                    Next();
                    var ca = new CompressAttribute { Compress = true };
                    if (Cur.Type == TokenType.Number) ca.PrefixLength = ParseAdditive();
                    else if (IsKw("BASIC") || IsKw("ADVANCED") || IsKw("FOR"))
                        ca.Method = ParseSyntaxItems(() => !(IsKw("BASIC") || IsKw("ADVANCED") || IsKw("FOR") || IsKw("OLTP") || IsKw("QUERY") ||
                                                           IsKw("ARCHIVE") || IsKw("LOW") || IsKw("HIGH") || IsKw("ALL") || IsKw("DIRECT_LOAD") || IsKw("OPERATIONS")));
                    return Fin(ca, s);
                }
                case "ENABLE" or "DISABLE" when IsKwAt(1, "ROW") && IsKwAt(2, "MOVEMENT"):
                {
                    bool en = Next().Value == "ENABLE";
                    Next(); Next();
                    return Fin(new RowMovementAttribute { Enabled = en }, s);
                }
                case "ORGANIZATION":
                {
                    Next();
                    var oa = new OrganizationAttribute();
                    if (AcceptKw("HEAP")) oa.Organization = TableOrganization.Heap;
                    else if (AcceptKw("INDEX")) oa.Organization = TableOrganization.Index;
                    else { ExpectKw("EXTERNAL"); oa.Organization = TableOrganization.External; }
                    if (oa.Organization == TableOrganization.External && IsOp("(")) oa.Details = [ParseSyntaxItem()];
                    return Fin(oa, s);
                }
                case "PARTITION" when IsKwAt(1, "BY"): return ParsePartitionBy();
            }
        }
        // anything else: one token-tree attribute up to the next recognized attribute start
        bool first = true;
        var items = ParseSyntaxItems(() =>
        {
            if (first) { first = false; return false; }
            return Cur.Type == TokenType.Identifier && AttributeStarts.Contains(Cur.Value) && !(Cur.Value == "ENABLE" || Cur.Value == "DISABLE") || stop();
        });
        return Fin(new OtherAttribute { Items = items }, s);
    }

    private static bool IsStorageWord(string w) =>
        w is "INITIAL" or "NEXT" or "MINEXTENTS" or "MAXEXTENTS" or "PCTINCREASE" or "FREELISTS" or "FREELIST" or "OPTIMAL" or
            "BUFFER_POOL" or "FLASH_CACHE" or "CELL_FLASH_CACHE" or "ENCRYPT" or "GROUPS";

    private PhysicalAttribute ParsePartitionBy()
    {
        var s = Cur;
        Next(); Next(); // PARTITION BY
        var pb = new PartitionByAttribute();
        pb.Method = ParsePartitionMethod(pb.Columns, out var refCon);
        pb.ReferenceConstraint = refCon;
        if (IsKw("INTERVAL") && IsOpAt(1, "(")) { Next(); Next(); pb.Interval = ParseExpr(); ExpectOp(")"); }
        if (IsKw("SUBPARTITION") && IsKwAt(1, "BY"))
        {
            Next(); Next();
            pb.SubpartitionMethod = ParsePartitionMethod(pb.SubpartitionColumns, out _);
        }
        if (IsKw("PARTITIONS")) { Next(); pb.PartitionCount = ParseAdditive(); }
        if (IsOp("("))
        {
            Next();
            do pb.Partitions.Add(ParsePartitionDefinition()); while (AcceptOp(","));
            ExpectOp(")");
        }
        return Fin(pb, s);
    }

    private PartitionMethod ParsePartitionMethod(List<Identifier> cols, out Identifier? refConstraint)
    {
        refConstraint = null;
        PartitionMethod m;
        if (AcceptKw("RANGE")) m = PartitionMethod.Range;
        else if (AcceptKw("LIST")) m = PartitionMethod.List;
        else if (AcceptKw("HASH")) m = PartitionMethod.Hash;
        else if (AcceptKw("SYSTEM")) return PartitionMethod.System;
        else if (AcceptKw("REFERENCE")) { ExpectOp("("); refConstraint = ParseIdentifier(); ExpectOp(")"); return PartitionMethod.Reference; }
        else throw Fail($"Expected RANGE, LIST, HASH, REFERENCE or SYSTEM but found {Describe(Cur)}");
        ExpectOp("(");
        cols.AddRange(ParseIdentifierList());
        ExpectOp(")");
        return m;
    }

    private PartitionDefinition ParsePartitionDefinition()
    {
        var s = Cur;
        if (!(IsKw("PARTITION") || IsKw("SUBPARTITION"))) throw Fail($"Expected PARTITION but found {Describe(Cur)}");
        Next();
        var pd = new PartitionDefinition();
        if (Cur.Type is TokenType.Identifier or TokenType.QuotedIdentifier && !IsKw("VALUES")) pd.Name = ParseIdentifier();
        if (AcceptKw("VALUES"))
        {
            if (AcceptKw("LESS")) { ExpectKw("THAN"); pd.LessThan = true; }
            ExpectOp("(");
            if (IsKw("DEFAULT") && IsOpAt(1, ")")) { Next(); pd.ValuesDefault = true; }
            else do pd.Values.Add(ParseExpr()); while (AcceptOp(","));
            ExpectOp(")");
        }
        pd.Attributes = ParsePhysicalAttributes(() => IsOp(",") || IsOp("("));
        if (IsOp("("))
        {
            Next();
            do pd.Subpartitions.Add(ParsePartitionDefinition()); while (AcceptOp(","));
            ExpectOp(")");
        }
        return Fin(pd, s);
    }
}
