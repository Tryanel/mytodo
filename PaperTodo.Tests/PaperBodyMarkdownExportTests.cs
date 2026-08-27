using System.Windows;
using System.Windows.Threading;
using System.Runtime.ExceptionServices;
using PaperTodo.Plugin;

namespace PaperTodo.Tests;

public sealed class PaperBodyMarkdownExportTests
{
    [Fact]
    public void Full_markdown_export_is_an_opt_in_manifest_capability()
    {
        Assert.Equal(
            PaperBodyCapabilities.FullMarkdownExport,
            PaperBodyPluginRegistry.ParseCapabilities(["fullMarkdownExport"]));
        Assert.Equal(
            PaperBodyCapabilities.None,
            PaperBodyPluginRegistry.ParseCapabilities([]));
        Assert.Equal(1, (int)PaperBodyCapabilities.TextZoom);
        Assert.Equal(2, (int)PaperBodyCapabilities.NoteLinks);
    }

    [Fact]
    public async Task Host_commits_the_live_session_before_returning_full_markdown()
    {
        var session = new ExportSession(
            "# Full document\n\nBody newer than capsule summary.");
        var host = new PaperBodyHost();
        host.Attach(session);

        var markdown = await host.GetFullMarkdownAsync();

        Assert.Equal(1, session.CommitCount);
        Assert.Equal(
            "# Full document\n\nBody newer than capsule summary.",
            markdown);
    }

    [Fact]
    public async Task Host_rejects_sessions_that_did_not_opt_in()
    {
        var host = new PaperBodyHost();
        host.Attach(new PlainSession());

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await host.GetFullMarkdownAsync());
    }

    [Fact]
    public async Task Host_rejects_null_or_failed_provider_results()
    {
        var nullHost = new PaperBodyHost();
        nullHost.Attach(new ExportSession(null));
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await nullHost.GetFullMarkdownAsync());

        var failedHost = new PaperBodyHost();
        failedHost.Attach(new ExportSession("ignored", fail: true));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await failedHost.GetFullMarkdownAsync());
    }

    [Fact]
    public async Task Host_rejects_a_result_from_a_session_that_was_replaced()
    {
        var session = new DeferredExportSession();
        var host = new PaperBodyHost();
        host.Attach(session);

        var export = host.GetFullMarkdownAsync().AsTask();
        await session.Started;
        host.CommitCancelDispose(cancelInteractions: false);
        host.Attach(new PlainSession());
        session.Complete("# stale session");

        await Assert.ThrowsAsync<InvalidOperationException>(() => export);
    }

    private sealed class ExportSession(string? markdown, bool fail = false) :
        IPaperBodySession,
        IPaperMarkdownExportProvider
    {
        public int CommitCount { get; private set; }
        public FrameworkElement View => null!;

        public void Commit() => CommitCount++;

        public ValueTask<string?> GetFullMarkdownAsync(
            CancellationToken cancellationToken = default)
        {
            if (fail)
            {
                throw new InvalidOperationException("provider failure");
            }
            return ValueTask.FromResult(markdown);
        }

        public void Dispose() { }
    }

    private sealed class PlainSession : IPaperBodySession
    {
        public FrameworkElement View => null!;
        public void Dispose() { }
    }

    private sealed class DeferredExportSession :
        IPaperBodySession,
        IPaperMarkdownExportProvider
    {
        private readonly TaskCompletionSource _started = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<string?> _result = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public FrameworkElement View => null!;
        public Task Started => _started.Task;

        public async ValueTask<string?> GetFullMarkdownAsync(
            CancellationToken cancellationToken = default)
        {
            _started.SetResult();
            return await _result.Task.WaitAsync(cancellationToken);
        }

        public void Complete(string markdown) => _result.SetResult(markdown);
        public void Dispose() { }
    }
}

