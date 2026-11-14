using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

public class DomainShapeTests
{
    [Theory]
    [InlineData("CREATE DOMAIN d AS NUMBER(3)",
        "(CreateDomainStatement name=[D] type=(DataType name=NUMBER arguments=[3]))")]
    [InlineData("CREATE OR REPLACE USECASE DOMAIN IF NOT EXISTS hr.d AS VARCHAR2(20) DEFAULT 'x' CONSTRAINT nn NOT NULL CHECK (LENGTH(d) > 1) DEFERRABLE INITIALLY DEFERRED COLLATE binary_ci DISPLAY d ORDER d",
        "(CreateDomainStatement orReplace useCase ifNotExists name=[HR D] type=(DataType name=VARCHAR2 arguments=[20]) default='x' constraints=[(TableConstraint name=NN kind=NotNull) (TableConstraint kind=Check check=(> (FunctionCall name=[LENGTH] arguments=[(Argument value=D)]) 1) state=(ConstraintState deferrable=true initiallyDeferred=true))] collation=BINARY_CI display=D order=D)")]
    [InlineData("ALTER DOMAIN d ADD DISPLAY d || 'x'",
        "(AlterDomainStatement name=[D] action=(AlterDomainAction kind=AddDisplay value=(|| D 'x')))")]
    [InlineData("ALTER DOMAIN d DROP ORDER",
        "(AlterDomainStatement name=[D] action=(AlterDomainAction kind=DropOrder))")]
    [InlineData("CREATE TABLE t (c NUMBER DOMAIN d)",
        "(ColumnDefinition name=C type=(DataType name=NUMBER) domain=[D])")]
    [InlineData("CREATE TABLE t (c DOMAIN hr.d NOT NULL)",
        "(ColumnDefinition name=C constraints=[(TableConstraint kind=NotNull)] domain=[HR D])")]
    public void Domain_Typed(string sql, string expected)
    {
        var printed = sql.StartsWith("CREATE TABLE") ? Shape.Of<ColumnDefinition>(sql) : Shape.Of(sql);
        Assert.Equal(expected, printed);
    }

    [Theory]
    [InlineData("CREATE DOMAIN d AS NUMBER ANNOTATIONS (label 'Name', hidden)")]
    [InlineData("CREATE DOMAIN d AS (a NUMBER, b NUMBER)")]
    [InlineData("ALTER DOMAIN d ADD ANNOTATIONS (label 'x')")]
    [InlineData("ALTER DOMAIN d DROP ANNOTATIONS (label)")]
    public void Domain_UnmodelledClausesKeptAsTokens(string sql)
    {
        var script = Shape.Parse(sql);
        var stmt = script.Statements[0];
        var tokens = stmt switch
        {
            CreateDomainStatement c => c.OtherClauses,
            AlterDomainStatement a => a.OtherClauses,
            _ => throw new Xunit.Sdk.XunitException("unexpected statement"),
        };
        Assert.NotEmpty(tokens);
    }
}
