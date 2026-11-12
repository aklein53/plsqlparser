using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

public class MiscShapeTests
{
    [Theory]
    [InlineData("INSERT INTO t VALUES rec", "(InsertStatement target=(NamedTable name=[T]) recordValue=REC)")]
    [InlineData("UPDATE t SET ROW = rec WHERE id = 1", "(UpdateStatement target=(NamedTable name=[T]) set=[(SetClause row value=REC)] where=(= ID 1))")]
    [InlineData("UPDATE t1 SET a = t2.a FROM t2 WHERE t1.id = t2.id",
        "(UpdateStatement target=(NamedTable name=[T1]) set=[(SetClause columns=[A] value=T2.A)] from=[(NamedTable name=[T2])] where=(= T1.ID T2.ID))")]
    [InlineData("MERGE INTO t USING s ON (t.id = s.id) WHEN MATCHED THEN DELETE WHERE t.x = 1",
        "(MergeStatement target=(NamedTable name=[T]) source=(NamedTable name=[S]) on=(paren (= T.ID S.ID)) clauses=[(MergeClause matched deleteWhere=(= T.X 1))])")]
    [InlineData("MERGE INTO t USING s ON (t.id = s.id) WHEN MATCHED AND s.v > 0 THEN UPDATE SET t.v = s.v",
        "(MergeStatement target=(NamedTable name=[T]) source=(NamedTable name=[S]) on=(paren (= T.ID S.ID)) clauses=[(MergeClause matched andCondition=(> S.V 0) set=[(SetClause columns=[T.V] value=S.V)])])")]
    public void DmlForms(string sql, string expected) => Assert.Equal(expected, Shape.Of(sql));

    [Fact]
    public void ValuesTableConstructor() =>
        Assert.Equal("(ValuesTable rows=[[1 'a'] [2 'b']] columnAliases=[C1 C2] alias=V)",
            Shape.Of<ValuesTable>("SELECT * FROM (VALUES (1, 'a'), (2, 'b')) v (c1, c2)"));

    [Fact]
    public void Descendants_ReachNodesInsideNestedLists()
    {
        var lits = Shape.Parse("INSERT INTO t VALUES (1, 2), (3, 4)").Descendants().OfType<Literal>().Select(l => l.Value).ToList();
        Assert.Equal(["1", "2", "3", "4"], lits);
        var ids = Shape.Parse("BEGIN NULL; EXCEPTION WHEN a.b OR c THEN NULL; END;").Descendants().OfType<Identifier>().Select(i => i.Value).ToList();
        Assert.Equal(["A", "B", "C"], ids);
    }

    [Fact]
    public void Visitor_CollectsAllTableNames()
    {
        var names = Shape.Parse("SELECT * FROM a JOIN (SELECT * FROM b) s ON 1=1 WHERE EXISTS (SELECT 1 FROM c) AND x IN (SELECT y FROM d)")
            .Descendants().OfType<NamedTable>().Select(t => t.Name[0].Value).OrderBy(x => x).ToList();
        Assert.Equal(["A", "B", "C", "D"], names);
    }

    [Fact]
    public void Json_Output_HasTypeDiscriminatorsAndEnumNames()
    {
        var json = AstJson.Serialize(Shape.Parse("SELECT 1 FROM a LEFT JOIN b ON 1 = 1").Statements[0], includePositions: true, indented: false);
        Assert.Contains("\"type\":\"JoinTable\"", json);
        Assert.Contains("\"kind\":\"Left\"", json);
        Assert.Contains("\"span\":{\"start\":0", json);
    }

    [Fact]
    public void ParseStatement_And_ParseExpression_ThrowOnError()
    {
        Assert.Throws<ParseException>(() => SqlParser.ParseStatement("SELECT FROM"));
        Assert.Throws<ParseException>(() => SqlParser.ParseExpression("1 +"));
        Assert.IsType<SelectStatement>(SqlParser.ParseStatement("SELECT 1 FROM dual;"));
    }
}
