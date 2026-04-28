using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MMTGui.Localization;
using SukiUI.Controls;

namespace MMTGui;

/// <summary>
/// 歌曲搜索窗口代码后置。
/// 用于展示搜索结果、预览歌词，并把选中的歌曲对象返回上一级窗口。
/// </summary>
public partial class SearchLink : SukiWindow
{
    /// <summary>当前搜索结果集合。</summary>
    private readonly ObservableCollection<SearchSongItem> _songs = new();
    /// <summary>当前搜索源文本。</summary>
    private readonly string _source;
    /// <summary>歌词预览请求版本号，用于丢弃过期的异步刷新。</summary>
    private int _previewRequestVersion;

    /// <summary>
    /// 默认构造函数，供设计时预览使用。
    /// </summary>
    public SearchLink()
        : this(null, "netease")
    {
    }

    /// <summary>
    /// 创建搜索窗口，并使用指定来源与初始查询词初始化界面。
    /// </summary>
    public SearchLink(string? initialQuery, string source)
    {
        _source = source;

        InitializeComponent();

        if (SearchResultsGrid != null)
            SearchResultsGrid.ItemsSource = _songs;

        if (TxtSearchInput != null && !string.IsNullOrWhiteSpace(initialQuery))
            TxtSearchInput.Text = initialQuery;

        Opened += async (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(initialQuery))
                await ExecuteSearchAsync(initialQuery);
        };

        LocalizationService.RegisterWindow(this, ApplyLocalizedState);
    }

    /// <summary>
    /// 响应搜索按钮点击。
    /// </summary>
    private async void OnSearch(object? sender, RoutedEventArgs e)
    {
        await ExecuteSearchAsync(TxtSearchInput?.Text);
    }

    /// <summary>
    /// 切换选中歌曲时加载并刷新歌词预览。
    /// </summary>
    private async void OnSearchResultSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var selected = SelectedSong;
        if (selected == null)
        {
            UpdatePreviewText(LocalizationService.T("请选择一首歌曲查看歌词预览。")); // 请选择一首歌曲查看歌词预览。
            UpdateConfirmButtonState();
            return;
        }

        var requestVersion = ++_previewRequestVersion;
        UpdatePreviewText(LocalizationService.T("正在加载歌词预览...")); // 正在加载歌词预览...

        try
        {
            if (string.Equals(_source, "netease", StringComparison.Ordinal))
                await NetEaseLyricsApi.EnsureLyricsLoadedAsync(selected);

            if (requestVersion == _previewRequestVersion)
                UpdatePreviewText(selected.GetPreviewLyrics());
        }
        catch (Exception ex)
        {
            if (requestVersion == _previewRequestVersion)
                UpdatePreviewText(LocalizationService.F("歌词预览加载失败: {0}", ex.Message)); // 歌词预览加载失败: {0}
        }
        finally
        {
            UpdateConfirmButtonState();
        }
    }

    /// <summary>
    /// 确认当前选择并关闭窗口。
    /// </summary>
    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        if (SelectedSong != null)
            Close(SelectedSong);
    }

    /// <summary>
    /// 取消并关闭窗口。
    /// </summary>
    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>当前 DataGrid 选中的歌曲对象。</summary>
    private SearchSongItem? SelectedSong => SearchResultsGrid?.SelectedItem as SearchSongItem;

    /// <summary>
    /// 执行远程搜索并刷新结果列表。
    /// </summary>
    private async Task ExecuteSearchAsync(string? query)
    {
        var keyword = query?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(keyword))
        {
            _songs.Clear();
            UpdatePreviewText(LocalizationService.T("请输入歌曲链接、歌曲 ID 或关键字。")); // 请输入歌曲链接、歌曲 ID 或关键字。
            UpdateConfirmButtonState();
            return;
        }

        if (!string.Equals(_source, "netease", StringComparison.Ordinal))
        {
            _songs.Clear();
            UpdatePreviewText(LocalizationService.T("QQ 音乐接口当前还没有接入。")); // QQ 音乐接口当前还没有接入。
            UpdateConfirmButtonState();
            return;
        }

        try
        {
            UpdatePreviewText(LocalizationService.T("正在搜索网易云歌曲...")); // 正在搜索网易云歌曲...

            var results = NetEaseLyricsApi.TryExtractSongId(keyword, out var songId)
                ? new ObservableCollection<SearchSongItem> { await NetEaseLyricsApi.GetSongAsync(songId) }
                : new ObservableCollection<SearchSongItem>(await NetEaseLyricsApi.SearchSongsAsync(keyword));

            _songs.Clear();
            foreach (var song in results)
            {
                ApplyLocalizedSongMetadata(song);
                _songs.Add(song);
            }

            if (_songs.Count == 0)
            {
                UpdatePreviewText(LocalizationService.T("没有找到匹配的歌曲。")); // 没有找到匹配的歌曲。
                UpdateConfirmButtonState();
                return;
            }

            if (SearchResultsGrid != null)
                SearchResultsGrid.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            _songs.Clear();
            UpdatePreviewText(LocalizationService.F("搜索失败: {0}", ex.Message)); // 搜索失败: {0}
            UpdateConfirmButtonState();
        }
    }

    /// <summary>
    /// 刷新歌词预览区域内容。
    /// </summary>
    private void UpdatePreviewText(string text)
    {
        if (TxtLyricsPreview != null)
            TxtLyricsPreview.Text = text;
    }

    /// <summary>
    /// 根据是否有选中项刷新确认按钮状态。
    /// </summary>
    private void UpdateConfirmButtonState()
    {
        if (BtnConfirm != null)
            BtnConfirm.IsEnabled = SelectedSong != null;
    }

    private void ApplyLocalizedState()
    {
        foreach (var song in _songs)
            ApplyLocalizedSongMetadata(song);

        if (SearchResultsGrid != null)
        {
            var selected = SearchResultsGrid.SelectedItem;
            SearchResultsGrid.ItemsSource = null;
            SearchResultsGrid.ItemsSource = _songs;
            SearchResultsGrid.SelectedItem = selected;
        }
    }

    private static void ApplyLocalizedSongMetadata(SearchSongItem song)
    {
        if (string.Equals(song.SourceId, "qq", StringComparison.Ordinal))
            song.SongSource = LocalizationService.T("QQ音乐");
        else if (string.Equals(song.SourceId, "netease", StringComparison.Ordinal))
            song.SongSource = LocalizationService.T("网易云");
    }
}
