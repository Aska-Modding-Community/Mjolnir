using System.Collections.Generic;
using System.Globalization;
using Mjolnir.SDK.World;
using SandSailorStudio;
using SandSailorStudio.UI;
using SSSGame;
using UnityEngine;

namespace Mjolnir.UiLab;

/// <summary>
/// The "colors" scenario (autopilot.once containing "colors"): when the picture's colours change during a run. Screenshots in
/// the main menu, then every two seconds from the moment the player stands in the loaded world (the save's own hour logged with
/// each), then the clock set to 10:00 as the animation scenarios do, and screenshots after it at growing intervals. The shots'
/// brightness and tint are measured outside the game. Nothing else is done: no animation, no mod content.
/// </summary>
internal static partial class Autopilot
{
    private static IEnumerator<float> ColorsScenario()
    {
        foreach (float wait in WaitFor("main menu", () => MainMenuReady, 300)) yield return wait;
        yield return 2f;
        Shot("colors-menu-a");
        yield return 4f;
        Shot("colors-menu-b");
        yield return 1f;
        foreach (float wait in LoadSave(Menu.GetInstance<MainMenu>())) yield return wait;
        foreach (float wait in WaitFor("the player", () => GameManagers.Get<PlayerManager>()?.LocalPlayer?.playerCharacter != null, 120)) yield return wait;
        float since = Time.realtimeSinceStartup;
        for (int i = 0; i < 7; i++)
        {
            Log(string.Create(CultureInfo.InvariantCulture, $"Colors: {Time.realtimeSinceStartup - since:0.0} s in the world, hour {WorldTime.Hour:0.00}"));
            Shot($"colors-world-{i * 2:00}s");
            yield return 2f;
        }
        Log(string.Create(CultureInfo.InvariantCulture, $"Colors: the clock from {WorldTime.Hour:0.00} to 10:00 at {Time.realtimeSinceStartup - since:0.0} s"));
        WorldTime.Hour = 10;
        float set = Time.realtimeSinceStartup;
        foreach (float after in new[] { 0.5f, 1f, 2f, 4f, 8f, 12f })
        {
            while (Time.realtimeSinceStartup - set < after) yield return 0f;
            Log(string.Create(CultureInfo.InvariantCulture, $"Colors: {after:0.0} s after the clock was set, hour {WorldTime.Hour:0.00}"));
            Shot($"colors-after-{after * 10:000}");
        }
        yield return 1f;
        Log($"Autopilot done; {CursorProbe.Describe()}");
        if (QuitWhenDone) Application.Quit();
    }
}
