#if !WINDOWS
using System.Runtime.InteropServices;
using MdExplorer.Platform.Abstractions;

namespace MdExplorer.Desktop;

/// <summary>
/// Linux 系统解码：直接驱动系统里的 GStreamer（playbin 会自动挑 ffmpeg/vaapi/alsa 组件）。
/// 选择它的理由：GStreamer 是发行版自带的 LGPL 媒体栈，不用往仓库里塞 FFmpeg 二进制，
/// 也不用自己维护原生构建链；代价是「装了哪些插件」由发行版决定，所以我们只报事实：
/// 库缺失就能力位 false，绝不给一个看得见却不出声的播放器。
/// </summary>
internal sealed class GStreamerMediaPlaybackService : IMediaPlaybackService, IMediaPlaybackPoller
{
    private const string Library = "libgstreamer-1.0.so.0";

    // GstState / GstFormat / GstSeekFlags 的数值来自 gst/gstelement.h 与 gst/gstformat.h。
    private const int StateNull = 1;
    private const int StatePaused = 3;
    private const int StatePlaying = 4;
    private const int FormatTime = 3;
    private const int SeekFlagFlush = 1;
    private const int SeekFlagAccurate = 2;

    private static readonly object InitGate = new();
    private static bool _initialized;
    private static string? _initFailure;

    private readonly object _gate = new();
    private IntPtr _pipeline;
    private bool _prepared;
    private bool _ended;
    private bool _disposed;

    public GStreamerMediaPlaybackService()
    {
        EnsureInitialized();
    }

    public MediaPlaybackCapabilities Capabilities => _initFailure is null
        ? new(true, false, "GStreamer（系统媒体栈）",
            "音频由发行版自带的 GStreamer 解码；插件缺失时打开会失败并给出原因。视频画面本版交系统应用打开。")
        : new(false, false, "none", $"GStreamer 不可用：{_initFailure}");

    public MediaRendererKind RendererKind => MediaRendererKind.None;

    public MediaPlaybackState State { get; private set; } = MediaPlaybackState.Idle;
    public int VolumePercent { get; private set; } = 100;
    public string? LastError { get; private set; }

    public event EventHandler? PlaybackChanged;

    public TimeSpan Position
    {
        get
        {
            if (!_prepared || _pipeline == IntPtr.Zero)
                return TimeSpan.Zero;
            return QueryPosition(_pipeline) ?? TimeSpan.Zero;
        }
    }

    public TimeSpan Duration
    {
        get
        {
            if (!_prepared || _pipeline == IntPtr.Zero)
                return TimeSpan.Zero;
            return QueryDuration(_pipeline) ?? TimeSpan.Zero;
        }
    }

