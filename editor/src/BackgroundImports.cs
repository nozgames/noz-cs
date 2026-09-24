namespace NoZ.Editor;

/// <summary>Save bursts coalesce; a superseded conversion can never publish.</summary>
internal static class BackgroundImports
{
    private sealed class Job
    {
        public long Due = Environment.TickCount64 + 300;
        public CancellationTokenSource Cancellation = new();
        public Task<BackgroundImportResult>? Task;
        public bool Restart;
        public int RetryCount;
    }

    private static readonly Dictionary<Document, Job> Jobs = [];

    internal static void Queue(Document document)
    {
        if (!Jobs.TryGetValue(document, out var job)) Jobs.Add(document, new Job());
        else
        {
            job.Due = Environment.TickCount64 + 300;
            job.RetryCount = 0;
            if (job.Task != null)
            {
                job.Restart = true;
                job.Cancellation.Cancel();
            }
        }
    }

    internal static void Update(Action<Document, Action<string, PropertySet>> export)
    {
        foreach (var (document, job) in Jobs.ToArray())
        {
            var importer = (IBackgroundImportDocument)document;
            if (document.IsDisposed || !File.Exists(document.Path))
            {
                job.Restart = false;
                job.Cancellation.Cancel();
            }
            if (job.Task is { IsCompleted: false }) continue;
            if (job.Task != null)
            {
                try
                {
                    // Third-party importers may still return a canceled Task.
                    // Observing ordinary cancellation must not throw on the UI thread.
                    var prepared = job.Task.IsCanceled ? BackgroundImportResult.Canceled : job.Task.GetAwaiter().GetResult();
                    if (!job.Restart && !document.IsDisposed && !job.Cancellation.IsCancellationRequested && !prepared.IsCanceled)
                    {
                        if (prepared.Error is { } error) ReportFailure(error);
                        else if (prepared.Export is { } publish) export(document, publish);
                    }
                }
                catch (OperationCanceledException) when (job.Cancellation.IsCancellationRequested) { }
                catch (Exception error)
                {
                    if (!job.Restart && !document.IsDisposed && !job.Cancellation.IsCancellationRequested) ReportFailure(error);
                }
                job.Cancellation.Dispose();
                if (!job.Restart || document.IsDisposed)
                {
                    Jobs.Remove(document);
                    document.IsQueuedForExport = false;
                    continue;
                }
                job.Task = null;
                job.Cancellation = new();
                job.Restart = false;
            }
            if (document.IsDisposed || job.Cancellation.IsCancellationRequested)
            {
                job.Cancellation.Dispose();
                Jobs.Remove(document);
                document.IsQueuedForExport = false;
                continue;
            }
            if (Environment.TickCount64 < job.Due) continue;
            try { job.Task = importer.PrepareExportAsync(job.Cancellation.Token); }
            catch (Exception error) { job.Task = Task.FromResult(new BackgroundImportResult(null, error)); }

            void ReportFailure(Exception error)
            {
                if (error is IOException or UnauthorizedAccessException && job.RetryCount++ < 8)
                {
                    job.Restart = true;
                    job.Due = Environment.TickCount64 + 250;
                }
                else
                {
                    importer.ReportImportError(error);
                    Log.Error($"Failed to import '{document.Name}': {error.Message}");
                }
            }
        }
    }

    internal static void Shutdown()
    {
        foreach (var (document, job) in Jobs)
        {
            job.Cancellation.Cancel();
            document.IsQueuedForExport = false;
            if (job.Task != null)
                _ = job.Task.ContinueWith(task => { _ = task.Exception; job.Cancellation.Dispose(); }, TaskScheduler.Default);
            else job.Cancellation.Dispose();
        }
        Jobs.Clear();
    }
}
