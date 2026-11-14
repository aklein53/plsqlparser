namespace OracleSqlParser;

public sealed partial class SqlParser
{
    private static readonly HashSet<string> SqlPlusCommands = new(StringComparer.Ordinal)
    {
        "SET","SPOOL","DEFINE","UNDEFINE","VARIABLE","VAR","PRINT","PROMPT","REM","REMARK","WHENEVER","CONNECT","CONN",
        "DISCONNECT","DISC","EXIT","QUIT","ACCEPT","COLUMN","COL","BREAK","COMPUTE","CLEAR","DESCRIBE","DESC","SHOW",
        "START","STORE","TIMING","TTITLE","BTITLE","REPHEADER","REPFOOTER","ATTRIBUTE","COPY","HOST","PAUSE","PASSWORD",
        "RECOVER","GET","SAVE","EDIT","HELP","INPUT","LIST","DEL","XQUERY","ARCHIVE","SQLPLUS","STARTUP","SHUTDOWN","RUN",
    };

    private Statement ParseOtherStatement()
    {
        var start = Cur;
        if (IsOp("<<") || IsKw("DECLARE") || IsKw("BEGIN")) return ParseBlock();
        if (IsOp("@")) return ParseSqlPlusCommand(start, "@");
        if (Cur.Type != TokenType.Identifier) throw Fail($"Unsupported or unexpected statement start {Describe(Cur)}");

        switch (Cur.Value)
        {
            case "COMMIT": return ParseCommit();
            case "ROLLBACK": return ParseRollback();
            case "SAVEPOINT":
                Next();
                return Fin(new SavepointStatement { Name = ParseIdentifier() }, start);
            case "SET" when IsKwAt(1, "TRANSACTION"): return ParseSetTransaction();
            case "SET" when IsKwAt(1, "CONSTRAINT") || IsKwAt(1, "CONSTRAINTS"): return ParseSetConstraints();
            case "SET" when IsKwAt(1, "ROLE"): return ParseSetRole();
            case "CALL": return ParseCall();
            case "EXPLAIN": return ParseExplain();
            case "LOCK": return ParseLock();
            case "TRUNCATE": return ParseTruncate();
            case "COMMENT" when IsKwAt(1, "ON"): return ParseComment();
            case "GRANT": case "REVOKE": return ParseGrant();
            case "DROP": return ParseDrop();
            case "CREATE": return ParseCreate();
            case "ALTER": return ParseAlter();
            case "RENAME" when !SqlPlusCommands.Contains("RENAME"): return ParseRename();
            case "PURGE": return ParsePurge();
            case "FLASHBACK" when IsKwAt(1, "TABLE"): return ParseFlashbackTable();
            case "ANALYZE": return ParseAnalyze(start);
            case "AUDIT": case "NOAUDIT": return ParseAudit(start);
            case "ASSOCIATE": case "DISASSOCIATE": case "ADMINISTER": case "FLASHBACK":
            {
                var verb = Next().Value;
                var body = ParseSyntaxItems(AtStatementEnd);
                return Fin(new AdminStatement { Verb = verb, Body = body }, start);
            }
            case "EXEC" or "EXECUTE" when !IsKwAt(1, "IMMEDIATE"):
            {
                Next();
                using (SetContext(true)) // SQL*Plus EXEC runs as PL/SQL
                {
                    var bodyStart = Cur;
                    var target = ParsePostfix();
                    Statement body = AcceptOp(":=")
                        ? Fin(new AssignmentStatement { Target = target, Value = ParseExpr() }, bodyStart)
                        : Fin(new ProcedureCallStatement { Call = target }, bodyStart);
                    return Fin(new ExecStatement { Body = body }, start);
                }
            }
        }
        if (SqlPlusCommands.Contains(Cur.Value)) return ParseSqlPlusCommand(start, Next().Value);
        throw Fail($"Unsupported or unexpected statement start {Describe(Cur)}");
    }

    private Statement ParseSqlPlusCommand(Token start, string command)
    {
        if (command == "@") { Next(); if (IsOp("@")) { Next(); command = "@@"; } }
        var cmd = new SqlPlusCommand { Command = command };
        cmd.Arguments = ParseSyntaxItems(() => Cur.Line != start.Line || IsOp(";"));
        return Fin(cmd, start);
    }

    // ---- transaction control & session ----

    private Statement ParseCommit()
    {
        var start = Next();
        var st = new CommitStatement { Work = AcceptKw("WORK") };
        if (AcceptKw("COMMENT")) st.Comment = ParseExpr();
        if (AcceptKw("FORCE")) { st.Force = ParseExpr(); if (AcceptOp(",")) st.ForceScn = ParseExpr(); }
        if (IsKw("WRITE"))
        {
            var ws = Next();
            var w = new CommitWrite();
            int prog14 = -1;
            while (Progress(ref prog14))
            {
                if (AcceptKw("WAIT")) w.Wait = WaitMode.Wait;
                else if (AcceptKw("NOWAIT")) w.Wait = WaitMode.NoWait;
                else if (AcceptKw("IMMEDIATE")) w.Mode = CommitWriteMode.Immediate;
                else if (AcceptKw("BATCH")) w.Mode = CommitWriteMode.Batch;
                else break;
            }
            st.Write = Fin(w, ws);
        }
        return Fin(st, start);
    }

    private Statement ParseRollback()
    {
        var start = Next();
        var st = new RollbackStatement { Work = AcceptKw("WORK") };
        if (AcceptKw("TO"))
        {
            AcceptKw("SAVEPOINT");
            st.ToSavepoint = ParseIdentifier();
        }
        else if (AcceptKw("FORCE")) st.Force = ParseExpr();
        return Fin(st, start);
    }

    private Statement ParseSetTransaction()
    {
        var start = Next(); Next();
        var st = new SetTransactionStatement();
        int prog15 = -1;
        while (!AtStatementEnd() && Progress(ref prog15))
        {
            if (IsKw("READ") && IsKwAt(1, "ONLY")) { Next(); Next(); st.Access = TransactionAccess.ReadOnly; }
            else if (IsKw("READ") && IsKwAt(1, "WRITE")) { Next(); Next(); st.Access = TransactionAccess.ReadWrite; }
            else if (IsKw("ISOLATION"))
            {
                Next(); ExpectKw("LEVEL");
                if (AcceptKw("SERIALIZABLE")) st.Isolation = IsolationLevel.Serializable;
                else { ExpectKw("READ"); ExpectKw("COMMITTED"); st.Isolation = IsolationLevel.ReadCommitted; }
            }
            else if (IsKw("USE") && IsKwAt(1, "ROLLBACK")) { Next(); Next(); ExpectKw("SEGMENT"); st.RollbackSegment = ParseIdentifier(); }
            else if (AcceptKw("NAME")) st.Name = ParseAdditive();
            else throw Fail($"Unexpected {Describe(Cur)} in SET TRANSACTION");
        }
        return Fin(st, start);
    }

    private Statement ParseSetConstraints()
    {
        var start = Next(); Next();
        var st = new SetConstraintsStatement();
        if (AcceptKw("ALL")) st.All = true;
        else do st.Constraints.Add(ParseDottedName()); while (AcceptOp(","));
        if (AcceptKw("DEFERRED")) st.Deferred = true; else ExpectKw("IMMEDIATE");
        return Fin(st, start);
    }

    private Statement ParseSetRole()
    {
        var start = Next(); Next();
        var st = new SetRoleStatement();
        if (AcceptKw("NONE")) st.None = true;
        else if (AcceptKw("ALL"))
        {
            st.All = true;
            if (AcceptKw("EXCEPT")) st.Except = ParseIdentifierList();
        }
        else
        {
            do
            {
                var s = Cur;
                var rs = new RoleSpec { Name = ParseIdentifier() };
                if (AcceptKw("IDENTIFIED")) { ExpectKw("BY"); rs.IdentifiedBy = ParseAdditive(); }
                st.Roles.Add(Fin(rs, s));
            } while (AcceptOp(","));
        }
        return Fin(st, start);
    }

    private Statement ParseCall()
    {
        var start = Next();
        var st = new CallStatement { Routine = ParsePostfix() };
        if (AcceptKw("INTO")) st.Into = ParsePostfix();
        return Fin(st, start);
    }

    private Statement ParseExplain()
    {
        var start = Next();
        ExpectKw("PLAN");
        Expression? statementId = null;
        var intoTable = new List<Identifier>();
        if (IsKw("SET")) { Next(); ExpectKw("STATEMENT_ID"); ExpectOp("="); statementId = ParseExpr(); }
        if (AcceptKw("INTO")) intoTable = ParseDottedName();
        ExpectKw("FOR");
        var st = new ExplainPlanStatement { StatementId = statementId, IntoTable = intoTable, Statement = ParseStatementCore() };
        return Fin(st, start);
    }

    private Statement ParseLock()
    {
        var start = Next();
        ExpectKw("TABLE");
        var st = new LockTableStatement();
        do st.Tables.Add(ParseTablePrimary()); while (AcceptOp(","));
        ExpectKw("IN");
        if (AcceptKw("ROW")) st.Mode = AcceptKw("SHARE") ? LockMode.RowShare : (AcceptKw("EXCLUSIVE") ? LockMode.RowExclusive : throw FailExpected("Expected SHARE or EXCLUSIVE", "SHARE", "EXCLUSIVE"));
        else if (AcceptKw("SHARE"))
        {
            if (AcceptKw("UPDATE")) st.Mode = LockMode.ShareUpdate;
            else if (AcceptKw("ROW")) { ExpectKw("EXCLUSIVE"); st.Mode = LockMode.ShareRowExclusive; }
            else st.Mode = LockMode.Share;
        }
        else { ExpectKw("EXCLUSIVE"); st.Mode = LockMode.Exclusive; }
        ExpectKw("MODE");
        if (AcceptKw("NOWAIT")) st.NoWait = true;
        else if (AcceptKw("WAIT")) st.Wait = ParseAdditive();
        return Fin(st, start);
    }

    private Statement ParseTruncate()
    {
        var start = Next();
        var st = new TruncateStatement();
        if (AcceptKw("CLUSTER")) st.Target = TruncateTarget.Cluster; else ExpectKw("TABLE");
        st.Name = ParseDottedName();
        int prog16 = -1;
        while (!AtStatementEnd() && Progress(ref prog16))
        {
            if (IsKw("PRESERVE") && IsKwAt(1, "MATERIALIZED")) { Next(); Next(); ExpectKw("VIEW"); ExpectKw("LOG"); st.MaterializedViewLog = MaterializedViewLogAction.Preserve; }
            else if (IsKw("PURGE") && IsKwAt(1, "MATERIALIZED")) { Next(); Next(); ExpectKw("VIEW"); ExpectKw("LOG"); st.MaterializedViewLog = MaterializedViewLogAction.Purge; }
            else if (IsKw("DROP"))
            {
                Next();
                if (AcceptKw("ALL")) { ExpectKw("STORAGE"); st.Storage = TruncateStorage.DropAllStorage; }
                else { ExpectKw("STORAGE"); st.Storage = TruncateStorage.DropStorage; }
            }
            else if (AcceptKw("REUSE")) { ExpectKw("STORAGE"); st.Storage = TruncateStorage.ReuseStorage; }
            else if (AcceptKw("CASCADE")) st.Cascade = true;
            else throw Fail($"Unexpected {Describe(Cur)} in TRUNCATE");
        }
        return Fin(st, start);
    }

    private Statement ParseComment()
    {
        var start = Next(); Next(); // COMMENT ON
        var st = new CommentStatement();
        if (AcceptKw("COLUMN")) st.Target = CommentTarget.Column;
        else if (AcceptKw("TABLE")) st.Target = CommentTarget.Table;
        else if (IsKw("MATERIALIZED")) { Next(); ExpectKw("VIEW"); st.Target = CommentTarget.MaterializedView; }
        else if (AcceptKw("OPERATOR")) st.Target = CommentTarget.Operator;
        else if (AcceptKw("INDEXTYPE")) st.Target = CommentTarget.Indextype;
        else if (IsKw("MINING")) { Next(); ExpectKw("MODEL"); st.Target = CommentTarget.MiningModel; }
        else if (AcceptKw("EDITION")) st.Target = CommentTarget.Edition;
        else throw Fail($"Unsupported COMMENT ON target {Describe(Cur)}", ParseErrorCodes.UnsupportedForm);
        st.Name = ParseDottedName();
        ExpectKw("IS");
        if (Cur.Type != TokenType.String) throw FailExpected($"Expected string literal but found {Describe(Cur)}", "string literal");
        st.Text = Next().Value;
        return Fin(st, start);
    }

    private Statement ParseRename()
    {
        var start = Next();
        var st = new RenameStatement { From = ParseDottedName() };
        ExpectKw("TO");
        st.To = ParseDottedName();
        return Fin(st, start);
    }

    private Statement ParsePurge()
    {
        var start = Next();
        var st = new PurgeStatement();
        if (AcceptKw("RECYCLEBIN")) st.Target = PurgeTarget.Recyclebin;
        else if (AcceptKw("DBA_RECYCLEBIN")) st.Target = PurgeTarget.DbaRecyclebin;
        else if (AcceptKw("TABLE")) { st.Target = PurgeTarget.Table; st.Name = ParseDottedName(); }
        else if (AcceptKw("INDEX")) { st.Target = PurgeTarget.Index; st.Name = ParseDottedName(); }
        else if (AcceptKw("TABLESPACE"))
        {
            if (AcceptKw("SET")) st.Target = PurgeTarget.TablespaceSet; else st.Target = PurgeTarget.Tablespace;
            st.Name = ParseDottedName();
            if (AcceptKw("USER")) st.User = ParseIdentifier();
        }
        else throw Fail($"Unexpected {Describe(Cur)} in PURGE");
        return Fin(st, start);
    }

    private Statement ParseFlashbackTable()
    {
        var start = Next(); Next();
        var st = new FlashbackTableStatement();
        do st.Tables.Add(ParseDottedName()); while (AcceptOp(","));
        ExpectKw("TO");
        if (AcceptKw("BEFORE"))
        {
            if (AcceptKw("DROP")) { st.Target = FlashbackTarget.BeforeDrop; st.Before = true; if (AcceptKw("RENAME")) { ExpectKw("TO"); st.RenameTo = ParseIdentifier(); } }
            else { st.Before = true; ParseFlashbackPoint(st); }
        }
        else ParseFlashbackPoint(st);
        if (IsKw("ENABLE") && IsKwAt(1, "TRIGGERS")) { Next(); Next(); st.EnableTriggers = true; }
        return Fin(st, start);
    }

