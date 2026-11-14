namespace OracleSqlParser;

// ---------- ANALYZE ----------

public enum AnalyzeTarget { Table, Index, Cluster }
public enum AnalyzeAction { ComputeStatistics, EstimateStatistics, DeleteStatistics, ValidateStructure, ListChainedRows, Other }

/// <summary>
/// ANALYZE TABLE|INDEX|CLUSTER name [PARTITION (p)] action. Clauses not modeled (FOR ..., VALIDATE REF UPDATE, ...) stay
/// in <see cref="OtherClauses"/> as a token tree, and <see cref="Action"/> is Other when the action itself is not modeled.
/// </summary>
public sealed partial class AnalyzeStatement : Statement
{
    public AnalyzeTarget Target { get; set; }
    public List<Identifier> Name { get; set; } = [];
    /// <summary>PARTITION (p) or SUBPARTITION (s) target.</summary>
    public PartitionReference? Partition { get; set; }
    public AnalyzeAction Action { get; set; }
    /// <summary>COMPUTE SYSTEM STATISTICS.</summary>
    public bool SystemStatistics { get; set; }
    /// <summary>ESTIMATE STATISTICS SAMPLE n: the sample size.</summary>
    public Expression? Sample { get; set; }
    /// <summary>SAMPLE n PERCENT (true) or SAMPLE n ROWS (false).</summary>
    public bool SamplePercent { get; set; }
    /// <summary>VALIDATE STRUCTURE CASCADE.</summary>
    public bool Cascade { get; set; }
    /// <summary>VALIDATE STRUCTURE ONLINE (true) or OFFLINE (false).</summary>
    public bool? Online { get; set; }
    /// <summary>LIST CHAINED ROWS INTO table.</summary>
    public List<Identifier> IntoTable { get; set; } = [];
    /// <summary>Trailing clauses without a dedicated node (FOR ..., VALIDATE REF UPDATE, ...), as a token tree.</summary>
    public List<SyntaxItem> OtherClauses { get; set; } = [];
}
