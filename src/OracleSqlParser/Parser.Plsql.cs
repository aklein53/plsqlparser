namespace OracleSqlParser;

public sealed partial class SqlParser
{
    // ---- blocks ----

    private bool AtBlockEnd() =>
        Cur.Type == TokenType.EOF ||
        (Cur.Type == TokenType.Identifier && Cur.Value is "END" or "ELSE" or "ELSIF" or "EXCEPTION" or "WHEN");

    private List<string> ParseLabels()
    {
        var labels = new List<string>();
        int prog132 = -1;
        while (IsOp("<<") && Progress(ref prog132))
        {
            Next();
            labels.Add(ParseIdentifier().Value);
            ExpectOp(">>");
        }
        return labels;
    }

    private Block ParseBlock()
    {
        Enter();
        var r = ParseBlockCore();
        Leave();
        return r;
    }

    private Block ParseBlockCore()
    {
        var start = Cur;
        var labels = ParseLabels();
        var b = new Block { Labels = labels };
        if (AcceptKw("DECLARE"))
        {
            b.HasDeclare = true;
            b.Declarations = ParseDeclarations(() => IsKw("BEGIN"));
        }
        ParseBlockStatements(b);
        return Fin(b, start);
    }

    /// <summary>Parses BEGIN ... [EXCEPTION ...] END [name] into the block.</summary>
    private void ParseBlockStatements(Block b)
    {
        ExpectKw("BEGIN");
        b.Statements = ParseStatements(AtBlockEnd);
        if (IsKw("EXCEPTION")) b.Handlers = ParseExceptionHandlers();
        ExpectKw("END");
        if (Cur.Type is TokenType.Identifier or TokenType.QuotedIdentifier) b.EndName = ParseIdentifier();
    }

    /// <summary>Body after IS/AS of a subprogram: declarations then BEGIN ... END.</summary>
    private Block ParseSubprogramBody()
    {
        var start = Cur;
        var b = new Block();
        b.Declarations = ParseDeclarations(() => IsKw("BEGIN"));
        ParseBlockStatements(b);
        return Fin(b, start);
    }

    private List<ExceptionHandler> ParseExceptionHandlers()
    {
        ExpectKw("EXCEPTION");
        var list = new List<ExceptionHandler>();
        int prog133 = -1;
        while (IsKw("WHEN") && Progress(ref prog133))
        {
            var s = Next();
            var h = new ExceptionHandler();
            if (AcceptKw("OTHERS")) h.Others = true;
            else
                do
                {
                    if (AcceptKw("OTHERS")) h.Others = true; else h.Exceptions.Add(ParseDottedName());
                } while (AcceptKw("OR"));
            ExpectKw("THEN");
            h.Statements = ParseStatements(AtBlockEnd);
            list.Add(Fin(h, s));
        }
        return list;
    }

    private List<Statement> ParseStatements(Func<bool> stop)
    {
        Enter();
        var r = ParseStatementsCore(stop);
        Leave();
        return r;
    }

    private List<Statement> ParseStatementsCore(Func<bool> stop)
    {
        var list = new List<Statement>();
        int prog30 = -1;
        while (!stop() && Progress(ref prog30))
        {
            list.Add(ParsePlsqlStatement());
            ExpectOp(";");
        }
        return list;
    }

    // ---- declarations ----

    private List<Declaration> ParseDeclarations(Func<bool> stop)
    {
        Enter();
        var r = ParseDeclarationsCore(stop);
        Leave();
        return r;
    }

    private List<Declaration> ParseDeclarationsCore(Func<bool> stop)
    {
        var list = new List<Declaration>();
        int prog134 = -1;
        while (!stop() && !IsKw("END") && Cur.Type != TokenType.EOF && Progress(ref prog134))
            list.Add(ParseDeclaration());
        return list;
    }

    private Declaration ParseDeclaration()
    {
        var start = Cur;
        if (Cur.Type != TokenType.Identifier && Cur.Type != TokenType.QuotedIdentifier)
            throw Fail($"Unexpected {Describe(Cur)} in declaration section");
        Declaration d;
        switch (Cur.Type == TokenType.Identifier ? Cur.Value : "")
        {
            case "CURSOR": d = ParseCursorDeclaration(); break;
            case "TYPE": d = ParseTypeDeclaration(); break;
            case "SUBTYPE": d = ParseSubtypeDeclaration(); break;
            case "PRAGMA":
            {
                Next();
                var p = new PragmaDeclaration { Name = ParseIdentifier() };
                if (AcceptOp("(")) { do p.Arguments.Add(ParseExpr()); while (AcceptOp(",")); ExpectOp(")"); }
                d = Fin(p, start);
                break;
            }
            case "PROCEDURE": case "FUNCTION":
                d = ParseSubprogram();
                break;
            default:
                if (IsKwAt(1, "EXCEPTION") && PeekTok().Type == TokenType.Identifier)
                {
                    var ed = new ExceptionDeclaration { Name = ParseIdentifier() };
                    Next();
                    d = Fin(ed, start);
                }
                else d = ParseVariableDeclaration(true);
                break;
        }
        ExpectOp(";");
        d.End = Prev.End;
        d.LastTokenIndex = Prev.TokenIndex;
        return d;
    }

    private VariableDeclaration ParseVariableDeclaration(bool allowConstant)
    {
        var start = Cur;
        var v = new VariableDeclaration { Name = ParseIdentifier() };
        if (allowConstant) v.Constant = AcceptKw("CONSTANT");
        v.Type = ParseDataType();
        if (IsKw("NOT") && IsKwAt(1, "NULL")) { Next(); Next(); v.NotNull = true; }
        if (AcceptOp(":=") || AcceptKw("DEFAULT")) v.Default = ParseExpr();
        return Fin(v, start);
    }

