using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

public class PropertyGraphShapeTests
{
    [Theory]
    [InlineData("CREATE PROPERTY GRAPH g VERTEX TABLES (v)",
        "(CreatePropertyGraphStatement name=[G] vertexTables=[(GraphVertexTable table=[V])])")]
    [InlineData("CREATE PROPERTY GRAPH g VERTEX TABLES (person PROPERTIES (id, name AS nm))",
        "(CreatePropertyGraphStatement name=[G] vertexTables=[(GraphVertexTable table=[PERSON] properties=(GraphPropertiesClause kind=Columns columns=[(GraphProperty property=ID) (GraphProperty property=NAME alias=NM)]))])")]
    public void PropertyGraph_Typed(string sql, string expected) => Assert.Equal(expected, Shape.Of<CreatePropertyGraphStatement>(sql));

    [Theory]
    [InlineData("CREATE OR REPLACE PROPERTY GRAPH hr.g VERTEX TABLES (emp AS e KEY (id) LABEL employee PROPERTIES (name, salary) LABEL manager PROPERTIES ARE ALL COLUMNS EXCEPT (secret) LABEL ghost NO PROPERTIES)")]
    [InlineData("CREATE PROPERTY GRAPH g VERTEX TABLES (p KEY (id), c KEY (id)) EDGE TABLES (knows KEY (src_id, dst_id) SOURCE KEY (src_id) REFERENCES p (id) DESTINATION KEY (dst_id) REFERENCES c (id) LABEL knows PROPERTIES (since))")]
    [InlineData("CREATE PROPERTY GRAPH g VERTEX TABLES (person KEY (id) LABEL person NO PROPERTIES LABEL x) EDGE TABLES (knows SOURCE person DESTINATION person)")]
    [InlineData("CREATE PROPERTY GRAPH g VERTEX TABLES (person PROPERTIES ARE ALL COLUMNS EXCEPT (city)) EDGE TABLES (knows KEY (src, dst) SOURCE KEY (src) REFERENCES person (id) DESTINATION KEY (dst) REFERENCES person (id) PROPERTIES (since))")]
    public void PropertyGraph_OracleAcceptedForms(string sql) => Assert.True(SqlParser.Parse(sql).Success);

    [Theory]
    // Forms Oracle rejects (checked against 23ai): a column list without KEY, REFERENCES without columns,
    // empty EXCEPT, and a trailing unknown clause.
    [InlineData("CREATE PROPERTY GRAPH g VERTEX TABLES (person KEY (id)) EDGE TABLES (knows SOURCE person (id) DESTINATION person (id))")]
    [InlineData("CREATE PROPERTY GRAPH g VERTEX TABLES (person KEY (id)) EDGE TABLES (knows SOURCE KEY (src) REFERENCES person DESTINATION person)")]
    [InlineData("CREATE PROPERTY GRAPH g VERTEX TABLES (person PROPERTIES ARE ALL COLUMNS EXCEPT ()))")]
    [InlineData("CREATE PROPERTY GRAPH g VERTEX TABLES (person) OPTIONS (x)")]
    public void PropertyGraph_OracleRejectedForms(string sql) => Assert.False(SqlParser.Parse(sql).Success);
}
