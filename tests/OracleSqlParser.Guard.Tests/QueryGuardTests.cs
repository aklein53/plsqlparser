using OracleSqlParser;
using OracleSqlParser.Guard;
using Xunit;

namespace OracleSqlParser.Guard.Tests;

public class QueryGuardTests
{
    private static QueryPolicy Policy(params string[] required) => new()
    {
        AllowedTables = new HashSet<string>(["ORDERS", "CUSTOMERS", "HR.EMP"], StringComparer.OrdinalIgnoreCase),
        RequiredColumns = required,
    };

    private static AccessFilter Access(AccessJoinStyle style = AccessJoinStyle.InSubquery) => new()
    {
        KeyColumn = "id",
        AccessSql = "SELECT order_id FROM user_orders WHERE user_id = :accessUser",
        Style = style,
        AccessIdColumn = "order_id",
    };

    private static ViolationCode[] Codes(string sql, QueryPolicy p) => QueryGuard.Validate(sql, p).Select(v => v.Code).ToArray();

    // ---------- validation ----------

    [Theory]
    [InlineData("SELECT id, name FROM orders WHERE status = 'A'")]
    [InlineData("SELECT o.id, c.name FROM orders o JOIN customers c ON c.id = o.cid")]
    [InlineData("WITH x AS (SELECT id FROM orders) SELECT id FROM x")]
    [InlineData("SELECT id FROM orders UNION ALL SELECT id FROM customers")]
    [InlineData("SELECT id, UPPER(name) AS nm FROM hr.emp")]
    public void Accepts(string sql) => Assert.Empty(QueryGuard.Validate(sql, Policy("ID")));

    [Fact] public void Rejects_StatementStacking() => Assert.Equal([ViolationCode.NotSingleSelect], Codes("SELECT id FROM orders; DROP TABLE orders", Policy()));
    [Fact] public void Rejects_Dml() => Assert.Equal([ViolationCode.NotSingleSelect], Codes("DELETE FROM orders", Policy()));
    [Fact] public void Rejects_PlsqlBlock() => Assert.Equal([ViolationCode.NotSingleSelect], Codes("BEGIN NULL; END;", Policy()));
    [Fact] public void Rejects_ParseErrors() => Assert.Equal([ViolationCode.ParseError], Codes("SELECT FROM", Policy()));
    [Fact] public void Rejects_UnknownTable() => Assert.Contains(ViolationCode.TableNotAllowed, Codes("SELECT id FROM secrets", Policy()));
    [Fact] public void Rejects_UnknownTable_InSubquery() => Assert.Contains(ViolationCode.TableNotAllowed, Codes("SELECT id FROM orders WHERE id IN (SELECT id FROM secrets)", Policy()));
    [Fact] public void Rejects_UnknownTable_InSetOperation() => Assert.Contains(ViolationCode.TableNotAllowed, Codes("SELECT id FROM orders UNION SELECT id FROM secrets", Policy()));
    [Fact] public void Rejects_SchemaQualifiedMismatch() => Assert.Contains(ViolationCode.TableNotAllowed, Codes("SELECT id FROM emp", Policy()));
    [Fact] public void Rejects_DatabaseLink() => Assert.Contains(ViolationCode.DatabaseLink, Codes("SELECT id FROM orders@remote", Policy()));
    [Fact] public void Rejects_Into() => Assert.Contains(ViolationCode.IntoClause, Codes("SELECT id INTO v FROM orders", Policy()));
    [Fact] public void Rejects_ForUpdate() => Assert.Contains(ViolationCode.ForUpdate, Codes("SELECT id FROM orders FOR UPDATE", Policy()));
    [Fact] public void Rejects_WithFunction() => Assert.Contains(ViolationCode.PlsqlDeclaration, Codes("WITH FUNCTION f RETURN NUMBER IS BEGIN RETURN 1; END; SELECT f FROM orders", Policy()));
    [Fact] public void Rejects_SubstitutionVariables() => Assert.Contains(ViolationCode.SubstitutionVariable, Codes("SELECT id FROM orders WHERE x = &v", Policy()));
    [Fact] public void Rejects_Star() => Assert.Equal([ViolationCode.StarNotAllowed], Codes("SELECT * FROM orders", Policy()));
    [Fact] public void Rejects_MissingRequired() => Assert.Equal([ViolationCode.MissingRequiredColumn], Codes("SELECT name FROM orders", Policy("ID")));
    [Fact] public void RequiredColumn_CanBeAliased() => Assert.Empty(QueryGuard.Validate("SELECT order_no AS id FROM orders", Policy("ID")));
    [Fact] public void Rejects_DuplicateNames() => Assert.Contains(ViolationCode.DuplicateColumnName, Codes("SELECT o.id, c.id FROM orders o JOIN customers c ON 1 = 1", Policy()));
    [Fact] public void Rejects_QualifiedAndPackageFunctions() => Assert.Contains(ViolationCode.FunctionNotAllowed, Codes("SELECT my_pkg.f(id) AS id FROM orders", Policy("ID")));
    [Fact] public void Rejects_DbmsFunctions() => Assert.Contains(ViolationCode.FunctionNotAllowed, Codes("SELECT dbms_random.value() AS id FROM orders", Policy("ID")));
    [Fact] public void Rejects_PackageMemberWithoutParens() => Assert.Contains(ViolationCode.UnknownQualifier, Codes("SELECT dbms_random.value AS id FROM orders", Policy("ID")));
    [Fact] public void Rejects_SequenceNextval() => Assert.Contains(ViolationCode.UnknownQualifier, Codes("SELECT my_seq.nextval AS id FROM orders", Policy("ID")));
    [Fact] public void Accepts_SchemaQualifiedColumn() => Assert.Empty(QueryGuard.Validate("SELECT hr.emp.id FROM hr.emp", Policy("ID")));
    [Fact] public void Accepts_TableNameAsQualifier() => Assert.Empty(QueryGuard.Validate("SELECT orders.id FROM orders", Policy("ID")));
    [Fact] public void Accepts_MatchRecognizeVariables() => Assert.Empty(QueryGuard.Validate("SELECT id FROM orders MATCH_RECOGNIZE (ORDER BY ts MEASURES a.id AS id PATTERN (a) DEFINE a AS a.x > 1)", Policy("ID")));

