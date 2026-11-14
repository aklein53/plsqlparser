namespace OracleSqlParser;

public sealed partial class SqlScriptGenerator
{
    private sealed partial class Printer
    {
        // ---------- object kind words ----------

        private static readonly Dictionary<ObjectKind, string> KindWords = BuildKindWords();

        private static Dictionary<ObjectKind, string> BuildKindWords()
        {
            var d = new Dictionary<ObjectKind, string>();
            foreach (var (words, kind) in SqlParser.ObjectKindTable) d.TryAdd(kind, string.Join(" ", words));
            return d;
        }

        private static string KindText(ObjectKind k) => KindWords.TryGetValue(k, out var s) ? s : "";

        private void KindKeyword(ObjectKind k)
        {
            var s = KindText(k);
            if (s.Length > 0) Add(s);
        }

        private static string SharingText(SharingKind s) =>
            "SHARING = " + s switch { SharingKind.Metadata => "METADATA", SharingKind.Data => "DATA", SharingKind.Extended => "EXTENDED DATA", _ => "NONE" };

        private void SharingClause(SharingKind? s)
        {
            if (s != null) Add(" " + SharingText(s.Value));
        }

        private void Editionable(EditionableKind? e)
        {
            if (e != null) Add(e == EditionableKind.Editionable ? " EDITIONABLE" : " NONEDITIONABLE");
        }

        // ---------- DDL statements ----------

