using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace YTPlus;

internal enum VideoPlatform
{
    YouTube,
    TikTok,
    Pinterest
}

internal static class VideoPlatformExtensions
{
    public static string DisplayName(this VideoPlatform platform) => platform switch
    {
        VideoPlatform.YouTube => "YouTube",
        VideoPlatform.TikTok => "TikTok",
        VideoPlatform.Pinterest => "Pinterest",
        _ => "YouTube"
    };

    public static string HeaderTitle(this VideoPlatform platform, AppLanguage lang = AppLanguage.Ru) =>
        lang == AppLanguage.En ? $"Download from {platform.DisplayName()}" : $"Скачать с {platform.DisplayName()}";

    public static string HeaderSubtitle(this VideoPlatform platform, AppLanguage lang = AppLanguage.Ru) => (platform, lang) switch
    {
        (VideoPlatform.Pinterest, AppLanguage.Ru) => "Видео, фото и звук",
        (VideoPlatform.Pinterest, AppLanguage.En) => "Video, photo & audio",
        (_, AppLanguage.Ru) => "Видео и звук",
        (_, AppLanguage.En) => "Video & audio",
        _ => "Video & audio"
    };

    public static string Placeholder(this VideoPlatform platform, AppLanguage lang = AppLanguage.Ru) => (platform, lang) switch
    {
        (VideoPlatform.YouTube, _) => "youtube.com/watch?v=…",
        (VideoPlatform.TikTok, AppLanguage.Ru) => "tiktok.com/@user/video/… или vm.tiktok.com/…",
        (VideoPlatform.TikTok, AppLanguage.En) => "tiktok.com/@user/video/… or vm.tiktok.com/…",
        (VideoPlatform.Pinterest, AppLanguage.Ru) => "pinterest.com/pin/… или pin.it/…",
        (VideoPlatform.Pinterest, AppLanguage.En) => "pinterest.com/pin/… or pin.it/…",
        _ => "youtube.com/watch?v=…"
    };

    public static LocalizedText InvalidLinkText(this VideoPlatform platform) => platform switch
    {
        VideoPlatform.YouTube => new(
            "Вставьте ссылку на отдельное видео YouTube.",
            "Paste a link to a single YouTube video."),
        VideoPlatform.TikTok => new(
            "Вставьте ссылку на отдельное видео TikTok.",
            "Paste a link to a single TikTok video."),
        VideoPlatform.Pinterest => new(
            "Вставьте ссылку на пин Pinterest (видео или фото).",
            "Paste a link to a Pinterest pin (video or photo)."),
        _ => new("Вставьте корректную ссылку на видео.", "Paste a valid video link.")
    };

    public static string InvalidLinkMessage(this VideoPlatform platform, AppLanguage lang = AppLanguage.Ru) =>
        platform.InvalidLinkText().Resolve(lang);

    public static Uri ConnectivityProbeUrl(this VideoPlatform platform) => platform switch
    {
        VideoPlatform.YouTube => new Uri("https://www.youtube.com/generate_204"),
        VideoPlatform.TikTok => new Uri("https://www.tiktok.com/"),
        VideoPlatform.Pinterest => new Uri("https://www.pinterest.com/"),
        _ => new Uri("https://www.youtube.com/generate_204")
    };
}

internal sealed record MediaLink(VideoPlatform Platform, Uri Url);

internal sealed record DownloadResult(string FilePath, bool IsPhoto = false);

internal sealed record MediaFormat(
    [property: JsonPropertyName("extension")] string Extension,
    string Label,
    bool IsAudio,
    string[] Options)
{
    public string OutputTemplate
    {
        get
        {
            var resolution = IsAudio ? "" : "%(height& [{}p]|)s";
            var format = Extension == "mp4" ? "" : $" [{Label}]";
            return $"%(title).170B [%(id)s]{resolution}{format}.%(ext)s";
        }
    }

    public string[] Arguments(int height, VideoPlatform platform = VideoPlatform.YouTube)
    {
        if (IsAudio)
            return ["--format", "ba/b", "--extract-audio", "--audio-format", Extension, .. Options];

        // Use yt-dlp's `res:<height>` sort (evaluates the smaller side) across all platforms
        // so both horizontal videos and vertical videos (YouTube Shorts, TikTok, Pinterest)
        // pick the requested tier instead of dropping 1080x1920 down to 480p.
        if (height == 0)
            return ["--format", "bv*+ba/b", .. Options];

        var resToken = $"res:{height.ToString(CultureInfo.InvariantCulture)}";
        var adjusted = new List<string>(Options);
        var sortIndex = adjusted.IndexOf("--format-sort");
        if (sortIndex >= 0 && sortIndex + 1 < adjusted.Count)
        {
            var current = adjusted[sortIndex + 1];
            if (current.StartsWith("res,", StringComparison.Ordinal))
                adjusted[sortIndex + 1] = $"{resToken},{current[4..]}";
            else if (current == "res")
                adjusted[sortIndex + 1] = resToken;
            else
                adjusted[sortIndex + 1] = $"{resToken},{current}";
        }
        else
        {
            adjusted.Add("--format-sort");
            adjusted.Add(resToken);
        }
        return ["--format", "bv*+ba/b", .. adjusted];
    }
}

