using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Mjolnir.ModKit
{
    /// <summary>
    /// The kit renders as the game does: HDRP for the project and every quality level (the game's pipeline is HDRP, its quality
    /// "High"), the linear color space HDRP needs (in gamma a mod's colours and textures look otherwise in the kit than in the
    /// game) and high-quality lightmap encoding for Windows, as HDRP's wizard asks. Done when the editor opens the project, and
    /// from the menu.
    /// </summary>
    [InitializeOnLoad]
    public static class KitSetup
    {
        private const string PipelinePath = "Assets/Mjolnir/Settings/HDRenderPipelineAsset.asset";

        static KitSetup() => EditorApplication.delayCall += () => Ensure(false);

        [MenuItem("Mjolnir/Set Up the Kit", priority = 100)]
        private static void EnsureFromMenu() => Ensure(true);

        /// <summary>Batch mode: <c>-executeMethod Mjolnir.ModKit.KitSetup.SetUpFromCommandLine</c>.</summary>
        public static void SetUpFromCommandLine() => Ensure(true);

        public static void Ensure(bool log)
        {
            var asset = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(PipelinePath);
            if (asset == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PipelinePath)!);
                asset = ScriptableObject.CreateInstance<HDRenderPipelineAsset>();
                AssetDatabase.CreateAsset(asset, PipelinePath);
            }
            bool changed = GraphicsSettings.defaultRenderPipeline != asset;
            if (changed) GraphicsSettings.defaultRenderPipeline = asset;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                if (QualitySettings.renderPipeline == asset) continue;
                QualitySettings.renderPipeline = asset;
                changed = true;
            }
            QualitySettings.SetQualityLevel(current, false);
            if (PlayerSettings.colorSpace != ColorSpace.Linear)
            {
                PlayerSettings.colorSpace = ColorSpace.Linear; // the editor reimports the textures
                changed = true;
            }
            changed |= HighQualityLightmaps();
            if (changed) AssetDatabase.SaveAssets();
            if (log || changed) Debug.Log($"Mjolnir kit: set up (HDRP {PipelinePath}, linear color space, high-quality lightmaps)");
        }

        // The platforms HDRP's wizard checks (HDWizard.IsLightmapCorrect): all of them High, else it keeps flagging the project
        // (it flagged the kit while only Windows 64-bit was set). A name this Unity lacks is skipped.
        private static readonly string[] WizardPlatforms =
            { "StandaloneWindows", "StandaloneWindows64", "StandaloneLinux64", "StandaloneOSX", "Android", "iOS", "Switch", "Switch2", "WSAPlayer" };

        // Unity has the lightmap encoding only in PlayerSettings' internal API, as HDRP's own wizard uses it.
        private static bool HighQualityLightmaps()
        {
            const BindingFlags any = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var get = typeof(PlayerSettings).GetMethod("GetLightmapEncodingQualityForPlatform", any, null, new[] { typeof(BuildTarget) }, null);
            var set = typeof(PlayerSettings).GetMethods(any).FirstOrDefault(m => m.Name == "SetLightmapEncodingQualityForPlatform" && m.GetParameters().Length == 2);
            if (get == null || set == null)
            {
                Debug.LogWarning("Mjolnir kit: this Unity has no lightmap encoding setting the kit knows; set it to High Quality in Project Settings > Player");
                return false;
            }
            var high = Enum.Parse(set.GetParameters()[1].ParameterType, "High");
            bool changed = false;
            foreach (string name in WizardPlatforms)
            {
                if (!Enum.TryParse(name, out BuildTarget target)) continue;
                if (Equals(get.Invoke(null, new object[] { target }), high)) continue;
                set.Invoke(null, new[] { target, high });
                changed = true;
            }
            return changed;
        }
    }
}
