using System.IO;
using PaperTodo.Plugin;

namespace PaperTodo;

/// <summary>
/// Owns the replace/invoke/dispose boundary for one paper-body session. PaperWindow remains
/// responsible for WPF placement and provider selection; plugin exceptions stop here.
/// </summary>
internal sealed class PaperBodyHost
{
    public IPaperBodySession? Current { get; private set; }

    public bool HasCurrent => Current != null;

    public void Attach(IPaperBodySession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (Current != null && !ReferenceEquals(Current, session))
        {
            throw new InvalidOperationException(
                "A paper-body session is already attached.");
        }
        Current = session;
    }

    public Exception? Invoke(Action<IPaperBodySession> callback)
    {
        var session = Current;
        if (session == null)
        {
            return null;
        }

        try
        {
            callback(session);
            return null;
        }
        catch (Exception ex)
        {
            return ex.GetBaseException();
        }
    }

    public async ValueTask<string> GetFullMarkdownAsync(
        CancellationToken cancellationToken = default)
    {
        var session = Current;
        if (session is not IPaperMarkdownExportProvider provider)
        {
            throw new InvalidOperationException(
                "The current paper-body session does not provide Markdown export.");
        }

        session.Commit();
        var markdown = await provider.GetFullMarkdownAsync(cancellationToken);
        if (!ReferenceEquals(Current, session))
        {
            throw new InvalidOperationException(
                "The paper-body session changed during Markdown export.");
        }
        return markdown ?? throw new InvalidDataException(
            "The paper-body session returned an invalid Markdown export.");
    }

    public void CommitCancelDispose(bool cancelInteractions)
    {
        var session = Current;
        Current = null;
        if (session == null)
        {
            return;
        }

        try { session.Commit(); } catch { }
        if (cancelInteractions)
        {
            try { session.CancelInteractions(); } catch { }
        }
        try { session.Dispose(); } catch { }
    }
}
