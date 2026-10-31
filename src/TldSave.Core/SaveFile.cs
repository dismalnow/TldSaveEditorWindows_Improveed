using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace TldSave.Core;

/// <summary>
/// A TLD save file.
///
///   file                 = LZF( JSON "SlotData" )         m_DisplayName, m_GameMode, m_Dict, ...
///   SlotData.m_Dict[key] = base64 -> LZF( JSON )          boot, global, info, screenshot, one per visited scene
///   inside sections      = many values are JSON strings containing more JSON (see TldJson.ReadNested)
/// </summary>
public sealed class SaveFile
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public string FilePath { get; }

    /// <summary>The outer SlotData object. Its m_Dict is rebuilt from <see cref="Sections"/> on save.</summary>
    public JObject Slot { get; }

    /// <summary>Decoded m_Dict sections, in file order.</summary>
    public IReadOnlyDictionary<string, JToken> Sections => sections;

    private readonly Dictionary<string, JToken> sections;

    /// <summary>
    /// SHA-256 of each parsed section's normalized JSON text. On save, a section whose JSON still
    /// has the same fingerprint keeps its original encoded value as-is
    /// (the game's own bytes), so only edited sections are re-encoded.
    /// </summary>
    private readonly Dictionary<string, byte[]> fingerprints;

    private sealed record DiskState(DateTime WriteTimeUtc, byte[] Fingerprint);
    private DiskState loadedDiskState;

    /// <summary>Sections re-encoded by the last <see cref="Save"/> (the ones that changed).</summary>
    public IReadOnlyList<string> LastSaveChangedSections { get; private set; } = [];

    /// <summary>The file's last-write time when it was loaded (or last saved by us).</summary>
    public DateTime LoadedWriteTimeUtc { get; private set; }

    /// <summary>True if something else (usually the game) wrote the file after we loaded it.</summary>
    public bool ChangedOnDisk
    {
        get
        {
            try { return !Matches(loadedDiskState, ReadDiskState(FilePath)); }
            catch (IOException) { return true; }
            catch (UnauthorizedAccessException) { return true; }
        }
    }

    private SaveFile(string filePath, JObject slot, Dictionary<string, JToken> sections,
        Dictionary<string, byte[]> fingerprints, DiskState diskState)
    {
        FilePath = Path.GetFullPath(filePath);
        Slot = slot;
        this.sections = sections;
        this.fingerprints = fingerprints;
        loadedDiskState = diskState;
        LoadedWriteTimeUtc = diskState.WriteTimeUtc;
    }

    public string DisplayName => (string?)Slot["m_DisplayName"] ?? "";
    public string GameMode => (string?)Slot["m_GameMode"] ?? "";
    public string Timestamp => (string?)Slot["m_Timestamp"] ?? "";
    public bool IsSurvival => GameMode == SaveLocator.SurvivalGameMode;

    /// <summary>True for the sections that aren't a visited scene (location).</summary>
    public static bool IsMetaSection(string key) => key is "boot" or "global" or "info" or "screenshot" or "sandbox_global";

    public JObject Global => (JObject)sections["global"];
    public JObject? Info => sections.GetValueOrDefault("info") as JObject;
    public JObject? Boot => sections.GetValueOrDefault("boot") as JObject;

    /// <summary>The game's main clock, in hours since the start of the run.</summary>
    public double HoursPlayed =>
        (double?)Global.ReadNested("m_TimeOfDay_Serialized")?["m_HoursPlayedNotPausedProxy"]
        ?? (double?)Info?["m_HoursSurvived"]
        ?? 0;

    public static SaveFile Load(string path)
    {
        var writeTime = File.GetLastWriteTimeUtc(path);
        var bytes = File.ReadAllBytes(path);
        if (File.GetLastWriteTimeUtc(path) != writeTime)
            throw new IOException("The save file changed while it was being read. Reload it and try again.");
        var slot = ParseSlot(bytes);

        var sections = new Dictionary<string, JToken>();
        var fingerprints = new Dictionary<string, byte[]>();
        foreach (var property in ((JObject)slot["m_Dict"]!).Properties())
        {
            var json = Lzf.Decompress(ReadBytes(property.Value));
            var section = TldJson.Parse(Utf8.GetString(json));
            sections[property.Name] = section;
            // Compare like with like: the game's whitespace and number spelling may differ from ours.
            // Preserve the original encoded section until its parsed contents actually change.
            fingerprints[property.Name] = SHA256.HashData(Utf8.GetBytes(TldJson.Serialize(section)));
        }
        if (sections.GetValueOrDefault("global") is not JObject)
            throw new InvalidDataException("The save must contain a global section that is a JSON object.");
        return new SaveFile(path, slot, sections, fingerprints, new DiskState(writeTime, SHA256.HashData(bytes)));
    }

    /// <summary>Reads only the outer SlotData (name, mode, timestamp). Sections are left undecoded.</summary>
    public static JObject ReadHeader(string path)
    {
        var slot = ParseSlot(File.ReadAllBytes(path));
        slot.Remove("m_Dict");
        return slot;
    }

    public void Save(string path)
    {
        path = Path.GetFullPath(path);
        var isOriginal = string.Equals(path, FilePath,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        var expectedDiskState = isOriginal ? loadedDiskState : ReadDiskState(path);
        EnsureUnchanged(path, expectedDiskState);

        var dict = new JObject();
        var changed = new List<string>();
        var newFingerprints = new Dictionary<string, byte[]>();
        foreach (var (key, value) in sections)
        {
            var json = Utf8.GetBytes(TldJson.Serialize(value));
            var fingerprint = SHA256.HashData(json);
            var original = Slot["m_Dict"]?[key];

            // Unchanged section: keep the encoded value from the file, byte for byte
            if (original is not null && fingerprints.TryGetValue(key, out var known) && known.AsSpan().SequenceEqual(fingerprint))
            {
                dict[key] = original.DeepClone();
                continue;
            }

            var compressed = Lzf.Compress(json);
            // The game writes base64 strings; older editors wrote number arrays. Keep whatever the file used.
            dict[key] = original?.Type == JTokenType.Array
                ? new JArray(compressed.Select(b => (int)b))
                : Convert.ToBase64String(compressed);
            newFingerprints[key] = fingerprint;
            changed.Add(key);
        }

        // Write first; only update our state once the file is really saved, so a failed save is retried in full
        var previousDict = Slot["m_Dict"];
        Slot["m_Dict"] = dict;
        byte[] bytes;
        try
        {
            bytes = Lzf.Compress(Utf8.GetBytes(TldJson.Serialize(Slot)));
            WriteVerified(path, temp => File.WriteAllBytes(temp, bytes), expectedDiskState);
        }
        catch
        {
            Slot["m_Dict"] = previousDict;
            throw;
        }
        foreach (var (key, fingerprint) in newFingerprints) fingerprints[key] = fingerprint;
        LastSaveChangedSections = changed;

        if (isOriginal)
        {
            LoadedWriteTimeUtc = File.GetLastWriteTimeUtc(path);
            loadedDiskState = new DiskState(LoadedWriteTimeUtc, SHA256.HashData(bytes));
        }
    }

    /// <summary>
    /// Stages the output in a unique file beside <paramref name="path"/>, validates it as a save, checks that
    /// the destination has not changed, then swaps it in with File.Replace. A failed write or validation leaves
    /// the destination untouched. Cleanup only ever touches this operation's own temporary file.
    /// </summary>
    internal static void WriteVerified(string path, Action<string> writeTemp)
    {
        path = Path.GetFullPath(path);
        WriteVerified(path, writeTemp, ReadDiskState(path));
    }

    private static void WriteVerified(string path, Action<string> writeTemp, DiskState? expectedDiskState)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var ownsTemp = false;
        try
        {
            // Reserve our own name. Never overwrite or clean up a temporary file from another instance.
            using (new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            ownsTemp = true;
            writeTemp(temp);
            Load(temp);
            EnsureUnchanged(path, expectedDiskState);
            if (expectedDiskState is not null) File.Replace(temp, path, destinationBackupFileName: null);
            else File.Move(temp, path);
        }
        finally
        {
            if (ownsTemp)
            {
                try { File.Delete(temp); }
                catch (IOException) { /* Do not hide the original failure if cleanup is blocked. */ }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static JObject ParseSlot(byte[] bytes)
    {
        if (TldJson.Parse(Utf8.GetString(Lzf.Decompress(bytes))) is not JObject slot)
            throw new InvalidDataException("The save root must be a JSON object.");
        if (slot["m_Dict"] is not JObject)
            throw new InvalidDataException("The save must contain an m_Dict object with its encoded sections.");
        return slot;
    }

    private static DiskState? ReadDiskState(string path)
    {
        try
        {
            var writeTime = File.GetLastWriteTimeUtc(path);
            var bytes = File.ReadAllBytes(path);
            if (File.GetLastWriteTimeUtc(path) != writeTime)
                throw new IOException("The save file changed while it was being checked. Reload it and try again.");
            return new DiskState(writeTime, SHA256.HashData(bytes));
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    private static bool Matches(DiskState? expected, DiskState? actual) =>
        expected is null ? actual is null : actual is not null
            && expected.WriteTimeUtc == actual.WriteTimeUtc
            && expected.Fingerprint.AsSpan().SequenceEqual(actual.Fingerprint);

    private static void EnsureUnchanged(string path, DiskState? expected)
    {
        if (!Matches(expected, ReadDiskState(path)))
            throw new InvalidOperationException("The save file changed or was deleted after it was opened. " +
                "Reload the save before saving again.");
    }

    private static byte[] ReadBytes(JToken value) => value.Type switch
    {
        JTokenType.String => Convert.FromBase64String((string)value!),
        JTokenType.Array => value.Select(b => (byte)b).ToArray(),
        _ => throw new InvalidDataException($"Unexpected m_Dict value type {value.Type}"),
    };
}
