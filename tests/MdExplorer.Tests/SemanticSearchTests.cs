using System.Text;
using MdExplorer.Domain;
using MdExplorer.Infrastructure;
using MdExplorer.Infrastructure.Ai;
using Xunit;

namespace MdExplorer.Tests;

/// <summary>AI semantic indexing and ranking without model weights: the embedding port is deterministic.</summary>
public sealed class SemanticSearchTests : IDisposable
{
    private readonly string _baseDir;
    private readonly string _root;
    private readonly string _dataDir;
    private readonly SettingsStore _settings;
    private readonly FileNameIndexService _fileIndex;
    private readonly SemanticSearchService _semantic;

    public SemanticSearchTests()
    {
        _baseDir = Path.Combine(Path.GetTempPath(), "mdexplorer-semantic-tests", Guid.NewGuid().ToString("N"));
        _root = Path.Combine(_baseDir, "files");
        _dataDir = Path.Combine(_baseDir, "data");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_dataDir);
        _settings = SettingsStore.LoadOrDefault(_dataDir);
        _settings.Settings.EnableLocalAi = true;
        _settings.Settings.SearchIndexRootPaths = [_root];
        _fileIndex = new FileNameIndexService(_dataDir, _settings);
        _semantic = new SemanticSearchService(_dataDir, _fileIndex, _settings, new FakeEmbeddings());
    }

    public void Dispose()
    {
        try { Directory.Delete(_baseDir, recursive: true); } catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task RebuildEmbedsTextSearchesByMeaningAndStoresNoRawContent()
    {
        var notes = Path.Combine(_root, "quarterly-notes.md");
        var finance = Path.Combine(_root, "market.txt");
        await File.WriteAllTextAsync(notes, "A short note about feline companionship.");
        await File.WriteAllTextAsync(finance, "Quarterly bonds and interest rates.");
        await _fileIndex.RebuildAsync(null, TestContext.Current.CancellationToken);

        var built = await _semantic.RebuildAsync(null, TestContext.Current.CancellationToken);
        Assert.Equal(2, built.IndexedItemCount);
        Assert.True(built.IsCurrent);
        Assert.Equal("Test embedding", Assert.Single(built.ModelNames));

        var hits = await _semantic.SearchAsync(
            "find a pet note", null, 10, TestContext.Current.CancellationToken);
        var hit = Assert.Single(hits);
        Assert.Equal(notes, hit.Item.FullPath);
        Assert.True(hit.Score > 0.99f);

        var indexPath = Path.Combine(_dataDir, "semantic-search-v1.bin");
        var bytes = await File.ReadAllBytesAsync(indexPath);
        Assert.DoesNotContain("feline", Encoding.Latin1.GetString(bytes), StringComparison.Ordinal);

        await _semantic.ClearAsync(TestContext.Current.CancellationToken);
        Assert.False(File.Exists(indexPath));
        Assert.Equal(0, (await _semantic.GetStatsAsync(TestContext.Current.CancellationToken)).IndexedItemCount);
    }

    [Fact]
    public async Task SemanticIndexRequiresExplicitLocalAiEnablement()
    {
        _settings.Settings.EnableLocalAi = false;
        var stats = await _semantic.GetStatsAsync(TestContext.Current.CancellationToken);
        Assert.False(stats.IsEnabled);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _semantic.RebuildAsync(null, TestContext.Current.CancellationToken));
    }

    private sealed class FakeEmbeddings : ITextEmbeddingService
    {
        public IReadOnlyList<TextEmbeddingModel> GetAvailableModels() =>
            [new TextEmbeddingModel("test-embed", "Test embedding", "revision-a", 2)];

        public float[] Embed(string modelId, string text, bool isQuery, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return text.Contains("pet", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("feline", StringComparison.OrdinalIgnoreCase)
                ? [1f, 0f]
                : [0f, 1f];
        }
    }
}
