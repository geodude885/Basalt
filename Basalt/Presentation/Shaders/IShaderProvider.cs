using Basalt.Services.Charts;
using UnityEngine;

namespace Basalt.Presentation.Shaders;

internal interface IShaderProvider
{
    float ShaderIntensity { get; set; }

    void PreloadPacks(IEnumerable<string> packNames);

    void ApplyLooksAt(IReadOnlyList<VisualCue> cues, double chartTime);

    void ReleaseAllPacks();

    void SetGlobal(string name, float value);

    void FireEvent(string name, Vector3 at, float strength, float seconds);
}
