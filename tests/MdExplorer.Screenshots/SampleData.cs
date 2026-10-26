using System.IO.Compression;
using System.Text;

namespace MdExplorer.Screenshots;

/// <summary>截图用样例文件树：与布局文档固定样例数据同名，含手写 PNG「照片」。</summary>
internal static class SampleData
{
    private static readonly object Gate = new();
    private static string? _root;

    public static string Create()
    {
        lock (Gate)
        {
            if (_root is not null) return _root;
            var root = Path.Combine(Path.GetTempPath(), "mdexplorer-sample");
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);

            var pictures = Path.Combine(root, "Pictures");
            var camera = Path.Combine(pictures, "Camera");
            var documents = Path.Combine(root, "Documents");
            Directory.CreateDirectory(camera);
            Directory.CreateDirectory(documents);
            Directory.CreateDirectory(Path.Combine(root, "Downloads"));

            File.WriteAllBytes(Path.Combine(pictures, "IMG_20261001.jpg.png"), Array.Empty<byte>()); // 占位将被下方真实 PNG 以 jpg 命名替代
            File.Delete(Path.Combine(pictures, "IMG_20261001.jpg.png"));
            WritePhotoPng(Path.Combine(pictures, "IMG_20261001.jpg"), 0);
            WritePhotoPng(Path.Combine(pictures, "海边日落.png"), 1);
            WritePhotoPng(Path.Combine(pictures, "screenshot_ide.png"), 2);
            WritePhotoPng(Path.Combine(camera, "IMG_0001.png"), 3);

            File.WriteAllText(Path.Combine(pictures, "Program.cs"),
                "// MdExplorer 样例源文件\nusing Avalonia;\n\ninternal static class Program\n{\n    [STAThread]\n    public static void Main(string[] args)\n        => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);\n\n    public static AppBuilder BuildAvaloniaApp()\n        => AppBuilder.Configure<App>()\n            .UsePlatformDetect()\n            .WithInterFont()\n            .LogToTrace();\n}\n");
            File.WriteAllText(Path.Combine(pictures, "README.md"),
                "# MdExplorer\n\n跨平台本地文件资源管理器（Avalonia 12 + .NET 10 + Material Design 3）。\n\n- 五档断点响应式布局\n- 预览与递归搜索\n- Native AOT 发布\n");
            File.WriteAllText(Path.Combine(pictures, "会议纪要.txt"),
                "会议纪要（样例）\n\n1. 决策：许可证采用 Apache-2.0\n2. Android 全文件变体稍后做\n3. 媒体播放后续版本跟进\n");
            WriteSamplePdf(Path.Combine(pictures, "设计规范 v3.pdf"));
            File.WriteAllBytes(Path.Combine(pictures, "演示视频.mp4"), new byte[8192]);
            File.WriteAllBytes(Path.Combine(pictures, "录音 001.m4a"), new byte[4096]);
            File.WriteAllText(Path.Combine(pictures, "数据.csv"), "名称,大小\n报告,128\n图片,4096\n");
            File.WriteAllText(Path.Combine(documents, "报告-终版.docx"), "fake docx payload");
            File.WriteAllText(Path.Combine(documents, "todo.txt"), "- [ ] 评审布局稿\n- [x] 定版 API\n");

            using (var stream = File.Create(Path.Combine(pictures, "backup-2026.zip")))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                foreach (var (entryName, content) in new[] { ("notes/readme.txt", "backup notes"), ("config.json", "{\"theme\":\"teal\"}"), ("photo-list.txt", "IMG_20261001.jpg") })
                {
                    var entry = zip.CreateEntry(entryName);
                    using var writer = entry.Open();
                    writer.Write(Encoding.UTF8.GetBytes(content));
                }
            }

            var baseTime = new DateTime(2026, 10, 1, 14, 22, 0, DateTimeKind.Local);
            var i = 0;
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                File.SetLastWriteTime(file, baseTime.AddDays(-3.0 * i++));

