using Xunit;

namespace OracleSqlParser.Tests;

public class ExpressionShapeTests
{
    [Theory]
    [InlineData("a OR b AND c", "(OR A (AND B C))")]
    [InlineData("1 + 2 * 3", "(+ 1 (* 2 3))")]
    [InlineData("(1 + 2) * 3", "(* (paren (+ 1 2)) 3)")]
    [InlineData("-a ** 2", "(- (** A 2))")]
    [InlineData("a - -1", "(- A (- 1))")]
    [InlineData("a || b || 'c'", "(|| (|| A B) 'c')")]
    [InlineData("NOT a = b", "(NOT (= A B))")]
    [InlineData("a <> 1 AND b != 2 AND c ^= 3", "(AND (AND (<> A 1) (!= B 2)) (^= C 3))")]
    [InlineData("TRUE AND NOT FALSE", "(AND TRUE (NOT FALSE))")]
    [InlineData("a.id = b.id(+)", "(= A.ID B.ID(+))")]
    [InlineData("s.t.c", "S.T.C")]
    [InlineData("\"MixedCase\".x", "\"MixedCase\".X")]
    [InlineData("q'[it's]'", "'it's'")]
    [InlineData("N'abc'", "'abc'")]
    [InlineData("1.5e3", "1.5e3")]
    [InlineData(":bind", ":bind")]
    [InlineData(":new.col", ":new.col")]
    [InlineData("&v", "(SubstitutionVariable name=v)")]
    [InlineData("&&v", "(SubstitutionVariable name=v persistent)")]
    [InlineData("NULL", "NULL")]
    [InlineData("DATE '2020-01-01'", "DATE'2020-01-01'")]
    [InlineData("TIMESTAMP '2020-01-01 10:00:00'", "TIMESTAMP'2020-01-01 10:00:00'")]
    [InlineData("INTERVAL '3' DAY", "(IntervalLiteral value='3' from=Day)")]
    [InlineData("INTERVAL '1-2' YEAR(3) TO MONTH", "(IntervalLiteral value='1-2' from=Year fromPrecision=3 to=Month)")]
    [InlineData("INTERVAL '4 5:12:10.222' DAY TO SECOND(3)", "(IntervalLiteral value='4 5:12:10.222' from=Day to=Second toPrecision=3)")]
    [InlineData("(a, b)", "(ExpressionList items=[A B])")]
    [InlineData("s.*", "S.*")]
    public void Basics(string sql, string expected) => Assert.Equal(expected, Shape.Expr(sql));

    [Theory]
    [InlineData("a BETWEEN 1 AND 2 AND b = 3", "(AND (BetweenExpression value=A low=1 high=2) (= B 3))")]
    [InlineData("a NOT BETWEEN 1 AND 2", "(BetweenExpression value=A not low=1 high=2)")]
    [InlineData("a IN (1, 2)", "(InExpression value=A list=[1 2])")]
    [InlineData("a NOT IN (1)", "(InExpression value=A not list=[1])")]
    [InlineData("(a, b) IN ((1, 2), (3, 4))", "(InExpression value=(ExpressionList items=[A B]) list=[(ExpressionList items=[1 2]) (ExpressionList items=[3 4])])")]
    [InlineData("x LIKE 'a%' ESCAPE '\\'", "(LikeExpression value=X kind=Like pattern='a%' escape='\\')")]
    [InlineData("x NOT LIKEC 'a'", "(LikeExpression value=X not kind=Likec pattern='a')")]
    [InlineData("a IS NULL", "(IsExpression value=A test=Null)")]
    [InlineData("a IS NOT NAN", "(IsExpression value=A not test=Nan)")]
    [InlineData("a IS A SET", "(IsExpression value=A test=ASet)")]
    [InlineData("a IS EMPTY", "(IsExpression value=A test=Empty)")]
    [InlineData("(1=1) IS TRUE", "(IsExpression value=(paren (= 1 1)) test=True)")]
    [InlineData("(1=1) IS NOT FALSE", "(IsExpression value=(paren (= 1 1)) not test=False)")]
    [InlineData("a IS TRUE AND b IS NOT TRUE", "(AND (IsExpression value=A test=True) (IsExpression value=B not test=True))")]
    [InlineData("x IS JSON STRICT WITH UNIQUE KEYS", "(IsExpression value=X test=Json jsonClauses=[(FlagClause flag=Strict) (FlagClause flag=WithUniqueKeys)])")]
    [InlineData("x IS OF (ONLY t1, t2)", "(IsExpression value=X test=OfType ofTypes=[(IsOfType only type=(DataType name=T1)) (IsOfType type=(DataType name=T2))])")]
    [InlineData("a MEMBER OF b", "(MEMBER OF A B)")]
    [InlineData("a NOT MEMBER OF b", "(NOT MEMBER OF A B)")]
    [InlineData("a MULTISET UNION DISTINCT b", "(MULTISET UNION DISTINCT A B)")]
    [InlineData("x = ALL (1, 2)", "(QuantifiedComparison left=X operator== quantifier=All right=(ExpressionList items=[1 2]))")]
    public void Predicates(string sql, string expected) => Assert.Equal(expected, Shape.Expr(sql));

