using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace MMTGui;

/// <summary>
/// MainWindow 视图、工具与帮助操作分部类。
/// 这一部分只负责菜单命令和工具窗口跳转，
/// 让主窗口核心渲染逻辑与菜单响应逻辑解耦。
/// </summary>
public partial class MainWindow
{
    #region 视图操作

    /// <summary>
    /// 切换左侧属性面板的显示状态。
    /// </summary>
    private void OnTogglePropertyPanel(object? sender, RoutedEventArgs e)
    {
        if (MainGrid == null) return;
        var currentWidth = MainGrid.ColumnDefinitions[0].Width;
        var isVisible = currentWidth.Value > 0;
        MainGrid.ColumnDefinitions[0].Width = isVisible ? new GridLength(0) : new GridLength(280);

        if (MenuToggleProperty != null)
            MenuToggleProperty.IsChecked = !isVisible;
    }

    /// <summary>
    /// 切换右侧脚本编辑器面板的显示状态。
    /// </summary>
    private void OnToggleScriptPanel(object? sender, RoutedEventArgs e)
    {
        if (MainGrid == null) return;
        var currentWidth = MainGrid.ColumnDefinitions[4].Width;
        var isVisible = currentWidth.Value > 0;
        MainGrid.ColumnDefinitions[4].Width = isVisible ? new GridLength(0) : new GridLength(320);

        if (MenuToggleScript != null)
            MenuToggleScript.IsChecked = !isVisible;
    }

    /// <summary>
    /// 切换字幕列表区域的可见性。
    /// </summary>
    private void OnToggleSubtitleList(object? sender, RoutedEventArgs e)
    {
        if (SubtitleListBorder == null || SubtitleListHeader == null) return;

        var isVisible = SubtitleListBorder.IsVisible;
        SubtitleListBorder.IsVisible = !isVisible;
        SubtitleListHeader.IsVisible = !isVisible;

        if (MenuToggleSubtitleList != null)
            MenuToggleSubtitleList.IsChecked = !isVisible;
    }

    /// <summary>
    /// 在普通窗口和全屏窗口之间切换。
    /// </summary>
    private void OnToggleFullScreen(object? sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.FullScreen)
            WindowState = WindowState.Normal;
        else
            WindowState = WindowState.FullScreen;
    }

    #endregion

    #region 预设操作

    //private void OnPresetChanged(object? sender, SelectionChangedEventArgs e)
    //{
    //    if (CmbPreset == null) return;
    //
    //    if (CmbPreset.SelectedIndex == 2)
    //    {
    //        SetStatus("新建预设...");
    //    }
    //
    //    UpdateStatusBar();
    //}

    //private void OnSavePreset(object? sender, RoutedEventArgs e)
    //{
    //    SetStatus("预设已保存");
    //}

    //private void OnManagePresets(object? sender, RoutedEventArgs e)
    //{
    //    SetStatus("管理预设...");
    //}

    //private void OnImportPreset(object? sender, RoutedEventArgs e)
    //{
    //    SetStatus("导入预设...");
    //}

    //private void OnExportPreset(object? sender, RoutedEventArgs e)
    //{
    //    SetStatus("导出预设...");
    //}

    #endregion

    #region 工具操作

    /// <summary>
    /// 打开时间换算工具窗口。
    /// </summary>
    private async void OnOpenTimeConversion(object? sender, RoutedEventArgs e)
    {
        var dialog = new TimeConversion();
        await dialog.ShowDialog(this);
    }

    /// <summary>
    /// 打开批量导出工具窗口。
    /// </summary>
    private async void OnBatchExport(object? sender, RoutedEventArgs e)
    {
        var dialog = new BatchExport(this);
        await dialog.ShowDialog(this);
    }

    /// <summary>
    /// 打开在线歌词搜索窗口。
    /// </summary>
    private async void OnOpenLyricsOnline(object? sender, RoutedEventArgs e)
    {
        var dialog = new LyricsOnline(this);
        await dialog.ShowDialog(this);
    }

    /// <summary>
    /// 将外部生成好的歌词脚本块插入到当前脚本的 <c>:end</c> 之前。
    /// </summary>
    public void InsertLyricsScriptBlock(string blockText)
    {
        if (ScriptEditor == null || string.IsNullOrWhiteSpace(blockText))
            return;

        EnsureEndCode();
        ScriptEditor.InsertLineBeforeEnd(blockText.TrimEnd());
        ScriptEditor.FocusEditor();
    }

    /// <summary>
    /// 打开预览画布选项窗口，并在确认后刷新当前预览画布。
    /// </summary>
    private async void OnOpenOptions(object? sender, RoutedEventArgs e)
    {
        var optionWindow = new Option(_previewCanvasColor, _previewBackgroundImagePath);
        await optionWindow.ShowDialog(this);

        if (!optionWindow.IsConfirmed)
            return;

        _previewCanvasColor = optionWindow.SelectedCanvasColor;
        _previewBackgroundImagePath = optionWindow.SelectedBackgroundImagePath;

        ApplyPreviewCanvasAppearance();

        var selectedSub = SubtitleList?.SelectedIndex >= 0 && SubtitleList.SelectedIndex < _subtitles.Count
            ? _subtitles[SubtitleList.SelectedIndex]
            : null;

        if (selectedSub != null)
            RenderPreviewFromSubtitle(selectedSub);
        else
            UpdatePreviewFrame((double)(NudScreenWidth?.Value ?? 1280), (double)(NudScreenHeight?.Value ?? 720));

        SetStatus("预览画布选项已更新");  // 预览画布选项已更新
    }

    #endregion

    #region 帮助操作

    /// <summary>
    /// 处理“打开文档”菜单命令。
    /// 当前版本先给出状态提示，后续可继续接入在线文档地址。
    /// </summary>
    private void OnOpenDocs(object? sender, RoutedEventArgs e)
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HelpDoc", "HelpDoc_EN.html");

        if (!File.Exists(path))
        {
            // 文件不存在的提示
            return;
        }

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", path);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", path);
            }
        }
        catch (Exception ex)
        {
            // 处理异常
        }
    }

    /// <summary>
    /// 打开关于窗口。
    /// </summary>
    private async void OnAbout(object? sender, RoutedEventArgs e)
    {
        var dialog = new About();
        await dialog.ShowDialog(this);
    }

    #endregion
}
