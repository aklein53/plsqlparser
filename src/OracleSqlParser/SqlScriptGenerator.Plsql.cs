namespace OracleSqlParser;

public sealed partial class SqlScriptGenerator
{
    private sealed partial class Printer
    {
        /// <summary>Separator before the next PL/SQL element: a new indented line in MultilinePlsql mode, else a space.</summary>
        private void PlLine() { if (_o.MultilinePlsql) NewLine(); else Sp(); }

        /// <summary>Statements one level deeper, each followed by ';' (conditional-compilation directives end with $END instead).</summary>
        private void StatementList(IEnumerable<Statement> stmts)
        {
            _indent++;
            foreach (var s in stmts) { PlLine(); Statement(s); if (!IsDirective(s)) Add(";"); }
            _indent--;
        }

        private void DeclList(IEnumerable<Declaration> decls)
        {
            _indent++;
            foreach (var d in decls) { PlLine(); Declaration(d); if (!IsDirective(d)) Add(";"); }
            _indent--;
        }

        private static bool IsDirective(SqlNode n) => n is ConditionalStatement or CompilationErrorStatement or ConditionalDeclaration or CompilationErrorDeclaration;

        // ---------- blocks ----------

        /// <summary>[DECLARE decls] BEGIN statements [EXCEPTION handlers] END [name]; with <paramref name="afterIs"/> the intro (IS) is already printed.</summary>
        private void BlockBody(Block b, bool afterIs)
        {
            bool intro = afterIs;
            if (!afterIs && (b.HasDeclare || b.Declarations.Count > 0)) { Add("DECLARE"); intro = true; }
            DeclList(b.Declarations);
            if (intro) PlLine();
            Add("BEGIN");
            StatementList(b.Statements);
            if (b.Handlers.Count > 0) ExceptionPart(b.Handlers);
            PlLine();
            Add("END");
            if (b.EndName != null) { Sp(); Id(b.EndName); }
        }

        private void ExceptionPart(List<ExceptionHandler> handlers)
        {
            PlLine();
            Add("EXCEPTION");
            _indent++;
            foreach (var h in handlers) { PlLine(); Handler(h); }
            _indent--;
        }

        private void Handler(ExceptionHandler h)
        {
            Add("WHEN ");
            Sep(h.Exceptions, Name, " OR ");
            if (h.Others) Add(h.Exceptions.Count > 0 ? " OR OTHERS" : "OTHERS");
            Add(" THEN");
            StatementList(h.Statements);
        }

        // ---------- declarations ----------

        private void VarDecl(VariableDeclaration v)
        {
            Id(v.Name);
            if (v.Constant) Add(" CONSTANT");
            Sp(); Type(v.Type);
            if (v.NotNull) Add(" NOT NULL");
            if (v.Default != null) { Add(" := "); Expr(v.Default); }
        }

        private void Param(Parameter p)
        {
            Id(p.Name);
            if (p.Mode != null) Add(p.Mode == ParamMode.In ? " IN" : p.Mode == ParamMode.Out ? " OUT" : " IN OUT");
            if (p.NoCopy) Add(" NOCOPY");
            if (p.Type != null) { Sp(); Type(p.Type); }
            if (p.Default != null) { Add(" DEFAULT "); Expr(p.Default); }
        }

        private void ParamList(List<Parameter> ps)
        {
            if (ps.Count > 0) { Add(" ("); Sep(ps, Param); Add(")"); }
        }

