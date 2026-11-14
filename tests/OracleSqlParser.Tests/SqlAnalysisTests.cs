using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

/// <summary>Discovery helpers in <see cref="SqlAnalysis"/>: tables, columns, binds and function calls.</summary>
public class SqlAnalysisTests
{
    private static SqlScript Parse(string sql)
    {
        var r = SqlParser.Parse(sql);
        Assert.True(r.Success, string.Join("; ", r.Errors));
        return r.Script;
    }

    private static string Dotted(IEnumerable<Identifier> parts) => string.Join(".", parts.Select(p => p.Value));

    /// <summary>"Access SCHEMA.NAME@LINK as ALIAS [cte]".</summary>
    private static string T(TableUse t) =>
        $"{t.Access} {Dotted(t.NameParts)}{(t.DbLink is null ? "" : "@" + t.DbLink)}{(t.Alias is null ? "" : " as " + t.Alias)}{(t.IsCteReference ? " [cte]" : "")}";

    /// <summary>"Kind QUAL.NAME:Resolution:SourceName[ write]".</summary>
    private static string C(ColumnUse c) =>
        $"{(c.Qualifier is null ? "" : c.Qualifier + ".")}{c.Name}:{c.Kind}:{c.Resolution}:{c.SourceName ?? "-"}{(c.IsWriteTarget ? ":write" : "")}";

    private static string F(FunctionUse f) =>
        $"{f.Kind} {(f.Qualifier is null ? "" : f.Qualifier + ".")}{f.Name}{(f.IsAnalytic ? " analytic" : "")}";

    private static List<string> Tables(string sql) => SqlAnalysis.GetTables(Parse(sql)).Select(T).ToList();
    private static List<string> Columns(string sql) => SqlAnalysis.GetColumns(Parse(sql)).Select(C).ToList();
    private static List<string> Functions(string sql) => SqlAnalysis.GetFunctions(Parse(sql)).Select(F).ToList();
    private static List<string> Binds(string sql, bool distinct = false) =>
        SqlAnalysis.GetBinds(Parse(sql), distinct).Select(b => (b.Quoted ? "\"" + b.Name + "\"" : b.Name)).ToList();

    // ---- GetTables: basic references ----

    [Fact]
    public void Tables_SimpleSelect_IsRead()
    {
        Assert.Equal(["Read EMP"], Tables("SELECT * FROM emp"));
    }

    [Fact]
    public void Tables_Joins_ListedInSourceOrder()
    {
        Assert.Equal(
            ["Read A", "Read S.B", "Read C"],
            Tables("SELECT 1 FROM a JOIN s.b ON a.x = b.x LEFT JOIN c USING (x)"));
    }

    [Fact]
    public void Tables_AliasAndDbLink_Reported()
    {
        Assert.Equal(["Read SCH.TAB@REMOTE as T"], Tables("SELECT * FROM sch.tab@remote t"));
    }

    [Fact]
    public void Tables_DbLinkAndAlias_OfDifferentTables()
    {
        Assert.Equal(
            ["Read REMOTE_T@DBLINK_NAME as D", "Read LOCAL_T as L"],
            Tables("SELECT * FROM remote_t@dblink_name d JOIN local_t l ON d.id = l.id"));
    }

    [Fact]
    public void Tables_ScalarInExistsSubqueries_AllRead()
    {
        Assert.Equal(
            ["Read T1", "Read T2", "Read T3", "Read T4"],
            Tables("SELECT (SELECT max(a) FROM t1) FROM t2 WHERE x IN (SELECT y FROM t3) AND EXISTS (SELECT 1 FROM t4)"));
    }

    [Fact]
    public void Tables_QuotedIdentifiers_PreserveCase()
    {
        var t = SqlAnalysis.GetTables(Parse("SELECT * FROM \"MixedCase\".\"Tab\"")).Single();
        Assert.Equal("MixedCase", t.Schema);
        Assert.Equal("Tab", t.Name);
        Assert.Equal("Read MixedCase.Tab", T(t));
    }

