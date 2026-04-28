using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MMTGui.Localization;
using SukiUI.Controls;

namespace MMTGui;

/// <summary>
/// 批量导出窗口代码后置。
/// 负责收集多个脚本文件，并调用主窗口执行逐项导出。
/// </summary>
public partial class BatchExport : SukiWindow
{
    /// <summary>关联的主窗口实例。</summary>
    private readonly MainWindow? _ownerWindow;

    /// <summary>当前批量导出列表的数据源。</summary>
    private readonly ObservableCollection<BatchExportItem> _items = new();

    /// <summary>是否正在执行批量导出。</summary>
    private bool _isExporting;

    /// <summary>
    /// 创建设计预览用的窗口实例。
    /// </summary>
    public BatchExport()
    {
        InitializeComponent();
        FilesGrid.ItemsSource = _items;
        SeedPreviewItems();
        UpdateSummary();
        LocalizationService.RegisterWindow(this, UpdateSummary);
    }

    /// <summary>
    /// 创建绑定主窗口的正式批量导出窗口。
    /// </summary>
    public BatchExport(MainWindow ownerWindow)
        : this()
    {
        _ownerWindow = ownerWindow;
        _items.Clear();
        UpdateSummary();
    }

    /// <summary>
    /// 向列表中填充少量示例数据，便于设计阶段预览。
    /// </summary>
    private void SeedPreviewItems()
    {
        _items.Add(new BatchExportItem
        {
            FilePath = "ep01_op.mmt",
            FileName = "ep01_op.mmt",
            SubtitleCountText = LocalizationService.F("{0}条字幕", 12),  // 条字幕
            Status = LocalizationService.T("待处理")  // 待处理
        });
        _items.Add(new BatchExportItem
        {
            FilePath = "ep01_ed.mmt",
            FileName = "ep01_ed.mmt",
            SubtitleCountText = LocalizationService.F("{0}条字幕", 8),  // 条字幕
            Status = LocalizationService.T("待处理")  // 待处理
        });
    }

    /// <summary>
    /// 处理拖放完成事件，将拖入的文件加入列表。
    /// </summary>
    private async void OnDropZoneDrop(object? sender, DragEventArgs e)
    {
        var filePaths = e.Data.GetFiles()?
            .Select(file => file.Path.LocalPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();

        if (filePaths == null || filePaths.Length == 0)
            return;

        await AddFilesAsync(filePaths);
    }

    /// <summary>
    /// 声明当前拖放区域支持复制效果。
    /// </summary>
    private void OnDropZoneDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    /// <summary>
    /// 批量读取文件并统计字幕数量。
    /// </summary>
    private async Task AddFilesAsync(string[] filePaths)
    {
        var existing = _items.Select(item => item.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var filePath in filePaths
                     .Where(path => path.EndsWith(".mmt", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (existing.Contains(filePath))
                continue;

            var item = new BatchExportItem
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                SubtitleCountText = LocalizationService.T("读取中..."),  // 读取中...
                Status = LocalizationService.T("待处理")  // 待处理
            };
            _items.Add(item);
            existing.Add(filePath);

            try
            {
                var script = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
                var subtitles = MmtScriptParser.Parse(script);
                item.SubtitleCountText = LocalizationService.F("{0}条字幕", subtitles.Count);
                item.Status = LocalizationService.T("待处理");
            }
            catch
            {
                item.SubtitleCountText = "-";
                item.Status = LocalizationService.T("读取失败");  // 读取失败
            }
        }

        UpdateSummary();
    }

    /// <summary>
    /// 刷新文件数量与状态提示。
    /// </summary>
    private void UpdateSummary()
    {
        if (TxtFileCount != null)
            TxtFileCount.Text = LocalizationService.F("已添加 {0} 个文件", _items.Count);  // 已添加 {0} 个文件

        if (TxtStatus != null && !_isExporting)
            TxtStatus.Text = _items.Count == 0
                ? LocalizationService.T("等待添加文件") // 等待添加文件
                : LocalizationService.T("准备就绪");     // 准备就绪
    }

    /// <summary>
    /// 浏览并选择批量导出的输出目录。
    /// </summary>
    private async void OnBrowseOutputDirectory(object? sender, RoutedEventArgs e)
    {
        var storageProvider = GetTopLevel(this)?.StorageProvider;
        if (storageProvider == null)
            return;

        var folder = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = LocalizationService.T("选择批量导出目录"),  // 选择批量导出目录
            AllowMultiple = false
        });

        if (folder.Count > 0 && TxtOutputDirectory != null)
            TxtOutputDirectory.Text = folder[0].Path.LocalPath;
    }

