using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

public class DmlAndSessionShapeTests
{
    private const string SelDual = "(SelectStatement body=(QuerySpecification selectList=[(ExpressionSelectItem expression=1)] from=[(NamedTable name=[DUAL])]))";

    [Theory]
    [InlineData("INSERT INTO t (a, b) VALUES (1, DEFAULT) RETURNING a INTO :x",
        "(InsertStatement target=(NamedTable name=[T]) columns=[A B] valueRows=[[1 DEFAULT]] returning=[A] returningInto=[:x])")]
    [InlineData("INSERT INTO t VALUES (1), (2)", "(InsertStatement target=(NamedTable name=[T]) valueRows=[[1] [2]])")]
    [InlineData("INSERT /*+ APPEND */ INTO t SELECT 1 FROM dual",
        "(InsertStatement target=(NamedTable name=[T]) query=" + SelDual + " hint=(HintClause text=APPEND items=[(HintItem name=APPEND)]))")]
    [InlineData("INSERT INTO t (a) (SELECT 1 FROM dual)",
        "(InsertStatement target=(NamedTable name=[T]) columns=[A] query=(SelectStatement body=(ParenthesizedQuery query=" + SelDual + ")))")]
    [InlineData("INSERT INTO t SELECT 1 FROM dual LOG ERRORS INTO err_t ('tag') REJECT LIMIT UNLIMITED",
        "(InsertStatement target=(NamedTable name=[T]) query=" + SelDual + " errorLogging=(ErrorLogging table=[ERR_T] tag='tag' rejectUnlimited))")]
    [InlineData("INSERT INTO t SELECT 1 FROM dual LOG ERRORS REJECT LIMIT 10",
        "(InsertStatement target=(NamedTable name=[T]) query=" + SelDual + " errorLogging=(ErrorLogging rejectLimit=10))")]
    [InlineData("INSERT INTO t VALUES (1) RETURNING a, b BULK COLLECT INTO v1, v2",
        "(InsertStatement target=(NamedTable name=[T]) valueRows=[[1]] returning=[A B] returningBulkCollect returningInto=[V1 V2])")]
    public void Inserts(string sql, string expected) => Assert.Equal(expected, Shape.Of(sql));

    [Theory]
    [InlineData("INSERT ALL INTO a (x) VALUES (1) INTO b VALUES (2) SELECT 1 FROM dual",
        "(MultiTableInsertStatement branches=[(MultiInsertBranch targets=[(InsertIntoClause table=(NamedTable name=[A]) columns=[X] values=[1]) (InsertIntoClause table=(NamedTable name=[B]) values=[2])])] query=" + SelDual + ")")]
    [InlineData("INSERT FIRST WHEN x > 1 THEN INTO a WHEN x > 0 THEN INTO b ELSE INTO c SELECT 1 FROM dual",
        "(MultiTableInsertStatement first conditional branches=[(MultiInsertBranch when=(> X 1) targets=[(InsertIntoClause table=(NamedTable name=[A]))]) (MultiInsertBranch when=(> X 0) targets=[(InsertIntoClause table=(NamedTable name=[B]))])] else=(MultiInsertBranch targets=[(InsertIntoClause table=(NamedTable name=[C]))]) query=" + SelDual + ")")]
    public void MultitableInserts(string sql, string expected) => Assert.Equal(expected, Shape.Of(sql));

