using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

public class SourceEditorTests
{
    private const string Query = "SELECT /* keep */ a  ,  b\nFROM   orders o -- trailing\nWHERE x = 1";

    private static T Find<T>(ParseResult r) where T : SqlNode => r.Script.Descendants().OfType<T>().First();

    /// <summary>The edited text must parse without errors; returns it for further asserts.</summary>
    private static string Reparsed(string text)
    {
        var r = SqlParser.Parse(text);
        Assert.True(r.Success, $"edited SQL does not parse: {text}\n{string.Join("; ", r.Errors)}");
        return text;
    }

    [Fact]
    public void ParseResult_Source_IsTheExactInputText()
    {
        const string sql = "SELECT /* keep */ a  ,  b\r\nFROM   orders o -- trailing\n\nWHERE x = 1;";
        var result = SqlParser.Parse(sql);
        Assert.Equal(sql, result.Source);
    }

    // ---------- Replace ----------

    [Fact]
    public void Replace_TableName_KeepsCommentsSpacingAndLayout()
    {
        var r = SqlParser.Parse(Query);
        var editor = new SourceEditor(r);
        editor.Replace(Find<NamedTable>(r).Name[0], "employees");

        Assert.Equal("SELECT /* keep */ a  ,  b\nFROM   employees o -- trailing\nWHERE x = 1", Reparsed(editor.Apply()));
    }

    [Fact]
    public void Replace_WithNode_PrintsTheReplacement()
    {
        var r = SqlParser.Parse(Query);
        var editor = new SourceEditor(r);
        editor.Replace(Find<NamedTable>(r).Name[0], new Identifier { Value = "employees" });

        Assert.Equal("SELECT /* keep */ a  ,  b\nFROM   employees o -- trailing\nWHERE x = 1", Reparsed(editor.Apply()));
    }

    [Fact]
    public void Replace_WithNode_UsesTheEditorOptions()
    {
        var r = SqlParser.Parse("SELECT a FROM t WHERE x = 1 -- keep");
        var editor = new SourceEditor(r, new SqlScriptGeneratorOptions { KeywordCasing = KeywordCasing.Lowercase });
        editor.Replace(Find<BinaryExpression>(r), SqlParser.ParseExpression("y IS NOT NULL OR z = 2"));

        Assert.Equal("SELECT a FROM t WHERE y is not null or z = 2 -- keep", Reparsed(editor.Apply()));
    }

    // ---------- Remove ----------

    [Fact]
    public void Remove_FirstSelectItem_RemovesItsCommaToo()
    {
        var r = SqlParser.Parse(Query);
        var editor = new SourceEditor(r);
        editor.Remove(Find<QuerySpecification>(r).SelectList[0]);

        Assert.Equal("SELECT /* keep */ b\nFROM   orders o -- trailing\nWHERE x = 1", Reparsed(editor.Apply()));
    }

    [Fact]
    public void Remove_LastSelectItem_RemovesItsCommaToo()
    {
        var r = SqlParser.Parse(Query);
        var editor = new SourceEditor(r);
        editor.Remove(Find<QuerySpecification>(r).SelectList[1]);

        Assert.Equal("SELECT /* keep */ a\nFROM   orders o -- trailing\nWHERE x = 1", Reparsed(editor.Apply()));
    }

    [Fact]
    public void Remove_MiddleItem_RemovesItsFollowingComma()
    {
        var r = SqlParser.Parse("SELECT a, b, c FROM t");
        var editor = new SourceEditor(r);
        editor.Remove(Find<QuerySpecification>(r).SelectList[1]);

        Assert.Equal("SELECT a, c FROM t", Reparsed(editor.Apply()));
    }

    [Fact]
    public void Remove_FirstItem_KeepsACommentBeforeTheNextItem()
    {
        var r = SqlParser.Parse("SELECT a, /* about b */ b FROM t");
        var editor = new SourceEditor(r);
        editor.Remove(Find<QuerySpecification>(r).SelectList[0]);

        Assert.Equal("SELECT /* about b */ b FROM t", Reparsed(editor.Apply()));
    }

    // ---------- Insert ----------

    private const string Script = "-- header\nSELECT 1 FROM dual;\n\nSELECT 2 FROM dual; -- tail\n";

    [Fact]
    public void InsertBefore_Statement_KeepsTheCommentAboveAndTheLayout()
    {
        var r = SqlParser.Parse(Script);
        var editor = new SourceEditor(r);
        editor.InsertBefore(r.Script.Statements[1], "-- added\n");

        Assert.Equal("-- header\nSELECT 1 FROM dual;\n\n-- added\nSELECT 2 FROM dual; -- tail\n", Reparsed(editor.Apply()));
    }

    [Fact]
    public void InsertAfter_Statement_GoesBeforeItsTerminator()
    {
        var r = SqlParser.Parse(Script);
        var editor = new SourceEditor(r);
        // A statement's span excludes its ';', so the text is placed before it. Include the terminator to add a statement.
        editor.InsertAfter(r.Script.Statements[0], ";\nSELECT 9 FROM dual");

        Assert.Equal("-- header\nSELECT 1 FROM dual;\nSELECT 9 FROM dual;\n\nSELECT 2 FROM dual; -- tail\n", Reparsed(editor.Apply()));
    }

