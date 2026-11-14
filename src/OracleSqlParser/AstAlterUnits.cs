namespace OracleSqlParser;

// ---------- ALTER of compiled units ----------

/// <summary>The part of a package or type that <c>ALTER ... COMPILE</c> recompiles.</summary>
public enum CompilePart
{
    /// <summary>The package as a whole: its specification and body.</summary>
    Package,
    /// <summary>The specification (the <c>SPEC</c> part) only.</summary>
    Specification,
    /// <summary>The body only.</summary>
    Body,
}

/// <summary>
/// ALTER {PROCEDURE|FUNCTION|PACKAGE|TYPE|TRIGGER|LIBRARY|JAVA SOURCE|CLASS|RESOURCE|VIEW|SYNONYM} name COMPILE ... or
/// ... EDITIONABLE | NONEDITIONABLE. Compiler parameters reuse <see cref="SessionSetting"/> (name = value).
/// </summary>
public sealed partial class AlterCompileStatement : Statement
{
    /// <summary>The kind of object recompiled, such as a procedure, a function or a trigger.</summary>
    public ObjectKind Kind { get; set; }
    /// <summary>The object name parts, schema first when the name is qualified.</summary>
    public List<Identifier> Name { get; set; } = [];
    /// <summary><c>EDITIONABLE</c> or <c>NONEDITIONABLE</c>, or null when neither is written.</summary>
    public EditionableKind? Editionable { get; set; }
    /// <summary>True when <c>COMPILE</c> is written.</summary>
    public bool Compile { get; set; }
    /// <summary>True when <c>DEBUG</c> is written.</summary>
    public bool Debug { get; set; }
    /// <summary>The package or type-body part to compile, or null when the whole unit is compiled.</summary>
    public CompilePart? Part { get; set; }
    /// <summary>True when <c>REUSE SETTINGS</c> is written.</summary>
    public bool ReuseSettings { get; set; }
    /// <summary>The compiler parameters written as <c>name = value</c>; empty when none are written.</summary>
    public List<SessionSetting> Parameters { get; set; } = [];
}

/// <summary>ALTER TRIGGER name ENABLE | DISABLE | RENAME TO new_name.</summary>
public sealed partial class AlterTriggerStatement : Statement
{
    /// <summary>The trigger name parts, schema first when the name is qualified.</summary>
    public List<Identifier> Name { get; set; } = [];
    /// <summary>ENABLE (true) or DISABLE (false); null for <c>RENAME TO</c>.</summary>
    public bool? Enable { get; set; }
    /// <summary>The new trigger name for <c>RENAME TO</c>; null for the other forms.</summary>
    public Identifier? RenameTo { get; set; }
}

/// <summary>ALTER VIEW name ADD|MODIFY|DROP constraint ... or READ ONLY | READ WRITE. Constraint actions reuse <see cref="AlterTableAction"/>.</summary>
public sealed partial class AlterViewStatement : Statement
{
    /// <summary>The view name parts, schema first when the name is qualified.</summary>
    public List<Identifier> Name { get; set; } = [];
    /// <summary>The constraint actions (<c>ADD</c>, <c>MODIFY</c> or <c>DROP</c>) applied to the view, in source order.</summary>
    public List<AlterTableAction> Actions { get; set; } = [];
    /// <summary>READ ONLY (true) or READ WRITE (false).</summary>
    public bool? ReadOnly { get; set; }
}