        private void Ddl(Statement st)
        {
            switch (st)
            {
                case CreateObjectStatement co:
                    Add("CREATE");
                    if (co.OrReplace) Add(" OR REPLACE");
                    if (co.Public) Add(" PUBLIC");
                    if (KindText(co.Kind).Length > 0) { Sp(); KindKeyword(co.Kind); }
                    if (co.IfNotExists) Add(" IF NOT EXISTS");
                    if (co.Name.Count > 0) { Sp(); Name(co.Name); }
                    if (co.Body.Count > 0) { Sp(); SyntaxList(co.Body); }
                    break;
                case AlterObjectStatement ao:
                    Add("ALTER");
                    if (ao.Public) Add(" PUBLIC");
                    if (KindText(ao.Kind).Length > 0) { Sp(); KindKeyword(ao.Kind); }
                    if (ao.IfExists) Add(" IF EXISTS");
                    if (ao.Name.Count > 0) { Sp(); Name(ao.Name); }
                    if (ao.Body.Count > 0) { Sp(); SyntaxList(ao.Body); }
                    break;
                case DropStatement dr: Drop(dr); break;
                case CreateRoleStatement cr:
                    Add("CREATE ROLE");
                    if (cr.IfNotExists) Add(" IF NOT EXISTS");
                    Sp(); Id(cr.Name);
                    RoleIdentificationText(cr.Authentication, cr.Password, cr.Package);
                    ContainerClause(cr.Container);
                    break;
                case AlterRoleStatement ar:
                    Add("ALTER ROLE");
                    if (ar.IfExists) Add(" IF EXISTS");
                    Sp(); Id(ar.Name);
                    RoleIdentificationText(ar.Authentication, ar.Password, ar.Package);
                    ContainerClause(ar.Container);
                    break;
                case CreateDirectoryStatement cd:
                    Add("CREATE");
                    if (cd.OrReplace) Add(" OR REPLACE");
                    Add(" DIRECTORY");
                    if (cd.IfNotExists) Add(" IF NOT EXISTS");
                    Sp(); Id(cd.Name);
                    SharingClause(cd.Sharing);
                    Add(" AS "); Expr(cd.Path);
                    break;
                case CreateDatabaseLinkStatement dl:
                    Add("CREATE");
                    if (dl.OrReplace) Add(" OR REPLACE");
                    if (dl.Shared) Add(" SHARED");
                    if (dl.Public) Add(" PUBLIC");
                    Add(" DATABASE LINK");
                    if (dl.IfNotExists) Add(" IF NOT EXISTS");
                    Sp(); Name(dl.Name);
                    if (dl.CurrentUser) Add(" CONNECT TO CURRENT_USER");
                    else if (dl.ConnectUser != null)
                    {
                        Add(" CONNECT TO "); Id(dl.ConnectUser);
                        if (dl.ConnectPassword != null) { Add(" IDENTIFIED BY "); Verbatim(dl.ConnectPassword); }
                    }
                    if (dl.AuthenticatedUser != null) { Add(" AUTHENTICATED BY "); Id(dl.AuthenticatedUser); Add(" IDENTIFIED BY "); Verbatim(dl.AuthenticatedPassword!); }
                    if (dl.Using != null) { Add(" USING "); Expr(dl.Using); }
                    break;
                case CreateContextStatement cx:
                    Add("CREATE");
                    if (cx.OrReplace) Add(" OR REPLACE");
                    Add(" CONTEXT");
                    if (cx.IfNotExists) Add(" IF NOT EXISTS");
                    Sp(); Id(cx.Name);
                    Add(" USING "); Name(cx.Package);
                    if (cx.Initialized != null) Add(cx.Initialized == ContextInit.Externally ? " INITIALIZED EXTERNALLY" : " INITIALIZED GLOBALLY");
                    if (cx.AccessedGlobally) Add(" ACCESSED GLOBALLY");
                    break;
                case CreateProfileStatement cp:
                    Add("CREATE PROFILE");
                    if (cp.IfNotExists) Add(" IF NOT EXISTS");
                    Sp(); Id(cp.Name);
                    ProfileBody(cp.Limits, cp.Container, false);
                    break;
                case AlterProfileStatement ap:
                    Add("ALTER PROFILE");
                    if (ap.IfExists) Add(" IF EXISTS");
                    Sp(); Id(ap.Name);
                    ProfileBody(ap.Limits, ap.Container, ap.Cascade);
                    break;
                case CreateUserStatement cu:
                    Add("CREATE USER");
                    if (cu.IfNotExists) Add(" IF NOT EXISTS");
                    Sp(); Id(cu.Name);
                    foreach (var c in cu.Clauses) { Sp(); UserClauseText(c); }
                    break;
                case AlterUserStatement au:
                    Add("ALTER USER");
                    if (au.IfExists) Add(" IF EXISTS");
                    Sp(); Id(au.Name);
                    foreach (var c in au.Clauses) { Sp(); UserClauseText(c); }
                    break;
                case CreateTableStatement ct: CreateTable(ct); break;
                case CreateIndexStatement ci: CreateIndex(ci); break;
                case CreateViewStatement cv: CreateView(cv); break;
                case CreateSequenceStatement cs:
                    Add("CREATE SEQUENCE");
                    if (cs.IfNotExists) Add(" IF NOT EXISTS");
                    Sp(); Name(cs.Name);
                    SharingClause(cs.Sharing);
                    foreach (var o in cs.Options) { Sp(); SeqOption(o); }
                    break;
                case CreateSynonymStatement sy:
                    Add("CREATE");
                    if (sy.OrReplace) Add(" OR REPLACE");
                    if (sy.Public) Add(" PUBLIC");
                    Add(" SYNONYM");
                    if (sy.IfNotExists) Add(" IF NOT EXISTS");
                    Sp(); Name(sy.Name);
                    SharingClause(sy.Sharing);
                    Add(" FOR "); Name(sy.Target);
                    if (sy.DbLink.Count > 0) { Add("@"); Name(sy.DbLink); }
                    break;
                case AlterTableStatement at:
                    Add("ALTER TABLE");
                    if (at.IfExists) Add(" IF EXISTS");
                    Sp(); Name(at.Name);
                    foreach (var a in at.Actions) { Sp(); AlterAction(a); }
                    break;
                case CreateJavaStatement cj: CreateJava(cj); break;
                case CreateLibraryStatement cl:
                    Add("CREATE");
                    if (cl.OrReplace) Add(" OR REPLACE");
                    Editionable(cl.Editionable);
                    Add(" LIBRARY "); Name(cl.Name);
                    SharingClause(cl.Sharing);
                    Add(" AS "); Expr(cl.Path);
                    if (cl.Agent != null) { Add(" AGENT "); Expr(cl.Agent); }
                    if (cl.Credential != null) { Add(" CREDENTIAL "); Id(cl.Credential); }
                    break;
                default: throw Unsupported(st);
            }
        }

        // ---------- CREATE / ALTER ROLE ----------

        private void RoleIdentificationText(RoleAuthentication? a, Identifier? password, List<Identifier> package)
        {
            switch (a)
            {
                case RoleAuthentication.NotIdentified: Add(" NOT IDENTIFIED"); break;
                case RoleAuthentication.Password: Add(" IDENTIFIED BY "); Verbatim(password!); break;
                case RoleAuthentication.Package: Add(" IDENTIFIED USING "); Name(package); break;
                case RoleAuthentication.External: Add(" IDENTIFIED EXTERNALLY"); break;
                case RoleAuthentication.Global: Add(" IDENTIFIED GLOBALLY"); break;
            }
        }