    [Fact]
    public void Tables_SchemaQualified_SplitsSchemaAndName()
    {
        var t = SqlAnalysis.GetTables(Parse("SELECT * FROM hr.employees")).Single();
        Assert.Equal("HR", t.Schema);
        Assert.Equal("EMPLOYEES", t.Name);
        Assert.Equal("HR.EMPLOYEES", Dotted(t.NameParts));
    }

    [Fact]
    public void Tables_UnqualifiedName_HasNoSchema()
    {
        var t = SqlAnalysis.GetTables(Parse("SELECT * FROM emp")).Single();
        Assert.Null(t.Schema);
        Assert.Null(t.DbLink);
        Assert.Null(t.Alias);
    }

    [Fact]
    public void Tables_Span_CoversNameOnly()
    {
        const string sql = "SELECT * FROM sch.tab@remote t";
        var t = SqlAnalysis.GetTables(Parse(sql)).Single();
        Assert.Equal("sch.tab", sql.Substring(t.Start, t.End - t.Start));
    }

    [Fact]
    public void Tables_FlashbackAndSample_ReadTable()
    {
        Assert.Equal(["Read T"], Tables("SELECT * FROM t AS OF TIMESTAMP SYSTIMESTAMP"));
        Assert.Equal(["Read T"], Tables("SELECT * FROM t SAMPLE (10)"));
    }

    [Fact]
    public void Tables_Pivot_SourceIsRead()
    {
        Assert.Equal(["Read SALES"], Tables("SELECT * FROM sales PIVOT (SUM(amt) FOR region IN ('E' AS east, 'W' AS west))"));
    }

    [Fact]
    public void Tables_JsonTableAndXmlTable_AreNotTables()
    {
        Assert.Equal(["Read T"], Tables("SELECT jt.name FROM t, JSON_TABLE(t.doc, '$[*]' COLUMNS (name VARCHAR2(10) PATH '$.name')) jt"));
        Assert.Equal(["Read T"], Tables("SELECT x.v FROM t, XMLTABLE('/a' PASSING t.doc COLUMNS v VARCHAR2(10) PATH 'b') x"));
    }

    [Fact]
    public void Tables_ApplyAndLateral_RightSideIsRead()
    {
        Assert.Equal(["Read A", "Read B"], Tables("SELECT * FROM a CROSS APPLY (SELECT * FROM b WHERE b.id = a.id) x"));
        Assert.Equal(["Read A", "Read B"], Tables("SELECT * FROM a OUTER APPLY (SELECT * FROM b WHERE b.id = a.id) x"));
        Assert.Equal(["Read A", "Read B"], Tables("SELECT * FROM a, LATERAL (SELECT * FROM b WHERE b.id = a.id) x"));
    }

    // ---- GetTables: CTEs ----

    [Fact]
    public void Tables_CteReference_IsFlaggedNotATable()
    {
        Assert.Equal(
            ["Read BASE", "Read C [cte]", "Read OTHER"],
            Tables("WITH c AS (SELECT * FROM base) SELECT * FROM c JOIN other ON 1 = 1"));
    }

    [Fact]
    public void Tables_CteNamedLikeTable_ShadowsOnlyInsideWith()
    {
        // Inside the WITH, EMP is the CTE; the EMP after it is the real table.
        Assert.Equal(
            ["Read DUAL", "Read EMP [cte]", "Read EMP as E2"],
            Tables("SELECT * FROM (WITH emp AS (SELECT 1 x FROM dual) SELECT x FROM emp) q, emp e2"));
    }

    [Fact]
    public void Tables_CteNamedLikeTable_OutsideWithIsTable()
    {
        // The WITH covers only its own statement; the next statement's EMP is the table.
        Assert.Equal(
            ["Read DUAL", "Read EMP [cte]", "Read EMP"],
            Tables("WITH emp AS (SELECT 1 x FROM dual) SELECT x FROM emp; SELECT * FROM emp"));
    }

