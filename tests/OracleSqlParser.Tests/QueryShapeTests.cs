using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

public class QueryShapeTests
{
    private const string FromDual = "from=[(NamedTable name=[DUAL])]";

    [Fact]
    public void Assertions_Actually_Fail_On_Wrong_Shape() =>
        Assert.NotEqual("(SelectStatement)", Shape.Of("SELECT 1 FROM dual"));

    [Theory]
    [InlineData("SELECT a, b x FROM t WHERE a > 1",
        "(SelectStatement body=(QuerySpecification selectList=[(ExpressionSelectItem expression=A) (ExpressionSelectItem expression=B alias=X)] from=[(NamedTable name=[T])] where=(> A 1)))")]
    [InlineData("SELECT DISTINCT a AS \"Alias\" FROM s.t",
        "(SelectStatement body=(QuerySpecification quantifier=Distinct selectList=[(ExpressionSelectItem expression=A alias=\"Alias\")] from=[(NamedTable name=[S T])]))")]
    [InlineData("SELECT e.*, * FROM emp e",
        "(SelectStatement body=(QuerySpecification selectList=[(StarSelectItem qualifier=[E]) (StarSelectItem)] from=[(NamedTable name=[EMP] alias=E)]))")]
    [InlineData("SELECT 1 FROM a x, b y",
        "(SelectStatement body=(QuerySpecification selectList=[(ExpressionSelectItem expression=1)] from=[(NamedTable name=[A] alias=X) (NamedTable name=[B] alias=Y)]))")]
    [InlineData("SELECT 1",
        "(SelectStatement body=(QuerySpecification selectList=[(ExpressionSelectItem expression=1)]))")]
    public void SimpleSelects(string sql, string expected) => Assert.Equal(expected, Shape.Of(sql));

    [Theory]
    [InlineData("SELECT /*+ INDEX(e emp_ix) FULL(t) PARALLEL(4) */ a FROM t",
        "(HintClause text=INDEX(e emp_ix) FULL(t) PARALLEL(4) items=[(HintItem name=INDEX arguments=[(WordItem value=E) (WordItem value=EMP_IX)]) (HintItem name=FULL arguments=[(WordItem value=T)]) (HintItem name=PARALLEL arguments=[(LiteralItem kind=Number value=4)])])")]
    [InlineData("SELECT /*+ NO_MERGE */ a FROM t", "(HintClause text=NO_MERGE items=[(HintItem name=NO_MERGE)])")]
    public void Hints(string sql, string expected) => Assert.Equal(expected, Shape.Of<HintClause>(sql));

    [Theory]
    [InlineData("SELECT * FROM a LEFT OUTER JOIN b ON a.id = b.id",
        "(JoinTable left=(NamedTable name=[A]) kind=Left right=(NamedTable name=[B]) on=(= A.ID B.ID))")]
    [InlineData("SELECT * FROM a PARTITION BY (a.x) RIGHT OUTER JOIN b ON a.id = b.id",
        "(JoinTable left=(NamedTable name=[A]) kind=Right right=(NamedTable name=[B]) on=(= A.ID B.ID) leftPartitionBy=[A.X])")]
    [InlineData("SELECT * FROM a LEFT JOIN b PARTITION BY (b.x) ON a.id = b.id",
        "(JoinTable left=(NamedTable name=[A]) kind=Left right=(NamedTable name=[B]) on=(= A.ID B.ID) rightPartitionBy=[B.X])")]
    [InlineData("SELECT * FROM a PARTITION BY (a.x, a.y) LEFT OUTER JOIN b ON a.id = b.id",
        "(JoinTable left=(NamedTable name=[A]) kind=Left right=(NamedTable name=[B]) on=(= A.ID B.ID) leftPartitionBy=[A.X A.Y])")]
    [InlineData("SELECT * FROM a JOIN b USING (id, x)",
        "(JoinTable left=(NamedTable name=[A]) kind=Inner right=(NamedTable name=[B]) using=[ID X])")]
    [InlineData("SELECT * FROM a NATURAL JOIN b",
        "(JoinTable left=(NamedTable name=[A]) kind=Inner natural right=(NamedTable name=[B]))")]
    [InlineData("SELECT * FROM a CROSS JOIN b",
        "(JoinTable left=(NamedTable name=[A]) kind=Cross right=(NamedTable name=[B]))")]
    [InlineData("SELECT * FROM a CROSS APPLY (SELECT 1 FROM dual) s",
        "(JoinTable left=(NamedTable name=[A]) kind=CrossApply right=(SubqueryTable query=(SelectStatement body=(QuerySpecification selectList=[(ExpressionSelectItem expression=1)] from=[(NamedTable name=[DUAL])])) alias=S))")]
    [InlineData("SELECT * FROM a FULL JOIN b ON 1 = 1 RIGHT JOIN c ON 2 = 2",
        "(JoinTable left=(JoinTable left=(NamedTable name=[A]) kind=Full right=(NamedTable name=[B]) on=(= 1 1)) kind=Right right=(NamedTable name=[C]) on=(= 2 2))")]
    public void Joins(string sql, string expected) => Assert.Equal(expected, Shape.Of<JoinTable>(sql));

