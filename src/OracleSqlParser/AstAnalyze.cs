namespace OracleSqlParser;

// ---------- ANALYZE ----------

/// <summary>The kind of object that <c>ANALYZE</c> operates on.</summary>
public enum AnalyzeTarget
{
    /// <summary><c>ANALYZE TABLE</c>.</summary>
    Table,
    /// <summary><c>ANALYZE INDEX</c>.</summary>
    Index,
    /// <summary><c>ANALYZE CLUSTER</c>.</summary>
    Cluster,
}
/// <summary>The action that <c>ANALYZE</c> performs.</summary>
public enum AnalyzeAction
{
    /// <summary><c>COMPUTE STATISTICS</c>.</summary>
    ComputeStatistics,
    /// <summary><c>ESTIMATE STATISTICS</c>, optionally with <c>SAMPLE</c>.</summary>
    EstimateStatistics,
    /// <summary><c>DELETE STATISTICS</c>.</summary>
    DeleteStatistics,
    /// <summary><c>VALIDATE STRUCTURE</c>.</summary>
    ValidateStructure,
    /// <summary><c>LIST CHAINED ROWS</c>.</summary>
    ListChainedRows,
    /// <summary>An action without a dedicated value; the clauses are kept in <c>OtherClauses</c>.</summary>
    Other,
}

/// <summary>
/// ANALYZE TABLE|INDEX|CLUSTER name [PARTITION (p)] action. Clauses not modeled (FOR ..., VALIDATE REF UPDATE, ...) stay
/// in <see cref="OtherClauses"/> as a token tree, and <see cref="Action"/> is Other when the action itself is not modeled.
/// </summary>
public sealed partial class AnalyzeStatement : Statement
{
    /// <summary>The kind of object analyzed: <c>TABLE</c>, <c>INDEX</c> or <c>CLUSTER</c>.</summary>
    public AnalyzeTarget Target { get; set; }
    /// <summary>The object name parts, schema first when the name is qualified.</summary>
    public List<Identifier> Name { get; set; } = [];
    /// <summary>PARTITION (p) or SUBPARTITION (s) target.</summary>
    public PartitionReference? Partition { get; set; }
    /// <summary>The action performed, such as <c>COMPUTE STATISTICS</c>.</summary>
    public AnalyzeAction Action { get; set; }
    /// <summary>COMPUTE SYSTEM STATISTICS.</summary>
    public bool SystemStatistics { get; set; }
    /// <summary>ESTIMATE STATISTICS SAMPLE n: the sample size.</summary>
    public Expression? Sample { get; set; }
    /// <summary>SAMPLE n PERCENT (true) or SAMPLE n ROWS (false).</summary>
    public bool SamplePercent { get; set; }
    /// <summary>VALIDATE STRUCTURE CASCADE.</summary>
    public bool Cascade { get; set; }
    /// <summary>VALIDATE STRUCTURE ONLINE (true) or OFFLINE (false); null when neither is written.</summary>
    public bool? Online { get; set; }
    /// <summary>LIST CHAINED ROWS INTO table.</summary>
    public List<Identifier> IntoTable { get; set; } = [];
    /// <summary>Trailing clauses without a dedicated node (FOR ..., VALIDATE REF UPDATE, ...), as a token tree.</summary>
    public List<SyntaxItem> OtherClauses { get; set; } = [];
}