    [Theory]
    [InlineData("UPDATE t SET a = 1, (b, c) = (SELECT 1, 2 FROM dual) WHERE id = 5",
        "(UpdateStatement target=(NamedTable name=[T]) set=[(SetClause columns=[A] value=1) (SetClause columns=[B C] value=(SubqueryExpression query=(SelectStatement body=(QuerySpecification selectList=[(ExpressionSelectItem expression=1) (ExpressionSelectItem expression=2)] from=[(NamedTable name=[DUAL])]))))] where=(= ID 5))")]
    [InlineData("UPDATE t SET a = 1 WHERE CURRENT OF c1", "(UpdateStatement target=(NamedTable name=[T]) set=[(SetClause columns=[A] value=1)] where=(CurrentOfExpression cursor=C1))")]
    [InlineData("UPDATE t SET a = DEFAULT RETURNING a INTO v", "(UpdateStatement target=(NamedTable name=[T]) set=[(SetClause columns=[A] value=DEFAULT)] returning=[A] returningInto=[V])")]
    [InlineData("DELETE FROM t x WHERE x.a = 1 RETURNING a BULK COLLECT INTO tab",
        "(DeleteStatement target=(NamedTable name=[T] alias=X) where=(= X.A 1) returning=[A] returningBulkCollect returningInto=[TAB])")]
    [InlineData("DELETE t", "(DeleteStatement target=(NamedTable name=[T]))")]
    public void UpdatesAndDeletes(string sql, string expected) => Assert.Equal(expected, Shape.Of(sql));

    [Fact]
    public void Merge_Shape() =>
        Assert.Equal(
            "(MergeStatement target=(NamedTable name=[T]) source=(SubqueryTable query=(SelectStatement body=(QuerySpecification selectList=[(StarSelectItem)] from=[(NamedTable name=[S])])) alias=S) on=(paren (= T.ID S.ID)) clauses=[" +
            "(MergeClause matched set=[(SetClause columns=[T.V] value=S.V)] updateWhere=(> S.V 0) deleteWhere=(= T.V 0)) " +
            "(MergeClause insertColumns=[ID V] insertValues=[S.ID S.V] insertWhere=(> S.V 1))])",
            Shape.Of("MERGE INTO t USING (SELECT * FROM s) s ON (t.id = s.id) WHEN MATCHED THEN UPDATE SET t.v = s.v WHERE s.v > 0 DELETE WHERE t.v = 0 WHEN NOT MATCHED THEN INSERT (id, v) VALUES (s.id, s.v) WHERE s.v > 1")
                );

    [Fact]
    public void Merge_LogErrors_Shape() =>
        Assert.Equal(
            "(MergeStatement target=(NamedTable name=[T]) source=(SubqueryTable query=(SelectStatement body=(QuerySpecification selectList=[(StarSelectItem)] from=[(NamedTable name=[S])])) alias=S) on=(paren (= T.ID S.ID)) clauses=[" +
            "(MergeClause matched set=[(SetClause columns=[T.V] value=S.V)])] errorLogging=(ErrorLogging table=[ERR_T] tag='x' rejectLimit=5))",
            Shape.Of("MERGE INTO t USING (SELECT * FROM s) s ON (t.id = s.id) WHEN MATCHED THEN UPDATE SET t.v = s.v LOG ERRORS INTO err_t ('x') REJECT LIMIT 5"));

