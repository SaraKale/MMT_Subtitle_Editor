using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MMTGui.Localization;
using System;
using System.IO;
using System.Linq;

namespace MMTGui;

/// <summary>
/// MainWindow 音频操作分部类。
/// 负责音频文件的加载、播放控制、进度同步与音量调节。
/// </summary>
public partial class MainWindow
{
    #region 音频初始化与释放

    /// <summary>
    /// 初始化音频播放服务及进度同步计时器。
    /// 在窗口构造完成后调用。
    /// </summary>
    private void InitializeAudio()
    {
        _audioPlayer = new AudioPlayerService();
        _audioPlayer.PlaybackEnded += OnAudioPlaybackEnded;

        _audioProgressTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _audioProgressTimer.Tick += OnAudioProgressTick;

        if (AudioVolumeSlider != null)
            AudioVolumeSlider.Value = 1.0;
    }

    /// <summary>
    /// 释放音频相关资源。在窗口关闭时调用。
    /// </summary>
    private void ShutdownAudio()
    {
        if (_audioProgressTimer != null)
        {
            _audioProgressTimer.Stop();
            _audioProgressTimer.Tick -= OnAudioProgressTick;
            _audioProgressTimer = null;
        }

        if (_audioPlayer != null)
        {
            _audioPlayer.PlaybackEnded -= OnAudioPlaybackEnded;
            _audioPlayer.Dispose();
            _audioPlayer = null;
        }
    }

    #endregion

    #region 加载与卸载音频

