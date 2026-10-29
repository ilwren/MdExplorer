using Android.Media;
using MdExplorer.Platform.Abstractions;

namespace MdExplorer.Android;

/// <summary>
/// Android 内嵌播放：用系统 <see cref="MediaPlayer"/> 播放音频轨。
/// 第一切片只做音频——视频画面需要 SurfaceView/TextureView 与 Avalonia 合成的专门桥接，
/// 在没有真机验证前不声称支持；视频文件仍走“系统应用打开”，能力位如实为 false。
/// </summary>
public sealed class AndroidMediaPlaybackService : IMediaPlaybackService, IMediaPlaybackPoller
{
    private readonly MediaPlayer _player = new();
    private readonly object _gate = new();
    private string? _path;
    private bool _prepared;
    private bool _disposed;

    public AndroidMediaPlaybackService()
    {
        _player.Completion += (_, _) =>
        {
            State = MediaPlaybackState.Ended;
            RaiseChanged();
        };
        _player.Error += (_, args) =>
        {
            LastError = $"Android MediaPlayer 报错：{args.What}";
            State = MediaPlaybackState.Failed;
            RaiseChanged();
        };
    }

    public MediaPlaybackCapabilities Capabilities { get; } = new(
        CanPlayAudio: true,
        CanPlayVideo: false,
        BackendName: "Android MediaPlayer",
        Diagnostic: "音频在应用内播放；视频画面请用系统应用打开（本版未提供内嵌视频画面）。");

    public MediaPlaybackState State { get; private set; } = MediaPlaybackState.Idle;
    public TimeSpan Position => _prepared ? TimeSpan.FromMilliseconds(_player.CurrentPosition) : TimeSpan.Zero;
    public TimeSpan Duration => _prepared ? TimeSpan.FromMilliseconds(_player.Duration) : TimeSpan.Zero;
    public int VolumePercent { get; private set; } = 100;
    public string? LastError { get; private set; }

    public event EventHandler? PlaybackChanged;

    public Task<bool> OpenAsync(string path, CancellationToken cancellationToken)
    {
        if (_disposed)
            return Task.FromResult(false);
        if (FileKindIsVideo(path))
        {
            LastError = "Android 内嵌播放当前只支持音频；视频请用系统应用打开。";
            State = MediaPlaybackState.Failed;
            RaiseChanged();
            return Task.FromResult(false);
        }

        lock (_gate)
        {
            try
            {
                _player.Reset();
                _prepared = false;
                _path = path;
                _player.SetDataSource(path);
                _player.SetVolume(VolumePercent / 100f, VolumePercent / 100f);
                _player.Prepare();
                _prepared = true;
                LastError = null;
                State = MediaPlaybackState.Paused;
                RaiseChanged();
                return Task.FromResult(true);
            }
            catch (Exception ex) when (ex is Java.IO.IOException or Java.Lang.IllegalStateException or ArgumentException)
            {
                LastError = $"无法打开音频：{ex.Message}";
                State = MediaPlaybackState.Failed;
                RaiseChanged();
                return Task.FromResult(false);
            }
        }
    }

    public Task PlayAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_prepared)
                return Task.CompletedTask;
            _player.Start();
            State = MediaPlaybackState.Playing;
        }
        RaiseChanged();
        return Task.CompletedTask;
    }

    public Task PauseAsync()
    {
        lock (_gate)
        {
            if (!_prepared)
                return Task.CompletedTask;
            if (_player.IsPlaying)
                _player.Pause();
            State = MediaPlaybackState.Paused;
        }
        RaiseChanged();
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        lock (_gate)
        {
            if (!_prepared)
                return Task.CompletedTask;
            _player.Pause();
            _player.SeekTo(0);
            State = MediaPlaybackState.Stopped;
        }
        RaiseChanged();
        return Task.CompletedTask;
    }

    public Task SeekAsync(TimeSpan position)
    {
        lock (_gate)
        {
            if (!_prepared)
                return Task.CompletedTask;
            var milliseconds = (int)Math.Clamp(position.TotalMilliseconds, 0, Math.Max(0, _player.Duration));
            _player.SeekTo(milliseconds);
        }
        RaiseChanged();
        return Task.CompletedTask;
    }

    public void SetVolume(int percent)
    {
        VolumePercent = Math.Clamp(percent, 0, 100);
        lock (_gate)
        {
            if (_prepared)
                _player.SetVolume(VolumePercent / 100f, VolumePercent / 100f);
        }
        RaiseChanged();
    }

    /// <summary>由宿主定时器调用：把播放进度推给 UI（Android MediaPlayer 没有位置回调）。</summary>
    public void PollPosition()
    {
        if (_prepared)
            RaiseChanged();
    }

    private void RaiseChanged() => PlaybackChanged?.Invoke(this, EventArgs.Empty);

    private static bool FileKindIsVideo(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.ToLowerInvariant() is ".mp4" or ".mkv" or ".webm" or ".avi" or ".mov" or ".m4v" or ".3gp";
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;
        _disposed = true;
        lock (_gate)
        {
            try
            {
                _player.Reset();
                _player.Release();
            }
            catch (Java.Lang.IllegalStateException)
            {
                // 释放期间的 IllegalState 不应阻止宿主退出
            }
            finally
            {
                _player.Dispose();
            }
        }
        return ValueTask.CompletedTask;
    }
}
