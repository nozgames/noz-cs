//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NoZ.Editor;

public class DeviceCodeResponse
{
    [JsonPropertyName("device_code")] public string DeviceCode { get; set; } = "";
    [JsonPropertyName("user_code")] public string UserCode { get; set; } = "";
    [JsonPropertyName("verification_uri")] public string VerificationUri { get; set; } = "";
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    [JsonPropertyName("interval")] public int Interval { get; set; }
}

public class TokenResponse
{
    [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
    [JsonPropertyName("token_type")] public string? TokenType { get; set; }
    [JsonPropertyName("scope")] public string? Scope { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
}

public class GitRef
{
    [JsonPropertyName("ref")] public string Ref { get; set; } = "";
    [JsonPropertyName("object")] public GitRefObject Object { get; set; } = new();
}

public class GitRefObject
{
    [JsonPropertyName("sha")] public string Sha { get; set; } = "";
}

public class GitTree
{
    [JsonPropertyName("sha")] public string Sha { get; set; } = "";
    [JsonPropertyName("tree")] public List<GitTreeEntry> Tree { get; set; } = [];
    [JsonPropertyName("truncated")] public bool Truncated { get; set; }
}

public class GitTreeEntry
{
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("mode")] public string Mode { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("sha")] public string Sha { get; set; } = "";
    [JsonPropertyName("size")] public long? Size { get; set; }
}

public class GitBlobRequest
{
    [JsonPropertyName("content")] public string Content { get; set; } = "";
    [JsonPropertyName("encoding")] public string Encoding { get; set; } = "base64";
}

public class GitBlobResponse
{
    [JsonPropertyName("sha")] public string Sha { get; set; } = "";
}

public class GitTreeCreateRequest
{
    [JsonPropertyName("base_tree")] public string? BaseTree { get; set; }
    [JsonPropertyName("tree")] public List<GitTreeCreateEntry> Tree { get; set; } = [];
}

public class GitTreeCreateEntry
{
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("mode")] public string Mode { get; set; } = "100644";
    [JsonPropertyName("type")] public string Type { get; set; } = "blob";
    [JsonPropertyName("sha")] public string? Sha { get; set; }
}

public class GitTreeCreateResponse
{
    [JsonPropertyName("sha")] public string Sha { get; set; } = "";
}

public class GitCommitCreateRequest
{
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("tree")] public string Tree { get; set; } = "";
    [JsonPropertyName("parents")] public List<string> Parents { get; set; } = [];
}

public class GitCommitResponse
{
    [JsonPropertyName("sha")] public string Sha { get; set; } = "";
}

public class GitRefUpdateRequest
{
    [JsonPropertyName("sha")] public string Sha { get; set; } = "";
}

public class RepoInfo
{
    [JsonPropertyName("full_name")] public string FullName { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("owner")] public RepoOwner Owner { get; set; } = new();
    [JsonPropertyName("default_branch")] public string DefaultBranch { get; set; } = "main";
}

public class RepoOwner
{
    [JsonPropertyName("login")] public string Login { get; set; } = "";
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DeviceCodeResponse))]
[JsonSerializable(typeof(TokenResponse))]
[JsonSerializable(typeof(GitRef))]
[JsonSerializable(typeof(GitTree))]
[JsonSerializable(typeof(GitBlobRequest))]
[JsonSerializable(typeof(GitBlobResponse))]
[JsonSerializable(typeof(GitTreeCreateRequest))]
[JsonSerializable(typeof(GitTreeCreateResponse))]
[JsonSerializable(typeof(GitCommitCreateRequest))]
[JsonSerializable(typeof(GitCommitResponse))]
[JsonSerializable(typeof(GitRefUpdateRequest))]
[JsonSerializable(typeof(List<RepoInfo>))]
internal partial class GitHubJsonContext : JsonSerializerContext;

public class GitHubApi(string token)
{
    private readonly HttpClient _http = CreateClient(token);