    private void ParseFlashbackPoint(FlashbackTableStatement st)
    {
        if (AcceptKw("SCN")) st.Target = FlashbackTarget.Scn;
        else if (AcceptKw("TIMESTAMP")) st.Target = FlashbackTarget.Timestamp;
        else if (IsKw("RESTORE")) { Next(); ExpectKw("POINT"); st.Target = FlashbackTarget.RestorePoint; }
        else throw FailExpected($"Expected SCN, TIMESTAMP or RESTORE POINT but found {Describe(Cur)}", "SCN", "TIMESTAMP", "RESTORE POINT");
        st.Point = ParseAdditive();
    }

    private Statement ParseGrant()
    {
        var start = Cur;
        var st = new GrantStatement { Revoke = Next().Value == "REVOKE" };
        if (IsKw("ALL") && !IsOpAt(1, ",")) { Next(); AcceptKw("PRIVILEGES"); st.AllPrivileges = true; }
        else
            do
            {
                var ps = Cur;
                var gp = new GrantPrivilege();
                var words = new List<string>();
                int prog17 = -1;
                while (Cur.Type is TokenType.Identifier or TokenType.QuotedIdentifier && !IsKw("ON") && !IsKw("TO") && !IsKw("FROM") && Progress(ref prog17))
                    words.Add(Cur.Type == TokenType.QuotedIdentifier ? "\"" + Next().Value.Replace("\"", "\"\"") + "\"" : Next().Value);
                gp.Name = string.Join(" ", words);
                if (IsOp("(")) { Next(); gp.Columns = ParseIdentifierList(); ExpectOp(")"); }
                st.Privileges.Add(Fin(gp, ps));
            } while (AcceptOp(","));
        if (AcceptKw("ON"))
        {
            if (AcceptKw("DIRECTORY")) st.ObjectKind = GrantObjectKind.Directory;
            else if (AcceptKw("EDITION")) st.ObjectKind = GrantObjectKind.Edition;
            else if (AcceptKw("USER")) st.ObjectKind = GrantObjectKind.User;
            else if (IsKw("SCHEMA") && !IsOpAt(1, "."))
            {
                // ON SCHEMA is a 23ai schema-level grant target; ON schema.table names a table in schema "schema".
                RequireVersion(OracleVersion.Oracle23, "schema-level privilege", Cur);
                Next();
                st.ObjectKind = GrantObjectKind.Schema;
            }
            else if (IsKw("MINING")) { Next(); ExpectKw("MODEL"); st.ObjectKind = GrantObjectKind.MiningModel; }
            else if (IsKw("JAVA") && IsKwAt(1, "SOURCE")) { Next(); Next(); st.ObjectKind = GrantObjectKind.JavaSource; }
            else if (IsKw("JAVA") && IsKwAt(1, "RESOURCE")) { Next(); Next(); st.ObjectKind = GrantObjectKind.JavaResource; }
            else if (IsKw("SQL") && IsKwAt(1, "TRANSLATION")) { Next(); Next(); ExpectKw("PROFILE"); st.ObjectKind = GrantObjectKind.SqlTranslationProfile; }
            st.Object = ParseDottedName();
        }
        if (!AcceptKw("TO")) ExpectKw("FROM");
        do
        {
            var gs = Cur;
            var g = new Grantee();
            if (AcceptKw("PUBLIC")) g.Public = true; else g.Name = ParseIdentifier();
            st.Grantees.Add(Fin(g, gs));
        } while (AcceptOp(","));
        int prog18 = -1;
        while (!AtStatementEnd() && Progress(ref prog18))
        {
            if (IsKw("WITH") && IsKwAt(1, "GRANT")) { Next(); Next(); ExpectKw("OPTION"); st.WithGrantOption = true; }
            else if (IsKw("WITH") && IsKwAt(1, "ADMIN")) { Next(); Next(); ExpectKw("OPTION"); st.WithAdminOption = true; }
            else if (IsKw("WITH") && IsKwAt(1, "HIERARCHY")) { Next(); Next(); ExpectKw("OPTION"); st.WithHierarchyOption = true; }
            else if (IsKw("CASCADE")) { Next(); ExpectKw("CONSTRAINTS"); st.CascadeConstraints = true; }
            else if (AcceptKw("FORCE")) st.Force = true;
            else if (IsKw("CONTAINER")) { Next(); ExpectOp("="); st.Container = AcceptKw("ALL") ? GrantContainer.All : (AcceptKw("CURRENT") ? GrantContainer.Current : throw FailExpected("Expected ALL or CURRENT", "ALL", "CURRENT")); }
            else throw Fail($"Unexpected {Describe(Cur)} in GRANT/REVOKE");
        }
        return Fin(st, start);
    }

    // ---- object kinds ----

    internal static readonly (string[] Words, ObjectKind Kind)[] ObjectKindTable = new (string[], ObjectKind)[]
    {
        (["MATERIALIZED","VIEW","LOG"], ObjectKind.MaterializedViewLog), (["MATERIALIZED","ZONEMAP"], ObjectKind.MaterializedZonemap),
        (["MATERIALIZED","VIEW"], ObjectKind.MaterializedView), (["PACKAGE","BODY"], ObjectKind.PackageBody),
        (["TYPE","BODY"], ObjectKind.TypeBody), (["DATABASE","LINK"], ObjectKind.DatabaseLink),
        (["PLUGGABLE","DATABASE"], ObjectKind.PluggableDatabase), (["LOCKDOWN","PROFILE"], ObjectKind.LockdownProfile),
        (["AUDIT","POLICY"], ObjectKind.AuditPolicy), (["ANALYTIC","VIEW"], ObjectKind.AnalyticView),
        (["ATTRIBUTE","DIMENSION"], ObjectKind.AttributeDimension), (["INMEMORY","JOIN","GROUP"], ObjectKind.InmemoryJoinGroup),
        (["MINING","MODEL"], ObjectKind.MiningModel), (["PROPERTY","GRAPH"], ObjectKind.PropertyGraph),
        (["JSON","RELATIONAL","DUALITY","VIEW"], ObjectKind.JsonRelationalDualityView), (["JSON","DUALITY","VIEW"], ObjectKind.JsonRelationalDualityView),
        (["SQL","TRANSLATION","PROFILE"], ObjectKind.SqlTranslationProfile), (["ROLLBACK","SEGMENT"], ObjectKind.RollbackSegment),
        (["RESTORE","POINT"], ObjectKind.RestorePoint), (["FLASHBACK","ARCHIVE"], ObjectKind.FlashbackArchive),
        (["TABLESPACE","SET"], ObjectKind.TablespaceSet), (["RESOURCE","COST"], ObjectKind.ResourceCost),
        (["JAVA","SOURCE"], ObjectKind.JavaSource), (["JAVA","CLASS"], ObjectKind.JavaClass), (["JAVA","RESOURCE"], ObjectKind.JavaResource),
        (["MLE","MODULE"], ObjectKind.MleModule), (["MLE","ENV"], ObjectKind.MleEnv),
        (["TABLE"], ObjectKind.Table), (["VIEW"], ObjectKind.View), (["INDEX"], ObjectKind.Index), (["INDEXTYPE"], ObjectKind.Indextype),
        (["SEQUENCE"], ObjectKind.Sequence), (["SYNONYM"], ObjectKind.Synonym), (["PROCEDURE"], ObjectKind.Procedure),
        (["FUNCTION"], ObjectKind.Function), (["PACKAGE"], ObjectKind.Package), (["TRIGGER"], ObjectKind.Trigger),
        (["TYPE"], ObjectKind.Type), (["USER"], ObjectKind.User), (["ROLE"], ObjectKind.Role), (["PROFILE"], ObjectKind.Profile),
        (["TABLESPACE"], ObjectKind.Tablespace), (["DIRECTORY"], ObjectKind.Directory), (["LIBRARY"], ObjectKind.Library),
        (["CLUSTER"], ObjectKind.Cluster), (["CONTEXT"], ObjectKind.Context), (["DIMENSION"], ObjectKind.Dimension),
        (["DOMAIN"], ObjectKind.Domain), (["EDITION"], ObjectKind.Edition), (["OPERATOR"], ObjectKind.Operator),
        (["OUTLINE"], ObjectKind.Outline), (["DISKGROUP"], ObjectKind.Diskgroup), (["DATABASE"], ObjectKind.Database),
        (["HIERARCHY"], ObjectKind.Hierarchy), (["CONTROLFILE"], ObjectKind.ControlFile), (["PFILE"], ObjectKind.Pfile),
        (["SPFILE"], ObjectKind.Spfile), (["SCHEMA"], ObjectKind.Schema), (["SYSTEM"], ObjectKind.System), (["SESSION"], ObjectKind.Session),
    };

    private bool TryParseObjectKind(out ObjectKind kind)
    {
        foreach (var (words, k) in ObjectKindTable)
        {
            bool ok = true;
            for (int i = 0; i < words.Length && ok; i++) ok = IsKwAt(i, words[i]);
            if (!ok) continue;
            for (int i = 0; i < words.Length; i++) Next();
            kind = k;
            return true;
        }
        kind = ObjectKind.Other;
        return false;
    }

    private static bool ObjectKindHasName(ObjectKind k) =>
        k is not (ObjectKind.MaterializedViewLog or ObjectKind.ControlFile or ObjectKind.Pfile or ObjectKind.Spfile or ObjectKind.System or ObjectKind.Session or ObjectKind.ResourceCost or ObjectKind.Schema or ObjectKind.Other);

    private bool TryIfNotExists() { if (IsKw("IF") && IsKwAt(1, "NOT") && IsKwAt(2, "EXISTS")) { RequireVersion(OracleVersion.Oracle23, "IF [NOT] EXISTS", Cur); Next(); Next(); Next(); return true; } return false; }
    private bool TryIfExists() { if (IsKw("IF") && IsKwAt(1, "EXISTS")) { RequireVersion(OracleVersion.Oracle23, "IF [NOT] EXISTS", Cur); Next(); Next(); return true; } return false; }

    /// <summary>Object kinds that need a newer release than the target, for CREATE, ALTER and DROP alike.</summary>
    private void RequireKindVersion(string verb, ObjectKind kind, Token at)
    {
        var (min, name) = kind switch
        {
            ObjectKind.Domain => (OracleVersion.Oracle23, "DOMAIN"),
            ObjectKind.PropertyGraph => (OracleVersion.Oracle23, "PROPERTY GRAPH"),
            ObjectKind.JsonRelationalDualityView => (OracleVersion.Oracle23, "JSON RELATIONAL DUALITY VIEW"),
            ObjectKind.MleModule => (OracleVersion.Oracle23, "MLE MODULE"),
            ObjectKind.MleEnv => (OracleVersion.Oracle23, "MLE ENV"),
            _ => (OracleVersion.Oracle19, string.Empty),
        };
        if (name.Length > 0) RequireVersion(min, $"{verb} {name}", at);
    }

    // ---- CREATE / ALTER USER ----

    private bool IsNameAt(int n) => PeekTok(n).Type is TokenType.Identifier or TokenType.QuotedIdentifier;

    private Statement ParseCreateUser(Token start, bool orReplace, bool pub)
    {
        if (orReplace || pub) throw Fail("OR REPLACE and PUBLIC do not apply to CREATE USER", ParseErrorCodes.InvalidCombination);
        var ifNotExists = TryIfNotExists();
        var name = ParseIdentifier();
        if (!ifNotExists) ifNotExists = TryIfNotExists();
        var st = new CreateUserStatement { IfNotExists = ifNotExists, Name = name };
        st.Clauses = ParseUserClauses();
        return Fin(st, start);
    }

    private Statement ParseAlterUser(Token start, bool pub)
    {
        if (pub) throw Fail("PUBLIC does not apply to ALTER USER", ParseErrorCodes.InvalidCombination);
        var ifExists = TryIfExists();
        var st = new AlterUserStatement { IfExists = ifExists, Name = ParseIdentifier() };
        st.Clauses = ParseUserClauses();
        return Fin(st, start);
    }

    private List<UserClause> ParseUserClauses()
    {
        var list = new List<UserClause>();
        int prog = -1;
        while (!AtStatementEnd() && Progress(ref prog)) list.Add(ParseUserClause());
        return list;
    }

