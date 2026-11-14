using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

/// <summary>ROADMAP Epic 5: TryParse, ParseSelect and TryParseSelect, and which entry points throw.</summary>
public class ParseEntryPointTests
{
    private static readonly ParseOptions Oracle19 = new() { OracleVersion = OracleVersion.Oracle19 };

    // ---------- TryParse ----------

    [Fact]
    public void TryParse_ValidScript_ReturnsTrueWithStatements()
    {
        bool ok = SqlParser.TryParse("SELECT 1 FROM dual; SELECT 2 FROM dual", out var script, out var errors);
        Assert.True(ok);
        Assert.Empty(errors);
        Assert.Equal(2, script.Statements.Count);
    }

    [Fact]
    public void TryParse_InvalidScript_ReturnsFalseWithErrorsAndScript()
    {
        bool ok = SqlParser.TryParse("SELECT 1 FROM dual;\nSELECT FROM dual", out var script, out var errors);
        Assert.False(ok);
        Assert.NotEmpty(errors);
        Assert.NotNull(script);
        Assert.Equal(ParseErrorCodes.UnexpectedToken, errors[0].Code);
        Assert.Equal(2, errors[0].Line);
        Assert.Single(script.Statements);
    }

    [Fact]
    public void TryParse_AgreesWithParse()
    {
        const string sql = "SELECT 1 FROM dual;\nSELECT 'abc FROM dual";
        var parsed = SqlParser.Parse(sql);
        bool ok = SqlParser.TryParse(sql, out var script, out var errors);
        Assert.Equal(parsed.Success, ok);
        Assert.Equal(parsed.Errors.Select(e => (e.Code, e.Offset, e.Message)), errors.Select(e => (e.Code, e.Offset, e.Message)));
        Assert.Equal(parsed.Script.Statements.Count, script.Statements.Count);
    }