    [Fact]
    public void Tables_NestedWith_InnerCteShadowsOuter()
    {
        const string sql = "WITH c AS (SELECT 1 x FROM dual) SELECT * FROM (WITH c AS (SELECT * FROM real_c) SELECT * FROM c) q";
        var refs = SqlAnalysis.GetTables(Parse(sql));
        var cteRef = Assert.Single(refs, t => t.IsCteReference);
        // The reference inside the inner WITH resolves to the inner definition, which reads REAL_C.
        Assert.Equal(sql.IndexOf("c AS (SELECT * FROM real_c", StringComparison.Ordinal), cteRef.Cte!.Name.Start);
        Assert.Contains("Read REAL_C", refs.Select(T));
    }

    [Fact]
    public void Tables_NestedWith_OuterCteVisibleAfterInnerWith()
    {
        const string sql = "WITH c AS (SELECT 1 x FROM dual) SELECT * FROM (WITH c AS (SELECT * FROM real_c) SELECT * FROM c) q JOIN c ON 1 = 1";
        var refs = SqlAnalysis.GetTables(Parse(sql));
        var last = refs.Last(t => t.IsCteReference);
        Assert.Equal(sql.IndexOf("c AS (SELECT 1 x", StringComparison.Ordinal), last.Cte!.Name.Start);
    }

    [Fact]
    public void Tables_RecursiveCte_SelfReferenceIsCteReference()
    {
        Assert.Equal(
            ["Read DUAL", "Read R [cte]", "Read R [cte]"],
            Tables("WITH r(n) AS (SELECT 1 FROM dual UNION ALL SELECT n + 1 FROM r WHERE n < 5) SELECT n FROM r"));
    }

    [Fact]
    public void Tables_CteReferencesEarlierCte()
    {
        Assert.Equal(
            ["Read BASE", "Read A [cte]", "Read A [cte]", "Read B [cte]"],
            Tables("WITH a AS (SELECT * FROM base), b AS (SELECT * FROM a) SELECT * FROM a JOIN b ON 1 = 1"));
    }

    [Fact]
    public void Tables_CteWithQualifiedName_IsTable()
    {
        // A schema-qualified or dblink name can never be a WITH name.
        Assert.Equal(["Read DUAL", "Read C [cte]", "Read S.C"], Tables("WITH c AS (SELECT 1 x FROM dual) SELECT * FROM c, s.c"));
    }

    // ---- GetTables: DML ----

    [Fact]
    public void Tables_InsertTargetAndSource()
    {
        Assert.Equal(["Insert TGT", "Read SRC"], Tables("INSERT INTO tgt (a) SELECT a FROM src"));
    }

    [Fact]
    public void Tables_InsertWithReturning_TargetOnly()
    {
        Assert.Equal(["Insert T"], Tables("INSERT INTO t (a) VALUES (1) RETURNING a INTO :x"));
    }

    [Fact]
    public void Tables_UpdateWithReturningAndSubquery()
    {
        Assert.Equal(["Update T", "Read S"], Tables("UPDATE t SET a = 1 WHERE b IN (SELECT b FROM s) RETURNING a INTO :v"));
    }

    [Fact]
    public void Tables_Update23aiFrom_SourceIsRead()
    {
        Assert.Equal(["Update TGT as T", "Read SRC as S"], Tables("UPDATE tgt t SET t.v = s.v FROM src s WHERE s.id = t.id"));
    }

    [Fact]
    public void Tables_Delete_TargetAndSubquery()
    {
        Assert.Equal(["Delete T", "Read S"], Tables("DELETE FROM t WHERE id IN (SELECT id FROM s)"));
    }

    [Fact]
    public void Tables_Merge_TargetWrittenSourceRead()
    {
        Assert.Equal(
            ["Merge TGT as T", "Read SRC as S"],
            Tables("MERGE INTO tgt t USING src s ON (t.id = s.id) WHEN MATCHED THEN UPDATE SET t.v = s.v"));
    }

    [Fact]
    public void Tables_Merge_SubquerySource()
    {
        Assert.Equal(
            ["Merge TGT", "Read S2"],
            Tables("MERGE INTO tgt USING (SELECT * FROM s2) s ON (tgt.id = s.id) WHEN NOT MATCHED THEN INSERT (id) VALUES (s.id)"));
    }