    /// <summary>
    /// 打开文件对话框，选择并加载音频文件。
    /// </summary>
    private async void OnLoadAudio(object? sender, RoutedEventArgs e)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage == null)
        {
            await ShowErrorDialog(LocalizationService.T("当前环境无法打开文件对话框。"));
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationService.T("选择音频文件"),
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(LocalizationService.T("音频文件"))
                {
                    Patterns = new[] { "*.mp3", "*.wav", "*.m4a", "*.wma", "*.aac", "*.ogg", "*.flac" }
                },
                new FilePickerFileType(LocalizationService.T("所有文件"))
                {
                    Patterns = new[] { "*.*" }
                }
            }
        });

        if (files.Count == 0)
            return;

        await LoadAudioAsync(files[0].Path.LocalPath);
    }

    /// <summary>
    /// 加载指定路径的音频文件。
    /// </summary>
    private async System.Threading.Tasks.Task LoadAudioAsync(string filePath)
    {
        if (_audioPlayer == null)
            return;

        try
        {
            _audioPlayer.Load(filePath);
            UpdateAudioPlayIcon();
            UpdateAudioProgressDisplay();
            SetStatus(LocalizationService.F("已加载音频: {0}", Path.GetFileName(filePath)));
        }
        catch (Exception ex)
        {
            await ShowErrorDialog(LocalizationService.F("加载音频失败: {0}", ex.Message));
        }
    }

    /// <summary>
    /// 卸载当前音频。
    /// </summary>
    private void OnUnloadAudio(object? sender, RoutedEventArgs e)
    {
        UnloadAudio();
    }

    /// <summary>
    /// 卸载音频并重置 UI。
    /// </summary>
    private void UnloadAudio()
    {
        if (_audioPlayer == null)
            return;

        _audioPlayer.Stop();
        _audioPlayer.Dispose();
        _audioPlayer = new AudioPlayerService();
        _audioPlayer.PlaybackEnded += OnAudioPlaybackEnded;

        if (AudioProgressSlider != null)
            AudioProgressSlider.Value = 0;
        if (AudioTimeText != null)
            AudioTimeText.Text = "00:00 / 00:00";
        UpdateAudioPlayIcon();
    }

    #endregion

    #region 播放控制

    /// <summary>
    /// 播放或暂停音频。
    /// 音频播放时自动同步字幕预览。
    /// </summary>
    private void OnPlayPauseAudio(object? sender, RoutedEventArgs e)
    {
        if (_audioPlayer == null || !_audioPlayer.IsLoaded)
        {
            SetStatus(LocalizationService.T("请先加载音频文件"));
            return;
        }

        if (_audioPlayer.IsPlaying)
        {
            _audioPlayer.Pause();
            _audioProgressTimer?.Stop();
            UpdateAudioPlayIcon();
        }
        else
        {
            _audioPlayer.Play();
            _audioProgressTimer?.Start();
            UpdateAudioPlayIcon();
        }
    }

    /// <summary>
    /// 停止音频播放并重置位置。
    /// </summary>
    private void OnStopAudio(object? sender, RoutedEventArgs e)
    {
        if (_audioPlayer == null || !_audioPlayer.IsLoaded)
            return;

        _audioPlayer.Stop();
        _audioProgressTimer?.Stop();
        UpdateAudioProgressDisplay();
        UpdateAudioPlayIcon();
    }

    /// <summary>
    /// 音频自然播放结束。
    /// </summary>
    private void OnAudioPlaybackEnded(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _audioProgressTimer?.Stop();
            UpdateAudioProgressDisplay();
            UpdateAudioPlayIcon();

            if (_isPreviewing)
                OnStopPreview(this, new RoutedEventArgs());
        });
    }

    #endregion

    #region 进度同步

    /// <summary>
    /// 进度同步计时器回调。刷新进度条和时间显示。
    /// </summary>
    private void OnAudioProgressTick(object? sender, EventArgs e)
    {
        UpdateAudioProgressDisplay();
    }

    /// <summary>
    /// 刷新进度条和时间显示（不触发递归）。
    /// </summary>
    private void UpdateAudioProgressDisplay()
    {
        if (_audioPlayer == null || !_audioPlayer.IsLoaded)
            return;

        _isUpdatingAudioProgress = true;

        var total = _audioPlayer.TotalTime.TotalSeconds;
        var current = _audioPlayer.CurrentTime.TotalSeconds;

        if (AudioProgressSlider != null && total > 0)
            AudioProgressSlider.Value = (current / total) * AudioProgressSlider.Maximum;

        if (AudioTimeText != null)
            AudioTimeText.Text = $"{FormatAudioTime(current)} / {FormatAudioTime(total)}";

        _isUpdatingAudioProgress = false;
    }

    /// <summary>
    /// 拖动进度条结束，跳转到指定位置。
    /// </summary>
    private void OnAudioSeek(object? sender, PointerReleasedEventArgs e)
    {
        SeekFromSlider();
    }

    /// <summary>
    /// 键盘操作进度条（方向键）后跳转。
    /// </summary>
    private void OnAudioSeekKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up || e.Key == Key.Down)
            SeekFromSlider();
    }

    /// <summary>
    /// 根据当前进度条值跳转音频位置。
    /// </summary>
    private void SeekFromSlider()
    {
        if (_audioPlayer == null || !_audioPlayer.IsLoaded || AudioProgressSlider == null)
            return;

        var ratio = AudioProgressSlider.Value / AudioProgressSlider.Maximum;
        var target = TimeSpan.FromSeconds(ratio * _audioPlayer.TotalTime.TotalSeconds);
        _audioPlayer.Seek(target);
        UpdateAudioProgressDisplay();

        if (_isPreviewing)
        {
            _previewStartTime = target.TotalSeconds;
            RenderPreviewAtTime(_previewStartTime);
        }
    }

    #endregion

    #region 音量控制

    /// <summary>
    /// 音量滑块变化。
    /// </summary>
    private void OnAudioVolumeChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_audioPlayer != null)
            _audioPlayer.Volume = (float)e.NewValue;
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 更新播放/暂停按钮图标。
    /// </summary>
    private void UpdateAudioPlayIcon()
    {
        if (AudioPlayIcon == null || _audioPlayer == null)
            return;

        AudioPlayIcon.Text = _audioPlayer.IsPlaying ? "⏸" : "▶";
    }

    /// <summary>
    /// 格式化音频时间显示（mm:ss 或 h:mm:ss）。
    /// </summary>
    private static string FormatAudioTime(double seconds)
    {
        if (seconds < 0)
            seconds = 0;
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1
            ? ts.ToString(@"h\:mm\:ss")
            : ts.ToString(@"mm\:ss");
    }

    #endregion
}