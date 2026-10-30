using MdExplorer.Infrastructure.Ai;
using Xunit;

namespace MdExplorer.Tests;

/// <summary>Opt-in integration tests that execute the pinned, downloaded production ONNX weights.</summary>
public sealed class RealModelEmbeddingTests
{
    [Fact]
    public void BgeChineseEmbedsRelatedTextCloserThanUnrelatedText()
    {
        EnsureRealModelTestsEnabled();
        AssertSemanticRanking(
            "text-embed-zh",
            "如何重置账户密码",
            "忘记登录密码后，可以通过邮箱链接重设账户密码。",
            "厨房里用番茄和鸡蛋制作一道家常菜。",
            expectedDimensions: 512);
    }

    [Fact]
    public void MiniLmEnglishEmbedsRelatedTextCloserThanUnrelatedText()
    {
        EnsureRealModelTestsEnabled();
        AssertSemanticRanking(
            "text-embed-en",
            "How can I reset my account password?",
            "If you forgot your login password, use the email link to reset your account.",
            "Chop tomatoes and eggs to make a quick dinner.",
            expectedDimensions: 384);
    }

    private static void EnsureRealModelTestsEnabled()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("MDEXPLORER_RUN_REAL_MODEL_TESTS"),
                "1",
                StringComparison.Ordinal))
        {
            Assert.Skip("Set MDEXPLORER_RUN_REAL_MODEL_TESTS=1 and download the pinned models to run real-weight integration tests.");
        }
    }

    private static void AssertSemanticRanking(
        string modelId,
        string query,
        string relevantDocument,
        string unrelatedDocument,
        int expectedDimensions)
    {
        var modelsDirectory = Environment.GetEnvironmentVariable(AiModelCatalog.DirectoryEnvironmentVariable);
        Assert.False(string.IsNullOrWhiteSpace(modelsDirectory),
            $"Set {AiModelCatalog.DirectoryEnvironmentVariable} to the downloaded model directory.");

        using var service = new OnnxTextEmbeddingService(new AiModelCatalog(modelsDirectory!));
        Assert.Contains(service.GetAvailableModels(), model => model.Id == modelId);

        var queryVector = service.Embed(modelId, query, isQuery: true, CancellationToken.None);
        var relevantVector = service.Embed(modelId, relevantDocument, isQuery: false, CancellationToken.None);
        var unrelatedVector = service.Embed(modelId, unrelatedDocument, isQuery: false, CancellationToken.None);

        AssertEmbedding(queryVector, expectedDimensions, modelId, "query");
        AssertEmbedding(relevantVector, expectedDimensions, modelId, "relevant document");
        AssertEmbedding(unrelatedVector, expectedDimensions, modelId, "unrelated document");

        var relevantScore = CosineSimilarity(queryVector, relevantVector);
        var unrelatedScore = CosineSimilarity(queryVector, unrelatedVector);
        Assert.True(
            relevantScore > unrelatedScore,
            $"{modelId} ranked unrelated text above the matching text: relevant={relevantScore:F4}, unrelated={unrelatedScore:F4}.");
    }

    private static void AssertEmbedding(float[] embedding, int dimensions, string modelId, string inputName)
    {
        Assert.Equal(dimensions, embedding.Length);
        Assert.All(embedding, value => Assert.True(float.IsFinite(value), $"{modelId} emitted a non-finite value for {inputName}."));
        var norm = Math.Sqrt(embedding.Sum(value => (double)value * value));
        Assert.InRange(norm, 0.999, 1.001);
    }

    private static double CosineSimilarity(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        Assert.Equal(left.Count, right.Count);
        var dot = 0d;
        for (var i = 0; i < left.Count; i++)
            dot += left[i] * right[i];
        return dot;
    }
}