    [Theory]
    [InlineData("CASE WHEN a > 1 THEN 'x' ELSE 'y' END", "(CaseExpression whens=[(WhenClause when=(> A 1) then='x')] else='y')")]
    [InlineData("CASE a WHEN 1 THEN 2 WHEN 3 THEN 4 END", "(CaseExpression operand=A whens=[(WhenClause when=1 then=2) (WhenClause when=3 then=4)])")]
    [InlineData("CAST(a AS NUMBER(10,2))", "(CastExpression kind=Cast value=A type=(DataType name=NUMBER arguments=[10 2]))")]
    [InlineData("CAST(a AS NUMBER(*,2))", "(CastExpression kind=Cast value=A type=(DataType name=NUMBER arguments=[* 2]))")]
    [InlineData("CAST(c AS VARCHAR2(10 CHAR))", "(CastExpression kind=Cast value=C type=(DataType name=VARCHAR2 arguments=[10] semantics=Char))")]
    [InlineData("CAST(c AS VARCHAR2(10 BYTE))", "(CastExpression kind=Cast value=C type=(DataType name=VARCHAR2 arguments=[10] semantics=Byte))")]
    [InlineData("CAST(b AS TIMESTAMP(6) WITH TIME ZONE)", "(CastExpression kind=Cast value=B type=(DataType name=TIMESTAMP WITH TIME ZONE arguments=[6]))")]
    [InlineData("CAST(b AS INTERVAL DAY(2) TO SECOND(6))", "(CastExpression kind=Cast value=B type=(DataType name=INTERVAL DAY TO SECOND arguments=[2 6]))")]
    [InlineData("CAST(b AS DOUBLE PRECISION)", "(CastExpression kind=Cast value=B type=(DataType name=DOUBLE PRECISION))")]
    [InlineData("CAST(b AS hr.my_type)", "(CastExpression kind=Cast value=B type=(DataType name=HR.MY_TYPE))")]
    [InlineData("CAST(x AS NUMBER DEFAULT 0 ON CONVERSION ERROR, '999')", "(CastExpression kind=Cast value=X type=(DataType name=NUMBER) onConversionError=(OnBehaviorClause behavior=Default value=0 event=ConversionError) format='999')")]
    [InlineData("CAST(MULTISET(SELECT 1 FROM dual) AS t_tab)", "(CastExpression kind=Cast multiset value=(SubqueryExpression query=(SelectStatement body=(QuerySpecification selectList=[(ExpressionSelectItem expression=1)] from=[(NamedTable name=[DUAL])]))) type=(DataType name=T_TAB))")]
    [InlineData("TREAT(x AS REF t)", "(CastExpression kind=Treat value=X ref type=(DataType name=T))")]
    [InlineData("VALIDATE_CONVERSION(x AS DATE, 'YYYY')", "(CastExpression kind=ValidateConversion value=X type=(DataType name=DATE) format='YYYY')")]
    [InlineData("XMLSERIALIZE(DOCUMENT x AS CLOB INDENT SIZE = 2)", "(CastExpression kind=XmlSerialize value=X type=(DataType name=CLOB) clauses=[(FlagClause flag=Document) (FlagClause flag=Indent) (ValueClause kind=IndentSize value=2)])")]
    [InlineData("EXTRACT(YEAR FROM d)", "(ExtractExpression field=Year source=D)")]
    [InlineData("EXTRACT(TIMEZONE_REGION FROM ts)", "(ExtractExpression field=TimezoneRegion source=TS)")]
    [InlineData("TRIM(BOTH 'x' FROM s)", "(TrimExpression spec=Both characters='x' source=S)")]
    [InlineData("TRIM(LEADING FROM s)", "(TrimExpression spec=Leading source=S)")]
    [InlineData("TRIM('x' FROM s)", "(TrimExpression characters='x' source=S)")]
    [InlineData("TRIM(s)", "(TrimExpression source=S)")]
    public void CaseCastAndSpecialForms(string sql, string expected) => Assert.Equal(expected, Shape.Expr(sql));