        private void ContainerClause(GrantContainer? c)
        {
            if (c != null) Add(" CONTAINER = " + (c == GrantContainer.All ? "ALL" : "CURRENT"));
        }

        // ---------- CREATE / ALTER PROFILE ----------

        private void ProfileBody(List<ProfileLimit> limits, GrantContainer? container, bool cascade)
        {
            if (limits.Count > 0)
            {
                Add(" LIMIT");
                foreach (var l in limits) { Sp(); ProfileLimitText(l); }
            }
            ContainerClause(container);
            if (cascade) Add(" CASCADE");
        }

        private void ProfileLimitText(ProfileLimit l)
        {
            Id(l.Name);
            Sp();
            if (l.Keyword != null) Add(l.Keyword switch { ProfileKeyword.Unlimited => "UNLIMITED", ProfileKeyword.Default => "DEFAULT", _ => "NULL" });
            else if (l.Function.Count > 0) Name(l.Function);
            else Expr(l.Value!);
        }

        // ---------- CREATE / ALTER USER ----------

        /// <summary>Printed as typed, not through IdentifierCasing: passwords are case-sensitive even when unquoted.</summary>
        private void Verbatim(Identifier i) => Raw(i.Quoted ? "\"" + i.Value.Replace("\"", "\"\"") + "\"" : i.Text ?? i.Value);

        private void UserClauseText(UserClause c)
        {
            switch (c.Kind)
            {
                case UserClauseKind.IdentifiedBy:
                    Add("IDENTIFIED BY "); Verbatim(c.Password!);
                    if (c.ReplacePassword != null) { Add(" REPLACE "); Verbatim(c.ReplacePassword); }
                    break;
                case UserClauseKind.IdentifiedExternally: Add("IDENTIFIED EXTERNALLY"); ExternalNameText(c); break;
                case UserClauseKind.IdentifiedGlobally: Add("IDENTIFIED GLOBALLY"); ExternalNameText(c); break;
                case UserClauseKind.NoAuthentication: Add("NO AUTHENTICATION"); break;
                case UserClauseKind.DefaultTablespace: Add("DEFAULT TABLESPACE "); Id(c.Tablespace!); break;
                case UserClauseKind.TemporaryTablespace:
                    if (c.Local) Add("LOCAL ");
                    Add("TEMPORARY TABLESPACE "); Id(c.Tablespace!);
                    break;
                case UserClauseKind.Quota:
                    Add("QUOTA ");
                    if (c.Unlimited) Add("UNLIMITED"); else Expr(c.Quota!);
                    Add(" ON "); Id(c.Tablespace!);
                    break;
                case UserClauseKind.Profile: Add("PROFILE "); Id(c.Profile!); break;
                case UserClauseKind.PasswordExpire: Add("PASSWORD EXPIRE"); break;
                case UserClauseKind.AccountLock: Add("ACCOUNT LOCK"); break;
                case UserClauseKind.AccountUnlock: Add("ACCOUNT UNLOCK"); break;
                case UserClauseKind.DefaultRole: Add("DEFAULT ROLE "); RoleSetText(c); break;
                case UserClauseKind.Container: Add("CONTAINER = " + (c.Container == GrantContainer.All ? "ALL" : "CURRENT")); break;
                case UserClauseKind.EnableEditions: Add("ENABLE EDITIONS"); if (c.Force) Add(" FORCE"); break;
                case UserClauseKind.DisableEditions: Add("DISABLE EDITIONS"); break;
                case UserClauseKind.ReadOnly: Add("READ ONLY"); break;
                case UserClauseKind.ReadWrite: Add("READ WRITE"); break;
                case UserClauseKind.GrantConnectThrough: Add("GRANT CONNECT THROUGH "); ProxyText(c, true); break;
                case UserClauseKind.RevokeConnectThrough: Add("REVOKE CONNECT THROUGH "); ProxyText(c, false); break;
                default: SyntaxList(c.Syntax); break;
            }
        }

        private void ExternalNameText(UserClause c)
        {
            if (c.ExternalName != null) { Add(" AS "); Expr(c.ExternalName); }
        }

        private void RoleSetText(UserClause c)
        {
            switch (c.RoleSet)
            {
                case RoleSetKind.All:
                    Add("ALL");
                    if (c.ExceptRoles.Count > 0) { Add(" EXCEPT "); IdList(c.ExceptRoles); }
                    break;
                case RoleSetKind.None: Add("NONE"); break;
                default: IdList(c.Roles); break;
            }
        }

