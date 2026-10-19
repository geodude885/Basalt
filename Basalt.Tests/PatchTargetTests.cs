using System.Reflection;
using Basalt.DevTools.Flatscreen;
using Basalt.Patches;
using HarmonyLib;
using Xunit;

namespace Basalt.Tests;

/// <summary>
/// Every patch's target still exists in the interop assemblies, with the parameters it injects
/// (Harmony binds by the game's names), so a game update fails here instead of in the headset.
/// </summary>
public class PatchTargetTests
{
    public static TheoryData<Type> Patches()
    {
        var data = new TheoryData<Type>();
        foreach (var group in new[] { typeof(ActorPatches), typeof(ArenaPatches), typeof(FlatscreenControls) })
        {
            foreach (var patch in group.GetNestedTypes(BindingFlags.Public)) data.Add(patch);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Patches))]
    public void PatchTargetExistsAndParametersBind(Type patch)
    {
        var attribute = patch.GetCustomAttribute<HarmonyPatch>()
                                 ?? throw new InvalidOperationException($"{patch.Name} has no [HarmonyPatch]");

        var target = attribute.info.declaringType;
        var methodName = attribute.info.methodName;

        // A patch naming argument types picks one overload; otherwise the name must be unambiguous.
        var argumentTypes = attribute.info.argumentTypes;
        var candidates = target
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => m.Name == methodName)
            .Where(m => argumentTypes is null || m.GetParameters().Select(p => p.ParameterType).SequenceEqual(argumentTypes))
            .ToArray();

        Assert.True(candidates.Length == 1, $"{target.Name}.{methodName}: expected one overload, found {candidates.Length}");

        var targetParameters = candidates[0].GetParameters().Select(p => p.Name!).ToHashSet(StringComparer.Ordinal);

        foreach (var hook in patch.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name is "Prefix" or "Postfix"))
        {
            foreach (var parameter in hook.GetParameters())
            {
                if (parameter.Name!.StartsWith("__", StringComparison.Ordinal)) continue;

                Assert.True(targetParameters.Contains(parameter.Name),
                    $"{patch.Name}.{hook.Name}: '{parameter.Name}' is not a parameter of {target.Name}.{methodName}({string.Join(", ", targetParameters)})");
            }
        }
    }
}
