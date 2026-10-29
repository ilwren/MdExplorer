namespace MdExplorer.Domain;

/// <summary>可由内置 Office 控件只读呈现的 Open XML 格式；旧二进制 .doc/.xls/.ppt 不在其中。</summary>
public enum OfficeDocumentFormat
{
    Word,
    Presentation,
    Spreadsheet,
}

/// <summary>按扩展名将文件分类。纯静态表，AOT 安全；未命中归 Unknown/Text（以空扩展常见文本名兜底）。</summary>
public static class FileKindClassifier
{
    private static readonly Dictionary<string, FileKind> Map = Build();

    private static Dictionary<string, FileKind> Build()
    {
        var map = new Dictionary<string, FileKind>(StringComparer.OrdinalIgnoreCase);
        void Add(FileKind kind, params string[] exts)
        {
            foreach (var e in exts) map[e] = kind;
        }

        Add(FileKind.Image, ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".svg", ".ico", ".tif", ".tiff", ".heic", ".heif", ".avif");
        Add(FileKind.Video, ".mp4", ".mkv", ".mov", ".avi", ".webm", ".flv", ".wmv", ".m4v", ".ts", ".mpg", ".mpeg", ".3gp");
        Add(FileKind.Audio, ".mp3", ".flac", ".wav", ".aac", ".ogg", ".opus", ".m4a", ".wma", ".ape", ".mid", ".midi");
        Add(FileKind.Document, ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".odt", ".ods", ".odp", ".rtf", ".epub", ".mobi", ".chm", ".pages", ".numbers", ".key");
        Add(FileKind.Archive, ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".xz", ".zst", ".iso", ".apk", ".jar", ".dmg", ".cab");
        Add(FileKind.Code,
            ".cs", ".fs", ".vb", ".java", ".kt", ".kts", ".c", ".h", ".cpp", ".hpp", ".cc", ".rs", ".go", ".py", ".js", ".ts",
            ".tsx", ".jsx", ".mjs", ".swift", ".m", ".mm", ".php", ".rb", ".lua", ".pl", ".sh", ".bash", ".zsh", ".ps1",
            ".bat", ".cmd", ".axaml", ".xaml", ".csproj", ".fsproj", ".sln", ".slnx", ".props", ".targets", ".html",
            ".htm", ".css", ".scss", ".less", ".vue", ".sql", ".gradle", ".cmake", ".dockerfile", ".r", ".jl", ".dart",
            ".scala", ".clj", ".hs", ".ml", ".ex", ".erl", ".nim", ".zig", ".v", ".sv");
        Add(FileKind.Data, ".json", ".jsonc", ".xml", ".yaml", ".yml", ".toml", ".ini", ".cfg", ".conf", ".csv", ".tsv", ".db", ".sqlite", ".sqlite3", ".reg");
        Add(FileKind.Text, ".txt", ".md", ".markdown", ".log", ".nfo", ".srt", ".ass", ".vtt", ".text", ".rst", ".adoc", ".org");
        Add(FileKind.Font, ".ttf", ".otf", ".woff", ".woff2", ".ttc");
        Add(FileKind.Executable, ".exe", ".msi", ".appimage", ".deb", ".rpm", ".com", ".scr", ".dll", ".so", ".dylib", ".bin", ".run", ".elf");
        return map;
    }

    private static readonly HashSet<string> TextFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "LICENSE", "README", "CHANGELOG", "AUTHORS", "NOTICE", "Makefile", "Dockerfile",
        ".gitignore", ".gitattributes", ".editorconfig", "CMakeLists.txt",
    };

    public static FileKind Classify(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        if (!string.IsNullOrEmpty(ext) && Map.TryGetValue(ext, out var kind))
            return kind;
        if (TextFileNames.Contains(fileName))
            return FileKind.Text;
        return FileKind.Unknown;
    }

    public static bool IsPdf(FileItem item) =>
        string.Equals(item.Extension, ".pdf", StringComparison.OrdinalIgnoreCase);

    public static bool TryGetOfficeDocumentFormat(FileItem item, out OfficeDocumentFormat format)
    {
        format = default;
        if (item.IsFolder)
            return false;
        if (string.Equals(item.Extension, ".docx", StringComparison.OrdinalIgnoreCase))
            format = OfficeDocumentFormat.Word;
        else if (string.Equals(item.Extension, ".pptx", StringComparison.OrdinalIgnoreCase))
            format = OfficeDocumentFormat.Presentation;
        else if (string.Equals(item.Extension, ".xlsx", StringComparison.OrdinalIgnoreCase))
            format = OfficeDocumentFormat.Spreadsheet;
        else
            return false;
        return true;
    }

    public static bool IsOfficeDocument(FileItem item) => TryGetOfficeDocumentFormat(item, out _);

    /// <summary>类型的人类可读描述（预览元信息与列表详情列用）。</summary>
    public static string Describe(FileItem item)
    {
        if (item.IsFolder) return "文件夹";
        var ext = item.Extension?.TrimStart('.').ToUpperInvariant();
        var suffix = string.IsNullOrEmpty(ext) ? "文件" : $"{ext} 文件";
        return item.Kind switch
        {
            FileKind.Image => $"{suffix} · 图像",
            FileKind.Video => $"{suffix} · 视频",
            FileKind.Audio => $"{suffix} · 音频",
            FileKind.Document => IsPdf(item)
                ? "PDF 文档"
                : TryGetOfficeDocumentFormat(item, out var officeFormat)
                    ? officeFormat switch
                    {
                        OfficeDocumentFormat.Word => "Word 文档",
                        OfficeDocumentFormat.Presentation => "PowerPoint 演示文稿",
                        _ => "Excel 工作簿",
                    }
                    : $"{suffix} · 文档",
            FileKind.Archive => $"{suffix} · 压缩包",
            FileKind.Code => $"{suffix} · 源代码",
            FileKind.Data => $"{suffix} · 数据",
            FileKind.Font => $"{suffix} · 字体",
            FileKind.Executable => $"{suffix} · 可执行/程序库",
            FileKind.Text => $"{suffix} · 文本",
            _ => suffix,
        };
    }
}
