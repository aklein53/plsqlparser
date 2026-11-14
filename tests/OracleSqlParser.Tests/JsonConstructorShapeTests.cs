using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

/// <summary>JSON { } / JSON [ ] constructors and JSON relational duality views (SQL form).</summary>
public class JsonConstructorShapeTests
{
    [Theory]
    [InlineData("SELECT JSON {'a' : 1, 'b' : 'x'} FROM dual")]
    [InlineData("SELECT JSON ['a', 1] FROM dual")]
    [InlineData("SELECT JSON {*} FROM person p")]
    [InlineData("SELECT JSON {'id' : p.id, 'name' : p.name ABSENT ON NULL} FROM person p")]
    [InlineData("SELECT JSON {'id' : p.id, 'name' : p.name NULL ON NULL} FROM person p")]
    [InlineData("SELECT JSON {'id' : p.id RETURNING VARCHAR2(20)} FROM person p")]
    [InlineData("SELECT JSON {'id' : p.id} AS j FROM person p")]
    [InlineData("SELECT JSON {'k' : [SELECT JSON {'v' : d.dst} FROM knows d WHERE d.src = 1]} FROM dual")]
    [InlineData("SELECT JSON {'id' : p.id, 'friends' : [SELECT JSON {'dst' : k.dst} FROM knows k WITH INSERT UPDATE DELETE WHERE k.src = p.id]} FROM person p")]
    [InlineData("SELECT JSON {'a':1} FROM dual")]
    public void JsonConstructor_Parses(string sql) => Assert.True(SqlParser.Parse(sql).Success);

    [Theory]
    [InlineData("SELECT JSON {'a' : 1, 'b' : 'x'} FROM dual", "(JsonObjectConstructor members=[(JsonMember key='a' value=1) (JsonMember key='b' value='x')])")]
    [InlineData("SELECT JSON {'a':1} FROM dual", "(JsonObjectConstructor members=[(JsonMember key='a' value=1)])")]
    public void JsonConstructor_Shape(string sql, string expected) => Assert.Equal(expected, Shape.Of<JsonObjectConstructor>(sql));

    [Fact]
    public void JsonArray_Shape() =>
        Assert.Equal("(JsonArrayConstructor elements=['a' 1])", Shape.Of<JsonArrayConstructor>("SELECT JSON ['a', 1] FROM dual"));

    [Theory]
    [InlineData("CREATE JSON RELATIONAL DUALITY VIEW dv1 AS SELECT JSON {'_id' : p.id, 'name' : p.name} FROM person p WITH INSERT UPDATE DELETE")]
    [InlineData("CREATE OR REPLACE JSON RELATIONAL DUALITY VIEW dv2 AS SELECT JSON {'_id' : p.id, 'name' : p.name, 'friends' : [SELECT JSON {'_id' : k.dst, 'since' : k.since} FROM knows k WITH INSERT UPDATE DELETE WHERE k.src = p.id]} FROM person p WITH INSERT UPDATE DELETE")]
    [InlineData("CREATE JSON RELATIONAL DUALITY VIEW dv3 AS SELECT JSON {'_id' : p.id, 'name' : p.name} FROM person p WITH NOINSERT NOUPDATE NODELETE")]
    [InlineData("CREATE JSON RELATIONAL DUALITY VIEW dv4 AS SELECT JSON {'_id' : p.id, 'name' : p.name} FROM person p WITH NOCHECK")]
    [InlineData("CREATE JSON RELATIONAL DUALITY VIEW dv5 AS SELECT JSON {'_id' : p.id} FROM person p WITH ETAG")]
    [InlineData("CREATE JSON RELATIONAL DUALITY VIEW dv6 AS SELECT JSON {'_id' : p.id} FROM person p WITH NO ETAG")]
    [InlineData("CREATE JSON RELATIONAL DUALITY VIEW dv7 AS SELECT JSON {'_id' : p.id} FROM person p WITH NO CHECK")]
    [InlineData("CREATE JSON DUALITY VIEW dv8 AS SELECT JSON {'_id' : p.id} FROM person p WITH INSERT")]
    [InlineData("CREATE JSON RELATIONAL DUALITY VIEW dv9 AS SELECT JSON {'_id' : p.id} FROM person p")]
    public void DualityView_Parses(string sql)
    {
        var parsed = SqlParser.Parse(sql);
        Assert.True(parsed.Success, string.Join("; ", parsed.Errors));
        Assert.Single(parsed.Script.Statements.OfType<CreateDualityViewStatement>());
    }

    [Fact]
    public void DualityView_TableAnnotationsAreTyped()
    {
        var dv = Shape.Parse("CREATE JSON RELATIONAL DUALITY VIEW dv AS SELECT JSON {'_id' : p.id} FROM person p WITH NOINSERT NOUPDATE WITH ETAG")
            .Descendants().OfType<NamedTable>().Single();
        Assert.Equal(new[] { DualityAnnotation.NoInsert, DualityAnnotation.NoUpdate, DualityAnnotation.Etag },
            dv.DualityAnnotations.Select(a => a.Kind).ToArray());
        Assert.Equal(new[] { "CREATE JSON RELATIONAL DUALITY VIEW dv AS SELECT JSON {'_id' : p.id} FROM person p WITH NOINSERT NOUPDATE ETAG" },
            new[] { new SqlScriptGenerator(new SqlScriptGeneratorOptions()).Generate(Shape.Parse("CREATE JSON RELATIONAL DUALITY VIEW dv AS SELECT JSON {'_id' : p.id} FROM person p WITH NOINSERT NOUPDATE WITH ETAG").Statements[0]) });
    }

    [Theory]
    [InlineData("SELECT JSON {'a' 1} FROM dual")]
    [InlineData("SELECT JSON {'a' : 1,} FROM dual")]
    [InlineData("SELECT * FROM person p WITH CHECK OPTION")]
    public void JsonAndDuality_OracleRejectedForms(string sql) => Assert.False(SqlParser.Parse(sql).Success);

    [Theory]
    [InlineData("CREATE JSON DUALITY VIEW dvg AS dept @insert @update { _id : deptno, dname : dname }")]
    public void DualityView_GraphQlFormKeptAsTokens(string sql)
    {
        var dv = Shape.Parse(sql).Descendants().OfType<CreateDualityViewStatement>().Single();
        Assert.NotEmpty(dv.OtherClauses);
    }
}
