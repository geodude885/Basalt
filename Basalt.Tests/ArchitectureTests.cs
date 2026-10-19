using System.Reflection;
using Xunit;

namespace Basalt.Tests;

/// <summary>
/// Keeps what <c>Services/</c> types hold free of game types, so they can be built and tested without RUMBLE.
/// Methods may take game types (a conversion from a <c>Transform</c>, say); only fields and properties are checked.
/// </summary>
public class ArchitectureTests
{
    private const string ServicesNamespace = nameof(Basalt) + "." + nameof(Services);

    private static readonly string[] GameAssemblyPrefixes =
    [
        "UnityEngine",
        "Il2Cpp",
        "Photon",
        "RumbleModdingAPI",
        "UIFramework",
    ];

    [Fact]
    public void ServicesDoNotTouchTheGame()
    {
        var offenders = new List<string>();

        foreach (var type in typeof(Services.Charts.ChartRunner).Assembly.GetTypes())
        {
            if (type.Namespace is null) continue;
            if (!type.Namespace.StartsWith(ServicesNamespace, StringComparison.Ordinal)) continue;

            foreach (var referenced in HeldTypes(type))
            {
                var assembly = referenced.Assembly.GetName().Name;
                if (assembly is null) continue;

                if (GameAssemblyPrefixes.Any(p => assembly.StartsWith(p, StringComparison.Ordinal)))
                {
                    offenders.Add($"{type.FullName} references {referenced.FullName} from {assembly}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Services types must hold no game types, so they can be built in tests:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, offenders.Distinct()));
    }

    private static IEnumerable<Type> HeldTypes(Type type)
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic
                                 | BindingFlags.Instance | BindingFlags.Static
                                 | BindingFlags.DeclaredOnly;

        if (type.BaseType is not null) yield return type.BaseType;

        foreach (var i in type.GetInterfaces()) yield return i;
        foreach (var f in type.GetFields(All)) yield return f.FieldType;
        foreach (var p in type.GetProperties(All)) yield return p.PropertyType;
    }
}
