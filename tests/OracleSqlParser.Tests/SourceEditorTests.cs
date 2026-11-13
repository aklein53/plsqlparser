using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

public class SourceEditorTests
{
    [Fact]
    public void ParseResult_Source_IsTheExactInputText()
    {
        const string sql = "SELECT /* keep */ a  ,  b\r\nFROM   orders o -- trailing\n\nWHERE x = 1;";
        var result = SqlParser.Parse(sql);
        Assert.Equal(sql, result.Source);
    }
}