    [Theory]
    [InlineData("COUNT(*)", "(FunctionCall name=[COUNT] arguments=[(Argument value=*)])")]
    [InlineData("SUM(DISTINCT x)", "(FunctionCall name=[SUM] quantifier=Distinct arguments=[(Argument value=X)])")]
    [InlineData("pkg.fn(a => 1, b => 2)", "(FunctionCall name=[PKG FN] arguments=[(Argument name=A value=1) (Argument name=B value=2)])")]
    [InlineData("SYSDATE - 1", "(- SYSDATE 1)")]
    [InlineData("seq.NEXTVAL", "SEQ.NEXTVAL")]
    [InlineData("ROW_NUMBER() OVER (PARTITION BY d ORDER BY s DESC)", "(FunctionCall name=[ROW_NUMBER] over=(WindowSpec partitionBy=[D] orderBy=[(OrderByItem expression=S descending)]))")]
    [InlineData("SUM(x) OVER (ORDER BY y ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW)", "(FunctionCall name=[SUM] arguments=[(Argument value=X)] over=(WindowSpec orderBy=[(OrderByItem expression=Y)] frame=(WindowFrame unit=Rows startBound=(FrameBound kind=UnboundedPreceding) endBound=(FrameBound kind=CurrentRow))))")]
    [InlineData("SUM(x) OVER (RANGE 5 PRECEDING)", "(FunctionCall name=[SUM] arguments=[(Argument value=X)] over=(WindowSpec frame=(WindowFrame unit=Range startBound=(FrameBound kind=Preceding offset=5))))")]
    [InlineData("LAG(x, 1) IGNORE NULLS OVER w", "(FunctionCall name=[LAG] arguments=[(Argument value=X) (Argument value=1)] ignoreNulls over=(WindowSpec name=W))")]
    [InlineData("MAX(sal) KEEP (DENSE_RANK FIRST ORDER BY hire)", "(FunctionCall name=[MAX] arguments=[(Argument value=SAL)] keep=(KeepClause first orderBy=[(OrderByItem expression=HIRE)]))")]
    [InlineData("LISTAGG(n, ',') WITHIN GROUP (ORDER BY n)", "(FunctionCall name=[LISTAGG] arguments=[(Argument value=N) (Argument value=',')] withinGroup=[(OrderByItem expression=N)])")]
    [InlineData("LISTAGG(n, ',' ON OVERFLOW TRUNCATE '...' WITH COUNT)", "(FunctionCall name=[LISTAGG] clauses=[(OverflowClause truncate indicator='...' withCount=true)] arguments=[(Argument value=N) (Argument value=',')])")]
    [InlineData("LISTAGG(n ON OVERFLOW ERROR)", "(FunctionCall name=[LISTAGG] clauses=[(OverflowClause)] arguments=[(Argument value=N)])")]
    [InlineData("SQL%BULK_ROWCOUNT(i)", "(FunctionCall name=[SQL] attribute=BULK_ROWCOUNT arguments=[(Argument value=I)])")]
    [InlineData("NEW my_type(1, 2)", "(FunctionCall name=[MY_TYPE] new arguments=[(Argument value=1) (Argument value=2)])")]
    [InlineData("f(x).y", "(MemberAccess target=(FunctionCall name=[F] arguments=[(Argument value=X)]) member=Y)")]
    [InlineData("SQL%ROWCOUNT", "SQL%ROWCOUNT")]
    [InlineData("c1%NOTFOUND", "C1%NOTFOUND")]
    public void FunctionCalls(string sql, string expected) => Assert.Equal(expected, Shape.Expr(sql));

