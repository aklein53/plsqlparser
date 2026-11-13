using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using OracleSqlParser;

namespace OracleSqlParser.Guard;

[Experimental("PLSQLGUARD001")]
public enum ViolationCode
{
    ParseError, NotSingleSelect, TableNotAllowed, DatabaseLink, IntoClause, ForUpdate, PlsqlDeclaration, SubstitutionVariable,
    FunctionNotAllowed, UnknownQualifier, StarNotAllowed, MissingRequiredColumn, DuplicateColumnName, BindNameCollision, UnsupportedAccessSql,
}

[Experimental("PLSQLGUARD001")]
public sealed record PolicyViolation(ViolationCode Code, string Message, int Line = 0, int Column = 0)
{
    public override string ToString() => Line > 0 ? $"({Line}:{Column}) {Code}: {Message}" : $"{Code}: {Message}";
}

[Experimental("PLSQLGUARD001")]
public sealed class QueryRejectedException(IReadOnlyList<PolicyViolation> violations)
    : Exception("Query rejected: " + string.Join("; ", violations))
{
    public IReadOnlyList<PolicyViolation> Violations { get; } = violations;
}

/// <summary>What a user-supplied query may do.</summary>
[Experimental("PLSQLGUARD001")]
public sealed class QueryPolicy
{
    /// <summary>Tables/views the query may read, matched exactly as written in the query: NAME or SCHEMA.NAME (case-insensitive; an unqualified use does not match a SCHEMA.NAME entry). CTE names defined in the query are always fine.</summary>
    public ISet<string> AllowedTables { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// If set, only these functions may be called (NAME or PKG.NAME). If null, the default rule applies:
    /// package-qualified functions and DBMS_*/UTL_* calls are rejected (they can have side effects); unqualified names are allowed.
    /// An allowlist is the safer choice.
    /// </summary>
    public ISet<string>? AllowedFunctions { get; init; }

    /// <summary>Columns the query's top-level select list must expose (by name or alias).</summary>
    public IReadOnlyList<string> RequiredColumns { get; init; } = [];

    /// <summary>Allow SELECT * (required columns cannot then be verified statically).</summary>
    public bool AllowStar { get; init; }
}

[Experimental("PLSQLGUARD001")]
public enum AccessJoinStyle { InSubquery, InnerJoin }

/// <summary>The trusted restriction applied to a user query: only rows whose key is in the access list.</summary>
[Experimental("PLSQLGUARD001")]
public sealed class AccessFilter
{
    /// <summary>Column of the user's query that identifies the row (must be exposed by its select list).</summary>
    public required string KeyColumn { get; init; }

    /// <summary>Trusted SQL returning the permitted ids, e.g. "SELECT order_id FROM user_orders WHERE user_id = :accessUserId".</summary>
    public required string AccessSql { get; init; }

    public AccessJoinStyle Style { get; init; } = AccessJoinStyle.InSubquery;

    /// <summary>For InnerJoin style: the id column exposed by <see cref="AccessSql"/>. Ignored for InSubquery (first column is used).</summary>
    public string AccessIdColumn { get; init; } = "ID";
}

[Experimental("PLSQLGUARD001")]
public sealed record GuardedSql(string Sql, IReadOnlyList<string> BindNames);

[Experimental("PLSQLGUARD001")]
public static class QueryGuard
{
    private static readonly HashSet<string> StructuralFunctions = new(StringComparer.OrdinalIgnoreCase) { "ROLLUP", "CUBE", "GROUPING SETS" };

    /// <summary>Checks a user-supplied query against the policy. Empty result means it is acceptable.</summary>
    public static IReadOnlyList<PolicyViolation> Validate(string sql, QueryPolicy policy)
    {
        var parsed = SqlParser.Parse(sql);
        var v = new List<PolicyViolation>();
        foreach (var e in parsed.Errors) v.Add(new PolicyViolation(ViolationCode.ParseError, e.Message, e.Line, e.Column));
        if (v.Count > 0) return v;
        if (parsed.Script.Statements.Count != 1 || parsed.Script.Statements[0] is not SelectStatement top)
        {
            v.Add(new PolicyViolation(ViolationCode.NotSingleSelect, "Exactly one SELECT statement is allowed"));
            return v;
        }
        Check(top, policy, v);
        return v;
    }

    private static PolicyViolation At(ViolationCode c, string msg, SqlNode n) => new(c, msg, n.Line, n.Column);

