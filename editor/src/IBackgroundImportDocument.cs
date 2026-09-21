namespace NoZ.Editor;

/// <summary>Prepares expensive imports without touching editor or graphics state.
/// The returned exporter runs on the editor thread, only if this is still the latest request.</summary>
public interface IBackgroundImportDocument
{
    bool BackgroundImportEnabled { get; }
    IEnumerable<string> ImportDependencies => [];
    Task<Action<string, PropertySet>> PrepareExportAsync(CancellationToken cancellationToken);
    void ReportImportError(Exception error);
}