    [Theory]
    [InlineData("JSON_VALUE(j, '$.a' RETURNING NUMBER ERROR ON ERROR)",
        "(FunctionCall name=[JSON_VALUE] clauses=[(ReturningClause type=(DataType name=NUMBER)) (OnBehaviorClause behavior=Error event=Error)] arguments=[(Argument value=J) (Argument value='$.a')])")]
    [InlineData("JSON_VALUE(j, '$.a' DEFAULT 0 ON EMPTY NULL ON MISMATCH)",
        "(FunctionCall name=[JSON_VALUE] clauses=[(OnBehaviorClause behavior=Default value=0 event=Empty) (OnBehaviorClause behavior=Null event=Mismatch)] arguments=[(Argument value=J) (Argument value='$.a')])")]
    [InlineData("JSON_QUERY(j, '$' WITH CONDITIONAL ARRAY WRAPPER KEEP QUOTES ON SCALAR STRING EMPTY ARRAY ON ERROR)",
        "(FunctionCall name=[JSON_QUERY] clauses=[(WrapperClause with conditional array) (QuotesClause keep onScalarString) (OnBehaviorClause behavior=EmptyArray event=Error)] arguments=[(Argument value=J) (Argument value='$')])")]
    [InlineData("JSON_OBJECT(KEY 'a' VALUE 1, 'b' VALUE x FORMAT JSON NULL ON NULL)",
        "(FunctionCall name=[JSON_OBJECT] clauses=[(NullOnClause)] arguments=[(Argument keyword=Key value='a' mappedValue=1) (Argument value='b' mappedValue=X format=(FormatClause kind=Json))])")]
    [InlineData("JSON_ARRAYAGG(x ORDER BY y ABSENT ON NULL RETURNING CLOB)",
        "(FunctionCall name=[JSON_ARRAYAGG] clauses=[(NullOnClause absent) (ReturningClause type=(DataType name=CLOB))] arguments=[(Argument value=X)] orderBy=[(OrderByItem expression=Y)])")]
    [InlineData("JSON_SERIALIZE(x RETURNING VARCHAR2(100) PRETTY ASCII TRUNCATE)",
        "(FunctionCall name=[JSON_SERIALIZE] clauses=[(ReturningClause type=(DataType name=VARCHAR2 arguments=[100])) (FlagClause flag=Pretty) (FlagClause flag=Ascii) (FlagClause flag=Truncate)] arguments=[(Argument value=X)])")]
    [InlineData("JSON_TRANSFORM(j, SET '$.a' = 1 IGNORE ON MISSING, REMOVE '$.b', RENAME '$.c' WITH 'd', SORT '$.e' DESC)",
        "(FunctionCall name=[JSON_TRANSFORM] operations=[(JsonTransformOperation op=Set path='$.a' value=1 clauses=[(OnBehaviorClause behavior=Ignore event=Missing)]) (JsonTransformOperation op=Remove path='$.b') (JsonTransformOperation op=Rename path='$.c' value='d') (JsonTransformOperation op=Sort path='$.e' descending)] arguments=[(Argument value=J)])")]
    [InlineData("TO_NUMBER(x DEFAULT 0 ON CONVERSION ERROR, 'fmt')",
        "(FunctionCall name=[TO_NUMBER] arguments=[(Argument value=X onConversionError=(OnBehaviorClause behavior=Default value=0 event=ConversionError)) (Argument value='fmt')])")]
    [InlineData("XMLELEMENT(NAME \"e\", XMLATTRIBUTES(a AS \"x\"), b)",
        "(FunctionCall name=[XMLELEMENT] arguments=[(Argument keyword=Name value=\"e\") (Argument value=(FunctionCall name=[XMLATTRIBUTES] arguments=[(Argument value=A alias=\"x\")])) (Argument value=B)])")]
    [InlineData("XMLQUERY('/a' PASSING BY VALUE x AS \"v\" RETURNING CONTENT NULL ON EMPTY)",
        "(FunctionCall name=[XMLQUERY] clauses=[(PassingClause byValue items=[(ExpressionSelectItem expression=X alias=\"v\")]) (ReturningClause content) (OnBehaviorClause behavior=Null event=Empty)] arguments=[(Argument value='/a')])")]
    [InlineData("TRANSLATE(x USING NCHAR_CS)", "(FunctionCall name=[TRANSLATE] clauses=[(UsingCharsetClause charset=NCHAR_CS)] arguments=[(Argument value=X)])")]
    // 'key':value with no space: ':' right after a string key is the separator, not the start of a bind :1
    [InlineData("JSON_OBJECT('a':1)", "(FunctionCall name=[JSON_OBJECT] arguments=[(Argument value='a' mappedValue=1)])")]
    [InlineData("JSON_OBJECT('a':x)", "(FunctionCall name=[JSON_OBJECT] arguments=[(Argument value='a' mappedValue=X)])")]
    [InlineData("JSON_OBJECT('a':1+2, 'b' : 'c')", "(FunctionCall name=[JSON_OBJECT] arguments=[(Argument value='a' mappedValue=(+ 1 2)) (Argument value='b' mappedValue='c')])")]
    [InlineData("JSON_OBJECT(UPPER('a'):1)", "(FunctionCall name=[JSON_OBJECT] arguments=[(Argument value=(FunctionCall name=[UPPER] arguments=[(Argument value='a')]) mappedValue=1)])")]
    public void JsonObjectColonSeparator(string sql, string expected) => Assert.Equal(expected, Shape.Expr(sql));