    [Fact]
    public void Tables_InsertAll_EveryTargetIsInsert()
    {
        Assert.Equal(
            ["Insert T1", "Insert T2", "Read SRC"],
            Tables("INSERT ALL INTO t1 VALUES (a) INTO t2 VALUES (b) SELECT a, b FROM src"));
    }

    [Fact]
    public void Tables_InsertFirst_ConditionalBranches()
    {
        Assert.Equal(
            ["Insert HI", "Insert LO", "Read SRC"],
            Tables("INSERT FIRST WHEN a > 5 THEN INTO hi VALUES (a) ELSE INTO lo VALUES (a) SELECT a FROM src"));
    }

    // ---- GetTables: PL/SQL and DDL ----

    [Fact]
    public void Tables_PlsqlBlock_SelectIntoAndDml()
    {
        Assert.Equal(
            ["Read EMP", "Insert LOG"],
            Tables("BEGIN SELECT a INTO v FROM emp WHERE id = 1; INSERT INTO log VALUES (1); END;"));
    }

    [Fact]
    public void Tables_CursorDeclaration_IsRead()
    {
        Assert.Equal(["Read EMP"], Tables("DECLARE CURSOR c IS SELECT * FROM emp; BEGIN OPEN c; END;"));
    }

    [Fact]
    public void Tables_ExecuteImmediateString_IsNotParsed()
    {
        // Documented limitation: the SQL in a dynamic string is a literal and is not analysed.
        Assert.Empty(Tables("BEGIN EXECUTE IMMEDIATE 'SELECT * FROM hidden'; END;"));
    }

    [Fact]
    public void Tables_CreateTableAs_TargetDdlAndSourceRead()
    {
        Assert.Equal(["Ddl T2", "Read T1"], Tables("CREATE TABLE t2 AS SELECT * FROM t1"));
    }

    [Fact]
    public void Tables_CreateView_IsDdl()
    {
        Assert.Equal(["Ddl V", "Read T"], Tables("CREATE OR REPLACE VIEW v AS SELECT * FROM t"));
    }

    [Fact]
    public void Tables_DdlStatements_NameTheirObject()
    {
        Assert.Equal(["Ddl T"], Tables("ALTER TABLE t ADD (x NUMBER)"));
        Assert.Equal(["Ddl T"], Tables("DROP TABLE t PURGE"));
        Assert.Equal(["Ddl T"], Tables("TRUNCATE TABLE t"));
        Assert.Equal(["Ddl T"], Tables("CREATE INDEX ix ON t (a)"));
        Assert.Equal(["Ddl T"], Tables("LOCK TABLE t IN EXCLUSIVE MODE"));
        Assert.Equal(["Ddl T"], Tables("FLASHBACK TABLE t TO SCN 1"));
    }

    [Fact]
    public void Tables_CommentOnColumn_TableIsEveryPartButLast()
    {
        var t = SqlAnalysis.GetTables(Parse("COMMENT ON COLUMN s.t.c IS 'x'")).Single();
        Assert.Equal(TableAccess.Ddl, t.Access);
        Assert.Equal("S", t.Schema);
        Assert.Equal("T", t.Name);
    }

    [Fact]
    public void Tables_ForeignKeyReference_IsDdl()
    {
        Assert.Equal(["Ddl T", "Ddl P"], Tables("CREATE TABLE t (pid NUMBER REFERENCES p (id))"));
    }

    // ---- GetBinds ----

    [Fact]
    public void Binds_InSourceOrder_NotTreeOrder()
    {
        // RETURNING is last in the text but is a child of the DML node that the walk visits first.
        Assert.Equal(["b", "a", "c"], Binds("UPDATE t SET x = :b WHERE id = :a RETURNING id INTO :c"));
    }

    [Fact]
    public void Binds_Positions_CoverColonAndName()
    {
        const string sql = "SELECT :x FROM t";
        var b = Assert.Single(SqlAnalysis.GetBinds(Parse(sql)));
        Assert.Equal(7, b.Start);
        Assert.Equal(9, b.End);
        Assert.Equal(":x", sql.Substring(b.Start, b.End - b.Start));
    }