        private void Declaration(Declaration d)
        {
            switch (d)
            {
                case VariableDeclaration v: VarDecl(v); break;
                case ExceptionDeclaration e: Id(e.Name); Add(" EXCEPTION"); break;
                case ConditionalDeclaration cd:
                    Add("$IF "); Expr(cd.Condition); Add(" $THEN");
                    DeclList(cd.ThenDeclarations);
                    foreach (var b in cd.ElsifBranches)
                    {
                        PlLine(); Add("$ELSIF "); Expr(b.Condition); Add(" $THEN");
                        DeclList(b.Declarations);
                    }
                    if (cd.ElseDeclarations != null) { PlLine(); Add("$ELSE"); DeclList(cd.ElseDeclarations); }
                    PlLine(); Add("$END");
                    break;
                case CompilationErrorDeclaration ced: Add("$ERROR "); Expr(ced.Message); Add(" $END"); break;
                case PragmaDeclaration p:
                    Add("PRAGMA "); Id(p.Name);
                    if (p.Arguments.Count > 0) { Add("("); Sep(p.Arguments, a => Expr(a)); Add(")"); }
                    break;
                case CursorDeclaration c:
                    Add("CURSOR "); Id(c.Name);
                    ParamList(c.Parameters);
                    if (c.ReturnType != null) { Add(" RETURN "); Type(c.ReturnType); }
                    if (c.Query != null) { Add(" IS "); Select(c.Query); }
                    break;
                case RecordTypeDeclaration r:
                    Add("TYPE "); Id(r.Name); Add(" IS RECORD (");
                    Sep(r.Fields, VarDecl);
                    Add(")");
                    break;
                case CollectionTypeDeclaration ct:
                    Add("TYPE "); Id(ct.Name);
                    if (ct.Kind == CollectionKind.Table) Add(" IS TABLE OF ");
                    else { Add(" IS VARRAY ("); Expr(ct.Limit!); Add(") OF "); }
                    Type(ct.ElementType);
                    if (ct.NotNull) Add(" NOT NULL");
                    if (ct.IndexBy != null) { Add(" INDEX BY "); Type(ct.IndexBy); }
                    break;
                case RefCursorTypeDeclaration rc:
                    Add("TYPE "); Id(rc.Name); Add(" IS REF CURSOR");
                    if (rc.ReturnType != null) { Add(" RETURN "); Type(rc.ReturnType); }
                    break;
                case SubtypeDeclaration st:
                    Add("SUBTYPE "); Id(st.Name); Add(" IS "); Type(st.BaseType);
                    if (st.RangeLow != null) { Add(" RANGE "); Expr(st.RangeLow); Add(" .. "); Expr(st.RangeHigh!); }
                    if (st.NotNull) Add(" NOT NULL");
                    break;
                case SubprogramDeclaration sp: Subprogram(sp, false); break;
                default: throw Unsupported(d);
            }
        }

        private void Modifiers(MemberModifiers m)
        {
            void F(MemberModifiers f, string text) { if ((m & f) != 0) Add(text + " "); }
            F(MemberModifiers.Overriding, "OVERRIDING");
            F(MemberModifiers.Final, "FINAL");
            F(MemberModifiers.NotFinal, "NOT FINAL");
            F(MemberModifiers.Instantiable, "INSTANTIABLE");
            F(MemberModifiers.NotInstantiable, "NOT INSTANTIABLE");
            F(MemberModifiers.Map, "MAP");
            F(MemberModifiers.Order, "ORDER");
            F(MemberModifiers.Member, "MEMBER");
            F(MemberModifiers.Static, "STATIC");
            F(MemberModifiers.Constructor, "CONSTRUCTOR");
        }

        private void Subprogram(SubprogramDeclaration sp, bool typeSpec)
        {
            Modifiers(sp.Modifiers);
            Add(sp.IsFunction ? "FUNCTION " : "PROCEDURE ");
            Name(sp.Name);
            ParamList(sp.Parameters);
            if (sp.ReturnSelfAsResult) Add(" RETURN SELF AS RESULT");
            else if (sp.ReturnType != null) { Add(" RETURN "); Type(sp.ReturnType); }
            if (sp.Options != null) { Sp(); ProgramOpts(sp.Options); }
            if (sp.External != null) { Add(typeSpec ? " " : " AS "); ExternalCallClause(sp.External); }
            else if (sp.Body != null) { Add(" IS"); BlockBody(sp.Body, true); }
        }

