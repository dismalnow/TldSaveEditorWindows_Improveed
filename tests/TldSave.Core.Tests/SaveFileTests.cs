using System.Text;
using Newtonsoft.Json.Linq;

namespace TldSave.Core.Tests;

public class SaveFileTests : IDisposable
{
    private readonly TestFiles files = new();
    public void Dispose() => files.Dispose();

    private static JObject RawSections(string path) => (JObject)SaveFile.Load(path).Slot["m_Dict"]!;

    private static byte[] EncodeSlot(JObject slot) => Lzf.Compress(Encoding.UTF8.GetBytes(TldJson.Serialize(slot)));

    private string WriteSyntheticSave(string sectionJson, bool numberArray = false)
    {
        var compressed = Lzf.Compress(Encoding.UTF8.GetBytes(sectionJson));
        var slot = new JObject
        {
            ["m_DisplayName"] = "Preservation test",
            ["m_GameMode"] = SaveLocator.SurvivalGameMode,
            ["m_Dict"] = new JObject
            {
                ["global"] = numberArray
                    ? new JArray(compressed.Select(b => (int)b))
                    : new JValue(Convert.ToBase64String(compressed)),
            },
        };
        var path = Path.Combine(files.Folder, "sandbox1");
        File.WriteAllBytes(path, EncodeSlot(slot));
        return path;
    }

    [Theory]
    [InlineData(TestFiles.Small, "GoDLC", 7)]
    [InlineData(TestFiles.Large, "1000Days", 169)]
    public void Fixture_saves_load(string fixture, string displayName, int sections)
    {
        var save = SaveFile.Load(TestFiles.Fixture(fixture));
        Assert.True(save.IsSurvival);
        Assert.Equal(displayName, save.DisplayName);
        Assert.Equal(sections, save.Sections.Count);
        Assert.True(save.HoursPlayed > 0);
    }