    [Theory]
    [InlineData("COMMIT", "(CommitStatement)")]
    [InlineData("COMMIT WORK COMMENT 'c'", "(CommitStatement work comment='c')")]
    [InlineData("COMMIT WRITE BATCH NOWAIT", "(CommitStatement write=(CommitWrite wait=NoWait mode=Batch))")]
    [InlineData("COMMIT FORCE 'tx', 5", "(CommitStatement force='tx' forceScn=5)")]
    [InlineData("ROLLBACK", "(RollbackStatement)")]
    [InlineData("ROLLBACK WORK TO SAVEPOINT sp1", "(RollbackStatement work toSavepoint=SP1)")]
    [InlineData("ROLLBACK FORCE 'tx'", "(RollbackStatement force='tx')")]
    [InlineData("SAVEPOINT sp1", "(SavepointStatement name=SP1)")]
    [InlineData("SET TRANSACTION READ ONLY NAME 'rpt'", "(SetTransactionStatement access=ReadOnly name='rpt')")]
    [InlineData("SET TRANSACTION ISOLATION LEVEL SERIALIZABLE", "(SetTransactionStatement isolation=Serializable)")]
    [InlineData("SET TRANSACTION ISOLATION LEVEL READ COMMITTED", "(SetTransactionStatement isolation=ReadCommitted)")]
    [InlineData("SET TRANSACTION READ WRITE USE ROLLBACK SEGMENT rbs1", "(SetTransactionStatement access=ReadWrite rollbackSegment=RBS1)")]
    [InlineData("SET CONSTRAINTS ALL DEFERRED", "(SetConstraintsStatement all deferred)")]
    [InlineData("SET CONSTRAINT a, s.b IMMEDIATE", "(SetConstraintsStatement constraints=[[A] [S B]])")]
    [InlineData("SET ROLE NONE", "(SetRoleStatement none)")]
    [InlineData("SET ROLE ALL EXCEPT r1, r2", "(SetRoleStatement all except=[R1 R2])")]
    [InlineData("SET ROLE r1, r2 IDENTIFIED BY pw", "(SetRoleStatement roles=[(RoleSpec name=R1) (RoleSpec name=R2 identifiedBy=PW)])")]
    public void TransactionControl(string sql, string expected) => Assert.Equal(expected, Shape.Of(sql));

    [Theory]
    [InlineData("ALTER SESSION SET NLS_DATE_FORMAT = 'YYYY-MM-DD' CURRENT_SCHEMA = hr",
        "(AlterSessionStatement kind=Set settings=[(SessionSetting name=[NLS_DATE_FORMAT] value='YYYY-MM-DD') (SessionSetting name=[CURRENT_SCHEMA] value=HR)])")]
    [InlineData("ALTER SESSION ENABLE PARALLEL DML PARALLEL 4", "(AlterSessionStatement kind=Enable feature=ParallelDml parallel=4)")]
    [InlineData("ALTER SESSION DISABLE PARALLEL QUERY", "(AlterSessionStatement kind=Disable feature=ParallelQuery)")]
    [InlineData("ALTER SESSION FORCE PARALLEL DDL", "(AlterSessionStatement kind=Force feature=ParallelDdl)")]
    [InlineData("ALTER SESSION ENABLE COMMIT IN PROCEDURE", "(AlterSessionStatement kind=Enable feature=CommitInProcedure)")]
    [InlineData("ALTER SESSION ENABLE RESUMABLE TIMEOUT 60 NAME 'job'", "(AlterSessionStatement kind=Enable feature=Resumable timeout=60 resumableName='job')")]
    [InlineData("ALTER SESSION CLOSE DATABASE LINK lnk", "(AlterSessionStatement kind=CloseDatabaseLink databaseLink=LNK)")]
    [InlineData("ALTER SESSION ADVISE ROLLBACK", "(AlterSessionStatement kind=Advise advise=Rollback)")]
    [InlineData("ALTER SESSION SYNC WITH PRIMARY", "(AlterSessionStatement kind=SyncWithPrimary)")]
    public void AlterSession(string sql, string expected) => Assert.Equal(expected, Shape.Of(sql));

