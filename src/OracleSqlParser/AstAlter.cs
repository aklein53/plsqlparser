namespace OracleSqlParser;

// ---------- ALTER INDEX ----------

/// <summary>The kind of an <c>ALTER INDEX</c> action, one value per clause form.</summary>
public enum AlterIndexActionKind
{
    /// <summary><c>REBUILD</c>: recreates the index, optionally with new attributes or <c>ONLINE</c>.</summary>
    Rebuild,
    /// <summary><c>RENAME TO</c>: gives the index a new name.</summary>
    RenameTo,
    /// <summary><c>UNUSABLE</c>: marks the index, or one of its partitions, unusable.</summary>
    Unusable,
    /// <summary><c>VISIBLE</c> or <c>INVISIBLE</c>.</summary>
    Visibility,
    /// <summary><c>MONITORING USAGE</c> or <c>NOMONITORING USAGE</c>.</summary>
    MonitoringUsage,
    /// <summary><c>COALESCE</c>: merges adjacent free index blocks to reclaim space.</summary>
    Coalesce,
    /// <summary><c>ENABLE</c> or <c>DISABLE</c> of a function-based index.</summary>
    EnableDisable,
    /// <summary>Changes physical attributes (for example <c>PCTFREE 10</c> or <c>STORAGE (...)</c>) without rebuilding.</summary>
    Attributes,
    /// <summary><c>SHRINK SPACE</c>: compacts the index segment and lowers the high-water mark.</summary>
    ShrinkSpace,
    /// <summary><c>DEALLOCATE UNUSED</c>: releases index space above the high-water mark.</summary>
    DeallocateUnused,
}

/// <summary><c>ALTER INDEX name action [, action]...</c>, for example <c>ALTER INDEX ix REBUILD ONLINE</c>.</summary>
public sealed partial class AlterIndexStatement : Statement
{
    /// <summary>The index name parts, schema first when the name is qualified.</summary>
    public List<Identifier> Name { get; set; } = [];
    /// <summary>The actions applied by the statement, in source order.</summary>
    public List<AlterIndexAction> Actions { get; set; } = [];
}

/// <summary>One action of an <c>ALTER INDEX</c> statement, such as <c>REBUILD PARTITION p1 ONLINE</c>.</summary>
public sealed partial class AlterIndexAction : SqlNode
{
    /// <summary>The clause this action performs.</summary>
    public AlterIndexActionKind Kind { get; set; }
    /// <summary>REBUILD PARTITION p / SUBPARTITION s.</summary>
    public Identifier? Partition { get; set; }
    /// <summary>True when the action names a <c>SUBPARTITION</c> rather than a <c>PARTITION</c>.</summary>
    public bool Subpartition { get; set; }
    /// <summary>REVERSE (true) or NOREVERSE (false) on REBUILD; null when neither is written.</summary>
    public bool? Reverse { get; set; }
    /// <summary>True when <c>ONLINE</c> is written.</summary>
    public bool Online { get; set; }
    /// <summary>The new name for <c>RENAME TO</c>; null for every other action.</summary>
    public Identifier? NewName { get; set; }
    /// <summary>VISIBLE (true) or INVISIBLE (false); null for the other actions.</summary>
    public bool? Visible { get; set; }
    /// <summary>MONITORING USAGE (true) or NOMONITORING USAGE (false); null for the other actions.</summary>
    public bool? Monitoring { get; set; }
    /// <summary>COALESCE CLEANUP.</summary>
    public bool Cleanup { get; set; }
    /// <summary>COALESCE ONLY (clean up without coalescing blocks).</summary>
    public bool Only { get; set; }
    /// <summary>ENABLE (true) or DISABLE (false) for function-based indexes; null for the other actions.</summary>
    public bool? Enabled { get; set; }
    /// <summary>SHRINK SPACE COMPACT.</summary>
    public bool Compact { get; set; }
    /// <summary>SHRINK SPACE CASCADE.</summary>
    public bool Cascade { get; set; }
    /// <summary>DEALLOCATE UNUSED KEEP n.</summary>
    public Expression? Keep { get; set; }
    /// <summary>The physical attributes given with the action; empty when none are written.</summary>
    public List<PhysicalAttribute> Attributes { get; set; } = [];
}

// ---------- ALTER SEQUENCE ----------

/// <summary>ALTER SEQUENCE; options are the same nodes as CREATE SEQUENCE (RESTART is an option name with no value).</summary>
public sealed partial class AlterSequenceStatement : Statement
{
    /// <summary>The sequence name parts, schema first when the name is qualified.</summary>
    public List<Identifier> Name { get; set; } = [];
    /// <summary>The options changed by the statement, such as <c>INCREMENT BY 2</c> or <c>CACHE 20</c>, in source order.</summary>
    public List<SequenceOption> Options { get; set; } = [];
}
