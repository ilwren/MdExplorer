using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using MdExplorer.Domain.Files;
using MdExplorer.Platform.Abstractions.Preview;
using SkiaSharp;

namespace MdExplorer.Infrastructure.Thumbnails;

/// 缩略图两级：内存 LRU → 生成器（§5.4）。图片走 Skia 解码；缩放用最近邻（稳定 API，跨 Skia 版本）。
public sealed class SkiaThumbnailService : IThumbnailService
{
    private readonly ConcurrentDictionary<string, ThumbnailResult> _cache = new();

    public ValueTask<ThumbnailResult?> GetAsync(FileItem item, int targetPx, CancellationToken ct)
    {
        if (item.Kind != FileKind.Image || !item.Locator.IsLocalPath)
            return ValueTask.FromResult<ThumbnailResult?>(null);
        var key = $"{item.Locator.Value}|{targetPx}";
        if (_cache.TryGetValue(key, out var cached))
            return ValueTask.FromResult<ThumbnailResult?>(cached);

        try
        {
            var path = item.Locator.ToLocalPath();
            using var stream = File.OpenRead(path);
            using var bitmap = SKBitmap.Decode(stream);
            if (bitmap is null)
                return ValueTask.FromResult<ThumbnailResult?>(null);

            var scale = Math.Min((double)targetPx / bitmap.Width, (double)targetPx / bitmap.Height);
            var w = Math.Max(1, (int)(bitmap.Width * scale));
            var h = Math.Max(1, (int)(bitmap.Height * scale));
            var pixels = ScaleNearest(bitmap, w, h);
            var image = DecodedImage.TryCreate(w, h, pixels);
            if (image is null)
                return ValueTask.FromResult<ThumbnailResult?>(null);
            var result = new ThumbnailResult(image, FromCache: false);
            if (_cache.Count > 256) _cache.Clear();
            _cache[key] = result;
            return ValueTask.FromResult<ThumbnailResult?>(result);
        }
        catch
        {
            return ValueTask.FromResult<ThumbnailResult?>(null);
        }
    }

    private static byte[] ScaleNearest(SKBitmap source, int targetW, int targetH)
    {
        var srcW = source.Width;
        var srcH = source.Height;
        var srcPixels = new byte[srcW * srcH * 4];
        Marshal.Copy(source.GetPixels(), srcPixels, 0, srcPixels.Length);
        var dest = new byte[targetW * targetH * 4];
        for (var y = 0; y < targetH; y++)
        {
            var sy = Math.Min(srcH - 1, (int)((long)y * srcH / targetH));
            for (var x = 0; x < targetW; x++)
            {
                var sx = Math.Min(srcW - 1, (int)((long)x * srcW / targetW));
                var s = (sy * srcW + sx) * 4;
                var d = (y * targetW + x) * 4;
                dest[d] = srcPixels[s];
                dest[d + 1] = srcPixels[s + 1];
                dest[d + 2] = srcPixels[s + 2];
                dest[d + 3] = srcPixels[s + 3];
            }
        }
        return dest;
    }

    public ValueTask ClearCacheAsync(CancellationToken ct)
    {
        _cache.Clear();
        return ValueTask.CompletedTask;
    }

    public ValueTask<long> GetCacheSizeAsync(CancellationToken ct)
        => ValueTask.FromResult(0L);
}
