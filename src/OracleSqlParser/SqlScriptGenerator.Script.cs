namespace OracleSqlParser;

public sealed partial class SqlScriptGenerator
{
    private sealed partial class Printer
    {
        // ---------- script ----------

        private static bool IsPlsqlUnit(Statement st) =>
            st is Block or CreateProgramStatement or CreatePackageStatement or CreatePackageBodyStatement or CreateTriggerStatement
                or CreateTypeStatement or CreateTypeBodyStatement;

        private void Script(SqlScript sc)
        {
            bool first = true;
            foreach (var st in sc.Statements)
            {
                if (!first) Add("\n");
                first = false;
                Statement(st);
                if (st is SqlPlusCommand) continue;
                if (st is CreateJavaStatement { SourceText: not null })
                {
                    // the Java source runs to a '/' line: required even without SqlPlusSlashAfterPlsql
                    Add("\n/");
                    continue;
                }
                if (IsPlsqlUnit(st))
                {
                    if (_o.IncludeSemicolons || _o.SqlPlusSlashAfterPlsql) Add(";");
                    if (_o.SqlPlusSlashAfterPlsql) Add("\n/");
                    continue;
                }
                if (_o.IncludeSemicolons) Add(";");
            }
        }

        // ---------- transaction control, session, admin ----------

