using System.Text.RegularExpressions;
using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

/// <summary>ParseError model (ROADMAP Epic 5): stable codes, span Length, Expected lists and the caret formatter.</summary>
public class ParseErrorTests
{
    private static ParseError OnlyError(string sql, ParseOptions? options = null)
    {
        var r = SqlParser.Parse(sql, options);
        return Assert.Single(r.Errors);
    }

    // ---------- codes ----------

    [Theory]
    [InlineData("SELECT 1 FROM dual ) ", ParseErrorCodes.UnexpectedToken)]
    [InlineData("SELECT (1 FROM dual", ParseErrorCodes.ExpectedToken)]
    [InlineData("SELECT 'abc FROM dual", ParseErrorCodes.UnterminatedLiteral)]
    [InlineData("SELECT 2 ** 3 FROM dual", ParseErrorCodes.InvalidCombination)]
    [InlineData("COMMENT ON FOO x IS 'y'", ParseErrorCodes.UnsupportedForm)]
    public void Code_IsSetForEachSyntaxClass(string sql, string code)
    {
        Assert.Equal(code, OnlyError(sql).Code);
    }

    [Fact]
    public void Code_VersionGated()
    {
        var e = OnlyError("SELECT 1", new ParseOptions { OracleVersion = OracleVersion.Oracle19 });
        Assert.Equal(ParseErrorCodes.VersionGated, e.Code);
    }

    [Fact]
    public void Code_NestingTooDeep()
    {
        var sql = "SELECT " + new string('(', 12) + "1" + new string(')', 12) + " FROM dual";
        var e = Assert.Single(SqlParser.Parse(sql, new ParseOptions { MaxNestingDepth = 10 }).Errors);
        Assert.Equal(ParseErrorCodes.NestingTooDeep, e.Code);
        Assert.StartsWith("nesting depth exceeds 10", e.Message);
    }

    [Fact]
    public void Codes_AreStableAndDistinct()
    {
        var codes = new[]
        {
            ParseErrorCodes.UnexpectedToken, ParseErrorCodes.ExpectedToken, ParseErrorCodes.UnterminatedLiteral,
            ParseErrorCodes.NestingTooDeep, ParseErrorCodes.VersionGated, ParseErrorCodes.InvalidCombination,
            ParseErrorCodes.UnsupportedForm, ParseErrorCodes.NotSingleSelect, ParseErrorCodes.InternalError,
        };
        Assert.Equal(codes.Length, codes.Distinct().Count());
        Assert.All(codes, c => Assert.Matches(new Regex("^PSP[0-9]{4}$"), c));
        Assert.Equal("PSP0001", ParseErrorCodes.UnexpectedToken);
        Assert.Equal("PSP0002", ParseErrorCodes.ExpectedToken);
        Assert.Equal("PSP0003", ParseErrorCodes.UnterminatedLiteral);
        Assert.Equal("PSP0004", ParseErrorCodes.NestingTooDeep);
        Assert.Equal("PSP0005", ParseErrorCodes.VersionGated);
        Assert.Equal("PSP0006", ParseErrorCodes.InvalidCombination);
        Assert.Equal("PSP0007", ParseErrorCodes.UnsupportedForm);
        Assert.Equal("PSP0008", ParseErrorCodes.NotSingleSelect);
        Assert.Equal("PSP0099", ParseErrorCodes.InternalError);
    }

    [Fact]
    public void ParseException_CarriesCodeOfFirstError()
    {
        var ex = Assert.Throws<ParseException>(() => SqlParser.ParseExpression("1 +"));
        Assert.Equal(ParseErrorCodes.UnexpectedToken, ex.Error.Code);
        Assert.Same(ex.Error, Assert.Single(ex.Errors));
    }

    // ---------- Length ----------

