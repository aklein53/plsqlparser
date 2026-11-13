using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

public class TreePlumbingTests
{
    private static string Gen(SqlNode n) => new SqlScriptGenerator().Generate(n).TrimEnd(';');

    // ---------- visitor ----------

    private sealed class TableCollector : SqlNodeVisitor
    {
        public List<string> Names { get; } = [];
        public override void Visit(NamedTable node) => Names.Add(string.Join(".", node.Name.Select(i => i.Value)));
    }

    [Fact]
    public void Visitor_TypedVisit_CollectsTablesInDocumentOrder()
    {
        var script = Shape.Parse("SELECT * FROM a JOIN s.b ON a.x = b.x WHERE EXISTS (SELECT 1 FROM c) UNION SELECT 1 FROM d");
        var v = new TableCollector();
        script.Accept(v);
        Assert.Equal(["A", "S.B", "C", "D"], v.Names);
    }

    private sealed class SkipSubqueries : SqlNodeVisitor
    {
        public int Tables;
        public override void Visit(NamedTable node) => Tables++;
        public override void ExplicitVisit(SelectStatement node)
        {
            Visit(node);
            if (!ReferenceEquals(node, Root)) return; // do not descend into nested selects
            node.AcceptChildren(this);
        }
        public SelectStatement? Root;
    }

    [Fact]
    public void Visitor_ExplicitVisit_CanControlDescent()
    {
        var script = Shape.Parse("SELECT * FROM a WHERE EXISTS (SELECT 1 FROM b)");
        var v = new SkipSubqueries { Root = (SelectStatement)script.Statements[0] };
        script.Accept(v);
        Assert.Equal(1, v.Tables);
    }

    private sealed class Counter : SqlNodeVisitor
    {
        public int Count;
        public override void Visit(SqlNode node) => Count++;
    }

    [Fact]
    public void Visitor_FallbackVisit_SeesEveryNode_AndMatchesDescendants()
    {
        foreach (var sql in GeneratedCodeTests.Samples)
        {
            var script = Shape.Parse(sql);
            var c = new Counter();
            script.Accept(c);
            Assert.Equal(script.Descendants().Count(), c.Count);
        }
    }

    [Fact]
    public void Visitor_On2000TermChain_Works()
    {
        var script = Shape.Parse(GeneratedCodeTests.AndChain(2_000));
        var c = new Counter();
        script.Accept(c);
        Assert.Equal(script.Descendants().Count(), c.Count);
    }

    [Fact]
    public void LegacySqlVisitor_StillWorks()
    {
        var script = Shape.Parse("SELECT a FROM t");
        var seen = new List<string>();
        new LegacyCollector(seen).Visit(script);
        Assert.Contains("NamedTable", seen);
    }

    private sealed class LegacyCollector(List<string> seen) : SqlVisitor
    {
        public override void Visit(SqlNode node) { seen.Add(node.GetType().Name); base.Visit(node); }
    }

    // ---------- rewriter ----------

    private sealed class TableToSubquery : SqlNodeRewriter
    {
        public override SqlNode? Rewrite(NamedTable node) =>
            new SubqueryTable
            {
                Alias = node.Alias ?? new Identifier { Value = "T", Text = "T" },
                Query = (SelectStatement)Shape.Parse("SELECT * FROM " + node.Name[0].Value).Statements[0],
            };
    }

    [Fact]
    public void Rewriter_ReplacesNamedTableWithSubquery()
    {
        var script = Shape.Parse("SELECT * FROM a x JOIN b ON x.i = b.i");
        var result = new TableToSubquery().RewriteTree(script);
        Assert.Same(script, result);
        var sql = Gen(script);
        Assert.Equal(0, script.Descendants().OfType<SubqueryTable>().Count(s => s.Query.Body is QuerySpecification q && q.From[0] is SubqueryTable));
        Assert.Contains("(SELECT * FROM A) x", sql);
        Assert.Contains("(SELECT * FROM B) T", sql);
        Assert.True(SqlParser.Parse(sql).Success);
    }

    private sealed class AndWhere(string predicate) : SqlNodeRewriter
    {
        public override SqlNode? Rewrite(QuerySpecification node)
        {
            base.Rewrite(node); // rewrite nested subqueries first
            var p = SqlParser.ParseExpression(predicate);
            node.Where = node.Where is null ? p : new BinaryExpression { Left = node.Where, Operator = "AND", Right = p };
            return node;
        }
    }

