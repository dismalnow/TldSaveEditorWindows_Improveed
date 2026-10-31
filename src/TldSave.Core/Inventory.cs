using System.ComponentModel;
using Newtonsoft.Json.Linq;

namespace TldSave.Core;

/// <summary>
/// The player inventory: global.m_Inventory_Serialized (JSON string)
///   -> m_SerializedItems[] { m_PrefabName, m_SerializedGear (JSON string) }
/// Edit the items, then call <see cref="Commit"/> to write them back into the save.
/// </summary>
public sealed class Inventory
{
    private const string Field = "m_Inventory_Serialized";

    private readonly SaveFile save;
    private readonly JObject root;

    public List<InventoryItem> Items { get; }

    private Inventory(SaveFile save, JObject root, List<InventoryItem> items)
    {
        this.save = save;
        this.root = root;
        Items = items;
    }

    public static Inventory Load(SaveFile save)
    {
        var root = save.Global.ReadNested(Field) as JObject ?? new JObject();
        var items = (root["m_SerializedItems"] as JArray ?? new JArray())
            .OfType<JObject>()
            .Select(entry => new InventoryItem(entry))
            .ToList();
        return new Inventory(save, root, items);
    }

    /// <summary>
    /// Adds a new item. <paramref name="template"/> (from <see cref="ItemCatalog"/>) supplies the item's component
    /// data (stack, food, flask contents...); without it the item only gets the basic fields.
    /// </summary>
    public InventoryItem Add(string prefabName, JObject? template = null)
    {
        var now = save.HoursPlayed;
        var gear = new JObject
        {
            // Timestamped "now" so the item doesn't decay on load
            ["m_HoursPlayed"] = now,
            ["m_Position"] = new JArray(0.0, 0.0, 0.0),
            ["m_Rotation"] = new JArray(0.0, 0.0, 0.0, 1.0),
            ["m_InstanceIDProxy"] = NewInstanceId(),
        };
        if (template is not null)
            gear.Merge(template.DeepClone(), new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace });
        gear["m_NormalizedCondition"] ??= 1.0;
        gear["m_GearItemSaveVersion"] ??= 4;
        StampComponentTimes(gear, now);

        // No template, but this prefab is already in the inventory as a stack: start the new one as a stack of 1
        if (gear["m_StackableItemSerialized"] is null && Items.Any(i => i.PrefabName == prefabName && i.IsStackable))
            gear["m_StackableItemSerialized"] = TldJson.Serialize(new JObject { ["m_UnitsProxy"] = 1 });

        var entry = new JObject
        {
            ["m_PrefabName"] = prefabName,
            ["m_SerializedGear"] = TldJson.Serialize(gear),
        };
        var item = new InventoryItem(entry);
        Items.Add(item);
        return item;
    }

    /// <summary>A random instance ID that no other inventory item uses.</summary>
    private int NewInstanceId()
    {
        var used = Items.Select(i => (int?)i.Gear["m_InstanceIDProxy"]).ToHashSet();
        int id;
        do id = Random.Shared.Next(1, int.MaxValue); while (used.Contains(id));
        return id;
    }

    /// <summary>Components such as food keep their own m_HoursPlayed timestamp; set those to "now" too.</summary>
    private static void StampComponentTimes(JObject gear, double now)
    {
        foreach (var property in gear.Properties().Where(p => p.Name.EndsWith("Serialized")).ToList())
        {
            if (property.Value.Type != JTokenType.String || !((string?)property.Value)!.StartsWith('{')) continue;
            if (gear.ReadNested(property.Name) is not JObject component || component["m_HoursPlayed"] is null) continue;
            component["m_HoursPlayed"] = now;
            gear.WriteNested(property.Name, component);
        }
    }

    public void Remove(InventoryItem item) => Items.Remove(item);

    public void Commit()
    {
        foreach (var item in Items) item.WriteBack();
        root["m_SerializedItems"] = new JArray(Items.Select(i => i.Entry));
        save.Global.WriteNested(Field, root);
    }
}

public sealed class InventoryItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The raw inventory entry (m_PrefabName, m_SerializedGear, ...).</summary>
    public JObject Entry { get; }

    /// <summary>Parsed m_SerializedGear. Written back to the entry on <see cref="WriteBack"/>.</summary>
    public JObject Gear { get; private set; }

    internal InventoryItem(JObject entry)
    {
        Entry = entry;
        Gear = entry.ReadNested("m_SerializedGear") as JObject ?? new JObject();
    }

    public string PrefabName => (string?)Entry["m_PrefabName"] ?? "";

    /// <summary>Prefab name without the GEAR_ prefix, for lists.</summary>
    public string ShortName => PrefabNames.Short(PrefabName);

    public ItemCategory Category => ItemCategories.Classify(PrefabName, Gear);
    public string CategoryName => ItemCategories.DisplayName(Category);

    /// <summary>0..1. Missing means the game omitted the default (worn-out clothing has none).</summary>
    public double? Condition
    {
        get => (double?)Gear["m_NormalizedCondition"];
        set
        {
            if (value is { } number && !double.IsFinite(number))
                throw new ArgumentOutOfRangeException(nameof(value), "Enter a finite condition value.");
            if (value is null) Gear.Remove("m_NormalizedCondition");
            else Gear["m_NormalizedCondition"] = Math.Clamp(value.Value, 0, 1);
            if (value > 0 && WornOut) Gear["m_WornOut"] = false;
            OnChanged();
        }
    }

    public double ConditionPercent
    {
        get => Math.Round((Condition ?? 0) * 100, 1);
        set => Condition = value / 100;
    }

    public bool WornOut => (bool?)Gear["m_WornOut"] ?? false;

    public string ConditionText => Condition is { } c ? $"{c * 100:0}%" : WornOut ? "worn out" : "?";

    private const string StackableField = "m_StackableItemSerialized";

    /// <summary>True for items with a quantity (wood, matches, bandages, arrows...): m_StackableItemSerialized is set.</summary>
    public bool IsStackable => !string.IsNullOrEmpty((string?)Gear[StackableField]);

    /// <summary>Quantity of a stackable item (m_UnitsProxy). Ignored for non-stackable items.</summary>
    public int Units
    {
        get => (int?)Gear.ReadNested(StackableField)?["m_UnitsProxy"] ?? 0;
        set
        {
            if (!IsStackable) return;
            var stackable = Gear.ReadNested(StackableField) as JObject ?? new JObject();
            stackable["m_UnitsProxy"] = Math.Max(1, value);
            Gear.WriteNested(StackableField, stackable);
            OnChanged();
        }
    }

    public string UnitsText => IsStackable ? $"×{Units}" : "";

    public void ReplaceGear(JObject gear)
    {
        Gear = gear;
        OnChanged();
    }

    internal void WriteBack() => Entry["m_SerializedGear"] = TldJson.Serialize(Gear);

    private void OnChanged()
    {
        // Everything derives from Gear, so refresh all bindings
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }
}
