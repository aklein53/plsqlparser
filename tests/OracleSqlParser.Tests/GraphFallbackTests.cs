using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

/// <summary>Graph syntax outside the typed grammar still parses, with the unmodelled part kept as tokens.</summary>
public class GraphFallbackTests
{
    [Theory]
    [InlineData("SELECT * FROM GRAPH_TABLE(g MATCH ((a)-[e]->(b)){1,3} COLUMNS (a.x)) gt")]
    [InlineData("SELECT * FROM GRAPH_TABLE(g MATCH (a)-[e]->(b) WHERE a.x = 1 ONE ROW PER MATCH COLUMNS (a.x AS x, b.y)) gt")]
    public void GraphTable_UnmodelledBodyKeptAsTokens(string sql)
    {
        var gt = Shape.Parse(sql).Descendants().OfType<GraphTable>().Single();
        if (gt.Match.Count == 0) Assert.NotEmpty(gt.OtherClauses);
        else Assert.Empty(gt.OtherClauses);
    }

    [Theory]
    [InlineData("CREATE PROPERTY GRAPH g VERTEX TABLES (v PROPERTIES ARE ALL COLUMNS)")]
    [InlineData("CREATE PROPERTY GRAPH g VERTEX TABLES (v LABEL x) OPTIONS (y)")]
    [InlineData("CREATE PROPERTY GRAPH g VERTEX TABLES (p KEY (id)) EDGE TABLES (e SOURCE p DESTINATION p LABEL l NOTHING)")]
    public void PropertyGraph_UnmodelledClausesKeptAsTokens(string sql)
    {
        var pg = Shape.Parse(sql).Descendants().OfType<CreatePropertyGraphStatement>().Single();
        var elementTails = pg.VertexTables.Select(v => v.OtherClauses).Concat(pg.EdgeTables.Select(e => e.OtherClauses));
        Assert.True(pg.OtherClauses.Count > 0 || elementTails.Any(t => t.Count > 0));
    }
}