    [Fact]
    public void Rewriter_AndsPredicateOntoEveryWhere()
    {
        var script = Shape.Parse("SELECT a FROM t WHERE x = 1 OR y = 2 UNION ALL SELECT b FROM (SELECT c FROM u) v");
        new AndWhere("tenant = 7").RewriteTree(script);
        Assert.Equal(
            "SELECT a FROM t WHERE (x = 1 OR y = 2) AND tenant = 7 UNION ALL SELECT b FROM (SELECT c FROM u WHERE tenant = 7) v WHERE tenant = 7",
            Gen(script));
    }

    private sealed class DropTable(string name) : SqlNodeRewriter
    {
        public override SqlNode? Rewrite(NamedTable node) => node.Name[0].Value == name ? null : node;
    }

    [Fact]
    public void Rewriter_NullRemovesFromList()
    {
        var script = Shape.Parse("SELECT * FROM a, b, c");
        new DropTable("B").RewriteTree(script);
        Assert.Equal("SELECT * FROM a, c", Gen(script));
    }

    private sealed class DropWhere : SqlNodeRewriter
    {
        public override SqlNode? Rewrite(BinaryExpression node) => null;
    }

    [Fact]
    public void Rewriter_NullNullsOptionalProperty()
    {
        var script = Shape.Parse("SELECT * FROM a WHERE x = 1");
        new DropWhere().RewriteTree(script);
        Assert.Equal("SELECT * FROM a", Gen(script));
    }

    private sealed class DropRequired : SqlNodeRewriter
    {
        public override SqlNode? Rewrite(Literal node) => null;
    }

    [Fact]
    public void Rewriter_NullForRequiredProperty_Throws()
    {
        var script = Shape.Parse("SELECT * FROM a WHERE x = 1");
        var ex = Assert.Throws<InvalidOperationException>(() => new DropRequired().RewriteTree(script));
        Assert.Contains("BinaryExpression.Right", ex.Message);
    }

    private sealed class WrongType : SqlNodeRewriter
    {
        public override SqlNode? Rewrite(NamedTable node) => new Literal { Kind = LiteralKind.Null };
    }

    [Fact]
    public void Rewriter_WrongType_ThrowsNamingTheProperty()
    {
        var script = Shape.Parse("SELECT * FROM a");
        var ex = Assert.Throws<InvalidOperationException>(() => new WrongType().RewriteTree(script));
        Assert.Contains("QuerySpecification.From", ex.Message);
        Assert.Contains("Literal", ex.Message);
    }

    [Fact]
    public void Rewriter_Identity_LeavesTreeUnchanged()
    {
        foreach (var sql in GeneratedCodeTests.Samples)
        {
            var script = Shape.Parse(sql);
            var before = AstPrinter.Print(script);
            new IdentityRewriter().RewriteTree(script);
            Assert.Equal(before, AstPrinter.Print(script));
        }
    }

    private sealed class IdentityRewriter : SqlNodeRewriter { }

    // ---------- DeepClone ----------

    [Fact]
    public void DeepClone_IsIndependentCopy()
    {
        foreach (var sql in GeneratedCodeTests.Samples)
        {
            var original = Shape.Parse(sql);
            var json = AstJson.Serialize(original, indented: false);
            var clone = original.DeepClone<SqlScript>();
            Assert.NotSame(original, clone);
            Assert.Equal(json, AstJson.Serialize(clone, indented: false)); // includes spans
            Assert.Empty(original.Descendants().Intersect(clone.Descendants()));
        }
    }

    [Fact]
    public void DeepClone_MutatingClone_LeavesOriginalUnchanged()
    {
        var original = Shape.Parse("SELECT a FROM t WHERE x = 1");
        var before = Gen(original);
        var clone = original.DeepClone<SqlScript>();
        new TableToSubquery().RewriteTree(clone);
        clone.Descendants().OfType<Identifier>().First().Value = "ZZZ";
        Assert.Equal(before, Gen(original));
        Assert.NotEqual(before, Gen(clone));
    }