    // Non-reserved words that also start a clause: an alias where the clause cannot start, the clause elsewhere.
    [Theory]
    [InlineData("SELECT 1 pivot, 2 left, 3 sample, 4 fetch FROM dual",
        "(SelectStatement body=(QuerySpecification selectList=[(ExpressionSelectItem expression=1 alias=PIVOT) (ExpressionSelectItem expression=2 alias=LEFT) (ExpressionSelectItem expression=3 alias=SAMPLE) (ExpressionSelectItem expression=4 alias=FETCH)] from=[(NamedTable name=[DUAL])]))")]
    [InlineData("SELECT * FROM t sample", "(SelectStatement body=(QuerySpecification selectList=[(StarSelectItem)] from=[(NamedTable name=[T] alias=SAMPLE)]))")]
    [InlineData("SELECT * FROM t pivot", "(SelectStatement body=(QuerySpecification selectList=[(StarSelectItem)] from=[(NamedTable name=[T] alias=PIVOT)]))")]
    [InlineData("SELECT * FROM t left", "(SelectStatement body=(QuerySpecification selectList=[(StarSelectItem)] from=[(NamedTable name=[T] alias=LEFT)]))")]
    [InlineData("SELECT * FROM t fetch", "(SelectStatement body=(QuerySpecification selectList=[(StarSelectItem)] from=[(NamedTable name=[T] alias=FETCH)]))")]
    [InlineData("SELECT * FROM t SAMPLE (10) sample", "(SelectStatement body=(QuerySpecification selectList=[(StarSelectItem)] from=[(NamedTable name=[T] sample=(SampleClause percent=10) alias=SAMPLE)]))")]
    [InlineData("SELECT * FROM t sample (10)", "(SelectStatement body=(QuerySpecification selectList=[(StarSelectItem)] from=[(NamedTable name=[T] sample=(SampleClause percent=10))]))")]
    [InlineData("SELECT * FROM t LEFT JOIN u ON 1 = 1", "(SelectStatement body=(QuerySpecification selectList=[(StarSelectItem)] from=[(JoinTable left=(NamedTable name=[T]) kind=Left right=(NamedTable name=[U]) on=(= 1 1))]))")]
    [InlineData("SELECT * FROM t FETCH FIRST 1 ROW ONLY", "(SelectStatement body=(QuerySpecification selectList=[(StarSelectItem)] from=[(NamedTable name=[T])]) fetch=(FetchClause count=1))")]
    public void ContextualKeywordAliases(string sql, string expected) => Assert.Equal(expected, Shape.Of(sql));

    [Theory]
    [InlineData("SELECT * FROM a PARTITION BY (a.x) JOIN b ON a.id = b.id")] // only outer joins take a partition clause
    [InlineData("SELECT * FROM a PARTITION BY (a.x) FULL OUTER JOIN b ON a.id = b.id")] // FULL outer joins cannot be partitioned
    [InlineData("SELECT * FROM a PARTITION BY (a.x) LEFT JOIN b PARTITION BY (b.x) ON a.id = b.id")] // one side only
    public void PartitionedOuterJoin_Rejected(string sql) => Assert.False(SqlParser.Parse(sql).Success);

