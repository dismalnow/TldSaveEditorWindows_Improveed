using System.ComponentModel;
using Newtonsoft.Json.Linq;

namespace TldSave.Core;

/// <summary>
/// Player status, skills and afflictions, read and written directly in the save's "global" section.
/// Every global.m_*Serialized field used here is a JSON string; changes are written back immediately.
///
/// Meaning of the meters (checked against a brand-new save, where all three are ~0):
///   thirst / fatigue / freezing: 0 = best, 100 = worst
///   calories: reserve kcal, a new game starts around 2000
/// </summary>
public sealed class PlayerState : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly SaveFile save;
    private JObject Global => save.Global;

    public IReadOnlyList<Skill> Skills { get; }
    public IReadOnlyList<Affliction> Afflictions { get; }

    public PlayerState(SaveFile save)
    {
        this.save = save;
        Skills = Skill.All.Select(def => new Skill(this, def)).ToList();
        Afflictions = Affliction.CreateAll(this);
    }

    // ------------------------------------------------------------------ meters

    private const string ConditionField = "m_Condition_Serialized";

    /// <summary>Health, 0..100 (105 while Well Fed).</summary>
    public double Condition
    {
        get => GetNumber(ConditionField, "m_CurrentHPProxy");
        set
        {
            SetValue(ConditionField, "m_CurrentHPProxy", ClampFinite(value, 0, ConditionMax));
            // The load menu shows this copy
            if (save.Info is { } info) info["m_Condition"] = Condition;
        }
    }

    public double ConditionMax => WellFed ? 105 : 100;

    public const double MaxCalories = 2500;

    public double Calories
    {
        get => GetNumber("m_Hunger_Serialized", "m_CurrentReserveCaloriesProxy");
        set => SetValue("m_Hunger_Serialized", "m_CurrentReserveCaloriesProxy", ClampFinite(value, 0, MaxCalories));
    }

    public double Thirst
    {
        get => GetNumber("m_Thirst_Serialized", "m_CurrentThirstProxy");
        set => SetValue("m_Thirst_Serialized", "m_CurrentThirstProxy", ClampFinite(value, 0, 100));
    }

    public double Fatigue
    {
        get => GetNumber("m_Fatigue_Serialized", "m_CurrentFatigueProxy");
        set => SetValue("m_Fatigue_Serialized", "m_CurrentFatigueProxy", ClampFinite(value, 0, 100));
    }

    public double Freezing
    {
        get => GetNumber("m_Freezing_Serialized", "m_CurrentFreezingProxy");
        set => SetValue("m_Freezing_Serialized", "m_CurrentFreezingProxy", ClampFinite(value, 0, 100));
    }

    /// <summary>Well Fed buff (+5 max condition). The game grants it after 72 h without starving.</summary>
    public bool WellFed
    {
        get => GetBool("m_WellFedSerialized", "m_Active");
        set
        {
            var wellFed = Read("m_WellFedSerialized");
            wellFed["m_Active"] = value;
            // A missing field means 0 hours: set it too, or the game could drop the buff for not having eaten enough
            if (value && ((double?)wellFed["m_ElapsedHoursNotStarving"] ?? 0) < 72) wellFed["m_ElapsedHoursNotStarving"] = 72.0;
            Write("m_WellFedSerialized", wellFed);
            if (!value && Condition > 100) Condition = 100;
        }
    }

    public bool Invulnerable
    {
        get => GetBool(ConditionField, "m_Invulnerable");
        set => SetValue(ConditionField, "m_Invulnerable", value);
    }

    public bool NeverDie
    {
        get => GetBool(ConditionField, "m_NeverDieProxy");
        set => SetValue(ConditionField, "m_NeverDieProxy", value);
    }

    /// <summary>Full health, no hunger, thirst, fatigue or cold.</summary>
    public void RestoreAll()
    {
        Calories = MaxCalories;
        Thirst = 0;
        Fatigue = 0;
        Freezing = 0;
        Condition = ConditionMax;
    }

    public void MaxAllSkills()
    {
        foreach (var skill in Skills.Where(s => s.Exists)) skill.Points = skill.MaxPoints;
    }

    public void CureAll()
    {
        foreach (var affliction in Afflictions) affliction.Cure();
    }

    // ------------------------------------------------------------------ JSON helpers

    internal JObject Read(string field) => Global.ReadNested(field) as JObject ?? new JObject();

    internal void Write(string field, JObject value)
    {
        Global.WriteNested(field, value);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    private double GetNumber(string field, string key) => (double?)Read(field)[key] ?? 0;
    private bool GetBool(string field, string key) => (bool?)Read(field)[key] ?? false;

    // Validate only explicit edits. Existing non-finite tokens elsewhere in a save must be preserved.
    private static double ClampFinite(double value, double minimum, double maximum)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), "Enter a finite number.");
        return Math.Clamp(value, minimum, maximum);
    }

    private void SetValue(string field, string key, JToken value)
    {
        var obj = Read(field);
        obj[key] = value;
        Write(field, obj);
    }
}