    [Fact]
    public void Binds_Distinct_CaseInsensitiveUnquotedQuotedExact()
    {
        const string sql = "SELECT :a, :A, :\"a\", :1, :1 FROM dual";
        Assert.Equal(["a", "A", "\"a\"", "1", "1"], Binds(sql));
        Assert.Equal(["a", "\"a\"", "1"], Binds(sql, distinct: true));
    }

    [Fact]
    public void Binds_NumericAndQuoted_NameWithoutColonOrQuotes()
    {
        var all = SqlAnalysis.GetBinds(Parse("SELECT :1, :\"Mixed\" FROM dual"));
        Assert.Equal("1", all[0].Name);
        Assert.False(all[0].Quoted);
        Assert.Equal("Mixed", all[1].Name);
        Assert.True(all[1].Quoted);
    }

    [Fact]
    public void Binds_NewColonField_ReportedAsWritten()
    {
        // Documented: the parser reads :new.col (trigger correlation) as a bind named "new.col".
        Assert.Equal(["new.col"], Binds("SELECT :new.col FROM dual"));
    }

    [Fact]
    public void Binds_NotPresent_EmptyList()
    {
        Assert.Empty(SqlAnalysis.GetBinds(Parse("SELECT 1 FROM dual")));
    }

    // ---- GetFunctions ----

    [Fact]
    public void Functions_PlainAndQualifiedNames()
    {
        Assert.Equal(
            ["Function UPPER", "Function PKG.FN", "Function SCH.PKG.FN2"],
            Functions("SELECT upper(a), pkg.fn(1), sch.pkg.fn2(2) FROM dual"));
    }

    [Fact]
    public void Functions_NameParts_KeepEachPart()
    {
        var f = SqlAnalysis.GetFunctions(Parse("SELECT sch.pkg.fn2(2) FROM dual")).Single();
        Assert.Equal(["SCH", "PKG", "FN2"], f.NameParts.Select(p => p.Value));
        Assert.Equal("SCH.PKG", f.Qualifier);
        Assert.Equal("FN2", f.Name);
        Assert.Equal("sch.pkg.fn2", "SELECT sch.pkg.fn2(2) FROM dual".Substring(f.Start, f.End - f.Start));
    }

    [Fact]
    public void Functions_Analytic_HasOver()
    {
        Assert.Equal(
            ["Function SUM analytic", "Function MAX"],
            Functions("SELECT sum(x) OVER (PARTITION BY y), max(x) FROM t"));
    }

    [Fact]
    public void Functions_NestedCalls_BothReported()
    {
        Assert.Equal(["Function F", "Function G"], Functions("SELECT f(g(1)) FROM dual"));
    }

    [Fact]
    public void Functions_Constructor_IsConstructorKind()
    {
        Assert.Equal(["Constructor T_OBJ"], Functions("SELECT NEW t_obj(1) FROM dual"));
    }

    [Fact]
    public void Functions_MethodCall_IsMethodKind()
    {
        Assert.Equal(["Function UTL.OBJ", "Method FOO"], Functions("BEGIN v := utl.obj(1).foo(); END;"));
    }

    [Fact]
    public void Functions_ProcedureStatements_IncludingBareName()
    {
        Assert.Equal(
            ["Procedure PKG.DO_IT", "Procedure CLEANUP"],
            Functions("BEGIN pkg.do_it(1); cleanup; END;"));
    }

    [Fact]
    public void Functions_CastAndTrim_AreNotCalls()
    {
        // CAST and TRIM are dedicated syntax nodes, not FunctionCall.
        Assert.Equal(["Function UPPER"], Functions("SELECT CAST(a AS NUMBER), upper(b), TRIM(c) FROM t"));
    }

    [Fact]
    public void Functions_BulkRowcountAttribute_IsNotACall()
    {
        var functions = Functions("BEGIN FORALL i IN 1..n DELETE FROM t WHERE k = x(i); v := SQL%BULK_ROWCOUNT(1); END;");
        Assert.Equal(["Function X"], functions);
    }