        private void AdminLike(Statement st)
        {
            switch (st)
            {
                case CommitStatement c:
                    Add("COMMIT");
                    if (c.Work) Add(" WORK");
                    if (c.Comment != null) { Add(" COMMENT "); Expr(c.Comment); }
                    if (c.Force != null) { Add(" FORCE "); Expr(c.Force); if (c.ForceScn != null) { Add(", "); Expr(c.ForceScn); } }
                    if (c.Write != null)
                    {
                        Add(" WRITE");
                        if (c.Write.Wait != null) Add(c.Write.Wait == WaitMode.Wait ? " WAIT" : " NOWAIT");
                        if (c.Write.Mode != null) Add(c.Write.Mode == CommitWriteMode.Immediate ? " IMMEDIATE" : " BATCH");
                    }
                    break;
                case RollbackStatement r:
                    Add("ROLLBACK");
                    if (r.Work) Add(" WORK");
                    if (r.ToSavepoint != null) { Add(" TO SAVEPOINT "); Id(r.ToSavepoint); }
                    else if (r.Force != null) { Add(" FORCE "); Expr(r.Force); }
                    break;
                case SavepointStatement sp: Add("SAVEPOINT "); Id(sp.Name); break;
                case SetTransactionStatement t:
                    Add("SET TRANSACTION");
                    if (t.Access != null) Add(t.Access == TransactionAccess.ReadOnly ? " READ ONLY" : " READ WRITE");
                    if (t.Isolation != null) Add(t.Isolation == IsolationLevel.Serializable ? " ISOLATION LEVEL SERIALIZABLE" : " ISOLATION LEVEL READ COMMITTED");
                    if (t.RollbackSegment != null) { Add(" USE ROLLBACK SEGMENT "); Id(t.RollbackSegment); }
                    if (t.Name != null) { Add(" NAME "); Expr(t.Name); }
                    break;
                case SetConstraintsStatement sc:
                    Add("SET CONSTRAINTS ");
                    if (sc.All) Add("ALL"); else Sep(sc.Constraints, Name);
                    Add(sc.Deferred ? " DEFERRED" : " IMMEDIATE");
                    break;
                case SetRoleStatement sr:
                    Add("SET ROLE ");
                    if (sr.None) Add("NONE");
                    else if (sr.All) { Add("ALL"); if (sr.Except.Count > 0) { Add(" EXCEPT "); IdList(sr.Except); } }
                    else Sep(sr.Roles, r2 => { Id(r2.Name); if (r2.IdentifiedBy != null) { Add(" IDENTIFIED BY "); Expr(r2.IdentifiedBy); } });
                    break;
                case AlterSessionStatement a: AlterSession(a); break;
                case CallStatement call:
                    Add("CALL "); Expr(call.Routine);
                    if (call.Into != null) { Add(" INTO "); Expr(call.Into); }
                    break;
                case ExplainPlanStatement ex:
                    Add("EXPLAIN PLAN");
                    if (ex.StatementId != null) { Add(" SET STATEMENT_ID = "); Expr(ex.StatementId); }
                    if (ex.IntoTable.Count > 0) { Add(" INTO "); Name(ex.IntoTable); }
                    Add(" FOR "); Statement(ex.Statement);
                    break;
                case LockTableStatement lk:
                    Add("LOCK TABLE "); Sep(lk.Tables, Table);
                    Add(" IN " + lk.Mode switch
                    {
                        LockMode.RowShare => "ROW SHARE", LockMode.RowExclusive => "ROW EXCLUSIVE", LockMode.ShareUpdate => "SHARE UPDATE",
                        LockMode.Share => "SHARE", LockMode.ShareRowExclusive => "SHARE ROW EXCLUSIVE", _ => "EXCLUSIVE",
                    } + " MODE");
                    if (lk.NoWait) Add(" NOWAIT"); else if (lk.Wait != null) { Add(" WAIT "); Expr(lk.Wait); }
                    break;
                case TruncateStatement tr:
                    Add(tr.Target == TruncateTarget.Cluster ? "TRUNCATE CLUSTER " : "TRUNCATE TABLE "); Name(tr.Name);
                    if (tr.MaterializedViewLog != null) Add(tr.MaterializedViewLog == MaterializedViewLogAction.Preserve ? " PRESERVE MATERIALIZED VIEW LOG" : " PURGE MATERIALIZED VIEW LOG");
                    if (tr.Storage != null) Add(tr.Storage switch { TruncateStorage.DropStorage => " DROP STORAGE", TruncateStorage.DropAllStorage => " DROP ALL STORAGE", _ => " REUSE STORAGE" });
                    if (tr.Cascade) Add(" CASCADE");
                    break;
                case CommentStatement cm:
                    Add("COMMENT ON " + cm.Target switch
                    {
                        CommentTarget.Table => "TABLE", CommentTarget.Column => "COLUMN", CommentTarget.MaterializedView => "MATERIALIZED VIEW",
                        CommentTarget.Operator => "OPERATOR", CommentTarget.Indextype => "INDEXTYPE", CommentTarget.MiningModel => "MINING MODEL", _ => "EDITION",
                    } + " ");
                    Name(cm.Name); Add(" IS "); Raw(Quote(cm.Text));
                    break;
                case GrantStatement g: Grant(g); break;
                case RenameStatement rn: Add("RENAME "); Name(rn.From); Add(" TO "); Name(rn.To); break;
                case PurgeStatement pg:
                    Add("PURGE ");
                    switch (pg.Target)
                    {
                        case PurgeTarget.Recyclebin: Add("RECYCLEBIN"); break;
                        case PurgeTarget.DbaRecyclebin: Add("DBA_RECYCLEBIN"); break;
                        case PurgeTarget.Table: Add("TABLE "); Name(pg.Name); break;
                        case PurgeTarget.Index: Add("INDEX "); Name(pg.Name); break;
                        case PurgeTarget.Tablespace: Add("TABLESPACE "); Name(pg.Name); if (pg.User != null) { Add(" USER "); Id(pg.User); } break;
                        default: Add("TABLESPACE SET "); Name(pg.Name); if (pg.User != null) { Add(" USER "); Id(pg.User); } break;
                    }
                    break;
                case FlashbackTableStatement fb:
                    Add("FLASHBACK TABLE "); Sep(fb.Tables, Name); Add(" TO ");
                    if (fb.Target == FlashbackTarget.BeforeDrop)
                    {
                        Add("BEFORE DROP");
                        if (fb.RenameTo != null) { Add(" RENAME TO "); Id(fb.RenameTo); }
                    }
                    else
                    {
                        if (fb.Before) Add("BEFORE ");
                        Add(fb.Target switch { FlashbackTarget.Scn => "SCN ", FlashbackTarget.Timestamp => "TIMESTAMP ", _ => "RESTORE POINT " });
                        Expr(fb.Point!);
                    }
                    if (fb.EnableTriggers) Add(" ENABLE TRIGGERS");
                    break;
                case ExecStatement ec:
                    Add("EXEC ");
                    if (ec.Body is AssignmentStatement asg) { Expr(asg.Target); Add(" := "); Expr(asg.Value); }
                    else if (ec.Body is ProcedureCallStatement pcs) Expr(pcs.Call);
                    else throw Unsupported(ec.Body);
                    break;
                case SqlPlusCommand sp2:
                    Add(sp2.Command);
                    if (sp2.Arguments.Count > 0)
                    {
                        if (sp2.Command is not ("@" or "@@")) Sp();
                        // PROMPT / REM: the lexer keeps the rest of the line as one string token that is verbatim text, not a literal
                        if (sp2.Command is "PROMPT" or "REM" or "REMARK" && sp2.Arguments is [LiteralItem { Kind: LiteralKind.String } rest]) Raw(rest.Value);
                        else SyntaxList(sp2.Arguments);
                    }
                    break;
                case AdminStatement ad:
                    Add(ad.Verb);
                    if (ad.Body.Count > 0) { Sp(); SyntaxList(ad.Body); }
                    break;
                default: throw Unsupported(st);
            }
        }

