namespace OracleSqlParser;

// ---------- ALTER MATERIALIZED VIEW ----------

/// <summary>
/// ALTER MATERIALIZED VIEW with refresh, query-rewrite, COMPILE, CONSIDER FRESH, SHRINK SPACE and physical clauses.
/// The refresh clause reuses <see cref="RefreshClause"/> from CREATE MATERIALIZED VIEW; [NO]CACHE, [NO]LOGGING and
/// PARALLEL reuse the physical attribute nodes.
/// </summary>
public sealed partial class AlterMaterializedViewStatement : Statement
{
    public List<Identifier> Name { get; set; } = [];
    public RefreshClause? Refresh { get; set; }
    /// <summary>ENABLE (true) or DISABLE (false) QUERY REWRITE.</summary>
    public bool? QueryRewrite { get; set; }
    public bool Compile { get; set; }
    public bool ConsiderFresh { get; set; }
    public bool ShrinkSpace { get; set; }
    public bool ShrinkCompact { get; set; }
    public bool ShrinkCascade { get; set; }
    public List<PhysicalAttribute> Attributes { get; set; } = [];
}
