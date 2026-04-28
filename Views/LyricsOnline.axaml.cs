using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MMTGui.Localization;
using SukiUI.Controls;

namespace MMTGui;

/// <summary>
/// 在线歌词窗口代码后置。
/// 负责加载歌曲信息、展示歌词、保存 LRC，并将歌词插入主脚本。
/// </summary>
public partial class LyricsOnline : SukiWindow
{
    /// <summary>关联的主窗口实例，用于回写脚本文本。</summary>
    private readonly MainWindow? _ownerWindow;
    /// <summary>当前已加载的歌曲对象。</summary>
    private SearchSongItem? _currentSong;

    /// <summary>
    /// 供设计器与独立预览使用的构造函数。
    /// </summary>
    public LyricsOnline()
        : this(null)
    {
    }

    /// <summary>
    /// 创建在线歌词窗口，并可选地绑定主窗口实例。
    /// </summary>
    public LyricsOnline(MainWindow? ownerWindow)
    {
        _ownerWindow = ownerWindow;

        InitializeComponent();
        UpdateFormatAvailability();
        ApplySongInfo(CreatePreviewSong());
        LocalizationService.RegisterWindow(this, ApplyLocalizedState);
    }

    /// <summary>
    /// 根据当前来源与输入内容执行歌曲查询。
    /// </summary>
    private async void OnOpenSearchLink(object? sender, RoutedEventArgs e)
    {
        var source = GetSelectedComboValue(CmbSource);
        var query = TxtSongLink?.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(query))
        {
            await ShowMessageDialogAsync(LocalizationService.T("请输入歌曲链接、歌曲 ID 或关键字。"), false); // 请输入歌曲链接、歌曲 ID 或关键字。
            return;
        }

        if (string.Equals(source, "qq", StringComparison.Ordinal))
        {
            try
            {
                if (QqMusicApi.TryExtractSongMid(query, out var songMid))
                {
                    await LoadQqSongByMidAsync(songMid);
                    return;
                }

                await ShowMessageDialogAsync(LocalizationService.T("QQ音乐目前只支持直接输入链接或id，暂时不支持关键字搜索。"), false); // QQ音乐目前只支持直接输入链接或id，暂时不支持关键字搜索。
            }
            catch (Exception ex)
            {
                await ShowMessageDialogAsync(LocalizationService.F("搜索歌词失败: {0}", ex.Message), false); // 搜索歌词失败: {0}
            }

            return;
        }

