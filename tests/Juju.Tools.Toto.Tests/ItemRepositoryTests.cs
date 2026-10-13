using Toto.App.Data;
using Toto.App.Domain;
using Xunit;

namespace Toto.App.Tests;

public sealed class ItemRepositoryTests : IDisposable
{
    private readonly string dataDirectory = Path.Combine(Path.GetTempPath(), "toto-tests", Guid.NewGuid().ToString("N"));
    private readonly ItemRepository repository;

    public ItemRepositoryTests()
    {
        repository = new ItemRepository(new AppPaths(dataDirectory));
        repository.EnsureFiles();
    }

    [Fact]
    public void Add_PreservesExistingActiveAndHistoryItems()
    {
        repository.Add(Item("active", "已有事项"));
        repository.Add(Item("completed", "已完成事项"));
        Assert.True(repository.End("completed", ItemStatus.Completed, "已有备注", DateTime.Now));

        repository.Add(Item("new", "新增事项"));

        var active = repository.GetActive();
        var history = repository.GetHistory(null, 1, 100).Items;
        Assert.Equal(2, active.Count);
        Assert.Contains(active, item => item.Id == "active" && item.Content == "已有事项");
        Assert.Contains(active, item => item.Id == "new" && item.Content == "新增事项" && item.Status == ItemStatus.Active);
        Assert.Single(history);
        Assert.Equal("已完成事项", history[0].Content);
        Assert.Equal("已有备注", history[0].Note);
    }

    [Fact]
    public void End_MovesOnlyTheSpecifiedItemAndKeepsContentSeparateFromNote()
    {
        repository.Add(Item("one", "事项一"));
        repository.Add(Item("two", "事项二"));
        repository.Add(Item("three", "事项三"));

        Assert.True(repository.End("two", ItemStatus.Completed, "完成备注", DateTime.Now));

        var active = repository.GetActive();
        var history = repository.GetHistory(null, 1, 100).Items;
        Assert.Equal(["one", "three"], active.Select(item => item.Id).Order().ToArray());
        var completed = Assert.Single(history);
        Assert.Equal("two", completed.Id);
        Assert.Equal("事项二", completed.Content);
        Assert.Equal("完成备注", completed.Note);
    }

    [Fact]
    public async Task ConcurrentAddsAndReminderUpdate_DoNotLoseOrRewriteItems()
    {
        repository.Add(Item("due", "到期事项", DateTime.Now.AddMinutes(-1)));
        var adds = Enumerable.Range(0, 30)
            .Select(index => Task.Run(() => repository.Add(Item($"item-{index}", $"事项 {index}"))));

        await Task.WhenAll(adds.Append(Task.Run(() => repository.MarkDueReminders(DateTime.Now))));

        var active = repository.GetActive();
        Assert.Equal(31, active.Count);
        Assert.Equal(31, active.Select(item => item.Id).Distinct().Count());
        Assert.Equal(ReminderStatus.Reminded, active.Single(item => item.Id == "due").ReminderStatus);
        Assert.Empty(repository.GetHistory(null, 1, 100).Items);
    }

    [Fact]
    public async Task ConcurrentAddEndAndReminderUpdate_KeepEachOperationScopedToItsItem()
    {
        repository.Add(Item("target", "待完成事项"));
        repository.Add(Item("due", "到期事项", DateTime.Now.AddMinutes(-1)));

        var results = await Task.WhenAll(
            Task.Run(() =>
            {
                repository.Add(Item("new", "并发新增事项"));
                return true;
            }),
            Task.Run(() => repository.End("target", ItemStatus.Completed, "完成备注", DateTime.Now)),
            Task.Run(() =>
            {
                repository.MarkDueReminders(DateTime.Now);
                return true;
            }));

        Assert.All(results, Assert.True);
        var active = repository.GetActive();
        Assert.Equal(["due", "new"], active.Select(item => item.Id).Order().ToArray());
        Assert.Equal("并发新增事项", active.Single(item => item.Id == "new").Content);
        Assert.Equal(ReminderStatus.Reminded, active.Single(item => item.Id == "due").ReminderStatus);
        var completed = Assert.Single(repository.GetHistory(null, 1, 100).Items);
        Assert.Equal("target", completed.Id);
        Assert.Equal("待完成事项", completed.Content);
        Assert.Equal("完成备注", completed.Note);
    }

    [Fact]
    public void Migration_copies_existing_toto_files_without_overwriting_juju_data()
    {
        var legacy = Path.Combine(dataDirectory, "legacy");
        var target = Path.Combine(dataDirectory, "juju");
        Directory.CreateDirectory(legacy);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(legacy, "config.ini"), "legacy settings");
        File.WriteAllText(Path.Combine(legacy, "toto_ing.csv"), "legacy items");
        File.WriteAllText(Path.Combine(legacy, "2026.json"), "legacy holidays");
        File.WriteAllText(Path.Combine(target, "toto_ing.csv"), "current items");

        Assert.True(TotoToolHost.ImportLegacyData(new AppPaths(target), legacy));

        Assert.Equal("legacy settings", File.ReadAllText(Path.Combine(target, "config.ini")));
        Assert.Equal("current items", File.ReadAllText(Path.Combine(target, "toto_ing.csv")));
        Assert.Equal("legacy holidays", File.ReadAllText(Path.Combine(target, "2026.json")));
        Assert.True(File.Exists(Path.Combine(legacy, "toto_ing.csv")));
    }

    [Fact]
    public void Toto_window_icon_is_embedded_in_the_integrated_tool()
    {
        Assert.Contains("Juju.Tools.Toto.toto.ico", typeof(TotoToolHost).Assembly.GetManifestResourceNames());
    }

    private static TodoItem Item(string id, string content, DateTime? remindAt = null) =>
        new(id, content, null, remindAt, DateTime.Now, 0, ItemStatus.Active,
            remindAt is null ? ReminderStatus.None : ReminderStatus.Pending, null, null, "");

    public void Dispose()
    {
        if (Directory.Exists(dataDirectory)) Directory.Delete(dataDirectory, true);
    }
}
