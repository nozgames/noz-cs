namespace NoZ.Editor;

/// <summary>Prepares expensive imports without touching editor or graphics state.
/// The returned exporter runs on the editor thread, only if this is still the latest request.</summary>
public interface IBackgroundImportDocument
{
    bool BackgroundImportEnabled { get; }
    // Interactive startup must not invoke an external converter. Implementations
    // may display a last-good preview, returning false when a refresh is needed.
    bool TryLoadCached() => false;
    IEnumerable<string> ImportDependencies => [];
    Task<BackgroundImportResult> PrepareExportAsync(CancellationToken cancellationToken);
    void ReportImportError(Exception error);
}

/// <summary>Import failures are editor diagnostics, not faults escaping a worker task.</summary>
public sealed record BackgroundImportResult(Action<string, PropertySet>? Export, Exception? Error = null, bool IsCanceled = false)
{
    public static BackgroundImportResult Canceled { get; } = new(null, IsCanceled: true);
    public static Task<BackgroundImportResult> PrepareAsync(Func<Action<string, PropertySet>> prepare,
        CancellationToken cancellationToken) => Task.Run(() =>
    {
        if (cancellationToken.IsCancellationRequested) return Canceled;
        try
        {
            var export = prepare();
            return cancellationToken.IsCancellationRequested ? Canceled : new BackgroundImportResult(export);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return Canceled; }
        catch (Exception error) { return new BackgroundImportResult(null, error); }
    });
}
