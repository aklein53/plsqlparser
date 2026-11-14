using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

public class DomainShapeTests
{
    [Theory]
    [InlineData("CREATE DOMAIN d AS NUMBER(3)",
        "(CreateDomainStatement name=[D] type=(DataType name=NUMBER arguments=[3]))")]
    [InlineData("CREATE USECASE DOMAIN IF NOT EXISTS hr.d AS VARCHAR2(20) DEFAULT 'x' CONSTRAINT nn CHECK (LENGTH(d) > 1) NOT DEFERRABLE COLLATE binary_ci DISPLAY d ORDER d",
        "(CreateDomainStatement useCase ifNotExists name=[HR D] type=(DataType name=VARCHAR2 arguments=[20]) default='x' constraints=[(TableConstraint name=NN kind=Check check=(> (FunctionCall name=[LENGTH] arguments=[(Argument value=D)]) 1) state=(ConstraintState deferrable=false))] collation=BINARY_CI display=D order=D)")]
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
    [InlineData("CREATE DOMAIN d AS NUMBER DISPLAY VALUE ANNOTATIONS (label 'Name', hidden)", 2)]
    [InlineData("CREATE DOMAIN d AS NUMBER ANNOTATIONS (ADD label 'x')", 1)]
    [InlineData("CREATE DOMAIN IF NOT EXISTS d AS NUMBER ANNOTATIONS (ADD IF NOT EXISTS label 'x', ui)", 2)]
    public void Domain_CreateAnnotations(string sql, int items)
    {
        var d = Shape.Parse(sql).Descendants().OfType<CreateDomainStatement>().Single();
        Assert.NotNull(d.Annotations);
        Assert.Equal(items, d.Annotations!.Items.Count);
    }

    [Theory]
    [InlineData("ALTER DOMAIN d ANNOTATIONS (label 'Pos', ADD IF NOT EXISTS ui 'y', DROP IF EXISTS old)", 3)]
    [InlineData("ALTER DOMAIN d ANNOTATIONS (DROP old)", 1)]
    public void Domain_AlterAnnotations(string sql, int items)
    {
        var d = Shape.Parse(sql).Descendants().OfType<AlterDomainStatement>().Single();
        Assert.NotNull(d.Annotations);
        Assert.Equal(items, d.Annotations!.Items.Count);
        Assert.Empty(d.OtherClauses);
    }

    [Theory]
    [InlineData("CREATE OR REPLACE DOMAIN d AS NUMBER")]
    [InlineData("CREATE DOMAIN d AS (street VARCHAR2(30), zip NUMBER)")]
    [InlineData("CREATE DOMAIN d AS NUMBER CONSTRAINT c NOT NULL")]
    [InlineData("CREATE DOMAIN d AS NUMBER ANNOTATIONS (DROP old)")]
    public void Domain_OracleRejectedForms(string sql) => Assert.False(SqlParser.Parse(sql).Success);
}