    private UserClause ParseUserClause()
    {
        var s = Cur;
        var c = new UserClause();
        if (IsKw("IDENTIFIED") && IsKwAt(1, "BY") && IsNameAt(2) && !IsKwAt(2, "VALUES"))
        {
            Next(); Next();
            c.Kind = UserClauseKind.IdentifiedBy;
            c.Password = ParseIdentifier();
            if (AcceptKw("REPLACE")) c.ReplacePassword = ParseIdentifier();
        }
        else if (IsKw("IDENTIFIED") && (IsKwAt(1, "EXTERNALLY") || IsKwAt(1, "GLOBALLY")))
        {
            Next();
            c.Kind = Next().Value == "EXTERNALLY" ? UserClauseKind.IdentifiedExternally : UserClauseKind.IdentifiedGlobally;
            if (AcceptKw("AS")) c.ExternalName = ParseAdditive();
        }
        else if (IsKw("NO") && IsKwAt(1, "AUTHENTICATION")) { Next(); Next(); c.Kind = UserClauseKind.NoAuthentication; }
        else if (IsKw("DEFAULT") && IsKwAt(1, "TABLESPACE")) { Next(); Next(); c.Kind = UserClauseKind.DefaultTablespace; c.Tablespace = ParseIdentifier(); }
        else if (IsKw("DEFAULT") && IsKwAt(1, "ROLE")) { Next(); Next(); c.Kind = UserClauseKind.DefaultRole; ParseRoleSet(c); }
        else if ((IsKw("TEMPORARY") && IsKwAt(1, "TABLESPACE")) || (IsKw("LOCAL") && IsKwAt(1, "TEMPORARY") && IsKwAt(2, "TABLESPACE")))
        {
            c.Local = AcceptKw("LOCAL");
            Next(); Next();
            c.Kind = UserClauseKind.TemporaryTablespace;
            c.Tablespace = ParseIdentifier();
        }
        else if (AcceptKw("QUOTA"))
        {
            c.Kind = UserClauseKind.Quota;
            if (AcceptKw("UNLIMITED")) c.Unlimited = true; else c.Quota = ParseAdditive();
            ExpectKw("ON");
            c.Tablespace = ParseIdentifier();
        }
        else if (AcceptKw("PROFILE")) { c.Kind = UserClauseKind.Profile; c.Profile = ParseIdentifier(); }
        else if (IsKw("PASSWORD") && IsKwAt(1, "EXPIRE")) { Next(); Next(); c.Kind = UserClauseKind.PasswordExpire; }
        else if (IsKw("ACCOUNT") && (IsKwAt(1, "LOCK") || IsKwAt(1, "UNLOCK")))
        {
            Next();
            c.Kind = Next().Value == "LOCK" ? UserClauseKind.AccountLock : UserClauseKind.AccountUnlock;
        }
        else if (IsKw("ENABLE") && IsKwAt(1, "EDITIONS")) { Next(); Next(); c.Kind = UserClauseKind.EnableEditions; c.Force = AcceptKw("FORCE"); }
        else if (IsKw("DISABLE") && IsKwAt(1, "EDITIONS")) { Next(); Next(); c.Kind = UserClauseKind.DisableEditions; }
        else if (IsKw("READ") && (IsKwAt(1, "ONLY") || IsKwAt(1, "WRITE")))
        {
            RequireVersion(OracleVersion.Oracle23, "user READ ONLY/READ WRITE", Cur);
            Next();
            c.Kind = Next().Value == "ONLY" ? UserClauseKind.ReadOnly : UserClauseKind.ReadWrite;
        }
        else if (IsKw("CONTAINER") && IsOpAt(1, "="))
        {
            Next(); Next();
            c.Kind = UserClauseKind.Container;
            if (AcceptKw("ALL")) c.Container = GrantContainer.All;
            else { ExpectKw("CURRENT"); c.Container = GrantContainer.Current; }
        }
        else if ((IsKw("GRANT") || IsKw("REVOKE")) && IsKwAt(1, "CONNECT") && IsKwAt(2, "THROUGH"))
        {
            bool grant = Next().Value == "GRANT";
            Next(); Next();
            c.Kind = grant ? UserClauseKind.GrantConnectThrough : UserClauseKind.RevokeConnectThrough;
            if (AcceptKw("ENTERPRISE")) { ExpectKw("USERS"); c.EnterpriseUsers = true; }
            else c.Proxy = ParseIdentifier();
            if (grant)
            {
                if (IsKw("WITH") && IsKwAt(1, "NO") && IsKwAt(2, "ROLES")) { Next(); Next(); Next(); c.RoleSet = RoleSetKind.None; }
                else if (AcceptKw("WITH")) { ExpectKw("ROLE"); ParseRoleSet(c); }
                if (AcceptKw("AUTHENTICATION")) { ExpectKw("REQUIRED"); c.AuthenticationRequired = true; }
            }
        }
        else
        {
            // Not modelled: keep one token so the clause order survives a round trip.
            c.Kind = UserClauseKind.Other;
            c.Syntax.Add(ParseSyntaxItem());
        }
        return Fin(c, s);
    }

    /// <summary>NONE | ALL [EXCEPT list] | list, as used by DEFAULT ROLE and WITH ROLE.</summary>
    private void ParseRoleSet(UserClause c)
    {
        if (AcceptKw("NONE")) c.RoleSet = RoleSetKind.None;
        else if (AcceptKw("ALL"))
        {
            c.RoleSet = RoleSetKind.All;
            if (AcceptKw("EXCEPT")) c.ExceptRoles = ParseIdentifierList();
        }
        else { c.RoleSet = RoleSetKind.Roles; c.Roles = ParseIdentifierList(); }
    }

    // ---- CREATE / ALTER ROLE ----

    private Statement ParseCreateRole(Token start, bool orReplace, bool pub)
    {
        if (orReplace || pub) throw Fail("OR REPLACE and PUBLIC do not apply to CREATE ROLE", ParseErrorCodes.InvalidCombination);
        var ifNotExists = TryIfNotExists();
        var name = ParseIdentifier();
        if (!ifNotExists) ifNotExists = TryIfNotExists();
        var st = new CreateRoleStatement { IfNotExists = ifNotExists, Name = name };
        st.Authentication = ParseRoleIdentification(out var password, out var package);
        st.Password = password;
        st.Package = package;
        st.Container = TryParseContainer();
        return Fin(st, start);
    }

    private Statement ParseAlterRole(Token start, bool pub)
    {
        if (pub) throw Fail("PUBLIC does not apply to ALTER ROLE", ParseErrorCodes.InvalidCombination);
        var ifExists = TryIfExists();
        var st = new AlterRoleStatement { IfExists = ifExists, Name = ParseIdentifier() };
        st.Authentication = ParseRoleIdentification(out var password, out var package);
        st.Password = password;
        st.Package = package;
        st.Container = TryParseContainer();
        return Fin(st, start);
    }

    /// <summary>NOT IDENTIFIED | IDENTIFIED BY pw | IDENTIFIED USING pkg | IDENTIFIED EXTERNALLY | IDENTIFIED GLOBALLY.</summary>
    private RoleAuthentication? ParseRoleIdentification(out Identifier? password, out List<Identifier> package)
    {
        password = null;
        package = [];
        if (IsKw("NOT") && IsKwAt(1, "IDENTIFIED")) { Next(); Next(); return RoleAuthentication.NotIdentified; }
        if (!AcceptKw("IDENTIFIED")) return null;
        if (AcceptKw("BY")) { password = ParseIdentifier(); return RoleAuthentication.Password; }
        if (AcceptKw("USING")) { package = ParseDottedName(); return RoleAuthentication.Package; }
        if (AcceptKw("EXTERNALLY")) return RoleAuthentication.External;
        ExpectKw("GLOBALLY");
        return RoleAuthentication.Global;
    }

    /// <summary>CONTAINER = CURRENT | ALL, when present.</summary>
    private GrantContainer? TryParseContainer()
    {
        if (!(IsKw("CONTAINER") && IsOpAt(1, "="))) return null;
        Next(); Next();
        if (AcceptKw("ALL")) return GrantContainer.All;
        ExpectKw("CURRENT");
        return GrantContainer.Current;
    }

    // ---- CREATE / ALTER PROFILE ----

    private Statement ParseCreateProfile(Token start, bool orReplace, bool pub)
    {
        if (orReplace || pub) throw Fail("OR REPLACE and PUBLIC do not apply to CREATE PROFILE", ParseErrorCodes.InvalidCombination);
        var ifNotExists = TryIfNotExists();
        var name = ParseIdentifier();
        if (!ifNotExists) ifNotExists = TryIfNotExists();
        var st = new CreateProfileStatement { IfNotExists = ifNotExists, Name = name };
        ParseProfileBody(st.Limits, out var container, out _, allowCascade: false);
        st.Container = container;
        return Fin(st, start);
    }

    private Statement ParseAlterProfile(Token start, bool pub)
    {
        if (pub) throw Fail("PUBLIC does not apply to ALTER PROFILE", ParseErrorCodes.InvalidCombination);
        var ifExists = TryIfExists();
        var st = new AlterProfileStatement { IfExists = ifExists, Name = ParseIdentifier() };
        ParseProfileBody(st.Limits, out var container, out var cascade, allowCascade: true);
        st.Container = container;
        st.Cascade = cascade;
        return Fin(st, start);
    }

    /// <summary>[LIMIT] {name value}... [CONTAINER = ...] [CASCADE]. The clauses may follow each other in any order.</summary>
    private void ParseProfileBody(List<ProfileLimit> limits, out GrantContainer? container, out bool cascade, bool allowCascade)
    {
        container = null;
        cascade = false;
        AcceptKw("LIMIT");
        int prog = -1;
        while (!AtStatementEnd() && Progress(ref prog))
        {
            if (IsKw("CONTAINER") && IsOpAt(1, "=")) container = TryParseContainer();
            else if (allowCascade && AcceptKw("CASCADE")) cascade = true;
            else limits.Add(ParseProfileLimit());
        }
    }

    private ProfileLimit ParseProfileLimit()
    {
        var s = Cur;
        var l = new ProfileLimit { Name = ParseIdentifier() };
        if (AcceptKw("UNLIMITED")) l.Keyword = ProfileKeyword.Unlimited;
        else if (AcceptKw("DEFAULT")) l.Keyword = ProfileKeyword.Default;
        else if (AcceptKw("NULL")) l.Keyword = ProfileKeyword.Null;
        else if (l.Name.Value == "PASSWORD_VERIFY_FUNCTION") l.Function = ParseDottedName();
        else l.Value = ParseAdditive();
        return Fin(l, s);
    }

    // ---- CREATE DIRECTORY / DATABASE LINK / CONTEXT ----

    private Statement ParseCreateDirectory(Token start, bool orReplace, bool pub)
    {
        if (pub) throw Fail("PUBLIC does not apply to CREATE DIRECTORY", ParseErrorCodes.InvalidCombination);
        var ifNotExists = TryIfNotExists();
        var name = ParseIdentifier();
        if (!ifNotExists) ifNotExists = TryIfNotExists();
        var sharing = TryParseSharing();
        ExpectKw("AS");
        var path = ParseAdditive();
        var st = new CreateDirectoryStatement { OrReplace = orReplace, IfNotExists = ifNotExists, Name = name, Sharing = sharing, Path = path };
        return Fin(st, start);
    }

    private Statement ParseCreateDatabaseLink(Token start, bool orReplace, bool pub, bool shared)
    {
        var st = new CreateDatabaseLinkStatement { OrReplace = orReplace, Public = pub, Shared = shared, IfNotExists = TryIfNotExists() };
        st.Name = ParseDottedName();
        if (!st.IfNotExists) st.IfNotExists = TryIfNotExists();
        if (IsKw("CONNECT") && IsKwAt(1, "TO") && AcceptKw("CONNECT"))
        {
            ExpectKw("TO");
            if (AcceptKw("CURRENT_USER")) st.CurrentUser = true;
            else
            {
                st.ConnectUser = ParseIdentifier();
                if (AcceptKw("IDENTIFIED")) { ExpectKw("BY"); st.ConnectPassword = ParseIdentifier(); }
            }
        }
        if (AcceptKw("AUTHENTICATED"))
        {
            ExpectKw("BY");
            st.AuthenticatedUser = ParseIdentifier();
            ExpectKw("IDENTIFIED");
            ExpectKw("BY");
            st.AuthenticatedPassword = ParseIdentifier();
        }
        if (AcceptKw("USING")) st.Using = ParseAdditive();
        int prog = -1;
        while (!AtStatementEnd() && Progress(ref prog)) st.OtherClauses.Add(ParseSyntaxItem());
        return Fin(st, start);
    }

    private Statement ParseCreateContext(Token start, bool orReplace, bool pub)
    {
        if (pub) throw Fail("PUBLIC does not apply to CREATE CONTEXT", ParseErrorCodes.InvalidCombination);
        var ifNotExists = TryIfNotExists();
        var name = ParseIdentifier();
        if (!ifNotExists) ifNotExists = TryIfNotExists();
        var st = new CreateContextStatement { OrReplace = orReplace, IfNotExists = ifNotExists, Name = name };
        ExpectKw("USING");
        st.Package = ParseDottedName();
        if (AcceptKw("INITIALIZED"))
        {
            if (AcceptKw("EXTERNALLY")) st.Initialized = ContextInit.Externally;
            else { ExpectKw("GLOBALLY"); st.Initialized = ContextInit.Globally; }
        }
        if (AcceptKw("ACCESSED")) { ExpectKw("GLOBALLY"); st.AccessedGlobally = true; }
        return Fin(st, start);
    }

    // ---- CREATE / ALTER TABLESPACE ----

    private Statement ParseCreateTablespace(Token start, bool orReplace, bool pub, bool? bigfile, TablespaceType? kind)
    {
        if (orReplace || pub) throw Fail("OR REPLACE and PUBLIC do not apply to CREATE TABLESPACE", ParseErrorCodes.InvalidCombination);
        var ifNotExists = TryIfNotExists();
        var name = ParseIdentifier();
        if (!ifNotExists) ifNotExists = TryIfNotExists();
        var st = new CreateTablespaceStatement { Bigfile = bigfile, Kind = kind, IfNotExists = ifNotExists, Name = name };
        int prog = -1;
        while (!AtStatementEnd() && Progress(ref prog)) ParseTablespaceOption(st);
        return Fin(st, start);
    }

    private void ParseTablespaceOption(CreateTablespaceStatement st)
    {
        if (IsKw("DATAFILE") || IsKw("TEMPFILE"))
        {
            bool temp = Next().Value == "TEMPFILE";
            do st.Files.Add(ParseDatafileSpec(temp)); while (AcceptOp(","));
        }
        else if (IsKw("FORCE") && IsKwAt(1, "LOGGING")) { Next(); Next(); st.Logging = true; st.ForceLogging = true; }
        else if (AcceptKw("LOGGING")) st.Logging = true;
        else if (AcceptKw("NOLOGGING")) st.Logging = false;
        else if (AcceptKw("ONLINE")) st.Online = true;
        else if (AcceptKw("OFFLINE")) st.Online = false;
        else if (IsKw("EXTENT") && IsKwAt(1, "MANAGEMENT") && IsKwAt(2, "LOCAL"))
        {
            Next(); Next(); Next();
            st.ExtentManagementLocal = true;
            if (AcceptKw("AUTOALLOCATE")) st.ExtentPolicy = ExtentPolicy.Autoallocate;
            else if (AcceptKw("UNIFORM"))
            {
                st.ExtentPolicy = ExtentPolicy.Uniform;
                if (AcceptKw("SIZE")) st.UniformSize = ParseAdditive();
            }
        }
        else if (IsKw("SEGMENT") && IsKwAt(1, "SPACE") && IsKwAt(2, "MANAGEMENT") && (IsKwAt(3, "AUTO") || IsKwAt(3, "MANUAL")))
        {
            Next(); Next(); Next();
            st.SegmentSpace = Next().Value == "AUTO" ? SegmentSpaceMode.Auto : SegmentSpaceMode.Manual;
        }
        else if (AcceptKw("ENCRYPTION"))
        {
            st.Encryption = true;
            if (AcceptKw("USING")) st.EncryptionUsing = ParseAdditive();
            if (IsKw("DEFAULT") && IsKwAt(1, "STORAGE") && IsOpAt(2, "(") && IsKwAt(3, "ENCRYPT") && IsOpAt(4, ")"))
            {
                for (int i = 0; i < 5; i++) Next();
                st.DefaultStorageEncrypt = true;
            }
        }
        else st.OtherClauses.Add(ParseSyntaxItem());
    }

