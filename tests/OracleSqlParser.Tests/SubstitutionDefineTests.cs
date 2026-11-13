using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

/// <summary>SQL*Plus SET DEFINE handling: whether '&amp;' (or another prefix character) starts a substitution variable.</summary>
public class SubstitutionDefineTests
{
    private static List<SubstitutionVariable> Subs(ParseResult r) =>
        r.Script.Descendants().OfType<SubstitutionVariable>().ToList();

    [Fact]
    public void Ampersand_is_a_substitution_variable_by_default()
    {
        var r = SqlParser.Parse("SELECT &x FROM dual");
        Assert.True(r.Success);
        var sv = Assert.Single(Subs(r));
        Assert.Equal("x", sv.Name);
        Assert.False(sv.Persistent);
    }

    [Fact]
    public void Double_ampersand_is_persistent_by_default()
    {
        var r = SqlParser.Parse("SELECT &&x FROM dual");
        Assert.True(r.Success);
        Assert.True(Assert.Single(Subs(r)).Persistent);
    }

    [Fact]
    public void Set_define_off_leaves_ampersand_literal_inside_strings()
    {
        const string sql = "SET DEFINE OFF\nSELECT 'A&B' FROM dual";
        var r = SqlParser.Parse(sql);
        Assert.True(r.Success);
        Assert.Empty(Subs(r));
        Assert.Contains("'A&B'", Shape.Of(sql));
    }

    [Fact]
    public void Set_define_off_makes_ampersand_an_ordinary_operator()
    {
        var r = SqlParser.Parse("SET DEFINE OFF\nSELECT &x FROM dual");
        Assert.False(r.Success);
        Assert.Empty(Subs(r));
    }

    [Theory]
    [InlineData("SET DEFINE OFF\nSELECT &x FROM dual")]
    [InlineData("set define off\nSELECT &x FROM dual")]
    [InlineData("SET DEF OFF\nSELECT &x FROM dual")]
    [InlineData("Set Define Off  \nSELECT &x FROM dual")]
    [InlineData("SET DEFINE OFF;\nSELECT &x FROM dual")]
    public void Set_define_off_is_case_insensitive_and_accepts_the_short_form(string sql)
    {
        var r = SqlParser.Parse(sql);
        Assert.False(r.Success);
        Assert.Empty(Subs(r));
    }

    [Fact]
    public void Set_define_on_restores_substitution()
    {
        var r = SqlParser.Parse("SET DEFINE OFF\nSELECT 'A&B' FROM dual;\nSET DEFINE ON\nSELECT &x FROM dual");
        Assert.True(r.Success);
        var sv = Assert.Single(Subs(r));
        Assert.Equal("x", sv.Name);
    }

    [Fact]
    public void Set_define_char_changes_the_prefix()
    {
        var r = SqlParser.Parse("SET DEFINE ^\nSELECT ^x FROM dual");
        Assert.True(r.Success);
        var sv = Assert.Single(Subs(r));
        Assert.Equal("x", sv.Name);
        Assert.False(sv.Persistent);
    }

    [Fact]
    public void Set_define_char_turns_ampersand_into_an_operator()
    {
        var r = SqlParser.Parse("SET DEFINE ^\nSELECT &x FROM dual");
        Assert.False(r.Success);
        Assert.Empty(Subs(r));
    }

    [Fact]
    public void Set_define_char_double_prefix_is_persistent()
    {
        var r = SqlParser.Parse("SET DEFINE ^\nSELECT ^^x FROM dual");
        Assert.True(r.Success);
        Assert.True(Assert.Single(Subs(r)).Persistent);
    }

    [Fact]
    public void Set_define_ampersand_restores_the_default_prefix()
    {
        var r = SqlParser.Parse("SET DEFINE ^\nSET DEFINE &\nSELECT &x FROM dual");
        Assert.True(r.Success);
        Assert.Single(Subs(r));
    }

    [Fact]
    public void Set_define_is_ignored_when_not_at_the_start_of_a_line()
    {
        var r = SqlParser.Parse("SELECT 1 FROM dual; SET DEFINE OFF\nSELECT &x FROM dual");
        Assert.True(r.Success);
        Assert.Single(Subs(r));
    }

    [Fact]
    public void Set_define_is_ignored_when_it_is_a_column_assignment()
    {
        var r = SqlParser.Parse("UPDATE t\nSET define = 1\nWHERE a = &x");
        Assert.True(r.Success);
        Assert.Single(Subs(r));
    }

    [Fact]
    public void Parse_option_disables_substitution_from_the_start()
    {
        var options = new ParseOptions { SubstitutionEnabled = false };
        var r = SqlParser.Parse("SELECT &x FROM dual", options);
        Assert.False(r.Success);
        Assert.Empty(Subs(r));
        Assert.Throws<ParseException>(() => SqlParser.ParseStatement("SELECT &x FROM dual", options));
        var lit = SqlParser.Parse("SELECT 'A&B' FROM dual", options);
        Assert.True(lit.Success);
        Assert.Empty(Subs(lit));
        Assert.Contains(lit.Tokens, t => t.Type == TokenType.String && t.Value == "A&B");
    }

    [Fact]
    public void Parse_option_defaults_to_enabled()
    {
        Assert.True(new ParseOptions().SubstitutionEnabled);
        Assert.Single(Subs(SqlParser.Parse("SELECT &x FROM dual", new ParseOptions())));
    }

    [Fact]
    public void Custom_prefix_is_printed_back_and_reparses_to_the_same_tree()
    {
        const string sql = "SET DEFINE ^\nSELECT ^x, ^^y FROM dual";
        var r = SqlParser.Parse(sql);
        Assert.True(r.Success);
        var printed = new SqlScriptGenerator(new SqlScriptGeneratorOptions()).Generate(r.Script);
        Assert.Contains("^x", printed);
        Assert.Contains("^^y", printed);
        Assert.DoesNotContain("&", printed);
        Assert.Equal(Shape.Of(sql), Shape.Of(printed));
    }

    [Fact]
    public void Custom_prefix_survives_deep_clone()
    {
        var sv = Subs(SqlParser.Parse("SET DEFINE ^\nSELECT ^^y FROM dual")).Single();
        var clone = sv.DeepClone<SubstitutionVariable>();
        Assert.Equal('^', clone.Prefix);
        Assert.True(clone.Persistent);
    }
}
