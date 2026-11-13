using System.Reflection;

namespace OracleSqlParser;

/// <summary>Reflection over AST properties, used only by the diagnostic <see cref="AstPrinter"/> and <see cref="AstJson"/> (not by traversal).</summary>
internal static class AstReflection
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, PropertyInfo[]> PropertyCache = new();

    /// <summary>Public instance properties declared below <see cref="SqlNode"/>, in declaration order (cached per type).</summary>
    internal static PropertyInfo[] NodeProperties(Type type) =>
        PropertyCache.GetOrAdd(type, t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.DeclaringType != typeof(SqlNode) && p.GetIndexParameters().Length == 0).ToArray());
}