    [Fact]
    public void Length_IsTheOffendingTokenSpan()
    {
        var e = OnlyError("SELECT 1 FROM dual ) ");
        Assert.Equal(1, e.Length);
        Assert.Equal(')', "SELECT 1 FROM dual ) "[e.Offset]);
    }

    [Fact]
    public void Length_OfMultiCharacterToken()
    {
        var e = OnlyError("SELECT FROM dual");
        Assert.Equal(ParseErrorCodes.UnexpectedToken, e.Code);
        Assert.Equal("FROM".Length, e.Length);
        Assert.Equal(7, e.Offset);
    }

    [Fact]
    public void Length_IsZeroAtEndOfInput()
    {
        var e = OnlyError("SELECT (1");
        Assert.Equal(ParseErrorCodes.ExpectedToken, e.Code);
        Assert.Equal("SELECT (1".Length, e.Offset);
        Assert.Equal(0, e.Length);
    }

    [Fact]
    public void Length_OfUnterminatedStringRunsToEndOfLine()
    {
        var sql = "SELECT 1 FROM dual;\nSELECT 'abc FROM dual";
        var e = OnlyError(sql);
        Assert.Equal("'abc FROM dual".Length, e.Length);
        Assert.Equal(2, e.Line);
        Assert.Equal(8, e.Column);
    }

    [Fact]
    public void Length_OfUnterminatedCommentSpansToEndOfInput()
    {
        var sql = "SELECT 1 FROM dual /* open\nmore";
        var e = OnlyError(sql);
        Assert.Equal(sql.Length - sql.IndexOf("/*", StringComparison.Ordinal), e.Length);
    }

    [Fact]
    public void Length_IsNeverNegative()
    {
        Assert.Equal(0, new ParseError("m", 0, 1, 1, -5).Length);
    }

    // ---------- Expected ----------

    [Fact]
    public void Expected_ForExpectOp_IsTheOperator()
    {
        var e = OnlyError("SELECT (1 FROM dual");
        Assert.Equal(ParseErrorCodes.ExpectedToken, e.Code);
        Assert.Equal([")"], e.Expected);
    }

    [Fact]
    public void Expected_ForExpectKw_IsTheKeyword()
    {
        var e = OnlyError("SELECT a FROM t ORDER x");
        Assert.Equal(ParseErrorCodes.ExpectedToken, e.Code);
        Assert.Equal(["BY"], e.Expected);
    }

    [Fact]
    public void Expected_ListsEveryAlternative()
    {
        var e = OnlyError("ALTER SESSION ENABLE PARALLEL FOO");
        Assert.Equal(ParseErrorCodes.ExpectedToken, e.Code);
        Assert.Equal(["DML", "DDL", "QUERY"], e.Expected);
    }

    [Fact]
    public void Expected_IsEmptyWhenNothingSpecific()
    {
        var e = OnlyError("SELECT 1 FROM dual ) ");
        Assert.Equal(ParseErrorCodes.UnexpectedToken, e.Code);
        Assert.Empty(e.Expected);
        Assert.NotNull(e.Expected);
    }

    [Fact]
    public void Expected_DefaultsToEmptyListNotNull()
    {
        var e = new ParseError("m", 0, 1, 1);
        Assert.NotNull(e.Expected);
        Assert.Empty(e.Expected);
        Assert.Equal(ParseErrorCodes.UnexpectedToken, e.Code);
        Assert.Equal(0, e.Length);
    }

    // ---------- ToString compatibility ----------

    [Fact]
    public void ToString_KeepsLineColumnMessageForm()
    {
        var e = new ParseError("Expected FROM", 7, 2, 3, 4, ParseErrorCodes.ExpectedToken, ["FROM"]);
        Assert.Equal("(2:3) Expected FROM", e.ToString());
    }

    // ---------- FormatWithCaret ----------

    [Fact]
    public void Caret_UnderASingleLineSpan()
    {
        var src = "SELECT FROM dual";
        var e = new ParseError("x", 7, 1, 8, 4);
        Assert.Equal("SELECT FROM dual\n       ^~~~", e.FormatWithCaret(src));
    }

