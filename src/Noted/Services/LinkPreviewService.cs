using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace Noted.Services;

public sealed record LinkPreview(string? Title = null, string? Description = null, BitmapSource? Image = null);

public sealed class LinkPreviewService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly SemaphoreSlim _downloads = new(4);
    private readonly Dictionary<string, Task<LinkPreview>> _cache = new();

    // Called on the UI thread; no controls are cached and downloads never block layout.
    public Task<LinkPreview> GetAsync(LinkTarget target, bool iconOnly)
    {
        string key = $"{iconOnly}:{target.Uri}";
        if (_cache.TryGetValue(key, out var task)) return task;
        if (_cache.Count >= 256) _cache.Clear();
        return _cache[key] = Task.Run(() => LoadAsync(target, iconOnly));
    }

    private async Task<LinkPreview> LoadAsync(LinkTarget target, bool iconOnly)
    {
        await _downloads.WaitAsync();
        try
        {
            var uri = target.Uri;
            if (iconOnly)
                return (uri.Scheme is "https" or "http") && (target.Kind == LinkKind.Website || target.VideoId is not null)
                    ? new(Image: await ImageAsync(new Uri(uri.GetLeftPart(UriPartial.Authority) + "/favicon.ico"))) : new();
            if (target.Kind == LinkKind.Image) return new(Image: await ImageAsync(uri));
            if (target.VideoId is not null)
                return new(Image: await ImageAsync(new Uri($"https://i.ytimg.com/vi/{target.VideoId}/hqdefault.jpg")));
            if (target.Kind != LinkKind.Website) return new();
            byte[] bytes = await BytesAsync(uri, 1024 * 1024);
            return await ReadPageAsync(Encoding.UTF8.GetString(bytes), uri);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or
                                   NotSupportedException or ArgumentException or InvalidOperationException or UnauthorizedAccessException or RegexMatchTimeoutException or FormatException)
        { return new(); }
        finally { _downloads.Release(); }
    }

    private async Task<LinkPreview> ReadPageAsync(string html, Uri uri)
    {
        var metadata = ParseMetadata(html);
        string? title = metadata.GetValueOrDefault("og:title");
        title ??= Regex.Match(html, @"<title\b[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromSeconds(1)) is { Success: true } match
            ? WebUtility.HtmlDecode(match.Groups[1].Value).Trim() : null;
        string? description = metadata.GetValueOrDefault("og:description") ?? metadata.GetValueOrDefault("description");
        BitmapSource? image = null;
        string? imageUrl = metadata.GetValueOrDefault("og:image") ?? metadata.GetValueOrDefault("twitter:image");
        if (Uri.TryCreate(uri, imageUrl, out var imageUri) && imageUri.Scheme is "https" or "http")
        {
            try { image = await ImageAsync(imageUri); }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or NotSupportedException or ArgumentException or FormatException) { }
        }
        return new(title, description, image);
    }

    public static Dictionary<string, string> ParseMetadata(string html)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match tag in Regex.Matches(html, @"<meta\b[^>]*>", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
        {
            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match attr in Regex.Matches(tag.Value, "([\\w:-]+)\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>]+))", RegexOptions.None, TimeSpan.FromSeconds(1)))
                attributes[attr.Groups[1].Value] = WebUtility.HtmlDecode(attr.Groups[2].Success ? attr.Groups[2].Value : attr.Groups[3].Success ? attr.Groups[3].Value : attr.Groups[4].Value);
            string? name = attributes.GetValueOrDefault("property") ?? attributes.GetValueOrDefault("name");
            if (name is not null && attributes.TryGetValue("content", out var content)) result[name] = content;
        }
        return result;
    }

    private static async Task<BitmapSource> ImageAsync(Uri uri)
    {
        var bytes = await BytesAsync(uri, 8 * 1024 * 1024);
        using var stream = new MemoryStream(bytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = 640;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static async Task<byte[]> BytesAsync(Uri uri, int limit)
    {
        if (uri.IsFile)
        {
            if (new FileInfo(uri.LocalPath).Length > limit) throw new IOException("Image is too large.");
            return await File.ReadAllBytesAsync(uri.LocalPath);
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > limit) throw new IOException("Preview is too large.");
        await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var output = new MemoryStream();
        byte[] buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, timeout.Token)) > 0)
        {
            if (output.Length + count > limit) throw new IOException("Preview is too large.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
}
