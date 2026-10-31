using Newtonsoft.Json.Linq;

namespace TldSave.Core.Tests;

public class InventoryTests : IDisposable
{
    private readonly TestFiles files = new();
    public void Dispose() => files.Dispose();

    private static InventoryItem Add(Inventory inventory, string prefab)
    {
        var entry = ItemCatalog.Find(prefab) ?? throw new Exception($"{prefab} not in catalog");
        return inventory.Add(entry.Name, entry.Template);
    }

    [Theory]
    [MemberData(nameof(TestFiles.BothSaves), MemberType = typeof(TestFiles))]
    public void Items_added_from_the_catalog_survive_a_save(string fixture)
    {
        var path = files.Copy(fixture);
        var save = SaveFile.Load(path);
        var inventory = Inventory.Load(save);
        var countBefore = inventory.Items.Count;

        Add(inventory, "GEAR_InsulatedFlask_A");
        Add(inventory, "GEAR_Softwood").Units = 10;
        Add(inventory, "GEAR_CannedBeans");
        Add(inventory, "GEAR_Jeans");
        inventory.Commit();
        save.Save(path);

        var reloaded = Inventory.Load(SaveFile.Load(path));
        Assert.Equal(countBefore + 4, reloaded.Items.Count);
        var added = reloaded.Items.TakeLast(4).ToDictionary(i => i.PrefabName);

        // The flask gets its (empty) contents data from the real template
        Assert.NotNull(added["GEAR_InsulatedFlask_A"].Gear["m_InsulatedFlaskSerialized"]);
        Assert.Equal(10, added["GEAR_Softwood"].Units);

        var food = (JObject)added["GEAR_CannedBeans"].Gear.ReadNested("m_FoodItemSerialized")!;
        Assert.Equal((double)food["m_CaloriesTotal"]!, (double)food["m_CaloriesRemainingProxy"]!);

        // Clothing comes in dry and not worn, even though the real sample was equipped
        var jeans = (JObject)added["GEAR_Jeans"].Gear.ReadNested("m_ClothingItemSerialized")!;
        Assert.Null(jeans["m_HasEquippedData"]);
        Assert.Equal(0.0, (double)jeans["m_PercentWet"]!);

        // New items are timestamped "now" so they don't decay on load
        Assert.All(added.Values, i => Assert.Equal(save.HoursPlayed, (double)i.Gear["m_HoursPlayed"]!, 3));
        Assert.All(added.Values, i => Assert.Equal(1.0, i.Condition));
    }

    [Fact]
    public void Quantity_condition_and_removal_edits_survive_a_save()
    {
        var path = files.Copy(TestFiles.Large);
        var save = SaveFile.Load(path);
        var inventory = Inventory.Load(save);

        var stack = inventory.Items.First(i => i.IsStackable);
        var tool = inventory.Items.First(i => !i.IsStackable && i.Condition is > 0.1);
        var removed = inventory.Items.Last();
        var stackId = (int)stack.Gear["m_InstanceIDProxy"]!;
        var toolId = (int)tool.Gear["m_InstanceIDProxy"]!;
        var removedId = (int)removed.Gear["m_InstanceIDProxy"]!;

        stack.Units = 50;
        tool.ConditionPercent = 42;
        inventory.Remove(removed);
        inventory.Commit();
        save.Save(path);

        var reloaded = Inventory.Load(SaveFile.Load(path)).Items.ToDictionary(i => (int)i.Gear["m_InstanceIDProxy"]!);
        Assert.Equal(50, reloaded[stackId].Units);
        Assert.Equal(0.42, reloaded[toolId].Condition!.Value, 3);
        Assert.False(reloaded.ContainsKey(removedId));
    }

    [Fact]
    public void Quantity_is_ignored_for_items_that_do_not_stack()
    {
        var inventory = Inventory.Load(SaveFile.Load(TestFiles.Fixture(TestFiles.Small)));
        var item = inventory.Items.First(i => !i.IsStackable);
        item.Units = 99;
        Assert.False(item.IsStackable);
        Assert.Null(item.Gear["m_StackableItemSerialized"]);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Non_finite_condition_edits_do_not_change_item_data(double value)
    {
        var inventory = Inventory.Load(SaveFile.Load(TestFiles.Fixture(TestFiles.Small)));
        var item = inventory.Items.First();
        item.Gear["m_WornOut"] = true;
        var original = item.Gear.DeepClone();
        var changeRaised = false;
        item.PropertyChanged += (_, _) => changeRaised = true;

        Assert.Throws<ArgumentOutOfRangeException>(() => item.Condition = value);
        Assert.Throws<ArgumentOutOfRangeException>(() => item.ConditionPercent = value);

        Assert.True(JToken.DeepEquals(original, item.Gear));
        Assert.False(changeRaised);
    }

    [Fact]
    public void Existing_non_finite_item_tokens_survive_an_unrelated_edit()
    {
        var path = files.Copy(TestFiles.Small);
        var save = SaveFile.Load(path);
        var inventory = Inventory.Load(save);
        var item = inventory.Items.First(i => i.IsStackable);
        item.Gear["m_NormalizedCondition"] = double.NaN;
        item.Gear["m_UnknownFutureValue"] = double.NegativeInfinity;
        var itemId = (int)item.Gear["m_InstanceIDProxy"]!;
        inventory.Commit();
        save.Save(path);

        save = SaveFile.Load(path);
        inventory = Inventory.Load(save);
        item = inventory.Items.Single(i => (int)i.Gear["m_InstanceIDProxy"]! == itemId);
        Assert.True(double.IsNaN(item.Condition!.Value));
        item.Units = 17;
        inventory.Commit();
        save.Save(path);

        var reloaded = Inventory.Load(SaveFile.Load(path)).Items.Single(i => (int)i.Gear["m_InstanceIDProxy"]! == itemId);
        Assert.True(double.IsNaN(reloaded.Condition!.Value));
        Assert.Equal(double.NegativeInfinity, (double)reloaded.Gear["m_UnknownFutureValue"]!);
        Assert.Equal(17, reloaded.Units);
    }

    [Fact]
    public void Added_items_never_reuse_an_instance_id()
    {
        var inventory = Inventory.Load(SaveFile.Load(TestFiles.Fixture(TestFiles.Large)));
        for (var i = 0; i < 200; i++) Add(inventory, "GEAR_Softwood");

        var ids = inventory.Items.Select(i => (int?)i.Gear["m_InstanceIDProxy"]).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }
}
