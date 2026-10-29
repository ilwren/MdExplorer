using MdExplorer.Platform.Abstractions;

namespace MdExplorer.Screenshots;

/// <summary>
/// 截图用的假播放后端：真实解码依赖平台能力（Windows Media Foundation / Android MediaPlayer），
/// 无头 CI 里没有，所以只模拟“已打开、已播放、时长 3:12、位置 0:42”这一个可截图状态。
/// </summary>
internal sealed class StubMediaPlayback : IMediaPlaybackService
{
    public MediaPlaybackCapabilities Capabilities { get; } = new(
        CanPlayAudio: true,
        CanPlayVideo: true,
        BackendName: "截图假后端（StubMediaPlayback）",
        Diagnostic: "截图环境没有真实解码器；真机由平台后端播放。");

    public MediaRendererKind RendererKind => MediaRendererKind.FramePush;

    public MediaPlaybackState State { get; private set; } = MediaPlaybackState.Idle;
    public TimeSpan Position { get; private set; } = TimeSpan.FromSeconds(42);
    public TimeSpan Duration { get; private set; } = TimeSpan.FromSeconds(192);
    public int VolumePercent { get; private set; } = 80;
    public string? LastError { get; private set; }

    public event EventHandler? PlaybackChanged;

    public Task<bool> OpenAsync(string path, CancellationToken cancellationToken)
    {
        State = MediaPlaybackState.Paused;
        Raise();
        return Task.FromResult(true);
    }

    public Task PlayAsync(CancellationToken cancellationToken)
    {
        State = MediaPlaybackState.Playing;
        Raise();
        return Task.CompletedTask;
    }

    public Task PauseAsync()
    {
        State = MediaPlaybackState.Paused;
        Raise();
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        State = MediaPlaybackState.Stopped;
        Position = TimeSpan.Zero;
        Raise();
        return Task.CompletedTask;
    }

    public Task SeekAsync(TimeSpan position)
    {
        Position = position < TimeSpan.Zero ? TimeSpan.Zero : position;
        Raise();
        return Task.CompletedTask;
    }

    public void SetVolume(int percent)
    {
        VolumePercent = Math.Clamp(percent, 0, 100);
        Raise();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private void Raise() => PlaybackChanged?.Invoke(this, EventArgs.Empty);
}
