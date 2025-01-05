using UnityEngine;
using UnityEditor;

namespace Sloane.FakeVolumetricLightGenerator.Editor
{
    [CustomEditor(typeof(FakeVolumetricLightGenerator))]
    public class FakeVolumetricLightGeneratorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            FakeVolumetricLightGenerator generator = (FakeVolumetricLightGenerator)target;
            if (GUILayout.Button("Initialize Generator"))
            {
                generator.Generate();
            }
        }
    }
}