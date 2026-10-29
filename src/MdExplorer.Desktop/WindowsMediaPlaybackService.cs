#if WINDOWS
using Windows.Media.Core;
using MdExplorer.Platform.Abstractions;
// 平台抽象里也有一个 MediaPlaybackState；不整体 using Windows.Media.Playback，避免同名类型二义。
using WinRtMediaPlayer = Windows.Media.Playback.MediaPlayer;
using WinRtAudioCategory = Windows.Media.Playback.MediaPlayerAudioCategory;
using WinRtState = Windows.Media.Playback.MediaPlaybackState;

namespace MdExplorer.Desktop;

/// <summary>
/// Windows 系统解码：直接使用 Media Foundation 之上的 WinRT <see cref="WinRtMediaPlayer"/>。
/// 音频没有画面宿主问题，所以这里先把三端里最容易漏的一端补上；
/// 视频画面需要 Surface/HWND 宿主（MediaPlayer 自己不会画进 Avalonia），本版如实报 None。
/// 整套调用都在 try/catch 里：WinRT 在未打包桌面进程、精简系统或缺少媒体功能包时会抛，
/// 此时能力位保持 false 并给出可执行建议，绝不假装能播。
/// </summary>
internal sealed class WindowsMediaPlaybackService : IMediaPlaybackService, IMediaProbe, IMediaPlaybackPoller
{
    private readonly WinRtMediaPlayer _player = new();
    private readonly object _gate = new();
    private string? _path;
    private bool _ready;
    private bool _disposed;

    public WindowsMediaPlaybackService()
    {
        try
        {
            _player.AudioCategory = WinRtAudioCategory.Media;
            _player.MediaOpened += (_, _) =>
            {
                _ready = true;
                State = MediaPlaybackState.Paused;
                RaiseChanged();
            };
            _player.MediaEnded += (_, _) =>
            {
                State = MediaPlaybackState.Ended;
                RaiseChanged();
            };
            _player.MediaFailed += (_, args) =>
            {
                LastError = $"Media Foundation 打不开该文件：{args.ErrorMessage}";
                State = MediaPlaybackState.Failed;
                RaiseChanged();
            };
            _player.PlaybackSession.PlaybackStateChanged += (_, _) =>
            {
                if (_player.PlaybackSession.PlaybackState is WinRtState.Playing)
                    State = MediaPlaybackState.Playing;
                else if (State == MediaPlaybackState.Playing)
                    State = MediaPlaybackState.Paused;
                RaiseChanged();
            };
        }
        catch (Exception ex) when (IsPlatformFailure(ex))
        {
            Unavailable = ex.Message;
        }
    }

    /// <summary>构造期失败原因（非 null 表示 WinRT 媒体栈在本机不可用）。</summary>
    public string? Unavailable { get; }

    public MediaPlaybackCapabilities Capabilities => Unavailable is null
        ? new(true, false, "Windows Media Foundation",
            "音频由系统 Media Foundation 解码；视频画面需要原生宿主，本版交系统应用打开。")
        : new(false, false, "none", $"Windows 媒体栈不可用：{Unavailable}");

    public MediaRendererKind RendererKind => MediaRendererKind.None;

    public MediaPlaybackState State { get; private set; } = MediaPlaybackState.Idle;
    public TimeSpan Position => _ready ? _player.PlaybackSession.Position : TimeSpan.Zero;
    public TimeSpan Duration => _ready ? _player.PlaybackSession.NaturalDuration : TimeSpan.Zero;
    public int VolumePercent { get; private set; } = 100;
    public string? LastError { get; private set; }

    public event EventHandler? PlaybackChanged;