    [Fact]
    public void DeepClone_CopiesSpellingOnlyText()
    {
        var script = Shape.Parse("SELECT MixedCase FROM t");
        var id = script.DeepClone<SqlScript>().Descendants().OfType<Identifier>().First();
        Assert.Equal("MixedCase", id.Text);
        Assert.Equal("MIXEDCASE", id.Value);
    }

    // ---------- StructurallyEqual ----------

    [Fact]
    public void StructurallyEqual_SameTextParsedTwice()
    {
        foreach (var sql in GeneratedCodeTests.Samples)
            Assert.True(SqlNodeComparer.StructurallyEqual(Shape.Parse(sql), Shape.Parse(sql)));
    }

    [Fact]
    public void StructurallyEqual_IgnoresSpansAndWhitespace()
    {
        Assert.True(SqlNodeComparer.StructurallyEqual(Shape.Parse("SELECT a FROM t"), Shape.Parse("SELECT   a\n  FROM t")));
    }

    [Fact]
    public void StructurallyEqual_DifferentAlias_NotEqual()
    {
        Assert.False(SqlNodeComparer.StructurallyEqual(Shape.Parse("SELECT a AS x FROM t"), Shape.Parse("SELECT a AS y FROM t")));
    }

    [Fact]
    public void StructurallyEqual_UnquotedIdentifierCase_Equal_QuotedCase_NotEqual()
    {
        Assert.True(SqlNodeComparer.StructurallyEqual(Shape.Parse("select Foo from Bar"), Shape.Parse("SELECT FOO FROM BAR")));
        Assert.False(SqlNodeComparer.StructurallyEqual(Shape.Parse("select \"Foo\" from t"), Shape.Parse("select \"FOO\" from t")));
    }

    [Fact]
    public void StructurallyEqual_DifferentShapes_NotEqual()
    {
        Assert.False(SqlNodeComparer.StructurallyEqual(Shape.Parse("SELECT a FROM t"), Shape.Parse("SELECT a FROM t WHERE 1 = 1")));
        Assert.False(SqlNodeComparer.StructurallyEqual(Shape.Parse("SELECT 1 FROM t"), Shape.Parse("SELECT 2 FROM t")));
        Assert.False(SqlNodeComparer.StructurallyEqual(Shape.Parse("SELECT a FROM t"), Shape.Parse("DELETE FROM t")));
        Assert.False(SqlNodeComparer.StructurallyEqual(Shape.Parse("SELECT a AND b OR c FROM t"), Shape.Parse("SELECT a AND b AND c FROM t")));
    }

    [Fact]
    public void StructurallyEqual_QuoteDelimiterIsSpellingOnly()
    {
        Assert.True(SqlNodeComparer.StructurallyEqual(Shape.Parse("SELECT q'[abc]' FROM t"), Shape.Parse("SELECT 'abc' FROM t")));
    }

    [Fact]
    public void StructurallyEqual_DeepChain_IsIterative()
    {
        var a = Shape.Parse(GeneratedCodeTests.AndChain(20_000));
        var b = Shape.Parse(GeneratedCodeTests.AndChain(20_000));
        Assert.True(SqlNodeComparer.StructurallyEqual(a, b));
        var c = Shape.Parse(GeneratedCodeTests.AndChain(19_999));
        Assert.False(SqlNodeComparer.StructurallyEqual(a, c));
    }

    [Fact]
    public void StructurallyEqual_ClonesAreEqual()
    {
        foreach (var sql in GeneratedCodeTests.Samples)
        {
            var s = Shape.Parse(sql);
            Assert.True(SqlNodeComparer.StructurallyEqual(s, s.DeepClone<SqlScript>()));
        }
    }

    // ---------- spans and results ----------

    [Fact]
    public void NodesBuiltInCode_HaveZeroSpans()
    {
        var id = new Identifier { Value = "X" };
        Assert.Equal((0, 0, 0, 0), (id.Start, id.End, id.Line, id.Column));
        var parsed = Shape.Parse("SELECT a FROM t").Descendants().OfType<Identifier>().First();
        Assert.True(parsed.Line >= 1);
        Assert.True(typeof(SqlNode).GetProperty("Start")!.SetMethod!.IsAssembly);
    }

    [Fact]
    public void ParseResult_ErrorsIsReadOnly()
    {
        Assert.Equal(typeof(IReadOnlyList<ParseError>), typeof(ParseResult).GetProperty("Errors")!.PropertyType);
    }
}