        private void ProgramOpts(ProgramOptions po)
        {
            var parts = new List<Action>();
            if (po.Authid != null) parts.Add(() => Add(po.Authid == AuthidKind.CurrentUser ? "AUTHID CURRENT_USER" : "AUTHID DEFINER"));
            if (po.Sharing != null) parts.Add(() => Add(SharingText(po.Sharing.Value)));
            if (po.DefaultCollation != null) parts.Add(() => { Add("DEFAULT COLLATION "); Id(po.DefaultCollation); });
            if (po.AccessibleBy.Count > 0)
                parts.Add(() =>
                {
                    Add("ACCESSIBLE BY (");
                    Sep(po.AccessibleBy, a =>
                    {
                        if (a.Kind != null) Add(a.Kind.ToString()!.ToUpperInvariant() + " ");
                        Name(a.Name);
                    });
                    Add(")");
                });
            if (po.Deprecate) parts.Add(() => { Add("DEPRECATE"); if (po.DeprecateMessage != null) { Sp(); Expr(po.DeprecateMessage); } });
            if (po.Deterministic) parts.Add(() => Add("DETERMINISTIC"));
            if (po.Pipelined) parts.Add(() => { Add("PIPELINED"); if (po.PipelinedUsing.Count > 0) { Add(" USING "); Name(po.PipelinedUsing); } });
            if (po.ParallelEnable != null) parts.Add(() => ParallelEnable(po.ParallelEnable));
            if (po.ResultCache)
                parts.Add(() =>
                {
                    Add("RESULT_CACHE");
                    if (po.RelyOn.Count > 0) { Add(" RELIES_ON ("); Sep(po.RelyOn, Name); Add(")"); }
                });
            if (po.AggregateUsing.Count > 0) parts.Add(() => { Add("AGGREGATE USING "); Name(po.AggregateUsing); });
            if (po.SqlMacro != null) parts.Add(() => Add(po.SqlMacro == MacroKind.Table ? "SQL_TABLE_MACRO" : "SQL_MACRO"));
            bool first = true;
            foreach (var p in parts) { if (!first) Sp(); first = false; p(); }
        }

        private void ParallelEnable(ParallelEnableClause pe)
        {
            Add("PARALLEL_ENABLE");
            if (pe.PartitionArgument == null) return;
            Add(" (PARTITION "); Id(pe.PartitionArgument); Add(" BY ");
            switch (pe.Method)
            {
                case ParallelPartitionMethod.Any: Add("ANY"); break;
                case ParallelPartitionMethod.Hash: Add("HASH ("); IdList(pe.MethodColumns); Add(")"); break;
                case ParallelPartitionMethod.Range: Add("RANGE ("); IdList(pe.MethodColumns); Add(")"); break;
                default: Add("VALUE ("); IdList(pe.MethodColumns); Add(")"); break;
            }
            if (pe.Order || pe.Cluster)
            {
                Add(pe.Order ? " ORDER " : " CLUSTER "); Id(pe.PartitionArgument); Add(" BY ("); IdList(pe.StreamingColumns); Add(")");
            }
            Add(")");
        }

        private void ExternalCallClause(ExternalCall ec)
        {
            Add("LANGUAGE " + ec.Language switch { ExternalLanguage.Java => "JAVA", ExternalLanguage.C => "C", _ => "JAVASCRIPT" });
            if (ec.Name != null) { Add(" NAME "); Expr(ec.Name); }
            if (ec.Library != null) { Add(" LIBRARY "); Id(ec.Library); }
            if (ec.MleModule.Count > 0) { Add(" MODULE "); Name(ec.MleModule); }
            if (ec.MleEnv.Count > 0) { Add(" ENV "); Name(ec.MleEnv); }
            if (ec.Signature != null) { Add(" SIGNATURE "); Expr(ec.Signature); }
            if (ec.AgentIn.Count > 0) { Add(" AGENT IN ("); IdList(ec.AgentIn); Add(")"); }
            if (ec.Pascal) Add(" CALLING STANDARD PASCAL");
            if (ec.WithContext) Add(" WITH CONTEXT");
            if (ec.Parameters.Count > 0) { Add(" PARAMETERS ("); Sep(ec.Parameters, ExternalParam); Add(")"); }
        }