    [Theory]
    [InlineData("SELECT * FROM emp@remote_db e", "(NamedTable name=[EMP] dbLink=[REMOTE_DB] alias=E)")]
    [InlineData("SELECT * FROM hr.dept@link PARTITION (p1) d", "(NamedTable name=[HR DEPT] dbLink=[LINK] partition=P1 alias=D)")]
    [InlineData("SELECT * FROM emp SAMPLE BLOCK (10) SEED (3) e", "(NamedTable name=[EMP] sample=(SampleClause block percent=10 seed=3) alias=E)")]
    [InlineData("SELECT * FROM emp AS OF TIMESTAMP SYSTIMESTAMP - INTERVAL '1' HOUR e",
        "(NamedTable name=[EMP] flashback=(FlashbackClause mode=AsOf unit=Timestamp value=(- SYSTIMESTAMP (IntervalLiteral value='1' from=Hour))) alias=E)")]
    [InlineData("SELECT * FROM emp AS OF SCN 100", "(NamedTable name=[EMP] flashback=(FlashbackClause mode=AsOf unit=Scn value=100))")]
    [InlineData("SELECT * FROM emp VERSIONS BETWEEN SCN MINVALUE AND MAXVALUE",
        "(NamedTable name=[EMP] flashback=(FlashbackClause mode=VersionsBetween unit=Scn value=MINVALUE toValue=MAXVALUE))")]
    [InlineData("SELECT * FROM emp AS OF PERIOD FOR vt SYSDATE", "(NamedTable name=[EMP] flashback=(FlashbackClause mode=AsOf unit=Period period=VT value=SYSDATE))")]
    [InlineData("SELECT * FROM emp SAMPLE (5) AS OF SCN 100 e",
        "(NamedTable name=[EMP] sample=(SampleClause percent=5) flashback=(FlashbackClause mode=AsOf unit=Scn value=100) alias=E)")]
    public void NamedTables(string sql, string expected) => Assert.Equal(expected, Shape.Of<NamedTable>(sql));

    [Theory]
    [InlineData("SELECT * FROM (SELECT 1 FROM dual) s", "(SubqueryTable query=(SelectStatement body=(QuerySpecification selectList=[(ExpressionSelectItem expression=1)] from=[(NamedTable name=[DUAL])])) alias=S)")]
    [InlineData("SELECT * FROM LATERAL (SELECT 1 FROM dual) l", "(SubqueryTable lateral query=(SelectStatement body=(QuerySpecification selectList=[(ExpressionSelectItem expression=1)] from=[(NamedTable name=[DUAL])])) alias=L)")]
    [InlineData("SELECT * FROM TABLE(f(1)) tf", "(TableFunction expression=(FunctionCall name=[F] arguments=[(Argument value=1)]) alias=TF)")]
    [InlineData("SELECT * FROM (a JOIN b ON 1 = 1) x", "(ParenthesizedTable inner=(JoinTable left=(NamedTable name=[A]) kind=Inner right=(NamedTable name=[B]) on=(= 1 1)) alias=X)")]
    public void OtherTableReferences(string sql, string expected) =>
        Assert.Equal(expected, Shape.Parse(sql).Descendants().OfType<TableReference>().Where(t => t is not NamedTable || true).Skip(0).Select(AstPrinter.Print).First(p => !p.StartsWith("(NamedTable name=[DUAL")));

    [Theory]
    [InlineData("SELECT a FROM t GROUP BY ROLLUP(a, b), CUBE(c), d HAVING COUNT(*) > 1",
        "groupBy=[(FunctionCall name=[ROLLUP] arguments=[(Argument value=A) (Argument value=B)]) (FunctionCall name=[CUBE] arguments=[(Argument value=C)]) D] having=(> (FunctionCall name=[COUNT] arguments=[(Argument value=*)]) 1)")]
    [InlineData("SELECT a FROM t GROUP BY GROUPING SETS ((a, b), (a), ())",
        "groupBy=[(FunctionCall name=[GROUPING SETS] arguments=[(Argument value=(ExpressionList items=[A B])) (Argument value=(ExpressionList items=[A])) (Argument value=(ExpressionList))])]")]
    [InlineData("SELECT empno FROM emp START WITH mgr IS NULL CONNECT BY NOCYCLE PRIOR empno = mgr",
        "startWith=(IsExpression value=MGR test=Null) connectBy=(= (PRIOR EMPNO) MGR) noCycle")]
    [InlineData("SELECT a INTO x, y FROM t", "into=[X Y]")]
    [InlineData("SELECT a BULK COLLECT INTO tab FROM t", "into=[TAB] intoBulkCollect")]
    public void QuerySpecificationClauses(string sql, string expectedFragment) =>
        Assert.Contains(expectedFragment, Shape.Of<QuerySpecification>(sql));

