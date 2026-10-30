using Juju.App.Contracts.Windows;
using Juju.App.Contracts.Settings;
using Juju.App.Settings;
using Juju.App.Tools.Catalog;
using Juju.App.Tools.Contracts;
using Juju.App.Tools.Routing;
using Juju.Core.Tools;
using Xunit;

namespace Juju.App.Tests;

public sealed class ToolManagerTests
{
    [Fact]
    public void Open_prepares_the_host_checks_readiness_then_opens_the_selected_module()
    {
        var calls = new List<string>();
        var module = new TestModule(ToolId.Json, 0, BackgroundCapability.Unsupported, true,
            new(true), calls);
        var manager = CreateManager([module], new RecordingWindows(calls), new RecordingFeedback(calls));

        manager.Open(ToolId.Json);

        Assert.Equal(["prepare", "ready:Json", "open:Json"], calls);
    }

    [Fact]
    public void Open_reports_not_ready_without_opening_and_preserves_placeholder_visibility()
    {
        var calls = new List<string>();
        var unavailable = new TestModule(ToolId.Toto, 0, BackgroundCapability.Required, true,
            new(false, "calendar unavailable", ToolOpenMessageKind.Error, "Toto"), calls);
        var placeholder = new TestModule(ToolId.EchoHttp, 1, BackgroundCapability.Unsupported, false,
            new(false, "EchoHttp 正在开发中。", ToolOpenMessageKind.Information, "juju"), calls);
        var manager = CreateManager([unavailable, placeholder], new RecordingWindows(calls),
            new RecordingFeedback(calls));

        manager.Open(ToolId.Toto);
        manager.Open(ToolId.EchoHttp);

        Assert.Equal(
            ["prepare", "ready:Toto", "feedback:Toto:Error:calendar unavailable", "ready:EchoHttp",
                "feedback:juju:Information:EchoHttp 正在开发中。"], calls);
        Assert.Equal(0, unavailable.OpenCount);
        Assert.Equal(0, placeholder.OpenCount);
    }

    [Fact]
    public async Task Start_starts_required_modules_once_and_stops_them_in_reverse_order()
    {
        var calls = new List<string>();
        var first = new TestModule(ToolId.Json, 0, BackgroundCapability.Required, false, new(true), calls);
        var optional = new TestModule(ToolId.Toto, 1, BackgroundCapability.Optional, false, new(true), calls);
        var last = new TestModule(ToolId.EchoHttp, 2, BackgroundCapability.Required, false, new(true), calls);
        var manager = CreateManager([first, optional, last], new RecordingWindows(calls),
            new RecordingFeedback(calls));

        await manager.StartAsync();
        await manager.StartAsync();
        await manager.StopAllAsync();
        await manager.StopAllAsync();

        Assert.Equal(["start:Json", "start:EchoHttp", "stop:EchoHttp", "stop:Json"], calls);
        Assert.Equal(0, optional.StartCount);
    }

    [Fact]
    public void Catalog_is_built_from_registered_modules_in_declared_order()
    {
        var modules = new[]
        {
            new TestModule(ToolId.Kairos, 3, BackgroundCapability.Unsupported, false, new(true), []),
            new TestModule(ToolId.Json, 0, BackgroundCapability.Unsupported, true, new(true), [])
        };

        var catalog = new ToolRegistry(modules);

        Assert.Equal([ToolId.Json, ToolId.Kairos], catalog.All.Select(item => item.Id));
        Assert.Equal(BackgroundCapability.Unsupported, catalog.GetDescriptor(ToolId.Json).Background);
    }

    private static ToolManager CreateManager(IEnumerable<IToolModule> modules,
        IAppWindowManager windows, IToolOpenFeedback feedback)
    {
        var registered = modules.ToArray();
        return new ToolManager(registered, new ToolRegistry(registered), windows, feedback);
    }

    private sealed class TestModule(ToolId id, int order, BackgroundCapability background,
        bool hideLauncher, ToolReadiness readiness, List<string> calls) : IToolModule
    {
        public ToolDescriptor Descriptor { get; } = new(id, background, StorageCapability.File);
        public LauncherToolMetadata Launcher { get; } = new(id, order, id.ToString(), id.ToString(), id.ToString(),
            null, null, "#000000", 24, "J", hideLauncher);
        public IReadOnlyCollection<ISettingsSection> SettingsSections { get; } = [];
        public int OpenCount { get; private set; }
        public int StartCount { get; private set; }

        public ToolReadiness CheckReadiness()
        {
            calls.Add($"ready:{id}");
            return readiness;
        }

        public void Open()
        {
            OpenCount++;
            calls.Add($"open:{id}");
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            StartCount++;
            calls.Add($"start:{id}");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            calls.Add($"stop:{id}");
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingWindows(List<string> calls) : IAppWindowManager
    {
        public void ShowLauncher() { }
        public void ShowSettings() { }
        public void PrepareToolWindowOpen() => calls.Add("prepare");
        public void CloseAllTools() { }
    }

    private sealed class RecordingFeedback(List<string> calls) : IToolOpenFeedback
    {
        public void Show(string title, string message, ToolOpenMessageKind kind) =>
            calls.Add($"feedback:{title}:{kind}:{message}");
    }
}