    /// <summary>'name' [SIZE n] [REUSE] [AUTOEXTEND ...] of a DATAFILE or TEMPFILE clause.</summary>
    private DatafileSpec ParseDatafileSpec(bool tempfile)
    {
        var s = Cur;
        var f = new DatafileSpec { Tempfile = tempfile, Path = Cur.Type == TokenType.String ? ParseAdditive() : null };
        int prog = -1;
        while (Progress(ref prog))
        {
            if (AcceptKw("SIZE")) f.Size = ParseAdditive();
            else if (AcceptKw("REUSE")) f.Reuse = true;
            else if (IsKw("AUTOEXTEND")) ParseAutoextend(f);
            else break;
        }
        return Fin(f, s);
    }

    /// <summary>AUTOEXTEND ON|OFF [NEXT n] [MAXSIZE n | UNLIMITED].</summary>
    private void ParseAutoextend(DatafileSpec f)
    {
        ExpectKw("AUTOEXTEND");
        if (AcceptKw("OFF")) f.Autoextend = false;
        else { ExpectKw("ON"); f.Autoextend = true; }
        if (AcceptKw("NEXT")) f.NextSize = ParseAdditive();
        if (AcceptKw("MAXSIZE"))
        {
            if (AcceptKw("UNLIMITED")) f.MaxSizeUnlimited = true;
            else f.MaxSize = ParseAdditive();
        }
    }

    private Statement ParseAlterTablespace(Token start, bool pub)
    {
        if (pub) throw Fail("PUBLIC does not apply to ALTER TABLESPACE", ParseErrorCodes.InvalidCombination);
        var ifExists = TryIfExists();
        var st = new AlterTablespaceStatement { IfExists = ifExists, Name = ParseIdentifier() };
        int prog = -1;
        while (!AtStatementEnd() && Progress(ref prog)) st.Actions.Add(ParseTablespaceAction());
        return Fin(st, start);
    }

    private TablespaceAction ParseTablespaceAction()
    {
        var s = Cur;
        var a = new TablespaceAction();
        if (IsKw("ADD") && (IsKwAt(1, "DATAFILE") || IsKwAt(1, "TEMPFILE")))
        {
            Next();
            bool temp = Next().Value == "TEMPFILE";
            a.Kind = temp ? TablespaceActionKind.AddTempfile : TablespaceActionKind.AddDatafile;
            do a.Files.Add(ParseDatafileSpec(temp)); while (AcceptOp(","));
        }
        else if ((IsKw("DATAFILE") || IsKw("TEMPFILE")) && PeekTok(1).Type == TokenType.String
                 && (IsKwAt(2, "RESIZE") || IsKwAt(2, "AUTOEXTEND") || IsKwAt(2, "ONLINE") || IsKwAt(2, "OFFLINE")))
        {
            bool temp = Next().Value == "TEMPFILE";
            var t = new DatafileSpec { Tempfile = temp, Path = ParseAdditive() };
            a.Target = t;
            if (AcceptKw("RESIZE")) { a.Kind = TablespaceActionKind.Resize; t.Size = ParseAdditive(); }
            else if (IsKw("AUTOEXTEND")) { a.Kind = TablespaceActionKind.Autoextend; ParseAutoextend(t); }
            else if (AcceptKw("ONLINE")) a.Kind = TablespaceActionKind.Online;
            else { ExpectKw("OFFLINE"); a.Kind = TablespaceActionKind.Offline; }
        }
        else if (AcceptKw("RESIZE")) { a.Kind = TablespaceActionKind.Resize; a.Target = new DatafileSpec { Size = ParseAdditive() }; }
        else if (IsKw("AUTOEXTEND")) { a.Kind = TablespaceActionKind.Autoextend; a.Target = new DatafileSpec(); ParseAutoextend(a.Target); }
        else if (AcceptKw("ONLINE")) a.Kind = TablespaceActionKind.Online;
        else if (AcceptKw("OFFLINE"))
        {
            a.Kind = TablespaceActionKind.Offline;
            if (AcceptKw("NORMAL")) a.Mode = OfflineMode.Normal;
            else if (AcceptKw("TEMPORARY")) a.Mode = OfflineMode.Temporary;
            else if (AcceptKw("IMMEDIATE")) a.Mode = OfflineMode.Immediate;
        }
        else if (IsKw("READ") && (IsKwAt(1, "ONLY") || IsKwAt(1, "WRITE")))
        {
            Next();
            a.Kind = Next().Value == "ONLY" ? TablespaceActionKind.ReadOnly : TablespaceActionKind.ReadWrite;
        }
        else if (AcceptKw("RENAME")) { ExpectKw("TO"); a.Kind = TablespaceActionKind.RenameTo; a.NewName = ParseIdentifier(); }
        else if (AcceptKw("LOGGING")) a.Kind = TablespaceActionKind.Logging;
        else if (AcceptKw("NOLOGGING")) a.Kind = TablespaceActionKind.NoLogging;
        else if (IsKw("BEGIN") && IsKwAt(1, "BACKUP")) { Next(); Next(); a.Kind = TablespaceActionKind.BeginBackup; }
        else if (IsKw("END") && IsKwAt(1, "BACKUP")) { Next(); Next(); a.Kind = TablespaceActionKind.EndBackup; }
        else
        {
            // Not modelled: keep one token so the action order survives a round trip.
            a.Kind = TablespaceActionKind.Other;
            a.Syntax.Add(ParseSyntaxItem());
        }
        return Fin(a, s);
    }

    // ---- AUDIT / NOAUDIT / AUDIT POLICY ----

    private Statement ParseAudit(Token start)
    {
        var st = new AuditStatement { Noaudit = Next().Value == "NOAUDIT" };
        if (AcceptKw("POLICY")) st.Policy = ParseIdentifier();
        else if (AcceptKw("CONTEXT"))
        {
            ExpectKw("NAMESPACE");
            st.ContextNamespace = ParseIdentifier();
            if (AcceptKw("ATTRIBUTES")) st.ContextAttributes = ParseIdentifierList();
        }
        else
        {
            do st.Options.Add(ParseAuditOption()); while (AcceptOp(","));
        }
        ParseAuditTail(st);
        return Fin(st, start);
    }

    /// <summary>The words of one traditional option, up to a comma or ON / BY / WHENEVER.</summary>
    private AuditOption ParseAuditOption()
    {
        var s = Cur;
        var words = new List<string>();
        int prog = -1;
        while (!AtStatementEnd() && !IsOp(",") && !IsKw("ON") && !IsKw("BY") && !IsKw("WHENEVER") && Progress(ref prog))
            words.Add(Next().Value);
        if (words.Count == 0) throw FailExpected($"Expected an audit option but found {Describe(Cur)}", "an audit option");
        return Fin(new AuditOption { Name = string.Join(" ", words) }, s);
    }

    private void ParseAuditTail(AuditStatement st)
    {
        int prog = -1;
        while (!AtStatementEnd() && Progress(ref prog))
        {
            if (AcceptKw("ON")) st.On = ParseAuditObject();
            else if (AcceptKw("EXCEPT")) st.ExceptUsers = ParseIdentifierList();
            else if (AcceptKw("BY"))
            {
                if (AcceptKw("ACCESS")) st.By = AuditBy.Access;
                else if (AcceptKw("SESSION")) st.By = AuditBy.Session;
                else if (IsKw("USERS") && IsKwAt(1, "WITH")) { Next(); Next(); st.GrantedRoles = AcceptKw("GRANTED"); ExpectKw("ROLES"); st.WithRoles = ParseIdentifierList(); }
                else st.ByUsers.AddRange(ParseIdentifierList());
            }
            else if (AcceptKw("WHENEVER"))
            {
                bool negated = AcceptKw("NOT");
                ExpectKw("SUCCESSFUL");
                st.Successful = !negated;
            }
            else throw Fail($"Unexpected {Describe(Cur)} in {(st.Noaudit ? "NOAUDIT" : "AUDIT")}");
        }
    }

    private AuditObject ParseAuditObject()
    {
        var s = Cur;
        var o = new AuditObject();
        if (AcceptKw("DEFAULT")) o.Default = true;
        else
        {
            if (AcceptKw("DIRECTORY")) o.Directory = true;
            o.Name = ParseDottedName();
        }
        return Fin(o, s);
    }

    private Statement ParseCreateAuditPolicy(Token start, bool orReplace, bool pub)
    {
        if (orReplace || pub) throw Fail("OR REPLACE and PUBLIC do not apply to CREATE AUDIT POLICY", ParseErrorCodes.InvalidCombination);
        var ifNotExists = TryIfNotExists();
        var name = ParseIdentifier();
        if (!ifNotExists) ifNotExists = TryIfNotExists();
        var st = new CreateAuditPolicyStatement { IfNotExists = ifNotExists, Name = name };
        int prog = -1;
        while (!AtStatementEnd() && Progress(ref prog))
        {
            if (IsKw("PRIVILEGES") || IsKw("ACTIONS") || IsKw("ROLES")) st.Lists.Add(ParseAuditPolicyList(false));
            else if (AcceptKw("WHEN"))
            {
                var (cond, ev) = ParseConditionEvaluate();
                st.Condition = cond;
                st.Evaluate = ev;
            }
            else if (AcceptKw("ONLY")) { ExpectKw("TOPLEVEL"); st.OnlyToplevel = true; }
            else if (IsKw("CONTAINER") && IsOpAt(1, "=")) st.Container = TryParseContainer();
            else throw Fail($"Unexpected {Describe(Cur)} in CREATE AUDIT POLICY");
        }
        return Fin(st, start);
    }

    private Statement ParseAlterAuditPolicy(Token start, bool pub)
    {
        if (pub) throw Fail("PUBLIC does not apply to ALTER AUDIT POLICY", ParseErrorCodes.InvalidCombination);
        var ifExists = TryIfExists();
        var st = new AlterAuditPolicyStatement { IfExists = ifExists, Name = ParseIdentifier() };
        bool lastDrop = false;
        int prog = -1;
        while (!AtStatementEnd() && Progress(ref prog))
        {
            if (AtAuditPolicyList()) { var list = ParseAuditPolicyList(lastDrop); st.Lists.Add(list); lastDrop = list.Drop; }
            else if (AcceptKw("CONDITION"))
            {
                if (AcceptKw("DROP")) st.DropCondition = true;
                else
                {
                    var (cond, ev) = ParseConditionEvaluate();
                    st.Condition = cond;
                    st.Evaluate = ev;
                }
            }
            else throw Fail($"Unexpected {Describe(Cur)} in ALTER AUDIT POLICY");
        }
        return Fin(st, start);
    }

    /// <summary>ADD|DROP PRIVILEGES/ACTIONS/ROLES, or the bare keyword on CREATE. ADD and DROP are only list starters before one of these words.</summary>
    private bool AtAuditPolicyList()
    {
        if (IsKw("ADD") || IsKw("DROP")) return IsKwAt(1, "PRIVILEGES") || IsKwAt(1, "ACTIONS") || IsKwAt(1, "ROLES");
        return IsKw("PRIVILEGES") || IsKw("ACTIONS") || IsKw("ROLES");
    }

    private AuditPolicyList ParseAuditPolicyList(bool inheritedDrop)
    {
        var s = Cur;
        var l = new AuditPolicyList { Drop = inheritedDrop };
        if (AcceptKw("DROP")) l.Drop = true;
        else if (AcceptKw("ADD")) l.Drop = false;
        if (AcceptKw("PRIVILEGES")) l.Kind = AuditPolicyListKind.Privileges;
        else if (AcceptKw("ACTIONS")) l.Kind = AuditPolicyListKind.Actions;
        else { ExpectKw("ROLES"); l.Kind = AuditPolicyListKind.Roles; }
        if (l.Kind == AuditPolicyListKind.Roles) l.Roles = ParseIdentifierList();
        else do l.Items.Add(ParseAuditPolicyItem()); while (AcceptOp(","));
        return Fin(l, s);
    }

    private AuditPolicyItem ParseAuditPolicyItem()
    {
        var s = Cur;
        var it = new AuditPolicyItem();
        if (IsKw("COMPONENT") && IsOpAt(1, "=")) { Next(); Next(); it.Component = ParseIdentifier(); }
        var words = new List<string>();
        int prog = -1;
        while (!AtAuditPolicyItemEnd() && Progress(ref prog)) words.Add(Next().Value);
        if (words.Count == 0 && it.Component == null) throw FailExpected($"Expected a privilege or action but found {Describe(Cur)}", "a privilege", "action");
        it.Name = string.Join(" ", words);
        if (AcceptKw("ON")) it.On = ParseAuditObject();
        return Fin(it, s);
    }

    private bool AtAuditPolicyItemEnd() =>
        AtStatementEnd() || IsOp(",") || IsKw("ON") || IsKw("WHEN") || IsKw("ONLY") || IsKw("CONTAINER") || IsKw("CONDITION") || AtAuditPolicyList();

    /// <summary>'condition' EVALUATE PER STATEMENT | SESSION | INSTANCE.</summary>
    private (Expression, AuditEvaluation) ParseConditionEvaluate()
    {
        var cond = ParseAdditive();
        ExpectKw("EVALUATE");
        ExpectKw("PER");
        var ev = AcceptKw("STATEMENT") ? AuditEvaluation.Statement
            : AcceptKw("SESSION") ? AuditEvaluation.Session
            : AcceptKw("INSTANCE") ? AuditEvaluation.Instance
            : throw FailExpected($"Expected STATEMENT, SESSION or INSTANCE but found {Describe(Cur)}", "STATEMENT", "SESSION", "INSTANCE");
        return (cond, ev);
    }

    // ---- DROP / ALTER (generic) ----

