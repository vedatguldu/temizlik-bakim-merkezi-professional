using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TemizlikMasaUygulamasi
{
    internal static class GitHubUpdateService
    {
        private static readonly HttpClient HttpClient = CreateClient();

        public static async Task<UpdateCheckResult> CheckLatestReleaseAsync(
            string owner,
            string repo,
            Version currentVersion,
            CancellationToken cancellationToken)
        {
            var apiUrl = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
            using var response = await HttpClient.GetAsync(apiUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.TryGetProperty("tag_name", out var tagProp)
                ? tagProp.GetString() ?? string.Empty
                : string.Empty;

            var latestVersion = ParseVersion(tagName);
            var releaseTitle = root.TryGetProperty("name", out var nameProp)
                ? nameProp.GetString() ?? tagName
                : tagName;
            var releaseNotes = root.TryGetProperty("body", out var bodyProp)
                ? bodyProp.GetString() ?? string.Empty
                : string.Empty;
            var htmlUrl = root.TryGetProperty("html_url", out var htmlProp)
                ? htmlProp.GetString() ?? string.Empty
                : string.Empty;

            var downloadUrl = string.Empty;
            if (root.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assetsProp.EnumerateArray())
                {
                    if (!asset.TryGetProperty("name", out var assetNameProp) ||
                        !asset.TryGetProperty("browser_download_url", out var assetUrlProp))
                    {
                        continue;
                    }

                    var assetName = assetNameProp.GetString() ?? string.Empty;
                    var assetUrl = assetUrlProp.GetString() ?? string.Empty;
                    if (assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                        assetName.Contains("Setup", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = assetUrl;
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                downloadUrl = htmlUrl;
            }

            var isUpdateAvailable = latestVersion > currentVersion;
            return new UpdateCheckResult(
                isUpdateAvailable,
                currentVersion,
                latestVersion,
                releaseTitle,
                releaseNotes,
                downloadUrl,
                htmlUrl);
        }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "TemizlikBakimMerkeziProfessional/3.1");
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
            return client;
        }

        private static Version ParseVersion(string raw)
        {
            var match = Regex.Match(raw ?? string.Empty, @"(\d+)\.(\d+)\.(\d+)");
            if (!match.Success)
            {
                return new Version(0, 0, 0);
            }

            var major = int.Parse(match.Groups[1].Value);
            var minor = int.Parse(match.Groups[2].Value);
            var build = int.Parse(match.Groups[3].Value);
            return new Version(major, minor, build);
        }
    }

    internal sealed class UpdateCheckResult
    {
        public UpdateCheckResult(
            bool isUpdateAvailable,
            Version currentVersion,
            Version latestVersion,
            string releaseTitle,
            string releaseNotes,
            string downloadUrl,
            string releaseUrl)
        {
            IsUpdateAvailable = isUpdateAvailable;
            CurrentVersion = currentVersion;
            LatestVersion = latestVersion;
            ReleaseTitle = releaseTitle;
            ReleaseNotes = releaseNotes;
            DownloadUrl = downloadUrl;
            ReleaseUrl = releaseUrl;
        }

        public bool IsUpdateAvailable { get; }

        public Version CurrentVersion { get; }

        public Version LatestVersion { get; }

        public string ReleaseTitle { get; }

        public string ReleaseNotes { get; }

        public string DownloadUrl { get; }

        public string ReleaseUrl { get; }
    }
}
