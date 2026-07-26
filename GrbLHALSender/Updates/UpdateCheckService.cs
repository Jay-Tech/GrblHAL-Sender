using GrbLHALSender.Utility;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace GrbLHALSender.Updates;

public class UpdateCheckService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private const string ReleasesUrl =
        "https://api.github.com/repos/Jay-Tech/GrblHAL-Sender/releases/latest";

    public event EventHandler<string>? StatusMessage;

    public async Task CheckForUpdateAsync()
    {
        try
        {
            var currentVersion = AppVersion.Current;
            if (currentVersion == null)
                return;

            var latestTag = await FetchLatestReleaseTagAsync();
            if (latestTag == null)
                return;

            var tagVersion = latestTag.TrimStart('v');

            if (!Version.TryParse(tagVersion, out var latest))
                return;

            if (!Version.TryParse(currentVersion, out var current))
                return;

            if (latest > current)
            {
                StatusMessage?.Invoke(this,
                    $"A new version is available: v{latest} (current: v{current}). " +
                    $"Download at https://github.com/Jay-Tech/GrblHAL-Sender/releases/latest");
            }
        }
        catch
        {
            // Silently swallow — network failures must not impact the app
        }
    }

    private static async Task<string?> FetchLatestReleaseTagAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl);
        request.Headers.UserAgent.Add(
            new ProductInfoHeaderValue("GrblHAL-Sender", "1.0"));
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

        using var response = await HttpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            return null;

        using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);

        if (doc.RootElement.TryGetProperty("tag_name", out var tagElement))
            return tagElement.GetString();

        return null;
    }

}
