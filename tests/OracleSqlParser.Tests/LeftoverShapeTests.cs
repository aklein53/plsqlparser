using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

/// <summary>Epic 4 leftovers: FETCH ... WITH TARGET ACCURACY, JSON_OBJECT key:1, JSON steps in PL/SQL, $IF in parameter lists.</summary>
public class LeftoverShapeTests
{
    [Theory]
    [InlineData("SELECT * FROM t FETCH APPROX FIRST 10 ROWS ONLY WITH TARGET ACCURACY 90 PERCENT",
        "(FetchClause count=10 accuracy=Approximate targetAccuracy=90)")]
    public void Fetch_TargetAccuracy(string sql, string expected) => Assert.Equal(expected, Shape.Of<FetchClause>(sql));

    [Theory]
    [InlineData("SELECT JSON_OBJECT(k:1) FROM dual", "(FunctionCall name=[JSON_OBJECT] arguments=[(Argument value=K mappedValue=1)])")]
    [InlineData("SELECT JSON_OBJECT('a':1, b:2) FROM dual", "(FunctionCall name=[JSON_OBJECT] arguments=[(Argument value='a' mappedValue=1) (Argument value=B mappedValue=2)])")]
    public void JsonObject_KeyColonNumber(string sql, string expected) => Assert.Equal(expected, Shape.Of<FunctionCall>(sql));

    [Theory]
    [InlineData("SELECT UPPER(x:1) FROM dual")]
    [InlineData("SELECT NVL(x:1, 2) FROM dual")]
    public void KeyColonNumber_OnlyInsideJsonObject(string sql) => Assert.False(SqlParser.Parse(sql).Success);

    [Theory]
    [InlineData("SELECT :x FROM dual", ":x")]
    public void Bind_AfterSqlKeywordStillBind(string sql, string expected) => Assert.Equal(expected, Shape.Of<BindVariable>(sql));

    [Theory]
    [InlineData("SELECT t.c.a[0] FROM t")]
    [InlineData("DECLARE n NUMBER; BEGIN SELECT t.c.a[0] INTO n FROM t; END;")]
    [InlineData("DECLARE n NUMBER; BEGIN UPDATE t SET x = t.c.a[0]; END;")]
    [InlineData("DECLARE n NUMBER; BEGIN DELETE FROM t WHERE t.c.a[0] = 1; END;")]
    [InlineData("DECLARE n NUMBER; BEGIN INSERT INTO t (x) SELECT t.c.a[0] FROM s; END;")]
    [InlineData("DECLARE n NUMBER; BEGIN FOR r IN (SELECT t.c.a[0] AS v FROM t) LOOP NULL; END LOOP; END;")]
    public void JsonArrayStep_AcceptedInSql(string sql) => Assert.True(SqlParser.Parse(sql).Success);

    [Theory]
    [InlineData("BEGIN n := t.c.a[0]; END;")]
    [InlineData("DECLARE n NUMBER := t.c.a[0]; BEGIN NULL; END;")]
    public void JsonArrayStep_RejectedInPlsqlExpression(string sql) => Assert.False(SqlParser.Parse(sql).Success);

    [Theory]
    [InlineData("CREATE PROCEDURE p(a NUMBER, $IF $$x $THEN b NUMBER $ELSIF $$y $THEN c NUMBER, d NUMBER $ELSE e NUMBER $END, f NUMBER)")]
    [InlineData("CREATE PROCEDURE p($IF $$x $THEN a NUMBER $END)")]
    public void ConditionalCompilation_ParameterBranches(string sql) => Assert.True(SqlParser.Parse(sql).Success);

    [Theory]
    [InlineData("CREATE PROCEDURE p(a NUMBER $IF $$x $THEN , b NUMBER $END) IS BEGIN NULL; END;",
        "(ConditionalParameter condition=(InquiryDirective name=$$X) thenParameters=[(Parameter name=B type=(DataType name=NUMBER))])")]
    public void ConditionalCompilation_InParameterList(string sql, string expected) =>
        Assert.Equal(expected, Shape.Of<ConditionalParameter>(sql));
}