    private Statement ParseDrop()
    {
        var start = Next();
        var st = new DropStatement { Public = AcceptKw("PUBLIC") };
        if (!TryParseObjectKind(out var kind)) throw Fail($"Unsupported DROP target {Describe(Cur)}", ParseErrorCodes.UnsupportedForm);
        st.Kind = kind;
        RequireKindVersion("DROP", kind, start);
        st.IfExists = TryIfExists();
        if (kind == ObjectKind.MaterializedViewLog) { ExpectKw("ON"); st.OnTable = ParseDottedName(); }
        else if (ObjectKindHasName(kind)) st.Name = ParseDottedName();
        int prog19 = -1;
        while (!AtStatementEnd() && Progress(ref prog19))
        {
            if (IsKw("CASCADE") && IsKwAt(1, "CONSTRAINTS")) { Next(); Next(); st.CascadeConstraints = true; }
            else if (AcceptKw("CASCADE")) st.Cascade = true;
            else if (AcceptKw("PURGE")) st.Purge = true;
            else if (AcceptKw("FORCE")) st.Force = true;
            else if (IsKw("INCLUDING") && IsKwAt(1, "CONTENTS")) { Next(); Next(); st.IncludingContents = true; }
            else if (IsKw("AND") && IsKwAt(1, "DATAFILES")) { Next(); Next(); st.AndDatafiles = true; }
            else if (AcceptKw("VALIDATE")) st.Validate = true;
            else if (AcceptKw("INVALIDATE")) st.Invalidate = true;
            else st.Rest.Add(ParseSyntaxItem());
        }
        return Fin(st, start);
    }

    private Statement ParseAlter()
    {
        var start = Next();
        var pub = AcceptKw("PUBLIC");
        if (!TryParseObjectKind(out var kind)) kind = ObjectKind.Other;
        RequireKindVersion("ALTER", kind, start);
        if (kind == ObjectKind.Table) return ParseAlterTable(start);
        if (kind == ObjectKind.Session) return ParseAlterSession(start);
        if (kind == ObjectKind.User) return ParseAlterUser(start, pub);
        if (kind == ObjectKind.Role) return ParseAlterRole(start, pub);
        if (kind == ObjectKind.Profile) return ParseAlterProfile(start, pub);
        if (kind == ObjectKind.Tablespace) return ParseAlterTablespace(start, pub);
        if (kind == ObjectKind.AuditPolicy) return ParseAlterAuditPolicy(start, pub);
        if (kind == ObjectKind.Domain) return ParseAlterDomain(start);
        if (kind is ObjectKind.View or ObjectKind.MaterializedView or ObjectKind.Index && TryParseAlterAnnotations(start, kind, out var annotated)) return annotated;
        // typed forms rewind to the generic node when a clause is not modeled, and never apply to ALTER ... IF EXISTS
        if (!pub && !(IsKw("IF") && IsKwAt(1, "EXISTS")) && TryParseTypedAlter(start, kind) is { } typed) return typed;
        var st = new AlterObjectStatement { Kind = kind, Public = pub };
        st.IfExists = TryIfExists();
        if (ObjectKindHasName(kind) && Cur.Type is TokenType.Identifier or TokenType.QuotedIdentifier) st.Name = ParseDottedName();
        st.Body = ParseSyntaxItems(AtStatementEnd);
        return Fin(st, start);
    }

    private Statement ParseAlterSession(Token start)
    {
        var st = new AlterSessionStatement();
        if (AcceptKw("SET"))
        {
            st.Kind = AlterSessionKind.Set;
            int prog20 = -1;
            while (!AtStatementEnd() && Progress(ref prog20))
            {
                var s = Cur;
                var name = ParseDottedName();
                ExpectOp("=");
                var ss = new SessionSetting { Name = name, Value = ParseExpr() };
                st.Settings.Add(Fin(ss, s));
            }
        }
        else if (IsKw("ENABLE") || IsKw("DISABLE") || IsKw("FORCE"))
        {
            var verb = Next().Value;
            st.Kind = verb == "ENABLE" ? AlterSessionKind.Enable : verb == "DISABLE" ? AlterSessionKind.Disable : AlterSessionKind.Force;
            if (IsKw("COMMIT") && IsKwAt(1, "IN")) { Next(); Next(); ExpectKw("PROCEDURE"); st.Feature = SessionFeature.CommitInProcedure; }
            else if (AcceptKw("GUARD")) st.Feature = SessionFeature.Guard;
            else if (AcceptKw("PARALLEL"))
            {
                st.Feature = AcceptKw("DML") ? SessionFeature.ParallelDml : AcceptKw("DDL") ? SessionFeature.ParallelDdl : AcceptKw("QUERY") ? SessionFeature.ParallelQuery : throw FailExpected("Expected DML, DDL or QUERY", "DML", "DDL", "QUERY");
                if (AcceptKw("PARALLEL")) st.Parallel = ParseAdditive();
            }
            else if (AcceptKw("RESUMABLE"))
            {
                st.Feature = SessionFeature.Resumable;
                int prog21 = -1;
                while (!AtStatementEnd() && Progress(ref prog21))
                {
                    if (AcceptKw("TIMEOUT")) st.Timeout = ParseAdditive();
                    else if (AcceptKw("NAME")) st.ResumableName = ParseAdditive();
                    else break;
                }
            }
            else { st.Feature = SessionFeature.Other; st.FeatureSyntax = ParseSyntaxItems(AtStatementEnd); }
        }
        else if (IsKw("CLOSE") && IsKwAt(1, "DATABASE")) { Next(); Next(); ExpectKw("LINK"); st.Kind = AlterSessionKind.CloseDatabaseLink; st.DatabaseLink = ParseIdentifier(); }
        else if (AcceptKw("ADVISE"))
        {
            st.Kind = AlterSessionKind.Advise;
            st.Advise = AcceptKw("COMMIT") ? AdviseMode.Commit : AcceptKw("ROLLBACK") ? AdviseMode.Rollback : (AcceptKw("NOTHING") ? AdviseMode.Nothing : throw FailExpected("Expected COMMIT, ROLLBACK or NOTHING", "COMMIT", "ROLLBACK", "NOTHING"));
        }
        else if (IsKw("SYNC")) { Next(); ExpectKw("WITH"); ExpectKw("PRIMARY"); st.Kind = AlterSessionKind.SyncWithPrimary; }
        else throw Fail($"Unexpected {Describe(Cur)} in ALTER SESSION");
        return Fin(st, start);
    }

    private Statement ParseCreate()
    {
        var start = Next();
        bool orReplace = false, unique = false, bitmap = false, pub = false, andResolve = false, andCompile = false, noForce = false;
        bool force = false, editioning = false, gtt = false, ptt = false, dup = false, imm = false, blk = false, useCase = false, shared = false;
        bool? bigfile = null;
        TablespaceType? tsType = null;
        EditionableKind? editionable = null;
        int prog22 = -1;
        while (Progress(ref prog22))
        {
            if (IsKw("OR") && IsKwAt(1, "REPLACE")) { Next(); Next(); orReplace = true; }
            else if (IsKw("EDITIONABLE")) { Next(); editionable = EditionableKind.Editionable; }
            else if (IsKw("NONEDITIONABLE")) { Next(); editionable = EditionableKind.NonEditionable; }
            else if (IsKw("EDITIONING")) { Next(); editioning = true; }
            else if (IsKw("FORCE")) { Next(); force = true; }
            else if (IsKw("NOFORCE")) { Next(); noForce = true; }
            else if (IsKw("GLOBAL") && IsKwAt(1, "TEMPORARY")) { Next(); Next(); gtt = true; }
            else if (IsKw("PRIVATE") && IsKwAt(1, "TEMPORARY")) { Next(); Next(); ptt = true; }
            else if (IsKw("DUPLICATED")) { Next(); dup = true; }
            else if (IsKw("IMMUTABLE")) { Next(); imm = true; }
            else if (IsKw("BLOCKCHAIN")) { Next(); blk = true; }
            else if (IsKw("USECASE")) { Next(); useCase = true; }
            else if (IsKw("AND") && (IsKwAt(1, "RESOLVE") || IsKwAt(1, "COMPILE"))) { Next(); if (Next().Value == "RESOLVE") andResolve = true; else andCompile = true; }
            else if (IsKw("UNIQUE")) { Next(); unique = true; }
            else if (IsKw("BITMAP")) { Next(); bitmap = true; }
            else if (IsKw("SHARED") && (IsKwAt(1, "PUBLIC") || IsKwAt(1, "DATABASE"))) { Next(); shared = true; }
            else if ((IsKw("BIGFILE") || IsKw("SMALLFILE")) && (IsKwAt(1, "TABLESPACE") || IsKwAt(1, "TEMPORARY") || IsKwAt(1, "UNDO"))) { bigfile = Next().Value == "BIGFILE"; }
            else if (IsKw("TEMPORARY") && IsKwAt(1, "TABLESPACE")) { Next(); tsType = TablespaceType.Temporary; }
            else if (IsKw("UNDO") && IsKwAt(1, "TABLESPACE")) { Next(); tsType = TablespaceType.Undo; }
            else if (IsKw("PUBLIC")) { Next(); pub = true; }
            else break;
        }

        int kindPos = _p;
        if (IsKw("JAVA"))
        {
            Next();
            return ParseCreateJava(start, orReplace, andResolve, andCompile, noForce);
        }
        TryParseObjectKind(out var kind);
        RequireKindVersion("CREATE", kind, start);
        switch (kind)
        {
            case ObjectKind.AuditPolicy: return ParseCreateAuditPolicy(start, orReplace, pub);
            case ObjectKind.Tablespace: return ParseCreateTablespace(start, orReplace, pub, bigfile, tsType);
            case ObjectKind.DatabaseLink: return ParseCreateDatabaseLink(start, orReplace, pub, shared);
            case ObjectKind.Directory: return ParseCreateDirectory(start, orReplace, pub);
            case ObjectKind.Context: return ParseCreateContext(start, orReplace, pub);
            case ObjectKind.Profile: return ParseCreateProfile(start, orReplace, pub);
            case ObjectKind.Role: return ParseCreateRole(start, orReplace, pub);
            case ObjectKind.User: return ParseCreateUser(start, orReplace, pub);
            case ObjectKind.Table: return ParseCreateTable(start, orReplace, gtt, ptt, dup, imm, blk);
            case ObjectKind.Index: return ParseCreateIndex(start, unique, bitmap);
            case ObjectKind.View: return ParseCreateView(start, orReplace, force, noForce, editioning, false);
            case ObjectKind.MaterializedView: return ParseCreateView(start, orReplace, force, noForce, editioning, true);
            case ObjectKind.Sequence: return ParseCreateSequence(start);
            case ObjectKind.PropertyGraph: return ParseCreatePropertyGraph(start, orReplace);
            case ObjectKind.JsonRelationalDualityView: return ParseCreateDualityView(start, orReplace, _t[kindPos + 1].Value == "RELATIONAL");
            case ObjectKind.Domain: return ParseCreateDomain(start, orReplace, useCase);
            case ObjectKind.Synonym: return ParseCreateSynonym(start, orReplace, pub);
            case ObjectKind.Procedure: case ObjectKind.Function:
            {
                _p = kindPos;
                var sub = ParseSubprogram();
                return Fin(new CreateProgramStatement { OrReplace = orReplace, Editionable = editionable, Subprogram = sub }, start);
            }
            case ObjectKind.Package: return ParseCreatePackage(start, orReplace, editionable, false);
            case ObjectKind.PackageBody: return ParseCreatePackage(start, orReplace, editionable, true);
            case ObjectKind.Trigger: return ParseCreateTrigger(start, orReplace, editionable);
            case ObjectKind.Type: return ParseCreateType(start, orReplace, editionable, false);
            case ObjectKind.TypeBody: return ParseCreateType(start, orReplace, editionable, true);
            case ObjectKind.Library: return ParseCreateLibrary(start, orReplace, editionable);
            case ObjectKind.JavaSource: case ObjectKind.JavaClass: case ObjectKind.JavaResource:
                _p = kindPos;
                Next(); // JAVA
                return ParseCreateJava(start, orReplace, andResolve, andCompile, noForce, true);
        }
        var st = new CreateObjectStatement { Kind = kind, OrReplace = orReplace, Public = pub };
        st.IfNotExists = TryIfNotExists();
        if (ObjectKindHasName(kind) && Cur.Type is TokenType.Identifier or TokenType.QuotedIdentifier) st.Name = ParseDottedName();
        if (!st.IfNotExists) st.IfNotExists = TryIfNotExists();
        st.Body = ParseSyntaxItems(AtStatementEnd);
        return Fin(st, start);
    }

    private SharingKind? TryParseSharing()
    {
        if (!(IsKw("SHARING") && IsOpAt(1, "="))) return null;
        Next(); Next();
        if (AcceptKw("METADATA")) return SharingKind.Metadata;
        if (AcceptKw("DATA")) return SharingKind.Data;
        if (AcceptKw("EXTENDED")) { ExpectKw("DATA"); return SharingKind.Extended; }
        ExpectKw("NONE");
        return SharingKind.None;
    }

    // ---- CREATE TABLE ----

    private bool AtAsQuery() => IsKw("AS") && (IsKwAt(1, "SELECT") || IsKwAt(1, "WITH") || IsOpAt(1, "("));

    private Statement ParseCreateTable(Token start, bool orReplace, bool gtt, bool ptt, bool dup, bool imm, bool blk)
    {
        if (imm) RequireVersion(OracleVersion.Oracle21, "IMMUTABLE table", start);
        if (blk) RequireVersion(OracleVersion.Oracle21, "BLOCKCHAIN table", start);
        var st = new CreateTableStatement { OrReplace = orReplace, GlobalTemporary = gtt, PrivateTemporary = ptt, Duplicated = dup, Immutable = imm, Blockchain = blk };
        st.IfNotExists = TryIfNotExists();
        st.Name = ParseDottedName();
        st.Sharing = TryParseSharing();
        if (AcceptKw("OF")) st.OfType = ParseDottedName();
        if (IsOp("(") && !LooksLikeQueryAfterParen())
        {
            Next();
            do
            {
                if (Cur.Type == TokenType.Identifier && ConstraintStarts.Contains(Cur.Value)) st.Constraints.Add(ParseConstraint(false));
                else st.Columns.Add(ParseColumnDefinition());
            } while (AcceptOp(","));
            ExpectOp(")");
        }
        ParseOnCommit(st);
        st.Attributes = ParsePhysicalAttributes(() => AtAsQuery() || (IsKw("ON") && IsKwAt(1, "COMMIT")));
        ParseOnCommit(st);
        if (!AtAsQuery() && IsKw("ON")) st.Attributes.AddRange(ParsePhysicalAttributes(AtAsQuery));
        if (AtAsQuery()) { Next(); st.AsQuery = ParseSelectStatement(); }
        return Fin(st, start);
    }