        private void ProxyText(UserClause c, bool grant)
        {
            if (c.EnterpriseUsers) Add("ENTERPRISE USERS"); else Id(c.Proxy!);
            if (!grant) return;
            if (c.RoleSet == RoleSetKind.None) Add(" WITH NO ROLES");
            else if (c.RoleSet != null) { Add(" WITH ROLE "); RoleSetText(c); }
            if (c.AuthenticationRequired) Add(" AUTHENTICATION REQUIRED");
        }

        private void Drop(DropStatement d)
        {
            Add("DROP");
            if (d.Public) Add(" PUBLIC");
            if (KindText(d.Kind).Length > 0) { Sp(); KindKeyword(d.Kind); }
            if (d.IfExists) Add(" IF EXISTS");
            if (d.OnTable.Count > 0) { Add(" ON "); Name(d.OnTable); }
            else if (d.Name.Count > 0) { Sp(); Name(d.Name); }
            if (d.CascadeConstraints) Add(" CASCADE CONSTRAINTS");
            if (d.Cascade) Add(" CASCADE");
            if (d.Purge) Add(" PURGE");
            if (d.Force) Add(" FORCE");
            if (d.IncludingContents) Add(" INCLUDING CONTENTS");
            if (d.AndDatafiles) Add(" AND DATAFILES");
            if (d.Validate) Add(" VALIDATE");
            if (d.Invalidate) Add(" INVALIDATE");
            if (d.Rest.Count > 0) { Sp(); SyntaxList(d.Rest); }
        }

        private void CreateJava(CreateJavaStatement j)
        {
            Add("CREATE");
            if (j.OrReplace) Add(" OR REPLACE");
            if (j.AndResolve) Add(" AND RESOLVE");
            if (j.AndCompile) Add(" AND COMPILE");
            if (j.NoForce) Add(" NOFORCE");
            Add(" JAVA " + j.Kind switch { JavaObjectKind.Source => "SOURCE", JavaObjectKind.Class => "CLASS", _ => "RESOURCE" });
            Add(" NAMED "); Name(j.Name);
            SharingClause(j.Sharing);
            if (j.Authid != null) Add(j.Authid == AuthidKind.CurrentUser ? " AUTHID CURRENT_USER" : " AUTHID DEFINER");
            if (j.Resolver.Count > 0) { Add(" RESOLVER "); SyntaxList(j.Resolver); }
            if (j.UsingKind != null)
            {
                Add(" USING ");
                switch (j.UsingKind)
                {
                    case JavaUsingKind.Bfile:
                        Add("BFILE");
                        if (j.UsingQuery != null) { Sp(); Select(j.UsingQuery); }
                        else { Add(" ("); Expr(j.UsingDirectory!); Add(", "); Expr(j.UsingFile!); Add(")"); }
                        break;
                    case JavaUsingKind.Clob: Add("CLOB "); Select(j.UsingQuery!); break;
                    case JavaUsingKind.Blob: Add("BLOB "); Select(j.UsingQuery!); break;
                    default: Expr(j.UsingKey!); break;
                }
            }
            else if (j.SourceText != null)
            {
                Add(" AS ");
                Raw(j.SourceText);
            }
        }

        // ---------- CREATE TABLE ----------

        private void CreateTable(CreateTableStatement t)
        {
            Add("CREATE");
            if (t.OrReplace) Add(" OR REPLACE");
            if (t.GlobalTemporary) Add(" GLOBAL TEMPORARY");
            if (t.PrivateTemporary) Add(" PRIVATE TEMPORARY");
            if (t.Duplicated) Add(" DUPLICATED");
            if (t.Immutable) Add(" IMMUTABLE");
            if (t.Blockchain) Add(" BLOCKCHAIN");
            Add(" TABLE");
            if (t.IfNotExists) Add(" IF NOT EXISTS");
            Sp(); Name(t.Name);
            SharingClause(t.Sharing);
            if (t.OfType.Count > 0) { Add(" OF "); Name(t.OfType); }
            if (t.Columns.Count > 0 || t.Constraints.Count > 0)
            {
                Add(" (");
                if (_o.MultilineColumnDefinitions)
                {
                    _indent++;
                    bool first = true;
                    foreach (var c in t.Columns) { if (!first) Add(","); first = false; NewLine(); ColumnDef(c); }
                    foreach (var c in t.Constraints) { if (!first) Add(","); first = false; NewLine(); Constraint(c); }
                    _indent--;
                    NewLine();
                }
                else
                {
                    Sep(t.Columns, ColumnDef);
                    if (t.Columns.Count > 0 && t.Constraints.Count > 0) Add(", ");
                    Sep(t.Constraints, Constraint);
                }
                Add(")");
            }
            if (t.OnCommit != null)
                Add(t.OnCommit switch
                {
                    OnCommitAction.PreserveRows => " ON COMMIT PRESERVE ROWS", OnCommitAction.DeleteRows => " ON COMMIT DELETE ROWS",
                    OnCommitAction.DropDefinition => " ON COMMIT DROP DEFINITION", _ => " ON COMMIT PRESERVE DEFINITION",
                });
            foreach (var a in t.Attributes) { Sp(); Attribute(a); }
            if (t.AsQuery != null) { Add(" AS "); Select(t.AsQuery); }
        }