    /// <summary>
    /// 启动批量导出流程。
    /// </summary>
    private async void OnStartBatchExport(object? sender, RoutedEventArgs e)
    {
        if (_ownerWindow == null)
        {
            if (TxtStatus != null)
                TxtStatus.Text = LocalizationService.T("设计预览模式下不可执行导出"); // 设计预览模式下不可执行导出
            return;
        }

        if (_isExporting || _items.Count == 0)
            return;

        var outputDir = TxtOutputDirectory?.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(outputDir))
        {
            if (TxtStatus != null)
                TxtStatus.Text = LocalizationService.T("请先选择输出目录"); // 请先选择输出目录
            return;
        }

        Directory.CreateDirectory(outputDir);

        _isExporting = true;
        if (BtnStartBatchExport != null)
            BtnStartBatchExport.IsEnabled = false;

        try
        {
            foreach (var item in _items)
            {
                if (item.Status == LocalizationService.T("读取失败")) // 读取失败
                    continue;

                item.Status = LocalizationService.T("处理中"); // 处理中
                try
                {
                    await _ownerWindow.ExportScriptFileAsync(item.FilePath, outputDir);
                    item.Status = LocalizationService.T("已完成"); // 已完成
                }
                catch (Exception ex)
                {
                    item.Status = LocalizationService.F("失败: {0}", ex.Message); // 失败: {0}
                }
            }

            if (TxtStatus != null)
                TxtStatus.Text = LocalizationService.T("批量导出完成"); // 批量导出完成
        }
        finally
        {
            _isExporting = false;
            if (BtnStartBatchExport != null)
                BtnStartBatchExport.IsEnabled = true;
        }
    }

    /// <summary>
    /// 关闭批量导出窗口。
    /// </summary>
    private void OnClose(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}

/// <summary>
/// 批量导出列表中的单个文件项。
/// 通过属性通知与 DataGrid 绑定联动。
/// </summary>
public sealed class BatchExportItem : INotifyPropertyChanged
{
    private string _filePath = string.Empty;
    private string _fileName = string.Empty;
    private string _subtitleCountText = string.Empty;
    private string _status = string.Empty;

    /// <summary>文件完整路径。</summary>
    public string FilePath
    {
        get => _filePath;
        set => SetField(ref _filePath, value);
    }

    /// <summary>文件名。</summary>
    public string FileName
    {
        get => _fileName;
        set
        {
            if (SetField(ref _fileName, value))
                OnPropertyChanged(nameof(FileDisplayName));
        }
    }

    /// <summary>供界面显示的文件名文本。</summary>
    public string FileDisplayName => $"✅ {FileName}";  // 已加入文件

    /// <summary>字幕数量显示文本。</summary>
    public string SubtitleCountText
    {
        get => _subtitleCountText;
        set => SetField(ref _subtitleCountText, value);
    }

    /// <summary>当前处理状态文本。</summary>
    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    /// <summary>属性变更事件。</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 通用字段赋值辅助方法。
    /// </summary>
    private bool SetField(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (string.Equals(field, value, StringComparison.Ordinal))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>
    /// 手动触发属性变更通知。
    /// </summary>
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
