using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

/// <summary>Helpers that parse SQL (asserting success) and print the resulting tree for shape assertions.</summary>
public static class Shape
{
    public static SqlScript Parse(string sql)
    {
        var r = SqlParser.Parse(sql);
        Assert.True(r.Success, string.Join("; ", r.Errors));
        return r.Script;
    }

    /// <summary>The whole script: statements joined by " ; ".</summary>
    public static string Of(string sql) => string.Join(" ; ", Parse(sql).Statements.Select(s => AstPrinter.Print(s)));

    /// <summary>The first node of type T (pre-order) found in the script.</summary>
    public static string Of<T>(string sql) where T : SqlNode
    {
        var node = Parse(sql).Descendants().OfType<T>().FirstOrDefault();
        Assert.True(node != null, $"No {typeof(T).Name} found in: {sql}");
        return AstPrinter.Print(node);
    }

    public static string Expr(string sql) => AstPrinter.Print(SqlParser.ParseExpression(sql));
}