    [Fact]
    public void FunctionAllowlist_IsEnforced()
    {
        var p = new QueryPolicy { AllowedTables = new HashSet<string>(["ORDERS"], StringComparer.OrdinalIgnoreCase), AllowedFunctions = new HashSet<string>(["UPPER"], StringComparer.OrdinalIgnoreCase) };
        Assert.Empty(QueryGuard.Validate("SELECT UPPER(name) AS n FROM orders", p));
        Assert.Equal([ViolationCode.FunctionNotAllowed], QueryGuard.Validate("SELECT my_f(name) AS n FROM orders", p).Select(v => v.Code).ToArray());
    }

    [Fact]
    public void Violations_CarryPositions()
    {
        var v = QueryGuard.Validate("SELECT id\nFROM secrets", Policy()).Single();
        Assert.Equal((2, 6), (v.Line, v.Column));
    }

    // ---------- wrapping ----------

    [Fact]
    public void Restrict_WrapsUserQuery_InSubqueryStyle() =>
        Assert.Equal(
            "SELECT Q.* FROM (SELECT ID, NAME FROM ORDERS WHERE STATUS = 'A') Q WHERE Q.ID IN (SELECT ORDER_ID FROM USER_ORDERS WHERE USER_ID = :accessUser)",
            QueryGuard.RestrictToIds("SELECT id, name FROM orders WHERE status = 'A'", Policy(), Access()).Sql);

    [Fact]
    public void Restrict_JoinStyle() =>
        Assert.Equal(
            "SELECT Q.* FROM (SELECT ID, NAME FROM ORDERS) Q JOIN (SELECT ORDER_ID FROM USER_ORDERS WHERE USER_ID = :accessUser) A ON Q.ID = A.ORDER_ID",
            QueryGuard.RestrictToIds("SELECT id, name FROM orders", Policy(), Access(AccessJoinStyle.InnerJoin)).Sql);

    [Fact]
    public void Restrict_UserOrCannotEscapeTheFilter()
    {
        var sql = QueryGuard.RestrictToIds("SELECT id FROM orders WHERE 1 = 1 OR status = 'A'", Policy(), Access()).Sql;
        Assert.Equal("SELECT Q.* FROM (SELECT ID FROM ORDERS WHERE 1 = 1 OR STATUS = 'A') Q WHERE Q.ID IN (SELECT ORDER_ID FROM USER_ORDERS WHERE USER_ID = :accessUser)", sql);
    }