internal sealed record DownloadProfiles(int[] Resolutions, string[] CommonArguments, MediaFormat[] Formats)
{
    public static DownloadProfiles Shared { get; } = Load();

    private static DownloadProfiles Load()
    {
        using var stream = typeof(DownloadProfiles).Assembly.GetManifestResourceStream("YTPlus.DownloadProfiles.json")
            ?? throw new InvalidOperationException("Download profiles are missing.");
        return JsonSerializer.Deserialize<DownloadProfiles>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Download profiles are invalid.");
    }
}

internal sealed record DownloadTools(string YtDlp, string FfmpegDirectory, string Node)
{
    public static DownloadTools Bundled()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "tools");
        return new(Path.Combine(directory, "yt-dlp.exe"), directory, Path.Combine(directory, "node.exe"));
    }

    public bool Installed => File.Exists(YtDlp) && File.Exists(Node) &&
                             File.Exists(Path.Combine(FfmpegDirectory, "ffmpeg.exe")) &&
                             File.Exists(Path.Combine(FfmpegDirectory, "ffprobe.exe"));
}

internal sealed record DownloadProgress(string? Title = null, bool Finalizing = false, bool DownloadingPhoto = false,
    bool WaitingForNetwork = false, bool Resuming = false,
    bool IsAudio = false, double? Fraction = null, string? Speed = null, string? Remaining = null);

internal sealed class DownloadFailure(LocalizedText localizedMessage, string? detail = null)
    : Exception(localizedMessage.Ru)
{
    public LocalizedText LocalizedMessage { get; } = localizedMessage;
    public string? Detail { get; } = detail;
}

internal static class DownloadEngine
{
    private const string AlreadyDownloadedPrefix = "[download] ";
    private const string AlreadyDownloadedSuffix = " has already been downloaded";
    private const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    private static readonly HashSet<string> PinterestTlds = new(StringComparer.OrdinalIgnoreCase)
    {
        "com", "ru", "co.uk", "de", "fr", "es", "it", "ca", "jp", "at", "ch",
        "se", "dk", "ie", "nz", "pt", "cl", "ph", "kr", "nl", "pl", "tr", "in",
        "id", "com.au", "com.mx", "co.kr", "com.br"
    };

    private static readonly Regex PinOriginalImageRegex = new(
        @"https://i\.pinimg\.com/originals/[A-Za-z0-9/_\-\.]+\.(?:jpg|jpeg|png|webp|gif)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static Uri? ParseLink(string input) =>
        ParseMediaLink(input, VideoPlatform.YouTube)?.Url;

    public static MediaLink? ParseMediaLink(string input, VideoPlatform? expected = null)
    {
        var address = input.Trim();
        if (address.Length == 0)
            return null;
        if (!address.Contains("://", StringComparison.Ordinal))
            address = "https://" + address;
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0 || !uri.IsDefaultPort)
            return null;

        var host = uri.Host.ToLowerInvariant();
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();

        var parsed = ParseYouTube(host, segments, uri.Query)
            ?? ParseTikTok(host, segments)
            ?? ParsePinterest(host, segments);

        if (parsed is null)
            return null;
        if (expected is { } required && parsed.Platform != required)
            return null;
        return parsed;
    }

    internal static string? ExtractPinterestPinId(Uri uri)
    {
        var host = uri.Host.ToLowerInvariant();
        if (!IsPinterestHost(host)) return null;
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();
        if (segments.Length < 2 || segments[0] != "pin") return null;

        var segment = segments[1];
        if (IsDigits(segment))
            return segment.Length >= 5 ? segment : null;

        var sep = segment.LastIndexOf("--", StringComparison.Ordinal);
        if (sep < 0) return null;
        var tail = segment[(sep + 2)..];
        return IsDigits(tail) && tail.Length >= 5 ? tail : null;
    }

    private static bool IsAsciiToken(string value) =>
        value.Length > 0 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static bool IsDigits(string value) =>
        value.Length > 0 && value.All(char.IsAsciiDigit);

    private static MediaLink? ParseYouTube(string host, string[] segments, string query)
    {
        string? id = null;
        switch (host)
        {
            case "youtu.be" when segments.Length == 1:
                id = segments[0];
                break;
            case "youtube.com" or "www.youtube.com" or "m.youtube.com" or "music.youtube.com":
                if (segments is ["watch"])
                {
                    id = query.TrimStart('?').Split('&')
                        .FirstOrDefault(parameter => parameter.StartsWith("v=", StringComparison.Ordinal))?[2..];
                    if (id is not null)
                        id = Uri.UnescapeDataString(id);
                }
                else if (segments is ["shorts" or "embed" or "live", var pathId])
                    id = pathId;
                break;
        }

        return id is { Length: 11 } && IsAsciiToken(id)
            ? new MediaLink(VideoPlatform.YouTube, new Uri($"https://www.youtube.com/watch?v={id}"))
            : null;
    }