[Collection(TodoBoardWpfSmokeCollection.Name)]
public sealed class PaperBodyMarkdownExportAvailabilityWpfTests
{
    [Fact]
    public void Export_entry_only_appears_for_opted_in_plugin_papers()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                RunAvailabilityScenario();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "WPF export test timed out.");
        if (failure != null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    [Fact]
    public void Closing_a_paper_cancels_a_deferred_plugin_export_without_writing()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                RunClosingExportScenario();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "WPF export-close test timed out.");
        if (failure != null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static void RunAvailabilityScenario()
    {
        var providerId = "test.full-markdown-export.web";
        var unsupportedProviderId = "test.no-markdown-export.web";
        var legacyProviderId = "test.legacy-markdown-export.web";
        var providerDirectory = CreateWebPlugin(providerId, fullMarkdownExport: true);
        var unsupportedDirectory = CreateWebPlugin(
            unsupportedProviderId,
            fullMarkdownExport: false);
        var legacyDirectory = CreateWebPlugin(
            legacyProviderId,
            fullMarkdownExport: true,
            apiVersion: "1.9");
        AppController? controller = null;
        var stateFile = FileSnapshot.Capture(Path.Combine(AppContext.BaseDirectory, "data.json"));
        var backupFile = FileSnapshot.Capture(
            Path.Combine(AppContext.BaseDirectory, "data.backup.json"));
        try
        {
            controller = new AppController(Dispatcher.CurrentDispatcher);
            controller.State.Papers.Clear();
            var papers = new[]
            {
                new PaperData { Id = "todo", Type = PaperTypes.Todo },
                new PaperData
                {
                    Id = "note",
                    Type = PaperTypes.Note,
                    BodyProviderId = PaperBodyProviderIds.Markdown
                },
                new PaperData { Id = "board", Type = PaperTypes.Board },
                new PaperData
                {
                    Id = "supported-plugin",
                    Type = PaperTypes.Note,
                    BodyProviderId = providerId
                },
                new PaperData
                {
                    Id = "unsupported-plugin",
                    Type = PaperTypes.Note,
                    BodyProviderId = unsupportedProviderId
                }
            };
            controller.State.Papers.AddRange(papers);

            var availability = papers.ToDictionary(
                paper => paper.Id,
                paper => controller.GetOrCreatePaperWindow(paper)
                    .CanExportPaperAsMarkdown(),
                StringComparer.Ordinal);

            Assert.True(availability["todo"]);
            Assert.True(availability["note"]);
            Assert.True(availability["board"]);
            Assert.True(availability["supported-plugin"]);
            Assert.False(availability["unsupported-plugin"]);
            Assert.False(controller.PaperBodyPlugins.TryGet(legacyProviderId, out _));
        }
        finally
        {
            controller?.Dispose();
            stateFile.Restore();
            backupFile.Restore();
            Directory.Delete(providerDirectory, recursive: true);
            Directory.Delete(unsupportedDirectory, recursive: true);
            Directory.Delete(legacyDirectory, recursive: true);
        }
    }

    private static void RunClosingExportScenario()
    {
        var exportPath = Path.Combine(
            Path.GetTempPath(),
            $"papertodo-plugin-export-{Guid.NewGuid():N}.md");
        var stateFile = FileSnapshot.Capture(Path.Combine(AppContext.BaseDirectory, "data.json"));
        var backupFile = FileSnapshot.Capture(
            Path.Combine(AppContext.BaseDirectory, "data.backup.json"));
        AppController? controller = null;
        try
        {
            controller = new AppController(Dispatcher.CurrentDispatcher);
            controller.State.Papers.Clear();
            var paper = new PaperData
            {
                Id = "closing-plugin-export",
                Type = PaperTypes.Note,
                Title = "Closing export",
                BodyProviderId = "test.deferred-export",
                IsVisible = true,
                X = -30000,
                Y = -30000
            };
            controller.State.Papers.Add(paper);
            var window = controller.GetOrCreatePaperWindow(paper);
            window.ShowActivated = false;
            window.ShowInTaskbar = false;
            window.Show();
            Drain(window.Dispatcher);

            var hostField = typeof(PaperWindow).GetField(
                "_paperBodyHost",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            var host = Assert.IsType<PaperBodyHost>(hostField?.GetValue(window));
            host.CommitCancelDispose(cancelInteractions: true);
            var session = new DeferredExportSession();
            host.Attach(session);

            var export = window.ExportPaperAsMarkdownToPathAsync(
                exportPath,
                "Closing export");
            Assert.True(session.Started.IsCompleted);

            window.CloseForReal(saveBeforeClose: false);
            session.Complete("# stale export");
            Drain(window.Dispatcher);

            Assert.True(export.IsCompleted);
            export.GetAwaiter().GetResult();
            Assert.False(File.Exists(exportPath));
        }
        finally
        {
            controller?.Dispose();
            File.Delete(exportPath);
            stateFile.Restore();
            backupFile.Restore();
        }
    }

    private static void Drain(Dispatcher dispatcher)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            var frame = new DispatcherFrame();
            _ = dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                (Action)(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
    }

    private sealed class DeferredExportSession :
        IPaperBodySession,
        IPaperMarkdownExportProvider
    {
        private readonly TaskCompletionSource _started = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<string?> _result = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public FrameworkElement View => null!;
        public Task Started => _started.Task;

        public async ValueTask<string?> GetFullMarkdownAsync(
            CancellationToken cancellationToken = default)
        {
            _started.SetResult();
            return await _result.Task.WaitAsync(cancellationToken);
        }

        public void Complete(string markdown) => _result.TrySetResult(markdown);
        public void Dispose() { }
    }

    private static string CreateWebPlugin(
        string providerId,
        bool fullMarkdownExport,
        string apiVersion = "1.10")
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "plugins", providerId);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
        var webDirectory = Path.Combine(directory, "web");
        Directory.CreateDirectory(webDirectory);
        File.WriteAllText(Path.Combine(webDirectory, "index.html"), "<!doctype html><p>test</p>");
        var capability = fullMarkdownExport
            ? ",\n  \"capabilities\": [\"fullMarkdownExport\"]"
            : "";
        File.WriteAllText(
            Path.Combine(directory, "plugin.json"),
            $$"""
            {
              "kind": "web",
              "id": "{{providerId}}",
              "name": "Export test",
              "version": "1.0.0",
              "apiVersion": "{{apiVersion}}",
              "stateVersion": 1,
              "entry": "web/index.html"{{capability}}
            }
            """);
        return directory;
    }

    private sealed record FileSnapshot(string Path, byte[]? Contents)
    {
        public static FileSnapshot Capture(string path) =>
            new(path, File.Exists(path) ? File.ReadAllBytes(path) : null);

        public void Restore()
        {
            if (Contents == null)
            {
                File.Delete(Path);
            }
            else
            {
                File.WriteAllBytes(Path, Contents);
            }
        }
    }
}
