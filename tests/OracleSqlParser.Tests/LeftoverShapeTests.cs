using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

/// <summary>Epic 4 leftovers: FETCH ... WITH TARGET ACCURACY, JSON_OBJECT key:1, JSON steps in PL/SQL, $IF in parameter lists.</summary>
public class LeftoverShapeTests
{
    [Fact]
    public void Fetch_ApproxWithTargetAccuracy() =>
        Assert.Equal("(FetchClause count=10 accuracy=Approximate targetAccuracy=90)",
            Shape.Of<FetchClause>("SELECT * FROM t FETCH APPROX FIRST 10 ROWS ONLY WITH TARGET ACCURACY 90 PERCENT"));

    [Fact]
    public void JsonObject_BareKeyWithoutSpaceBeforeNumber() =>
        Assert.Equal("(FunctionCall name=[JSON_OBJECT] arguments=[(Argument value=K mappedValue=1)])",
            Shape.Of<FunctionCall>("SELECT JSON_OBJECT(k:1) FROM dual"));

    [Fact]
    public void Bind_AfterSqlKeywordStillBind() =>
        Assert.Equal(":x", Shape.Of<BindVariable>("SELECT :x FROM dual"));

    [Theory]
    [InlineData("SELECT t.c.a[0] FROM t")]
    [InlineData("DECLARE n NUMBER; BEGIN SELECT t.c.a[0] INTO n FROM t; END;")]
    public void JsonArrayStep_AcceptedInSql(string sql) => Assert.True(SqlParser.Parse(sql).Success);

    [Fact]
    public void JsonArrayStep_RejectedInPlsqlExpression() =>
        Assert.False(SqlParser.Parse("BEGIN n := t.c.a[0]; END;").Success);

    [Theory]
    [InlineData("CREATE PROCEDURE p(a NUMBER, $IF $$x $THEN b NUMBER $ELSIF $$y $THEN c NUMBER, d NUMBER $ELSE e NUMBER $END, f NUMBER)")]
    [InlineData("CREATE PROCEDURE p($IF $$x $THEN a NUMBER $END)")]
    public void ConditionalCompilation_ParameterBranches(string sql) => Assert.True(SqlParser.Parse(sql).Success);

    [Fact]
    public void ConditionalCompilation_InParameterList() =>
        Assert.Equal("(ConditionalParameter condition=(InquiryDirective name=$$X) thenParameters=[(Parameter name=B type=(DataType name=NUMBER))])",
            Shape.Of<ConditionalParameter>("CREATE PROCEDURE p(a NUMBER $IF $$x $THEN , b NUMBER $END) IS BEGIN NULL; END;"));
}
