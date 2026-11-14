namespace OracleSqlParser;

// ---------- ALTER MATERIALIZED VIEW ----------

/// <summary>
/// ALTER MATERIALIZED VIEW with refresh, query-rewrite, COMPILE, CONSIDER FRESH, SHRINK SPACE and physical clauses.
/// The refresh clause reuses <see cref="RefreshClause"/> from CREATE MATERIALIZED VIEW; [NO]CACHE, [NO]LOGGING and
/// PARALLEL reuse the physical attribute nodes.
/// </summary>
public sealed partial class AlterMaterializedViewStatement : Statement
{
    /// <summary>The materialized view name parts, schema first when the name is qualified.</summary>
    public List<Identifier> Name { get; set; } = [];
    /// <summary>The new <c>REFRESH</c> clause, or null when the statement does not change the refresh method.</summary>
    public RefreshClause? Refresh { get; set; }
    /// <summary>ENABLE (true) or DISABLE (false) QUERY REWRITE.</summary>
    public bool? QueryRewrite { get; set; }
    /// <summary>True when <c>COMPILE</c> is written.</summary>
    public bool Compile { get; set; }
    /// <summary>True when <c>CONSIDER FRESH</c> is written.</summary>
    public bool ConsiderFresh { get; set; }
    /// <summary>True when <c>SHRINK SPACE</c> is written.</summary>
    public bool ShrinkSpace { get; set; }
    /// <summary>True when <c>SHRINK SPACE COMPACT</c> is written.</summary>
    public bool ShrinkCompact { get; set; }
    /// <summary>True when <c>SHRINK SPACE CASCADE</c> is written.</summary>
    public bool ShrinkCascade { get; set; }
    /// <summary>The physical attributes changed by the statement; empty when none are written.</summary>
    public List<PhysicalAttribute> Attributes { get; set; } = [];
}