    // Real binds keep lexing as binds everywhere else, including right after a string or a ')' in a non-key position.
    [Theory]
    [InlineData(":1", ":1")]
    [InlineData("y = :1", "(= Y :1)")]
    [InlineData("(:1, :2)", "(ExpressionList items=[:1 :2])")]
    [InlineData("x = :name", "(= X :name)")]
    public void BindsStillLexAsBinds(string sql, string expected) => Assert.Equal(expected, Shape.Expr(sql));

    /// <summary>Token <paramref name="index"/> of the lexed input (EOF is included): ':' after a string key is an operator, elsewhere ':x' is a bind.</summary>
    [Theory]
    [InlineData("'a':1", 1, TokenType.Operator, ":")]
    [InlineData("'a' :1", 1, TokenType.Operator, ":")]
    [InlineData("'a' : 1", 1, TokenType.Operator, ":")]
    [InlineData("'a':1", 2, TokenType.Number, "1")]
    [InlineData("f(x):1", 4, TokenType.Operator, ":")]
    [InlineData("'a' := 1", 1, TokenType.Operator, ":=")]
    [InlineData("y = :1", 2, TokenType.Bind, ":1")]
    [InlineData("(:1", 1, TokenType.Bind, ":1")]
    [InlineData("INTO :x", 1, TokenType.Bind, ":x")]
    [InlineData("x := :1", 2, TokenType.Bind, ":1")]
    public void ColonLexing(string sql, int index, TokenType type, string text)
    {
        var t = Lexer.Tokenize(sql)[index];
        Assert.Equal(type, t.Type);
        Assert.Equal(text, t.Text);
    }