    [Theory]
    [MemberData(nameof(TestFiles.BothSaves), MemberType = typeof(TestFiles))]
    public void Saving_without_edits_keeps_every_section_byte_for_byte(string fixture)
    {
        var path = files.Copy(fixture);
        var save = SaveFile.Load(path);
        save.Save(path);

        Assert.Empty(save.LastSaveChangedSections);
        var original = RawSections(TestFiles.Fixture(fixture));
        var written = RawSections(path);
        Assert.All(original.Properties(), p => Assert.True(JToken.DeepEquals(p.Value, written[p.Name]), p.Name));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unedited_sections_keep_the_games_whitespace_and_number_spelling(bool numberArray)
    {
        var path = WriteSyntheticSave("{\r\n  \"unknown\": 1e0, \"precise\": 2.5000, \"other\": { \"a\": true }\r\n}\n", numberArray);
        var save = SaveFile.Load(path);
        var original = save.Slot["m_Dict"]!["global"]!.DeepClone();

        save.Save(path);

        Assert.Empty(save.LastSaveChangedSections);
        Assert.True(JToken.DeepEquals(original, RawSections(path)["global"]));
    }

    [Theory]
    [MemberData(nameof(TestFiles.BothSaves), MemberType = typeof(TestFiles))]
    public void An_edit_rewrites_only_the_edited_sections(string fixture)
    {
        var path = files.Copy(fixture);
        var save = SaveFile.Load(path);
        new PlayerState(save).Condition = 50;   // global + its copy in info
        save.Save(path);

        Assert.Equal(["global", "info"], save.LastSaveChangedSections.Order());
        var original = RawSections(TestFiles.Fixture(fixture));
        var written = RawSections(path);
        var untouched = original.Properties().Where(p => p.Name is not ("global" or "info"));
        Assert.All(untouched, p => Assert.True(JToken.DeepEquals(p.Value, written[p.Name]), p.Name));

        var reloaded = SaveFile.Load(path);
        Assert.Equal(50, new PlayerState(reloaded).Condition);
        Assert.Equal(50, (double)reloaded.Info!["m_Condition"]!);
    }

    [Fact]
    public void Saving_twice_rewrites_nothing_the_second_time()
    {
        var path = files.Copy(TestFiles.Large);
        var save = SaveFile.Load(path);
        new PlayerState(save).Thirst = 10;
        save.Save(path);
        save.Save(path);
        Assert.Empty(save.LastSaveChangedSections);
    }

    [Fact]
    public void A_failed_save_is_retried_in_full_by_the_next_one()
    {
        var path = files.Copy(TestFiles.Small);
        var save = SaveFile.Load(path);
        new PlayerState(save).Thirst = 42;

        // Fails: the folder doesn't exist
        Assert.ThrowsAny<IOException>(() => save.Save(Path.Combine(files.Folder, "missing", "sandbox1")));

        save.Save(path);
        Assert.Equal(["global"], save.LastSaveChangedSections);
        Assert.Equal(42, new PlayerState(SaveFile.Load(path)).Thirst);
    }

    [Fact]
    public void Corrupt_data_never_replaces_the_save()
    {
        var path = files.Copy(TestFiles.Small);
        var before = File.ReadAllBytes(path);

        Assert.ThrowsAny<Exception>(() => SaveFile.WriteVerified(path, temp => File.WriteAllBytes(temp, [1, 2, 3])));

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void A_crash_while_writing_never_replaces_the_save()
    {
        var path = files.Copy(TestFiles.Small);
        var before = File.ReadAllBytes(path);

        Assert.Throws<IOException>(() => SaveFile.WriteVerified(path, temp =>
        {
            File.WriteAllBytes(temp, before[..1000]);
            throw new IOException("simulated disk full");
        }));

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void A_file_touched_after_loading_requires_a_reload_before_saving()
    {
        var path = files.Copy(TestFiles.Small);
        var save = SaveFile.Load(path);
        Assert.False(save.ChangedOnDisk);

        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5)); // the game saving meanwhile
        Assert.True(save.ChangedOnDisk);

        var before = File.ReadAllBytes(path);
        Assert.Throws<InvalidOperationException>(() => save.Save(path));
        Assert.Equal(before, File.ReadAllBytes(path));

        var reloaded = SaveFile.Load(path);
        reloaded.Save(path);
        Assert.False(reloaded.ChangedOnDisk);
    }

    [Fact]
    public void A_replacement_with_the_same_timestamp_is_detected_and_not_overwritten()
    {
        var path = WriteSyntheticSave("{\"value\":1}");
        var save = SaveFile.Load(path);
        var newer = (JObject)save.Slot.DeepClone();
        newer["m_DisplayName"] = "Newer progress";
        var newerBytes = EncodeSlot(newer);
        File.WriteAllBytes(path, newerBytes);
        File.SetLastWriteTimeUtc(path, save.LoadedWriteTimeUtc);

        Assert.True(save.ChangedOnDisk);
        Assert.Throws<InvalidOperationException>(() => save.Save(path));
        Assert.Equal(newerBytes, File.ReadAllBytes(path));
        Assert.Empty(save.LastSaveChangedSections);
    }

    [Fact]
    public void A_deleted_save_is_detected_and_not_silently_recreated()
    {
        var path = WriteSyntheticSave("{\"value\":1}");
        var save = SaveFile.Load(path);
        File.Delete(path);

        Assert.True(save.ChangedOnDisk);
        Assert.Throws<InvalidOperationException>(() => save.Save(path));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Save_as_can_recover_edits_without_overwriting_a_changed_original()
    {
        var path = WriteSyntheticSave("{\"value\":1}");
        var save = SaveFile.Load(path);
        save.Global["value"] = 2;
        File.Delete(path);
        var copy = Path.Combine(files.Folder, "sandbox2");

        save.Save(copy);

        Assert.Equal(2, (int)SaveFile.Load(copy).Global["value"]!);
        Assert.False(File.Exists(path));
        Assert.True(save.ChangedOnDisk);
        Assert.Equal(path, save.FilePath);
    }

    [Fact]
    public void A_change_during_staging_is_not_overwritten_even_with_the_same_timestamp()
    {
        var path = WriteSyntheticSave("{\"value\":1}");
        var before = File.ReadAllBytes(path);
        var writeTime = File.GetLastWriteTimeUtc(path);
        var newer = (JObject)SaveFile.Load(path).Slot.DeepClone();
        newer["m_DisplayName"] = "Saved while editor was writing";
        var newerBytes = EncodeSlot(newer);
        string? temporaryPath = null;

        Assert.Throws<InvalidOperationException>(() => SaveFile.WriteVerified(path, temp =>
        {
            temporaryPath = temp;
            File.WriteAllBytes(temp, before);
            File.WriteAllBytes(path, newerBytes);
            File.SetLastWriteTimeUtc(path, writeTime);
        }));

        Assert.Equal(newerBytes, File.ReadAllBytes(path));
        Assert.NotNull(temporaryPath);
        Assert.False(File.Exists(temporaryPath));
    }

    [Fact]
    public void A_failed_write_never_uses_or_deletes_an_existing_temporary_file()
    {
        var path = WriteSyntheticSave("{\"value\":1}");
        var before = File.ReadAllBytes(path);
        var otherTemp = path + ".tmp";
        File.WriteAllText(otherTemp, "belongs to another operation");
        string? temporaryPath = null;

        Assert.Throws<InvalidDataException>(() => SaveFile.WriteVerified(path, temp =>
        {
            temporaryPath = temp;
            File.WriteAllBytes(temp, Lzf.Compress(Encoding.UTF8.GetBytes("{}")));
        }));

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal("belongs to another operation", File.ReadAllText(otherTemp));
        Assert.NotNull(temporaryPath);
        Assert.NotEqual(otherTemp, temporaryPath);
        Assert.False(File.Exists(temporaryPath));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"m_Dict\":[]}")]
    [InlineData("{\"m_Dict\":{}}")]
    public void Compressed_json_that_is_not_a_save_never_replaces_the_destination(string json)
    {
        var path = WriteSyntheticSave("{\"value\":1}");
        var before = File.ReadAllBytes(path);

        Assert.Throws<InvalidDataException>(() => SaveFile.WriteVerified(path,
            temp => File.WriteAllBytes(temp, Lzf.Compress(Encoding.UTF8.GetBytes(json)))));

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(files.Folder, "*.tmp"));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("17")]
    public void Global_section_must_be_an_object(string globalJson)
    {
        var path = WriteSyntheticSave(globalJson);
        Assert.Throws<InvalidDataException>(() => SaveFile.Load(path));
    }

    [Fact]
    public void Invalid_utf8_in_a_section_cannot_replace_the_destination()
    {
        var path = WriteSyntheticSave("{\"value\":1}");
        var before = File.ReadAllBytes(path);
        var slot = (JObject)SaveFile.Load(path).Slot.DeepClone();
        // An invalid byte inside a quoted value would otherwise become a replacement character silently.
        var malformed = Encoding.UTF8.GetBytes("{\"label\":\"X\"}");
        malformed[Array.IndexOf(malformed, (byte)'X')] = 0xff;
        slot["m_Dict"]!["global"] = Convert.ToBase64String(Lzf.Compress(malformed));

        Assert.Throws<DecoderFallbackException>(() => SaveFile.WriteVerified(path,
            temp => File.WriteAllBytes(temp, EncodeSlot(slot))));

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(files.Folder, "*.tmp"));
    }

    [Theory]
    [InlineData("global", false)]
    [InlineData("screenshot", false)]
    [InlineData("LakeRegion", true)]
    public void Meta_sections_are_not_scenes(string key, bool isScene)
    {
        Assert.Equal(!isScene, SaveFile.IsMetaSection(key));
    }
}