        private void ColumnDef(ColumnDefinition c)
        {
            Id(c.Name);
            if (c.Type != null) { Sp(); Type(c.Type); }
            if (c.Collation != null) { Add(" COLLATE "); Id(c.Collation); }
            if (c.Default != null) { Add(c.DefaultOnNull ? " DEFAULT ON NULL " : " DEFAULT "); Expr(c.Default); }
            if (c.Identity != null)
            {
                Add(" GENERATED " + (c.Identity.Mode == IdentityMode.Always ? "ALWAYS" : "BY DEFAULT" + (c.Identity.OnNull ? " ON NULL" : "")) + " AS IDENTITY");
                if (c.Identity.Options.Count > 0) { Add(" ("); Sep(c.Identity.Options, SeqOption, " "); Add(")"); }
            }
            if (c.VirtualExpression != null) { Add(" AS ("); Expr(c.VirtualExpression); Add(")"); }
            if (c.Visible != null) Add(c.Visible == true ? " VISIBLE" : " INVISIBLE");
            if (c.Encrypt != null)
            {
                Add(" ENCRYPT");
                if (c.Encrypt.Algorithm != null) { Add(" USING "); Expr(c.Encrypt.Algorithm); }
                if (c.Encrypt.IdentifiedBy != null) { Add(" IDENTIFIED BY "); Expr(c.Encrypt.IdentifiedBy); }
                if (c.Encrypt.NoSalt) Add(" NO SALT"); else if (c.Encrypt.Salt) Add(" SALT");
            }
            if (c.Sort) Add(" SORT");
            foreach (var k in c.Constraints) { Sp(); Constraint(k); }
            if (c.OtherProperties.Count > 0) { Sp(); SyntaxList(c.OtherProperties); }
        }

        private static bool NamedSeqOption(string n) => n is "START WITH" or "INCREMENT BY" or "MAXVALUE" or "MINVALUE" or "CACHE" or "START WITH LIMIT VALUE";

        private void SeqOption(SequenceOption o)
        {
            Add(o.Name);
            if (o.Value != null) { Add(NamedSeqOption(o.Name) ? " " : " = "); Expr(o.Value); }
        }

        private void Constraint(TableConstraint c)
        {
            if (c.Name != null) { Add("CONSTRAINT "); Id(c.Name); if (c.Kind != null) Sp(); }
            switch (c.Kind)
            {
                case ConstraintKind.NotNull: Add("NOT NULL"); break;
                case ConstraintKind.Null: Add("NULL"); break;
                case ConstraintKind.PrimaryKey: Add("PRIMARY KEY"); if (c.Columns.Count > 0) { Add(" ("); IdList(c.Columns); Add(")"); } break;
                case ConstraintKind.Unique: Add("UNIQUE"); if (c.Columns.Count > 0) { Add(" ("); IdList(c.Columns); Add(")"); } break;
                case ConstraintKind.ForeignKey:
                    if (c.Columns.Count > 0) { Add("FOREIGN KEY ("); IdList(c.Columns); Add(") "); }
                    Add("REFERENCES "); Name(c.RefTable);
                    if (c.RefColumns.Count > 0) { Add(" ("); IdList(c.RefColumns); Add(")"); }
                    if (c.OnDelete != null) Add(c.OnDelete == ReferentialAction.Cascade ? " ON DELETE CASCADE" : " ON DELETE SET NULL");
                    break;
                case ConstraintKind.Check: Add("CHECK ("); Expr(c.Check!); Add(")"); break;
            }
            if (c.State != null) { Sp(); ConstraintStateText(c.State); }
        }

