using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

/// <summary>ANNOTATIONS placements that Oracle 23ai accepts (checked against Oracle; see AnnotationsAcceptanceTests) and forms it rejects.</summary>
public class AnnotationsShapeTests
{
    [Theory]
    [InlineData("CREATE TABLE t1 (id NUMBER, name VARCHAR2(20)) ANNOTATIONS (label 'Tbl', hidden)")]
    [InlineData("CREATE TABLE t2 (id NUMBER ANNOTATIONS (label 'Col'), name VARCHAR2(20)) ANNOTATIONS (label 'Tbl2')")]
    [InlineData("CREATE TABLE t3 (id NUMBER, name VARCHAR2(20)) ANNOTATIONS (ADD IF NOT EXISTS label 'x')")]
    [InlineData("CREATE TABLE t4 (id NUMBER ANNOTATIONS (ADD label 'a', ui 'b'), name VARCHAR2(20))")]
    [InlineData("CREATE TABLE t5 (id NUMBER GENERATED ALWAYS AS IDENTITY ANNOTATIONS (label 'g'))")]
    [InlineData("CREATE TABLE t6 (id NUMBER DEFAULT 1 NOT NULL ANNOTATIONS (label 'a'), name VARCHAR2(20))")]
    [InlineData("CREATE TABLE t7 (id NUMBER PRIMARY KEY ANNOTATIONS (label 'a'), name VARCHAR2(20))")]
    [InlineData("CREATE TABLE t8 (id NUMBER, name VARCHAR2(20)) TABLESPACE users ANNOTATIONS (label 'a')")]
    [InlineData("CREATE TABLE t9 (id NUMBER, CONSTRAINT pk9 PRIMARY KEY (id)) ANNOTATIONS (label 'a')")]
    [InlineData("ALTER TABLE t1 ANNOTATIONS (label 'Tbl1 altered', ADD ui 'v', DROP IF EXISTS hidden)")]
    [InlineData("ALTER TABLE t1 ADD (extra NUMBER ANNOTATIONS (label 'e'))")]
    [InlineData("ALTER TABLE t1 MODIFY (name VARCHAR2(30) ANNOTATIONS (label 'm'))")]
    [InlineData("ALTER TABLE t1 MODIFY name ANNOTATIONS (ADD label 'm2')")]
    [InlineData("ALTER TABLE t1 MODIFY (name ANNOTATIONS (DROP label))")]
    [InlineData("CREATE VIEW v1 ANNOTATIONS (label 'View') AS SELECT id, name FROM t1")]
    [InlineData("CREATE VIEW v2 (id ANNOTATIONS (label 'vid'), name) AS SELECT id, name FROM t1")]
    [InlineData("CREATE OR REPLACE VIEW v4 ANNOTATIONS (ADD label 'v4') AS SELECT id FROM t1")]
    [InlineData("CREATE MATERIALIZED VIEW m1 ANNOTATIONS (label 'MV') AS SELECT id, name FROM t1")]
    [InlineData("CREATE MATERIALIZED VIEW m3 (id ANNOTATIONS (label 'mid'), name ANNOTATIONS (ADD label 'mn')) ANNOTATIONS (ADD ui 'mv') AS SELECT id, name FROM t1")]
    [InlineData("CREATE INDEX i1 ON t1 (id) ANNOTATIONS (label 'Idx')")]
    [InlineData("CREATE INDEX i6 ON t1 (id, name) ANNOTATIONS (label 'a') TABLESPACE users")]
    [InlineData("ALTER VIEW v1 ANNOTATIONS (label 'View altered')")]
    [InlineData("ALTER VIEW v2 MODIFY (id ANNOTATIONS (label 'vid2'))")]
    [InlineData("ALTER MATERIALIZED VIEW m1 ANNOTATIONS (ADD ui 'x', DROP IF EXISTS label)")]
    [InlineData("ALTER MATERIALIZED VIEW m2 MODIFY id ANNOTATIONS (label 'mid3')")]
    [InlineData("ALTER INDEX i1 ANNOTATIONS (label 'Idx altered')")]
    [InlineData("ALTER INDEX i6 ANNOTATIONS (DROP IF EXISTS nothere)")]
    public void Annotations_Accepted(string sql) => Assert.True(SqlParser.Parse(sql).Success, string.Join("; ", SqlParser.Parse(sql).Errors));

    [Theory]
    [InlineData("CREATE TABLE t (id NUMBER ANNOTATIONS (label 'a') NOT NULL)")]
    [InlineData("CREATE TABLE t (id NUMBER ANNOTATIONS (label 'a') DEFAULT 1)")]
    [InlineData("CREATE TABLE t (id NUMBER ANNOTATIONS (label 'a') PRIMARY KEY)")]
    [InlineData("CREATE TABLE t (id NUMBER ANNOTATIONS (label 'a') PRIMARY KEY, n NUMBER)")]
    [InlineData("CREATE TABLE t (id NUMBER(5) ANNOTATIONS (label 'a'), name VARCHAR2(20) ANNOTATIONS (label 'b') NOT NULL)")]
    [InlineData("CREATE TABLE t (id NUMBER, name VARCHAR2(20)) ANNOTATIONS (DROP label)")]
    [InlineData("CREATE TABLE t (id NUMBER ANNOTATIONS (DROP label))")]
    [InlineData("CREATE TABLE t (id NUMBER, name VARCHAR2(20)) ANNOTATIONS ()")]
    [InlineData("CREATE TABLE t (id NUMBER, name VARCHAR2(20)) ANNOTATIONS (label 'a', label 'a')")]
    [InlineData("CREATE TABLE t AS SELECT 1 AS id FROM dual ANNOTATIONS (label 'a')")]
    [InlineData("ALTER TABLE t1 MODIFY (name ANNOTATIONS (ADD label 'z') VARCHAR2(30))")]
    [InlineData("ALTER TABLE t1 ADD ANNOTATIONS (label 'nope')")]
    [InlineData("CREATE VIEW v6 AS SELECT id, name FROM t1 ANNOTATIONS (label 'x')")]
    [InlineData("CREATE VIEW v7 ANNOTATIONS (DROP label) AS SELECT id FROM t1")]
    [InlineData("CREATE VIEW v8 (id ANNOTATIONS (DROP label)) AS SELECT id FROM t1")]
    [InlineData("CREATE INDEX i4 ON t1 (id) ANNOTATIONS (DROP label)")]
    [InlineData("ALTER VIEW v6 MODIFY id ANNOTATIONS (label 'x')")]
    public void Annotations_Rejected(string sql) => Assert.False(SqlParser.Parse(sql).Success);

    [Fact]
    public void ColumnAnnotations_AreTyped()
    {
        var col = Shape.Parse("CREATE TABLE t (id NUMBER NOT NULL ANNOTATIONS (label 'a'))").Descendants().OfType<ColumnDefinition>().Single();
        Assert.NotNull(col.Annotations);
        Assert.Equal("LABEL", col.Annotations!.Items.Single().Name.Value);
    }

    [Fact]
    public void TableAnnotations_AreAttributes() =>
        Assert.Single(Shape.Parse("CREATE TABLE t (id NUMBER) ANNOTATIONS (label 'a')").Descendants().OfType<AnnotationsAttribute>());

    [Fact]
    public void AlterViewAnnotations_AreTyped() =>
        Assert.IsType<AlterAnnotationsStatement>(Shape.Parse("ALTER VIEW v1 ANNOTATIONS (label 'x')").Statements[0]);
}