    private List<Parameter> ParseParameters()
    {
        var list = new List<Parameter>();
        ExpectOp("(");
        if (AcceptOp(")")) return list;
        do
        {
            var s = Cur;
            var p = new Parameter { Name = ParseIdentifier() };
            if (IsKw("IN") && IsKwAt(1, "OUT")) { Next(); Next(); p.Mode = ParamMode.InOut; }
            else if (IsKw("IN")) { Next(); p.Mode = ParamMode.In; }
            else if (IsKw("OUT")) { Next(); p.Mode = ParamMode.Out; }
            p.NoCopy = AcceptKw("NOCOPY");
            if (!IsOp(",") && !IsOp(")") && !IsKw("DEFAULT") && !IsOp(":=")) p.Type = ParseDataType();
            if (AcceptOp(":=") || AcceptKw("DEFAULT")) p.Default = ParseExpr();
            list.Add(Fin(p, s));
        } while (AcceptOp(","));
        ExpectOp(")");
        return list;
    }

    private Declaration ParseCursorDeclaration()
    {
        var start = Next(); // CURSOR
        var c = new CursorDeclaration { Name = ParseIdentifier() };
        if (IsOp("(")) c.Parameters = ParseParameters();
        if (AcceptKw("RETURN")) c.ReturnType = ParseDataType();
        if (AcceptKw("IS") || AcceptKw("AS")) c.Query = ParseSelectStatement();
        return Fin(c, start);
    }

    private Declaration ParseTypeDeclaration()
    {
        var start = Next(); // TYPE
        var name = ParseIdentifier();
        ExpectKw("IS");
        if (AcceptKw("RECORD"))
        {
            var r = new RecordTypeDeclaration { Name = name };
            ExpectOp("(");
            do r.Fields.Add(ParseVariableDeclaration(false)); while (AcceptOp(","));
            ExpectOp(")");
            return Fin(r, start);
        }
        if (IsKw("REF") && IsKwAt(1, "CURSOR"))
        {
            Next(); Next();
            var rc = new RefCursorTypeDeclaration { Name = name };
            if (AcceptKw("RETURN")) rc.ReturnType = ParseDataType();
            return Fin(rc, start);
        }
        var ct = new CollectionTypeDeclaration { Name = name };
        if (AcceptKw("TABLE")) ct.Kind = CollectionKind.Table;
        else if (AcceptKw("VARRAY")) ct.Kind = CollectionKind.Varray;
        else if (IsKw("VARYING")) { Next(); ExpectKw("ARRAY"); ct.Kind = CollectionKind.Varray; }
        else throw Fail($"Expected RECORD, TABLE, VARRAY or REF CURSOR but found {Describe(Cur)}");
        if (ct.Kind == CollectionKind.Varray) { ExpectOp("("); ct.Limit = ParseExpr(); ExpectOp(")"); }
        ExpectKw("OF");
        ct.ElementType = ParseDataType();
        if (IsKw("NOT") && IsKwAt(1, "NULL")) { Next(); Next(); ct.NotNull = true; }
        if (IsKw("INDEX")) { Next(); ExpectKw("BY"); ct.IndexBy = ParseDataType(); }
        return Fin(ct, start);
    }

    private Declaration ParseSubtypeDeclaration()
    {
        var start = Next(); // SUBTYPE
        var st = new SubtypeDeclaration { Name = ParseIdentifier() };
        ExpectKw("IS");
        st.BaseType = ParseDataType();
        if (AcceptKw("RANGE"))
        {
            st.RangeLow = ParseAdditive();
            ExpectOp("..");
            st.RangeHigh = ParseAdditive();
        }
        if (IsKw("NOT") && IsKwAt(1, "NULL")) { Next(); Next(); st.NotNull = true; }
        return Fin(st, start);
    }

    private MemberModifiers ParseMemberModifiers()
    {
        var m = MemberModifiers.None;
        int prog31 = -1;
        while (Cur.Type == TokenType.Identifier && Progress(ref prog31))
        {
            if (IsKw("NOT") && IsKwAt(1, "FINAL")) { Next(); Next(); m |= MemberModifiers.NotFinal; }
            else if (IsKw("NOT") && IsKwAt(1, "INSTANTIABLE")) { Next(); Next(); m |= MemberModifiers.NotInstantiable; }
            else if (IsKw("MEMBER")) { Next(); m |= MemberModifiers.Member; }
            else if (IsKw("STATIC")) { Next(); m |= MemberModifiers.Static; }
            else if (IsKw("CONSTRUCTOR")) { Next(); m |= MemberModifiers.Constructor; }
            else if (IsKw("MAP")) { Next(); m |= MemberModifiers.Map; }
            else if (IsKw("ORDER")) { Next(); m |= MemberModifiers.Order; }
            else if (IsKw("FINAL")) { Next(); m |= MemberModifiers.Final; }
            else if (IsKw("INSTANTIABLE")) { Next(); m |= MemberModifiers.Instantiable; }
            else if (IsKw("OVERRIDING")) { Next(); m |= MemberModifiers.Overriding; }
            else break;
        }
        return m;
    }

