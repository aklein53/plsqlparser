namespace OracleSqlParser;

/// <summary>One property of a node as seen by the diagnostic <see cref="AstPrinter"/> and <see cref="AstJson"/>; produced by
/// generated <c>NodeProperties()</c> overrides instead of reflection, so both stay trim- and Native-AOT-safe.</summary>
internal readonly record struct NodeProperty(string Name, object? Value, bool NullableBool = false, bool SpellingOnly = false);