    [Fact]
    public void Binds_IntoTarget_IsStillABind()
    {
        Assert.Equal(["x"], Binds("BEGIN SELECT a INTO :x FROM t; END;"));
    }

    // ---- GetColumns: resolution ----

    [Fact]
    public void Columns_QualifiedByAlias_Resolved()
    {
        Assert.Equal(["E.ID:Column:Resolved:E"], Columns("SELECT e.id FROM emp e"));
    }

    [Fact]
    public void Columns_QualifiedByTableName_WhenUnaliased()
    {
        Assert.Equal(["EMP.ID:Column:Resolved:HR.EMP"], Columns("SELECT emp.id FROM hr.emp"));
    }

    [Fact]
    public void Columns_TableNameIsNotAQualifier_WhenAliased()
    {
        // Oracle: an aliased table is known only by its alias, so the table name does not resolve.
        Assert.Equal(["EMP.ID:Column:Unresolved:-"], Columns("SELECT emp.id FROM hr.emp e"));
    }

    [Fact]
    public void Columns_UnqualifiedSingleSource_Resolved()
    {
        Assert.Equal(["ID:Column:Resolved:EMP"], Columns("SELECT id FROM emp"));
    }

    [Fact]
    public void Columns_UnqualifiedWithTwoSources_Ambiguous()
    {
        Assert.Equal(["ID:Column:Ambiguous:-"], Columns("SELECT id FROM a JOIN b ON 1 = 1"));
    }

    [Fact]
    public void Columns_JoinCondition_BothSidesQualified()
    {
        Assert.Equal(
            ["A.X:Column:Resolved:A", "B.ID:Column:Resolved:B", "A.ID:Column:Resolved:A"],
            Columns("SELECT a.x FROM a JOIN b ON b.id = a.id"));
    }

    [Fact]
    public void Columns_CorrelatedScalarSubquery_OuterAliasResolves()
    {
        Assert.Equal(
            ["X.V:Column:Resolved:X", "X.K:Column:Resolved:X", "T1.K:Column:Resolved:T1"],
            Columns("SELECT (SELECT max(x.v) FROM t2 x WHERE x.k = t1.k) FROM t1"));
    }

    [Fact]
    public void Columns_SubqueryWithoutFrom_ResolvesToOuterSource()
    {
        Assert.Equal(["K:Column:Resolved:T1"], Columns("SELECT (SELECT k + 1) FROM t1"));
    }

    [Fact]
    public void Columns_InnermostWithTwoSources_Ambiguous_OuterStillResolves()
    {
        Assert.Equal(
            ["ID:Column:Resolved:A", "ID:Column:Ambiguous:-"],
            Columns("SELECT id FROM a WHERE EXISTS (SELECT 1 FROM b, c WHERE id = 1)"));
    }

    [Fact]
    public void Columns_DerivedTable_InnerAndOuterResolve()
    {
        Assert.Equal(
            ["S.A:Column:Resolved:S", "A:Column:Resolved:T"],
            Columns("SELECT s.a FROM (SELECT a FROM t) s"));
    }

    [Fact]
    public void Columns_ApplyRightSide_SeesLeftSource()
    {
        Assert.Equal(
            ["B.V:Column:Resolved:B", "B.ID:Column:Resolved:B", "A.ID:Column:Resolved:A"],
            Columns("SELECT * FROM a CROSS APPLY (SELECT b.v FROM b WHERE b.id = a.id) x"));
    }

    [Fact]
    public void Columns_NoFromAtTopLevel_Unresolved()
    {
        Assert.Equal(["A:Column:Unresolved:-"], Columns("SELECT a"));
    }

    [Fact]
    public void Columns_ExpressionRoot_ReportedEvenWithoutStatement()
    {
        var c = Assert.Single(SqlAnalysis.GetColumns(SqlParser.ParseExpression("e.id + 1")));
        Assert.Equal(ColumnResolution.Unresolved, c.Resolution);
        Assert.Equal("ID", c.Name);
    }

