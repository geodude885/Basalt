using System.Reflection;
using Xunit;

namespace BossBattles.Tests;

/// <summary>
/// Keeps <c>Services/</c> free of game types in its signatures, so it stays testable without RUMBLE.
/// Method bodies are not inspected: a tripwire, not a proof.
/// </summary>
public sealed class ArchitectureTests
{
    private const string ServicesNamespace = nameof(BossBattles) + "." + nameof(Services);

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

        foreach (Type type in typeof(Services.Charts.ChartRunner).Assembly.GetTypes())
        {
            if (type.Namespace is null) continue;
            if (!type.Namespace.StartsWith(ServicesNamespace, StringComparison.Ordinal)) continue;

            foreach (Type referenced in SignatureTypes(type))
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
            "Services must stay free of game types so they remain testable:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, offenders.Distinct()));
    }

    private static IEnumerable<Type> SignatureTypes(Type type)
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic
                                 | BindingFlags.Instance | BindingFlags.Static
                                 | BindingFlags.DeclaredOnly;

        if (type.BaseType is not null) yield return type.BaseType;

        foreach (Type i in type.GetInterfaces()) yield return i;
        foreach (FieldInfo f in type.GetFields(All)) yield return f.FieldType;
        foreach (PropertyInfo p in type.GetProperties(All)) yield return p.PropertyType;

        foreach (MethodInfo m in type.GetMethods(All))
        {
            yield return m.ReturnType;
            foreach (ParameterInfo p in m.GetParameters()) yield return p.ParameterType;
        }

        foreach (ConstructorInfo c in type.GetConstructors(All))
        {
            foreach (ParameterInfo p in c.GetParameters()) yield return p.ParameterType;
        }
    }
}