    [Fact]
    public void InsertBefore_TwoInsertsAtOneOffset_KeepTheOrderTheyWereAdded()
    {
        var r = SqlParser.Parse("SELECT a FROM t");
        var editor = new SourceEditor(r);
        var q = Find<QuerySpecification>(r);
        editor.InsertBefore(q, "/*1*/ ");
        editor.InsertBefore(q, "/*2*/ ");

        Assert.Equal("/*1*/ /*2*/ SELECT a FROM t", Reparsed(editor.Apply()));
    }

    [Fact]
    public void InsertBefore_AndReplaceOfTheSameNode_PutsTheInsertBeforeTheReplacement()
    {
        var r = SqlParser.Parse("SELECT a FROM t");
        var editor = new SourceEditor(r);
        var item = Find<QuerySpecification>(r).SelectList[0];
        editor.Replace(item, "b");
        editor.InsertBefore(item, "DISTINCT ");

        Assert.Equal("SELECT DISTINCT b FROM t", Reparsed(editor.Apply()));
    }

    [Fact]
    public void Apply_WithoutEdits_ReturnsTheSourceUnchanged()
    {
        var r = SqlParser.Parse(Query);
        Assert.Equal(Query, new SourceEditor(r).Apply());
    }

    [Fact]
    public void Apply_CanBeCalledAgain_AfterMoreEdits()
    {
        var r = SqlParser.Parse("SELECT a FROM t");
        var editor = new SourceEditor(r);
        editor.Replace(Find<NamedTable>(r).Name[0], "u");
        Assert.Equal("SELECT a FROM u", editor.Apply());
        editor.InsertAfter(Find<NamedTable>(r), " -- new");
        Assert.Equal("SELECT a FROM u -- new", editor.Apply());
    }

    // ---------- Validation ----------

    [Fact]
    public void Replace_NodeWithoutPosition_Throws()
    {
        var r = SqlParser.Parse("SELECT a FROM t");
        var editor = new SourceEditor(r);
        var built = new Identifier { Value = "x" };

        var ex = Assert.Throws<InvalidOperationException>(() => editor.Replace(built, "y"));
        Assert.Contains("no source position", ex.Message);
    }

    [Fact]
    public void Remove_NodeFromParseStatement_Throws()
    {
        var r = SqlParser.Parse("SELECT a FROM t");
        var editor = new SourceEditor(r);
        var item = SqlParser.ParseStatement("SELECT b FROM u").Descendants().OfType<SelectItem>().First();

        var ex = Assert.Throws<InvalidOperationException>(() => editor.Remove(item));
        Assert.Contains("no source position", ex.Message);
    }

    [Fact]
    public void Node_FromAnotherParseResult_Throws()
    {
        var first = SqlParser.Parse("SELECT a FROM t");
        var second = SqlParser.Parse("SELECT abc FROM elsewhere");
        var editor = new SourceEditor(first);

        var ex = Assert.Throws<InvalidOperationException>(() => editor.Replace(Find<NamedTable>(second), "x"));
        Assert.Contains("does not belong to this ParseResult", ex.Message);
    }

    [Fact]
    public void Replace_TwiceTheSameNode_Throws()
    {
        var r = SqlParser.Parse("SELECT a FROM t");
        var editor = new SourceEditor(r);
        var table = Find<NamedTable>(r);
        editor.Replace(table, "u");

        Assert.Throws<InvalidOperationException>(() => editor.Replace(table, "v"));
    }

    [Fact]
    public void Replace_NodeInsideAReplacedNode_Throws()
    {
        var r = SqlParser.Parse("SELECT a FROM t");
        var editor = new SourceEditor(r);
        var q = Find<QuerySpecification>(r);
        editor.Replace(q, "SELECT 1 FROM dual");

        Assert.Throws<InvalidOperationException>(() => editor.Remove(q.SelectList[0]));
    }

    [Fact]
    public void Remove_NodeContainingAnEarlierRemoval_Throws()
    {
        var r = SqlParser.Parse("SELECT a, b FROM t");
        var editor = new SourceEditor(r);
        editor.Remove(Find<QuerySpecification>(r).SelectList[1]);

        Assert.Throws<InvalidOperationException>(() => editor.Remove(Find<QuerySpecification>(r)));
    }

    [Fact]
    public void InsertInsideAReplacedRange_Throws()
    {
        var r = SqlParser.Parse("SELECT a FROM t");
        var editor = new SourceEditor(r);
        editor.Replace(Find<QuerySpecification>(r), "SELECT 1 FROM dual");

        Assert.Throws<InvalidOperationException>(() => editor.InsertBefore(Find<NamedTable>(r), "/*x*/ "));
    }

    [Fact]
    public void InsertAtTheBoundaryOfAReplacedRange_IsAllowed()
    {
        var r = SqlParser.Parse("SELECT a FROM t");
        var editor = new SourceEditor(r);
        var item = Find<QuerySpecification>(r).SelectList[0];
        editor.Replace(item, "b");
        editor.InsertAfter(item, " /* after */");

        Assert.Equal("SELECT b /* after */ FROM t", Reparsed(editor.Apply()));
    }

    [Fact]
    public void Replacements_OfDifferentItems_DoNotOverlap()
    {
        var r = SqlParser.Parse("SELECT a, b FROM t");
        var editor = new SourceEditor(r);
        var items = Find<QuerySpecification>(r).SelectList;
        editor.Replace(items[0], "x");
        editor.Replace(items[1], "y");

        Assert.Equal("SELECT x, y FROM t", Reparsed(editor.Apply()));
    }
}