    [Theory]
    [InlineData("SELECT 1 FROM a ORDER BY 1 DESC NULLS LAST, b", "(SelectStatement body=(QuerySpecification selectList=[(ExpressionSelectItem expression=1)] from=[(NamedTable name=[A])]) orderBy=[(OrderByItem expression=1 descending nulls=Last) (OrderByItem expression=B)])")]
    [InlineData("SELECT 1 FROM a ORDER SIBLINGS BY b", "(SelectStatement body=(QuerySpecification selectList=[(ExpressionSelectItem expression=1)] from=[(NamedTable name=[A])]) orderSiblings orderBy=[(OrderByItem expression=B)])")]
    [InlineData("SELECT 1 FROM a OFFSET 5 ROWS FETCH NEXT 10 ROWS ONLY", "(SelectStatement body=(QuerySpecification selectList=[(ExpressionSelectItem expression=1)] from=[(NamedTable name=[A])]) offset=5 fetch=(FetchClause next count=10))")]
    [InlineData("SELECT 1 FROM a FETCH FIRST 10 PERCENT ROWS WITH TIES", "(SelectStatement body=(QuerySpecification selectList=[(ExpressionSelectItem expression=1)] from=[(NamedTable name=[A])]) fetch=(FetchClause count=10 percent withTies))")]
    [InlineData("SELECT 1 FROM a ORDER BY b FETCH APPROX FIRST 20 ROWS ONLY", "(SelectStatement body=(QuerySpecification selectList=[(ExpressionSelectItem expression=1)] from=[(NamedTable name=[A])]) orderBy=[(OrderByItem expression=B)] fetch=(FetchClause count=20 accuracy=Approximate))")]
    [InlineData("SELECT 1 FROM a ORDER BY b FETCH APPROXIMATE NEXT 20 ROWS ONLY", "(SelectStatement body=(QuerySpecification selectList=[(ExpressionSelectItem expression=1)] from=[(NamedTable name=[A])]) orderBy=[(OrderByItem expression=B)] fetch=(FetchClause next count=20 accuracy=Approximate))")]
    [InlineData("SELECT 1 FROM a ORDER BY b FETCH EXACT FIRST 20 ROWS ONLY", "(SelectStatement body=(QuerySpecification selectList=[(ExpressionSelectItem expression=1)] from=[(NamedTable name=[A])]) orderBy=[(OrderByItem expression=B)] fetch=(FetchClause count=20 accuracy=Exact))")]
    [InlineData("SELECT 1 FROM a FETCH FIRST ROW ONLY","(SelectStatement body=(QuerySpecification selectList=[(ExpressionSelectItem expression=1)] from=[(NamedTable name=[A])]) fetch=(FetchClause))")]
    [InlineData("SELECT 1 FROM a FOR UPDATE OF a.x, y SKIP LOCKED", "(SelectStatement body=(QuerySpecification selectList=[(ExpressionSelectItem expression=1)] from=[(NamedTable name=[A])]) forUpdate=(ForUpdateClause of=[A.X Y] skipLocked))")]
    [InlineData("SELECT 1 FROM a FOR UPDATE WAIT 5", "(SelectStatement body=(QuerySpecification selectList=[(ExpressionSelectItem expression=1)] from=[(NamedTable name=[A])]) forUpdate=(ForUpdateClause wait=5))")]
    public void OrderingAndLocking(string sql, string expected) => Assert.Equal(expected, Shape.Of(sql));

    [Theory]
    [InlineData("SELECT 1 FROM a UNION ALL SELECT 2 FROM b MINUS SELECT 3 FROM c",
        "(SetOperation left=(SetOperation left=(QuerySpecification selectList=[(ExpressionSelectItem expression=1)] from=[(NamedTable name=[A])]) operator=Union all right=(QuerySpecification selectList=[(ExpressionSelectItem expression=2)] from=[(NamedTable name=[B])])) operator=Minus right=(QuerySpecification selectList=[(ExpressionSelectItem expression=3)] from=[(NamedTable name=[C])]))")]
    [InlineData("SELECT 1 FROM a INTERSECT (SELECT 2 FROM b)",
        "(SetOperation left=(QuerySpecification selectList=[(ExpressionSelectItem expression=1)] from=[(NamedTable name=[A])]) operator=Intersect right=(ParenthesizedQuery query=(SelectStatement body=(QuerySpecification selectList=[(ExpressionSelectItem expression=2)] from=[(NamedTable name=[B])]))))")]
    public void SetOperations(string sql, string expected) => Assert.Equal(expected, Shape.Of<SetOperation>(sql));