    private void ParseOnCommit(CreateTableStatement st)
    {
        if (!(IsKw("ON") && IsKwAt(1, "COMMIT"))) return;
        Next(); Next();
        if (AcceptKw("PRESERVE")) st.OnCommit = AcceptKw("DEFINITION") ? OnCommitAction.PreserveDefinition : OnCommitAction.PreserveRows;
        else if (AcceptKw("DELETE")) st.OnCommit = OnCommitAction.DeleteRows;
        else if (AcceptKw("DROP")) st.OnCommit = OnCommitAction.DropDefinition;
        else throw FailExpected($"Expected PRESERVE, DELETE or DROP but found {Describe(Cur)}", "PRESERVE", "DELETE", "DROP");
        if (st.OnCommit is OnCommitAction.PreserveRows or OnCommitAction.DeleteRows) ExpectKw("ROWS");
        else if (st.OnCommit == OnCommitAction.DropDefinition) ExpectKw("DEFINITION");
    }

    private static readonly HashSet<string> ColumnClauseWords = new(StringComparer.Ordinal)
    {
        "DEFAULT","GENERATED","CONSTRAINT","NOT","NULL","PRIMARY","UNIQUE","REFERENCES","CHECK","AS","VISIBLE",
        "INVISIBLE","ENCRYPT","COLLATE","SORT","DROP","SET","RENAME","ADD","MODIFY","DOMAIN","ANNOTATIONS",
    };

    /// <summary>A view column: name [ANNOTATIONS (...)].</summary>
    private ViewColumn ParseViewColumn(bool allowDrop)
    {
        var s = Cur;
        var vc = new ViewColumn { Name = ParseIdentifier() };
        if (AcceptKw("ANNOTATIONS")) vc.Annotations = ParseAnnotations(allowDrop);
        return Fin(vc, s);
    }

    private ColumnDefinition ParseColumnDefinition(Func<bool>? stop = null, bool alter = false)
    {
        var s = Cur;
        var cd = new ColumnDefinition { Name = ParseIdentifier() };
        if (Cur.Type == TokenType.Identifier && !ColumnClauseWords.Contains(Cur.Value) && !AtStatementEnd())
            cd.Type = ParseDataType();
        int prog130 = -1;
        while (!IsOp(",") && !IsOp(")") && !AtStatementEnd() && !(stop?.Invoke() ?? false) && Progress(ref prog130))
        {
            if (cd.Annotations != null) throw Fail("A column ANNOTATIONS clause must be the last column property (ORA-03099)", ParseErrorCodes.InvalidCombination);
            if (AcceptKw("ANNOTATIONS")) { cd.Annotations = ParseAnnotations(allowDrop: alter); continue; }
            if (IsKw("DEFAULT"))
            {
                Next();
                if (IsKw("ON") && IsKwAt(1, "NULL"))
                {
                    Next(); Next(); cd.DefaultOnNull = true;
                    if (AcceptKw("FOR"))
                    {
                        ExpectKw("INSERT");
                        // Plain DEFAULT ON NULL is 12c; only the FOR INSERT ... qualifiers are 23ai.
                        RequireVersion(OracleVersion.Oracle23, "DEFAULT ON NULL FOR", Prev);
                        if (AcceptKw("AND")) { ExpectKw("UPDATE"); cd.DefaultOnNullFor = OnNullScope.InsertAndUpdate; }
                        else { AcceptKw("ONLY"); cd.DefaultOnNullFor = OnNullScope.InsertOnly; }
                    }
                }
                cd.Default = ParseExpr();
            }
            else if (IsKw("GENERATED"))
            {
                var gs = Next();
                var idn = new IdentityClause { Mode = IdentityMode.Always };
                bool always = AcceptKw("ALWAYS");
                if (!always && IsKw("BY")) { Next(); ExpectKw("DEFAULT"); idn.Mode = IdentityMode.ByDefault; if (IsKw("ON") && IsKwAt(1, "NULL")) { Next(); Next(); idn.OnNull = true; } }
                ExpectKw("AS");
                if (AcceptKw("IDENTITY"))
                {
                    if (AcceptOp("(")) { idn.Options = ParseSequenceOptions(() => IsOp(")")); ExpectOp(")"); }
                    cd.Identity = Fin(idn, gs);
                }
                else
                {
                    ExpectOp("(");
                    cd.VirtualExpression = ParseExpr();
                    ExpectOp(")");
                    AcceptKw("VIRTUAL");
                }
            }
            else if (IsKw("AS") && IsOpAt(1, "("))
            {
                Next(); Next();
                cd.VirtualExpression = ParseExpr();
                ExpectOp(")");
                AcceptKw("VIRTUAL");
            }
            else if (AcceptKw("VISIBLE")) cd.Visible = true;
            else if (AcceptKw("INVISIBLE")) cd.Visible = false;
            else if (AcceptKw("COLLATE")) cd.Collation = ParseIdentifier();
            else if (AcceptVersioned("DOMAIN", OracleVersion.Oracle23, "column DOMAIN")) cd.Domain = ParseDottedName();
            else if (AcceptKw("SORT")) cd.Sort = true;
            else if (IsKw("ENCRYPT"))
            {
                var es = Next();
                var ec = new EncryptClause();
                int prog23 = -1;
                while (Progress(ref prog23))
                {
                    if (AcceptKw("USING")) ec.Algorithm = ParsePrimary();
                    else if (AcceptKw("IDENTIFIED")) { ExpectKw("BY"); ec.IdentifiedBy = ParsePrimary(); }
                    else if (IsKw("NO") && IsKwAt(1, "SALT")) { Next(); Next(); ec.NoSalt = true; }
                    else if (AcceptKw("SALT")) ec.Salt = true;
                    else break;
                }
                cd.Encrypt = Fin(ec, es);
            }
            else if (IsKw("CONSTRAINT") || IsKw("PRIMARY") || IsKw("UNIQUE") || IsKw("REFERENCES") || IsKw("CHECK") ||
                     (IsKw("NOT") && IsKwAt(1, "NULL")) || IsKw("NULL"))
                cd.Constraints.Add(ParseConstraint(true));
            else cd.OtherProperties.Add(ParseSyntaxItem());
        }
        return Fin(cd, s);
    }

    private static readonly HashSet<string> ConstraintStarts = new(StringComparer.Ordinal)
        { "CONSTRAINT", "PRIMARY", "UNIQUE", "FOREIGN", "CHECK" };

    private static readonly HashSet<string> ConstraintStateWords = new(StringComparer.Ordinal)
        { "DEFERRABLE","INITIALLY","ENABLE","DISABLE","VALIDATE","NOVALIDATE","RELY","NORELY","USING","EXCEPTIONS" };

    private TableConstraint ParseConstraint(bool inline)
    {
        var s = Cur;
        var tc = new TableConstraint();
        if (AcceptKw("CONSTRAINT")) tc.Name = ParseIdentifier();
        if (IsKw("NOT") && IsKwAt(1, "NULL")) { Next(); Next(); tc.Kind = ConstraintKind.NotNull; }
        else if (AcceptKw("NULL")) tc.Kind = ConstraintKind.Null;
        else if (IsKw("PRIMARY")) { Next(); ExpectKw("KEY"); tc.Kind = ConstraintKind.PrimaryKey; if (!inline) ParseConstraintColumns(tc); }
        else if (AcceptKw("UNIQUE")) { tc.Kind = ConstraintKind.Unique; if (!inline || IsOp("(")) ParseConstraintColumns(tc); }
        else if (IsKw("FOREIGN")) { Next(); ExpectKw("KEY"); tc.Kind = ConstraintKind.ForeignKey; ParseConstraintColumns(tc); ParseReferences(tc); }
        else if (IsKw("REFERENCES")) { tc.Kind = ConstraintKind.ForeignKey; ParseReferences(tc); }
        else if (AcceptKw("CHECK"))
        {
            tc.Kind = ConstraintKind.Check;
            ExpectOp("(");
            tc.Check = ParseExpr();
            ExpectOp(")");
        }
        else throw FailExpected($"Expected constraint definition but found {Describe(Cur)}", "constraint definition");
        tc.State = ParseConstraintState();
        return Fin(tc, s);
    }

    private ConstraintState? ParseConstraintState()
    {
        if (!(Cur.Type == TokenType.Identifier && (ConstraintStateWords.Contains(Cur.Value) || (IsKw("NOT") && IsKwAt(1, "DEFERRABLE"))))) return null;
        var s = Cur;
        var cs = new ConstraintState();
        int prog24 = -1;
        while (Cur.Type == TokenType.Identifier && Progress(ref prog24))
        {
            if (IsKw("NOT") && IsKwAt(1, "DEFERRABLE")) { Next(); Next(); cs.Deferrable = false; }
            else if (AcceptKw("DEFERRABLE")) cs.Deferrable = true;
            else if (IsKw("INITIALLY")) { Next(); cs.InitiallyDeferred = AcceptKw("DEFERRED") ? true : (AcceptKw("IMMEDIATE") ? false : throw FailExpected("Expected DEFERRED or IMMEDIATE", "DEFERRED", "IMMEDIATE")); }
            else if (AcceptKw("ENABLE")) cs.Enable = true;
            else if (AcceptKw("DISABLE")) cs.Enable = false;
            else if (AcceptKw("VALIDATE")) cs.Validate = true;
            else if (AcceptKw("NOVALIDATE")) cs.Validate = false;
            else if (AcceptKw("RELY")) cs.Rely = true;
            else if (AcceptKw("NORELY")) cs.Rely = false;
            else if (IsKw("USING") && IsKwAt(1, "INDEX")) { var us = Next(); Next(); cs.UsingIndex = ParseUsingIndex(us); }
            else if (IsKw("EXCEPTIONS") && IsKwAt(1, "INTO")) { Next(); Next(); cs.ExceptionsInto = ParseDottedName(); }
            else break;
        }
        return Fin(cs, s);
    }

    private UsingIndexClause ParseUsingIndex(Token s)
    {
        var ui = new UsingIndexClause();
        if (IsOp("(") && IsKwAt(1, "CREATE"))
        {
            Next();
            ui.Create = (CreateIndexStatement)ParseCreate();
            ExpectOp(")");
        }
        else if (Cur.Type is TokenType.Identifier or TokenType.QuotedIdentifier && !AttributeStarts.Contains(Cur.Value) && !ConstraintStateWords.Contains(Cur.Value) && !ColumnClauseWords.Contains(Cur.Value))
            ui.IndexName = ParseDottedName();
        else
            ui.Attributes = ParsePhysicalAttributes(() => IsOp(",") || (Cur.Type == TokenType.Identifier && ConstraintStateWords.Contains(Cur.Value) && Cur.Value != "ENABLE" && Cur.Value != "DISABLE") || (IsKw("ENABLE") && !IsKwAt(1, "ROW")) || (IsKw("DISABLE") && !IsKwAt(1, "ROW")) || (IsKw("NOT") && IsKwAt(1, "DEFERRABLE")));
        return Fin(ui, s);
    }

    private void ParseConstraintColumns(TableConstraint tc)
    {
        ExpectOp("(");
        tc.Columns = ParseIdentifierList();
        ExpectOp(")");
    }

    private void ParseReferences(TableConstraint tc)
    {
        ExpectKw("REFERENCES");
        tc.RefTable = ParseDottedName();
        if (AcceptOp("(")) { tc.RefColumns = ParseIdentifierList(); ExpectOp(")"); }
        if (IsKw("ON") && IsKwAt(1, "DELETE"))
        {
            Next(); Next();
            if (AcceptKw("CASCADE")) tc.OnDelete = ReferentialAction.Cascade;
            else { ExpectKw("SET"); ExpectKw("NULL"); tc.OnDelete = ReferentialAction.SetNull; }
        }
    }

    // ---- CREATE INDEX / VIEW / SEQUENCE / SYNONYM ----

    private Statement ParseCreateIndex(Token start, bool unique, bool bitmap)
    {
        var st = new CreateIndexStatement { Unique = unique, Bitmap = bitmap };
        st.IfNotExists = TryIfNotExists();
        st.Name = ParseDottedName();
        ExpectKw("ON");
        if (AcceptKw("CLUSTER")) st.Cluster = ParseDottedName();
        else
        {
            st.Table = ParseDottedName();
            if (Cur.Type is TokenType.Identifier or TokenType.QuotedIdentifier && !IsKw("INDEXTYPE") && !ClauseWords.Contains(Cur.Value)) st.TableAlias = ParseIdentifier();
        }
        if (IsOp("("))
        {
            Next();
            do
            {
                var s = Cur;
                var ic = new IndexColumn { Expression = ParseExpr() };
                if (AcceptKw("DESC")) ic.Descending = true; else AcceptKw("ASC");
                st.Columns.Add(Fin(ic, s));
            } while (AcceptOp(","));
            ExpectOp(")");
        }
        int prog25 = -1;
        while (!AtStatementEnd() && Progress(ref prog25))
        {
            if (IsKw("INDEXTYPE")) { Next(); ExpectKw("IS"); st.Indextype = ParseDottedName(); if (AcceptKw("PARAMETERS")) { ExpectOp("("); st.IndextypeParameters = ParseExpr(); ExpectOp(")"); } }
            else if (AcceptKw("VISIBLE")) st.Visible = true;
            else if (AcceptKw("INVISIBLE")) st.Visible = false;
            else if (AcceptKw("ONLINE")) st.Online = true;
            else if (AcceptKw("REVERSE")) st.Reverse = true;
            else if (AcceptKw("LOCAL")) st.Local = true;
            else if (AcceptKw("GLOBAL")) st.Global = true;
            else st.Attributes.Add(ParsePhysicalAttribute(() => IsKw("INDEXTYPE") || IsKw("VISIBLE") || IsKw("INVISIBLE") || IsKw("ONLINE") || IsKw("REVERSE")));
        }
        return Fin(st, start);
    }

