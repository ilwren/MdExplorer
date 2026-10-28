using MdExplorer.Domain.Files;
using MdExplorer.Platform.Abstractions.FileSystem;

namespace MdExplorer.Infrastructure.FileSystem;

/// 扩展名 → MIME/Kind 静态表（§5.2）。
public sealed class MimeTypeService : IMimeTypeService
{
    private static readonly HashSet<string> Image = new(StringComparer.OrdinalIgnoreCase)
        { "jpg", "jpeg", "png", "gif", "webp", "bmp", "svg", "ico", "avif" };
    private static readonly HashSet<string> Video = new(StringComparer.OrdinalIgnoreCase)
        { "mp4", "mkv", "avi", "mov", "webm", "m4v", "flv", "wmv" };
    private static readonly HashSet<string> Audio = new(StringComparer.OrdinalIgnoreCase)
        { "mp3", "wav", "flac", "ogg", "m4a", "aac", "wma", "opus" };
    private static readonly HashSet<string> Archive = new(StringComparer.OrdinalIgnoreCase)
        { "zip", "7z", "rar", "tar", "gz", "bz2", "xz", "tgz", "zst" };
    private static readonly HashSet<string> Code = new(StringComparer.OrdinalIgnoreCase)
    {
        "js", "mjs", "cjs", "ts", "tsx", "jsx", "py", "cs", "java", "go", "rs", "rb", "sh",
        "zsh", "fish", "ps1", "bat", "cmd", "json", "toml", "yaml", "yml", "xml", "html",
        "css", "scss", "sql", "lua", "c", "cc", "cpp", "h", "hpp", "swift", "kt", "dart",
    };
    private static readonly HashSet<string> Text = new(StringComparer.OrdinalIgnoreCase)
        { "txt", "log", "csv", "tsv", "ini", "conf", "cfg", "rst" };
    private static readonly HashSet<string> Doc = new(StringComparer.OrdinalIgnoreCase)
        { "pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "odt", "odp", "rtf", "epub" };
    private static readonly HashSet<string> Exec = new(StringComparer.OrdinalIgnoreCase)
        { "exe", "msi", "apk", "deb", "rpm", "appimage", "dmg", "pkg", "jar", "dll", "so" };

    public FileKind GetKind(string name, bool isDirectory)
    {
        if (isDirectory) return FileKind.Folder;
        var ext = Path.GetExtension(name).TrimStart('.');
        if (ext.Length == 0) return FileKind.Unknown;
        if (Image.Contains(ext)) return FileKind.Image;
        if (Video.Contains(ext)) return FileKind.Video;
        if (Audio.Contains(ext)) return FileKind.Audio;
        if (Archive.Contains(ext)) return FileKind.Archive;
        if (ext.Equals("md", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals("mdx", StringComparison.OrdinalIgnoreCase)) return FileKind.Document;
        if (Doc.Contains(ext)) return FileKind.Document;
        if (Code.Contains(ext)) return FileKind.Code;
        if (Text.Contains(ext)) return FileKind.Text;
        if (Exec.Contains(ext)) return FileKind.Executable;
        return FileKind.Unknown;
    }

    public string? GetMimeType(string? extension)
    {
        if (string.IsNullOrEmpty(extension)) return null;
        var ext = extension.TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            "jpg" or "jpeg" => "image/jpeg",
            "png" => "image/png",
            "gif" => "image/gif",
            "webp" => "image/webp",
            "svg" => "image/svg+xml",
            "mp4" or "m4v" => "video/mp4",
            "webm" => "video/webm",
            "mkv" => "video/x-matroska",
            "mp3" => "audio/mpeg",
            "wav" => "audio/wav",
            "flac" => "audio/flac",
            "ogg" => "audio/ogg",
            "pdf" => "application/pdf",
            "zip" => "application/zip",
            "md" => "text/markdown",
            "txt" or "log" or "csv" => "text/plain",
            "json" => "application/json",
            "html" => "text/html",
            _ => "application/octet-stream",
        };
    }
}