    [Theory]
    [InlineData("WITH x (a, b) AS (SELECT 1, 2 FROM dual) SELECT * FROM x",
        "(CommonTableExpression name=X columns=[A B] query=(SelectStatement body=(QuerySpecification selectList=[(ExpressionSelectItem expression=1) (ExpressionSelectItem expression=2)] from=[(NamedTable name=[DUAL])])))")]
    [InlineData("WITH x(a, b) AS (VALUES (1, 2), (3, 4)) SELECT * FROM x",
        "(CommonTableExpression name=X columns=[A B] query=(SelectStatement body=(ValuesQuery rows=[[1 2] [3 4]])))")]
    [InlineData("WITH t(n) AS (SELECT 1 FROM dual) SEARCH DEPTH FIRST BY n DESC SET ord CYCLE n SET is_cycle TO 'Y' DEFAULT 'N' SELECT * FROM t",
        "(CommonTableExpression name=T columns=[N] query=(SelectStatement body=(QuerySpecification selectList=[(ExpressionSelectItem expression=1)] from=[(NamedTable name=[DUAL])])) search=(SearchClause depthFirst by=[(OrderByItem expression=N descending)] setColumn=ORD) cycle=(CycleClause columns=[N] setColumn=IS_CYCLE cycleValue='Y' defaultValue='N'))")]
    [InlineData("WITH t AS (SELECT 1 FROM dual) SEARCH BREADTH FIRST BY n SET o SELECT * FROM t",
        "(CommonTableExpression name=T query=(SelectStatement body=(QuerySpecification selectList=[(ExpressionSelectItem expression=1)] from=[(NamedTable name=[DUAL])])) search=(SearchClause by=[(OrderByItem expression=N)] setColumn=O))")]
    public void CommonTableExpressions(string sql, string expected) => Assert.Equal(expected, Shape.Of<CommonTableExpression>(sql));

    [Fact]
    public void WithFunction_Declarations()
    {
        Assert.Equal(
            "(SelectStatement body=(QuerySpecification selectList=[(ExpressionSelectItem expression=(FunctionCall name=[F] arguments=[(Argument value=1)]))] from=[(NamedTable name=[DUAL])]) " +
            "withDeclarations=[(SubprogramDeclaration isFunction name=[F] parameters=[(Parameter name=X type=(DataType name=NUMBER))] returnType=(DataType name=NUMBER) body=(Block statements=[(ReturnStatement value=(* X 2))]))])",
            Shape.Of<SelectStatement>("WITH FUNCTION f(x NUMBER) RETURN NUMBER IS BEGIN RETURN x * 2; END; SELECT f(1) FROM dual"));
    }

    [Theory]
    [InlineData("SELECT * FROM sales PIVOT (SUM(amount) AS total, COUNT(*) c FOR q IN ('Q1' AS q1, 'Q2')) p",
        "(PivotTable source=(NamedTable name=[SALES]) aggregates=[(PivotAggregate function=(FunctionCall name=[SUM] arguments=[(Argument value=AMOUNT)]) alias=TOTAL) (PivotAggregate function=(FunctionCall name=[COUNT] arguments=[(Argument value=*)]) alias=C)] forColumns=[Q] inItems=[(PivotInItem value='Q1' alias=Q1) (PivotInItem value='Q2')] alias=P)")]
    [InlineData("SELECT * FROM s PIVOT XML (SUM(a) FOR (x, y) IN (ANY, ANY))",
        "(PivotTable source=(NamedTable name=[S]) xml aggregates=[(PivotAggregate function=(FunctionCall name=[SUM] arguments=[(Argument value=A)]))] forColumns=[X Y] inAny)")]
    [InlineData("SELECT * FROM s PIVOT (SUM(a) FOR x IN (SELECT DISTINCT x FROM t))",
        "(PivotTable source=(NamedTable name=[S]) aggregates=[(PivotAggregate function=(FunctionCall name=[SUM] arguments=[(Argument value=A)]))] forColumns=[X] inSubquery=(SelectStatement body=(QuerySpecification quantifier=Distinct selectList=[(ExpressionSelectItem expression=X)] from=[(NamedTable name=[T])])))")]
    [InlineData("SELECT * FROM t UNPIVOT INCLUDE NULLS (val FOR col IN (a AS 'A', b AS 'B'))",
        "(UnpivotTable source=(NamedTable name=[T]) includeNulls valueColumns=[VAL] forColumns=[COL] inItems=[(UnpivotInItem columns=[A] aliasValue='A') (UnpivotInItem columns=[B] aliasValue='B')])")]
    [InlineData("SELECT * FROM t UNPIVOT ((x, y) FOR c IN ((a, b) AS 'ab'))",
        "(UnpivotTable source=(NamedTable name=[T]) valueColumns=[X Y] forColumns=[C] inItems=[(UnpivotInItem columns=[A B] aliasValue='ab')])")]
    public void PivotUnpivot(string sql, string expected) =>
        Assert.Equal(expected, Shape.Parse(sql).Descendants().First(n => n is PivotTable or UnpivotTable) is var n ? AstPrinter.Print(n) : "");