    [Theory]
    [InlineData("CALL proc(1, 2)", "(CallStatement routine=(FunctionCall name=[PROC] arguments=[(Argument value=1) (Argument value=2)]))")]
    [InlineData("CALL pkg.fn(1) INTO :x", "(CallStatement routine=(FunctionCall name=[PKG FN] arguments=[(Argument value=1)]) into=:x)")]
    [InlineData("EXPLAIN PLAN SET STATEMENT_ID = 'q' INTO plan_table FOR SELECT 1 FROM dual", "(ExplainPlanStatement statementId='q' intoTable=[PLAN_TABLE] statement=" + SelDual + ")")]
    [InlineData("LOCK TABLE a, b IN EXCLUSIVE MODE NOWAIT", "(LockTableStatement tables=[(NamedTable name=[A]) (NamedTable name=[B])] mode=Exclusive noWait)")]
    [InlineData("LOCK TABLE a IN ROW SHARE MODE WAIT 5", "(LockTableStatement tables=[(NamedTable name=[A])] mode=RowShare wait=5)")]
    [InlineData("LOCK TABLE a IN ROW EXCLUSIVE MODE", "(LockTableStatement tables=[(NamedTable name=[A])] mode=RowExclusive)")]
    [InlineData("LOCK TABLE a IN SHARE UPDATE MODE", "(LockTableStatement tables=[(NamedTable name=[A])] mode=ShareUpdate)")]
    [InlineData("LOCK TABLE a IN SHARE ROW EXCLUSIVE MODE", "(LockTableStatement tables=[(NamedTable name=[A])] mode=ShareRowExclusive)")]
    [InlineData("LOCK TABLE a IN SHARE MODE", "(LockTableStatement tables=[(NamedTable name=[A])] mode=Share)")]
    [InlineData("TRUNCATE TABLE s.t PRESERVE MATERIALIZED VIEW LOG DROP ALL STORAGE CASCADE",
        "(TruncateStatement target=Table name=[S T] materializedViewLog=Preserve storage=DropAllStorage cascade)")]
    [InlineData("TRUNCATE CLUSTER c REUSE STORAGE", "(TruncateStatement target=Cluster name=[C] storage=ReuseStorage)")]
    [InlineData("COMMENT ON COLUMN t.c IS 'it''s'", "(CommentStatement target=Column name=[T C] text=it's)")]
    [InlineData("COMMENT ON MATERIALIZED VIEW mv IS 'x'", "(CommentStatement target=MaterializedView name=[MV] text=x)")]
    [InlineData("RENAME a TO b", "(RenameStatement from=[A] to=[B])")]
    [InlineData("PURGE RECYCLEBIN", "(PurgeStatement target=Recyclebin)")]
    [InlineData("PURGE TABLE t", "(PurgeStatement target=Table name=[T])")]
    [InlineData("PURGE TABLESPACE ts USER u", "(PurgeStatement target=Tablespace name=[TS] user=U)")]
    [InlineData("FLASHBACK TABLE t TO BEFORE DROP RENAME TO t2", "(FlashbackTableStatement tables=[[T]] target=BeforeDrop renameTo=T2 before)")]
    [InlineData("FLASHBACK TABLE t1, s.t2 TO TIMESTAMP SYSDATE - 1 ENABLE TRIGGERS", "(FlashbackTableStatement tables=[[T1] [S T2]] target=Timestamp point=(- SYSDATE 1) enableTriggers)")]
    [InlineData("FLASHBACK TABLE t TO BEFORE SCN 123", "(FlashbackTableStatement tables=[[T]] target=Scn point=123 before)")]
    [InlineData("FLASHBACK TABLE t TO RESTORE POINT rp", "(FlashbackTableStatement tables=[[T]] target=RestorePoint point=RP)")]
    public void OtherStatements(string sql, string expected) => Assert.Equal(expected, Shape.Of(sql));