    [Fact]
    public void Caret_PointWhenLengthIsZeroOrOne()
    {
        var src = "SELECT x";
        Assert.Equal("SELECT x\n       ^", new ParseError("x", 7, 1, 8, 0).FormatWithCaret(src));
        Assert.Equal("SELECT x\n       ^", new ParseError("x", 7, 1, 8, 1).FormatWithCaret(src));
    }

    [Fact]
    public void Caret_CopiesTabsSoItLinesUpInAnyTabWidth()
    {
        var src = "\tSELECT\tfoo";
        int offset = src.IndexOf("foo", StringComparison.Ordinal);
        var e = new ParseError("x", offset, 1, 9, 3);
        Assert.Equal("\tSELECT\tfoo\n\t      \t^~~", e.FormatWithCaret(src));
    }

    [Fact]
    public void Caret_OnTheLastLineWithoutTrailingNewline()
    {
        var src = "a\nbb";
        Assert.Equal("bb\n^~", new ParseError("x", 2, 2, 1, 2).FormatWithCaret(src));
    }

    [Fact]
    public void Caret_OnTheSecondLineOfAMultiLineSource()
    {
        var src = "SELECT 1 FROM dual;\nSELECT 'abc FROM dual";
        var e = OnlyError(src);
        Assert.Equal("SELECT 'abc FROM dual\n       ^~~~~~~~~~~~~~", e.FormatWithCaret(src));
    }

    [Fact]
    public void Caret_AtEndOfInputWithTrailingNewline_ShowsEmptyLine()
    {
        var src = "SELECT 1\n";
        Assert.Equal("\n^", new ParseError("x", src.Length, 2, 1, 0).FormatWithCaret(src));
    }

    [Fact]
    public void Caret_AtEndOfInputWithoutTrailingNewline()
    {
        var src = "SELECT (1";
        var e = OnlyError(src);
        Assert.Equal("SELECT (1\n         ^", e.FormatWithCaret(src));
    }

    [Fact]
    public void Caret_AtEndOfEmptySource()
    {
        Assert.Equal("\n^", new ParseError("x", 0, 1, 1, 0).FormatWithCaret(""));
    }

    [Fact]
    public void Caret_ClipsASpanThatRunsPastItsLine()
    {
        var src = "ab\ncd";
        Assert.Equal("ab\n ^", new ParseError("x", 1, 1, 2, 10).FormatWithCaret(src));
    }

    [Fact]
    public void Caret_HandlesCrLfLineEndings()
    {
        var src = "ab\r\ncd";
        Assert.Equal("ab\n^~", new ParseError("x", 0, 1, 1, 2).FormatWithCaret(src));
        Assert.Equal("cd\n^", new ParseError("x", 4, 2, 1, 0).FormatWithCaret(src));
    }

    [Fact]
    public void Caret_ClampsAnOffsetPastTheEnd()
    {
        Assert.Equal("ab\n  ^", new ParseError("x", 99, 1, 3, 0).FormatWithCaret("ab"));
    }

    [Fact]
    public void Caret_FromAParsedErrorMatchesItsPosition()
    {
        var src = "SELECT 1 FROM dual;\nSELECT 'abc FROM dual";
        var e = OnlyError(src);
        var lines = e.FormatWithCaret(src).Split('\n');
        Assert.Equal("SELECT 'abc FROM dual", lines[0]);
        Assert.Equal(e.Column - 1, lines[1].IndexOf('^'));
        Assert.Equal(e.Length, lines[1].Length - (e.Column - 1));
    }

    [Fact]
    public void Caret_ThrowsOnNullSource()
    {
        Assert.Throws<ArgumentNullException>(() => new ParseError("x", 0, 1, 1).FormatWithCaret(null!));
    }
}
