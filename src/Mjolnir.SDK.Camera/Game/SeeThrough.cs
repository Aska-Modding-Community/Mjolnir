using System;
using UnityEngine;

namespace Mjolnir.SDK.Camera;

/// <summary>
/// The vegetation's see-through. The game keeps its player's character in sight: each frame <c>PlayerEntity.LateUpdate</c>
/// writes the character's place into the global shader vector <c>Global_PlayerPos</c>, and the vegetation's shaders draw
/// what stands between the camera and that point see-through — a tree drawn by a game object dithered, one drawn by the
/// vegetation system not at all. Under a mod's camera far from the character that strip crosses the view: trees in it were
/// checkered or gone. While a mod's camera that neither follows nor looks at the character is shown, the point is the
/// output camera's own place, written after the game's, so nothing lies between.
/// </summary>
internal sealed partial class GameCameraSource
{
    private const string CharacterPoint = "Global_PlayerPos";

    private static readonly Func<Transform?, bool> IsCharacter = IsPartOfCharacter;
    private static Transform? s_character;

    /// <summary>After the game wrote the character's place for the vegetation: the camera's own while a mod's camera looks elsewhere.</summary>
    internal static void AfterCharacterPoint()
    {
        if (CameraDirector.Shown == null || CameraDirector.Camera is not GameCameraSource source || source._output == null) return;
        s_character = LocalPlayerRoot();
        try
        {
            if (CameraDirector.SeesThroughToCharacter(IsCharacter)) return;
        }
        finally
        {
            s_character = null;
        }
        Shader.SetGlobalVector(CharacterPoint, source._output.transform.position);
    }

    private static bool IsPartOfCharacter(Transform? thing) =>
        thing != null && s_character != null && (thing.Pointer == s_character.Pointer || thing.IsChildOf(s_character));
}
