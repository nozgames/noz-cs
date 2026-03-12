//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NoZ.Editor;

public class GenerationRequest
{
    [JsonIgnore]
    public string Server { get; set; } = "";

    public string Workflow { get; set; } = "sprite";
    public string? Image { get; set; }
    public string? Mask { get; set; }
    public string Prompt { get; set; } = "";
    public string? NegativePrompt { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public float Strength { get; set; } = 0.85f;
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int Steps { get; set; } = 20;
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public float GuidanceScale { get; set; } = 6.0f;
    public long? Seed { get; set; }
    public float StyleStrength { get; set; }
    public GenerationRefine? Refine { get; set; }
    public GenerationStyleBlock? Style { get; set; }
    public GenerationLora? Lora { get; set; }
}

public class GenerationStyleBlock
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public float Strength { get; set; } = 0.5f;
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public float InpaintStrength { get; set; } = 0.2f;
    public List<GenerationStyleReference>? References { get; set; }
}

public class GenerationStyleReference
{
    public string Image { get; set; } = "";
}

public class GenerationLora
{
    public string Name { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public float Strength { get; set; } = 0.8f;
}

public class LoraInfo
{
    public string Name { get; set; } = "";
    public float DefaultStrength { get; set; } = 0.8f;
}

public class GenerationRefine
{
    public string Prompt { get; set; } = "";
    public string? NegativePrompt { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public float Strength { get; set; } = 0.64f;
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int Steps { get; set; } = 10;
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public float GuidanceScale { get; set; } = 6.0f;
    public long? Seed { get; set; }
}

public class GenerationResponse
{
    public string Image { get; set; } = "";
    public long Seed { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public enum GenerationWorkflow
{
    Sprite
}

public enum GenerationState
{
    Queued,
    Running,
    Completed,
    Failed
}

public class GenerationStatus
{
    public GenerationState State { get; set; }
    public string? JobId { get; set; }

    // Queued
    public int QueuePosition { get; set; }

    // Running
    public float Progress { get; set; }

    // Completed
    public GenerationResponse? Result { get; set; }

    // Failed
    public string? Error { get; set; }
    public bool IsTerminal => State is GenerationState.Completed or GenerationState.Failed;
}

// POST /generate response
public class GenerationSubmitResponse
{
    public string JobId { get; set; } = "";
    public string Status { get; set; } = "";
    public int Position { get; set; }
}

// GET /jobs/{job_id} response
public class GenerationJobResponse
{
    public string JobId { get; set; } = "";
    public string Status { get; set; } = "";
    public int? QueuePosition { get; set; }
    public float? Progress { get; set; }
    public GenerationResponse? Result { get; set; }
    public string? Error { get; set; }
}


public static class GenerationClient
{
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static void Generate(GenerationRequest request, Action<GenerationStatus> callback,
        CancellationToken cancellationToken = default)
    {
        Task.Run(async () =>
        {
            try
            {
                // Phase 1: Submit job
                var json = JsonSerializer.Serialize(request, _jsonOptions);

                // Debug: write request JSON to tmp folder
                try
                {
                    var tmpDir = System.IO.Path.Combine(EditorApplication.ProjectPath, "tmp");
                    Directory.CreateDirectory(tmpDir);
                    File.WriteAllText(System.IO.Path.Combine(tmpDir, "generation_request.json"), json);
                }
                catch { }

                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _http.PostAsync($"{request.Server}/generate", content, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    Log.Error($"Generation submit failed {response.StatusCode}: {errorBody}");
                    EditorApplication.RunOnMainThread(() => callback(new GenerationStatus
                    {
                        State = GenerationState.Failed,
                        Error = $"Submit failed: {response.StatusCode}"
                    }));
                    return;
                }

                var submitJson = await response.Content.ReadAsStringAsync();
                var submitResult = JsonSerializer.Deserialize<GenerationSubmitResponse>(submitJson, _jsonOptions);
                if (submitResult == null || string.IsNullOrEmpty(submitResult.JobId))
                {
                    Log.Error("Generation submit returned no job_id");
                    EditorApplication.RunOnMainThread(() => callback(new GenerationStatus
                    {
                        State = GenerationState.Failed,
                        Error = "No job_id returned"
                    }));
                    return;
                }

                var jobId = submitResult.JobId;
                Log.Info($"Generation job submitted: {jobId}");

                EditorApplication.RunOnMainThread(() => callback(new GenerationStatus
                {
                    State = GenerationState.Queued,
                    JobId = jobId,
                    QueuePosition = submitResult.Position
                }));

                // Phase 2: Poll loop
                var pollUrl = $"{request.Server}/jobs/{jobId}";
                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(500, cancellationToken);

                    var pollResponse = await _http.GetAsync(pollUrl, cancellationToken);
                    if (!pollResponse.IsSuccessStatusCode)
                    {
                        var errorBody = await pollResponse.Content.ReadAsStringAsync();
                        Log.Error($"Poll failed {pollResponse.StatusCode}: {errorBody}");
                        EditorApplication.RunOnMainThread(() => callback(new GenerationStatus
                        {
                            State = GenerationState.Failed,
                            JobId = jobId,
                            Error = $"Poll failed: {pollResponse.StatusCode}"
                        }));
                        return;
                    }

                    var pollJson = await pollResponse.Content.ReadAsStringAsync();
                    var job = JsonSerializer.Deserialize<GenerationJobResponse>(pollJson, _jsonOptions);
                    if (job == null)
                    {
                        Log.Error("Poll returned invalid response");
                        EditorApplication.RunOnMainThread(() => callback(new GenerationStatus
                        {
                            State = GenerationState.Failed,
                            JobId = jobId,
                            Error = "Invalid poll response"
                        }));
                        return;
                    }

                    var status = MapJobToStatus(job);
                    EditorApplication.RunOnMainThread(() => callback(status));

                    if (status.IsTerminal)
                        return;
                }
            }
            catch (OperationCanceledException)
            {
                Log.Info("Generation cancelled");
            }
            catch (Exception ex)
            {
                Log.Error($"Generation failed: {ex.Message}");
                EditorApplication.RunOnMainThread(() => callback(new GenerationStatus
                {
                    State = GenerationState.Failed,
                    Error = ex.Message
                }));
            }
        }, cancellationToken);
    }

    public static List<GenerationStyleReference>? LoadStyleReferences(List<string>? styles)
    {
        if (styles == null || styles.Count == 0)
            return null;

        var refs = new List<GenerationStyleReference>();
        foreach (var name in styles)
        {
            var doc = DocumentManager.Find(AssetType.Texture, name);
            if (doc == null)
            {
                Log.Warning($"Style reference texture '{name}' not found");
                continue;
            }

            if (!File.Exists(doc.Path))
            {
                Log.Warning($"Style reference file not found: {doc.Path}");
                continue;
            }

            var bytes = File.ReadAllBytes(doc.Path);
            var base64 = Convert.ToBase64String(bytes);
            refs.Add(new GenerationStyleReference { Image = base64 });
        }

        return refs.Count > 0 ? refs : null;
    }

    private static List<LoraInfo>? _cachedLoras;
    private static string? _cachedLorasServer;
    private static bool _fetchingLoras;
    private static double _loraRetryTime;

    public static List<LoraInfo>? CachedLoras => _cachedLoras;

    public static void FetchLoras(string server)
    {
        if (_cachedLorasServer == server && _cachedLoras != null)
            return;

        if (_fetchingLoras)
            return;

        if (_cachedLorasServer == server && Time.TotalTime < _loraRetryTime)
            return;

        _cachedLorasServer = server;
        _fetchingLoras = true;
        Task.Run(async () =>
        {
            try
            {
                var response = await _http.GetAsync($"{server}/loras");
                if (!response.IsSuccessStatusCode)
                {
                    Log.Warning($"Failed to fetch LoRAs: {response.StatusCode}");
                    EditorApplication.RunOnMainThread(() =>
                    {
                        _fetchingLoras = false;
                        _loraRetryTime = Time.TotalTime + 5.0;
                    });
                    return;
                }

                var json = await response.Content.ReadAsStringAsync();
                var loras = JsonSerializer.Deserialize<List<LoraInfo>>(json, _jsonOptions);
                EditorApplication.RunOnMainThread(() =>
                {
                    _cachedLoras = loras ?? [];
                    _fetchingLoras = false;
                });
            }
            catch (Exception ex)
            {
                Log.Warning($"Failed to fetch LoRAs: {ex.Message}");
                EditorApplication.RunOnMainThread(() =>
                {
                    _fetchingLoras = false;
                    _loraRetryTime = Time.TotalTime + 5.0;
                });
            }
        });
    }

    private static GenerationStatus MapJobToStatus(GenerationJobResponse job)
    {
        var state = job.Status switch
        {
            "queued" => GenerationState.Queued,
            "running" => GenerationState.Running,
            "completed" => GenerationState.Completed,
            "failed" => GenerationState.Failed,
            _ => GenerationState.Failed
        };

        return new GenerationStatus
        {
            State = state,
            JobId = job.JobId,
            QueuePosition = job.QueuePosition ?? 0,
            Progress = job.Progress ?? 0f,
            Result = job.Result,
            Error = job.Error ?? (state == GenerationState.Failed ? "Unknown error" : null)
        };
    }
}
