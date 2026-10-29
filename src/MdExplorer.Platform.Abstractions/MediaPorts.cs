namespace MdExplorer.Platform.Abstractions;

public enum MediaPlaybackState
{
    Idle,
    Opening,
    Playing,
    Paused,
    Stopped,
    Ended,
    Failed,
}

/// <summary>后端自述能力。UI 只按这里说的显隐控件，不猜平台。</summary>
public sealed record MediaPlaybackCapabilities(
    bool CanPlayAudio,
    bool CanPlayVideo,
    string BackendName,
    string Diagnostic)
{
    public bool IsSupported => CanPlayAudio || CanPlayVideo;
}

/// <summary>
/// 内嵌音视频播放端口（平台无关）。实现负责解码与输出；
/// 调用方不得假定任何实现是线程安全的，全部调用都在 UI 线程发起。
/// </summary>
public interface IMediaPlaybackService : IAsyncDisposable
{
    MediaPlaybackCapabilities Capabilities { get; }
    MediaPlaybackState State { get; }
    TimeSpan Position { get; }
    TimeSpan Duration { get; }
    int VolumePercent { get; }

    /// <summary>最近一次失败的用户可读原因；成功打开或停止后清空。</summary>
    string? LastError { get; }

    /// <summary>状态、位置或时长变化。可能来自后台线程，订阅方自行切回 UI 线程。</summary>
    event EventHandler? PlaybackChanged;

    /// <summary>打开媒体但不自动播放；返回是否成功。</summary>
    Task<bool> OpenAsync(string path, CancellationToken cancellationToken);

    Task PlayAsync(CancellationToken cancellationToken);
    Task PauseAsync();
    Task StopAsync();
    Task SeekAsync(TimeSpan position);

    /// <summary>0–100，实现自行夹取。</summary>
    void SetVolume(int percent);
}

/// <summary>
/// 没有位置回调的后端（如 Android MediaPlayer）实现它，宿主定时器调用即可把进度推给 UI。
/// 支持回调的后端不必实现。
/// </summary>
public interface IMediaPlaybackPoller
{
    void PollPosition();
}