    private Statement ParseCreateView(Token start, bool orReplace, bool force, bool noForce, bool editioning, bool materialized)
    {
        // Query is required and is parsed last, so the options are collected in locals and the node is built at the end.
        var ifNotExists = TryIfNotExists();
        var name = ParseDottedName();
        var sharing = TryParseSharing();
        var columns = new List<ViewColumn>();
        ViewBequeath? bequeath = null;
        bool? buildImmediate = null;
        RefreshClause? refresh = null;
        bool? queryRewrite = null;
        var attributes = new List<PhysicalAttribute>();
        if (IsOp("(") && !LooksLikeQueryAfterParen())
        {
            Next();
            do columns.Add(ParseViewColumn(allowDrop: false)); while (AcceptOp(","));
            ExpectOp(")");
        }
        if (IsKw("BEQUEATH")) { Next(); bequeath = AcceptKw("CURRENT_USER") ? ViewBequeath.CurrentUser : (AcceptKw("DEFINER") ? ViewBequeath.Definer : throw FailExpected("Expected CURRENT_USER or DEFINER", "CURRENT_USER", "DEFINER")); }
        int prog26 = -1;
        while (!AtAsQuery() && Progress(ref prog26))
        {
            if (AtStatementEnd()) throw FailExpected($"Expected AS but found {Describe(Cur)}", "AS");
            if (IsKw("BUILD")) { Next(); buildImmediate = AcceptKw("IMMEDIATE") ? true : (AcceptKw("DEFERRED") ? false : throw FailExpected("Expected IMMEDIATE or DEFERRED", "IMMEDIATE", "DEFERRED")); }
            else if (IsKw("REFRESH") || (IsKw("NEVER") && IsKwAt(1, "REFRESH"))) refresh = ParseRefresh();
            else if ((IsKw("ENABLE") || IsKw("DISABLE")) && IsKwAt(1, "QUERY")) { queryRewrite = Next().Value == "ENABLE"; Next(); ExpectKw("REWRITE"); }
            else attributes.Add(ParsePhysicalAttribute(() => AtAsQuery() || IsKw("BUILD") || IsKw("REFRESH") || IsKw("NEVER") || ((IsKw("ENABLE") || IsKw("DISABLE")) && IsKwAt(1, "QUERY"))));
        }
        Next(); // AS
        var query = ParseSelectStatement();
        bool readOnly = false, checkOption = false;
        Identifier? checkOptionConstraint = null;
        if (IsKw("WITH") && IsKwAt(1, "READ")) { Next(); Next(); ExpectKw("ONLY"); readOnly = true; }
        else if (IsKw("WITH") && IsKwAt(1, "CHECK"))
        {
            Next(); Next(); ExpectKw("OPTION"); checkOption = true;
            if (AcceptKw("CONSTRAINT")) checkOptionConstraint = ParseIdentifier();
        }
        var st = new CreateViewStatement
        {
            OrReplace = orReplace, Force = force, NoForce = noForce, Editioning = editioning, Materialized = materialized,
            IfNotExists = ifNotExists, Name = name, Sharing = sharing, Columns = columns, Bequeath = bequeath,
            BuildImmediate = buildImmediate, Refresh = refresh, QueryRewrite = queryRewrite, Attributes = attributes,
            Query = query, ReadOnly = readOnly, CheckOption = checkOption, CheckOptionConstraint = checkOptionConstraint,
        };
        return Fin(st, start);
    }

    private RefreshClause ParseRefresh()
    {
        var s = Cur;
        var rc = new RefreshClause();
        if (IsKw("NEVER")) { Next(); Next(); rc.Method = RefreshMethod.Never; return Fin(rc, s); }
        Next(); // REFRESH
        int prog27 = -1;
        while (Progress(ref prog27))
        {
            if (AcceptKw("FAST")) rc.Method = RefreshMethod.Fast;
            else if (AcceptKw("COMPLETE")) rc.Method = RefreshMethod.Complete;
            else if (AcceptKw("FORCE")) rc.Method = RefreshMethod.Force;
            else if (IsKw("ON") && IsKwAt(1, "DEMAND")) { Next(); Next(); rc.Mode = RefreshMode.OnDemand; }
            else if (IsKw("ON") && IsKwAt(1, "COMMIT")) { Next(); Next(); rc.Mode = RefreshMode.OnCommit; }
            else if (IsKw("ON") && IsKwAt(1, "STATEMENT")) { Next(); Next(); rc.Mode = RefreshMode.OnStatement; }
            else if (IsKw("START") && IsKwAt(1, "WITH")) { Next(); Next(); rc.StartWith = ParseAdditive(); }
            else if (AcceptKw("NEXT")) rc.Next = ParseAdditive();
            else if (IsKw("WITH") && IsKwAt(1, "PRIMARY")) { Next(); Next(); ExpectKw("KEY"); rc.WithPrimaryKey = true; }
            else if (IsKw("WITH") && IsKwAt(1, "ROWID")) { Next(); Next(); rc.WithRowId = true; }
            else break;
        }
        return Fin(rc, s);
    }

    private List<SequenceOption> ParseSequenceOptions(Func<bool> stop)
    {
        var list = new List<SequenceOption>();
        int prog131 = -1;
        while (!AtStatementEnd() && !stop() && Progress(ref prog131))
        {
            var s = Cur;
            if (Cur.Type != TokenType.Identifier) throw Fail($"Unexpected {Describe(Cur)} in sequence options");
            var opt = new SequenceOption { Name = Next().Value };
            if (opt.Name == "START") { ExpectKw("WITH"); opt.Name = "START WITH"; if (IsKw("LIMIT") && IsKwAt(1, "VALUE")) { Next(); Next(); opt.Name = "START WITH LIMIT VALUE"; } else opt.Value = ParseExpr(); }
            else if (opt.Name == "INCREMENT") { ExpectKw("BY"); opt.Name = "INCREMENT BY"; opt.Value = ParseExpr(); }
            else if (opt.Name is "MAXVALUE" or "MINVALUE" or "CACHE") opt.Value = ParseExpr();
            else if (IsOp("=")) { Next(); opt.Value = ParseExpr(); }
            list.Add(Fin(opt, s));
        }
        return list;
    }

    private Statement ParseCreateDomain(Token start, bool orReplace, bool useCase)
    {
        // Oracle rejects OR REPLACE on domains (ORA-00922).
        if (orReplace) throw Fail("CREATE OR REPLACE is not supported for DOMAIN", ParseErrorCodes.InvalidCombination);
        var st = new CreateDomainStatement { UseCase = useCase, IfNotExists = TryIfNotExists() };
        st.Name = ParseDottedName();
        ExpectKw("AS");
        st.Type = ParseDataType();
        int prog = -1;
        while (!AtStatementEnd() && Progress(ref prog))
        {
            if (AcceptKw("DEFAULT")) st.Default = ParseExpr();
            else if (IsKw("CONSTRAINT") && IsKwAt(2, "NOT")) throw Fail("A NOT NULL constraint on a domain cannot be named (ORA-02253)", ParseErrorCodes.InvalidCombination);
            else if (IsKw("CONSTRAINT") || IsKw("NOT") || IsKw("NULL") || IsKw("CHECK")) st.Constraints.Add(ParseConstraint(true));
            else if (AcceptKw("COLLATE")) st.Collation = ParseIdentifier();
            else if (AcceptKw("DISPLAY")) st.Display = ParseExpr();
            else if (AcceptKw("ORDER")) st.Order = ParseExpr();
            else if (AcceptKw("ANNOTATIONS")) st.Annotations = ParseAnnotations(allowDrop: false);
            else throw Fail($"Unexpected {Describe(Cur)} in CREATE DOMAIN");
        }
        return Fin(st, start);
    }

    /// <summary>ANNOTATIONS ( item [, item]... ); the keyword has been consumed.</summary>
    private AnnotationsClause ParseAnnotations(bool allowDrop)
    {
        // Shared by every use site (domains, tables, columns, views, indexes, duality views): gated here once.
        RequireVersion(OracleVersion.Oracle23, "ANNOTATIONS", Prev);
        var c = new AnnotationsClause();
        ExpectOp("(");
        var names = new HashSet<string>(StringComparer.Ordinal);
        do
        {
            var item = ParseAnnotationItem(allowDrop);
            // Oracle rejects the same annotation name twice in one clause (ORA-11562).
            if (!names.Add(item.Name.Value)) throw Fail($"Duplicate annotation {item.Name.Value} in the ANNOTATIONS clause", ParseErrorCodes.InvalidCombination);
            c.Items.Add(item);
        } while (AcceptOp(","));
        ExpectOp(")");
        return c;
    }

    private AnnotationItem ParseAnnotationItem(bool allowDrop)
    {
        var s = Cur;
        AnnotationAction? action = null;
        bool ifNotExists = false, ifExists = false;
        if (AcceptKw("ADD"))
        {
            action = AnnotationAction.Add;
            if (IsKw("IF") && IsKwAt(1, "NOT") && IsKwAt(2, "EXISTS")) { Next(); Next(); Next(); ifNotExists = true; }
        }
        else if (IsKw("DROP"))
        {
            if (!allowDrop) throw Fail("DROP is not allowed in an annotation list of a CREATE statement (ORA-11555)", ParseErrorCodes.InvalidCombination);
            Next();
            action = AnnotationAction.Drop;
            if (IsKw("IF") && IsKwAt(1, "EXISTS")) { Next(); Next(); ifExists = true; }
        }
        var name = ParseIdentifier();
        // A DROP item names an existing annotation only; other items may carry a value.
        Expression? value = null;
        if (action != AnnotationAction.Drop && !IsOp(",") && !IsOp(")")) value = ParseExpr();
        var it = new AnnotationItem { Action = action, IfNotExists = ifNotExists, IfExists = ifExists, Name = name, Value = value };
        return Fin(it, s);
    }

    /// <summary>
    /// ALTER VIEW/MATERIALIZED VIEW/INDEX ... ANNOTATIONS (...), and MODIFY (column ANNOTATIONS (...)). Other ALTER forms
    /// return false (position restored) and stay generic.
    /// </summary>
    private bool TryParseAlterAnnotations(Token start, ObjectKind kind, out Statement result)
    {
        result = null!;
        int mark = _p;
        var st = new AlterAnnotationsStatement { Kind = kind };
        st.Name = ParseDottedName();
        if (IsKw("ANNOTATIONS") && IsOpAt(1, "("))
        {
            Next();
            st.Annotations = ParseAnnotations(allowDrop: true);
        }
        else if (IsKw("MODIFY") && (IsOpAt(1, "(") || (kind == ObjectKind.MaterializedView && IsKwAt(2, "ANNOTATIONS"))))
        {
            Next();
            if (AcceptOp("("))
            {
                do st.ColumnAnnotations.Add(ParseAlterViewColumn()); while (AcceptOp(","));
                ExpectOp(")");
            }
            else st.ColumnAnnotations.Add(ParseAlterViewColumn());
        }
        else
        {
            _p = mark;
            return false;
        }
        if (!AtStatementEnd()) throw Fail($"Unexpected {Describe(Cur)} after ALTER {kind} annotations");
        result = Fin(st, start);
        return true;
    }

    private ViewColumn ParseAlterViewColumn()
    {
        var s = Cur;
        var vc = new ViewColumn { Name = ParseIdentifier() };
        ExpectKw("ANNOTATIONS");
        vc.Annotations = ParseAnnotations(allowDrop: true);
        return Fin(vc, s);
    }

    private Statement ParseAlterDomain(Token start)
    {
        var st = new AlterDomainStatement { Name = ParseDottedName() };
        if ((IsKw("ADD") || IsKw("DROP")) && (IsKwAt(1, "DISPLAY") || IsKwAt(1, "ORDER")))
        {
            var a = new AlterDomainAction();
            bool add = Next().Value == "ADD";
            bool display = Next().Value == "DISPLAY";
            a.Kind = add ? (display ? AlterDomainActionKind.AddDisplay : AlterDomainActionKind.AddOrder)
                         : (display ? AlterDomainActionKind.DropDisplay : AlterDomainActionKind.DropOrder);
            if (add) a.Value = ParseExpr();
            st.Action = Fin(a, start);
        }
        else if (AcceptKw("ANNOTATIONS")) st.Annotations = ParseAnnotations(allowDrop: true);
        if (!AtStatementEnd()) st.OtherClauses = ParseSyntaxItems(AtStatementEnd);
        return Fin(st, start);
    }

    private Statement ParseCreatePropertyGraph(Token start, bool orReplace)
    {
        var st = new CreatePropertyGraphStatement { OrReplace = orReplace, Name = ParseDottedName() };
        ExpectKw("VERTEX"); ExpectKw("TABLES"); ExpectOp("(");
        do st.VertexTables.Add(ParseGraphVertexTable()); while (AcceptOp(","));
        ExpectOp(")");
        if (AcceptKw("EDGE"))
        {
            ExpectKw("TABLES"); ExpectOp("(");
            do st.EdgeTables.Add(ParseGraphEdgeTable()); while (AcceptOp(","));
            ExpectOp(")");
        }
        return Fin(st, start);
    }

    private GraphVertexTable ParseGraphVertexTable()
    {
        var s = Cur;
        var (table, alias, key) = ParseGraphElementCommon();
        var t = new GraphVertexTable { Table = table, Alias = alias, Key = key };
        ParseGraphLabels(t);
        return Fin(t, s);
    }

    private GraphEdgeTable ParseGraphEdgeTable()
    {
        var s = Cur;
        var (table, alias, key) = ParseGraphElementCommon();
        ExpectKw("SOURCE");
        var source = ParseGraphEdgeEndpoint();
        ExpectKw("DESTINATION");
        var destination = ParseGraphEdgeEndpoint();
        var t = new GraphEdgeTable { Table = table, Alias = alias, Key = key, Source = source, Destination = destination };
        ParseGraphLabels(t);
        return Fin(t, s);
    }

    /// <summary>The table, optional AS alias and optional KEY, shared by vertex and edge tables.</summary>
    private (List<Identifier> Table, Identifier? Alias, List<Identifier> Key) ParseGraphElementCommon()
    {
        var table = ParseDottedName();
        Identifier? alias = null;
        var key = new List<Identifier>();
        if (AcceptKw("AS")) alias = ParseIdentifier();
        if (AcceptKw("KEY")) { ExpectOp("("); key = ParseIdentifierList(); ExpectOp(")"); }
        return (table, alias, key);
    }