    [Fact]
    public void MatchRecognize_Shape()
    {
        var sql = "SELECT * FROM ticker MATCH_RECOGNIZE (PARTITION BY symbol ORDER BY ts MEASURES FIRST(down.ts) AS bottom ONE ROW PER MATCH AFTER MATCH SKIP TO LAST up PATTERN (strt down+ up{2,3}? (a|b)* ^ $) SUBSET u = (strt, up) DEFINE down AS price < PREV(price), up AS price > PREV(price)) m";
        Assert.Equal(
            "(MatchRecognizeTable source=(NamedTable name=[TICKER]) partitionBy=[SYMBOL] orderBy=[(OrderByItem expression=TS)] " +
            "measures=[(ExpressionSelectItem expression=(FunctionCall name=[FIRST] arguments=[(Argument value=DOWN.TS)]) alias=BOTTOM)] " +
            "rowsPerMatch=OneRow skip=ToLast skipVariable=UP " +
            "pattern=(PatternConcat items=[(PatternVariable name=STRT) (PatternQuantified inner=(PatternVariable name=DOWN) min=1) " +
            "(PatternQuantified inner=(PatternVariable name=UP) min=2 max=3 reluctant) " +
            "(PatternQuantified inner=(PatternGroup inner=(PatternAlternation items=[(PatternVariable name=A) (PatternVariable name=B)])) min=0) " +
            "(PatternAnchor isStart) (PatternAnchor)]) " +
            "subsets=[(PatternSubset name=U members=[STRT UP])] " +
            "define=[(PatternDefine variable=DOWN condition=(< PRICE (FunctionCall name=[PREV] arguments=[(Argument value=PRICE)]))) (PatternDefine variable=UP condition=(> PRICE (FunctionCall name=[PREV] arguments=[(Argument value=PRICE)])))] alias=M)",
            Shape.Of<MatchRecognizeTable>(sql));
    }

    [Theory]
    [InlineData("SELECT * FROM x MATCH_RECOGNIZE (ORDER BY ts ALL ROWS PER MATCH WITH UNMATCHED ROWS AFTER MATCH SKIP PAST LAST ROW SEEK PATTERN (a) DEFINE a AS 1 = 1)",
        "(MatchRecognizeTable source=(NamedTable name=[X]) orderBy=[(OrderByItem expression=TS)] rowsPerMatch=AllRows allRowsMode=WithUnmatchedRows skip=PastLastRow seek pattern=(PatternVariable name=A) define=[(PatternDefine variable=A condition=(= 1 1))])")]
    public void MatchRecognize_Options(string sql, string expected) => Assert.Equal(expected, Shape.Of<MatchRecognizeTable>(sql));

    [Fact]
    public void XmlTable_Shape() =>
        Assert.Equal(
            "(XmlTable namespaces=[(XmlNamespace uri='u' alias=\"n\") (XmlNamespace uri='d' isDefault)] xQuery='/a/b' passing=(PassingClause items=[(ExpressionSelectItem expression=X)]) " +
            "columns=[(PathColumn name=ID type=(DataType name=NUMBER) path='@id') (PathColumn name=NM type=(DataType name=VARCHAR2 arguments=[10]) path='n' default='x') (PathColumn name=SEQ forOrdinality)] alias=T)",
            Shape.Of<XmlTable>("SELECT * FROM XMLTABLE(XMLNAMESPACES('u' AS \"n\", DEFAULT 'd'), '/a/b' PASSING x COLUMNS id NUMBER PATH '@id', nm VARCHAR2(10) PATH 'n' DEFAULT 'x', seq FOR ORDINALITY) t"));