    private static MediaLink? ParseTikTok(string host, string[] segments)
    {
        switch (host)
        {
            case "vm.tiktok.com" or "vt.tiktok.com":
                if (segments.Length == 1 && segments[0].Length is >= 4 and <= 32 && IsAsciiToken(segments[0]))
                    return new MediaLink(VideoPlatform.TikTok, new Uri($"https://{host}/{segments[0]}/"));
                return null;

            case "tiktok.com" or "www.tiktok.com" or "m.tiktok.com" or "www.tiktokv.com":
                if (segments.Length >= 3 && segments[0].StartsWith('@') && segments[0].Length > 1 &&
                    segments[1] == "video" && IsDigits(segments[2]) && segments[2].Length >= 8)
                    return new MediaLink(VideoPlatform.TikTok, new Uri($"https://www.tiktok.com/{segments[0]}/video/{segments[2]}"));

                if (segments is ["share", "video", var shareId] && IsDigits(shareId) && shareId.Length >= 8)
                    return new MediaLink(VideoPlatform.TikTok, new Uri($"https://www.tiktok.com/share/video/{shareId}"));

                if (segments is ["t", var shortCode] && shortCode.Length is >= 4 and <= 32 && IsAsciiToken(shortCode))
                    return new MediaLink(VideoPlatform.TikTok, new Uri($"https://www.tiktok.com/t/{shortCode}/"));

                if (segments is ["v", var rawMobile])
                {
                    var rawId = rawMobile.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                        ? rawMobile[..^5]
                        : rawMobile;
                    if (IsDigits(rawId) && rawId.Length >= 8)
                        return new MediaLink(VideoPlatform.TikTok, new Uri($"https://m.tiktok.com/v/{rawId}.html"));
                }

                if (segments is ["embed", var embedId] && IsDigits(embedId) && embedId.Length >= 8)
                    return new MediaLink(VideoPlatform.TikTok, new Uri($"https://www.tiktok.com/embed/v2/{embedId}"));

                if (segments is ["embed", "v2", var embedV2Id] && IsDigits(embedV2Id) && embedV2Id.Length >= 8)
                    return new MediaLink(VideoPlatform.TikTok, new Uri($"https://www.tiktok.com/embed/v2/{embedV2Id}"));

                return null;

            default:
                return null;
        }
    }

    private static bool IsPinterestHost(string host)
    {
        var parts = host.Split('.');
        var idx = Array.IndexOf(parts, "pinterest");
        if (idx < 0 || idx > 1 || idx == parts.Length - 1)
            return false;
        if (idx == 1)
        {
            var sub = parts[0];
            if (sub.Length is < 1 or > 4 || !sub.All(char.IsAsciiLetterLower))
                return false;
        }
        var tld = string.Join('.', parts[(idx + 1)..]);
        return PinterestTlds.Contains(tld);
    }

    private static MediaLink? ParsePinterest(string host, string[] segments)
    {
        if (host == "pin.it")
        {
            if (segments.Length == 1 && segments[0].Length is >= 4 and <= 32 && IsAsciiToken(segments[0]))
                return new MediaLink(VideoPlatform.Pinterest, new Uri($"https://pin.it/{segments[0]}"));
            return null;
        }

        if (!IsPinterestHost(host) || segments.Length < 2 || segments[0] != "pin")
            return null;

        var segment = segments[1];
        string pinId;
        if (IsDigits(segment))
        {
            pinId = segment;
        }
        else
        {
            var sep = segment.LastIndexOf("--", StringComparison.Ordinal);
            if (sep < 0)
                return null;
            pinId = segment[(sep + 2)..];
            if (!IsDigits(pinId))
                return null;
        }

        return pinId.Length >= 5
            ? new MediaLink(VideoPlatform.Pinterest, new Uri($"https://www.pinterest.com/pin/{pinId}/"))
            : null;
    }

    internal static Func<VideoPlatform, CancellationToken, Task<bool>> WaitForConnectionAsync { get; set; } =
        DefaultWaitForConnectionAsync;

    private static bool ContainsCode(string text, string code) =>
        Regex.IsMatch(text, $@"\b{code}\b");

    internal static bool IsNetworkError(string? detail)
    {
        var text = detail?.ToLowerInvariant() ?? "";
        if (text.Length == 0) return false;
        if (text.Contains("http error 400") || text.Contains("http error 401") ||
            text.Contains("http error 403") || text.Contains("http error 404") ||
            text.Contains("http error 410") || text.Contains("http error 429") ||
            text.Contains("http error 451") ||
            ContainsCode(text, "10204") || ContainsCode(text, "10216") || ContainsCode(text, "10222") ||
            text.Contains("ip address is blocked") || text.Contains("log into an account") ||
            text.Contains("private video") || text.Contains("sign in") ||
            text.Contains("requested format") || text.Contains("no video formats found") ||
            text.Contains("permission denied") || text.Contains("access is denied") ||
            text.Contains("winerror 5") || text.Contains("no space left") || text.Contains("winerror 112"))
            return false;

        string[] markers =
        [
            "timed out", "timeout", "resolve hostname", "getaddrinfo failed",
            "failed to resolve", "temporary failure in name resolution",
            "name or service not known", "nodename nor servname",
            "network is unreachable", "network is down", "no route to host",
            "host is down", "connection reset", "connection refused",
            "connection aborted", "connection closed", "remotedisconnected",
            "broken pipe", "eof occurred", "incomplete read", "incompleteread",
            "did not get any data blocks", "urlerror", "timeouterror",
            "connectionerror", "remote end closed",
            "unable to download webpage", "unable to download video data",
            "unable to download api", "transport error", "sslerror",
            "winerror 10051", "winerror 10053", "winerror 10054", "winerror 10060",
            "winerror 10061", "winerror 10065", "winerror 11001", "winerror 11002"
        ];
        return markers.Any(text.Contains);
    }

