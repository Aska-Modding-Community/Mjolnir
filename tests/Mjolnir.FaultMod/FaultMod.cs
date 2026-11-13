using System;
using Mjolnir.SDK;

namespace Mjolnir.FaultMod;

/// <summary>
/// Isolation and dependency test, deployed with deploy.ps1 -SmokeMod.
/// <list type="bullet">
/// <item>Its third OnUpdate throws on purpose: Core must disable this mod only, so its OnLateUpdate stops after the
/// fault while Mjolnir.SmokeMod keeps receiving callbacks.</item>
/// <item>It depends on SmokeMod (loaded first, although "faultmod" sorts before "smokemod") and optionally on a
/// mod that is not installed (must not stop it from loading).</item>
/// </list>
/// </summary>
[ModManifest("mjolnir.test.faultmod", "FaultMod", "0.1.0")]
[ModDependency("mjolnir.test.smokemod", ">=0.1")]
[ModDependency("mjolnir.test.absent", Optional = true)]
public sealed class FaultMod : Mod
{
    private int _update;
    private int _lateUpdate;

    public override void OnUpdate()
    {
        int n = ++_update;
        Log.Info($"OnUpdate #{n}");
        if (n == 3) throw new InvalidOperationException("deliberate fault (isolation test)");
    }

    public override void OnLateUpdate()
    {
        int n = ++_lateUpdate;
        if (n <= 5) Log.Info($"OnLateUpdate #{n}");
    }
}
