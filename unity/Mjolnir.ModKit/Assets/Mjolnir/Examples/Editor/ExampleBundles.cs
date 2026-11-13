using UnityEditor;

namespace Mjolnir.ModKit.Examples
{
    /// <summary>
    /// The examples' models made again (and Mjolnir's masks from the skeletons imported with Mjolnir > Rigs > Import from the
    /// Game, imported again when the game wrote them) and every bundle built, in one run: <c>-executeMethod
    /// Mjolnir.ModKit.Examples.ExampleBundles.BuildFromCommandLine</c> (tools/build-bundles.ps1); the editor exits 1 on a failure.
    /// </summary>
    public static class ExampleBundles
    {
        [MenuItem("Mjolnir/Examples/Make the Models and Build Bundles")]
        public static void Build()
        {
            MakeAll();
            BundleBuilder.Build();
        }

        public static void BuildFromCommandLine()
        {
            KitSetup.Ensure(false);
            MakeAll();
            EditorApplication.Exit(BundleBuilder.Build() > 0 ? 0 : 1);
        }

        private static void MakeAll()
        {
            TwineModel.Make();
            TwineSpoolModel.Make();
            AltarModel.Make();
            // The skeletons come from the player's game: without it the masks of the last build stay.
            RigImporter.Import(ask: false);
        }
    }
}