    public Task<bool> OpenAsync(string path, CancellationToken cancellationToken)
    {
        if (_disposed || Unavailable is not null)
        {
            LastError = Capabilities.Diagnostic;
            State = MediaPlaybackState.Failed;
            RaiseChanged();
            return Task.FromResult(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            lock (_gate)
            {
                _player.Source = MediaSource.CreateFromUri(new Uri(path));
                _path = path;
                _ready = false;
                LastError = null;
                State = MediaPlaybackState.Opening;
            }
            RaiseChanged();
            return Task.FromResult(true);
        }
        catch (Exception ex) when (IsPlatformFailure(ex) || ex is UriFormatException)
        {
            LastError = $"无法打开：{ex.Message}";
            State = MediaPlaybackState.Failed;
            RaiseChanged();
            return Task.FromResult(false);
        }
    }

    public Task PlayAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_disposed || _path is null)
            return Task.CompletedTask;
        _player.Play();
        State = MediaPlaybackState.Playing;
        RaiseChanged();
        return Task.CompletedTask;
    }

    public Task PauseAsync()
    {
        if (_disposed || _path is null)
            return Task.CompletedTask;
        _player.Pause();
        State = MediaPlaybackState.Paused;
        RaiseChanged();
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (_disposed || _path is null)
            return Task.CompletedTask;
        _player.Pause();
        _player.PlaybackSession.Position = TimeSpan.Zero;
        State = MediaPlaybackState.Stopped;
        RaiseChanged();
        return Task.CompletedTask;
    }

    public Task SeekAsync(TimeSpan position)
    {
        if (_disposed || !_ready)
            return Task.CompletedTask;
        var duration = Duration;
        var target = position < TimeSpan.Zero ? TimeSpan.Zero
            : duration > TimeSpan.Zero && position > duration ? duration : position;
        _player.PlaybackSession.Position = target;
        RaiseChanged();
        return Task.CompletedTask;
    }

    public void SetVolume(int percent)
    {
        VolumePercent = Math.Clamp(percent, 0, 100);
        if (_disposed)
            return;
        try { _player.Volume = VolumePercent / 100.0; }
        catch (Exception ex) when (IsPlatformFailure(ex)) { }
        RaiseChanged();
    }

    /// <summary>MediaPlayer 的位置变化事件频率不可控，由宿主定时器统一拉取。</summary>
    public void PollPosition()
    {
        if (_ready)
            RaiseChanged();
    }

    /// <summary>
    /// 预判只看容器清单：Windows 的解码器由系统组件与已安装扩展决定，
    /// 逐个文件真开一遍会带来可见延迟。文案里必须点明 HEVC 扩展与 DTS/AC-3 的现实缺口。
    /// </summary>
    public Task<MediaProbeResult> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var videoContainers = new[] { ".mp4", ".m4v", ".mov", ".mkv", ".webm", ".avi", ".wmv" };
        var audioContainers = new[] { ".mp3", ".m4a", ".aac", ".wav", ".flac", ".ogg", ".opus", ".wma" };

        if (videoContainers.Contains(extension))
            return Task.FromResult(new MediaProbeResult(
                false, true, true, "含视频轨（系统容器清单内）",
                "视频画面需要原生宿主，本版交系统应用打开；HEVC 需系统扩展、DTS/AC-3 默认缺失。"));

        if (audioContainers.Contains(extension))
            return Task.FromResult(new MediaProbeResult(
                true, false, true, $"音频 · {extension.TrimStart('.').ToUpperInvariant()}（系统容器清单内）",
                null));

        return Task.FromResult(new MediaProbeResult(
            false, false, false, "容器不在系统清单内",
            "Windows Media Foundation 对这些容器没有内置支持，可用系统应用打开。"));
    }

    private void RaiseChanged() => PlaybackChanged?.Invoke(this, EventArgs.Empty);

    private static bool IsPlatformFailure(Exception ex) =>
        ex is System.Runtime.InteropServices.COMException
            or TypeLoadException
            or DllNotFoundException
            or EntryPointNotFoundException
            or NotSupportedException
            or InvalidOperationException
            or UnauthorizedAccessException
            or FileNotFoundException;

    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;
        _disposed = true;
        try { _player.Dispose(); }
        catch (Exception ex) when (IsPlatformFailure(ex)) { }
        return ValueTask.CompletedTask;
    }
}
#endif