    [Fact]
    public void Columns_QuotedQualifier_IsCaseSensitive()
    {
        Assert.Equal(["Al.Col:Column:Resolved:Al"], Columns("SELECT \"Al\".\"Col\" FROM emp \"Al\""));
        Assert.Equal(["e.X:Column:Unresolved:-"], Columns("SELECT \"e\".x FROM emp e"));
    }

    [Fact]
    public void Columns_WindowSpec_PartitionAndOrderResolve()
    {
        Assert.Equal(
            ["X:Column:Resolved:T", "Y:Column:Resolved:T", "Z:Column:Resolved:T"],
            Columns("SELECT sum(x) OVER (PARTITION BY y ORDER BY z) FROM t"));
    }

    // ---- GetColumns: exclusions and special kinds ----

    [Fact]
    public void Columns_FunctionNamesAndBinds_Excluded()
    {
        Assert.Equal(["NAME:Column:Resolved:EMP", "ID:Column:Resolved:EMP"], Columns("SELECT upper(name) FROM emp WHERE id = :b"));
    }

    [Fact]
    public void Columns_IntoTargets_AreVariablesNotColumns()
    {
        Assert.Equal(
            ["A:Column:Resolved:T", "K:Column:Resolved:T"],
            Columns("BEGIN SELECT a INTO v FROM t WHERE k = 1; END;"));
    }

    [Fact]
    public void Columns_PlsqlOutsideSql_NotReported()
    {
        Assert.Empty(Columns("BEGIN v := 1; IF x > 0 THEN NULL; END IF; END;"));
    }

    [Fact]
    public void Columns_SequenceValues_ReportedAsSequence()
    {
        Assert.Equal(
            ["SEQ.NEXTVAL:SequenceValue:NotApplicable:-", "S.SEQ2.CURRVAL:SequenceValue:NotApplicable:-"],
            Columns("SELECT seq.NEXTVAL, s.seq2.CURRVAL FROM dual"));
    }

    [Fact]
    public void Columns_SequenceInPlsqlAssignment_StillReported()
    {
        Assert.Equal(["SEQ.NEXTVAL:SequenceValue:NotApplicable:-"], Columns("BEGIN v := seq.NEXTVAL; END;"));
    }

    [Fact]
    public void Columns_PseudoColumns_NotColumns()
    {
        Assert.Equal(
            ["ROWNUM:PseudoColumn:NotApplicable:-", "ROWID:PseudoColumn:NotApplicable:-"],
            Columns("SELECT ROWNUM, rowid FROM t"));
    }

    [Fact]
    public void Columns_SelectAliasInOrderBy_ReportedAsAlias()
    {
        Assert.Equal(
            ["A:Column:Resolved:T", "B:Column:Resolved:T", "TOTAL:SelectAlias:NotApplicable:-", "B:Column:Resolved:T"],
            Columns("SELECT a AS total, b FROM t ORDER BY total, b"));
    }

    [Fact]
    public void Columns_SelectAliasInOrderByExpression_ReportedAsAlias()
    {
        Assert.Equal(
            ["A:Column:Resolved:T", "TOTAL:SelectAlias:NotApplicable:-"],
            Columns("SELECT a AS total FROM t ORDER BY total + 1"));
    }

    [Fact]
    public void Columns_OrderByNameThatIsNotAlias_Resolved()
    {
        Assert.Equal(["A:Column:Resolved:T", "A:Column:Resolved:T"], Columns("SELECT a FROM t ORDER BY a"));
    }

    [Fact]
    public void Columns_CheckConstraint_NotReported()
    {
        Assert.Empty(Columns("CREATE TABLE t (a NUMBER CHECK (a > 0))"));
    }

    [Fact]
    public void Columns_CreateViewQuery_Resolved()
    {
        Assert.Equal(["A:Column:Resolved:T"], Columns("CREATE VIEW v AS SELECT a FROM t"));
    }