    private SubprogramDeclaration ParseSubprogram()
    {
        var start = Cur;
        var sp = new SubprogramDeclaration { Modifiers = ParseMemberModifiers() };
        if (IsKw("FUNCTION")) sp.IsFunction = true;
        else if (!IsKw("PROCEDURE")) throw Fail($"Expected FUNCTION or PROCEDURE but found {Describe(Cur)}");
        Next();
        sp.Name = ParseDottedName();
        if (IsOp("(")) sp.Parameters = ParseParameters();
        if (sp.IsFunction && AcceptKw("RETURN"))
        {
            if (IsKw("SELF") && IsKwAt(1, "AS") && IsKwAt(2, "RESULT")) { Next(); Next(); Next(); sp.ReturnSelfAsResult = true; }
            else sp.ReturnType = ParseDataType();
        }
        sp.Options = ParseProgramOptions(() => IsKw("IS") || IsKw("AS") || IsKw("LANGUAGE") || IsKw("EXTERNAL") || IsKw("MLE") || IsOp(";") || IsOp(","));
        if (IsKw("LANGUAGE") || IsKw("EXTERNAL") || IsKw("MLE")) sp.External = ParseExternalCall();
        else if (IsKw("IS") || IsKw("AS"))
        {
            Next();
            if (IsKw("LANGUAGE") || IsKw("EXTERNAL") || IsKw("MLE")) sp.External = ParseExternalCall();
            else sp.Body = ParseSubprogramBody();
        }
        return Fin(sp, start);
    }

    private ProgramOptions? ParseProgramOptions(Func<bool> stop)
    {
        var s = Cur;
        var po = new ProgramOptions();
        bool any = false;
        int prog32 = -1;
        while (Cur.Type == TokenType.Identifier && !stop() && Progress(ref prog32))
        {
            switch (Cur.Value)
            {
                case "AUTHID":
                    Next();
                    po.Authid = AcceptKw("CURRENT_USER") ? AuthidKind.CurrentUser : (AcceptKw("DEFINER") ? AuthidKind.Definer : throw Fail("Expected CURRENT_USER or DEFINER"));
                    break;
                case "SHARING" when IsOpAt(1, "="): po.Sharing = TryParseSharing(); break;
                case "DETERMINISTIC": Next(); po.Deterministic = true; break;
                case "PIPELINED":
                    Next(); po.Pipelined = true;
                    if (IsKw("ROW") || IsKw("TABLE")) { Next(); ExpectKw("POLYMORPHIC"); }
                    if (AcceptKw("USING")) po.PipelinedUsing = ParseDottedName();
                    break;
                case "PARALLEL_ENABLE": po.ParallelEnable = ParseParallelEnable(); break;
                case "RESULT_CACHE":
                    Next(); po.ResultCache = true;
                    if (AcceptKw("RELIES_ON"))
                    {
                        ExpectOp("(");
                        do po.RelyOn.Add(ParseDottedName()); while (AcceptOp(","));
                        ExpectOp(")");
                    }
                    break;
                case "AGGREGATE": Next(); ExpectKw("USING"); po.AggregateUsing = ParseDottedName(); break;
                case "SQL_MACRO" or "SQL_TABLE_MACRO":
                {
                    bool tableMacro = Next().Value == "SQL_TABLE_MACRO";
                    po.SqlMacro = tableMacro ? MacroKind.Table : MacroKind.Scalar;
                    if (IsOp("(") && IsKwAt(1, "TYPE"))
                    {
                        Next(); Next(); ExpectOp("=");
                        po.SqlMacro = AcceptKw("TABLE") ? MacroKind.Table : (AcceptKw("SCALAR") ? MacroKind.Scalar : throw Fail("Expected TABLE or SCALAR"));
                        ExpectOp(")");
                    }
                    break;
                }
                case "ACCESSIBLE":
                {
                    Next(); ExpectKw("BY");
                    ExpectOp("(");
                    do
                    {
                        var acs = Cur;
                        var ac = new Accessor();
                        if (IsKw("FUNCTION")) { Next(); ac.Kind = AccessorKind.Function; }
                        else if (IsKw("PROCEDURE")) { Next(); ac.Kind = AccessorKind.Procedure; }
                        else if (IsKw("PACKAGE")) { Next(); ac.Kind = AccessorKind.Package; }
                        else if (IsKw("TRIGGER")) { Next(); ac.Kind = AccessorKind.Trigger; }
                        else if (IsKw("TYPE")) { Next(); ac.Kind = AccessorKind.Type; }
                        ac.Name = ParseDottedName();
                        po.AccessibleBy.Add(Fin(ac, acs));
                    } while (AcceptOp(","));
                    ExpectOp(")");
                    break;
                }
                case "DEPRECATE":
                    Next(); po.Deprecate = true;
                    if (Cur.Type == TokenType.String) po.DeprecateMessage = ParsePrimary();
                    break;
                case "DEFAULT" when IsKwAt(1, "COLLATION"): Next(); Next(); po.DefaultCollation = ParseIdentifier(); break;
                default:
                    if (!any) return null;
                    throw Fail($"Unexpected {Describe(Cur)} in program options");
            }
            any = true;
        }
        return any ? Fin(po, s) : null;
    }

    private ParallelEnableClause ParseParallelEnable()
    {
        var s = Next(); // PARALLEL_ENABLE
        var pe = new ParallelEnableClause();
        if (!IsOp("(")) return Fin(pe, s);
        Next();
        ExpectKw("PARTITION");
        pe.PartitionArgument = ParseIdentifier();
        ExpectKw("BY");
        if (AcceptKw("ANY")) pe.Method = ParallelPartitionMethod.Any;
        else
        {
            if (AcceptKw("HASH")) pe.Method = ParallelPartitionMethod.Hash;
            else if (AcceptKw("RANGE")) pe.Method = ParallelPartitionMethod.Range;
            else { ExpectKw("VALUE"); pe.Method = ParallelPartitionMethod.Value; }
            ExpectOp("(");
            pe.MethodColumns = ParseIdentifierList();
            ExpectOp(")");
        }
        if (IsKw("ORDER") || IsKw("CLUSTER"))
        {
            if (Next().Value == "ORDER") pe.Order = true; else pe.Cluster = true;
            ParseIdentifierList(); // streaming expression column list (ORDER|CLUSTER expr BY (...)): argument names
            ExpectKw("BY");
            ExpectOp("(");
            pe.StreamingColumns = ParseIdentifierList();
            ExpectOp(")");
        }
        ExpectOp(")");
        return Fin(pe, s);
    }