    private static HttpClient CreateClient(string token)
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("NoZ-Editor", "1.0"));
        return client;
    }

    public async Task<GitRef> GetRefAsync(string owner, string repo, string branch, CancellationToken ct = default)
    {
        var json = await _http.GetStringAsync($"https://api.github.com/repos/{owner}/{repo}/git/ref/heads/{branch}", ct);
        return JsonSerializer.Deserialize(json, GitHubJsonContext.Default.GitRef)!;
    }

    public async Task<GitTree> GetTreeAsync(string owner, string repo, string treeSha, bool recursive = false, CancellationToken ct = default)
    {
        var url = $"https://api.github.com/repos/{owner}/{repo}/git/trees/{treeSha}";
        if (recursive)
            url += "?recursive=1";
        var json = await _http.GetStringAsync(url, ct);
        return JsonSerializer.Deserialize(json, GitHubJsonContext.Default.GitTree)!;
    }

    public struct SubtreeResult
    {
        public GitTree Tree;
        public string? SubmoduleOwner;
        public string? SubmoduleRepo;
        public string? SubmoduleCommit;
        public string? SubmoduleSubPath; // path within the submodule (e.g. "engine/assets")
    }

    public async Task<SubtreeResult?> GetSubtreeAsync(string owner, string repo, string rootTreeSha, string path,
        Dictionary<string, (string Owner, string Repo)>? submodules = null, CancellationToken ct = default)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var currentSha = rootTreeSha;
        var currentOwner = owner;
        var currentRepo = repo;
        string? submoduleCommit = null;

        for (var i = 0; i < segments.Length; i++)
        {
            var tree = await GetTreeAsync(currentOwner, currentRepo, currentSha, recursive: false, ct: ct);
            var segment = segments[i];
            var entry = tree.Tree.FirstOrDefault(e => e.Path == segment);
            if (entry == null)
                return null;

            if (entry.Type == "tree")
            {
                currentSha = entry.Sha;
            }
            else if (entry.Type == "commit" && submodules != null)
            {
                // Submodule — look up the submodule's repo from .gitmodules
                var submodulePath = string.Join("/", segments[..( i + 1)]);
                if (!submodules.TryGetValue(submodulePath, out var sub))
                    return null;

                currentOwner = sub.Owner;
                currentRepo = sub.Repo;
                submoduleCommit = entry.Sha;
                currentSha = entry.Sha;
            }
            else
            {
                return null;
            }
        }

        var resultTree = await GetTreeAsync(currentOwner, currentRepo, currentSha, recursive: true, ct: ct);

        // Compute the sub-path within the submodule (segments after the submodule boundary)
        string? subPath = null;
        if (submoduleCommit != null)
        {
            // Find which segment was the submodule
            for (var i = 0; i < segments.Length; i++)
            {
                var checkPath = string.Join("/", segments[..(i + 1)]);
                if (submodules!.ContainsKey(checkPath) && i + 1 < segments.Length)
                {
                    subPath = string.Join("/", segments[(i + 1)..]);
                    break;
                }
            }
        }

        return new SubtreeResult
        {
            Tree = resultTree,
            SubmoduleOwner = currentOwner != owner ? currentOwner : null,
            SubmoduleRepo = currentRepo != repo ? currentRepo : null,
            SubmoduleCommit = submoduleCommit,
            SubmoduleSubPath = subPath,
        };
    }

    public async Task<byte[]> DownloadBlobAsync(string owner, string repo, string branch, string path, CancellationToken ct = default)
    {
        return await _http.GetByteArrayAsync($"https://raw.githubusercontent.com/{owner}/{repo}/{branch}/{path}", ct);
    }

    public async Task<GitBlobResponse> CreateBlobAsync(string owner, string repo, byte[] content, CancellationToken ct = default)
    {
        var request = new GitBlobRequest { Content = Convert.ToBase64String(content) };
        var body = JsonSerializer.Serialize(request, GitHubJsonContext.Default.GitBlobRequest);
        var response = await _http.PostAsync(
            $"https://api.github.com/repos/{owner}/{repo}/git/blobs",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"), ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize(json, GitHubJsonContext.Default.GitBlobResponse)!;
    }

    public async Task<GitTreeCreateResponse> CreateTreeAsync(string owner, string repo, GitTreeCreateRequest request, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(request, GitHubJsonContext.Default.GitTreeCreateRequest);
        var response = await _http.PostAsync(
            $"https://api.github.com/repos/{owner}/{repo}/git/trees",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"), ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize(json, GitHubJsonContext.Default.GitTreeCreateResponse)!;
    }

    public async Task<GitCommitResponse> CreateCommitAsync(string owner, string repo, GitCommitCreateRequest request, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(request, GitHubJsonContext.Default.GitCommitCreateRequest);
        var response = await _http.PostAsync(
            $"https://api.github.com/repos/{owner}/{repo}/git/commits",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"), ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize(json, GitHubJsonContext.Default.GitCommitResponse)!;
    }

    public async Task UpdateRefAsync(string owner, string repo, string branch, string sha, CancellationToken ct = default)
    {
        var request = new GitRefUpdateRequest { Sha = sha };
        var body = JsonSerializer.Serialize(request, GitHubJsonContext.Default.GitRefUpdateRequest);
        var response = await _http.PatchAsync(
            $"https://api.github.com/repos/{owner}/{repo}/git/refs/heads/{branch}",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"), ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<List<RepoInfo>> GetReposAsync(CancellationToken ct = default)
    {
        var json = await _http.GetStringAsync("https://api.github.com/user/repos?per_page=100&sort=updated", ct);
        return JsonSerializer.Deserialize(json, GitHubJsonContext.Default.ListRepoInfo)!;
    }

    public static async Task<DeviceCodeResponse> RequestDeviceCodeAsync(string clientId, CancellationToken ct = default)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var content = new FormUrlEncodedContent([
            new("client_id", clientId),
            new("scope", "repo"),
        ]);
        var response = await http.PostAsync("https://github.com/login/device/code", content, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize(json, GitHubJsonContext.Default.DeviceCodeResponse)!;
    }

    public static async Task<TokenResponse> PollTokenAsync(string clientId, string deviceCode, CancellationToken ct = default)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var content = new FormUrlEncodedContent([
            new("client_id", clientId),
            new("device_code", deviceCode),
            new("grant_type", "urn:ietf:params:oauth:grant-type:device_code"),
        ]);
        var response = await http.PostAsync("https://github.com/login/oauth/access_token", content, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize(json, GitHubJsonContext.Default.TokenResponse)!;
    }

    public void Dispose() => _http.Dispose();
}
