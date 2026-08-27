using PaperTodo.Plugin;

namespace PaperTodo.Plugin.ReviewArchive;

public sealed class ReviewArchivePlugin : IPaperBodyPlugin
{
    public string Id => "sample.review-archive.native";
    public string DisplayName => "待办复盘记录池";
    public string Description => "实时记录待办生命周期、提醒变化和完成趋势，长期保存并支持 CSV 与完整 Markdown 导出。";
    public Version Version => new(1, 3, 0);
    public string ApiVersion => "1.10";
    public int StateVersion => 1;
    public PaperBodyCapabilities Capabilities =>
        PaperBodyCapabilities.TextZoom |
        PaperBodyCapabilities.FullMarkdownExport;
    public PaperBodyRuntimeRequirements RuntimeRequirements =>
        PaperBodyRuntimeRequirements.BackgroundUpdates;

    public IPaperBodySession Create(PaperBodyContext context) =>
        new ReviewArchiveSession(context);
}