    [Fact]
    public void JsonTable_Shape() =>
        Assert.Equal(
            "(JsonTable input=DOC path='$.items[*]' clauses=[(OnBehaviorClause behavior=Error event=Error)] columns=[" +
            "(PathColumn name=ID type=(DataType name=NUMBER) path='$.id') " +
            "(PathColumn name=NM type=(DataType name=VARCHAR2 arguments=[20]) clauses=[(FlagClause flag=Truncate)] path='$.nm') " +
            "(PathColumn name=E type=(DataType name=VARCHAR2) exists path='$.e') " +
            "(PathColumn name=J type=(DataType name=CLOB) clauses=[(FormatClause kind=Json) (WrapperClause with array) (OnBehaviorClause behavior=Null event=Error)] path='$.j') " +
            "(PathColumn path='$.tags[*]' nested columns=[(PathColumn name=TAG type=(DataType name=VARCHAR2 arguments=[10]) path='$')])] alias=JT)",
            Shape.Of<JsonTable>("SELECT * FROM JSON_TABLE(doc, '$.items[*]' ERROR ON ERROR COLUMNS (id NUMBER PATH '$.id', nm VARCHAR2(20) TRUNCATE PATH '$.nm', e VARCHAR2 EXISTS PATH '$.e', " +
                "j CLOB FORMAT JSON WITH ARRAY WRAPPER PATH '$.j' NULL ON ERROR, NESTED PATH '$.tags[*]' COLUMNS (tag VARCHAR2(10) PATH '$'))) jt"));

    [Fact]
    public void Model_Shape() =>
        Assert.Equal(
            "(ModelClause nav=IgnoreNav uniqueness=UniqueDimension return=UpdatedRows partitionBy=[(ExpressionSelectItem expression=COUNTRY)] " +
            "dimensionBy=[(ExpressionSelectItem expression=PRODUCT) (ExpressionSelectItem expression=YEAR)] measures=[(ExpressionSelectItem expression=AMT alias=SALES)] " +
            "rulesMode=UpsertAll rulesOrder=Sequential iterate=10 iterateUntil=(> (CellReference base=AMT subscripts=['x' 2003]) 5) " +
            "rules=[(ModelRule mode=Update target=(CellReference base=AMT subscripts=['x' 2003]) orderBy=[(OrderByItem expression=PRODUCT descending)] value=(+ (CellReference base=AMT subscripts=['x' 2002]) 1)) " +
            "(ModelRule target=(CellReference base=AMT subscripts=[ANY 2004]) value=(FunctionCall name=[SUM] arguments=[(Argument value=AMT)]))])",
            Shape.Of<ModelClause>("SELECT * FROM s MODEL IGNORE NAV UNIQUE DIMENSION RETURN UPDATED ROWS PARTITION BY (country) DIMENSION BY (product, year) MEASURES (amt sales) " +
                "RULES UPSERT ALL SEQUENTIAL ORDER ITERATE (10) UNTIL (amt['x', 2003] > 5) (UPDATE amt['x', 2003] ORDER BY product DESC = amt['x', 2002] + 1, amt[ANY, 2004] = SUM(amt))"));

    [Fact]
    public void Model_ReferenceModel() =>
        Assert.Equal(
            "(ReferenceModel name=PRICES query=(SelectStatement body=(QuerySpecification selectList=[(ExpressionSelectItem expression=P)] from=[(NamedTable name=[PR])])) dimensionBy=[(ExpressionSelectItem expression=P)] measures=[(ExpressionSelectItem expression=V)] nav=KeepNav)",
            Shape.Of<ReferenceModel>("SELECT * FROM s MODEL REFERENCE prices ON (SELECT p FROM pr) DIMENSION BY (p) MEASURES (v) KEEP NAV MAIN m DIMENSION BY (a) MEASURES (b) RULES (b[1] = 2)"));

