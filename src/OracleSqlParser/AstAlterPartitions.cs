namespace OracleSqlParser;

/// <summary>
/// One partition or subpartition named in ALTER TABLE partition maintenance: PARTITION name, SUBPARTITION name, or
/// PARTITION FOR (value, ...) (in which case <see cref="ForValues"/> is set and <see cref="Name"/> is null).
/// </summary>
public sealed partial class PartitionReference : SqlNode
{
    public bool Subpartition { get; set; }
    public Identifier? Name { get; set; }
    public List<Expression> ForValues { get; set; } = [];
}
