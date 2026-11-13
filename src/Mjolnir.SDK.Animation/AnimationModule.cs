using Mjolnir.SDK;
using Mjolnir.SDK.Animation;

[assembly: SdkModule(typeof(AnimationModule))]

namespace Mjolnir.SDK.Animation;

/// <summary>
/// The animation module's entry: Mjolnir.SDK starts it in phase A. From then on <see cref="CharacterAnimator"/> plays on the
/// game's characters, <see cref="Animations"/> lists the game's clips, the skeletons are written for the Mjolnir kit as the
/// characters appear, and the console gets <c>anims</c>, <c>anim</c>, <c>animstop</c> and <c>emote</c>. The player's
/// movement is patched only once an animation holds it.
/// </summary>
internal sealed class AnimationModule : ISdkModule
{
    public void Start()
    {
        GameAnimations.Connect();
        AnimationCommands.Register();
        // The characters' models for the Mjolnir kit, beside their skeletons: only when the player asks (a mod maker); turned
        // on during a game, they are written at once.
        BodyModels.Setting = PlatformConfig.Config.Bind("Kit", "Models", false,
            "Writes the characters' models and textures to Mjolnir/ModKit for the Mjolnir kit's previews (about 160 MB, only on this computer).");
        BodyModels.Setting.Changed += (_, e) =>
        {
            if (e.NewValue) GameAnimations.LookForRigsAgain();
        };
        // The game's own clips for the kit, read on its skeletons once they are written (in the main menu).
        GameClips.Setting = PlatformConfig.Config.Bind("Kit", "Animations", false,
            "Writes the game's animation clips of the people to Mjolnir/ModKit for the Mjolnir kit's previews (about 50 MB, only on this computer).");
        GameClips.Setting.Changed += (_, e) =>
        {
            if (!e.NewValue) return;
            GameClips.Again();
            GameAnimations.LookForRigsAgain();
        };
    }
}