    [Fact]
    public void Restrict_UnionBranchesAreBothRestricted() =>
        Assert.Equal(
            "SELECT Q.* FROM (SELECT ID FROM ORDERS UNION SELECT ID FROM CUSTOMERS) Q WHERE Q.ID IN (SELECT ORDER_ID FROM USER_ORDERS WHERE USER_ID = :accessUser)",
            QueryGuard.RestrictToIds("SELECT id FROM orders UNION SELECT id FROM customers", Policy(), Access()).Sql);

    [Fact]
    public void Restrict_GroupByAndAggregates_StillWrap() =>
        Assert.Equal(
            "SELECT Q.* FROM (SELECT ID, COUNT(*) AS N FROM ORDERS GROUP BY ID) Q WHERE Q.ID IN (SELECT ORDER_ID FROM USER_ORDERS WHERE USER_ID = :accessUser)",
            QueryGuard.RestrictToIds("SELECT id, COUNT(*) AS n FROM orders GROUP BY id", Policy(), Access()).Sql);

    [Fact]
    public void Restrict_HoistsOrderBy_WhenExpressibleOnOutputColumns() =>
        Assert.Equal(
            "SELECT Q.* FROM (SELECT ID, NAME FROM ORDERS) Q WHERE Q.ID IN (SELECT ORDER_ID FROM USER_ORDERS WHERE USER_ID = :accessUser) ORDER BY Q.NAME DESC, Q.ID",
            QueryGuard.RestrictToIds("SELECT id, name FROM orders ORDER BY name DESC, 1", Policy(), Access()).Sql);

    [Fact]
    public void Restrict_KeepsOrderByInside_WhenNotExpressible() =>
        Assert.Equal(
            "SELECT Q.* FROM (SELECT ID, NAME FROM ORDERS ORDER BY UPPER(NAME)) Q WHERE Q.ID IN (SELECT ORDER_ID FROM USER_ORDERS WHERE USER_ID = :accessUser)",
            QueryGuard.RestrictToIds("SELECT id, name FROM orders ORDER BY upper(name)", Policy(), Access()).Sql);

    [Fact]
    public void Restrict_KeepsFetchInside_AndOrdersOutside() =>
        Assert.Equal(
            "SELECT Q.* FROM (SELECT ID FROM ORDERS ORDER BY ID FETCH FIRST 10 ROWS ONLY) Q WHERE Q.ID IN (SELECT ORDER_ID FROM USER_ORDERS WHERE USER_ID = :accessUser) ORDER BY Q.ID",
            QueryGuard.RestrictToIds("SELECT id FROM orders ORDER BY id FETCH FIRST 10 ROWS ONLY", Policy(), Access()).Sql);

    [Fact]
    public void Restrict_ReportsBindNames()
    {
        var g = QueryGuard.RestrictToIds("SELECT id FROM orders WHERE status = :status", Policy(), Access());
        Assert.Equal(["status", "accessUser"], g.BindNames);
    }

    [Fact]
    public void Restrict_RejectsInvalidUserQuery_WithAllViolations()
    {
        var ex = Assert.Throws<QueryRejectedException>(() => QueryGuard.RestrictToIds("SELECT name FROM secrets", Policy(), Access()));
        Assert.Contains(ex.Violations, v => v.Code == ViolationCode.TableNotAllowed);
        Assert.Contains(ex.Violations, v => v.Code == ViolationCode.MissingRequiredColumn); // key column "id" is implicitly required
    }

    [Fact]
    public void Restrict_RejectsBindNameCollision()
    {
        var ex = Assert.Throws<QueryRejectedException>(() => QueryGuard.RestrictToIds("SELECT id FROM orders WHERE x = :accessUser", Policy(), Access()));
        Assert.Equal(ViolationCode.BindNameCollision, ex.Violations.Single().Code);
    }

    [Fact]
    public void Restrict_RejectsBadAccessSql()
    {
        var f = new AccessFilter { KeyColumn = "id", AccessSql = "DELETE FROM x" };
        Assert.Equal(ViolationCode.UnsupportedAccessSql, Assert.Throws<QueryRejectedException>(() => QueryGuard.RestrictToIds("SELECT id FROM orders", Policy(), f)).Violations.Single().Code);
    }

    [Fact]
    public void Restrict_Output_IsASingleSelect()
    {
        var sql = QueryGuard.RestrictToIds("SELECT o.id, c.name FROM orders o LEFT JOIN customers c ON c.id = o.cid WHERE o.x IN (1, 2) OR o.y IS NULL", Policy(), Access()).Sql;
        var r = SqlParser.Parse(sql);
        Assert.True(r.Success);
        Assert.IsType<SelectStatement>(Assert.Single(r.Script.Statements));
    }
}
