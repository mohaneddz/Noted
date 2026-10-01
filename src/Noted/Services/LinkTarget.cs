using System.IO;
using System.Text.RegularExpressions;
using Noted.Markdown;

namespace Noted.Services;

public enum LinkDisplayMode { Nothing, Icon, MiniPreview, Preview }
public enum LinkKind { Website, Image, Video, Audio, Document, Folder, File, Email }

public sealed record LinkTarget(Uri Uri, LinkKind Kind, string? VideoId = null)
{
    public string Icon => Kind switch
    {
        LinkKind.Image => "\uEB9F", LinkKind.Video => "\uE714", LinkKind.Audio => "\uE8D6",
        LinkKind.Folder => "\uE8B7", LinkKind.Document => "\uE8A5", LinkKind.File => "\uE7C3",
        LinkKind.Email => "\uE715", _ => "\uE774",
    };

    public static LinkTarget? Resolve(string destination, string? notePath = null)
    {
        destination = destination.Trim();
        if (destination.StartsWith('<') && destination.Contains('>'))
            destination = destination[1..destination.IndexOf('>')];
        else
            destination = Regex.Replace(destination, "\\s+[\"'][^\"']*[\"']\\s*$", "");
        destination = Regex.Replace(destination, @"\\([()\[\] ])", "$1");
        if (string.IsNullOrWhiteSpace(destination) || destination.StartsWith('#')) return null;
        if (destination.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) destination = "https://" + destination;
        try
        {
            if (!Uri.TryCreate(destination, UriKind.Absolute, out var uri))
            {
                if (notePath is null) return null;
                uri = new Uri(Path.GetFullPath(Uri.UnescapeDataString(destination), Path.GetDirectoryName(notePath)!));
            }
            if (uri.Scheme is not ("http" or "https" or "file" or "mailto")) return null;
            if (uri.Scheme == "mailto") return new(uri, LinkKind.Email);
            string path = uri.IsFile ? uri.LocalPath : uri.AbsolutePath;
            if (uri.IsFile && (Directory.Exists(path) || path.EndsWith('/') || path.EndsWith('\\')))
                return new(uri, LinkKind.Folder);
            string? videoId = YouTubeId(uri);
            if (videoId is not null) return new(uri, LinkKind.Video, videoId);
            var kind = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".html" or ".htm" => LinkKind.Website,
                ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".svg" => LinkKind.Image,
                ".mp4" or ".webm" or ".mov" or ".m4v" or ".avi" => LinkKind.Video,
                ".mp3" or ".wav" or ".ogg" or ".m4a" or ".flac" => LinkKind.Audio,
                ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".txt" => LinkKind.Document,
                _ => uri.IsFile ? LinkKind.File : LinkKind.Website,
            };
            return new(uri, kind);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException) { return null; }
    }

    private static string? YouTubeId(Uri uri)
    {
        string host = uri.Host.ToLowerInvariant();
        string? id = null;
        if (host == "youtu.be") id = uri.AbsolutePath.Trim('/').Split('/')[0];
        else if (host is "youtube.com" or "www.youtube.com" or "m.youtube.com" or "www.youtube-nocookie.com")
        {
            var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[0] is "embed" or "shorts" or "live") id = parts[1];
            else if (uri.AbsolutePath == "/watch")
                id = uri.Query.TrimStart('?').Split('&').FirstOrDefault(p => p.StartsWith("v="))?[2..];
        }
        return id is not null && Regex.IsMatch(id, @"^[A-Za-z0-9_-]{11}$") ? id : null;
    }
}

public sealed record LinkSpan(int Offset, int Length, string Label, string Destination);

/// <summary>Uses the scanner's spans so code, escaped links and comments stay literal.</summary>
public static class LinkSpans
{
    public static IEnumerable<LinkSpan> Read(string text, MdLine line, Func<string, string?>? resolveReference = null)
    {
        if ((line.Block & (MdStyle.CodeBlock | MdStyle.Table)) != 0) yield break;
        int open = -1;
        foreach (var token in line.Tokens)
        {
            if ((token.Style & (MdStyle.Code | MdStyle.CodeBlock | MdStyle.Comment | MdStyle.Image)) != 0) continue;
            int commentStart = text.LastIndexOf("<!--", token.Offset, StringComparison.Ordinal);
            if (commentStart >= 0 && text.LastIndexOf("-->", token.Offset, StringComparison.Ordinal) < commentStart) continue;
            if ((token.Style & MdStyle.Link) != 0 && token.IsMarker && text[token.Offset] == '[')
                open = token.Offset;
            else if (token.IsMarker && (token.Style & MdStyle.Url) != 0 && open >= 0)
            {
                string tail = text[token.Offset..token.End];
                if (tail.StartsWith("](") && tail.EndsWith(')'))
                    yield return new(open, token.End - open, text[(open + 1)..token.Offset], tail[2..^1]);
                else if (resolveReference is not null)
                {
                    string label = text[(open + 1)..token.Offset];
                    string id = tail.StartsWith("][") && tail.EndsWith(']') ? tail[2..^1] : label;
                    if (string.IsNullOrWhiteSpace(id)) id = label;
                    if (resolveReference(id) is { } destination)
                        yield return new(open, token.End - open, label, destination);
                }
                open = -1;
            }
            else if (!token.IsMarker && (token.Style & (MdStyle.Link | MdStyle.Url)) == (MdStyle.Link | MdStyle.Url))
            {
                var raw = text[token.Offset..token.End];
                bool angle = token.Offset > 0 && text[token.Offset - 1] == '<' && token.End < text.Length && text[token.End] == '>';
                yield return new(token.Offset - (angle ? 1 : 0), token.Length + (angle ? 2 : 0), raw, raw);
            }
        }
    }
}