    [Fact]
    public void TryParse_HonoursOptions()
    {
        bool ok = SqlParser.TryParse("SELECT 1", out _, out var errors, Oracle19);
        Assert.False(ok);
        Assert.Equal(ParseErrorCodes.VersionGated, Assert.Single(errors).Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("((((")]
    [InlineData("'unterminated")]
    [InlineData("/* open")]
    [InlineData("SELECT 1 FROM dual;;; ) ]")]
    public void TryParse_NeverThrows(string sql)
    {
        var ex = Record.Exception(() => SqlParser.TryParse(sql, out _, out _));
        Assert.Null(ex);
    }

    // ---------- ParseSelect ----------

    [Fact]
    public void ParseSelect_ReturnsTheSelect()
    {
        var select = SqlParser.ParseSelect("SELECT 1 FROM dual");
        Assert.IsType<SelectStatement>(select);
        Assert.True(SqlNodeComparer.StructurallyEqual(select, SqlParser.ParseStatement("SELECT 1 FROM dual")));
    }

    [Fact]
    public void ParseSelect_AllowsTrailingSemicolonAndSlash()
    {
        Assert.IsType<SelectStatement>(SqlParser.ParseSelect("SELECT 1 FROM dual;"));
        Assert.IsType<SelectStatement>(SqlParser.ParseSelect("SELECT 1 FROM dual\n/"));
    }

    [Fact]
    public void ParseSelect_WithCte_ReturnsTheSelect()
    {
        Assert.IsType<SelectStatement>(SqlParser.ParseSelect("WITH c AS (SELECT 1 AS x FROM dual) SELECT x FROM c"));
    }

    [Fact]
    public void ParseSelect_SyntaxError_ThrowsWithNonEmptyErrors()
    {
        var ex = Assert.Throws<ParseException>(() => SqlParser.ParseSelect("SELECT FROM dual"));
        Assert.NotEmpty(ex.Errors);
        Assert.Equal(ParseErrorCodes.UnexpectedToken, ex.Error.Code);
        Assert.Same(ex.Error, ex.Errors[0]);
    }

    [Fact]
    public void ParseSelect_VersionError_Throws()
    {
        var ex = Assert.Throws<ParseException>(() => SqlParser.ParseSelect("SELECT 1", Oracle19));
        Assert.Equal(ParseErrorCodes.VersionGated, ex.Error.Code);
    }

    [Fact]
    public void ParseSelect_TwoStatements_ThrowsNotSingleSelectAtTheSecond()
    {
        const string sql = "SELECT 1 FROM dual; SELECT 2 FROM dual";
        var ex = Assert.Throws<ParseException>(() => SqlParser.ParseSelect(sql));
        var e = Assert.Single(ex.Errors);
        Assert.Equal(ParseErrorCodes.NotSingleSelect, e.Code);
        Assert.Equal(sql.IndexOf("SELECT 2", StringComparison.Ordinal), e.Offset);
        Assert.Contains("found 2 statements", e.Message);
    }

    [Fact]
    public void ParseSelect_NonSelect_ThrowsNotSingleSelect()
    {
        var ex = Assert.Throws<ParseException>(() => SqlParser.ParseSelect("INSERT INTO t VALUES (1)"));
        var e = Assert.Single(ex.Errors);
        Assert.Equal(ParseErrorCodes.NotSingleSelect, e.Code);
        Assert.Equal(0, e.Offset);
        Assert.Equal("INSERT INTO t VALUES (1)".Length, e.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ;  ")]
    public void ParseSelect_NoStatement_ThrowsNotSingleSelect(string sql)
    {
        var ex = Assert.Throws<ParseException>(() => SqlParser.ParseSelect(sql));
        var e = Assert.Single(ex.Errors);
        Assert.Equal(ParseErrorCodes.NotSingleSelect, e.Code);
        Assert.Equal(0, e.Length);
    }

    [Fact]
    public void ParseSelect_NeverThrowsAnythingOtherThanParseException()
    {
        foreach (var sql in new[] { "", "((((", "SELECT 'x", "SELECT 1; SELECT 2", "DELETE FROM t" })
        {
            var ex = Record.Exception(() => SqlParser.ParseSelect(sql));
            Assert.IsType<ParseException>(ex);
            Assert.NotEmpty(((ParseException)ex).Errors);
        }
    }

    [Fact]
    public void ParseSelect_WithOptions_ReturnsTheSelect()
    {
        Assert.IsType<SelectStatement>(SqlParser.ParseSelect("SELECT 1 FROM dual", new ParseOptions()));
    }

    // ---------- TryParseSelect ----------

    [Fact]
    public void TryParseSelect_OneSelect_ReturnsTrue()
    {
        bool ok = SqlParser.TryParseSelect("SELECT 1 FROM dual", out var select, out var errors);
        Assert.True(ok);
        Assert.NotNull(select);
        Assert.Empty(errors);
    }

    [Fact]
    public void TryParseSelect_SyntaxError_ReturnsFalseWithParseErrors()
    {
        bool ok = SqlParser.TryParseSelect("SELECT (1 FROM dual", out var select, out var errors);
        Assert.False(ok);
        Assert.Null(select);
        Assert.Equal(ParseErrorCodes.ExpectedToken, Assert.Single(errors).Code);
    }

    [Fact]
    public void TryParseSelect_NonSelect_ReturnsFalseWithNotSingleSelect()
    {
        bool ok = SqlParser.TryParseSelect("DELETE FROM t", out var select, out var errors);
        Assert.False(ok);
        Assert.Null(select);
        Assert.Equal(ParseErrorCodes.NotSingleSelect, Assert.Single(errors).Code);
    }

    [Fact]
    public void TryParseSelect_TwoSelects_ReturnsFalse()
    {
        bool ok = SqlParser.TryParseSelect("SELECT 1 FROM dual; SELECT 2 FROM dual", out var select, out var errors);
        Assert.False(ok);
        Assert.Null(select);
        Assert.Equal(ParseErrorCodes.NotSingleSelect, Assert.Single(errors).Code);
    }

    [Fact]
    public void TryParseSelect_Empty_ReturnsFalseWithAnError()
    {
        bool ok = SqlParser.TryParseSelect("", out var select, out var errors);
        Assert.False(ok);
        Assert.Null(select);
        Assert.NotEmpty(errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("((((")]
    [InlineData("'unterminated")]
    [InlineData("SELECT 1; SELECT 2")]
    public void TryParseSelect_NeverThrows(string sql)
    {
        var ex = Record.Exception(() => SqlParser.TryParseSelect(sql, out _, out _));
        Assert.Null(ex);
    }

    [Fact]
    public void TryParseSelect_FalseAlwaysCarriesAnError()
    {
        foreach (var sql in new[] { "", "INSERT INTO t VALUES (1)", "SELECT FROM", "SELECT 1; SELECT 2" })
        {
            bool ok = SqlParser.TryParseSelect(sql, out _, out var errors);
            Assert.False(ok, sql);
            Assert.NotEmpty(errors);
        }
    }

    // ---------- ParseException ----------

    [Fact]
    public void ParseException_FromList_ExposesAllErrorsAndFirstAsError()
    {
        var a = new ParseError("first", 0, 1, 1);
        var b = new ParseError("second", 4, 1, 5);
        var ex = new ParseException([a, b]);
        Assert.Same(a, ex.Error);
        Assert.Equal(2, ex.Errors.Count);
        Assert.Same(b, ex.Errors[1]);
        Assert.Equal(a.ToString(), ex.Message);
    }

    [Fact]
    public void ParseException_FromSingleError_HasOneEntry()
    {
        var a = new ParseError("only", 0, 1, 1);
        var ex = new ParseException(a);
        Assert.Same(a, Assert.Single(ex.Errors));
        Assert.Same(a, ex.Error);
    }

    [Fact]
    public void ParseException_EmptyList_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ParseException(Array.Empty<ParseError>()));
    }

    [Fact]
    public void ParseStatement_Failure_ExposesErrors()
    {
        var ex = Assert.Throws<ParseException>(() => SqlParser.ParseStatement("SELECT FROM"));
        Assert.Equal(ex.Error, Assert.Single(ex.Errors));
    }
}