    private ExternalCall ParseExternalCall()
    {
        var s = Cur;
        var ec = new ExternalCall();
        if (IsKw("MLE"))
        {
            ec.Language = ExternalLanguage.JavaScript;
            Next();
            if (AcceptKw("MODULE")) ec.MleModule = ParseDottedName();
            if (AcceptKw("ENV")) ec.MleEnv = ParseDottedName();
            if (AcceptKw("SIGNATURE")) ec.Signature = ParsePrimary();
            return Fin(ec, s);
        }
        if (AcceptKw("LANGUAGE"))
        {
            if (AcceptKw("JAVA")) ec.Language = ExternalLanguage.Java;
            else if (AcceptKw("JAVASCRIPT")) ec.Language = ExternalLanguage.JavaScript;
            else { ExpectKw("C"); ec.Language = ExternalLanguage.C; }
        }
        else { ExpectKw("EXTERNAL"); ec.Language = ExternalLanguage.C; }
        int prog33 = -1;
        while (!AtStatementEnd() && Progress(ref prog33))
        {
            if (AcceptKw("NAME")) ec.Name = ParseAdditive();
            else if (AcceptKw("LIBRARY")) ec.Library = ParseIdentifier();
            else if (IsKw("MLE")) { Next(); if (AcceptKw("MODULE")) ec.MleModule = ParseDottedName(); }
            else if (AcceptKw("ENV")) ec.MleEnv = ParseDottedName();
            else if (AcceptKw("SIGNATURE")) ec.Signature = ParsePrimary();
            else if (IsKw("MODULE")) { Next(); ec.MleModule = ParseDottedName(); }
            else if (IsKw("AGENT")) { Next(); ExpectKw("IN"); ExpectOp("("); ec.AgentIn = ParseIdentifierList(); ExpectOp(")"); }
            else if (IsKw("WITH") && IsKwAt(1, "CONTEXT")) { Next(); Next(); ec.WithContext = true; }
            else if (IsKw("CALLING")) { Next(); ExpectKw("STANDARD"); ExpectKw("PASCAL"); ec.Pascal = true; }
            else if (AcceptKw("PARAMETERS"))
            {
                ExpectOp("(");
                if (!IsOp(")")) do ec.Parameters.Add(ParseExternalParameter()); while (AcceptOp(","));
                ExpectOp(")");
            }
            else break;
        }
        return Fin(ec, s);
    }

    private ExternalProperty? TryParseExternalProperty()
    {
        if (IsKw("INDICATOR"))
        {
            Next();
            if (AcceptKw("STRUCT")) return ExternalProperty.IndicatorStruct;
            if (AcceptKw("TDO")) return ExternalProperty.IndicatorTdo;
            return ExternalProperty.Indicator;
        }
        if (AcceptKw("LENGTH")) return ExternalProperty.Length;
        if (AcceptKw("MAXLEN")) return ExternalProperty.MaxLen;
        if (AcceptKw("TDO")) return ExternalProperty.Tdo;
        if (AcceptKw("CHARSETID")) return ExternalProperty.CharsetId;
        if (AcceptKw("CHARSETFORM")) return ExternalProperty.CharsetForm;
        return null;
    }

    private ExternalParameter ParseExternalParameter()
    {
        var s = Cur;
        var ep = new ExternalParameter();
        if (AcceptKw("CONTEXT")) ep.Kind = ExternalParameterKind.Context;
        else if (AcceptKw("SELF")) { ep.Kind = ExternalParameterKind.Self; ep.Property = TryParseExternalProperty(); }
        else
        {
            if (AcceptKw("RETURN")) ep.Kind = ExternalParameterKind.Return;
            else { ep.Kind = ExternalParameterKind.Named; ep.Name = ParseIdentifier(); }
            ep.Property = TryParseExternalProperty();
            if (IsKw("BY")) { Next(); ExpectKw("REFERENCE"); ep.ByReference = true; }
            if (Cur.Type == TokenType.Identifier && !IsOp(",")) ep.Type = ParseDataType();
        }
        return Fin(ep, s);
    }

    // ---- program units ----

    private Statement ParseCreatePackage(Token start, bool orReplace, EditionableKind? editionable, bool body)
    {
        if (body)
        {
            var pb = new CreatePackageBodyStatement { OrReplace = orReplace, Editionable = editionable };
            TryIfNotExists();
            pb.Name = ParseDottedName();
            if (!AcceptKw("IS")) ExpectKw("AS");
            pb.Declarations = ParseDeclarations(() => IsKw("BEGIN"));
            if (AcceptKw("BEGIN"))
            {
                pb.InitStatements = ParseStatements(AtBlockEnd);
                if (IsKw("EXCEPTION")) pb.InitHandlers = ParseExceptionHandlers();
            }
            ExpectKw("END");
            if (Cur.Type is TokenType.Identifier or TokenType.QuotedIdentifier) pb.EndName = ParseIdentifier();
            return Fin(pb, start);
        }
        var ps = new CreatePackageStatement { OrReplace = orReplace, Editionable = editionable };
        TryIfNotExists();
        ps.Name = ParseDottedName();
        ps.Options = ParseProgramOptions(() => IsKw("IS") || IsKw("AS"));
        if (!AcceptKw("IS")) ExpectKw("AS");
        ps.Declarations = ParseDeclarations(() => false);
        ExpectKw("END");
        if (Cur.Type is TokenType.Identifier or TokenType.QuotedIdentifier) ps.EndName = ParseIdentifier();
        return Fin(ps, start);
    }