    [Theory]
    [InlineData("SELECT * FROM GRAPH_TABLE(g MATCH (a) COLUMNS (a.name)) gt",
        "(GraphTable graph=[G] match=[(GraphPathPattern elements=[(GraphVertexPattern variable=A)])] columns=[(GraphColumn value=A.NAME)] alias=GT)")]
    [InlineData("SELECT * FROM GRAPH_TABLE(g MATCH (a IS person WHERE a.age > 1)-[e IS knows]->(b IS person|company) COLUMNS (a.name AS who, vertex_id(b)))",
        "(GraphTable graph=[G] match=[(GraphPathPattern elements=[(GraphVertexPattern variable=A label=(GraphLabelExpression labels=[PERSON]) where=(> A.AGE 1)) (GraphEdgePattern direction=Right bracketed variable=E label=(GraphLabelExpression labels=[KNOWS])) (GraphVertexPattern variable=B label=(GraphLabelExpression labels=[PERSON COMPANY]))])] columns=[(GraphColumn value=A.NAME alias=WHO) (GraphColumn value=(FunctionCall name=[VERTEX_ID] arguments=[(Argument value=B)]))])")]
    [InlineData("SELECT * FROM GRAPH_TABLE(g MATCH (a)<-[]-(b)-[]-(c)->(d)-(e)<-(f) COLUMNS (a.x))",
        "(GraphTable graph=[G] match=[(GraphPathPattern elements=[(GraphVertexPattern variable=A) (GraphEdgePattern direction=Left bracketed) (GraphVertexPattern variable=B) (GraphEdgePattern direction=Any bracketed) (GraphVertexPattern variable=C) (GraphEdgePattern direction=Right) (GraphVertexPattern variable=D) (GraphEdgePattern direction=Any) (GraphVertexPattern variable=E) (GraphEdgePattern direction=Left) (GraphVertexPattern variable=F)])] columns=[(GraphColumn value=A.X)])")]
    [InlineData("SELECT * FROM GRAPH_TABLE(g MATCH (a)-[e]->{1,3}(b) COLUMNS (a.x))",
        "(GraphTable graph=[G] match=[(GraphPathPattern elements=[(GraphVertexPattern variable=A) (GraphEdgePattern direction=Right bracketed variable=E quantifier=(GraphQuantifier kind=Range min=1 max=3)) (GraphVertexPattern variable=B)])] columns=[(GraphColumn value=A.X)])")]
    [InlineData("SELECT * FROM GRAPH_TABLE(g MATCH (a)-[e]-{2}(b) COLUMNS (a.x))",
        "(GraphTable graph=[G] match=[(GraphPathPattern elements=[(GraphVertexPattern variable=A) (GraphEdgePattern direction=Any bracketed variable=E quantifier=(GraphQuantifier kind=Exact min=2)) (GraphVertexPattern variable=B)])] columns=[(GraphColumn value=A.X)])")]
    [InlineData("SELECT * FROM GRAPH_TABLE(g MATCH (a)-[e]->*(b) COLUMNS (a.x))",
        "(GraphTable graph=[G] match=[(GraphPathPattern elements=[(GraphVertexPattern variable=A) (GraphEdgePattern direction=Right bracketed variable=E quantifier=(GraphQuantifier kind=Star)) (GraphVertexPattern variable=B)])] columns=[(GraphColumn value=A.X)])")]
    [InlineData("SELECT * FROM GRAPH_TABLE(g MATCH (a)-[e]->(b), (c) WHERE a.x = 1 ONE ROW PER VERTEX(a) COLUMNS (a.x))",
        "(GraphTable graph=[G] match=[(GraphPathPattern elements=[(GraphVertexPattern variable=A) (GraphEdgePattern direction=Right bracketed variable=E) (GraphVertexPattern variable=B)]) (GraphPathPattern elements=[(GraphVertexPattern variable=C)])] where=(= A.X 1) rowsPer=Vertex rowsPerVariables=[A] columns=[(GraphColumn value=A.X)])")]
    [InlineData("SELECT * FROM GRAPH_TABLE(g MATCH (a)-[e]->(b) COLUMNS (a.x))", "(GraphTable graph=[G] match=[(GraphPathPattern elements=[(GraphVertexPattern variable=A) (GraphEdgePattern direction=Right bracketed variable=E) (GraphVertexPattern variable=B)])] columns=[(GraphColumn value=A.X)])")]
    [InlineData("SELECT * FROM GRAPH_TABLE(g MATCH (a)-[e]->(b) ONE ROW PER STEP(a, e, b) COLUMNS (a.x))", "(GraphTable graph=[G] match=[(GraphPathPattern elements=[(GraphVertexPattern variable=A) (GraphEdgePattern direction=Right bracketed variable=E) (GraphVertexPattern variable=B)])] rowsPer=Step rowsPerVariables=[A E B] columns=[(GraphColumn value=A.X)])")]
    public void GraphTable_TypedPatterns(string sql, string expected) => Assert.Equal(expected, Shape.Of<GraphTable>(sql));
}
