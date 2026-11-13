using System;
using Mjolnir.SDK.Threading;

namespace Mjolnir.SDK;

/// <summary>The skills of a character, as its window lists them.</summary>
public enum SkillKind
{
    WoodHarvest,
    StoneHarvest,
    Crafting,
    Gathering,
    Building,
    Firekeeping,
    Hunting,
    Skinning,
    Fishing,
    Blacksmithing,
    Carpentry,
    Farming,
    MeleeCombat,
    RangedCombat,
    Cooking,
    Shamanism,
    DogCombat,
}

/// <summary>The rank a skill's level gives (<see cref="Skill.Rank"/>).</summary>
public enum SkillRank
{
    /// <summary>Below 25.</summary>
    Novice,

    /// <summary>From 25.</summary>
    Apprentice,

    /// <summary>From 50.</summary>
    Adept,

    /// <summary>From 75.</summary>
    Master,
}

/// <summary>
/// One skill of a character (<see cref="CharacterSheet.Skill"/>). The game gives experience for what the character does, and
/// the level grows by one each time experience reaches the level's cost.
/// </summary>
public sealed class Skill
{
    private const int LevelsToARank = 25;
    private readonly ICharacterSource? _source;

    /// <summary>
    /// Raised on the main thread before the game gives a character experience, for every character and skill (compare
    /// <see cref="ExperienceGainingEventArgs.Sheet"/> and <see cref="ExperienceGainingEventArgs.Skill"/>). A handler can
    /// change the amount or cancel it. The sender is null.
    /// </summary>
    public static event EventHandler<ExperienceGainingEventArgs>? ExperienceGaining
    {
        add => CharacterSheet.AddGaining(value);
        remove => CharacterSheet.RemoveGaining(value);
    }

    internal Skill(SkillKind kind, ICharacterSource? source, Func<string, CharacterStat> stat)
    {
        Kind = kind;
        _source = source;
        var (level, multiplier, experience) = SkillNames.Of(kind);
        Level = stat(level);
        ExperienceMultiplier = stat(multiplier);
        Experience = stat(experience);
    }

    /// <summary>Which skill it is.</summary>
    public SkillKind Kind { get; }

    /// <summary>
    /// Its level, usually 0 to 100. <see cref="CharacterStat.Value"/> counts the character's perks, and
    /// <see cref="CharacterStat.BaseValue"/> is what it earned; both have modifiers as any attribute.
    /// </summary>
    public CharacterStat Level { get; }

    /// <summary>Experience towards the next level. It goes back near 0 at each level; the game keeps it hidden.</summary>
    public CharacterStat Experience { get; }

    /// <summary>What experience earned is multiplied by, 1 without bonus (the stars beside the skill in the character's window).</summary>
    public CharacterStat ExperienceMultiplier { get; }

    /// <summary>Its name in the player's language.</summary>
    public string DisplayName => Level.DisplayName;

    /// <summary>The rank <see cref="Level"/> gives.</summary>
    public SkillRank Rank => RankOf(Level.Value);

    /// <summary>
    /// Gives experience the game's way, as if the character had done something: <see cref="Skill.ExperienceGaining"/> is
    /// raised, the character's <see cref="ExperienceMultiplier"/> counts, levels come with their effects. False when the
    /// character is gone or nothing in the game trains this skill. Off the main thread it runs on the next frame and answers true.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The amount is not above 0.</exception>
    public bool AddExperience(float amount)
    {
        if (!float.IsFinite(amount) || amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), amount, "Experience given is above 0.");
        if (Dispatcher.IsMainThread) return _source is { IsAlive: true } source && source.AddExperience(Kind, amount);
        Dispatcher.Run(() => AddExperience(amount));
        return true;
    }

    /// <summary>The rank of a level, as the game's skill panel works it out.</summary>
    internal static SkillRank RankOf(float level) => (SkillRank)Math.Clamp((int)(level / LevelsToARank), 0, (int)SkillRank.Master);
}

/// <summary>The game's attributes of each skill, by asset name (a game rename is fixed here, not in mods).</summary>
internal static class SkillNames
{
    /// <summary>The level, the experience multiplier and the experience; "" where the game has none.</summary>
    public static (string Level, string Multiplier, string Experience) Of(SkillKind kind) => kind switch
    {
        SkillKind.WoodHarvest => ("Proficiency_WoodHarvest", "ExpMultiplier_WoodHarvest", "VariableAttribute_WoodExpertise"),
        SkillKind.StoneHarvest => ("Proficiency_StoneHarvest", "ExpMultiplier_StoneHarvest", "VariableAttribute_StoneExpertise"),
        SkillKind.Crafting => ("Proficiency_Crafting", "ExpMultiplier_Crafting", "VariableAttribute_CraftingExpertise"),
        SkillKind.Gathering => ("Proficiency_Gatherer", "ExpMultiplier_Gatherer", "VariableAttribute_GatheringExpertise"),
        SkillKind.Building => ("Proficiency_Building", "ExpMultiplier_Building", "VariableAttribute_BuildingExpertise"),
        SkillKind.Firekeeping => ("Proficiency_Firekeeping", "ExpMultiplier_Firekeeping", "VariableAttribute_FirekeepingExpertise"),
        SkillKind.Hunting => ("Proficiency_Hunting", "ExpMultiplier_Hunting", ""),
        SkillKind.Skinning => ("Proficiency_Skinning", "ExpMultiplier_Skinning", "VariableAttribute_SkinExpertise"),
        SkillKind.Fishing => ("Proficiency_Fishing", "ExpMultiplier_Fishing", "VariableAttribute_FishingExpertise"),
        SkillKind.Blacksmithing => ("Proficiency_Blacksmith", "ExpMultiplier_Blacksmith", "VariableAttribute_BlacksmithExpertise"),
        SkillKind.Carpentry => ("Proficiency_Carpentry", "ExpMultiplier_Carpentry", "VariableAttribute_CarpentryExpertise"),
        SkillKind.Farming => ("Proficiency_Farming", "ExpMultiplier_Farming", "VariableAttribute_FarmingExpertise"),
        SkillKind.MeleeCombat => ("Proficiency_MeleeCombat", "ExpMultiplier_MeleeCombat", "VariableAttribute_MeleeCombatExpertise"),
        SkillKind.RangedCombat => ("Proficiency_RangedCombat", "ExpMultiplier_RangedCombat", "VariableAttribute_RangedCombatExpertise"),
        SkillKind.Cooking => ("Proficiency_Cooking", "ExpMultiplier_Cooking", "VariableAttribute_CookingExpertise"),
        SkillKind.Shamanism => ("Proficiency_Shamanism", "ExpMultiplier_Shamanism", "VariableAttribute_ShamanicExpertise"),
        SkillKind.DogCombat => ("Proficiency_DogCombat", "", "VariableAttribute_DogCombatExpertise"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>The skill whose level is that attribute, null for any other.</summary>
    public static SkillKind? ByLevel(string assetName)
    {
        foreach (var kind in Enum.GetValues<SkillKind>())
        {
            if (Of(kind).Level == assetName) return kind;
        }
        return null;
    }
}
