namespace OracleSqlParser;

/// <summary>
/// One partition or subpartition named in ALTER TABLE partition maintenance: PARTITION name, SUBPARTITION name, or
/// PARTITION FOR (value, ...) (in which case <see cref="ForValues"/> is set and <see cref="Name"/> is null).
/// </summary>
public sealed partial class PartitionReference : SqlNode
{
    /// <summary>True when the reference names a subpartition (<c>SUBPARTITION</c>) rather than a partition.</summary>
    public bool Subpartition { get; set; }
    /// <summary>The partition or subpartition name, or null when the reference is given by <c>FOR (values)</c> instead.</summary>
    public Identifier? Name { get; set; }
    /// <summary>The partition key values of a <c>PARTITION FOR (values)</c> reference; empty when the reference is by name.</summary>
    public List<Expression> ForValues { get; set; } = [];
}