    private static readonly HashSet<string> DdlTriggerEvents = new(StringComparer.Ordinal)
        { "CREATE","ALTER","DROP","ANALYZE","ASSOCIATE","AUDIT","COMMENT","DDL","DISASSOCIATE","GRANT","NOAUDIT","RENAME","REVOKE","TRUNCATE" };

    private TriggerEvent ParseTriggerEvent()
    {
        var s = Cur;
        var ev = new TriggerEvent();
        if (AcceptKw("INSERT")) ev.Kind = TriggerEventKind.Insert;
        else if (AcceptKw("DELETE")) ev.Kind = TriggerEventKind.Delete;
        else if (AcceptKw("UPDATE"))
        {
            ev.Kind = TriggerEventKind.Update;
            if (AcceptKw("OF")) ev.Columns = ParseIdentifierList();
        }
        else if (Cur.Type == TokenType.Identifier)
        {
            ev.Kind = DdlTriggerEvents.Contains(Cur.Value) ? TriggerEventKind.Ddl : TriggerEventKind.Database;
            ev.EventName = Next().Value;
        }
        else throw Fail($"Expected trigger event but found {Describe(Cur)}");
        return Fin(ev, s);
    }

    private Statement ParseCreateTrigger(Token start, bool orReplace, EditionableKind? editionable)
    {
        var tr = new CreateTriggerStatement { OrReplace = orReplace, Editionable = editionable };
        TryIfNotExists();
        tr.Name = ParseDottedName();
        tr.Sharing = TryParseSharing();
        if (IsKw("INSTEAD")) { Next(); ExpectKw("OF"); tr.Timing = TriggerTiming.InsteadOf; }
        else if (AcceptKw("BEFORE")) tr.Timing = TriggerTiming.Before;
        else if (AcceptKw("AFTER")) tr.Timing = TriggerTiming.After;
        else if (AcceptKw("FOR")) tr.Timing = TriggerTiming.For;
        else throw Fail($"Expected BEFORE, AFTER, INSTEAD OF or FOR but found {Describe(Cur)}");
        do tr.Events.Add(ParseTriggerEvent()); while (AcceptKw("OR"));
        ExpectKw("ON");
        if (AcceptKw("DATABASE")) tr.TargetKind = TriggerTargetKind.Database;
        else if (IsKw("PLUGGABLE") && IsKwAt(1, "DATABASE")) { Next(); Next(); tr.TargetKind = TriggerTargetKind.PluggableDatabase; }
        else if (IsKw("NESTED") && IsKwAt(1, "TABLE"))
        {
            Next(); Next();
            tr.NestedTableColumn = ParseIdentifier();
            ExpectKw("OF");
            tr.TargetKind = TriggerTargetKind.NestedTable;
            tr.Target = ParseDottedName();
        }
        else
        {
            tr.Target = ParseDottedName();
            if (tr.Target[^1] is { Quoted: false, Value: "SCHEMA" }) { tr.Target.RemoveAt(tr.Target.Count - 1); tr.TargetKind = TriggerTargetKind.Schema; }
        }
        int prog34 = -1;
        while (Cur.Type == TokenType.Identifier && Progress(ref prog34))
        {
            if (IsKw("REFERENCING"))
            {
                Next();
                int prog135 = -1;
                while ((IsKw("NEW") || IsKw("OLD") || IsKw("PARENT")) && Progress(ref prog135))
                {
                    var rs = Cur;
                    var ri = new ReferencingItem { Kind = Next().Value switch { "NEW" => ReferencingKind.New, "OLD" => ReferencingKind.Old, _ => ReferencingKind.Parent } };
                    AcceptKw("AS");
                    ri.Alias = ParseIdentifier();
                    tr.Referencing.Add(Fin(ri, rs));
                }
            }
            else if (IsKw("FOR") && IsKwAt(1, "EACH")) { Next(); Next(); ExpectKw("ROW"); tr.ForEachRow = true; }
            else if (IsKw("FORWARD") || IsKw("REVERSE")) { tr.CrossEdition = Next().Value == "FORWARD" ? CrossEdition.Forward : CrossEdition.Reverse; ExpectKw("CROSSEDITION"); }
            else if (IsKw("FOLLOWS") || IsKw("PRECEDES"))
            {
                tr.Ordering = Next().Value == "FOLLOWS" ? TriggerOrdering.Follows : TriggerOrdering.Precedes;
                do tr.OrderingTriggers.Add(ParseDottedName()); while (AcceptOp(","));
            }
            else if (AcceptKw("ENABLE")) tr.Enabled = true;
            else if (AcceptKw("DISABLE")) tr.Enabled = false;
            else break;
        }
        if (AcceptKw("WHEN"))
        {
            ExpectOp("(");
            tr.When = ParseExpr();
            ExpectOp(")");
        }
        if (IsKw("COMPOUND"))
        {
            Next(); ExpectKw("TRIGGER");
            tr.Compound = true;
            tr.CompoundDeclarations = ParseDeclarations(() => IsKw("BEFORE") || IsKw("AFTER") || IsKw("INSTEAD"));
            int prog136 = -1;
            while ((IsKw("BEFORE") || IsKw("AFTER") || IsKw("INSTEAD")) && Progress(ref prog136))
            {
                var ss = Cur;
                var sec = new TimingPointSection { Point = ParseTimingPoint() };
                ExpectKw("IS");
                ExpectKw("BEGIN");
                sec.Statements = ParseStatements(AtBlockEnd);
                if (IsKw("EXCEPTION")) sec.Handlers = ParseExceptionHandlers();
                ExpectKw("END");
                ParseTimingPoint();
                ExpectOp(";");
                tr.Sections.Add(Fin(sec, ss));
            }
            ExpectKw("END");
            if (Cur.Type is TokenType.Identifier or TokenType.QuotedIdentifier) ParseIdentifier();
        }
        else if (IsKw("CALL")) tr.Call = (CallStatement)ParseCall();
        else tr.Body = ParseBlock();
        return Fin(tr, start);
    }

