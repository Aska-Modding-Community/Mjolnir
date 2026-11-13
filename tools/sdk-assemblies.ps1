# The SDK and its modules, each from its own project's bin; all of them ship to Mjolnir\core, where Core treats them as
# shared (one copy for every mod) and Mjolnir.SDK finds its modules (SdkModules). The .xml next to each is its
# documentation, for mods built against Mjolnir\core (the game never reads it). Dot-sourced by deploy.ps1 and
# package.ps1, so a new module is listed once.
$sdkAssemblies = @(
    @{ Project = 'Mjolnir.SDK'; Files = @('Mjolnir.SDK.dll', 'Mjolnir.SDK.pdb', 'Mjolnir.SDK.xml', 'Tomlyn.dll') },
    @{ Project = 'Mjolnir.SDK.UI'; Files = @('Mjolnir.SDK.UI.dll', 'Mjolnir.SDK.UI.pdb', 'Mjolnir.SDK.UI.xml') },
    @{ Project = 'Mjolnir.SDK.Player'; Files = @('Mjolnir.SDK.Player.dll', 'Mjolnir.SDK.Player.pdb', 'Mjolnir.SDK.Player.xml') },
    @{ Project = 'Mjolnir.SDK.Content'; Files = @('Mjolnir.SDK.Content.dll', 'Mjolnir.SDK.Content.pdb', 'Mjolnir.SDK.Content.xml') },
    @{ Project = 'Mjolnir.SDK.AI'; Files = @('Mjolnir.SDK.AI.dll', 'Mjolnir.SDK.AI.pdb', 'Mjolnir.SDK.AI.xml') },
    @{ Project = 'Mjolnir.SDK.World'; Files = @('Mjolnir.SDK.World.dll', 'Mjolnir.SDK.World.pdb', 'Mjolnir.SDK.World.xml') },
    @{ Project = 'Mjolnir.SDK.Stats'; Files = @('Mjolnir.SDK.Stats.dll', 'Mjolnir.SDK.Stats.pdb', 'Mjolnir.SDK.Stats.xml') },
    @{ Project = 'Mjolnir.SDK.Net'; Files = @('Mjolnir.SDK.Net.dll', 'Mjolnir.SDK.Net.pdb', 'Mjolnir.SDK.Net.xml') },
    @{ Project = 'Mjolnir.SDK.Combat'; Files = @('Mjolnir.SDK.Combat.dll', 'Mjolnir.SDK.Combat.pdb', 'Mjolnir.SDK.Combat.xml') },
    @{ Project = 'Mjolnir.SDK.Camera'; Files = @('Mjolnir.SDK.Camera.dll', 'Mjolnir.SDK.Camera.pdb', 'Mjolnir.SDK.Camera.xml') },
    @{ Project = 'Mjolnir.SDK.Terrain'; Files = @('Mjolnir.SDK.Terrain.dll', 'Mjolnir.SDK.Terrain.pdb', 'Mjolnir.SDK.Terrain.xml') },
    # bundles\masks: Mjolnir's own avatar masks (AnimationMask.UpperBody), built by the Mjolnir kit.
    @{ Project = 'Mjolnir.SDK.Animation'; Files = @('Mjolnir.SDK.Animation.dll', 'Mjolnir.SDK.Animation.pdb', 'Mjolnir.SDK.Animation.xml', 'bundles\masks') }
)
