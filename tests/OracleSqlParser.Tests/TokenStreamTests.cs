using System.Reflection;
using System.Text;
using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

public class TokenStreamTests
{
    private static readonly string[] ExtraInputs =
    [
        "SELECT /* c1 */ a -- c2\nFROM t /* tail */",
        "-- only a comment",
        "",
        "SELECT 1 /*+ FULL(t) */ FROM dual -- trailing\n",
        "REM a remark line\nSELECT 1 FROM dual\nPROMPT  hello there\n",
        "SELECT 'unterminated\nFROM t",
        "SELECT \"unterminated\nFROM t",
        "SELECT 1 /* unterminated comment",
        "CREATE JAVA SOURCE NAMED \"H\" AS\n  public class H { }\n/\n",
        "SELECT\tq'[it's]' , n'x' ,  &&sub.  FROM t\r\n",
    ];

    public static IEnumerable<object[]> Inputs()
    {
        var corpus = typeof(SmokeCorpusTests).GetMethod("Parses")!.GetCustomAttributes<InlineDataAttribute>()
            .SelectMany(a => a.GetData(null!))
            .Select(d => (string)d[0]);
        return corpus.Concat(ExtraInputs).Select(s => new object[] { s });
    }

    /// <summary>Runs the lexer to end of input without throwing on lexical errors.</summary>
    private static List<Token> Lex(string sql)
    {
        var lx = new Lexer(sql);
        var list = new List<Token>();
        while (true)
        {
            var t = lx.Next();
            list.Add(t);
            if (t.Type == TokenType.EOF) return list;
        }
    }

    [Theory]
    [MemberData(nameof(Inputs))]
    public void TokenStream_ReproducesSourceExactly(string sql)
    {
        var tokens = Lex(sql);
        var sb = new StringBuilder();
        foreach (var t in tokens)
        {
            foreach (var tr in t.LeadingTrivia)
            {
                Assert.Equal(sql.Substring(tr.Start, tr.End - tr.Start), tr.Text);
                sb.Append(tr.Text);
            }
            sb.Append(sql, t.Start, t.End - t.Start);
        }
        Assert.Equal(sql, sb.ToString());
    }

    [Fact]
    public void Trivia_CapturesCommentsAndWhitespace()
    {
        var tokens = Lex("SELECT /* c1 */ a -- c2\nFROM t /* tail */");

        var from = tokens.Single(t => t.Text == "FROM");
        Assert.Equal(
            [(TriviaKind.Whitespace, " "), (TriviaKind.LineComment, "-- c2"), (TriviaKind.Whitespace, "\n")],
            from.LeadingTrivia.Select(tr => (tr.Kind, tr.Text)));

        var select = tokens[0];
        Assert.Equal("SELECT", select.Text);
        Assert.Empty(select.LeadingTrivia);

        var eof = tokens[^1];
        Assert.Equal(TokenType.EOF, eof.Type);
        Assert.Equal(
            [(TriviaKind.Whitespace, " "), (TriviaKind.BlockComment, "/* tail */")],
            eof.LeadingTrivia.Select(tr => (tr.Kind, tr.Text)));
    }

    [Fact]
    public void ParseResult_Tokens_IsTheCompleteUnfilteredStream()
    {
        // The hint after the Number is not in a position the parser uses, so the parser's internal list drops it.
        const string sql = "SELECT 1 /*+ FULL(t) */ FROM dual -- tail";
        var r = SqlParser.Parse(sql);

        Assert.Equal(Lex(sql).Select(t => (t.Type, t.Start, t.End)), r.Tokens.Select(t => (t.Type, t.Start, t.End)));
        Assert.Contains(r.Tokens, t => t.Type == TokenType.Hint && t.Text == "/*+ FULL(t) */");
        Assert.Equal(TokenType.EOF, r.Tokens[^1].Type);
        for (int i = 0; i < r.Tokens.Count; i++)
            Assert.Equal(i, r.Tokens[i].TokenIndex);
    }

    [Theory]
    [MemberData(nameof(Inputs))]
    public void ParseResult_Tokens_ReproducesSourceExactly(string sql)
    {
        var r = SqlParser.Parse(sql);
        var sb = new StringBuilder();
        foreach (var t in r.Tokens)
        {
            foreach (var tr in t.LeadingTrivia) sb.Append(tr.Text);
            sb.Append(sql, t.Start, t.End - t.Start);
        }
        Assert.Equal(sql, sb.ToString());
    }

    [Fact]
    public void Trivia_OnlyCommentKeptForEmptyInput()
    {
        var tokens = Lex("-- only a comment");
        Assert.Single(tokens);
        Assert.Equal(TokenType.EOF, tokens[0].Type);
        Assert.Equal([(TriviaKind.LineComment, "-- only a comment")], tokens[0].LeadingTrivia.Select(tr => (tr.Kind, tr.Text)));
    }
}
