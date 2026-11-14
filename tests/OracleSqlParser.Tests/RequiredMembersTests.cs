using System.Reflection;
using System.Runtime.CompilerServices;
using OracleSqlParser;
using Xunit;

namespace OracleSqlParser.Tests;

/// <summary>
/// Keeps mandatory children honest: a non-nullable reference property on an AST node must either be
/// <c>required</c> (so a hand-built tree fails to compile without it), or start out non-null (for example a list
/// initialised to <c>[]</c>), or be listed in <see cref="Allowed"/> with a reason.
/// </summary>
public class RequiredMembersTests
{
    /// <summary>
    /// Properties that are non-nullable reference types but deliberately neither <c>required</c> nor non-null at
    /// construction, as <c>"Type.Property"</c>. Every entry needs a reason; keep this empty unless there is one.
    /// </summary>
    private static readonly HashSet<string> Allowed = [];

    private static IEnumerable<Type> NodeTypes() =>
        typeof(SqlNode).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(SqlNode).IsAssignableFrom(t))
            .OrderBy(t => t.FullName, StringComparer.Ordinal);

    private static IEnumerable<PropertyInfo> OwnProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.DeclaringType != typeof(SqlNode) && p.GetIndexParameters().Length == 0);

    private static bool IsNonNullableReference(PropertyInfo p) =>
        !p.PropertyType.IsValueType
        && new NullabilityInfoContext().Create(p).ReadState == NullabilityState.NotNull;

    [Fact]
    public void NonNullableReferenceProperties_AreRequiredOrNonNullAtConstruction()
    {
        var problems = new List<string>();
        foreach (var type in NodeTypes())
        {
            var instance = (SqlNode)Activator.CreateInstance(type)!;
            foreach (var p in OwnProperties(type).Where(IsNonNullableReference))
            {
                var key = $"{type.Name}.{p.Name}";
                if (Allowed.Contains(key)) continue;
                if (p.GetCustomAttribute<RequiredMemberAttribute>() is not null) continue;
                if (p.GetValue(instance) is not null) continue;
                problems.Add(key);
            }
        }
        Assert.True(problems.Count == 0,
            "Non-nullable reference properties that are neither required nor non-null at construction (make them required, " +
            "initialise them, or document them in Allowed): " + string.Join(", ", problems));
    }

    [Fact]
    public void RequiredProperties_AreNotNullableReferences()
    {
        var problems = new List<string>();
        foreach (var type in NodeTypes())
            foreach (var p in OwnProperties(type))
            {
                if (p.GetCustomAttribute<RequiredMemberAttribute>() is null) continue;
                if (p.PropertyType.IsValueType) continue;
                if (new NullabilityInfoContext().Create(p).ReadState != NullabilityState.NotNull)
                    problems.Add($"{type.Name}.{p.Name}");
            }
        Assert.True(problems.Count == 0, "Required properties must be non-nullable: " + string.Join(", ", problems));
    }

    [Fact]
    public void Allowed_EntriesStillExistAndAreNonNullableReferences()
    {
        var known = NodeTypes().ToDictionary(t => t.Name, t => t);
        foreach (var entry in Allowed)
        {
            var dot = entry.IndexOf('.');
            Assert.True(dot > 0, $"Allowed entry '{entry}' must be Type.Property.");
            Assert.True(known.TryGetValue(entry[..dot], out var type), $"Allowed entry '{entry}' names an unknown node type.");
            var prop = OwnProperties(type!).FirstOrDefault(p => p.Name == entry[(dot + 1)..]);
            Assert.True(prop is not null && IsNonNullableReference(prop), $"Allowed entry '{entry}' is not a non-nullable reference property.");
        }
    }
}
