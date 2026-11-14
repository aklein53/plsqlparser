using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

public class PropertyGraphShapeTests
{
    [Theory]
    [InlineData("CREATE PROPERTY GRAPH g VERTEX TABLES (v)",
        "(CreatePropertyGraphStatement name=[G] vertexTables=[(GraphVertexTable table=[V])])")]
    [InlineData("CREATE OR REPLACE PROPERTY GRAPH hr.g VERTEX TABLES (emp AS e KEY (id) LABEL employee PROPERTIES (name, salary) LABEL manager PROPERTIES ARE ALL COLUMNS EXCEPT (secret) LABEL ghost NO PROPERTIES)",
        "(CreatePropertyGraphStatement orReplace name=[HR G] vertexTables=[(GraphVertexTable table=[EMP] alias=E key=[ID] labels=[(GraphElementLabel name=EMPLOYEE properties=(GraphPropertiesClause kind=Columns columns=[NAME SALARY])) (GraphElementLabel name=MANAGER properties=(GraphPropertiesClause kind=AllColumns except=[SECRET])) (GraphElementLabel name=GHOST properties=(GraphPropertiesClause kind=None))])])")]
    [InlineData("CREATE PROPERTY GRAPH g VERTEX TABLES (p KEY (id), c KEY (id)) EDGE TABLES (knows KEY (src_id, dst_id) SOURCE KEY (src_id) REFERENCES p (id) DESTINATION KEY (dst_id) REFERENCES c LABEL knows PROPERTIES (since))",
        "(CreatePropertyGraphStatement name=[G] vertexTables=[(GraphVertexTable table=[P] key=[ID]) (GraphVertexTable table=[C] key=[ID])] edgeTables=[(GraphEdgeTable source=(GraphEdgeEndpoint keys=[SRC_ID] table=[P] columns=[ID]) destination=(GraphEdgeEndpoint keys=[DST_ID] table=[C]) table=[KNOWS] key=[SRC_ID DST_ID] labels=[(GraphElementLabel name=KNOWS properties=(GraphPropertiesClause kind=Columns columns=[SINCE]))])])")]
    public void PropertyGraph_Typed(string sql, string expected) => Assert.Equal(expected, Shape.Of<CreatePropertyGraphStatement>(sql));
}
