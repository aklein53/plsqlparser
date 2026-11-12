using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

public class PlsqlShapeTests
{
    private static Block FirstBlock(string sql) => Shape.Parse(sql).Descendants().OfType<Block>().First();
    private static string Stmts(string body) => AstPrinter.Print(FirstBlock("BEGIN " + body + " END;").Statements);

    [Fact]
    public void Declarations_AllKinds()
    {
        var b = FirstBlock(@"DECLARE
  v_n NUMBER := 0;
  c_max CONSTANT PLS_INTEGER := 10;
  v_name emp.ename%TYPE;
  r emp%ROWTYPE;
  e_bad EXCEPTION;
  PRAGMA EXCEPTION_INIT(e_bad, -20001);
  CURSOR c1 (p NUMBER) IS SELECT 1 FROM dual;
  TYPE t_tab IS TABLE OF NUMBER INDEX BY PLS_INTEGER;
  TYPE t_rec IS RECORD (a NUMBER, b VARCHAR2(10) NOT NULL := 'x');
  TYPE t_cur IS REF CURSOR RETURN emp%ROWTYPE;
  TYPE t_va IS VARRAY(5) OF VARCHAR2(3) NOT NULL;
  SUBTYPE small IS NUMBER(3) RANGE 1..10 NOT NULL;
BEGIN NULL; END;");
        Assert.True(b.HasDeclare);
        Assert.Equal(
            "[(VariableDeclaration name=V_N type=(DataType name=NUMBER) default=0) " +
            "(VariableDeclaration name=C_MAX constant type=(DataType name=PLS_INTEGER) default=10) " +
            "(VariableDeclaration name=V_NAME type=(DataType name=EMP.ENAME attribute=TYPE)) " +
            "(VariableDeclaration name=R type=(DataType name=EMP attribute=ROWTYPE)) " +
            "(ExceptionDeclaration name=E_BAD) " +
            "(PragmaDeclaration name=EXCEPTION_INIT arguments=[E_BAD (- 20001)]) " +
            "(CursorDeclaration name=C1 parameters=[(Parameter name=P type=(DataType name=NUMBER))] query=(SelectStatement body=(QuerySpecification selectList=[(ExpressionSelectItem expression=1)] from=[(NamedTable name=[DUAL])]))) " +
            "(CollectionTypeDeclaration name=T_TAB kind=Table elementType=(DataType name=NUMBER) indexBy=(DataType name=PLS_INTEGER)) " +
            "(RecordTypeDeclaration name=T_REC fields=[(VariableDeclaration name=A type=(DataType name=NUMBER)) (VariableDeclaration name=B type=(DataType name=VARCHAR2 arguments=[10]) notNull default='x')]) " +
            "(RefCursorTypeDeclaration name=T_CUR returnType=(DataType name=EMP attribute=ROWTYPE)) " +
            "(CollectionTypeDeclaration name=T_VA kind=Varray limit=5 elementType=(DataType name=VARCHAR2 arguments=[3]) notNull) " +
            "(SubtypeDeclaration name=SMALL baseType=(DataType name=NUMBER arguments=[3]) rangeLow=1 rangeHigh=10 notNull)]",
            AstPrinter.Print(b.Declarations));
    }

    [Fact]
    public void NestedSubprograms_InDeclarations()
    {
        var b = FirstBlock("DECLARE PROCEDURE p(x IN OUT NOCOPY NUMBER) IS BEGIN x := 1; END p; FUNCTION f RETURN NUMBER; BEGIN NULL; END;");
        Assert.Equal(
            "[(SubprogramDeclaration name=[P] parameters=[(Parameter name=X mode=InOut noCopy type=(DataType name=NUMBER))] body=(Block statements=[(AssignmentStatement target=X value=1)] endName=P)) " +
            "(SubprogramDeclaration isFunction name=[F] returnType=(DataType name=NUMBER))]",
            AstPrinter.Print(b.Declarations));
    }

    [Fact]
    public void ExceptionHandlers()
    {
        var b = FirstBlock("BEGIN NULL; EXCEPTION WHEN no_data_found OR too_many_rows THEN NULL; WHEN pkg.my_err THEN RAISE; WHEN OTHERS THEN RAISE; END;");
        Assert.Equal(
            "[(ExceptionHandler exceptions=[[NO_DATA_FOUND] [TOO_MANY_ROWS]] statements=[(NullStatement)]) " +
            "(ExceptionHandler exceptions=[[PKG MY_ERR]] statements=[(RaiseStatement)]) " +
            "(ExceptionHandler others statements=[(RaiseStatement)])]",
            AstPrinter.Print(b.Handlers));
    }

    [Theory]
    [InlineData("x := 1; y.z := x + 1;", "[(AssignmentStatement target=X value=1) (AssignmentStatement target=Y.Z value=(+ X 1))]")]
    [InlineData("arr(i) := 2; arr(i).f := 3; :new.c := 4;",
        "[(AssignmentStatement target=(FunctionCall name=[ARR] arguments=[(Argument value=I)]) value=2) " +
        "(AssignmentStatement target=(MemberAccess target=(FunctionCall name=[ARR] arguments=[(Argument value=I)]) member=F) value=3) " +
        "(AssignmentStatement target=:new.c value=4)]")]
    [InlineData("IF a > 1 THEN b := 1; ELSIF a < 0 THEN b := 2; ELSE b := 3; END IF;",
        "[(IfStatement condition=(> A 1) thenStatements=[(AssignmentStatement target=B value=1)] elsifBranches=[(IfBranch condition=(< A 0) statements=[(AssignmentStatement target=B value=2)])] elseStatements=[(AssignmentStatement target=B value=3)])]")]
    [InlineData("CASE x WHEN 1 THEN y := 1; WHEN 2 THEN NULL; ELSE y := 3; END CASE;",
        "[(PlsqlCaseStatement operand=X whens=[(CaseStatementWhen when=1 statements=[(AssignmentStatement target=Y value=1)]) (CaseStatementWhen when=2 statements=[(NullStatement)])] elseStatements=[(AssignmentStatement target=Y value=3)])]")]
    [InlineData("CASE WHEN a THEN NULL; END CASE;", "[(PlsqlCaseStatement whens=[(CaseStatementWhen when=A statements=[(NullStatement)])])]")]
    [InlineData("LOOP EXIT WHEN x > 5; x := x + 1; END LOOP;",
        "[(LoopStatement kind=Basic body=[(ExitStatement when=(> X 5)) (AssignmentStatement target=X value=(+ X 1))])]")]
    [InlineData("<<outer>> WHILE x < 5 LOOP CONTINUE outer WHEN x = 2; EXIT outer; END LOOP outer;",
        "[(LoopStatement kind=While condition=(< X 5) body=[(ContinueStatement label=OUTER when=(= X 2)) (ExitStatement label=OUTER)] endLabel=OUTER labels=[OUTER])]")]
    [InlineData("FOR i IN REVERSE 1..n LOOP NULL; END LOOP;", "[(LoopStatement kind=ForNumeric variable=I reverse lower=1 upper=N body=[(NullStatement)])]")]
    [InlineData("FOR r IN (SELECT 1 FROM dual) LOOP NULL; END LOOP;",
        "[(LoopStatement kind=ForCursor variable=R query=(SelectStatement body=(QuerySpecification selectList=[(ExpressionSelectItem expression=1)] from=[(NamedTable name=[DUAL])])) body=[(NullStatement)])]")]
    [InlineData("FOR r IN c1(5) LOOP NULL; END LOOP;", "[(LoopStatement kind=ForCursor variable=R cursorCall=(FunctionCall name=[C1] arguments=[(Argument value=5)]) body=[(NullStatement)])]")]
    [InlineData("OPEN c1; OPEN c2(1, 2); CLOSE c1;",
        "[(OpenStatement cursor=C1) (OpenStatement cursor=(FunctionCall name=[C2] arguments=[(Argument value=1) (Argument value=2)])) (CloseStatement cursor=C1)]")]
    [InlineData("OPEN rc FOR SELECT a FROM t WHERE b = :x USING y;",
        "[(OpenStatement cursor=RC forQuery=(SelectStatement body=(QuerySpecification selectList=[(ExpressionSelectItem expression=A)] from=[(NamedTable name=[T])] where=(= B :x))) using=[(UsingArgument value=Y)])]")]
    [InlineData("OPEN rc FOR 'select 1 from dual';", "[(OpenStatement cursor=RC forExpression='select 1 from dual')]")]
    [InlineData("FETCH c1 INTO a, b; FETCH c2 BULK COLLECT INTO tab LIMIT 100;",
        "[(FetchStatement cursor=C1 into=[A B]) (FetchStatement cursor=C2 bulkCollect into=[TAB] limit=100)]")]
    [InlineData("EXECUTE IMMEDIATE v_sql INTO v USING IN a, OUT b, IN OUT c RETURNING INTO r;",
        "[(ExecuteImmediateStatement sql=V_SQL into=[V] using=[(UsingArgument mode=In value=A) (UsingArgument mode=Out value=B) (UsingArgument mode=InOut value=C)] returningInto=[R])]")]
    [InlineData("EXECUTE IMMEDIATE 'x' BULK COLLECT INTO tab;", "[(ExecuteImmediateStatement sql='x' bulkCollect into=[TAB])]")]
    [InlineData("FORALL i IN 1..tab.COUNT SAVE EXCEPTIONS INSERT INTO t VALUES (tab(i));",
        "[(ForallStatement index=I lower=1 upper=TAB.COUNT saveExceptions body=(InsertStatement target=(NamedTable name=[T]) valueRows=[[(FunctionCall name=[TAB] arguments=[(Argument value=I)])]]))]")]
    [InlineData("FORALL i IN INDICES OF tab BETWEEN 1 AND 5 DELETE FROM t WHERE id = tab(i);",
        "[(ForallStatement index=I indicesOf=TAB betweenLow=1 betweenHigh=5 body=(DeleteStatement target=(NamedTable name=[T]) where=(= ID (FunctionCall name=[TAB] arguments=[(Argument value=I)]))))]")]
    [InlineData("FORALL i IN VALUES OF idx UPDATE t SET a = 1;", "[(ForallStatement index=I valuesOf=IDX body=(UpdateStatement target=(NamedTable name=[T]) set=[(SetClause columns=[A] value=1)]))]")]
    [InlineData("RETURN; RETURN x + 1; RAISE; RAISE my_err; GOTO done; <<done>> NULL;",
        "[(ReturnStatement) (ReturnStatement value=(+ X 1)) (RaiseStatement) (RaiseStatement exception=[MY_ERR]) (GotoStatement target=DONE) (NullStatement labels=[DONE])]")]
    [InlineData("PIPE ROW (t_rec(1, 2));", "[(PipeRowStatement value=(FunctionCall name=[T_REC] arguments=[(Argument value=1) (Argument value=2)]))]")]
    [InlineData("pkg.proc(p_a => 1); my_proc; RAISE_APPLICATION_ERROR(-20001, 'bad');",
        "[(ProcedureCallStatement call=(FunctionCall name=[PKG PROC] arguments=[(Argument name=P_A value=1)])) (ProcedureCallStatement call=MY_PROC) " +
        "(ProcedureCallStatement call=(FunctionCall name=[RAISE_APPLICATION_ERROR] arguments=[(Argument value=(- 20001)) (Argument value='bad')]))]")]
    [InlineData("COMMIT; ROLLBACK TO sp; SAVEPOINT sp;", "[(CommitStatement) (RollbackStatement toSavepoint=SP) (SavepointStatement name=SP)]")]
    [InlineData("SELECT a INTO v FROM t WHERE id = 1;",
        "[(SelectStatement body=(QuerySpecification selectList=[(ExpressionSelectItem expression=A)] from=[(NamedTable name=[T])] where=(= ID 1) into=[V]))]")]
    [InlineData("BEGIN NULL; EXCEPTION WHEN OTHERS THEN NULL; END;",
        "[(Block statements=[(NullStatement)] handlers=[(ExceptionHandler others statements=[(NullStatement)])])]")]
    [InlineData("v := SQL%ROWCOUNT; IF c1%ISOPEN THEN NULL; END IF;",
        "[(AssignmentStatement target=V value=SQL%ROWCOUNT) (IfStatement condition=C1%ISOPEN thenStatements=[(NullStatement)])]")]
    public void Statements(string body, string expected) => Assert.Equal(expected, Stmts(body.Replace("BEGIN NULL; EXCEPTION WHEN OTHERS THEN NULL; END;", "BEGIN NULL; EXCEPTION WHEN OTHERS THEN NULL; END;")));

    [Fact]
    public void Block_LabelAndDeclareFlag()
    {
        var b = Shape.Parse("<<lbl>> DECLARE x NUMBER; BEGIN NULL; END lbl;").Statements[0];
        Assert.Equal("(Block hasDeclare declarations=[(VariableDeclaration name=X type=(DataType name=NUMBER))] statements=[(NullStatement)] endName=LBL labels=[LBL])", AstPrinter.Print(b));
    }

    // ---- program units ----

    [Fact]
    public void CreateProcedure_Shape() =>
        Assert.Equal(
            "(CreateProgramStatement orReplace subprogram=(SubprogramDeclaration name=[P] parameters=[" +
            "(Parameter name=A mode=In type=(DataType name=NUMBER)) " +
            "(Parameter name=B mode=InOut noCopy type=(DataType name=VARCHAR2) default='x') " +
            "(Parameter name=C mode=Out type=(DataType name=T))] " +
            "body=(Block declarations=[(VariableDeclaration name=V type=(DataType name=NUMBER))] statements=[(NullStatement)] endName=P)))",
            Shape.Of("CREATE OR REPLACE PROCEDURE p (a IN NUMBER, b IN OUT NOCOPY VARCHAR2 DEFAULT 'x', c OUT t) AS v NUMBER; BEGIN NULL; END p;"));

    [Theory]
    [InlineData("CREATE EDITIONABLE FUNCTION f RETURN NUMBER AUTHID DEFINER DETERMINISTIC RESULT_CACHE RELIES_ON (t1, s.t2) IS BEGIN RETURN 1; END;",
        "options=(ProgramOptions authid=Definer deterministic resultCache relyOn=[[T1] [S T2]])")]
    [InlineData("CREATE FUNCTION f RETURN t_tab PIPELINED USING impl_t IS BEGIN RETURN; END;", "options=(ProgramOptions pipelined pipelinedUsing=[IMPL_T])")]
    [InlineData("CREATE FUNCTION f (x NUMBER) RETURN NUMBER PARALLEL_ENABLE (PARTITION x BY HASH (a, b) ORDER y BY (c)) IS BEGIN RETURN 1; END;",
        "options=(ProgramOptions parallelEnable=(ParallelEnableClause partitionArgument=X method=Hash methodColumns=[A B] order streamingColumns=[C]))")]
    [InlineData("CREATE FUNCTION f (x NUMBER) RETURN NUMBER PARALLEL_ENABLE (PARTITION x BY ANY) IS BEGIN RETURN 1; END;",
        "options=(ProgramOptions parallelEnable=(ParallelEnableClause partitionArgument=X method=Any))")]
    [InlineData("CREATE FUNCTION f RETURN VARCHAR2 SQL_MACRO (TYPE = TABLE) IS BEGIN RETURN 'x'; END;", "options=(ProgramOptions sqlMacro=Table)")]
    [InlineData("CREATE FUNCTION f RETURN VARCHAR2 SQL_MACRO IS BEGIN RETURN 'x'; END;", "options=(ProgramOptions sqlMacro=Scalar)")]
    [InlineData("CREATE PROCEDURE p ACCESSIBLE BY (PACKAGE pk, TRIGGER s.t, other) IS BEGIN NULL; END;",
        "options=(ProgramOptions accessibleBy=[(Accessor kind=Package name=[PK]) (Accessor kind=Trigger name=[S T]) (Accessor name=[OTHER])])")]
    [InlineData("CREATE PROCEDURE p DEPRECATE 'use q' IS BEGIN NULL; END;", "options=(ProgramOptions deprecate deprecateMessage='use q')")]
    [InlineData("CREATE FUNCTION f RETURN NUMBER DEFAULT COLLATION using_nls_comp SHARING = METADATA IS BEGIN RETURN 1; END;", "options=(ProgramOptions sharing=Metadata defaultCollation=USING_NLS_COMP)")]
    [InlineData("CREATE FUNCTION f (x NUMBER) RETURN NUMBER AGGREGATE USING impl_t;", "options=(ProgramOptions aggregateUsing=[IMPL_T])")]
    public void ProgramOptionClauses(string sql, string expectedFragment)
    {
        var sp = Shape.Parse(sql).Descendants().OfType<SubprogramDeclaration>().First();
        Assert.Contains(expectedFragment, AstPrinter.Print(sp));
    }

    [Theory]
    [InlineData("CREATE PROCEDURE p AS LANGUAGE JAVA NAME 'X.y()'", "external=(ExternalCall language=Java name='X.y()')")]
    [InlineData("CREATE FUNCTION f (x NUMBER) RETURN NUMBER AS LANGUAGE C NAME \"f\" LIBRARY lib AGENT IN (x) WITH CONTEXT PARAMETERS (CONTEXT, x INDICATOR, RETURN INDICATOR)",
        "external=(ExternalCall language=C name=\"f\" library=LIB agentIn=[X] withContext parameters=[(ExternalParameter kind=Context) (ExternalParameter kind=Named name=X property=Indicator) (ExternalParameter kind=Return property=Indicator)])")]
    [InlineData("CREATE PROCEDURE p AS MLE MODULE m ENV e SIGNATURE 'f(string)'", "external=(ExternalCall language=JavaScript mleModule=[M] mleEnv=[E] signature='f(string)')")]
    [InlineData("CREATE PROCEDURE p AS LANGUAGE JAVASCRIPT MODULE m SIGNATURE 'g()'", "external=(ExternalCall language=JavaScript mleModule=[M] signature='g()')")]
    public void ExternalCalls(string sql, string expectedFragment) =>
        Assert.Contains(expectedFragment, AstPrinter.Print(Shape.Parse(sql).Descendants().OfType<SubprogramDeclaration>().First()));

    [Fact]
    public void CreatePackage_Spec() =>
        Assert.Equal(
            "(CreatePackageStatement orReplace name=[PKG] options=(ProgramOptions authid=CurrentUser) declarations=[" +
            "(VariableDeclaration name=G type=(DataType name=NUMBER)) " +
            "(CollectionTypeDeclaration name=T kind=Table elementType=(DataType name=NUMBER)) " +
            "(SubprogramDeclaration name=[P] parameters=[(Parameter name=A type=(DataType name=NUMBER))]) " +
            "(SubprogramDeclaration isFunction name=[F] returnType=(DataType name=NUMBER))] endName=PKG)",
            Shape.Of("CREATE OR REPLACE PACKAGE pkg AUTHID CURRENT_USER AS g NUMBER; TYPE t IS TABLE OF NUMBER; PROCEDURE p(a NUMBER); FUNCTION f RETURN NUMBER; END pkg;"));

    [Fact]
    public void CreatePackage_Body() =>
        Assert.Equal(
            "(CreatePackageBodyStatement name=[PKG] declarations=[(SubprogramDeclaration name=[P] body=(Block statements=[(NullStatement)]))] " +
            "initStatements=[(AssignmentStatement target=G value=1)] initHandlers=[(ExceptionHandler others statements=[(NullStatement)])] endName=PKG)",
            Shape.Of("CREATE PACKAGE BODY pkg AS PROCEDURE p IS BEGIN NULL; END; BEGIN g := 1; EXCEPTION WHEN OTHERS THEN NULL; END pkg;"));

    [Theory]
    [InlineData("CREATE OR REPLACE TRIGGER trg BEFORE INSERT OR UPDATE OF a, b ON hr.t REFERENCING NEW AS n OLD o FOR EACH ROW FOLLOWS other_trg WHEN (n.a > 0) BEGIN NULL; END;",
        "(CreateTriggerStatement orReplace name=[TRG] timing=Before events=[(TriggerEvent kind=Insert) (TriggerEvent kind=Update columns=[A B])] targetKind=Table target=[HR T] " +
        "referencing=[(ReferencingItem kind=New alias=N) (ReferencingItem kind=Old alias=O)] forEachRow ordering=Follows orderingTriggers=[[OTHER_TRG]] when=(> N.A 0) body=(Block statements=[(NullStatement)]))")]
    [InlineData("CREATE TRIGGER trg AFTER LOGON ON DATABASE BEGIN NULL; END;",
        "(CreateTriggerStatement name=[TRG] timing=After events=[(TriggerEvent kind=Database eventName=LOGON)] targetKind=Database body=(Block statements=[(NullStatement)]))")]
    [InlineData("CREATE TRIGGER trg AFTER CREATE OR DROP ON hr.SCHEMA BEGIN NULL; END;",
        "(CreateTriggerStatement name=[TRG] timing=After events=[(TriggerEvent kind=Ddl eventName=CREATE) (TriggerEvent kind=Ddl eventName=DROP)] targetKind=Schema target=[HR] body=(Block statements=[(NullStatement)]))")]
    [InlineData("CREATE TRIGGER trg INSTEAD OF INSERT ON v FOR EACH ROW DECLARE x NUMBER; BEGIN NULL; END;",
        "(CreateTriggerStatement name=[TRG] timing=InsteadOf events=[(TriggerEvent kind=Insert)] targetKind=Table target=[V] forEachRow body=(Block hasDeclare declarations=[(VariableDeclaration name=X type=(DataType name=NUMBER))] statements=[(NullStatement)]))")]
    [InlineData("CREATE TRIGGER trg INSTEAD OF INSERT ON NESTED TABLE c OF v FOR EACH ROW BEGIN NULL; END;",
        "(CreateTriggerStatement name=[TRG] timing=InsteadOf events=[(TriggerEvent kind=Insert)] targetKind=NestedTable target=[V] nestedTableColumn=C forEachRow body=(Block statements=[(NullStatement)]))")]
    [InlineData("CREATE TRIGGER trg BEFORE DELETE ON t FOR EACH ROW FORWARD CROSSEDITION DISABLE BEGIN NULL; END;",
        "(CreateTriggerStatement name=[TRG] timing=Before events=[(TriggerEvent kind=Delete)] targetKind=Table target=[T] forEachRow crossEdition=Forward enabled=false body=(Block statements=[(NullStatement)]))")]
    [InlineData("CREATE TRIGGER trg BEFORE INSERT ON t CALL proc(1)",
        "(CreateTriggerStatement name=[TRG] timing=Before events=[(TriggerEvent kind=Insert)] targetKind=Table target=[T] call=(CallStatement routine=(FunctionCall name=[PROC] arguments=[(Argument value=1)])))")]
    [InlineData("CREATE TRIGGER trg FOR UPDATE ON t COMPOUND TRIGGER v NUMBER; BEFORE STATEMENT IS BEGIN NULL; END BEFORE STATEMENT; AFTER EACH ROW IS BEGIN v := 1; END AFTER EACH ROW; END trg;",
        "(CreateTriggerStatement name=[TRG] timing=For events=[(TriggerEvent kind=Update)] targetKind=Table target=[T] compound compoundDeclarations=[(VariableDeclaration name=V type=(DataType name=NUMBER))] " +
        "sections=[(TimingPointSection point=BeforeStatement statements=[(NullStatement)]) (TimingPointSection point=AfterEachRow statements=[(AssignmentStatement target=V value=1)])])")]
    public void Triggers(string sql, string expected) => Assert.Equal(expected, Shape.Of(sql));

    [Theory]
    [InlineData("CREATE TYPE addr AS OBJECT (street VARCHAR2(30), zip NUMBER, MEMBER FUNCTION full RETURN VARCHAR2, STATIC PROCEDURE sp, CONSTRUCTOR FUNCTION addr (SELF IN OUT addr) RETURN SELF AS RESULT) NOT FINAL",
        "(CreateTypeStatement name=[ADDR] kind=Object final=false attributes=[(VariableDeclaration name=STREET type=(DataType name=VARCHAR2 arguments=[30])) (VariableDeclaration name=ZIP type=(DataType name=NUMBER))] " +
        "members=[(SubprogramDeclaration isFunction modifiers=Member name=[FULL] returnType=(DataType name=VARCHAR2)) (SubprogramDeclaration modifiers=Static name=[SP]) " +
        "(SubprogramDeclaration isFunction modifiers=Constructor name=[ADDR] parameters=[(Parameter name=SELF mode=InOut type=(DataType name=ADDR))] returnSelfAsResult)])")]
    [InlineData("CREATE TYPE nums AS TABLE OF NUMBER", "(CreateTypeStatement name=[NUMS] kind=Table elementType=(DataType name=NUMBER))")]
    [InlineData("CREATE TYPE v AS VARRAY(10) OF VARCHAR2(5)", "(CreateTypeStatement name=[V] kind=Varray elementType=(DataType name=VARCHAR2 arguments=[5]) limit=10)")]
    [InlineData("CREATE TYPE sub UNDER addr (extra NUMBER) FINAL INSTANTIABLE", "(CreateTypeStatement name=[SUB] kind=Under final=true instantiable=true attributes=[(VariableDeclaration name=EXTRA type=(DataType name=NUMBER))] superType=[ADDR])")]
    [InlineData("CREATE TYPE t FORCE OID '123' AUTHID DEFINER AS OBJECT (a NUMBER) NOT PERSISTABLE",
        "(CreateTypeStatement name=[T] kind=Object options=(ProgramOptions authid=Definer) force oid='123' persistable=false attributes=[(VariableDeclaration name=A type=(DataType name=NUMBER))])")]
    [InlineData("CREATE OR REPLACE NONEDITIONABLE TYPE t AS OBJECT (MAP MEMBER FUNCTION m RETURN NUMBER)",
        "(CreateTypeStatement orReplace editionable=NonEditionable name=[T] kind=Object members=[(SubprogramDeclaration isFunction modifiers=Member, Map name=[M] returnType=(DataType name=NUMBER))])")]
    public void ObjectTypes(string sql, string expected) => Assert.Equal(expected, Shape.Of(sql));

    [Fact]
    public void TypeBody() =>
        Assert.Equal(
            "(CreateTypeBodyStatement name=[ADDR] members=[(SubprogramDeclaration isFunction modifiers=Member name=[FULL] returnType=(DataType name=VARCHAR2) body=(Block statements=[(ReturnStatement value=STREET)]))])",
            Shape.Of("CREATE TYPE BODY addr AS MEMBER FUNCTION full RETURN VARCHAR2 IS BEGIN RETURN street; END; END;"));

    [Fact]
    public void Script_BlocksAndSlashes()
    {
        var s = Shape.Parse("BEGIN NULL; END;\n/\nSELECT 1 FROM dual;\nBEGIN NULL; END;\n/\n");
        Assert.Equal(["Block", "SelectStatement", "Block"], s.Statements.Select(x => x.GetType().Name).ToArray());
    }

    // ---- error recovery ----

    [Fact]
    public void Recovery_Simple_ResyncsAtSemicolon()
    {
        var r = SqlParser.Parse("SELECT FROM t; SELECT 1 FROM dual; SELEC 2");
        Assert.Equal(2, r.Errors.Count);
        Assert.Equal((1, 8), (r.Errors[0].Line, r.Errors[0].Column));
        Assert.Single(r.Script.Statements);
    }

    [Fact]
    public void Recovery_FailedProcedureBody_DoesNotCascade()
    {
        var r = SqlParser.Parse("CREATE OR REPLACE PROCEDURE p AS\nBEGIN\n  x := ;\n  y := 1;\n  IF y > 0 THEN NULL; END IF;\nEND;\n/\nSELECT 1 FROM dual;\n");
        Assert.Single(r.Errors);
        Assert.Equal(3, r.Errors[0].Line);
        Assert.Equal(["SelectStatement"], r.Script.Statements.Select(x => x.GetType().Name).ToArray());
    }

    [Fact]
    public void Recovery_FailedAnonymousBlock_WithoutSlash_ResyncsAfterEnd()
    {
        var r = SqlParser.Parse("BEGIN\n  x := ;\n  y := 1;\nEND;\nSELECT 1 FROM dual;");
        Assert.Single(r.Errors);
        Assert.Equal(["SelectStatement"], r.Script.Statements.Select(x => x.GetType().Name).ToArray());
    }

    [Fact]
    public void Recovery_FailedPackage_ResyncsAtSlash()
    {
        var r = SqlParser.Parse("CREATE PACKAGE BODY pk AS\n  PROCEDURE p IS BEGIN bad bad; END;\n  PROCEDURE q IS BEGIN NULL; END;\nEND pk;\n/\nCREATE TABLE t (a NUMBER);");
        Assert.Single(r.Errors);
        Assert.Equal(["CreateTableStatement"], r.Script.Statements.Select(x => x.GetType().Name).ToArray());
    }

    [Fact]
    public void LexerError_IsReported_AndEarlierStatementsKept()
    {
        var r = SqlParser.Parse("SELECT 1 FROM dual; SELECT 'abc FROM dual");
        Assert.False(r.Success);
        Assert.Single(r.Script.Statements);
        Assert.Contains("Unterminated string", r.Errors[^1].Message);
    }

    [Fact]
    public void Positions_ForNodes()
    {
        const string sql = "CREATE TABLE t (a NUMBER)";
        var ct = Shape.Parse(sql).Descendants().OfType<CreateTableStatement>().First();
        Assert.Equal((0, sql.Length), (ct.Start, ct.End));
        var col = Shape.Parse(sql).Descendants().OfType<ColumnDefinition>().First();
        Assert.Equal("a NUMBER", sql[col.Start..col.End]);
        var id = Shape.Parse("SELECT 1\nFROM   emp").Descendants().OfType<NamedTable>().First();
        Assert.Equal((2, 8), (id.Line, id.Column));
    }
}