    private static void Check(SelectStatement top, QueryPolicy policy, List<PolicyViolation> v)
    {
        var all = top.Descendants().ToList();
        var cteNames = new HashSet<string>(all.OfType<CommonTableExpression>().Select(c => c.Name.Value), StringComparer.OrdinalIgnoreCase);

        // Names a qualified reference may start from: tables, aliases, CTEs, MATCH_RECOGNIZE variables.
        var known = new HashSet<string>(cteNames, StringComparer.OrdinalIgnoreCase);
        foreach (var t in all.OfType<TableReference>())
        {
            if (t.Alias != null) known.Add(t.Alias.Value);
            if (t is NamedTable nt && nt.Name.Count > 0) { known.Add(nt.Name[^1].Value); known.Add(string.Join(".", nt.Name.Select(p => p.Value))); }
        }
        foreach (var pv in all.OfType<PatternVariable>()) known.Add(pv.Name.Value);
        foreach (var pd in all.OfType<PatternDefine>()) known.Add(pd.Variable.Value);
        foreach (var ps in all.OfType<PatternSubset>()) known.Add(ps.Name.Value);

        foreach (var n in all)
        {
            switch (n)
            {
                case ColumnReference cr when cr.Parts.Count >= 2 && !(known.Contains(cr.Parts[0].Value) || (cr.Parts.Count >= 3 && known.Contains(cr.Parts[1].Value))):
                    // e.g. dbms_random.value or seq.nextval: indistinguishable from a package-member call, so rejected.
                    v.Add(At(ViolationCode.UnknownQualifier, $"{string.Join(".", cr.Parts.Select(p => p.Value))}: {cr.Parts[0].Value} is not a table or alias in this query", cr));
                    break;
                case SelectStatement s:
                    if (s.ForUpdate != null) v.Add(At(ViolationCode.ForUpdate, "FOR UPDATE is not allowed", s.ForUpdate));
                    if (s.WithDeclarations.Count > 0) v.Add(At(ViolationCode.PlsqlDeclaration, "WITH FUNCTION/PROCEDURE is not allowed", s.WithDeclarations[0]));
                    break;
                case QuerySpecification qs when qs.Into.Count > 0:
                    v.Add(At(ViolationCode.IntoClause, "SELECT ... INTO is not allowed", qs));
                    break;
                case NamedTable t:
                    if (t.DbLink.Count > 0) v.Add(At(ViolationCode.DatabaseLink, "Database links are not allowed", t));
                    var full = string.Join(".", t.Name.Select(p => p.Value));
                    bool isCte = t.Name.Count == 1 && cteNames.Contains(t.Name[0].Value);
                    if (!isCte && !policy.AllowedTables.Contains(full))
                        v.Add(At(ViolationCode.TableNotAllowed, $"Table {full} is not allowed", t));
                    break;
                case SubstitutionVariable sv:
                    v.Add(At(ViolationCode.SubstitutionVariable, "Substitution variables (&) are not allowed", sv));
                    break;
                case FunctionCall fc:
                    CheckFunction(fc, policy, v);
                    break;
            }
        }

        // output columns
        var spec = LeftmostSpec(top.Body);
        if (spec == null) return;
        var names = OutputNames(spec, out bool hasStar);
        if (hasStar && !policy.AllowStar)
            v.Add(At(ViolationCode.StarNotAllowed, "SELECT * is not allowed; list the columns", spec));
        if (!hasStar)
            foreach (var req in policy.RequiredColumns)
                if (!names.Any(x => x != null && x.Equals(req, StringComparison.OrdinalIgnoreCase)))
                    v.Add(At(ViolationCode.MissingRequiredColumn, $"Required column {req} is not selected", spec));
        var dup = names.Where(x => x != null).GroupBy(x => x!, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (dup != null) v.Add(At(ViolationCode.DuplicateColumnName, $"Column name {dup.Key} is selected more than once; alias one", spec));
    }

    private static void CheckFunction(FunctionCall fc, QueryPolicy policy, List<PolicyViolation> v)
    {
        var name = string.Join(".", fc.Name.Select(p => p.Value));
        if (StructuralFunctions.Contains(name)) return;
        if (policy.AllowedFunctions != null)
        {
            if (!policy.AllowedFunctions.Contains(name))
                v.Add(At(ViolationCode.FunctionNotAllowed, $"Function {name} is not allowed", fc));
            return;
        }
        if (fc.Name.Count > 1 || name.StartsWith("DBMS_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("UTL_", StringComparison.OrdinalIgnoreCase))
            v.Add(At(ViolationCode.FunctionNotAllowed, $"Function {name} is not allowed", fc));
    }

    private static QuerySpecification? LeftmostSpec(QueryExpression q) => q switch
    {
        QuerySpecification s => s,
        SetOperation so => LeftmostSpec(so.Left),
        ParenthesizedQuery pq => LeftmostSpec(pq.Query.Body),
        _ => null,
    };

    /// <summary>Output column names of a select list (null for unnamed expressions).</summary>
    private static List<string?> OutputNames(QuerySpecification spec, out bool hasStar)
    {
        hasStar = false;
        var names = new List<string?>();
        foreach (var item in spec.SelectList)
        {
            if (item is StarSelectItem) { hasStar = true; continue; }
            var e = (ExpressionSelectItem)item;
            names.Add(e.Alias?.Value ?? (e.Expression is ColumnReference c ? c.Parts[^1].Value : null));
        }
        return names;
    }

    /// <summary>
    /// Validates the user's query and wraps it so only rows whose key is in the access list are returned:
    /// SELECT q.* FROM (user query) q WHERE q.key IN (access query)  [or an INNER JOIN].
    /// Because the restriction is applied outside the user's query, OR/UNION/GROUP BY inside it cannot bypass it.
    /// </summary>
    public static GuardedSql RestrictToIds(string userSql, QueryPolicy policy, AccessFilter filter)
    {
        var required = policy.RequiredColumns.Concat([filter.KeyColumn]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var effective = new QueryPolicy { AllowedTables = policy.AllowedTables, AllowedFunctions = policy.AllowedFunctions, RequiredColumns = required, AllowStar = false };
        var violations = Validate(userSql, effective).ToList();
        if (violations.Count > 0) throw new QueryRejectedException(violations);

        var user = (SelectStatement)SqlParser.Parse(userSql).Script.Statements[0];
        var accessParsed = SqlParser.Parse(filter.AccessSql);
        if (!accessParsed.Success || accessParsed.Script.Statements.Count != 1 || accessParsed.Script.Statements[0] is not SelectStatement access)
            throw new QueryRejectedException([new PolicyViolation(ViolationCode.UnsupportedAccessSql, "AccessSql must be a single valid SELECT")]);

        var userBinds = BindNames(user);
        var accessBinds = BindNames(access);
        var clash = userBinds.Intersect(accessBinds, StringComparer.OrdinalIgnoreCase).ToList();
        if (clash.Count > 0)
            throw new QueryRejectedException([new PolicyViolation(ViolationCode.BindNameCollision, $"Bind name(s) used by both the user query and the access query: {string.Join(", ", clash)}")]);

        var spec = LeftmostSpec(user.Body)!;
        var outNames = OutputNames(spec, out _);

        var q = Ident("Q");
        var outer = new QuerySpecification { SelectList = [new StarSelectItem { Qualifier = [Ident("Q")] }] };
        var inline = new SubqueryTable { Query = user, Alias = q };
        var key = Col("Q", filter.KeyColumn);
        if (filter.Style == AccessJoinStyle.InSubquery)
        {
            outer.From = [inline];
            outer.Where = new InExpression { Value = key, Subquery = access };
        }
        else
        {
            outer.From =
            [
                new JoinTable
                {
                    Left = inline, Kind = JoinKind.Inner,
                    Right = new SubqueryTable { Query = access, Alias = Ident("A") },
                    On = new BinaryExpression { Left = key, Operator = "=", Right = Col("A", filter.AccessIdColumn) },
                },
            ];
        }
        var result = new SelectStatement { Body = outer };
        HoistOrderBy(user, result, outNames);

        var sql = SqlWriter.ToSql(result);
        // The printed SQL must be a single valid SELECT; guards against writer/parser drift.
        var check = SqlParser.Parse(sql);
        if (!check.Success || check.Script.Statements.Count != 1 || check.Script.Statements[0] is not SelectStatement)
            throw new InvalidOperationException("Internal error: guarded SQL did not re-parse: " + sql);
        return new GuardedSql(sql, BindNames(result));
    }

    private static List<string> BindNames(SqlNode n) =>
        n.Descendants().OfType<BindVariable>().Select(b => b.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static Identifier Ident(string name)
    {
        bool simple = Regex.IsMatch(name, @"^[A-Za-z][A-Za-z0-9_$#]*$");
        return new Identifier { Value = simple ? name.ToUpperInvariant() : name, Quoted = !simple };
    }

    private static ColumnReference Col(string qualifier, string column) => new() { Parts = [Ident(qualifier), Ident(column)] };

    /// <summary>Moves the user's top-level ORDER BY outside the inline view when every item can be expressed on its output columns.</summary>
    private static void HoistOrderBy(SelectStatement user, SelectStatement outer, List<string?> outNames)
    {
        if (user.OrderBy.Count == 0 || user.OrderSiblings) return;
        var mapped = new List<OrderByItem>();
        foreach (var item in user.OrderBy)
        {
            string? name = item.Expression switch
            {
                ColumnReference c when outNames.Any(n => n != null && n.Equals(c.Parts[^1].Value, StringComparison.OrdinalIgnoreCase)) => c.Parts[^1].Value,
                Literal { Kind: LiteralKind.Number } l when int.TryParse(l.Value, out var pos) && pos >= 1 && pos <= outNames.Count => outNames[pos - 1],
                _ => null,
            };
            if (name == null) return; // cannot express: leave ORDER BY inside
            mapped.Add(new OrderByItem { Expression = Col("Q", name), Descending = item.Descending, Nulls = item.Nulls });
        }
        outer.OrderBy = mapped;
        if (user.Fetch == null && user.Offset == null) user.OrderBy = [];
    }
}
