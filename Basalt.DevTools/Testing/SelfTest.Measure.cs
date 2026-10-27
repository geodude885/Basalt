using System.Collections;
using System.Globalization;
using System.Text;
using Il2CppRUMBLE.Managers;
using MelonLoader.Utils;
using UnityEngine;

namespace Basalt.DevTools.Testing;

/// <summary>
/// <c>--basalt-selftest=measure</c>: writes each throwable structure's collider size, posed as
/// a projectile, to <c>SelfTest/structures.json</c> for the editor's top-down view.
/// </summary>
internal static partial class SelfTest
{
    private static readonly string[] Measured = { "Disc", "Ball", "Pillar", "Wall", "RockCube", "BoulderBall", "SmallRock", "LargeRock" };

    private static IEnumerator RunMeasure(List<string> problems)
    {
        Vector3 spot = new(0f, 50f, 0f);
        var json = new StringBuilder("{\n");
        var lines = new List<string>();

        foreach (var name in Measured)
        {
            var pooled = PoolManager.instance.GetPooledObject(name, spot, Quaternion.LookRotation(Vector3.back));
            if (pooled == null)
            {
                problems.Add($"no pool called {name}");
                continue;
            }

            var body = pooled.GetComponent<Rigidbody>();
            body?.isKinematic = true;
            pooled.transform.SetPositionAndRotation(spot, Quaternion.LookRotation(Vector3.back));
            yield return null;
            Physics.SyncTransforms();

            Bounds? all = null;
            foreach (var c in pooled.GetComponentsInChildren<Collider>())
            {
                if (c == null || !c.enabled || c.isTrigger) continue;
                if (all is { } b)
                {
                    b.Encapsulate(c.bounds);
                    all = b;
                }
                else
                {
                    all = c.bounds;
                }
            }

            if (all is not { } size)
            {
                problems.Add($"{name}: no solid collider");
                continue;
            }

            // Looking down -z: x is across the lane, y up, z along it.
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"  \"{name}\": {{ \"across\": {size.size.x:0.###}, \"up\": {size.size.y:0.###}, \"along\": {size.size.z:0.###}, \"pivotUp\": {spot.y - size.min.y:0.###} }}"));
            Logger.Info($"[SelfTest] measure {name}: {size.size.x:0.00} across, {size.size.y:0.00} up, {size.size.z:0.00} along");

            body?.isKinematic = false;
            pooled.gameObject.SetActive(false);
        }

        json.Append(string.Join(",\n", lines)).Append("\n}\n");
        File.WriteAllText(Path.Combine(MelonEnvironment.UserDataDirectory, "Basalt", "SelfTest", "structures.json"), json.ToString());
    }
}
