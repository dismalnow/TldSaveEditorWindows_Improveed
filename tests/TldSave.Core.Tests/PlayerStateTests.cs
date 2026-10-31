using Newtonsoft.Json.Linq;

namespace TldSave.Core.Tests;

public class PlayerStateTests : IDisposable
{
    private readonly TestFiles files = new();
    public void Dispose() => files.Dispose();

    [Fact]
    public void Status_values_survive_a_save()
    {
        var path = files.Copy(TestFiles.Large);
        var save = SaveFile.Load(path);
        var player = new PlayerState(save);
        player.WellFed = false;
        player.Condition = 60;
        player.Calories = 500;
        player.Thirst = 80;
        player.Fatigue = 0;
        player.Freezing = 12.5;
        save.Save(path);

        var reloaded = new PlayerState(SaveFile.Load(path));
        Assert.Equal(60, reloaded.Condition);
        Assert.Equal(500, reloaded.Calories);
        Assert.Equal(80, reloaded.Thirst);
        Assert.Equal(0, reloaded.Fatigue);
        Assert.Equal(12.5, reloaded.Freezing);
        Assert.False(reloaded.WellFed);
    }

    [Fact]
    public void Values_are_clamped_to_their_range()
    {
        var player = new PlayerState(SaveFile.Load(TestFiles.Fixture(TestFiles.Small)));
        player.Thirst = 150;
        player.Calories = -10;
        player.WellFed = false;
        player.Condition = 500;
        Assert.Equal(100, player.Thirst);
        Assert.Equal(0, player.Calories);
        Assert.Equal(100, player.Condition);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Non_finite_meter_edits_are_rejected_without_changing_the_save(double value)
    {
        var save = SaveFile.Load(TestFiles.Fixture(TestFiles.Small));
        var player = new PlayerState(save);
        var originalGlobal = save.Global.DeepClone();
        var originalInfo = save.Info?.DeepClone();
        var changeRaised = false;
        player.PropertyChanged += (_, _) => changeRaised = true;
        Action<double>[] edits =
        [
            v => player.Condition = v,
            v => player.Calories = v,
            v => player.Thirst = v,
            v => player.Fatigue = v,
            v => player.Freezing = v,
        ];

        foreach (var edit in edits)
            Assert.Throws<ArgumentOutOfRangeException>(() => edit(value));

        Assert.True(JToken.DeepEquals(originalGlobal, save.Global));
        Assert.True(JToken.DeepEquals(originalInfo, save.Info));
        Assert.False(changeRaised);
    }

    [Fact]
    public void Existing_non_finite_tokens_survive_an_unrelated_player_edit()
    {
        var path = files.Copy(TestFiles.Small);
        var save = SaveFile.Load(path);
        var fatigue = (JObject)save.Global.ReadNested("m_Fatigue_Serialized")!;
        fatigue["m_CurrentFatigueProxy"] = double.NaN;
        fatigue["m_UnknownFutureValue"] = double.PositiveInfinity;
        save.Global.WriteNested("m_Fatigue_Serialized", fatigue);
        save.Save(path);

        save = SaveFile.Load(path);
        var player = new PlayerState(save);
        Assert.True(double.IsNaN(player.Fatigue));
        player.Thirst = 12.5;
        save.Save(path);

        var reloaded = SaveFile.Load(path);
        var reloadedFatigue = (JObject)reloaded.Global.ReadNested("m_Fatigue_Serialized")!;
        Assert.True(double.IsNaN((double)reloadedFatigue["m_CurrentFatigueProxy"]!));
        Assert.Equal(double.PositiveInfinity, (double)reloadedFatigue["m_UnknownFutureValue"]!);
        Assert.Equal(12.5, new PlayerState(reloaded).Thirst);
    }

    [Fact]
    public void Restore_all_gives_full_meters()
    {
        var player = new PlayerState(SaveFile.Load(TestFiles.Fixture(TestFiles.Large)));
        player.RestoreAll();
        Assert.Equal(player.ConditionMax, player.Condition);
        Assert.Equal(PlayerState.MaxCalories, player.Calories);
        Assert.Equal(0, player.Thirst);
        Assert.Equal(0, player.Fatigue);
        Assert.Equal(0, player.Freezing);
    }

    [Fact]
    public void Well_fed_sets_the_hours_even_when_the_field_is_missing()
    {
        var save = SaveFile.Load(TestFiles.Fixture(TestFiles.Small));
        save.Global.WriteNested("m_WellFedSerialized", new JObject());   // no m_ElapsedHoursNotStarving at all

        new PlayerState(save).WellFed = true;

        var wellFed = (JObject)save.Global.ReadNested("m_WellFedSerialized")!;
        Assert.True((bool)wellFed["m_Active"]!);
        Assert.Equal(72.0, (double)wellFed["m_ElapsedHoursNotStarving"]!);
    }

    [Theory]
    [InlineData("Cooking", 0, "Level 1")]
    [InlineData("Cooking", 80, "Level 3")]
    [InlineData("Cooking", 350, "Level 5 (max)")]
    [InlineData("Fire starting", 20, "Level 2")]
    [InlineData("Archery", 125, "Level 5 (max)")]
    public void Skill_levels_match_their_points(string skillName, int points, string level)
    {
        var player = new PlayerState(SaveFile.Load(TestFiles.Fixture(TestFiles.Small)));
        var skill = player.Skills.Single(s => s.Name == skillName);
        skill.Points = points;
        Assert.Equal(level, skill.LevelText);
    }

    [Fact]
    public void Max_all_skills_survives_a_save()
    {
        var path = files.Copy(TestFiles.Small);
        var save = SaveFile.Load(path);
        new PlayerState(save).MaxAllSkills();
        save.Save(path);

        var skills = new PlayerState(SaveFile.Load(path)).Skills.Where(s => s.Exists).ToList();
        Assert.NotEmpty(skills);
        Assert.All(skills, s => Assert.Equal(s.MaxPoints, s.Points));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("[]")]
    public void Max_all_skips_missing_or_non_object_skills_and_preserves_unknown_data(string? unsupportedSkill)
    {
        const string managerField = "m_SkillsManagerSerialized";
        const string missingField = "m_Skill_GunsmithingSerialized";
        const string cookingField = "m_Skill_CookingSerialized";
        var path = files.Copy(TestFiles.Small);
        var save = SaveFile.Load(path);
        var manager = (JObject)save.Global.ReadNested(managerField)!;
        if (unsupportedSkill is null) manager.Remove(missingField);
        else manager[missingField] = unsupportedSkill;
        manager["m_UnknownSkillSerialized"] = "{\"m_Points\":17,\"future\":true}";
        var cooking = (JObject)manager.ReadNested(cookingField)!;
        cooking["m_FutureCounter"] = 42;
        manager.WriteNested(cookingField, cooking);
        save.Global.WriteNested(managerField, manager);

        var player = new PlayerState(save);
        var missing = player.Skills.Single(s => s.Name == "Gunsmithing");
        Assert.False(missing.Exists);
        Assert.Equal(0, missing.Points);
        missing.Points = missing.MaxPoints; // Direct API edits must not create a schema either.
        player.MaxAllSkills();
        save.Save(path);

        var reloaded = SaveFile.Load(path);
        var updatedManager = (JObject)reloaded.Global.ReadNested(managerField)!;
        Assert.Equal(unsupportedSkill, (string?)updatedManager[missingField]);
        Assert.Equal((string?)manager["m_UnknownSkillSerialized"], (string?)updatedManager["m_UnknownSkillSerialized"]);
        Assert.Equal(42, (int)updatedManager.ReadNested(cookingField)!["m_FutureCounter"]!);
        var existingSkills = new PlayerState(reloaded).Skills.Where(s => s.Exists).ToList();
        Assert.NotEmpty(existingSkills);
        Assert.All(existingSkills, skill => Assert.Equal(skill.MaxPoints, skill.Points));
    }

    [Fact]
    public void Max_all_skills_does_not_create_a_missing_manager()
    {
        var save = SaveFile.Load(TestFiles.Fixture(TestFiles.Small));
        save.Global.Remove("m_SkillsManagerSerialized");
        var original = save.Global.DeepClone();

        new PlayerState(save).MaxAllSkills();

        Assert.True(JToken.DeepEquals(original, save.Global));
    }

    [Fact]
    public void Curing_empties_the_wound_lists()
    {
        var save = SaveFile.Load(TestFiles.Fixture(TestFiles.Small));
        // A wolf-bite sprained wrist, in the exact shape a real save stores it
        save.Global.WriteNested("m_SprainedWristSerialized", JObject.Parse(
            """{"m_CausesLocIDs":["GAMEPLAY_WolfBite"],"m_Locations":[3],"m_ElapsedHoursList":[0.27],"m_DurationHoursList":[51.4],"m_ElapsedRestList":[0.0]}"""));
        var player = new PlayerState(save);
        var wrist = player.Afflictions.Single(a => a.Name == "Sprained wrist");
        Assert.True(wrist.IsActive);

        wrist.Cure();

        Assert.False(wrist.IsActive);
        var data = (JObject)save.Global.ReadNested("m_SprainedWristSerialized")!;
        Assert.All(data.Properties(), p => Assert.Empty((JArray)p.Value));
    }

    [Fact]
    public void Curing_a_flag_affliction_clears_the_flag()
    {
        var save = SaveFile.Load(TestFiles.Fixture(TestFiles.Small));
        save.Global.WriteNested("m_BurnsSerialized", JObject.Parse("""{"m_Active":true,"m_DurationHours":33.0}"""));
        var burns = new PlayerState(save).Afflictions.Single(a => a.Name == "Burns");
        Assert.True(burns.IsActive);

        burns.Cure();

        Assert.False(burns.IsActive);
    }

    [Theory]
    [MemberData(nameof(TestFiles.BothSaves), MemberType = typeof(TestFiles))]
    public void Fixture_players_have_no_active_affliction(string fixture)
    {
        var player = new PlayerState(SaveFile.Load(TestFiles.Fixture(fixture)));
        Assert.DoesNotContain(player.Afflictions, a => a.IsActive);
    }
}