    public Task<bool> OpenAsync(string path, CancellationToken cancellationToken)
    {
        if (_disposed || _initFailure is not null)
        {
            LastError = Capabilities.Diagnostic;
            State = MediaPlaybackState.Failed;
            RaiseChanged();
            return Task.FromResult(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            try
            {
                DestroyPipeline();
                var uri = new Uri(path).AbsoluteUri.Replace("\"", "%22", StringComparison.Ordinal);
                var launch = $"playbin uri=\"{uri}\"";
                _pipeline = gst_parse_launch(launch, out var error);
                if (_pipeline == IntPtr.Zero)
                {
                    var message = error == IntPtr.Zero ? "无法创建 playbin" : "gst_parse_launch 失败";
                    LastError = $"GStreamer 无法为该文件建立播放管线：{message}";
                    State = MediaPlaybackState.Failed;
                    RaiseChanged();
                    return Task.FromResult(false);
                }

                ApplyVolume();
                var change = gst_element_set_state(_pipeline, StatePaused);
                // ASYNC 是正常返回：真正的失败要等 get_state 或后续 poll 才知道。
                if (change == 0)
                {
                    LastError = "GStreamer 拒绝了暂停态切换（可能缺少解码插件）。";
                    State = MediaPlaybackState.Failed;
                    DestroyPipeline();
                    RaiseChanged();
                    return Task.FromResult(false);
                }

                _prepared = true;
                _ended = false;
                LastError = null;
                State = MediaPlaybackState.Paused;
            }
            catch (Exception ex) when (IsPlatformFailure(ex) || ex is UriFormatException)
            {
                LastError = $"无法打开：{ex.Message}";
                State = MediaPlaybackState.Failed;
                DestroyPipeline();
            }
        }

        RaiseChanged();
        return Task.FromResult(State == MediaPlaybackState.Paused);
    }

    public Task PlayAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_prepared || _pipeline == IntPtr.Zero || _ended)
                return Task.CompletedTask;
            if (gst_element_set_state(_pipeline, StatePlaying) == 0)
            {
                LastError = "GStreamer 无法进入播放态（缺少解码插件或设备被占用）。";
                State = MediaPlaybackState.Failed;
                RaiseChanged();
                return Task.CompletedTask;
            }
            State = MediaPlaybackState.Playing;
        }
        RaiseChanged();
        return Task.CompletedTask;
    }

    public Task PauseAsync()
    {
        lock (_gate)
        {
            if (!_prepared || _pipeline == IntPtr.Zero)
                return Task.CompletedTask;
            gst_element_set_state(_pipeline, StatePaused);
            State = MediaPlaybackState.Paused;
        }
        RaiseChanged();
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        lock (_gate)
        {
            if (!_prepared || _pipeline == IntPtr.Zero)
                return Task.CompletedTask;
            gst_element_set_state(_pipeline, StateNull);
            _prepared = false;
            State = MediaPlaybackState.Stopped;
        }
        RaiseChanged();
        return Task.CompletedTask;
    }

    public Task SeekAsync(TimeSpan position)
    {
        lock (_gate)
        {
            if (!_prepared || _pipeline == IntPtr.Zero)
                return Task.CompletedTask;
            var target = position < TimeSpan.Zero ? 0 : (long)position.TotalMilliseconds * 1_000_000L;
            var duration = QueryDuration(_pipeline);
            if (duration is { } total)
            {
                var limit = total.Ticks * 100L;
                if (target > limit) target = limit;
            }
            gst_element_seek_simple(_pipeline, FormatTime, SeekFlagFlush | SeekFlagAccurate, target);
            _ended = false;
        }
        RaiseChanged();
        return Task.CompletedTask;
    }

    public void SetVolume(int percent)
    {
        VolumePercent = Math.Clamp(percent, 0, 100);
        lock (_gate)
        {
            if (_prepared && _pipeline != IntPtr.Zero)
                ApplyVolume();
        }
        RaiseChanged();
    }

    /// <summary>由宿主定时器调用：GStreamer 的状态靠查询推进，没有托管事件。</summary>
    public void PollPosition()
    {
        if (!_prepared || _pipeline == IntPtr.Zero)
            return;
        var duration = QueryDuration(_pipeline);
        var position = QueryPosition(_pipeline);
        if (State == MediaPlaybackState.Playing && duration is { } total && position is { } current &&
            total > TimeSpan.Zero && current >= total - TimeSpan.FromMilliseconds(250))
        {
            _ended = true;
            State = MediaPlaybackState.Ended;
        }
        RaiseChanged();
    }

    private void RaiseChanged() => PlaybackChanged?.Invoke(this, EventArgs.Empty);

    private void ApplyVolume()
    {
        try
        {
            gst_util_set_object_arg(_pipeline, "volume", (VolumePercent / 100.0).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (IsPlatformFailure(ex))
        {
            // 音量设置失败不影响播放本身，只影响音量。
        }
    }

    private void DestroyPipeline()
    {
        if (_pipeline == IntPtr.Zero)
            return;
        try
        {
            gst_element_set_state(_pipeline, StateNull);
            gst_object_unref(_pipeline);
        }
        catch (Exception ex) when (IsPlatformFailure(ex))
        {
        }
        _pipeline = IntPtr.Zero;
        _prepared = false;
    }

    private static void EnsureInitialized()
    {
        lock (InitGate)
        {
            if (_initialized || _initFailure is not null)
                return;
            try
            {
                gst_init(IntPtr.Zero, IntPtr.Zero);
                _initialized = true;
            }
            catch (Exception ex) when (IsPlatformFailure(ex))
            {
                _initFailure = ex is DllNotFoundException
                    ? "系统未安装 GStreamer（libgstreamer-1.0.so.0）"
                    : ex.Message;
            }
        }
    }

    private static TimeSpan? QueryPosition(IntPtr pipeline) =>
        Query(pipeline, gst_element_query_position);

    private static TimeSpan? QueryDuration(IntPtr pipeline) =>
        Query(pipeline, gst_element_query_duration);

    private static TimeSpan? Query(IntPtr pipeline, QueryDelegate query)
    {
        try
        {
            if (query(pipeline, FormatTime, out var value) == 0 || value < 0)
                return null;
            return TimeSpan.FromTicks(value / 100); // GStreamer 的单位是纳秒
        }
        catch (Exception ex) when (IsPlatformFailure(ex))
        {
            return null;
        }
    }

    private static bool IsPlatformFailure(Exception ex) =>
        ex is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException
            or NotSupportedException or UnauthorizedAccessException;

    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;
        _disposed = true;
        lock (_gate)
        {
            DestroyPipeline();
        }
        return ValueTask.CompletedTask;
    }

    private delegate int QueryDelegate(IntPtr element, int format, out long value);

    [DllImport(Library)]
    private static extern void gst_init(IntPtr argc, IntPtr argv);

    [DllImport(Library)]
    private static extern IntPtr gst_parse_launch(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string pipeline, out IntPtr error);

    [DllImport(Library)]
    private static extern int gst_element_set_state(IntPtr element, int state);

    [DllImport(Library)]
    private static extern int gst_element_query_position(IntPtr element, int format, out long position);

    [DllImport(Library)]
    private static extern int gst_element_query_duration(IntPtr element, int format, out long duration);

    [DllImport(Library)]
    private static extern int gst_element_seek_simple(IntPtr element, int format, int flags, long position);

    [DllImport(Library)]
    private static extern void gst_util_set_object_arg(
        IntPtr obj, [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport(Library)]
    private static extern void gst_object_unref(IntPtr obj);
}
#endif