        private void ExternalParam(ExternalParameter p)
        {
            switch (p.Kind)
            {
                case ExternalParameterKind.Context: Add("CONTEXT"); return;
                case ExternalParameterKind.Self: Add("SELF"); break;
                case ExternalParameterKind.Return: Add("RETURN"); break;
                default: Id(p.Name!); break;
            }
            if (p.Property != null)
                Add(" " + p.Property switch
                {
                    ExternalProperty.Indicator => "INDICATOR", ExternalProperty.IndicatorStruct => "INDICATOR STRUCT", ExternalProperty.IndicatorTdo => "INDICATOR TDO",
                    ExternalProperty.Length => "LENGTH", ExternalProperty.MaxLen => "MAXLEN", ExternalProperty.Tdo => "TDO",
                    ExternalProperty.CharsetId => "CHARSETID", _ => "CHARSETFORM",
                });
            if (p.ByReference) Add(" BY REFERENCE");
            if (p.Type != null) { Sp(); Type(p.Type); }
        }

        // ---------- program units ----------

        private void Plsql(Statement st)
        {
            switch (st)
            {
                case Block b: BlockBody(b, false); break;
                case CreateProgramStatement cp:
                    Add("CREATE");
                    if (cp.OrReplace) Add(" OR REPLACE");
                    Editionable(cp.Editionable);
                    Sp(); Subprogram(cp.Subprogram, false);
                    break;
                case CreatePackageStatement pk:
                    Add("CREATE");
                    if (pk.OrReplace) Add(" OR REPLACE");
                    Editionable(pk.Editionable);
                    Add(" PACKAGE "); Name(pk.Name);
                    if (pk.Options != null) { Sp(); ProgramOpts(pk.Options); }
                    Add(" IS");
                    DeclList(pk.Declarations);
                    PlLine(); Add("END");
                    if (pk.EndName != null) { Sp(); Id(pk.EndName); }
                    break;
                case CreatePackageBodyStatement pb:
                    Add("CREATE");
                    if (pb.OrReplace) Add(" OR REPLACE");
                    Editionable(pb.Editionable);
                    Add(" PACKAGE BODY "); Name(pb.Name); Add(" IS");
                    DeclList(pb.Declarations);
                    if (pb.InitStatements.Count > 0 || pb.InitHandlers.Count > 0)
                    {
                        PlLine(); Add("BEGIN");
                        StatementList(pb.InitStatements);
                        if (pb.InitHandlers.Count > 0) ExceptionPart(pb.InitHandlers);
                    }
                    PlLine(); Add("END");
                    if (pb.EndName != null) { Sp(); Id(pb.EndName); }
                    break;
                case CreateTriggerStatement tr: Trigger(tr); break;
                case CreateTypeStatement ty: CreateType(ty); break;
                case CreateTypeBodyStatement tb:
                    Add("CREATE");
                    if (tb.OrReplace) Add(" OR REPLACE");
                    Add(" TYPE BODY "); Name(tb.Name); Add(" IS");
                    _indent++;
                    foreach (var m in tb.Members) { PlLine(); Subprogram(m, true); Add(";"); }
                    _indent--;
                    PlLine(); Add("END");
                    break;
                default: PlsqlStatement(st); break;
            }
        }

        private void TriggerEv(TriggerEvent e)
        {
            switch (e.Kind)
            {
                case TriggerEventKind.Insert: Add("INSERT"); break;
                case TriggerEventKind.Delete: Add("DELETE"); break;
                case TriggerEventKind.Update: Add("UPDATE"); if (e.Columns.Count > 0) { Add(" OF "); IdList(e.Columns); } break;
                default: Add(e.EventName ?? ""); break;
            }
        }

        private static string TimingPointText(TimingPoint p) => p switch
        {
            TimingPoint.BeforeStatement => "BEFORE STATEMENT", TimingPoint.BeforeEachRow => "BEFORE EACH ROW",
            TimingPoint.AfterStatement => "AFTER STATEMENT", TimingPoint.AfterEachRow => "AFTER EACH ROW", _ => "INSTEAD OF EACH ROW",
        };