        try
        {
            if (NetEaseLyricsApi.TryExtractSongId(query, out var songId))
            {
                await LoadSongByIdAsync(songId);
                return;
            }

            var dialog = new SearchLink(query, source);
            var selectedSong = await dialog.ShowDialog<SearchSongItem?>(this);
            if (selectedSong == null)
                return;

            await NetEaseLyricsApi.EnsureLyricsLoadedAsync(selectedSong);
            ApplySongInfo(selectedSong);
        }
        catch (Exception ex)
        {
            await ShowMessageDialogAsync(LocalizationService.F("搜索歌词失败: {0}", ex.Message), false); // 搜索歌词失败: {0}
        }
    }

    /// <summary>
    /// 切换歌词模式时刷新格式可用性与歌词内容。
    /// </summary>
    private void OnDisplayModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateFormatAvailability();
        UpdateLyricsText();
    }

    /// <summary>
    /// 切换双语歌词格式时刷新显示文本。
    /// </summary>
    private void OnLyricsFormatChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateLyricsText();
    }

    /// <summary>
    /// 将当前歌词保存为本地 LRC 文件。
    /// </summary>
    private async void OnSaveLyrics(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtLyrics?.Text))
        {
            await ShowMessageDialogAsync(LocalizationService.T("当前没有可保存的歌词内容。"), false); // 当前没有可保存的歌词内容。
            return;
        }

        var storageProvider = GetTopLevel(this)?.StorageProvider;
        if (storageProvider == null)
        {
            await ShowMessageDialogAsync(LocalizationService.T("当前环境无法打开保存对话框。"), false); // 当前环境无法打开保存对话框。
            return;
        }

        var suggestedName = string.IsNullOrWhiteSpace(_currentSong?.SongName)
            ? "lyrics"
            : SanitizeFileName(_currentSong!.SongName);

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LocalizationService.T("保存 LRC 歌词"), // 保存 LRC 歌词
            SuggestedFileName = suggestedName,
            DefaultExtension = "lrc",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("LRC Lyrics") { Patterns = new[] { "*.lrc" } }
            }
        });

        if (file == null)
            return;

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(TxtLyrics!.Text);

        await ShowMessageDialogAsync(LocalizationService.T("歌词已保存。"), true); // 歌词已保存。
    }

    /// <summary>
    /// 把当前歌词转换为 MMT 脚本块并插入主脚本。
    /// </summary>
    private async void OnInsertToScript(object? sender, RoutedEventArgs e)
    {
        if (_ownerWindow == null)
        {
            await ShowMessageDialogAsync(LocalizationService.T("当前窗口没有关联主脚本编辑器，暂时无法插入脚本。"), false); // 当前窗口没有关联主脚本编辑器，暂时无法插入脚本。
            return;
        }

        if (_currentSong == null)
        {
            await ShowMessageDialogAsync(LocalizationService.T("请先获取歌词后再插入到脚本。"), false); // 请先获取歌词后再插入到脚本。
            return;
        }

        var blocks = OnlineLyricsFormatter.BuildScriptBlocks(_currentSong, GetDisplayMode(), GetCombineMode());
        if (blocks.Count == 0)
        {
            await ShowMessageDialogAsync(LocalizationService.T("当前歌词中没有可插入脚本的时间标签。"), false); // 当前歌词中没有可插入脚本的时间标签。
            return;
        }

        var scriptText = OnlineLyricsFormatter.FormatScriptBlocks(blocks, GetTimeCodeFormat());
        _ownerWindow.InsertLyricsScriptBlock(scriptText);
        await ShowMessageDialogAsync(LocalizationService.T("歌词已插入到脚本的 :end 之前。"), true); // 歌词已插入到脚本的 :end 之前。
    }

    /// <summary>
    /// 通过网易云歌曲 ID 加载歌曲详情与歌词。
    /// </summary>
    private async Task LoadSongByIdAsync(long songId)
    {
        SetLoadingState(true, LocalizationService.T("正在获取网易云歌曲信息和歌词...")); // 正在获取网易云歌曲信息和歌词...

        try
        {
            var song = await NetEaseLyricsApi.GetSongAsync(songId);
            ApplySongInfo(song);
        }
        finally
        {
            SetLoadingState(false);
        }
    }

    /// <summary>
    /// 通过 QQ 音乐的 songmid 加载歌曲详情与歌词。
    /// </summary>
    private async Task LoadQqSongByMidAsync(string songMid)
    {
        SetLoadingState(true, LocalizationService.T("正在获取 QQ 音乐歌曲信息和歌词...")); // 正在获取 QQ 音乐歌曲信息和歌词...

        try
        {
            var song = await QqMusicApi.GetSongAsync(songMid);
            ApplySongInfo(song);
        }
        finally
        {
            SetLoadingState(false);
        }
    }

    /// <summary>
    /// 将歌曲元数据回填到界面各个信息区。
    /// </summary>
    private void ApplySongInfo(SearchSongItem song)
    {
        _currentSong = song;

        if (TxtSongLink != null)
            TxtSongLink.Text = song.SongLink ?? string.Empty;

        if (TxtSongName != null)
            TxtSongName.Text = string.IsNullOrWhiteSpace(song.SongName) ? LocalizationService.T("未选择歌曲") : song.SongName; // 歌曲名称

        if (TxtSinger != null)
            TxtSinger.Text = LocalizationService.F("歌手: {0}", DisplayOrDash(song.Singer)); // 歌手

        if (TxtAlbum != null)
            TxtAlbum.Text = LocalizationService.F("专辑: {0}", DisplayOrDash(song.Album)); // 专辑

        if (TxtDuration != null)
            TxtDuration.Text = LocalizationService.F("时长: {0}", DisplayOrDash(song.SongDuration)); // 时长

        if (TxtPublishDate != null)
            TxtPublishDate.Text = LocalizationService.F("发行日期: {0}", DisplayOrDash(song.PublishDate)); // 发行日期

        if (TxtSongSource != null)
            TxtSongSource.Text = LocalizationService.F("源: {0}", DisplayOrDash(LocalizationService.T(song.SongSource))); // 来源

        SelectComboBoxItemByTag(CmbSource, song.SourceId);
        UpdateLyricsText();
    }

    /// <summary>
    /// 按当前模式与格式重建歌词显示文本。
    /// </summary>
    private void UpdateLyricsText()
    {
        if (TxtLyrics == null)
            return;

        if (_currentSong == null)
        {
            TxtLyrics.Text = string.Empty;
            return;
        }

        TxtLyrics.Text = OnlineLyricsFormatter.FormatLyrics(_currentSong, GetDisplayMode(), GetCombineMode());
    }

    /// <summary>
    /// 根据歌词模式启用或禁用双语格式下拉框。
    /// </summary>
    private void UpdateFormatAvailability()
    {
        if (LrcAnalysis == null)
            return;

        var enable = GetDisplayMode() == LyricsDisplayMode.OriginalAndTranslation;
        LrcAnalysis.IsEnabled = enable;
        if (!enable)
            LrcAnalysis.SelectedIndex = 0;
    }

    /// <summary>读取当前歌词显示模式。</summary>
    private LyricsDisplayMode GetDisplayMode()
    {
        return (LyricsDisplayMode)(CmbDisplayMode?.SelectedIndex ?? 0);
    }

    /// <summary>读取当前双语歌词格式。</summary>
    private LyricsCombineMode GetCombineMode()
    {
        return GetDisplayMode() == LyricsDisplayMode.OriginalAndTranslation
            ? (LyricsCombineMode)(LrcAnalysis?.SelectedIndex ?? 0)
            : LyricsCombineMode.Mixed;
    }

    /// <summary>读取当前脚本时间码格式。</summary>
    private LyricsTimeCodeFormat GetTimeCodeFormat()
    {
        return (LyricsTimeCodeFormat)(CmbTimeCodeFormat?.SelectedIndex ?? 0);
    }

    /// <summary>
    /// 在歌曲加载过程中更新界面提示。
    /// </summary>
    private void SetLoadingState(bool isLoading, string? message = null)
    {
        if (TxtLyrics != null && isLoading)
            TxtLyrics.Text = message ?? LocalizationService.T("正在加载歌词..."); // 正在加载歌词...

        if (TxtSongName != null && isLoading)
            TxtSongName.Text = LocalizationService.T("正在加载..."); // 正在加载...
    }

    /// <summary>
    /// 显示通用消息弹窗。
    /// </summary>
    private async Task ShowMessageDialogAsync(string message, bool isSuccess)
    {
        var dialog = new SukiWindow
        {
            Title = isSuccess ? LocalizationService.T("提示") : LocalizationService.T("错误"), // 提示或错误
            Width = 380,
            Height = 190,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = message,
                        FontSize = 13,
                        Foreground = isSuccess ? Brushes.Black : Brushes.Red,
                        TextWrapping = TextWrapping.Wrap
                    },
                    new Button
                    {
                        Content = LocalizationService.T("确定"), // 确定
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Classes = { isSuccess ? "Outlined" : "Standard" }
                    }
                }
            }
        };

        if (dialog.Content is StackPanel panel && panel.Children[1] is Button button)
            button.Click += (_, _) => dialog.Close();

        LocalizationService.Apply(dialog);
        await dialog.ShowDialog(this);
    }

    private void ApplyLocalizedState()
    {
        UpdateFormatAvailability();
        if (_currentSong != null)
            ApplySongInfo(_currentSong);
        else
            UpdateLyricsText();
    }

    /// <summary>
    /// 空值显示助手，统一回退为短横线。
    /// </summary>
    private static string DisplayOrDash(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value;
    }

    /// <summary>
    /// 移除文件名中的非法字符，供保存歌词时使用。
    /// </summary>
    private static string SanitizeFileName(string text)
    {
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            text = text.Replace(invalidChar, '_');
        }

        return string.IsNullOrWhiteSpace(text) ? "lyrics" : text.Trim();
    }

    /// <summary>
    /// 读取当前下拉框项的文本内容。
    /// </summary>
    private static string GetSelectedComboValue(ComboBox? comboBox)
    {
        if (comboBox?.SelectedItem is ComboBoxItem item)
            return item.Tag?.ToString() ?? item.Content?.ToString() ?? string.Empty;

        return string.Empty;
    }

    /// <summary>
    /// 根据 Tag 值选中对应的下拉项。
    /// </summary>
    private static void SelectComboBoxItemByTag(ComboBox? comboBox, string? tagValue)
    {
        if (comboBox?.Items is not IEnumerable items || string.IsNullOrWhiteSpace(tagValue))
            return;

        var index = 0;
        foreach (var item in items)
        {
            if (item is ComboBoxItem comboBoxItem &&
                string.Equals(comboBoxItem.Tag?.ToString(), tagValue, StringComparison.Ordinal))
            {
                comboBox.SelectedIndex = index;
                return;
            }

            index++;
        }
    }

    /// <summary>
    /// 构造设计预览用的空白歌曲对象。
    /// </summary>
    private static SearchSongItem CreatePreviewSong()
    {
        var song = new SearchSongItem
        {
            SongId = 0, // 网易云歌曲 ID，预览用
            SourceId = "netease", // 来源标识
            SongSource = "", // 来源
            SongName = "", // 歌曲名称
            Singer = "", // 歌手
            Album = "", // 专辑
            SongDuration = "", // 歌曲时长
            SongDurationSeconds = 0, // 歌曲时长（秒）
            PublishDate = "", // 发行日期
            SongLink = "" // 歌曲链接 https://music.163.com/#/song?id=1847207199
        };

        song.SetLyrics(
            // 原文
            """

            """,
            // 翻译
            """
 
            """
        );

        return song;
    }
}