        private void AlterSession(AlterSessionStatement a)
        {
            Add("ALTER SESSION ");
            switch (a.Kind)
            {
                case AlterSessionKind.Set:
                    Add("SET ");
                    Sep(a.Settings, s => { Name(s.Name); Add(" = "); Expr(s.Value); }, " ");
                    break;
                case AlterSessionKind.Enable or AlterSessionKind.Disable or AlterSessionKind.Force:
                    Add(a.Kind == AlterSessionKind.Enable ? "ENABLE " : a.Kind == AlterSessionKind.Disable ? "DISABLE " : "FORCE ");
                    switch (a.Feature)
                    {
                        case SessionFeature.CommitInProcedure: Add("COMMIT IN PROCEDURE"); break;
                        case SessionFeature.Guard: Add("GUARD"); break;
                        case SessionFeature.ParallelDml or SessionFeature.ParallelDdl or SessionFeature.ParallelQuery:
                            Add("PARALLEL " + (a.Feature == SessionFeature.ParallelDml ? "DML" : a.Feature == SessionFeature.ParallelDdl ? "DDL" : "QUERY"));
                            if (a.Parallel != null) { Add(" PARALLEL "); Expr(a.Parallel); }
                            break;
                        case SessionFeature.Resumable:
                            Add("RESUMABLE");
                            if (a.Timeout != null) { Add(" TIMEOUT "); Expr(a.Timeout); }
                            if (a.ResumableName != null) { Add(" NAME "); Expr(a.ResumableName); }
                            break;
                        default: SyntaxList(a.FeatureSyntax); break;
                    }
                    break;
                case AlterSessionKind.CloseDatabaseLink: Add("CLOSE DATABASE LINK "); Id(a.DatabaseLink!); break;
                case AlterSessionKind.Advise:
                    Add("ADVISE " + a.Advise switch { AdviseMode.Commit => "COMMIT", AdviseMode.Rollback => "ROLLBACK", _ => "NOTHING" });
                    break;
                default: Add("SYNC WITH PRIMARY"); break;
            }
        }

        private void Grant(GrantStatement g)
        {
            Add(g.Revoke ? "REVOKE " : "GRANT ");
            if (g.AllPrivileges) Add("ALL PRIVILEGES");
            else Sep(g.Privileges, p => { if (p.Name.Contains('"')) Raw(p.Name); else Add(p.Name); if (p.Columns.Count > 0) { Add(" ("); IdList(p.Columns); Add(")"); } });
            if (g.Object.Count > 0 || g.ObjectKind != null)
            {
                Add(" ON ");
                if (g.ObjectKind != null)
                    Add(g.ObjectKind switch
                    {
                        GrantObjectKind.Directory => "DIRECTORY ", GrantObjectKind.Edition => "EDITION ", GrantObjectKind.MiningModel => "MINING MODEL ",
                        GrantObjectKind.JavaSource => "JAVA SOURCE ", GrantObjectKind.JavaResource => "JAVA RESOURCE ",
                        GrantObjectKind.SqlTranslationProfile => "SQL TRANSLATION PROFILE ", GrantObjectKind.Schema => "SCHEMA ", _ => "USER ",
                    });
                Name(g.Object);
            }
            Add(g.Revoke ? " FROM " : " TO ");
            Sep(g.Grantees, x => { if (x.Public) Add("PUBLIC"); else Id(x.Name!); });
            if (g.WithGrantOption) Add(" WITH GRANT OPTION");
            if (g.WithAdminOption) Add(" WITH ADMIN OPTION");
            if (g.WithHierarchyOption) Add(" WITH HIERARCHY OPTION");
            if (g.CascadeConstraints) Add(" CASCADE CONSTRAINTS");
            if (g.Force) Add(" FORCE");
            if (g.Container != null) Add(g.Container == GrantContainer.All ? " CONTAINER = ALL" : " CONTAINER = CURRENT");
        }

        // ---------- token trees (long-tail DDL / admin bodies) ----------

        private void SyntaxList(IEnumerable<SyntaxItem> items)
        {
            SyntaxItem? prev = null;
            foreach (var it in items)
            {
                if (prev != null && SpaceBetween(prev, it)) Sp();
                Syntax(it);
                prev = it;
            }
        }

        private string SyntaxText(IEnumerable<SyntaxItem> items)
        {
            var p = new Printer(_o);
            p.SyntaxList(items);
            return p.ToString();
        }

        private static bool IsSym(SyntaxItem i, string s) => i is SymbolItem sy && sy.Symbol == s;

        /// <summary>Token trees keep no whitespace: items are separated by a space except around '.', '@', '%', ',' and before '('.</summary>
        private static bool SpaceBetween(SyntaxItem prev, SyntaxItem cur)
        {
            if (IsSym(cur, ",") || IsSym(cur, ";")) return false;
            bool prevWordish = prev is WordItem or GroupItem;
            bool curWordish = cur is WordItem or GroupItem;
            if ((IsSym(cur, ".") || IsSym(cur, "@") || IsSym(cur, "%")) && prevWordish) return false;
            if ((IsSym(prev, ".") || IsSym(prev, "@") || IsSym(prev, "%")) && curWordish) return false;
            if (cur is GroupItem && prev is WordItem) return false;
            return true;
        }

        private void Syntax(SyntaxItem i)
        {
            switch (i)
            {
                case WordItem w: Raw(Word(w.Value, w.Quoted, w.Text)); break;
                case LiteralItem l: Raw(l.Kind == LiteralKind.String ? Quote(l.Value) : l.Value); break;
                case SymbolItem s: Raw(s.Symbol); break;
                case BindItem b: Raw(b.Name.StartsWith('&') ? b.Name : ":" + b.Name); break;
                case GroupItem g: Add("("); SyntaxList(g.Items); Add(")"); break;
                default: throw Unsupported(i);
            }
        }
    }
}