    [Fact]
    public void Columns_CteBody_ResolvesAndCteReferenceIsASource()
    {
        Assert.Equal(
            ["X:Column:Resolved:T", "C.X:Column:Resolved:C"],
            Columns("WITH c AS (SELECT x FROM t) SELECT c.x FROM c"));
    }

    [Fact]
    public void Columns_RecursiveCteSearchClause_Unresolved()
    {
        Assert.Equal(
            ["N:Column:Resolved:R", "N:Column:Unresolved:-", "N:Column:Resolved:R"],
            Columns("WITH r (n) AS (SELECT 1 FROM dual UNION ALL SELECT n + 1 FROM r) SEARCH DEPTH FIRST BY n SET ord SELECT n FROM r"));
    }

    // ---- GetColumns: DML ----

    [Fact]
    public void Columns_UpdateSetTarget_IsWriteTarget()
    {
        Assert.Equal(
            ["SAL:Column:Resolved:EMP:write", "SAL:Column:Resolved:EMP", "ID:Column:Resolved:EMP"],
            Columns("UPDATE emp SET sal = sal + 1 WHERE id = 3"));
    }

    [Fact]
    public void Columns_InsertColumnList_IsWriteTarget()
    {
        Assert.Equal(["A:Column:Resolved:T:write", "B:Column:Resolved:T:write"], Columns("INSERT INTO t (a, b) VALUES (1, 2)"));
    }

    [Fact]
    public void Columns_Returning_ResolvesToDmlTarget()
    {
        Assert.Equal(
            ["A:Column:Resolved:T:write", "A:Column:Resolved:T"],
            Columns("UPDATE t SET a = 1 RETURNING a INTO v"));
    }

    [Fact]
    public void Columns_Merge_ClausesResolveToTargetAndSource()
    {
        Assert.Equal(
            [
                "T.ID:Column:Resolved:T", "S.ID:Column:Resolved:S",
                "T.V:Column:Resolved:T:write", "S.V:Column:Resolved:S",
                "ID:Column:Resolved:T:write", "V:Column:Resolved:T:write",
                "S.ID:Column:Resolved:S", "S.V:Column:Resolved:S",
            ],
            Columns("MERGE INTO tgt t USING src s ON (t.id = s.id) WHEN MATCHED THEN UPDATE SET t.v = s.v "
                + "WHEN NOT MATCHED THEN INSERT (id, v) VALUES (s.id, s.v)"));
    }

    [Fact]
    public void Columns_InsertAll_ValuesReadTheSubquery()
    {
        Assert.Equal(
            [
                "A:Column:Resolved:SRC", "B:Column:Resolved:SRC",
                "A:Column:Resolved:SRC", "B:Column:Resolved:SRC",
                "A:Column:Resolved:SRC", "B:Column:Resolved:SRC",
            ],
            Columns("INSERT ALL INTO t1 VALUES (a, b) INTO t2 VALUES (a, b) SELECT a, b FROM src"));
    }

    // ---- GetColumns: table functions and special sources ----

    [Fact]
    public void Columns_PivotForAndAggregateColumns_ResolveToPivotSource()
    {
        Assert.Equal(
            ["AMT:Column:Resolved:SALES", "REGION:Column:Resolved:SALES"],
            Columns("SELECT * FROM sales PIVOT (SUM(amt) FOR region IN ('E' AS east))"));
    }

    [Fact]
    public void Columns_JsonTable_OutputIsSourceNotTableColumn()
    {
        // The COLUMNS names of JSON_TABLE are PATH columns, not references, so only the uses of jt are reported.
        Assert.Equal(
            ["JT.NAME:Column:Resolved:JT", "T.DOC:Column:Resolved:T"],
            Columns("SELECT jt.name FROM t, JSON_TABLE(t.doc, '$[*]' COLUMNS (name VARCHAR2(10) PATH '$.name')) jt"));
    }

    [Fact]
    public void Columns_GraphTableVariable_Unresolved()
    {
        Assert.Equal(
            ["V.NAME:Column:Unresolved:-"],
            Columns("SELECT * FROM GRAPH_TABLE (g MATCH (v IS person) COLUMNS (v.name AS n))"));
    }
}