    private TimingPoint ParseTimingPoint()
    {
        if (AcceptKw("BEFORE")) return AcceptKw("STATEMENT") ? TimingPoint.BeforeStatement : ExpectEachRow(TimingPoint.BeforeEachRow);
        if (AcceptKw("AFTER")) return AcceptKw("STATEMENT") ? TimingPoint.AfterStatement : ExpectEachRow(TimingPoint.AfterEachRow);
        ExpectKw("INSTEAD"); ExpectKw("OF");
        return ExpectEachRow(TimingPoint.InsteadOfEachRow);
    }

    private TimingPoint ExpectEachRow(TimingPoint p) { ExpectKw("EACH"); ExpectKw("ROW"); return p; }

    private static readonly HashSet<string> TypeMemberStarts = new(StringComparer.Ordinal)
        { "MEMBER", "STATIC", "CONSTRUCTOR", "MAP", "ORDER", "FINAL", "NOT", "INSTANTIABLE", "OVERRIDING", "FUNCTION", "PROCEDURE" };

    private Statement ParseCreateType(Token start, bool orReplace, EditionableKind? editionable, bool body)
    {
        if (body)
        {
            var tb = new CreateTypeBodyStatement { OrReplace = orReplace };
            TryIfNotExists();
            tb.Name = ParseDottedName();
            if (!AcceptKw("IS")) ExpectKw("AS");
            int prog35 = -1;
            while (!IsKw("END") && Progress(ref prog35))
            {
                tb.Members.Add(ParseSubprogram());
                ExpectOp(";");
            }
            ExpectKw("END");
            return Fin(tb, start);
        }
        var ts = new CreateTypeStatement { OrReplace = orReplace, Editionable = editionable };
        TryIfNotExists();
        ts.Name = ParseDottedName();
        int prog36 = -1;
        while (Progress(ref prog36))
        {
            if (AcceptKw("FORCE")) ts.Force = true;
            else if (AcceptKw("OID")) ts.Oid = ParsePrimary();
            else break;
        }
        ts.Options = ParseProgramOptions(() => IsKw("IS") || IsKw("AS") || IsKw("UNDER") || IsKw("OID") || IsKw("FORCE"));
        if (AcceptKw("OID")) ts.Oid = ParsePrimary();
        if (AcceptKw("UNDER"))
        {
            ts.Kind = TypeKind.Under;
            ts.SuperType = ParseDottedName();
            ParseTypeMembers(ts);
        }
        else
        {
            if (!AcceptKw("IS")) ExpectKw("AS");
            if (AcceptKw("OBJECT"))
            {
                ts.Kind = TypeKind.Object;
                ParseTypeMembers(ts);
            }
            else if (AcceptKw("TABLE"))
            {
                ts.Kind = TypeKind.Table;
                ExpectKw("OF");
                ts.ElementType = ParseDataType();
            }
            else if (IsKw("VARRAY") || IsKw("VARYING"))
            {
                if (AcceptKw("VARYING")) ExpectKw("ARRAY"); else Next();
                ts.Kind = TypeKind.Varray;
                ExpectOp("("); ts.Limit = ParseExpr(); ExpectOp(")");
                ExpectKw("OF");
                ts.ElementType = ParseDataType();
            }
            else throw Fail($"Expected OBJECT, TABLE or VARRAY but found {Describe(Cur)}");
        }
        int prog37 = -1;
        while (!AtStatementEnd() && Progress(ref prog37))
        {
            bool not = AcceptKw("NOT");
            if (AcceptKw("FINAL")) ts.Final = !not;
            else if (AcceptKw("INSTANTIABLE")) ts.Instantiable = !not;
            else if (AcceptKw("PERSISTABLE")) ts.Persistable = !not;
            else throw Fail($"Unexpected {Describe(Cur)} in CREATE TYPE");
        }
        return Fin(ts, start);
    }

    private void ParseTypeMembers(CreateTypeStatement ts)
    {
        ExpectOp("(");
        do
        {
            if (Cur.Type == TokenType.Identifier && TypeMemberStarts.Contains(Cur.Value)) ts.Members.Add(ParseSubprogram());
            else ts.Attributes.Add(ParseVariableDeclaration(false));
        } while (AcceptOp(","));
        ExpectOp(")");
    }

    // ---- statements ----

    private Statement ParsePlsqlStatement()
    {
        var labels = ParseLabels();
        var start = Cur;
        Statement st = ParsePlsqlStatementCore(start);
        if (labels.Count > 0) st.Labels = labels;
        return st;
    }