    /// <summary>
    /// SOURCE/DESTINATION: KEY (cols) REFERENCES vertex_table (cols), or just vertex_table (Oracle rejects a column list
    /// without KEY, and REFERENCES without its columns).
    /// </summary>
    private GraphEdgeEndpoint ParseGraphEdgeEndpoint()
    {
        var s = Cur;
        var ep = new GraphEdgeEndpoint();
        if (AcceptKw("KEY"))
        {
            ExpectOp("("); ep.Keys = ParseIdentifierList(); ExpectOp(")");
            ExpectKw("REFERENCES");
            ep.Table = ParseDottedName();
            ExpectOp("("); ep.Columns = ParseIdentifierList(); ExpectOp(")");
        }
        else ep.Table = ParseDottedName();
        return Fin(ep, s);
    }

    private GraphPropertiesClause ParseGraphProperties()
    {
        var s = Cur;
        var p = new GraphPropertiesClause();
        if (AcceptKw("NO")) { ExpectKw("PROPERTIES"); p.Kind = GraphPropertiesKind.None; }
        else
        {
            ExpectKw("PROPERTIES");
            if (AcceptOp("("))
            {
                do
                {
                    var ps = Cur;
                    var gp = new GraphProperty { Property = ParseIdentifier() };
                    if (AcceptKw("AS")) gp.Alias = ParseIdentifier();
                    p.Columns.Add(Fin(gp, ps));
                } while (AcceptOp(","));
                ExpectOp(")");
            }
            else
            {
                ExpectKw("ARE"); ExpectKw("ALL"); ExpectKw("COLUMNS");
                p.Kind = GraphPropertiesKind.AllColumns;
                if (AcceptKw("EXCEPT")) { ExpectOp("("); p.Except = ParseIdentifierList(); ExpectOp(")"); }
            }
        }
        return Fin(p, s);
    }

    /// <summary>The LABEL clauses that close a vertex or edge table definition, with PROPERTIES allowed before any LABEL.</summary>
    private void ParseGraphLabels(GraphElementTable t)
    {
        if (IsKw("PROPERTIES") || (IsKw("NO") && IsKwAt(1, "PROPERTIES"))) t.Properties = ParseGraphProperties();
        while (AcceptKw("LABEL"))
        {
            var l = new GraphElementLabel { Name = ParseIdentifier() };
            if (IsKw("PROPERTIES") || (IsKw("NO") && IsKwAt(1, "PROPERTIES"))) l.Properties = ParseGraphProperties();
            t.Labels.Add(l);
        }
    }

    private Statement ParseCreateSequence(Token start)
    {
        var st = new CreateSequenceStatement { IfNotExists = TryIfNotExists() };
        st.Name = ParseDottedName();
        st.Options = ParseSequenceOptions(() => false);
        return Fin(st, start);
    }

    private Statement ParseCreateSynonym(Token start, bool orReplace, bool pub)
    {
        var st = new CreateSynonymStatement { OrReplace = orReplace, Public = pub, IfNotExists = TryIfNotExists() };
        st.Name = ParseDottedName();
        st.Sharing = TryParseSharing();
        ExpectKw("FOR");
        st.Target = ParseDottedName();
        if (AcceptOp("@")) st.DbLink = ParseDottedName();
        return Fin(st, start);
    }

    // ---- ALTER TABLE ----

    private Statement ParseAlterTable(Token start)
    {
        var st = new AlterTableStatement { IfExists = TryIfExists() };
        st.Name = ParseDottedName();
        int prog28 = -1;
        while (!AtStatementEnd() && Progress(ref prog28)) st.Actions.Add(ParseAlterTableAction());
        return Fin(st, start);
    }

    private bool AtAlterActionStart() => Cur.Type == TokenType.Identifier && Cur.Value is "ADD" or "MODIFY" or "DROP" or "RENAME" or "SET";

    private void ParseColumnOrConstraintList(AlterTableAction a)
    {
        ExpectOp("(");
        do
        {
            if (Cur.Type == TokenType.Identifier && ConstraintStarts.Contains(Cur.Value)) a.Constraints.Add(ParseConstraint(false));
            else a.Columns.Add(ParseColumnDefinition());
        } while (AcceptOp(","));
        ExpectOp(")");
    }

    private AlterTableAction ParseAlterTableAction()
    {
        if (AtPartitionMaintenanceStart()) return ParsePartitionMaintenance();
        var s = Cur;
        var a = new AlterTableAction();
        if (IsKw("ANNOTATIONS") && IsOpAt(1, "("))
        {
            Next();
            a.Kind = AlterTableActionKind.Annotations;
            a.Annotations = ParseAnnotations(allowDrop: true);
            return Fin(a, s);
        }
        if (IsKw("ADD") && !(IsKwAt(1, "PARTITION") || IsKwAt(1, "SUPPLEMENTAL") || IsKwAt(1, "PERIOD") || IsKwAt(1, "SUBPARTITION") || IsKwAt(1, "OVERFLOW") || IsKwAt(1, "ATTRIBUTE")))
        {
            Next();
            if (Cur.Type == TokenType.Identifier && ConstraintStarts.Contains(Cur.Value)) { a.Kind = AlterTableActionKind.AddConstraint; a.Constraints.Add(ParseConstraint(false)); }
            else if (IsOp("(")) { a.Kind = AlterTableActionKind.AddColumns; ParseColumnOrConstraintList(a); if (a.Columns.Count == 0) a.Kind = AlterTableActionKind.AddConstraint; }
            else { if (IsKw("ANNOTATIONS")) throw Fail("ADD ANNOTATIONS is not an ALTER TABLE form; use ANNOTATIONS (ADD ...)", ParseErrorCodes.InvalidCombination); a.Kind = AlterTableActionKind.AddColumns; a.Columns.Add(ParseColumnDefinition(AtAlterActionStart, alter: true)); }
        }
        else if (IsKw("MODIFY") && (IsOpAt(1, "(") || (PeekTok().Type is TokenType.Identifier or TokenType.QuotedIdentifier &&
                 !IsKwAt(1, "CONSTRAINT") && !IsKwAt(1, "PRIMARY") && !IsKwAt(1, "UNIQUE") && !IsKwAt(1, "PARTITION") && !IsKwAt(1, "SUBPARTITION") &&
                 !IsKwAt(1, "DEFAULT") && !IsKwAt(1, "LOB") && !IsKwAt(1, "VARRAY") && !IsKwAt(1, "NESTED") && !IsKwAt(1, "OVERFLOW") && !IsKwAt(1, "ATTRIBUTE"))))
        {
            Next();
            a.Kind = AlterTableActionKind.ModifyColumns;
            if (IsOp("(")) { Next(); do a.Columns.Add(ParseColumnDefinition(alter: true)); while (AcceptOp(",")); ExpectOp(")"); }
            else a.Columns.Add(ParseColumnDefinition(AtAlterActionStart, alter: true));
        }
        else if (IsKw("MODIFY") && (IsKwAt(1, "CONSTRAINT") || IsKwAt(1, "PRIMARY") || IsKwAt(1, "UNIQUE")))
        {
            Next();
            a.Kind = AlterTableActionKind.ModifyConstraint;
            var cs = Cur;
            var tc = new TableConstraint();
            if (AcceptKw("CONSTRAINT")) tc.Name = ParseIdentifier();
            else if (AcceptKw("PRIMARY")) { ExpectKw("KEY"); tc.Kind = ConstraintKind.PrimaryKey; }
            else { Next(); tc.Kind = ConstraintKind.Unique; ParseConstraintColumns(tc); }
            tc.State = ParseConstraintState();
            a.Constraints.Add(Fin(tc, cs));
        }
        else if (IsKw("DROP") && (IsKwAt(1, "COLUMN") || IsOpAt(1, "(")))
        {
            Next();
            a.Kind = AlterTableActionKind.DropColumns;
            AcceptKw("COLUMN");
            if (AcceptOp("(")) { a.Names = ParseIdentifierList(); ExpectOp(")"); }
            else a.Names.Add(ParseIdentifier());
            ParseDropColumnTail(a);
        }
        else if (IsKw("DROP") && IsKwAt(1, "CONSTRAINT"))
        {
            Next(); Next();
            a.Kind = AlterTableActionKind.DropConstraint;
            a.Names.Add(ParseIdentifier());
            ParseDropColumnTail(a);
        }
        else if (IsKw("DROP") && IsKwAt(1, "PRIMARY") && IsKwAt(2, "KEY"))
        {
            Next(); Next(); Next();
            a.Kind = AlterTableActionKind.DropPrimaryKey;
            ParseDropColumnTail(a);
        }
        else if (IsKw("DROP") && IsKwAt(1, "UNIQUE") && IsOpAt(2, "("))
        {
            Next(); Next(); Next();
            a.Kind = AlterTableActionKind.DropUnique;
            a.Names = ParseIdentifierList();
            ExpectOp(")");
            ParseDropColumnTail(a);
        }
        else if (IsKw("SET") && IsKwAt(1, "UNUSED"))
        {
            Next(); Next();
            a.Kind = AlterTableActionKind.SetUnused;
            if (AcceptKw("COLUMN")) a.Names.Add(ParseIdentifier());
            else { ExpectOp("("); a.Names = ParseIdentifierList(); ExpectOp(")"); }
            ParseDropColumnTail(a);
        }
        else if (IsKw("RENAME") && (IsKwAt(1, "COLUMN") || IsKwAt(1, "CONSTRAINT")))
        {
            Next();
            a.Kind = Next().Value == "COLUMN" ? AlterTableActionKind.RenameColumn : AlterTableActionKind.RenameConstraint;
            a.Names.Add(ParseIdentifier());
            ExpectKw("TO");
            a.NewName = ParseIdentifier();
        }
        else if (IsKw("RENAME") && IsKwAt(1, "TO"))
        {
            Next(); Next();
            a.Kind = AlterTableActionKind.RenameTo;
            a.NewName = ParseIdentifier();
        }
        else if (Cur.Type == TokenType.Identifier && AttributeStarts.Contains(Cur.Value) && !IsKw("PARTITION") &&
                 !((IsKw("ENABLE") || IsKw("DISABLE")) && !IsKwAt(1, "ROW")))
        {
            a.Kind = AlterTableActionKind.Attributes;
            a.Attributes = ParsePhysicalAttributes(AtAlterActionStart);
        }
        else
        {
            a.Kind = AlterTableActionKind.Other;
            bool first = true;
            a.Syntax = ParseSyntaxItems(() => { if (first) { first = false; return false; } return AtAlterActionStart(); });
        }
        return Fin(a, s);
    }

    private void ParseDropColumnTail(AlterTableAction a)
    {
        int prog29 = -1;
        while (Progress(ref prog29))
        {
            if (AcceptKw("CASCADE")) { AcceptKw("CONSTRAINTS"); a.Cascade = true; }
            else if (AcceptKw("ONLINE")) a.Online = true;
            else if (IsKw("INVALIDATE") || IsKw("CHECKPOINT") || IsKw("KEEP") || IsKw("DROP")) a.Syntax.Add(ParseSyntaxItem());
            else break;
        }
    }

    // ---- Java / library ----

    private Statement ParseCreateLibrary(Token start, bool orReplace, EditionableKind? ed)
    {
        TryIfNotExists();
        var name = ParseDottedName();
        var sharing = TryParseSharing();
        if (!AcceptKw("IS")) ExpectKw("AS");
        var path = ParsePrimary();
        var st = new CreateLibraryStatement { OrReplace = orReplace, Editionable = ed, Name = name, Sharing = sharing, Path = path };
        if (AcceptKw("AGENT")) st.Agent = ParsePrimary();
        if (AcceptKw("CREDENTIAL")) st.Credential = ParseIdentifier();
        return Fin(st, start);
    }

    private Statement ParseCreateJava(Token start, bool orReplace, bool andResolve, bool andCompile, bool noForce, bool kindConsumedAtJava = false)
    {
        var st = new CreateJavaStatement { OrReplace = orReplace, AndResolve = andResolve, AndCompile = andCompile, NoForce = noForce };
        if (kindConsumedAtJava || true)
        {
            if (AcceptKw("SOURCE")) st.Kind = JavaObjectKind.Source;
            else if (AcceptKw("CLASS")) st.Kind = JavaObjectKind.Class;
            else if (AcceptKw("RESOURCE")) st.Kind = JavaObjectKind.Resource;
            else throw FailExpected($"Expected SOURCE, CLASS or RESOURCE but found {Describe(Cur)}", "SOURCE", "CLASS", "RESOURCE");
        }
        ExpectKw("NAMED");
        st.Name = ParseDottedName();
        st.Sharing = TryParseSharing();
        if (IsKw("AUTHID")) { Next(); st.Authid = AcceptKw("CURRENT_USER") ? AuthidKind.CurrentUser : (AcceptKw("DEFINER") ? AuthidKind.Definer : throw FailExpected("Expected CURRENT_USER or DEFINER", "CURRENT_USER", "DEFINER")); }
        if (AcceptKw("RESOLVER")) st.Resolver = ParseSyntaxItems(() => IsKw("USING") || IsKw("AS") || IsKw("IS"));
        if (AcceptKw("USING"))
        {
            if (AcceptKw("BFILE"))
            {
                if (IsOp("("))
                {
                    Next();
                    st.UsingKind = JavaUsingKind.Bfile;
                    st.UsingDirectory = ParseExpr(); ExpectOp(","); st.UsingFile = ParseExpr();
                    ExpectOp(")");
                }
                else { st.UsingKind = JavaUsingKind.Bfile; st.UsingQuery = ParseSelectStatement(); }
            }
            else if (IsKw("CLOB") || IsKw("BLOB"))
            {
                st.UsingKind = Next().Value == "CLOB" ? JavaUsingKind.Clob : JavaUsingKind.Blob;
                st.UsingQuery = ParseSelectStatement();
            }
            else { st.UsingKind = JavaUsingKind.Key; st.UsingKey = ParsePrimary(); }
        }
        else
        {
            if (!AcceptKw("AS")) ExpectKw("IS");
            if (Cur.Type != TokenType.String) throw FailExpected("Expected Java source text", "Java source text");
            st.SourceText = Next().Value;
        }
        return Fin(st, start);
    }
}