        private void ConstraintStateText(ConstraintState s)
        {
            var parts = new List<Action>();
            if (s.Deferrable != null) { var v = s.Deferrable.Value; parts.Add(() => Add(v ? "DEFERRABLE" : "NOT DEFERRABLE")); }
            if (s.InitiallyDeferred != null) { var v = s.InitiallyDeferred.Value; parts.Add(() => Add(v ? "INITIALLY DEFERRED" : "INITIALLY IMMEDIATE")); }
            if (s.Enable != null) { var v = s.Enable.Value; parts.Add(() => Add(v ? "ENABLE" : "DISABLE")); }
            if (s.Validate != null) { var v = s.Validate.Value; parts.Add(() => Add(v ? "VALIDATE" : "NOVALIDATE")); }
            if (s.Rely != null) { var v = s.Rely.Value; parts.Add(() => Add(v ? "RELY" : "NORELY")); }
            if (s.UsingIndex != null) parts.Add(() => UsingIndex(s.UsingIndex));
            if (s.ExceptionsInto.Count > 0) parts.Add(() => { Add("EXCEPTIONS INTO "); Name(s.ExceptionsInto); });
            bool first = true;
            foreach (var p in parts) { if (!first) Sp(); first = false; p(); }
        }

        private void UsingIndex(UsingIndexClause u)
        {
            Add("USING INDEX");
            if (u.Create != null) { Add(" ("); Statement(u.Create); Add(")"); }
            else if (u.IndexName.Count > 0) { Sp(); Name(u.IndexName); }
            else foreach (var a in u.Attributes) { Sp(); Attribute(a); }
        }

        // ---------- CREATE INDEX / VIEW ----------

        private void IndexCol(IndexColumn c)
        {
            Expr(c.Expression);
            if (c.Descending) Add(" DESC");
        }

        private void CreateIndex(CreateIndexStatement i)
        {
            Add("CREATE");
            if (i.Unique) Add(" UNIQUE");
            if (i.Bitmap) Add(" BITMAP");
            Add(" INDEX");
            if (i.IfNotExists) Add(" IF NOT EXISTS");
            Sp(); Name(i.Name);
            Add(" ON ");
            if (i.Cluster.Count > 0) { Add("CLUSTER "); Name(i.Cluster); }
            else
            {
                Name(i.Table);
                if (i.TableAlias != null) { Sp(); Id(i.TableAlias); }
            }
            if (i.Columns.Count > 0) { Add(" ("); Sep(i.Columns, IndexCol); Add(")"); }
            if (i.Indextype.Count > 0)
            {
                Add(" INDEXTYPE IS "); Name(i.Indextype);
                if (i.IndextypeParameters != null) { Add(" PARAMETERS ("); Expr(i.IndextypeParameters); Add(")"); }
            }
            if (i.Local) Add(" LOCAL");
            if (i.Global) Add(" GLOBAL");
            if (i.Reverse) Add(" REVERSE");
            if (i.Online) Add(" ONLINE");
            if (i.Visible != null) Add(i.Visible == true ? " VISIBLE" : " INVISIBLE");
            foreach (var a in i.Attributes) { Sp(); Attribute(a); }
        }

        private void CreateView(CreateViewStatement v)
        {
            Add("CREATE");
            if (v.OrReplace) Add(" OR REPLACE");
            if (v.Force) Add(" FORCE");
            if (v.NoForce) Add(" NOFORCE");
            if (v.Editioning) Add(" EDITIONING");
            Add(v.Materialized ? " MATERIALIZED VIEW" : " VIEW");
            if (v.IfNotExists) Add(" IF NOT EXISTS");
            Sp(); Name(v.Name);
            SharingClause(v.Sharing);
            if (v.Columns.Count > 0) { Add(" ("); IdList(v.Columns); Add(")"); }
            if (v.Bequeath != null) Add(v.Bequeath == ViewBequeath.CurrentUser ? " BEQUEATH CURRENT_USER" : " BEQUEATH DEFINER");
            if (v.BuildImmediate != null) Add(v.BuildImmediate == true ? " BUILD IMMEDIATE" : " BUILD DEFERRED");
            if (v.Refresh != null) { Sp(); Refresh(v.Refresh); }
            if (v.QueryRewrite != null) Add(v.QueryRewrite == true ? " ENABLE QUERY REWRITE" : " DISABLE QUERY REWRITE");
            foreach (var a in v.Attributes) { Sp(); Attribute(a); }
            Add(" AS ");
            Select(v.Query);
            if (v.ReadOnly) Add(" WITH READ ONLY");
            else if (v.CheckOption)
            {
                Add(" WITH CHECK OPTION");
                if (v.CheckOptionConstraint != null) { Add(" CONSTRAINT "); Id(v.CheckOptionConstraint); }
            }
        }