    private Statement ParsePlsqlStatementCore(Token start)
    {
        if (Cur.Type == TokenType.Identifier)
        {
            switch (Cur.Value)
            {
                case "NULL" when IsOpAt(1, ";"): Next(); return Fin(new NullStatement(), start);
                case "IF": return ParseIf();
                case "CASE": return ParseCaseStatement();
                case "LOOP": case "WHILE": case "FOR": return ParseLoop();
                case "EXIT": case "CONTINUE": return ParseExitContinue();
                case "GOTO": Next(); return Fin(new GotoStatement { Target = ParseIdentifier() }, start);
                case "RETURN":
                {
                    Next();
                    var r = new ReturnStatement();
                    if (!IsOp(";")) r.Value = ParseExpr();
                    return Fin(r, start);
                }
                case "RAISE":
                {
                    Next();
                    var r = new RaiseStatement();
                    if (!IsOp(";")) r.Exception = ParseDottedName();
                    return Fin(r, start);
                }
                case "OPEN": return ParseOpen();
                case "FETCH": return ParseFetch();
                case "CLOSE": Next(); return Fin(new CloseStatement { Cursor = ParsePostfix() }, start);
                case "EXECUTE" when IsKwAt(1, "IMMEDIATE"): return ParseExecuteImmediate();
                case "FORALL": return ParseForall();
                case "PIPE" when IsKwAt(1, "ROW"):
                {
                    Next(); Next();
                    ExpectOp("(");
                    var pr = new PipeRowStatement { Value = ParseExpr() };
                    ExpectOp(")");
                    return Fin(pr, start);
                }
                case "DECLARE": case "BEGIN": return ParseBlock();
                case "SELECT": case "WITH": case "INSERT": case "UPDATE": case "DELETE": case "MERGE":
                case "COMMIT": case "ROLLBACK": case "SAVEPOINT": case "LOCK": case "CALL":
                    return ParseStatementCore();
                case "SET" when IsKwAt(1, "TRANSACTION"):
                    return ParseStatementCore();
            }
        }
        else if (IsOp("<<")) return ParseBlock();

        var target = ParsePostfix();
        if (AcceptOp(":=")) return Fin(new AssignmentStatement { Target = target, Value = ParseExpr() }, start);
        return Fin(new ProcedureCallStatement { Call = target }, start);
    }

    private Statement ParseIf()
    {
        var start = Next();
        var st = new IfStatement { Condition = ParseExpr() };
        ExpectKw("THEN");
        st.ThenStatements = ParseStatements(AtBlockEnd);
        int prog137 = -1;
        while (IsKw("ELSIF") && Progress(ref prog137))
        {
            var bs = Next();
            var br = new IfBranch { Condition = ParseExpr() };
            ExpectKw("THEN");
            br.Statements = ParseStatements(AtBlockEnd);
            st.ElsifBranches.Add(Fin(br, bs));
        }
        if (AcceptKw("ELSE")) st.ElseStatements = ParseStatements(AtBlockEnd);
        ExpectKw("END"); ExpectKw("IF");
        return Fin(st, start);
    }

    private Statement ParseCaseStatement()
    {
        var start = Next();
        var st = new PlsqlCaseStatement();
        if (!IsKw("WHEN")) st.Operand = ParseExpr();
        int prog138 = -1;
        while (IsKw("WHEN") && Progress(ref prog138))
        {
            var ws = Next();
            var w = new CaseStatementWhen { When = ParseExpr() };
            ExpectKw("THEN");
            w.Statements = ParseStatements(AtBlockEnd);
            st.Whens.Add(Fin(w, ws));
        }
        if (AcceptKw("ELSE")) st.ElseStatements = ParseStatements(AtBlockEnd);
        ExpectKw("END"); ExpectKw("CASE");
        if (Cur.Type is TokenType.Identifier or TokenType.QuotedIdentifier) ParseIdentifier();
        return Fin(st, start);
    }

    private Statement ParseLoop()
    {
        var start = Cur;
        var lp = new LoopStatement();
        if (AcceptKw("WHILE")) { lp.Kind = LoopKind.While; lp.Condition = ParseExpr(); }
        else if (AcceptKw("FOR"))
        {
            lp.Kind = LoopKind.For;
            do lp.Iterands.Add(ParseForIterand()); while (AcceptOp(","));
            ExpectKw("IN");
            do lp.Controls.Add(ParseIterationControl()); while (AcceptOp(","));
        }
        ExpectKw("LOOP");
        lp.Body = ParseStatements(AtBlockEnd);
        ExpectKw("END"); ExpectKw("LOOP");
        if (Cur.Type is TokenType.Identifier or TokenType.QuotedIdentifier) lp.EndLabel = ParseIdentifier();
        return Fin(lp, start);
    }

    /// <summary>FOR name [, name] [type] [MUTABLE|IMMUTABLE] IN ...: one iterand (two for PAIRS OF).</summary>
    private ForIterand ParseForIterand()
    {
        var start = Cur;
        var fi = new ForIterand { Name = ParseIdentifier() };
        if (!IsKw("IN") && !IsOp(",") && !IsKw("MUTABLE") && !IsKw("IMMUTABLE")) fi.Type = ParseDataType();
        if (AcceptKw("MUTABLE")) fi.Mutable = true;
        else AcceptKw("IMMUTABLE");
        return Fin(fi, start);
    }

