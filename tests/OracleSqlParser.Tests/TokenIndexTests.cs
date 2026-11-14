using System.Reflection;
using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

public class TokenIndexTests
{
    public static IEnumerable<object[]> Corpus() =>
        typeof(SmokeCorpusTests).GetMethod("Parses")!.GetCustomAttributes<InlineDataAttribute>()
            .SelectMany(a => a.GetData(null!))
            .Select(d => new object[] { (string)d[0] });

    [Theory]
    [MemberData(nameof(Corpus))]
    public void EveryNode_TokenIndexesMatchItsSpan(string sql)
    {
        var r = SqlParser.Parse(sql);
        Assert.True(r.Success, string.Join("; ", r.Errors));

        int checkedNodes = 0;
        foreach (var node in r.Script.Descendants())
        {
            if (node.FirstTokenIndex < 0) continue;
            checkedNodes++;
            Assert.True(node.LastTokenIndex >= node.FirstTokenIndex, $"{node.GetType().Name} at {node.Start}");
            Assert.Equal(node.Start, r.Tokens[node.FirstTokenIndex].Start);
            Assert.Equal(node.End, r.Tokens[node.LastTokenIndex].End);
        }
        Assert.True(checkedNodes > 0);
    }

    [Fact]
    public void NodeBuiltInCode_HasNoTokenIndexes()
    {
        var select = new SelectStatement { Body = new QuerySpecification() };
        Assert.Equal(-1, select.FirstTokenIndex);
        Assert.Equal(-1, select.LastTokenIndex);
    }

    [Fact]
    public void ParsedNode_CoversItsTokens()
    {
        var r = SqlParser.Parse("SELECT a FROM t WHERE b = 1");
        var stmt = Assert.IsType<SelectStatement>(r.Script.Statements[0]);
        Assert.Equal("SELECT", r.Tokens[stmt.FirstTokenIndex].Text);
        Assert.Equal("1", r.Tokens[stmt.LastTokenIndex].Text);
    }
}