        private void Refresh(RefreshClause r)
        {
            if (r.Method == RefreshMethod.Never) { Add("NEVER REFRESH"); return; }
            Add("REFRESH");
            if (r.Method != null) Add(r.Method switch { RefreshMethod.Fast => " FAST", RefreshMethod.Complete => " COMPLETE", _ => " FORCE" });
            if (r.Mode != null) Add(r.Mode == RefreshMode.OnDemand ? " ON DEMAND" : " ON COMMIT");
            if (r.StartWith != null) { Add(" START WITH "); Expr(r.StartWith); }
            if (r.Next != null) { Add(" NEXT "); Expr(r.Next); }
            if (r.WithPrimaryKey) Add(" WITH PRIMARY KEY");
            if (r.WithRowId) Add(" WITH ROWID");
        }

        // ---------- ALTER TABLE ----------

        private void DropTail(AlterTableAction a, bool columns = true)
        {
            if (a.Cascade) Add(columns ? " CASCADE CONSTRAINTS" : " CASCADE"); // DROP CONSTRAINT / PRIMARY KEY / UNIQUE take plain CASCADE (CASCADE CONSTRAINTS is the column form)
            if (a.Online) Add(" ONLINE");
            if (a.Syntax.Count > 0) { Sp(); SyntaxList(a.Syntax); }
        }

        private void AlterAction(AlterTableAction a)
        {
            switch (a.Kind)
            {
                case AlterTableActionKind.AddColumns:
                    Add("ADD (");
                    Sep(a.Columns, ColumnDef);
                    if (a.Constraints.Count > 0) { if (a.Columns.Count > 0) Add(", "); Sep(a.Constraints, Constraint); }
                    Add(")");
                    break;
                case AlterTableActionKind.AddConstraint:
                    Add("ADD ");
                    if (a.Constraints.Count == 1) Constraint(a.Constraints[0]);
                    else { Add("("); Sep(a.Constraints, Constraint); Add(")"); }
                    break;
                case AlterTableActionKind.ModifyColumns:
                    Add("MODIFY ("); Sep(a.Columns, ColumnDef); Add(")");
                    break;
                case AlterTableActionKind.ModifyConstraint:
                    Add("MODIFY ");
                    foreach (var c in a.Constraints) Constraint(c);
                    break;
                case AlterTableActionKind.DropColumns:
                    if (a.Names.Count == 1) { Add("DROP COLUMN "); Id(a.Names[0]); }
                    else { Add("DROP ("); IdList(a.Names); Add(")"); }
                    DropTail(a);
                    break;
                case AlterTableActionKind.DropConstraint: Add("DROP CONSTRAINT "); IdList(a.Names); DropTail(a, false); break;
                case AlterTableActionKind.DropPrimaryKey: Add("DROP PRIMARY KEY"); DropTail(a, false); break;
                case AlterTableActionKind.DropUnique: Add("DROP UNIQUE ("); IdList(a.Names); Add(")"); DropTail(a, false); break;
                case AlterTableActionKind.SetUnused:
                    if (a.Names.Count == 1) { Add("SET UNUSED COLUMN "); Id(a.Names[0]); }
                    else { Add("SET UNUSED ("); IdList(a.Names); Add(")"); }
                    DropTail(a);
                    break;
                case AlterTableActionKind.RenameColumn: Add("RENAME COLUMN "); IdList(a.Names); Add(" TO "); Id(a.NewName!); break;
                case AlterTableActionKind.RenameConstraint: Add("RENAME CONSTRAINT "); IdList(a.Names); Add(" TO "); Id(a.NewName!); break;
                case AlterTableActionKind.RenameTo: Add("RENAME TO "); Id(a.NewName!); break;
                case AlterTableActionKind.Attributes:
                    Sep(a.Attributes, Attribute, " ");
                    break;
                default: SyntaxList(a.Syntax); break;
            }
        }

        // ---------- physical attributes ----------