public sealed class Skill : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    internal sealed record Definition(string Name, string Field, int MaxPoints, int[]? LevelThresholds);

    // Max points match the values stored by maxed-out skills in real saves.
    // Per-level thresholds are only listed where a source gives them (TLD wiki).
    internal static readonly Definition[] All =
    [
        new("Fire starting", "m_Skill_FirestartingSerialized", 200, [0, 20, 50, 100, 200]),
        new("Carcass harvesting", "m_Skill_CarcassHarvestingSerialized", 100, null),
        new("Cooking", "m_Skill_CookingSerialized", 350, [0, 25, 75, 150, 350]),
        new("Ice fishing", "m_Skill_IceFishingSerialized", 250, null),
        new("Rifle", "m_Skill_RifleSerialized", 100, null),
        new("Archery", "m_Skill_ArcherySerialized", 125, null),
        new("Mending", "m_Skill_ClothingRepairSerialized", 350, null),
        new("Revolver", "m_Skill_RevolverSerialized", 100, null),
        new("Gunsmithing", "m_Skill_GunsmithingSerialized", 100, null),
    ];

    private const string ManagerField = "m_SkillsManagerSerialized";

    private readonly PlayerState player;
    private readonly Definition definition;

    internal Skill(PlayerState player, Definition definition)
    {
        this.player = player;
        this.definition = definition;
        player.PropertyChanged += (_, _) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    public string Name => definition.Name;
    public int MaxPoints => definition.MaxPoints;

    /// <summary>False when the save has no editable object for this skill (e.g. DLC skills in an old save).</summary>
    public bool Exists => ReadSkill() is not null;

    private JObject? ReadSkill() => player.Read(ManagerField).ReadNested(definition.Field) as JObject;

    public int Points
    {
        get => (int?)(ReadSkill()?["m_Points"]) ?? 0;
        set
        {
            var manager = player.Read(ManagerField);
            // A bulk action or API caller must not invent a missing skill's save schema.
            if (manager.ReadNested(definition.Field) is not JObject skill) return;
            skill["m_Points"] = Math.Clamp(value, 0, MaxPoints);
            manager.WriteNested(definition.Field, skill);
            player.Write(ManagerField, manager);
        }
    }

    public string LevelText
    {
        get
        {
            if (Points >= MaxPoints) return "Level 5 (max)";
            if (definition.LevelThresholds is { } thresholds)
                return $"Level {thresholds.Count(t => Points >= t)}";
            return Points == 0 ? "Level 1" : "";
        }
    }
}

public sealed class Affliction : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly PlayerState player;
    private readonly string saveField;
    private readonly string[] flags; // bool fields meaning "active"; empty = list-based affliction

    private Affliction(PlayerState player, string name, string field, params string[] flags)
    {
        this.player = player;
        this.saveField = field;
        this.flags = flags;
        Name = name;
        player.PropertyChanged += (_, _) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    internal static IReadOnlyList<Affliction> CreateAll(PlayerState player) =>
    [
        // List-based: one entry per wound in parallel arrays (m_Locations, m_CausesLocIDs, ...)
        new(player, "Sprained ankle", "m_SprainedAnkleSerialized"),
        new(player, "Sprained wrist", "m_SprainedWristSerialized"),
        new(player, "Sprain pain", "m_SprainPainSerialized"),
        new(player, "Blood loss", "m_BloodLossSerialized"),
        new(player, "Broken rib", "m_BrokenRibSerialized"),
        new(player, "Infection", "m_InfectionSerialized"),
        new(player, "Infection risk", "m_InfectionRiskSerialized"),
        new(player, "Frostbite", "m_FrostbiteSerialized"),
        // Flag-based
        new(player, "Burns", "m_BurnsSerialized", "m_Active"),
        new(player, "Electrical burns", "m_BurnsElectricSerialized", "m_Active"),
        new(player, "Food poisoning", "m_FoodPoisoningSerialized", "m_Active"),
        new(player, "Dysentery", "m_DysenterySerialized", "m_Active"),
        new(player, "Hypothermia", "m_HypothermiaSerialized", "m_Active"),
        new(player, "Cabin fever", "m_CabinFeverSerialized", "m_Active", "m_RiskActive"),
        new(player, "Intestinal parasites", "m_IntestinalParasitesSerialized", "m_HasParasites", "m_HasParasiteRisk"),
    ];

    public string Name { get; }

    public int ActiveCount
    {
        get
        {
            var data = player.Read(saveField);
            if (flags.Length > 0) return flags.Count(f => (bool?)data[f] == true);
            return data.Properties()
                .Where(p => p.Value is JArray && p.Name != "m_LocationsCurrentFrostbiteDamage")
                .Select(p => ((JArray)p.Value).Count)
                .DefaultIfEmpty(0)
                .Max();
        }
    }

    public bool IsActive => ActiveCount > 0;

    public string StatusText => ActiveCount switch
    {
        0 => "—",
        1 => "Active",
        var n when flags.Length == 0 => $"Active ×{n}",
        _ => "Active",
    };

    public void Cure()
    {
        if (!IsActive) return;
        var data = player.Read(saveField);
        if (flags.Length > 0)
        {
            foreach (var flag in flags) data[flag] = false;
        }
        else
        {
            foreach (var property in data.Properties().Where(p => p.Value is JArray).ToList())
            {
                // Frostbite damage is one value per body part: zero it instead of emptying it
                property.Value = property.Name == "m_LocationsCurrentFrostbiteDamage"
                    ? new JArray(((JArray)property.Value).Select(_ => 0.0))
                    : new JArray();
            }
            if (data["m_Active"] is not null) data["m_Active"] = false;
        }
        player.Write(saveField, data);
    }
}
