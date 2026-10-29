using MdExplorer.Domain;
using MdExplorer.Infrastructure;
using MdExplorer.UI.Preview;
using Xunit;

namespace MdExplorer.Tests;

/// <summary>P0 预览提供者注册表（docs/PLUGIN-SYSTEM.md §6）：分发、兜底文案与优先级压制。</summary>
public sealed class PreviewRegistryTests
{
    private static FileItem Item(string name, FileKind kind) => new(
        FileLocator.FromPath("/data/" + name),
        name,
        kind,
        1234,
        DateTimeOffset.UnixEpoch,
        IsHidden: false,
        IsSymlink: false,
        Path.GetExtension(name));

    private static PreviewProviderRegistry BuiltIns() => BuiltInPreviewProviders.Create(new ArchiveService(), new LocalFileSystemService());

    [Theory]
    [InlineData("photo.jpg", FileKind.Image, "builtin.image")]
    [InlineData("doc.pdf", FileKind.Document, "builtin.pdf")]
    [InlineData("report.docx", FileKind.Document, "builtin.office")]
    [InlineData("slides.pptx", FileKind.Document, "builtin.office")]
    [InlineData("budget.xlsx", FileKind.Document, "builtin.office")]
    [InlineData("README.md", FileKind.Text, "builtin.markdown")]
    [InlineData("notes.txt", FileKind.Text, "builtin.text")]
    [InlineData("Program.cs", FileKind.Code, "builtin.text")]
    [InlineData("data.csv", FileKind.Data, "builtin.text")]
    [InlineData("blob.bin", FileKind.Unknown, "builtin.hex")]
    [InlineData("backup.zip", FileKind.Archive, "builtin.archive")]
    [InlineData("backup.tar", FileKind.Archive, "builtin.fallback")] // ArchiveService v0.1 只认 ZIP 家族
    [InlineData("film.mp4", FileKind.Video, "builtin.fallback")]
    [InlineData("song.m4a", FileKind.Audio, "builtin.fallback")]
    [InlineData("font.ttf", FileKind.Font, "builtin.fallback")]
    public void Resolve_PicksExpectedProvider(string name, FileKind kind, string expectedId)
    {
        var provider = BuiltIns().Resolve(Item(name, kind));
        Assert.NotNull(provider);
        Assert.Equal(expectedId, provider!.Id);
    }

    [Fact]
    public async Task Fallback_MessagesMatchKind()
    {
        var registry = BuiltIns();
        var video = await registry.Resolve(Item("film.mp4", FileKind.Video))!
            .LoadAsync(Item("film.mp4", FileKind.Video), TestContext.Current.CancellationToken);
        Assert.Contains("播放", video.Message);
    }

    [Fact]
    public async Task PdfProvider_PrefersDirectControlForHeaderValidPdf()
    {
        var path = Path.Combine(Path.GetTempPath(), $"eidoria-direct-{Guid.NewGuid():N}.pdf");
        await File.WriteAllTextAsync(path, "%PDF-1.4 direct-control candidate");
        try
        {
            var item = new FileItem(
                FileLocator.FromPath(path),
                Path.GetFileName(path),
                FileKind.Document,
                new FileInfo(path).Length,
                DateTimeOffset.UtcNow,
                IsHidden: false,
                IsSymlink: false,
                ".pdf");
            var provider = BuiltIns().Resolve(item);
            Assert.Equal("builtin.pdf", provider!.Id);

            var content = await provider.LoadAsync(item, TestContext.Current.CancellationToken);
            Assert.Null(content.Pdf);
            Assert.Equal(OfficePreviewFormat.Pdf, content.Office?.Format);
            Assert.Contains("PDF 直接预览", content.Office?.Facts.Summary, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task InvalidPdfWithoutHeader_ReturnsFriendlyMessageInsteadOfThrowing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"eidoria-invalid-{Guid.NewGuid():N}.pdf");
        await File.WriteAllTextAsync(path, "this is not a PDF");
        try
        {
            var item = new FileItem(
                FileLocator.FromPath(path),
                Path.GetFileName(path),
                FileKind.Document,
                new FileInfo(path).Length,
                DateTimeOffset.UtcNow,
                IsHidden: false,
                IsSymlink: false,
                ".pdf");
            var provider = BuiltIns().Resolve(item);
            Assert.Equal("builtin.pdf", provider!.Id);

            var content = await provider.LoadAsync(item, TestContext.Current.CancellationToken);
            Assert.Null(content.Pdf);
            Assert.Null(content.Office);
            Assert.Contains("PDF", content.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class OverrideImageProvider : IPreviewProvider
    {
        public string Id => "test.override-image";
        public int Priority => 200;
        public bool CanHandle(FileItem item) => item.Kind == FileKind.Image;
        public Task<PreviewContent> LoadAsync(FileItem item, CancellationToken cancellationToken) =>
            Task.FromResult(new PreviewContent(Text: "overridden"));
    }

    [Fact]
    public void Resolve_HigherPriorityWins()
    {
        // 第三方提供者以更高优先级压制同类内置提供者（插件冲突裁决的核心语义）
        var flat = new PreviewProviderRegistry([new OverrideImageProvider(), .. BuiltInList()]);
        Assert.Equal("test.override-image", flat.Resolve(Item("p.png", FileKind.Image))!.Id);
        Assert.Equal("builtin.text", flat.Resolve(Item("a.txt", FileKind.Text))!.Id);
    }

    private sealed class TextSentinelProvider : IPreviewProvider
    {
        public string Id => "builtin.text";
        public int Priority => 100;
        public bool CanHandle(FileItem item) => item.Kind == FileKind.Text;
        public Task<PreviewContent> LoadAsync(FileItem item, CancellationToken cancellationToken) =>
            Task.FromResult(new PreviewContent(Text: "x"));
    }

    private static IEnumerable<IPreviewProvider> BuiltInList() => [new TextSentinelProvider()];

    [Fact]
    public void Resolve_EmptyRegistry_ReturnsNull()
    {
        var registry = new PreviewProviderRegistry([]);
        Assert.Null(registry.Resolve(Item("a.txt", FileKind.Text)));
    }
}
