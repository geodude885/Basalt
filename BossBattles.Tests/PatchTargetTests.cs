using System.Reflection;
using BossBattles.Flatscreen;
using BossBattles.Patches;
using HarmonyLib;
using Xunit;

namespace BossBattles.Tests;

/// <summary>
/// Checks every trace patch against the interop assemblies: the target method still exists, and
/// each injected parameter is one the target really declares (Harmony binds by the game's names).
/// A game update that moves a target fails here instead of in the headset.
/// </summary>
public sealed class PatchTargetTests
{
    public static TheoryData<Type> Patches()
    {
        var data = new TheoryData<Type>();
        foreach (Type group in new[] { typeof(ActorPatches), typeof(ArenaPatches), typeof(FlatscreenControls) })
        {
            foreach (Type patch in group.GetNestedTypes(BindingFlags.Public)) data.Add(patch);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Patches))]
    public void PatchTargetExistsAndParametersBind(Type patch)
    {
        HarmonyPatch attribute = patch.GetCustomAttribute<HarmonyPatch>()
                                 ?? throw new InvalidOperationException($"{patch.Name} has no [HarmonyPatch]");

        Type target = attribute.info.declaringType;
        var methodName = attribute.info.methodName;

        // A patch naming argument types picks one overload; otherwise the name must be unambiguous.
        Type[]? argumentTypes = attribute.info.argumentTypes;
        MethodInfo[] candidates = target
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => m.Name == methodName)
            .Where(m => argumentTypes is null || m.GetParameters().Select(p => p.ParameterType).SequenceEqual(argumentTypes))
            .ToArray();

        Assert.True(candidates.Length == 1, $"{target.Name}.{methodName}: expected one overload, found {candidates.Length}");

        var targetParameters = candidates[0].GetParameters().Select(p => p.Name!).ToHashSet(StringComparer.Ordinal);

        foreach (MethodInfo hook in patch.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name is "Prefix" or "Postfix"))
        {
            foreach (ParameterInfo parameter in hook.GetParameters())
            {
                if (parameter.Name!.StartsWith("__", StringComparison.Ordinal)) continue;

                Assert.True(targetParameters.Contains(parameter.Name),
                    $"{patch.Name}.{hook.Name}: '{parameter.Name}' is not a parameter of {target.Name}.{methodName}({string.Join(", ", targetParameters)})");
            }
        }
    }
}