        private void Trigger(CreateTriggerStatement t)
        {
            Add("CREATE");
            if (t.OrReplace) Add(" OR REPLACE");
            Editionable(t.Editionable);
            Add(" TRIGGER "); Name(t.Name);
            SharingClause(t.Sharing);
            Add(t.Timing switch { TriggerTiming.Before => " BEFORE ", TriggerTiming.After => " AFTER ", TriggerTiming.InsteadOf => " INSTEAD OF ", _ => " FOR " });
            Sep(t.Events, TriggerEv, " OR ");
            Add(" ON ");
            switch (t.TargetKind)
            {
                case TriggerTargetKind.Database: Add("DATABASE"); break;
                case TriggerTargetKind.PluggableDatabase: Add("PLUGGABLE DATABASE"); break;
                case TriggerTargetKind.Schema:
                    if (t.Target.Count > 0) { Name(t.Target); Add("."); }
                    Add("SCHEMA");
                    break;
                case TriggerTargetKind.NestedTable: Add("NESTED TABLE "); Id(t.NestedTableColumn!); Add(" OF "); Name(t.Target); break;
                default: Name(t.Target); break;
            }
            if (t.Referencing.Count > 0)
            {
                Add(" REFERENCING");
                foreach (var r in t.Referencing)
                {
                    Add(r.Kind switch { ReferencingKind.New => " NEW AS ", ReferencingKind.Old => " OLD AS ", _ => " PARENT AS " });
                    Id(r.Alias);
                }
            }
            if (t.ForEachRow) Add(" FOR EACH ROW");
            if (t.CrossEdition != null) Add(t.CrossEdition == CrossEdition.Forward ? " FORWARD CROSSEDITION" : " REVERSE CROSSEDITION");
            if (t.Ordering != null) { Add(t.Ordering == TriggerOrdering.Follows ? " FOLLOWS " : " PRECEDES "); Sep(t.OrderingTriggers, Name); }
            if (t.Enabled != null) Add(t.Enabled == true ? " ENABLE" : " DISABLE");
            if (t.When != null) { Add(" WHEN ("); Expr(t.When); Add(")"); }
            if (t.Compound)
            {
                PlLine(); Add("COMPOUND TRIGGER");
                DeclList(t.CompoundDeclarations);
                _indent++;
                foreach (var s in t.Sections)
                {
                    var text = TimingPointText(s.Point);
                    PlLine(); Add(text + " IS");
                    PlLine(); Add("BEGIN");
                    StatementList(s.Statements);
                    if (s.Handlers.Count > 0) ExceptionPart(s.Handlers);
                    PlLine(); Add("END " + text + ";");
                }
                _indent--;
                PlLine(); Add("END");
            }
            else if (t.Call != null) { Sp(); Statement(t.Call); }
            else if (t.Body != null) { PlLine(); BlockBody(t.Body, false); }
        }

        private void CreateType(CreateTypeStatement ty)
        {
            Add("CREATE");
            if (ty.OrReplace) Add(" OR REPLACE");
            Editionable(ty.Editionable);
            Add(" TYPE "); Name(ty.Name);
            if (ty.Force) Add(" FORCE");
            if (ty.Oid != null) { Add(" OID "); Expr(ty.Oid); }
            if (ty.Options != null) { Sp(); ProgramOpts(ty.Options); }
            void Members()
            {
                Add(" (");
                if (_o.MultilinePlsql)
                {
                    _indent++;
                    bool first = true;
                    foreach (var a in ty.Attributes) { if (!first) Add(","); first = false; NewLine(); VarDecl(a); }
                    foreach (var m in ty.Members) { if (!first) Add(","); first = false; NewLine(); Subprogram(m, true); }
                    _indent--;
                    NewLine();
                }
                else
                {
                    Sep(ty.Attributes, VarDecl);
                    if (ty.Attributes.Count > 0 && ty.Members.Count > 0) Add(", ");
                    Sep(ty.Members, m => Subprogram(m, true));
                }
                Add(")");
            }
            switch (ty.Kind)
            {
                case TypeKind.Under: Add(" UNDER "); Name(ty.SuperType); Members(); break;
                case TypeKind.Object: Add(" AS OBJECT"); Members(); break;
                case TypeKind.Table: Add(" AS TABLE OF "); Type(ty.ElementType!); break;
                default: Add(" AS VARRAY ("); Expr(ty.Limit!); Add(") OF "); Type(ty.ElementType!); break;
            }
            if (ty.Final != null) Add(ty.Final == true ? " FINAL" : " NOT FINAL");
            if (ty.Instantiable != null) Add(ty.Instantiable == true ? " INSTANTIABLE" : " NOT INSTANTIABLE");
            if (ty.Persistable != null) Add(ty.Persistable == true ? " PERSISTABLE" : " NOT PERSISTABLE");
        }

