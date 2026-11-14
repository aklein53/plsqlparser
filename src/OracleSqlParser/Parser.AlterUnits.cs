namespace OracleSqlParser;

public sealed partial class SqlParser
{
    // ---- ALTER of compiled units, triggers and views ----

    /// <summary>Kinds whose ALTER forms (COMPILE, EDITIONABLE, trigger/view clauses) have typed nodes. MATERIALIZED VIEW is handled separately.</summary>
    private static bool IsAlterUnitKind(ObjectKind k) => k is ObjectKind.Procedure or ObjectKind.Function or ObjectKind.Package
        or ObjectKind.PackageBody or ObjectKind.Type or ObjectKind.TypeBody or ObjectKind.Trigger or ObjectKind.Library
        or ObjectKind.JavaSource or ObjectKind.JavaClass or ObjectKind.JavaResource or ObjectKind.View or ObjectKind.Synonym;

    /// <summary>Returns the typed node for the clause after the name, or null when the clause is not modeled (the caller then rewinds).</summary>
    private Statement? ParseAlterUnit(Token start, ObjectKind kind, List<Identifier> name)
    {
        if (kind == ObjectKind.Trigger && (IsKw("ENABLE") || IsKw("DISABLE") || IsKw("RENAME"))) return ParseAlterTrigger(start, name);
        if (kind == ObjectKind.View && (IsKw("ADD") || IsKw("MODIFY") || IsKw("DROP") || IsKw("READ"))) return ParseAlterView(start, name);
        if (IsKw("COMPILE") || IsKw("EDITIONABLE") || IsKw("NONEDITIONABLE")) return ParseAlterCompile(start, kind, name);
        return null;
    }

    private Statement ParseAlterCompile(Token start, ObjectKind kind, List<Identifier> name)
    {
        var st = new AlterCompileStatement { Kind = kind, Name = name };
        if (AcceptKw("EDITIONABLE")) st.Editionable = EditionableKind.Editionable;
        else if (AcceptKw("NONEDITIONABLE")) st.Editionable = EditionableKind.NonEditionable;
        else
        {
            ExpectKw("COMPILE");
            st.Compile = true;
            st.Debug = AcceptKw("DEBUG");
            if (AcceptKw("SPECIFICATION")) st.Part = CompilePart.Specification;
            else if (AcceptKw("BODY")) st.Part = CompilePart.Body;
            else if (AcceptKw("PACKAGE")) st.Part = CompilePart.Package;
            if (IsKw("REUSE") && IsKwAt(1, "SETTINGS")) { Next(); Next(); st.ReuseSettings = true; }
            int prog = -1;
            while (!AtStatementEnd() && Progress(ref prog))
            {
                var s = Cur;
                var p = new SessionSetting { Name = ParseDottedName() };
                ExpectOp("=");
                p.Value = ParseExpr();
                st.Parameters.Add(Fin(p, s));
            }
        }
        return Fin(st, start);
    }

    private Statement ParseAlterTrigger(Token start, List<Identifier> name)
    {
        var st = new AlterTriggerStatement { Name = name };
        if (AcceptKw("ENABLE")) st.Enable = true;
        else if (AcceptKw("DISABLE")) st.Enable = false;
        else { ExpectKw("RENAME"); ExpectKw("TO"); st.RenameTo = ParseIdentifier(); }
        return Fin(st, start);
    }

    private Statement ParseAlterView(Token start, List<Identifier> name)
    {
        var st = new AlterViewStatement { Name = name };
        int prog = -1;
        while (!AtStatementEnd() && Progress(ref prog))
        {
            var s = Cur;
            if (AcceptKw("READ"))
            {
                if (AcceptKw("ONLY")) st.ReadOnly = true;
                else { ExpectKw("WRITE"); st.ReadOnly = false; }
                continue;
            }
            var a = new AlterTableAction();
            if (AcceptKw("ADD"))
            {
                a.Kind = AlterTableActionKind.AddConstraint;
                a.Constraints.Add(ParseConstraint(false));
            }
            else if (AcceptKw("MODIFY"))
            {
                ExpectKw("CONSTRAINT");
                a.Kind = AlterTableActionKind.ModifyConstraint;
                var tc = new TableConstraint { Name = ParseIdentifier() };
                tc.State = ParseConstraintState();
                a.Constraints.Add(Fin(tc, s));
            }
            else
            {
                ExpectKw("DROP");
                if (AcceptKw("CONSTRAINT")) { a.Kind = AlterTableActionKind.DropConstraint; a.Names.Add(ParseIdentifier()); }
                else if (AcceptKw("PRIMARY")) { ExpectKw("KEY"); a.Kind = AlterTableActionKind.DropPrimaryKey; }
                else
                {
                    ExpectKw("UNIQUE"); ExpectOp("(");
                    a.Kind = AlterTableActionKind.DropUnique;
                    a.Names = ParseIdentifierList();
                    ExpectOp(")");
                }
            }
            st.Actions.Add(Fin(a, s));
        }
        return Fin(st, start);
    }
}