        private void Attribute(PhysicalAttribute pa)
        {
            switch (pa)
            {
                case TablespaceAttribute ts: Add(ts.Default ? "TABLESPACE DEFAULT " : "TABLESPACE "); Id(ts.Name); break;
                case NumericAttribute na:
                    Add(na.Kind switch
                    {
                        NumericAttributeKind.PctFree => "PCTFREE ", NumericAttributeKind.PctUsed => "PCTUSED ", NumericAttributeKind.InitTrans => "INITRANS ",
                        NumericAttributeKind.MaxTrans => "MAXTRANS ", NumericAttributeKind.PctThreshold => "PCTTHRESHOLD ", _ => "FREELISTS ",
                    });
                    Expr(na.Value);
                    break;
                case StorageAttribute sa:
                    Add("STORAGE (");
                    Sep(sa.Options, o => { Add(o.Name); if (o.Value != null) { Sp(); Expr(o.Value); } }, " ");
                    Add(")");
                    break;
                case LoggingAttribute la: Add(la.Logging ? "LOGGING" : "NOLOGGING"); break;
                case CacheAttribute ca: Add(ca.Cache ? "CACHE" : "NOCACHE"); break;
                case RowMovementAttribute rm: Add(rm.Enabled ? "ENABLE ROW MOVEMENT" : "DISABLE ROW MOVEMENT"); break;
                case ParallelAttribute pr:
                    if (pr.NoParallel) Add("NOPARALLEL");
                    else { Add("PARALLEL"); if (pr.Degree != null) { Sp(); Expr(pr.Degree); } }
                    break;
                case CompressAttribute cp:
                    if (!cp.Compress) { Add("NOCOMPRESS"); break; }
                    Add("COMPRESS");
                    if (cp.PrefixLength != null) { Sp(); Expr(cp.PrefixLength); }
                    else if (cp.Method.Count > 0) { Sp(); SyntaxList(cp.Method); }
                    break;
                case OrganizationAttribute oa:
                    Add("ORGANIZATION " + oa.Organization switch { TableOrganization.Heap => "HEAP", TableOrganization.Index => "INDEX", _ => "EXTERNAL" });
                    if (oa.Details.Count > 0) { Sp(); SyntaxList(oa.Details); }
                    break;
                case PartitionByAttribute pb: PartitionBy(pb); break;
                case OtherAttribute oth: SyntaxList(oth.Items); break;
                default: throw Unsupported(pa);
            }
        }

        private void PartitionMethodText(PartitionMethod m, List<Identifier> cols, Identifier? refConstraint)
        {
            switch (m)
            {
                case PartitionMethod.System: Add("SYSTEM"); break;
                case PartitionMethod.Reference: Add("REFERENCE ("); Id(refConstraint!); Add(")"); break;
                default:
                    Add(m switch { PartitionMethod.Range => "RANGE", PartitionMethod.List => "LIST", _ => "HASH" });
                    Add(" ("); IdList(cols); Add(")");
                    break;
            }
        }

        private void PartitionBy(PartitionByAttribute pb)
        {
            Add("PARTITION BY ");
            PartitionMethodText(pb.Method, pb.Columns, pb.ReferenceConstraint);
            if (pb.Interval != null) { Add(" INTERVAL ("); Expr(pb.Interval); Add(")"); }
            if (pb.SubpartitionMethod != null) { Add(" SUBPARTITION BY "); PartitionMethodText(pb.SubpartitionMethod.Value, pb.SubpartitionColumns, null); }
            if (pb.PartitionCount != null) { Add(" PARTITIONS "); Expr(pb.PartitionCount); }
            if (pb.Partitions.Count > 0) { Add(" ("); Sep(pb.Partitions, p => PartitionDef(p, false)); Add(")"); }
        }

        private void PartitionDef(PartitionDefinition p, bool sub)
        {
            Add(sub ? "SUBPARTITION" : "PARTITION");
            if (p.Name != null) { Sp(); Id(p.Name); }
            if (p.LessThan || p.ValuesDefault || p.Values.Count > 0)
            {
                Add(p.LessThan ? " VALUES LESS THAN (" : " VALUES (");
                if (p.ValuesDefault) Add("DEFAULT"); else Sep(p.Values, e => Expr(e));
                Add(")");
            }
            foreach (var a in p.Attributes) { Sp(); Attribute(a); }
            if (p.Subpartitions.Count > 0) { Add(" ("); Sep(p.Subpartitions, s => PartitionDef(s, true)); Add(")"); }
        }
    }
}
