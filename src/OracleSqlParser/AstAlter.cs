namespace OracleSqlParser;

// ---------- ALTER INDEX ----------

public enum AlterIndexActionKind
{
    Rebuild, RenameTo, Unusable, Visibility, MonitoringUsage, Coalesce, EnableDisable, Attributes, ShrinkSpace, DeallocateUnused,
}

public sealed partial class AlterIndexStatement : Statement
{
    public List<Identifier> Name { get; set; } = [];
    public List<AlterIndexAction> Actions { get; set; } = [];
}

public sealed partial class AlterIndexAction : SqlNode
{
    public AlterIndexActionKind Kind { get; set; }
    /// <summary>REBUILD PARTITION p / SUBPARTITION s.</summary>
    public Identifier? Partition { get; set; }
    public bool Subpartition { get; set; }
    /// <summary>REVERSE (true) or NOREVERSE (false) on REBUILD.</summary>
    public bool? Reverse { get; set; }
    public bool Online { get; set; }
    public Identifier? NewName { get; set; }
    /// <summary>VISIBLE (true) or INVISIBLE (false).</summary>
    public bool? Visible { get; set; }
    /// <summary>MONITORING USAGE (true) or NOMONITORING USAGE (false).</summary>
    public bool? Monitoring { get; set; }
    /// <summary>COALESCE CLEANUP.</summary>
    public bool Cleanup { get; set; }
    /// <summary>COALESCE ONLY (clean up without coalescing blocks).</summary>
    public bool Only { get; set; }
    /// <summary>ENABLE (true) or DISABLE (false) for function-based indexes.</summary>
    public bool? Enabled { get; set; }
    /// <summary>SHRINK SPACE COMPACT.</summary>
    public bool Compact { get; set; }
    /// <summary>SHRINK SPACE CASCADE.</summary>
    public bool Cascade { get; set; }
    /// <summary>DEALLOCATE UNUSED KEEP n.</summary>
    public Expression? Keep { get; set; }
    public List<PhysicalAttribute> Attributes { get; set; } = [];
}

// ---------- ALTER SEQUENCE ----------

/// <summary>ALTER SEQUENCE; options are the same nodes as CREATE SEQUENCE (RESTART is an option name with no value).</summary>
public sealed partial class AlterSequenceStatement : Statement
{
    public List<Identifier> Name { get; set; } = [];
    public List<SequenceOption> Options { get; set; } = [];
}