    internal static bool IsNetworkFailure(string? detail, string? retryError)
    {
        if (IsNetworkError(detail))
            return true;
        var text = detail?.ToLowerInvariant() ?? "";
        return text.Contains("giving up after", StringComparison.Ordinal) && IsNetworkError(retryError);
    }

    private static async Task<bool> DefaultWaitForConnectionAsync(VideoPlatform platform, CancellationToken cancellation)
    {
        var deadline = DateTime.UtcNow.AddMinutes(30);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(BrowserUserAgent);
        var probeUri = platform.ConnectivityProbeUrl();

        while (DateTime.UtcNow < deadline)
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, probeUri);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation)
                    .ConfigureAwait(false);
                // Any HTTP response confirms network connectivity to the host is back.
                return true;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Still offline; retry after a short pause.
            }

            await Task.Delay(2500, cancellation).ConfigureAwait(false);
        }
        return false;
    }

    public static async Task<string> DownloadAsync(Uri video, MediaFormat format, int height, string folder,
        DownloadTools tools, IProgress<DownloadProgress> progress, CancellationToken cancellation) =>
        (await DownloadMediaAsync(new MediaLink(VideoPlatform.YouTube, video), format, height, folder, tools, progress, cancellation)
            .ConfigureAwait(false)).FilePath;

    public static async Task<DownloadResult> DownloadMediaAsync(MediaLink video, MediaFormat format, int height, string folder,
        DownloadTools tools, IProgress<DownloadProgress> progress, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var destination = NormalizePath(folder);
        if (!Directory.Exists(destination))
            throw new DownloadFailure(new(
                "Папка недоступна. Выберите другую папку.",
                "Folder is unavailable. Choose another folder."));
        EnsureWritableDirectory(destination);

        // Keep the same staging folder across network reconnects so yt-dlp resumes partial files (.part / .ytdl).
        string? stage = null;
        if (format.Extension != "mp4")
        {
            try
            {
                stage = Directory.CreateDirectory(Path.Combine(destination, $".ytplus-{Guid.NewGuid():N}")).FullName;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                throw new DownloadFailure(new(
                    "Папка недоступна для записи. Выберите другую папку.",
                    "Folder is not writable. Choose another folder."), error.Message);
            }
        }

        const int maxReconnectAttempts = 60;
        var reconnectAttempt = 0;

        try
        {
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                using var process = new Process { StartInfo = StartInfo(video, format, height, destination, stage, tools) };
                process.Start();
                using var stop = cancellation.Register(() => Stop(process));
                process.StandardInput.Close();

                string? reportedPath = null;
                string? existingPath = null;
                string? errorLine = null;
                string? lastRetryError = null;
                string? lastDiagnostic = null;
                var sync = new object();
                void AcceptLine(string line)
                {
                    lock (sync)
                    {
                        var split = line.Split('\t');
                        if (split.Length == 2 && split[0] == "YTPLUS_TITLE")
                            progress.Report(new(Title: DecodeString(split[1])));
                        else if (split.Length == 2 && split[0] == "YTPLUS_SAVED")
                            reportedPath = DecodeString(split[1]);
                        else if (split.Length == 5 && split[0] == "YTPLUS_TRANSFER")
                            progress.Report(ParseTransfer(split));
                        else if (line == "YTPLUS_FINALIZING")
                            progress.Report(new(Finalizing: true));
                        else if (line.Length != 0)
                        {
                            if (line.StartsWith(AlreadyDownloadedPrefix, StringComparison.Ordinal) &&
                                line.EndsWith(AlreadyDownloadedSuffix, StringComparison.Ordinal))
                            {
                                var candidate = line[AlreadyDownloadedPrefix.Length..^AlreadyDownloadedSuffix.Length].Trim();
                                if (candidate.Length > 0)
                                    existingPath = candidate;
                            }
                            lastDiagnostic = line;
                            if (line.Contains("got error:", StringComparison.OrdinalIgnoreCase))
                                lastRetryError = line;
                            if (line.StartsWith("ERROR:", StringComparison.Ordinal))
                                errorLine = line;
                        }
                    }
                }
                var output = ReadLinesAsync(process.StandardOutput, AcceptLine);
                var errors = ReadLinesAsync(process.StandardError, AcceptLine);
                await Task.WhenAll(output, errors, process.WaitForExitAsync()).ConfigureAwait(false);

                if (process.ExitCode == 0)
                {
                    if (ValidateSavedFile(reportedPath, destination, format.Extension) is { } saved)
                        return new DownloadResult(saved, IsPhoto: false);
                    if (ValidateSavedFile(existingPath, destination, format.Extension) is { } existing)
                        return new DownloadResult(existing, IsPhoto: false);
                }

                cancellation.ThrowIfCancellationRequested();
                var detail = errorLine ?? lastDiagnostic;
                var lowered = detail?.ToLowerInvariant() ?? "";
                if (video.Platform == VideoPlatform.Pinterest && !format.IsAudio &&
                    (lowered.Contains("no video formats found") || lowered.Contains("unable to extract video")))
                {
                    return await DownloadPinterestImageAsync(video, destination, progress, detail, cancellation)
                        .ConfigureAwait(false);
                }

                if (IsNetworkFailure(detail, lastRetryError) && reconnectAttempt < maxReconnectAttempts)
                {
                    reconnectAttempt++;
                    progress.Report(new(WaitingForNetwork: true));
                    var online = await WaitForConnectionAsync(video.Platform, cancellation).ConfigureAwait(false);
                    cancellation.ThrowIfCancellationRequested();
                    if (online)
                    {
                        progress.Report(new(Resuming: true));
                        continue;
                    }
                }

                var combinedDetail = CombineErrorDetail(detail, lastRetryError);
                throw new DownloadFailure(ErrorMessage(detail, lastRetryError, process.ExitCode, video.Platform, format), combinedDetail);
            }
        }
        finally
        {
            if (stage is not null)
                await CleanupDirectoryAsync(stage).ConfigureAwait(false);
        }
    }

    internal static async Task<DownloadResult> DownloadPinterestImageAsync(MediaLink link, string destination,
        IProgress<DownloadProgress> progress, string? originalDetail, CancellationToken cancellation)
    {
        const int maxReconnectAttempts = 30;
        var reconnectAttempt = 0;

        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            progress.Report(new(DownloadingPhoto: true));
            using var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                AllowAutoRedirect = true
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(BrowserUserAgent);

            try
            {
                var pinId = await ResolvePinterestPinIdAsync(link.Url, client, cancellation).ConfigureAwait(false);
                if (pinId is null)
                    throw new DownloadFailure(new(
                        "Не удалось найти видео или фото в этом пине Pinterest.",
                        "Could not find a video or photo in this Pinterest pin."), originalDetail);

                var (imageUrl, title) = await FetchPinterestImageMetadataAsync(pinId, client, cancellation).ConfigureAwait(false);
                if (imageUrl is null)
                    throw new DownloadFailure(new(
                        "Не удалось найти видео или фото в этом пине Pinterest.",
                        "Could not find a video or photo in this Pinterest pin."), originalDetail);

                if (!IsAllowedPinimgUrl(imageUrl))
                    throw new DownloadFailure(new(
                        "Pinterest вернул некорректный адрес изображения.",
                        "Pinterest returned an invalid image address."), imageUrl.AbsoluteUri);

                if (!string.IsNullOrWhiteSpace(title))
                    progress.Report(new(Title: title));

                using var imageHandler = new HttpClientHandler
                {
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                    AllowAutoRedirect = false
                };
                using var imageClient = new HttpClient(imageHandler) { Timeout = TimeSpan.FromSeconds(30) };
                imageClient.DefaultRequestHeaders.UserAgent.ParseAdd(BrowserUserAgent);

                using var response = await SendPinimgImageRequestAsync(imageClient, imageUrl, cancellation).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var bytes = await response.Content.ReadAsByteArrayAsync(cancellation).ConfigureAwait(false);
                if (bytes.Length == 0)
                    throw new DownloadFailure(new(
                        "Не удалось скачать фото из Pinterest.",
                        "Could not download photo from Pinterest."), originalDetail);

                var contentType = response.Content.Headers.ContentType?.MediaType;
                var extension = DetectImageExtension(bytes, contentType, imageUrl);
                var safeTitle = SanitizeFileName(title) ?? "Pinterest";
                var finalPath = Path.Combine(destination, $"{safeTitle} [{pinId}].{extension}");

                if (File.Exists(finalPath) || Directory.Exists(finalPath))
                {
                    if (IsSafeExistingFile(finalPath))
                        return new DownloadResult(finalPath, IsPhoto: true);
                    throw new DownloadFailure(new(
                        "Не удалось сохранить фото: путь уже занят папкой или ссылкой.",
                        "Could not save photo: target path is occupied by a folder or link."), finalPath);
                }

                var tempPath = Path.Combine(destination, $".ytplus-photo-{Guid.NewGuid():N}.tmp");
                try
                {
                    await File.WriteAllBytesAsync(tempPath, bytes, cancellation).ConfigureAwait(false);
                    File.Move(tempPath, finalPath, overwrite: false);
                    return new DownloadResult(finalPath, IsPhoto: true);
                }
                catch (IOException) when (IsSafeExistingFile(finalPath))
                {
                    try { File.Delete(tempPath); } catch { }
                    return new DownloadResult(finalPath, IsPhoto: true);
                }
                catch (Exception diskError)
                {
                    try { File.Delete(tempPath); } catch { }
                    throw new DownloadFailure(new(
                        "Не удалось сохранить фото в выбранную папку.",
                        "Could not save photo to the selected folder."), diskError.Message);
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                throw;
            }
            catch (DownloadFailure)
            {
                throw;
            }
            catch (Exception error) when (IsNetworkException(error, cancellation) &&
                                          reconnectAttempt < maxReconnectAttempts)
            {
                reconnectAttempt++;
                progress.Report(new(WaitingForNetwork: true));
                var online = await WaitForConnectionAsync(VideoPlatform.Pinterest, cancellation).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested();
                if (online)
                {
                    progress.Report(new(Resuming: true));
                    continue;
                }
                throw new DownloadFailure(AppStrings.ConnectionFailed("Pinterest"), error.Message);
            }
            catch (Exception error)
            {
                throw new DownloadFailure(new(
                    "Не удалось скачать фото из Pinterest.",
                    "Could not download photo from Pinterest."), error.Message);
            }
        }
    }

    internal static bool IsAllowedPinimgUrl(Uri? url) =>
        url is not null &&
        string.Equals(url.Scheme, "https", StringComparison.OrdinalIgnoreCase) &&
        (url.Host.Equals("i.pinimg.com", StringComparison.OrdinalIgnoreCase) ||
         url.Host.EndsWith(".pinimg.com", StringComparison.OrdinalIgnoreCase));

    private static bool IsSafeExistingFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && (info.Attributes & FileAttributes.Directory) == 0 && info.LinkTarget is null;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static async Task<HttpResponseMessage> SendPinimgImageRequestAsync(
        HttpClient client, Uri initialUrl, CancellationToken cancellation)
    {
        var current = initialUrl;
        for (var hop = 0; hop < 5; hop++)
        {
            if (!IsAllowedPinimgUrl(current))
                throw new DownloadFailure(new(
                    "Pinterest вернул некорректный адрес изображения.",
                    "Pinterest returned an invalid image address."), current.AbsoluteUri);

            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.Referrer = new Uri("https://www.pinterest.com/");
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellation)
                .ConfigureAwait(false);
            var code = (int)response.StatusCode;
            if (code is >= 300 and <= 399 && response.Headers.Location is { } location)
            {
                var next = location.IsAbsoluteUri ? location : new Uri(current, location);
                response.Dispose();
                current = next;
                continue;
            }
            return response;
        }
        throw new DownloadFailure(new(
            "Pinterest вернул слишком много перенаправлений.",
            "Pinterest returned too many redirects."), current.AbsoluteUri);
    }

    internal static bool IsNetworkException(Exception error, CancellationToken cancellation) => error switch
    {
        HttpRequestException http => http.StatusCode is null
            || http.StatusCode == HttpStatusCode.RequestTimeout
            || (int)http.StatusCode >= 500,
        TaskCanceledException => !cancellation.IsCancellationRequested,
        IOException => true,
        _ => false
    };

    private static async Task<string?> ResolvePinterestPinIdAsync(Uri url, HttpClient client, CancellationToken cancellation)
    {
        if (ExtractPinterestPinId(url) is { } direct)
            return direct;

        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.RequestTimeout || (int)response.StatusCode >= 500)
            throw new HttpRequestException($"HTTP {(int)response.StatusCode}", null, response.StatusCode);
        var finalUri = response.RequestMessage?.RequestUri;
        return finalUri is null ? null : ExtractPinterestPinId(finalUri);
    }

    private static async Task<(Uri? ImageUrl, string? Title)> FetchPinterestImageMetadataAsync(
        string pinId, HttpClient client, CancellationToken cancellation)
    {
        var dataParam = Uri.EscapeDataString($"{{\"options\":{{\"field_set_key\":\"unauth_react_main_pin\",\"id\":\"{pinId}\"}}}}");
        var apiUri = new Uri($"https://www.pinterest.com/resource/PinResource/get/?data={dataParam}");

        try
        {
            using var apiRequest = new HttpRequestMessage(HttpMethod.Get, apiUri);
            apiRequest.Headers.TryAddWithoutValidation("X-Pinterest-PWS-Handler", "www/[username].js");
            using var apiResponse = await client.SendAsync(apiRequest, cancellation).ConfigureAwait(false);
            if (apiResponse.IsSuccessStatusCode)
            {
                var json = await apiResponse.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
                var parsed = ParsePinResourceJson(json);
                if (parsed.ImageUrl is not null)
                    return parsed;
            }
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) when (!cancellation.IsCancellationRequested) { }
        catch (IOException) { }
        catch (JsonException) { }

        // Fallback: scan pin webpage for i.pinimg.com/originals/... URL
        try
        {
            var html = await client.GetStringAsync($"https://www.pinterest.com/pin/{pinId}/", cancellation)
                .ConfigureAwait(false);
            var match = PinOriginalImageRegex.Match(html);
            if (match.Success && Uri.TryCreate(match.Value, UriKind.Absolute, out var found))
                return (found, null);
        }
        catch (HttpRequestException http) when (http.StatusCode is not null &&
                                                http.StatusCode != HttpStatusCode.RequestTimeout &&
                                                (int)http.StatusCode < 500) { }

        return (null, null);
    }

    internal static (Uri? ImageUrl, string? Title) ParsePinResourceJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("resource_response", out var rr) ||
            !rr.TryGetProperty("data", out var pin) ||
            pin.ValueKind != JsonValueKind.Object)
            return (null, null);

        string? title = null;
        foreach (var field in new[] { "title", "grid_title", "seo_title" })
        {
            if (pin.TryGetProperty(field, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                var text = prop.GetString()?.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    title = text;
                    break;
                }
            }
        }

        if (pin.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Object)
        {
            if (images.TryGetProperty("orig", out var orig) &&
                orig.ValueKind == JsonValueKind.Object &&
                orig.TryGetProperty("url", out var origUrlProp) &&
                Uri.TryCreate(origUrlProp.GetString(), UriKind.Absolute, out var origUri))
                return (origUri, title);

            Uri? best = null;
            var bestArea = -1;
            foreach (var entry in images.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.Object) continue;
                if (!entry.Value.TryGetProperty("url", out var urlProp) ||
                    !Uri.TryCreate(urlProp.GetString(), UriKind.Absolute, out var candidate))
                    continue;
                var w = entry.Value.TryGetProperty("width", out var wp) && wp.TryGetInt32(out var wi) ? wi : 0;
                var h = entry.Value.TryGetProperty("height", out var hp) && hp.TryGetInt32(out var hi) ? hi : 0;
                var area = w * h;
                if (area > bestArea)
                {
                    bestArea = area;
                    best = candidate;
                }
            }
            if (best is not null)
                return (best, title);
        }

        if (pin.TryGetProperty("story_pin_data", out var story) &&
            story.ValueKind == JsonValueKind.Object &&
            story.TryGetProperty("pages", out var pages) &&
            pages.ValueKind == JsonValueKind.Array)
        {
            foreach (var page in pages.EnumerateArray())
            {
                if (page.TryGetProperty("blocks", out var blocks) && blocks.ValueKind == JsonValueKind.Array)
                {
                    foreach (var block in blocks.EnumerateArray())
                    {
                        if (ExtractStoryImage(block) is { } blockUri)
                            return (blockUri, title);
                    }
                }
                if (ExtractStoryImage(page) is { } pageUri)
                    return (pageUri, title);
            }
        }

        return (null, title);
    }

    private static Uri? ExtractStoryImage(JsonElement container)
    {
        if (!container.TryGetProperty("image", out var image) || image.ValueKind != JsonValueKind.Object)
            return null;
        if (!image.TryGetProperty("images", out var imgs) || imgs.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var key in new[] { "originals", "orig", "1200x", "736x" })
        {
            if (imgs.TryGetProperty(key, out var entry) &&
                entry.ValueKind == JsonValueKind.Object &&
                entry.TryGetProperty("url", out var urlProp) &&
                Uri.TryCreate(urlProp.GetString(), UriKind.Absolute, out var uri))
                return uri;
        }
        return null;
    }

    internal static string DetectImageExtension(byte[] data, string? contentType, Uri url)
    {
        if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
            return "png";
        if (data.Length >= 6 && data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x38)
            return "gif";
        if (data.Length >= 12 && data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46 &&
            data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50)
            return "webp";
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return "jpg";

        if (!string.IsNullOrEmpty(contentType))
        {
            var mime = contentType.ToLowerInvariant();
            if (mime.Contains("png")) return "png";
            if (mime.Contains("webp")) return "webp";
            if (mime.Contains("gif")) return "gif";
            if (mime.Contains("jpeg") || mime.Contains("jpg")) return "jpg";
        }

        var pathExt = Path.GetExtension(url.AbsolutePath).TrimStart('.').ToLowerInvariant();
        return pathExt switch
        {
            "png" or "webp" or "gif" or "jpg" => pathExt,
            "jpeg" => "jpg",
            _ => "jpg"
        };
    }

    internal static string? SanitizeFileName(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(title.Length);
        var lastWasSpace = false;
        foreach (var ch in title)
        {
            var bad = ch < 32 || ch is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' ||
                      Array.IndexOf(invalid, ch) >= 0;
            if (bad || char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }
            }
            else
            {
                sb.Append(ch);
                lastWasSpace = false;
            }
        }
        var cleaned = sb.ToString().Trim(' ', '.');
        if (cleaned.Length == 0) return null;
        if (cleaned.Length > 140)
            cleaned = cleaned[..140].Trim(' ', '.');
        return cleaned.Length == 0 ? null : cleaned;
    }

    private static void EnsureWritableDirectory(string destination)
    {
        var probe = Path.Combine(destination, $".ytplus-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            using (File.Create(probe)) { }
            File.Delete(probe);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new DownloadFailure(new(
                "Папка недоступна для записи. Выберите другую папку.",
                "Folder is not writable. Choose another folder."), error.Message);
        }
    }

    private static async Task CleanupDirectoryAsync(string directory)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            if (!Directory.Exists(directory))
                return;
            try
            {
                Directory.Delete(directory, recursive: true);
                return;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                if (attempt == 5) return;
                await Task.Delay(150).ConfigureAwait(false);
            }
        }
    }

    private static ProcessStartInfo StartInfo(MediaLink video, MediaFormat format, int height, string folder,
        string? stage, DownloadTools tools)
    {
        var start = new ProcessStartInfo(tools.YtDlp)
        {
            WorkingDirectory = folder,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        start.Environment["PYTHONIOENCODING"] = "utf-8";
        start.Environment["PYTHONUTF8"] = "1";
        var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        start.Environment["PATH"] = string.IsNullOrEmpty(currentPath)
            ? tools.FfmpegDirectory
            : $"{tools.FfmpegDirectory}{Path.PathSeparator}{currentPath}";

        foreach (var argument in DownloadProfiles.Shared.CommonArguments)
            start.ArgumentList.Add(argument);

        var nodePath = OperatingSystem.IsWindows() ? tools.Node.Replace('\\', '/') : tools.Node;
        string[] arguments = [
            "--ffmpeg-location", tools.FfmpegDirectory, "--js-runtimes", $"node:{nodePath}",
            "--output", format.OutputTemplate, .. format.Arguments(height, video.Platform),
            .. (stage is null ? Array.Empty<string>() : ["--paths", $"temp:{Path.GetFileName(stage)}"]),
            "--", video.Url.AbsoluteUri
        ];
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        return start;
    }

    private static async Task ReadLinesAsync(StreamReader reader, Action<string> accept)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            accept(line);
    }

    private static string? DecodeString(string json)
    {
        try { return JsonSerializer.Deserialize<string>(json); }
        catch (JsonException) { return null; }
    }

    private static DownloadProgress ParseTransfer(string[] split)
    {
        double? fraction = double.TryParse(split[2].Trim(' ', '%'), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
            ? Math.Clamp(value / 100, 0, 1) : null;
        return new(IsAudio: split[1] == "none", Fraction: fraction,
                   Speed: Measurement(split[3]), Remaining: Measurement(split[4]));
    }

    private static string? Measurement(string value) =>
        value.Trim() is "" or "NA" or "Unknown" or "unknown" ? null : value.Trim();

    private static string StripExtendedPrefix(string path)
    {
        if (!OperatingSystem.IsWindows())
            return path;
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            return @"\\" + path[8..];
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
            return path[4..];
        return path;
    }

    private static string NormalizePath(string path)
    {
        var full = StripExtendedPrefix(Path.GetFullPath(path));
        return Path.TrimEndingDirectorySeparator(full);
    }

    private static string? ValidateSavedFile(string? path, string folder, string extension)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var raw = StripExtendedPrefix(path);
            var fullPath = NormalizePath(Path.GetFullPath(raw, folder));
            var parent = Path.GetDirectoryName(fullPath);
            if (parent is not null)
                parent = Path.TrimEndingDirectorySeparator(parent);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!string.Equals(parent, folder, comparison) ||
                !Path.GetExtension(fullPath).Equals("." + extension, StringComparison.OrdinalIgnoreCase))
                return null;

            var info = new FileInfo(fullPath);
            if (!info.Exists || (info.Attributes & FileAttributes.Directory) != 0)
                return null;
            // Allow OneDrive / Cloud Files reparse points, while still rejecting symbolic links.
            if (info.LinkTarget is not null)
                return null;

            return fullPath;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (ArgumentException) { return null; }
    }

    internal static string? CombineErrorDetail(string? detail, string? retryError)
    {
        if (string.IsNullOrEmpty(detail))
            return retryError;
        if (detail.Contains("giving up after", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(retryError))
            return $"{detail}\n{retryError}";
        return detail;
    }

    private static LocalizedText ErrorMessage(string? detail, string? retryError, int exitCode, VideoPlatform platform, MediaFormat format)
    {
        if (exitCode == 0) return AppStrings.MissingOutputFile(format.Label);
        var text = CombineErrorDetail(detail, retryError)?.ToLowerInvariant() ?? "";
        var name = platform.DisplayName();

        if (text.Contains("no video formats found") || text.Contains("unable to extract video"))
        {
            return platform switch
            {
                VideoPlatform.Pinterest => format.IsAudio
                    ? new(
                        "В этом пине только фото, без звуковой дорожки. Выберите MP4, чтобы скачать изображение.",
                        "This pin contains only a photo with no audio track. Choose MP4 to download the image.")
                    : new(
                        "Не удалось найти видео или фото в этом пине Pinterest.",
                        "Could not find a video or photo in this Pinterest pin."),
                VideoPlatform.TikTok => new(
                    "В этой публикации TikTok нет видео (возможно, это фото или слайдшоу).",
                    "This TikTok post has no video (it may be a photo or slideshow)."),
                _ => new(
                    "Видео недоступно для скачивания.",
                    "Video is unavailable for download.")
            };
        }
        if (ContainsCode(text, "10204") || text.Contains("ip address is blocked"))
            return new(
                "TikTok временно ограничил доступ с этого соединения.",
                "TikTok temporarily restricted access from this connection.");
        if (text.Contains("sign in") || text.Contains("private video") ||
            text.Contains("log into an account") || ContainsCode(text, "10216") || ContainsCode(text, "10222"))
            return AppStrings.SignInOrPrivate(name);
        if (text.Contains("requested format"))
            return format.IsAudio
                ? new(
                    "Для этого видео нет доступной звуковой дорожки.",
                    "No audio track is available for this video.")
                : new(
                    "Разрешение недоступно. Попробуйте повысить предел.",
                    "This resolution is unavailable. Try increasing the limit.");
        if (text.Contains("http error 403"))
            return AppStrings.RejectedRequest(name);
        if (text.Contains("http error 429") || text.Contains("too many requests"))
            return AppStrings.RateLimited(name);
        if (text.Contains("video unavailable") || text.Contains("not available") || text.Contains("status code"))
            return new(
                "Видео недоступно для скачивания.",
                "Video is unavailable for download.");
        if (IsNetworkFailure(detail, retryError))
            return AppStrings.ConnectionFailed(name);
        if (text.Contains("no space left") || text.Contains("not enough space") || text.Contains("winerror 112"))
            return new(
                "На диске недостаточно свободного места.",
                "Not enough free disk space.");
        if (text.Contains("permission denied") || text.Contains("access is denied") ||
            text.Contains("winerror 5") || text.Contains("operation not permitted"))
            return new(
                "Система ограничила запуск загрузчика или запись файла.",
                "System permissions blocked the downloader or file write.");
        return new("Не удалось скачать файл.", "Could not download file.");
    }

    private static void Stop(Process process)
    {
        try { process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (NotSupportedException) { }
        catch (System.ComponentModel.Win32Exception) { }
        catch (AggregateException) { }
    }
}
