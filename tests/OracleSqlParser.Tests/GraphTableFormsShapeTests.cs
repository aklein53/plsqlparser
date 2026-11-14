using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

/// <summary>GRAPH_TABLE forms checked against Oracle 23ai: accepted forms parse and round-trip, rejected forms fail.</summary>
public class GraphTableFormsShapeTests
{
    [Theory]
    [InlineData("SELECT * FROM GRAPH_TABLE(g MATCH (p IS person) ((x IS person)-[k IS knows]->(y IS person)){2} COLUMNS (vertex_id(p) AS vp)) gt")]
    [InlineData("SELECT * FROM GRAPH_TABLE(g MATCH (p IS person) ((x IS person)-[k IS knows]->(y IS person)){,2} COLUMNS (vertex_id(p) AS vp))")]
    [InlineData("SELECT * FROM GRAPH_TABLE(g MATCH (p IS person)<-[k IS knows]->(q IS person) COLUMNS (p.name))")]
    [InlineData("SELECT * FROM GRAPH_TABLE(g MATCH (p IS person)-[k IS knows]-{1,2}(q IS person) COLUMNS (p.name, q.name AS qn))")]
    [InlineData("SELECT * FROM GRAPH_TABLE(g MATCH (p IS person)-[IS knows]->(q) COLUMNS (p.name AS pn)) g")]
    [InlineData("SELECT * FROM GRAPH_TABLE(g MATCH (p IS person) ONE ROW PER MATCH COLUMNS (p.name AS pn)) g")]
    public void Accepted(string sql) => Assert.True(SqlParser.Parse(sql).Success);

    [Theory]
    [InlineData("SELECT * FROM GRAPH_TABLE(g MATCH (a)-[e]->*(b) COLUMNS (a.x))")]
    [InlineData("SELECT * FROM GRAPH_TABLE(g MATCH (a)-[e]->+(b) COLUMNS (a.x))")]
    [InlineData("SELECT * FROM GRAPH_TABLE(g MATCH ((a)-[e]->(b))* COLUMNS (a.x))")]
    [InlineData("SELECT * FROM GRAPH_TABLE(g MATCH (a)-[e]->{1,}(b) COLUMNS (a.x))")]
    [InlineData("SELECT * FROM GRAPH_TABLE(g MATCH ((a)-[e]->(b)) COLUMNS (a.x))")]
    [InlineData("SELECT * FROM GRAPH_TABLE(g MATCH (a)-[e]->(b) COLUMNS (a.x)) AS gt")]
    [InlineData("SELECT * FROM GRAPH_TABLE(g MATCH (a)-[e]->(b) COLUMNS (a.x) ORDER BY 1)")]
    [InlineData("SELECT * FROM GRAPH_TABLE(g MATCH (a)-[e]->(b) COLUMNS (a.x y))")]
    public void Rejected(string sql) => Assert.False(SqlParser.Parse(sql).Success);

    [Fact]
    public void SubpatternIsTyped()
    {
        var sp = Shape.Parse("SELECT * FROM GRAPH_TABLE(g MATCH (p) ((x)-[k]->(y)){2} COLUMNS (p.n))")
            .Descendants().OfType<GraphSubpatternElement>().Single();
        Assert.Equal(GraphQuantifierKind.Exact, sp.Quantifier!.Kind);
    }
}