        // ---------- PL/SQL statements ----------

        private void UsingList(List<UsingArgument> list)
        {
            Add(" USING ");
            Sep(list, u =>
            {
                if (u.Mode != null) Add(u.Mode == ParamMode.In ? "IN " : u.Mode == ParamMode.Out ? "OUT " : "IN OUT ");
                Expr(u.Value);
            });
        }

        private void PlsqlStatement(Statement st)
        {
            switch (st)
            {
                case NullStatement: Add("NULL"); break;
                case AssignmentStatement a: Expr(a.Target); Add(" := "); Expr(a.Value); break;
                case ProcedureCallStatement pc: Expr(pc.Call); break;
                case ConditionalStatement cs:
                    Add("$IF "); Expr(cs.Condition); Add(" $THEN");
                    StatementList(cs.ThenStatements);
                    foreach (var b in cs.ElsifBranches)
                    {
                        PlLine(); Add("$ELSIF "); Expr(b.Condition); Add(" $THEN");
                        StatementList(b.Statements);
                    }
                    if (cs.ElseStatements != null) { PlLine(); Add("$ELSE"); StatementList(cs.ElseStatements); }
                    PlLine(); Add("$END");
                    break;
                case CompilationErrorStatement ce: Add("$ERROR "); Expr(ce.Message); Add(" $END"); break;
                case IfStatement i:
                    Add("IF "); Expr(i.Condition); Add(" THEN");
                    StatementList(i.ThenStatements);
                    foreach (var b in i.ElsifBranches)
                    {
                        PlLine(); Add("ELSIF "); Expr(b.Condition); Add(" THEN");
                        StatementList(b.Statements);
                    }
                    if (i.ElseStatements != null) { PlLine(); Add("ELSE"); StatementList(i.ElseStatements); }
                    PlLine(); Add("END IF");
                    break;
                case PlsqlCaseStatement c:
                    Add("CASE");
                    if (c.Operand != null) { Sp(); Expr(c.Operand); }
                    _indent++;
                    foreach (var w in c.Whens)
                    {
                        PlLine(); Add("WHEN "); Expr(w.When); Add(" THEN");
                        StatementList(w.Statements);
                    }
                    if (c.ElseStatements != null) { PlLine(); Add("ELSE"); StatementList(c.ElseStatements); }
                    _indent--;
                    PlLine(); Add("END CASE");
                    break;
                case LoopStatement l: Loop(l); break;
                case ExitStatement e:
                    Add("EXIT");
                    if (e.Label != null) { Sp(); Id(e.Label); }
                    if (e.When != null) { Add(" WHEN "); Expr(e.When); }
                    break;
                case ContinueStatement e:
                    Add("CONTINUE");
                    if (e.Label != null) { Sp(); Id(e.Label); }
                    if (e.When != null) { Add(" WHEN "); Expr(e.When); }
                    break;
                case GotoStatement g: Add("GOTO "); Id(g.Target); break;
                case ReturnStatement r: Add("RETURN"); if (r.Value != null) { Sp(); Expr(r.Value); } break;
                case RaiseStatement r: Add("RAISE"); if (r.Exception.Count > 0) { Sp(); Name(r.Exception); } break;
                case OpenStatement o:
                    Add("OPEN "); Expr(o.Cursor);
                    if (o.ForQuery != null) { Add(" FOR "); Select(o.ForQuery); }
                    else if (o.ForExpression != null) { Add(" FOR "); Expr(o.ForExpression); }
                    if (o.Using.Count > 0) UsingList(o.Using);
                    break;
                case FetchStatement f:
                    Add("FETCH "); Expr(f.Cursor);
                    Add(f.BulkCollect ? " BULK COLLECT INTO " : " INTO ");
                    Sep(f.Into, x => Expr(x));
                    if (f.Limit != null) { Add(" LIMIT "); Expr(f.Limit); }
                    break;
                case CloseStatement cl: Add("CLOSE "); Expr(cl.Cursor); break;
                case ExecuteImmediateStatement ei:
                    Add("EXECUTE IMMEDIATE "); Expr(ei.Sql);
                    if (ei.Into.Count > 0) { Add(ei.BulkCollect ? " BULK COLLECT INTO " : " INTO "); Sep(ei.Into, x => Expr(x)); }
                    if (ei.Using.Count > 0) UsingList(ei.Using);
                    if (ei.ReturningInto.Count > 0)
                    {
                        Add(ei.ReturningBulkCollect ? " RETURNING BULK COLLECT INTO " : " RETURNING INTO ");
                        Sep(ei.ReturningInto, x => Expr(x));
                    }
                    break;
                case ForallStatement fa:
                    Add("FORALL "); Id(fa.Index); Add(" IN ");
                    if (fa.IndicesOf != null)
                    {
                        Add("INDICES OF "); Expr(fa.IndicesOf);
                        if (fa.BetweenLow != null) { Add(" BETWEEN "); Expr(fa.BetweenLow); Add(" AND "); Expr(fa.BetweenHigh!); }
                    }
                    else if (fa.ValuesOf != null) { Add("VALUES OF "); Expr(fa.ValuesOf); }
                    else { Expr(fa.Lower!); Add(" .. "); Expr(fa.Upper!); }
                    if (fa.SaveExceptions) Add(" SAVE EXCEPTIONS");
                    Sp(); Statement(fa.Body);
                    break;
                case PipeRowStatement pr: Add("PIPE ROW ("); Expr(pr.Value); Add(")"); break;
                default: throw Unsupported(st);
            }
        }

