namespace OracleSqlParser;

// ---------- ALTER of compiled units ----------

/// <summary>Which part of a package or type COMPILE recompiles.</summary>
public enum CompilePart { Package, Specification, Body }

/// <summary>
/// ALTER {PROCEDURE|FUNCTION|PACKAGE|TYPE|TRIGGER|LIBRARY|JAVA SOURCE|CLASS|RESOURCE|VIEW|SYNONYM} name COMPILE ... or
/// ... EDITIONABLE | NONEDITIONABLE. Compiler parameters reuse <see cref="SessionSetting"/> (name = value).
/// </summary>
public sealed partial class AlterCompileStatement : Statement
{
    public ObjectKind Kind { get; set; }
    public List<Identifier> Name { get; set; } = [];
    public EditionableKind? Editionable { get; set; }
    public bool Compile { get; set; }
    public bool Debug { get; set; }
    public CompilePart? Part { get; set; }
    public bool ReuseSettings { get; set; }
    public List<SessionSetting> Parameters { get; set; } = [];
}

/// <summary>ALTER TRIGGER name ENABLE | DISABLE | RENAME TO new_name.</summary>
public sealed partial class AlterTriggerStatement : Statement
{
    public List<Identifier> Name { get; set; } = [];
    /// <summary>ENABLE (true) or DISABLE (false).</summary>
    public bool? Enable { get; set; }
    public Identifier? RenameTo { get; set; }
}

/// <summary>ALTER VIEW name ADD|MODIFY|DROP constraint ... or READ ONLY | READ WRITE. Constraint actions reuse <see cref="AlterTableAction"/>.</summary>
public sealed partial class AlterViewStatement : Statement
{
    public List<Identifier> Name { get; set; } = [];
    public List<AlterTableAction> Actions { get; set; } = [];
    /// <summary>READ ONLY (true) or READ WRITE (false).</summary>
    public bool? ReadOnly { get; set; }
}
