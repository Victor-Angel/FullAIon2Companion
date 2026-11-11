using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FullAIon2Companion;

public sealed record GitHubRelease(string Version, string Notes, long Bytes, DateTime? CreatedUtc, string Sha256, string Url);

/// <summary>Updates only from the owner's published GitHub Releases, with asset size and SHA-256 verification.</summary>
public static class GitHubUpdater
{
    const string Repo = "Victor-Angel/FullAIon2Companion";
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(5) };
    static GitHubUpdater()
    {
        http.DefaultRequestHeaders.UserAgent.ParseAdd("FullAIon2Companion-updater/1.0");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public static async Task<GitHubRelease?> CheckAsync(CancellationToken ct = default)
    {
        using var response = await http.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = json.RootElement;
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Regex.IsMatch(tag, @"^v\d+\.\d+\.\d+$")) return null;
        string version = tag[1..];
        string file = $"FullAIon2Companion-{version}-setup.exe";
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != file) continue;
            string url = asset.GetProperty("browser_download_url").GetString() ?? "";
            string digest = asset.TryGetProperty("digest", out var d) ? d.GetString() ?? "" : "";
            if (url != $"https://github.com/{Repo}/releases/download/{tag}/{file}" ||
                !Regex.IsMatch(digest, "^sha256:[a-f0-9]{64}$")) return null;
            DateTime? published = root.TryGetProperty("published_at", out var at) && at.TryGetDateTime(out var date) ? date : null;
            return new(version, root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "", asset.GetProperty("size").GetInt64(), published, digest[7..], url);
        }
        return null;
    }

    public static async Task<string> DownloadAsync(GitHubRelease release, string folder, CancellationToken ct = default)
    {
        string file = $"FullAIon2Companion-{release.Version}-setup.exe";
        if (!Regex.IsMatch(release.Version, @"^\d+\.\d+\.\d+$") ||
            !release.Url.Equals($"https://github.com/{Repo}/releases/download/v{release.Version}/{file}", StringComparison.Ordinal) ||
            release.Bytes <= 0 || !Regex.IsMatch(release.Sha256, "^[a-f0-9]{64}$"))
            throw new InvalidOperationException("La actualización de GitHub no tiene una dirección o huella válida.");
        Directory.CreateDirectory(folder);
        string target = Path.Combine(folder, file), partial = target + ".part";
        try
        {
            using var response = await http.GetAsync(release.Url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(ct))
            await using (var output = File.Create(partial)) await input.CopyToAsync(output, ct);
            string hash;
            long bytes;
            await using (var stream = File.OpenRead(partial))
            {
                bytes = stream.Length;
                hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
            }
            if (bytes != release.Bytes || hash != release.Sha256)
                throw new InvalidOperationException("El instalador no coincide con el tamaño y SHA-256 publicados en GitHub.");
            File.Move(partial, target, true);
            return target;
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }
}