    [Theory]
    [InlineData("x AT TIME ZONE 'UTC'", "(AtTimeZoneExpression value=X zone='UTC')")]
    [InlineData("x AT LOCAL", "(AtTimeZoneExpression value=X local)")]
    [InlineData("a COLLATE binary_ci", "(CollateExpression value=A collation=BINARY_CI)")]
    [InlineData("s['x', 2000]", "(CellReference base=S subscripts=['x' 2000])")]
    [InlineData("s[ANY, 2000]", "(CellReference base=S subscripts=[ANY 2000])")]
    [InlineData("s[FOR p IN ('a', 'b'), 2000]", "(CellReference base=S subscripts=[(ForIterationSubscript dimensions=[P] inList=['a' 'b']) 2000])")]
    [InlineData("s[FOR y FROM 2000 TO 2010 INCREMENT 2]", "(CellReference base=S subscripts=[(ForIterationSubscript dimensions=[Y] from=2000 to=2010 step=2)])")]
    // JSON simple dot-notation continues after an array step: the step is a CellReference, the member a MemberAccess.
    [InlineData("t.c.a[0].b", "(MemberAccess target=(CellReference base=T.C.A subscripts=[0]) member=B)")]
    [InlineData("t.c.a[*].b", "(MemberAccess target=(CellReference base=T.C.A subscripts=[*]) member=B)")]
    [InlineData("t.c.a[0 to 2].b", "(MemberAccess target=(CellReference base=T.C.A subscripts=[(SubscriptRange from=0 to=2)]) member=B)")]
    [InlineData("t.c.a[1,3].b", "(MemberAccess target=(CellReference base=T.C.A subscripts=[1 3]) member=B)")]
    [InlineData("t.c.a[0].b[1].c", "(MemberAccess target=(CellReference base=(MemberAccess target=(CellReference base=T.C.A subscripts=[0]) member=B) subscripts=[1]) member=C)")]
    [InlineData("t.c.a[0].b.c", "(MemberAccess target=(MemberAccess target=(CellReference base=T.C.A subscripts=[0]) member=B) member=C)")]
    [InlineData("PRIOR x", "(PRIOR X)")]
    [InlineData("CONNECT_BY_ROOT x", "(CONNECT_BY_ROOT X)")]
    [InlineData("CURSOR(SELECT 1 FROM dual)", "(CursorExpression query=(SelectStatement body=(QuerySpecification selectList=[(ExpressionSelectItem expression=1)] from=[(NamedTable name=[DUAL])])))")]
    [InlineData("EXISTS (SELECT 1 FROM dual)", "(ExistsExpression subquery=(SelectStatement body=(QuerySpecification selectList=[(ExpressionSelectItem expression=1)] from=[(NamedTable name=[DUAL])])))")]
    public void Postfix_And_Misc(string sql, string expected) => Assert.Equal(expected, Shape.Expr(sql));

    [Theory]
    [InlineData("(a - b) DAY TO SECOND","(IntervalExpression value=(paren (- A B)) from=Day to=Second)")]
    [InlineData("(SYSTIMESTAMP - SYSTIMESTAMP) DAY(9) TO SECOND", "(IntervalExpression value=(paren (- SYSTIMESTAMP SYSTIMESTAMP)) from=Day fromPrecision=9 to=Second)")]
    [InlineData("(a - b) DAY(2) TO SECOND(6)", "(IntervalExpression value=(paren (- A B)) from=Day fromPrecision=2 to=Second toPrecision=6)")]
    [InlineData("(a - b) YEAR TO MONTH", "(IntervalExpression value=(paren (- A B)) from=Year to=Month)")]
    [InlineData("(a - b) YEAR(4) TO MONTH", "(IntervalExpression value=(paren (- A B)) from=Year fromPrecision=4 to=Month)")]
    [InlineData("x + (a - b) DAY TO SECOND", "(+ X (IntervalExpression value=(paren (- A B)) from=Day to=Second))")]
    public void IntervalExpressions(string sql, string expected) => Assert.Equal(expected, Shape.Expr(sql));

    [Theory]
    [InlineData("SELECT (a - b) day FROM t", "(SelectStatement body=(QuerySpecification selectList=[(ExpressionSelectItem expression=(paren (- A B)) alias=DAY)] from=[(NamedTable name=[T])]))")]
    [InlineData("SELECT (a - b) year FROM t", "(SelectStatement body=(QuerySpecification selectList=[(ExpressionSelectItem expression=(paren (- A B)) alias=YEAR)] from=[(NamedTable name=[T])]))")]
    public void IntervalWordsStillWorkAsAliases(string sql, string expected) => Assert.Equal(expected, Shape.Of(sql));

    [Theory]
    [InlineData("SELECT (a - b) DAY TO MONTH FROM t")]   // DAY only goes with SECOND, YEAR only with MONTH
    [InlineData("SELECT (a - b) YEAR TO SECOND FROM t")]
    [InlineData("SELECT a - b DAY TO SECOND FROM t")]     // operand must be parenthesized
    public void IntervalExpressionsRejectInvalidForms(string sql) => Assert.False(SqlParser.Parse(sql).Success);

}