        private void Iterand(ForIterand fi)
        {
            Id(fi.Name);
            if (fi.Type != null) { Add(" "); Type(fi.Type); }
            if (fi.Mutable) Add(" MUTABLE");
        }

        private void Control(IterationControl c)
        {
            switch (c)
            {
                case RangeControl r:
                    if (r.Reverse) Add("REVERSE ");
                    Expr(r.Lower); Add(" .. "); Expr(r.Upper);
                    if (r.By != null) { Add(" BY "); Expr(r.By); }
                    break;
                case ExpressionControl e: Expr(e.Value); break;
                case RepeatControl rp: Add("REPEAT "); Expr(rp.Value); break;
                case ValuesOfControl v: Add("VALUES OF "); Expr(v.Collection); break;
                case IndicesOfControl ix: Add("INDICES OF "); Expr(ix.Collection); break;
                case PairsOfControl p: Add("PAIRS OF "); Expr(p.Collection); break;
                case CursorControl cc: ParenQuery(cc.Query); break;
                case DynamicSqlControl d:
                    Add("EXECUTE IMMEDIATE "); Expr(d.Sql);
                    if (d.Using.Count > 0) UsingList(d.Using);
                    break;
                default: throw Unsupported(c);
            }
            if (c.While != null) { Add(" WHILE "); Expr(c.While); }
            if (c.When != null) { Add(" WHEN "); Expr(c.When); }
        }

        private void Loop(LoopStatement l)
        {
            switch (l.Kind)
            {
                case LoopKind.While: Add("WHILE "); Expr(l.Condition!); Add(" "); break;
                case LoopKind.For:
                    Add("FOR ");
                    Sep(l.Iterands, Iterand);
                    Add(" IN ");
                    Sep(l.Controls, Control);
                    Add(" ");
                    break;
            }
            Add("LOOP");
            StatementList(l.Body);
            PlLine(); Add("END LOOP");
            if (l.EndLabel != null) { Sp(); Id(l.EndLabel); }
        }
    }
}