    /// <summary>
    /// One FOR loop iteration control. A bare expression is always an ExpressionControl (a single value, or a
    /// cursor or collection name); only a parenthesised SELECT is a CursorControl. REVERSE applies to ranges only.
    /// </summary>
    private IterationControl ParseIterationControl()
    {
        var start = Cur;
        IterationControl c;
        if (IsKw("REPEAT")) { Next(); c = new RepeatControl { Value = ParseExpr() }; }
        else if (IsKw("VALUES") && IsKwAt(1, "OF")) { Next(); Next(); c = new ValuesOfControl { Collection = ParseExpr() }; }
        else if (IsKw("INDICES") && IsKwAt(1, "OF")) { Next(); Next(); c = new IndicesOfControl { Collection = ParseExpr() }; }
        else if (IsKw("PAIRS") && IsKwAt(1, "OF")) { Next(); Next(); c = new PairsOfControl { Collection = ParseExpr() }; }
        else if (IsKw("EXECUTE") && IsKwAt(1, "IMMEDIATE"))
        {
            Next(); Next();
            var dc = new DynamicSqlControl { Sql = ParseExpr() };
            if (AcceptKw("USING")) dc.Using = ParseUsingList();
            c = dc;
        }
        else if (IsOp("(") && LooksLikeQuery())
        {
            Next();
            var cc = new CursorControl { Query = ParseSelectStatement() };
            ExpectOp(")");
            c = cc;
        }
        else
        {
            bool reverse = AcceptKw("REVERSE");
            var e = ParseExpr();
            if (AcceptOp(".."))
            {
                var rc = new RangeControl { Reverse = reverse, Lower = e, Upper = ParseExpr() };
                if (AcceptKw("BY")) rc.By = ParseExpr();
                c = rc;
            }
            else
            {
                if (reverse) throw Fail("REVERSE must be followed by a range (lower..upper)");
                c = new ExpressionControl { Value = e };
            }
        }
        if (AcceptKw("WHILE")) c.While = ParseExpr();
        if (AcceptKw("WHEN")) c.When = ParseExpr();
        return Fin(c, start);
    }

    private Statement ParseExitContinue()
    {
        var start = Cur;
        bool isExit = Next().Value == "EXIT";
        Identifier? label = null;
        if (Cur.Type is TokenType.Identifier or TokenType.QuotedIdentifier && !IsKw("WHEN")) label = ParseIdentifier();
        Expression? when = AcceptKw("WHEN") ? ParseExpr() : null;
        return isExit
            ? Fin(new ExitStatement { Label = label, When = when }, start)
            : Fin(new ContinueStatement { Label = label, When = when }, start);
    }

    private List<UsingArgument> ParseUsingList()
    {
        var list = new List<UsingArgument>();
        do
        {
            var s = Cur;
            var ua = new UsingArgument();
            if (IsKw("IN") && IsKwAt(1, "OUT")) { Next(); Next(); ua.Mode = ParamMode.InOut; }
            else if (IsKw("IN")) { Next(); ua.Mode = ParamMode.In; }
            else if (IsKw("OUT")) { Next(); ua.Mode = ParamMode.Out; }
            ua.Value = ParseExpr();
            list.Add(Fin(ua, s));
        } while (AcceptOp(","));
        return list;
    }

    private Statement ParseOpen()
    {
        var start = Next();
        var st = new OpenStatement { Cursor = ParsePostfix() };
        if (AcceptKw("FOR"))
        {
            if (IsKw("SELECT") || IsKw("WITH") || (IsOp("(") && LooksLikeQuery())) st.ForQuery = ParseSelectStatement();
            else st.ForExpression = ParseExpr();
            if (AcceptKw("USING")) st.Using = ParseUsingList();
        }
        return Fin(st, start);
    }

    private Statement ParseFetch()
    {
        var start = Next();
        var st = new FetchStatement { Cursor = ParsePostfix() };
        if (IsKw("BULK") && IsKwAt(1, "COLLECT")) { Next(); Next(); st.BulkCollect = true; }
        ExpectKw("INTO");
        ParseIntoTargets(st.Into);
        if (AcceptKw("LIMIT")) st.Limit = ParseExpr();
        return Fin(st, start);
    }

    private Statement ParseExecuteImmediate()
    {
        var start = Next(); Next();
        var st = new ExecuteImmediateStatement { Sql = ParseExpr() };
        int prog38 = -1;
        while (Progress(ref prog38))
        {
            if (IsKw("BULK") && IsKwAt(1, "COLLECT")) { Next(); Next(); st.BulkCollect = true; ExpectKw("INTO"); ParseIntoTargets(st.Into); }
            else if (AcceptKw("INTO")) ParseIntoTargets(st.Into);
            else if (AcceptKw("USING")) st.Using = ParseUsingList();
            else if (IsKw("RETURNING") || IsKw("RETURN"))
            {
                Next();
                if (IsKw("BULK") && IsKwAt(1, "COLLECT")) { Next(); Next(); st.ReturningBulkCollect = true; }
                ExpectKw("INTO");
                ParseIntoTargets(st.ReturningInto);
            }
            else break;
        }
        return Fin(st, start);
    }

    private Statement ParseForall()
    {
        var start = Next();
        var st = new ForallStatement { Index = ParseIdentifier() };
        ExpectKw("IN");
        if (IsKw("INDICES") && IsKwAt(1, "OF"))
        {
            Next(); Next();
            st.IndicesOf = ParsePostfix();
            if (AcceptKw("BETWEEN")) { st.BetweenLow = ParseAdditive(); ExpectKw("AND"); st.BetweenHigh = ParseAdditive(); }
        }
        else if (IsKw("VALUES") && IsKwAt(1, "OF")) { Next(); Next(); st.ValuesOf = ParsePostfix(); }
        else
        {
            st.Lower = ParseExpr();
            ExpectOp("..");
            st.Upper = ParseExpr();
        }
        if (IsKw("SAVE") && IsKwAt(1, "EXCEPTIONS")) { Next(); Next(); st.SaveExceptions = true; }
        st.Body = ParsePlsqlStatement();
        return Fin(st, start);
    }
}