    [Theory]
    [InlineData("GRANT SELECT, INSERT (a, b) ON hr.emp TO scott, PUBLIC WITH GRANT OPTION",
        "(GrantStatement privileges=[(GrantPrivilege name=SELECT) (GrantPrivilege name=INSERT columns=[A B])] object=[HR EMP] grantees=[(Grantee name=SCOTT) (Grantee public)] withGrantOption)")]
    [InlineData("GRANT ALL PRIVILEGES TO u", "(GrantStatement allPrivileges grantees=[(Grantee name=U)])")]
    [InlineData("GRANT CREATE ANY TABLE, dba TO u WITH ADMIN OPTION", "(GrantStatement privileges=[(GrantPrivilege name=CREATE ANY TABLE) (GrantPrivilege name=DBA)] grantees=[(Grantee name=U)] withAdminOption)")]
    [InlineData("GRANT READ ON DIRECTORY d TO u", "(GrantStatement privileges=[(GrantPrivilege name=READ)] objectKind=Directory object=[D] grantees=[(Grantee name=U)])")]
    [InlineData("REVOKE SELECT ON emp FROM scott CASCADE CONSTRAINTS", "(GrantStatement revoke privileges=[(GrantPrivilege name=SELECT)] object=[EMP] grantees=[(Grantee name=SCOTT)] cascadeConstraints)")]
    [InlineData("GRANT SELECT ON t TO u CONTAINER = ALL", "(GrantStatement privileges=[(GrantPrivilege name=SELECT)] object=[T] grantees=[(Grantee name=U)] container=All)")]
    public void Grants(string sql, string expected) => Assert.Equal(expected, Shape.Of(sql));

    [Theory]
    [InlineData("ANALYZE TABLE emp COMPUTE STATISTICS", "(AdminStatement verb=ANALYZE body=[(WordItem value=TABLE) (WordItem value=EMP) (WordItem value=COMPUTE) (WordItem value=STATISTICS)])")]
    [InlineData("AUDIT SELECT ON emp BY ACCESS", "(AdminStatement verb=AUDIT body=[(WordItem value=SELECT) (WordItem value=ON) (WordItem value=EMP) (WordItem value=BY) (WordItem value=ACCESS)])")]
    [InlineData("ADMINISTER KEY MANAGEMENT CREATE KEYSTORE 'k' IDENTIFIED BY pw", "(AdminStatement verb=ADMINISTER body=[(WordItem value=KEY) (WordItem value=MANAGEMENT) (WordItem value=CREATE) (WordItem value=KEYSTORE) (LiteralItem kind=String value=k) (WordItem value=IDENTIFIED) (WordItem value=BY) (WordItem value=PW)])")]
    public void AdminTokenTreeStatements(string sql, string expected) => Assert.Equal(expected, Shape.Of(sql));

    [Fact]
    public void SqlPlus_Commands_And_Terminators()
    {
        var script = Shape.Parse("SET SERVEROUTPUT ON SIZE 1000000\nSPOOL out.log\nPROMPT it's fine\nDEFINE x = 5\n@child.sql\nWHENEVER SQLERROR EXIT FAILURE ROLLBACK\nEXEC dbms_output.put_line('x')\nSELECT &x FROM dual;\n/\n");
        var printed = script.Statements.Select(s => AstPrinter.Print(s)).ToList();
        Assert.Equal(
        [
            "(SqlPlusCommand command=SET arguments=[(WordItem value=SERVEROUTPUT) (WordItem value=ON) (WordItem value=SIZE) (LiteralItem kind=Number value=1000000)])",
            "(SqlPlusCommand command=SPOOL arguments=[(WordItem value=OUT) (SymbolItem symbol=.) (WordItem value=LOG)])",
            "(SqlPlusCommand command=PROMPT arguments=[(LiteralItem kind=String value=it's fine)])",
            "(SqlPlusCommand command=DEFINE arguments=[(WordItem value=X) (SymbolItem symbol==) (LiteralItem kind=Number value=5)])",
            "(SqlPlusCommand command=@ arguments=[(WordItem value=CHILD) (SymbolItem symbol=.) (WordItem value=SQL)])",
            "(SqlPlusCommand command=WHENEVER arguments=[(WordItem value=SQLERROR) (WordItem value=EXIT) (WordItem value=FAILURE) (WordItem value=ROLLBACK)])",
            "(ExecStatement call=(FunctionCall name=[DBMS_OUTPUT PUT_LINE] arguments=[(Argument value='x')]))",
            "(SelectStatement body=(QuerySpecification selectList=[(ExpressionSelectItem expression=(SubstitutionVariable name=x))] from=[(NamedTable name=[DUAL])]))",
        ], printed);
    }
}
