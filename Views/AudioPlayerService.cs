using System;
using NAudio.Wave;

namespace MMTGui;

/// <summary>
/// 基于 NAudio 的音频播放服务。
/// 封装音频加载、播放、暂停、停止、进度跳转和音量控制。
/// </summary>
public sealed class AudioPlayerService : IDisposable
{
    private WaveOutEvent? _outputDevice;
    private AudioFileReader? _audioFile;
    private bool _disposed;

    /// <summary>当前已加载的音频文件路径。</summary>
    public string? FilePath { get; private set; }

    /// <summary>是否已加载音频。</summary>
    public bool IsLoaded => _audioFile != null;

    /// <summary>是否正在播放。</summary>
    public bool IsPlaying => _outputDevice?.PlaybackState == PlaybackState.Playing;

    /// <summary>是否处于暂停状态。</summary>
    public bool IsPaused => _outputDevice?.PlaybackState == PlaybackState.Paused;

    /// <summary>当前播放位置。</summary>
    public TimeSpan CurrentTime => _audioFile?.CurrentTime ?? TimeSpan.Zero;

    /// <summary>音频总时长。</summary>
    public TimeSpan TotalTime => _audioFile?.TotalTime ?? TimeSpan.Zero;

    /// <summary>音量（0.0 ~ 1.0）。</summary>
    public float Volume
    {
        get => _audioFile?.Volume ?? 1f;
        set
        {
            if (_audioFile != null)
                _audioFile.Volume = Math.Clamp(value, 0f, 1f);
        }
    }

    /// <summary>播放结束事件。</summary>
    public event EventHandler? PlaybackEnded;

    /// <summary>
    /// 加载音频文件。加载前会释放已有资源。
    /// </summary>
    public void Load(string filePath)
    {
        if (!System.IO.File.Exists(filePath))
            throw new System.IO.FileNotFoundException("音频文件不存在。", filePath);

        DisposeInternal();

        _audioFile = new AudioFileReader(filePath);
        _outputDevice = new WaveOutEvent();
        _outputDevice.Init(_audioFile);
        _outputDevice.PlaybackStopped += OnPlaybackStopped;
        FilePath = filePath;
    }

    /// <summary>
    /// 开始或继续播放。
    /// </summary>
    public void Play()
    {
        if (_outputDevice != null && _audioFile != null && _outputDevice.PlaybackState != PlaybackState.Playing)
        {
            _outputDevice.Play();
        }
    }

    /// <summary>
    /// 暂停播放（保留当前位置）。
    /// </summary>
    public void Pause()
    {
        _outputDevice?.Pause();
    }

    /// <summary>
    /// 停止播放并重置到起始位置。
    /// </summary>
    public void Stop()
    {
        if (_outputDevice != null && _audioFile != null)
        {
            _outputDevice.Stop();
            _audioFile.CurrentTime = TimeSpan.Zero;
        }
    }

    /// <summary>
    /// 跳转到指定时间位置。
    /// </summary>
    public void Seek(TimeSpan time)
    {
        if (_audioFile != null)
        {
            var clamped = TimeSpan.Zero;
            if (time > TimeSpan.Zero)
                clamped = time;
            if (clamped > _audioFile.TotalTime)
                clamped = _audioFile.TotalTime;
            _audioFile.CurrentTime = clamped;
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        // 自然播放结束（非用户主动停止）时触发
        if (_audioFile != null && _audioFile.CurrentTime >= _audioFile.TotalTime)
        {
            PlaybackEnded?.Invoke(this, EventArgs.Empty);
        }
    }

    private void DisposeInternal()
    {
        if (_outputDevice != null)
        {
            _outputDevice.PlaybackStopped -= OnPlaybackStopped;
            _outputDevice.Dispose();
            _outputDevice = null;
        }
        _audioFile?.Dispose();
        _audioFile = null;
        FilePath = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        DisposeInternal();
        _disposed = true;
    }
}