            Directory.CreateDirectory(Path.Combine(root, "Music"));
            return _root = root;
        }
    }

    /// <summary>生成一张风景示意 PNG（渐变海面 + 太阳 + 山丘），供缩略图/预览截图出真实图。</summary>
    private static void WritePhotoPng(string path, int variant)
    {
        const int w = 640, h = 420;
        var pixels = new byte[h * (1 + w * 3)];
        (byte r, byte g, byte b) Sky(int y)
        {
            double t = y / (double)h;
            return variant switch
            {
                1 => ((byte)(250 - 120 * t), (byte)(160 - 60 * t), (byte)(90 + 40 * t)),
                2 => ((byte)(28 + 30 * t), (byte)(32 + 26 * t), (byte)(52 + 30 * t)),
                _ => ((byte)(110 - 40 * t), (byte)(200 - 60 * t), (byte)(205 - 35 * t)),
            };
        }
        for (int y = 0; y < h; y++)
        {
            var row = y * (1 + w * 3);
            pixels[row] = 0; // filter: None
            for (int x = 0; x < w; x++)
            {
                var (r, g, b) = Sky(y);
                var horizon = h * 0.62 + Math.Sin(x / 55.0 + variant) * 14;
                if (y > horizon) { r = (byte)(r * 0.35); g = (byte)(g * 0.55); b = (byte)(b * 0.5); }
                var sunX = w * (0.68 + variant * 0.05);
                var sunY = h * 0.30;
                var d = Math.Sqrt((x - sunX) * (x - sunX) + (y - sunY) * (y - sunY));
                if (d < 42) { r = 255; g = 226; b = 150; }
                var offset = row + 1 + x * 3;
                pixels[offset] = r;
                pixels[offset + 1] = g;
                pixels[offset + 2] = b;
            }
        }

        using var stream = File.Create(path);
        stream.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }); // PNG 签名须用原始字节：u8 字面量会把 \x89 当作字符再 UTF-8 编码为 C2 89
        WriteChunk(stream, "IHDR", BuildIhdr(w, h));
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            zlib.Write(pixels);
        WriteChunk(stream, "IDAT", compressed.ToArray());
        WriteChunk(stream, "IEND", []);
    }

    /// <summary>手写一份最小两页 PDF（正确 xref 偏移），让 PDFium 截图测试渲染真实分页而非占位文本。</summary>
    private static void WriteSamplePdf(string path)
    {
        const string page1 = "BT /F1 30 Tf 64 720 Td (Eidoria PDF Preview) Tj /F1 16 Tf 0 -42 Td (Page 1 - layered pages, paths and light.) Tj ET\n";
        const string page2 = "BT /F1 30 Tf 64 720 Td (Eidoria PDF Preview) Tj /F1 16 Tf 0 -42 Td (Page 2 - paging renders on demand.) Tj ET\n";
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 6 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 6 0 R >> >> /Contents 7 0 R >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(page1)} >>\nstream\n{page1}endstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(page2)} >>\nstream\n{page2}endstream",
        };

        using var stream = new MemoryStream();
        void WriteAscii(string text) => stream.Write(Encoding.ASCII.GetBytes(text));

        WriteAscii("%PDF-1.4\n");
        var offsets = new long[objects.Length + 1];
        for (int i = 0; i < objects.Length; i++)
        {
            offsets[i + 1] = stream.Length;
            WriteAscii($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = stream.Length;
        WriteAscii($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        for (int i = 1; i < offsets.Length; i++)
            WriteAscii($"{offsets[i]:D10} 00000 n \n");
        WriteAscii($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        File.WriteAllBytes(path, stream.ToArray());
    }

    private static byte[] BuildIhdr(int w, int h)
    {
        var data = new byte[13];
        WriteBigEndian(data.AsSpan(0, 4), (uint)w);
        WriteBigEndian(data.AsSpan(4, 4), (uint)h);
        data[8] = 8;  // bit depth
        data[9] = 2;  // truecolor
        return data;
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> header = stackalloc byte[4];
        WriteBigEndian(header, (uint)data.Length);
        stream.Write(header);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        var crcSource = new byte[typeBytes.Length + data.Length];
        typeBytes.CopyTo(crcSource, 0);
        data.CopyTo(crcSource, typeBytes.Length);
        WriteBigEndian(header, Crc32(crcSource));
        stream.Write(header);
    }

    private static void WriteBigEndian(Span<byte> dest, uint value)
    {
        dest[0] = (byte)(value >> 24);
        dest[1] = (byte)(value >> 16);
        dest[2] = (byte)(value >> 8);
        dest[3] = (byte)value;
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320U ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFU;
        foreach (var b in data)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFU;
    }
}
