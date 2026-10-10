using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
/// 导入LRC歌词文件的窗口。
/// 负责加载歌曲文件，或者手动输入歌词文本插入主脚本。
/// 支持 LRC、SRT、ASS 三种歌词/字幕格式。
///
/// 支持双语场景：同一时间戳下的多行文本，第一行作原文、其余作译文，
/// 再按「模式」（原文+译文 / 独立）与「格式」（混合 / 合并 / 独立）组合，
/// 语义与在线歌词功能保持一致。
/// </summary>
public partial class OpenLRCFile : SukiWindow
{
    /// <summary>关联的主窗口实例。</summary>
    private readonly MainWindow? _ownerWindow;

    public OpenLRCFile()
    {
        InitializeComponent();
        LocalizationService.RegisterWindow(this, ApplyLocalizedState);
    }

    public OpenLRCFile(MainWindow ownerWindow)
        : this()
    {
        _ownerWindow = ownerWindow;
    }

    private async void OpenLRCFileButton(object? sender, RoutedEventArgs e)
    {
        var storage = GetTopLevel(this)?.StorageProvider;
        if (storage == null)
        {
            await ShowMessageDialogAsync(LocalizationService.T("当前环境无法打开文件对话框。"), false);
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationService.T("选择歌词文件"),
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(LocalizationService.T("歌词文件"))
                {
                    Patterns = new[] { "*.lrc", "*.srt", "*.ass" }
                },
                new FilePickerFileType(LocalizationService.T("所有文件"))
                {
                    Patterns = new[] { "*.*" }
                }
            }
        });

        if (files.Count == 0)
            return;

        await LoadFileAsync(files[0].Path.LocalPath);
    }

    /// 拖动文件事件处理函数。
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// 拖动文件事件处理函数。
    private async void OnFileDrop(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(DataFormats.Files))
            return;

        var files = e.Data.GetFiles();
        if (files == null || !files.Any())
            return;

        var firstFile = files.First();
        var filePath = firstFile is IStorageItem storageItem
            ? storageItem.Path.LocalPath
            : firstFile.ToString();
        await LoadFileAsync(filePath);
        e.Handled = true;
    }

    /// 加载文件事件处理函数。
    private async Task LoadFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            await ShowMessageDialogAsync(LocalizationService.T("文件不存在。"), false);
            return;
        }

        try
        {
            if (LRCFileTextTip != null)
                LRCFileTextTip.Text = filePath;

            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            var content = await ReadFileWithAutoEncodingAsync(filePath);

            var lyricsText = ext switch
            {
                ".lrc" => ConvertLrcToPlainText(content),
                ".srt" => ConvertSrtToLrc(content),
                ".ass" => ConvertAssToLrc(content),
                _ => ConvertLrcToPlainText(content)
            };

            if (LRCInputs != null)
                LRCInputs.Text = lyricsText;

            SetStatus(LocalizationService.T("文件已加载。"));
        }
        catch (Exception ex)
        {
            await ShowMessageDialogAsync(LocalizationService.F("加载文件失败: {0}", ex.Message), false);
        }
    }

    /// 插入歌词文本脚本事件处理函数。
    private async void INLRCTextScript(object? sender, RoutedEventArgs e)
    {
        if (_ownerWindow == null)
        {
            await ShowMessageDialogAsync(LocalizationService.T("当前窗口没有关联主脚本编辑器，暂时无法插入脚本。"), false);
            return;
        }

        var rawText = LRCInputs?.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rawText))
        {
            await ShowMessageDialogAsync(LocalizationService.T("请先输入或加载歌词内容。"), false);
            return;
        }

        var lrcLines = OnlineLyricsFormatter.ParseLrc(rawText);
        if (lrcLines.Count == 0 || lrcLines.All(l => !l.IsTimed))
        {
            await ShowMessageDialogAsync(LocalizationService.T("未检测到有效的时间标签，请检查歌词格式。"), false);
            return;
        }

        // 按时间戳分组：同一时间戳的第一行=原文，其余=译文。
        var groups = LocalLyricsSupport.GroupByTimestamp(lrcLines);
        if (groups.Count == 0)
        {
            await ShowMessageDialogAsync(LocalizationService.T("当前歌词中没有可插入脚本的时间标签。"), false);
            return;
        }

        var blocks = LocalLyricsSupport.BuildBlocks(groups, GetDisplayMode(), GetCombineMode());
        if (blocks.Count == 0)
        {
            await ShowMessageDialogAsync(LocalizationService.T("当前歌词中没有可插入脚本的时间标签。"), false);
            return;
        }

        var format = (LyricsTimeCodeFormat)(CmbTimeCodeFormat?.SelectedIndex ?? 0);
        var scriptText = OnlineLyricsFormatter.FormatScriptBlocks(blocks, format);
        _ownerWindow.InsertLyricsScriptBlock(scriptText);

        await ShowMessageDialogAsync(LocalizationService.T("歌词已插入到脚本的 :end 之前。"), true);
    }

    /// <summary>模式切换时，根据是否需要双语来决定「格式」是否可用。</summary>
    private void OnDisplayModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateFormatAvailability();
    }

    /// <summary>格式切换事件（当前无需额外处理，保留以便后续扩展）。</summary>
    private void OnLyricsFormatChanged(object? sender, SelectionChangedEventArgs e)
    {
    }

    /// <summary>
    /// 「格式」仅在「原文+译文」模式下有意义；「独立」模式下应禁用。
    /// </summary>
    private void UpdateFormatAvailability()
    {
        if (LrcAnalysis == null)
            return;

        var enable = GetDisplayMode() == LocalLyricsMode.OriginalAndTranslation;
        LrcAnalysis.IsEnabled = enable;

        if (!enable)
            LrcAnalysis.SelectedIndex = 0;
    }

    /// <summary>读取当前模式。</summary>
    private LocalLyricsMode GetDisplayMode()
    {
        return (LocalLyricsMode)(CmbDisplayMode?.SelectedIndex ?? 0);
    }

    /// <summary>读取当前格式；「独立」模式下格式不生效，统一按「混合」处理。</summary>
    private LyricsCombineMode GetCombineMode()
    {
        return GetDisplayMode() == LocalLyricsMode.OriginalAndTranslation
            ? (LyricsCombineMode)(LrcAnalysis?.SelectedIndex ?? 0)
            : LyricsCombineMode.Mixed;
    }

    private const string MetaTagPattern = @"^\[[a-zA-Z]+:";
    private const string SrtTimePattern = @"(\d{1,2}):(\d{2}):(\d{2})[,.](\d{1,3})\s*-->\s*(\d{1,2}):(\d{2}):(\d{2})[,.](\d{1,3})";
    private const string AssDialoguePattern = @"^Dialogue:\s*\d+,([^,]+),([^,]+),([^,]+)";
    private const string AssOverridePattern = @"\{[^}]*\}";

    private static string ConvertLrcToPlainText(string content)
    {
        var lines = NormalizeLines(content);
        var output = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                output.Add(string.Empty);
                continue;
            }

            if (Regex.IsMatch(trimmed, MetaTagPattern))
                continue;

            output.Add(trimmed);
        }

        return string.Join("\n", output).Trim();
    }

    private static string ConvertSrtToLrc(string content)
    {
        var lines = NormalizeLines(content);
        var output = new List<string>();
        var timeRegex = new Regex(SrtTimePattern);

        string? currentTimeTag = null;
        var textBuffer = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (int.TryParse(trimmed, out _))
                continue;

            var match = timeRegex.Match(trimmed);
            if (match.Success)
            {
                if (currentTimeTag != null && textBuffer.Count > 0)
                {
                    output.AddRange(BuildTimedLines(currentTimeTag, textBuffer));
                    textBuffer.Clear();
                }

                var h = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                var m = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                var s = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
                var ms = int.Parse(match.Groups[4].Value.PadRight(3, '0'), CultureInfo.InvariantCulture);

                var totalSeconds = h * 3600 + m * 60 + s + ms / 1000.0;
                currentTimeTag = FormatLrcTimeTag(totalSeconds);
                continue;
            }

            if (string.IsNullOrEmpty(trimmed))
            {
                if (currentTimeTag != null && textBuffer.Count > 0)
                {
                    output.AddRange(BuildTimedLines(currentTimeTag, textBuffer));
                    textBuffer.Clear();
                }
                currentTimeTag = null;
                continue;
            }

            if (currentTimeTag != null)
                textBuffer.Add(trimmed);
        }

        if (currentTimeTag != null && textBuffer.Count > 0)
            output.AddRange(BuildTimedLines(currentTimeTag, textBuffer));

        return string.Join("\n", output).Trim();
    }

    /// <summary>
    /// 把同一时间轴的多个文本行展开为多条带相同时间标签的 LRC 行。
    ///
    /// 这是 LRC 表达「同一时刻多行」的标准写法，也让
    /// <see cref="OnlineLyricsFormatter.ParseLrc"/> 能把它们解析成
    /// 时间戳相同、可被 <see cref="LocalLyricsSupport.GroupByTimestamp"/>
    /// 正确归并到一组的多个 <see cref="LrcLine"/>。
    ///
    /// 注意：不能写成 <c>[tag]原文\n译文</c> —— ParseLrc 是先按换行切分的，
    /// 无时间标签的「译文」会退化成未经计时的行，从而丢失配对关系。
    /// </summary>
    private static IEnumerable<string> BuildTimedLines(string timeTag, IEnumerable<string> textLines)
    {
        foreach (var textLine in textLines)
        {
            if (!string.IsNullOrWhiteSpace(textLine))
                yield return $"[{timeTag}]{textLine.Trim()}";
        }
    }

    /// 将ASS文件转换为LRC文件。
    private static string ConvertAssToLrc(string content)
    {
        var lines = NormalizeLines(content);
        var output = new List<string>();
        var dialogueRegex = new Regex(AssDialoguePattern);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("Dialogue:", StringComparison.Ordinal))
                continue;

            var match = dialogueRegex.Match(trimmed);
            if (!match.Success)
                continue;

            var startStr = match.Groups[1].Value.Trim();
            if (!TryParseAssTime(startStr, out var startSeconds))
                continue;

            var ninthCommaIndex = IndexOfNth(trimmed, ',', 9);
            if (ninthCommaIndex < 0 || ninthCommaIndex + 1 >= trimmed.Length)
                continue;

            var textPart = trimmed.Substring(ninthCommaIndex + 1);
            textPart = Regex.Replace(textPart, AssOverridePattern, string.Empty);
            textPart = textPart.Replace("\\N", "\n").Replace("\\n", "\n").Replace("\\h", " ");
            textPart = textPart.Trim();

            if (string.IsNullOrEmpty(textPart))
                continue;

            // ASS 用 \N 表示硬换行，双语字幕常写成「原文\N译文」。
            // 这里展开为多条同时间标签的 LRC 行，保留原文/译文的配对关系。
            var timeTag = FormatLrcTimeTag(startSeconds);
            output.AddRange(BuildTimedLines(timeTag, textPart.Split('\n')));
        }

        return string.Join("\n", output).Trim();
    }

    /// 尝试解析ASS时间格式。
    private static bool TryParseAssTime(string text, out double seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var parts = text.Split(':');
        if (parts.Length != 3)
            return false;

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var h) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var m) ||
            !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
        {
            return false;
        }

        seconds = h * 3600 + m * 60 + s;
        return true;
    }

    /// 格式化LRC时间标签。
    private static string FormatLrcTimeTag(double seconds)
    {
        var safe = Math.Max(0, seconds);
        var timeSpan = TimeSpan.FromSeconds(safe);
        var totalMinutes = (int)timeSpan.TotalMinutes;
        return $"{totalMinutes:D2}:{timeSpan.Seconds:D2}.{timeSpan.Milliseconds / 10:D2}";
    }

    /// 查找字符串中第n个指定字符的索引。
    private static int IndexOfNth(string text, char value, int nth)
    {
        int count = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == value)
            {
                count++;
                if (count == nth)
                    return i;
            }
        }
        return -1;
    }

    /// 归一化字符串中的换行符。
    private static List<string> NormalizeLines(string content)
    {
        if (string.IsNullOrEmpty(content))
            return new List<string>();

        return content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
    }

    /// 以自动编码读取文件内容。
    private static async Task<string> ReadFileWithAutoEncodingAsync(string path)
    {
        var bytes = await File.ReadAllBytesAsync(path);

        var encodings = new[]
        {
            Encoding.UTF8,
            Encoding.GetEncoding("shift_jis"),
            Encoding.GetEncoding("GBK"),
            Encoding.Default
        };

        foreach (var encoding in encodings)
        {
            try
            {
                return encoding.GetString(bytes);
            }
            catch { }
        }

        return Encoding.Default.GetString(bytes);
    }

    /// 设置窗口标题状态信息。
    private void SetStatus(string message)
    {
        Title = LocalizationService.F("导入LRC文件 - {0}", message);
    }

    /// <summary>
    /// 语言切换后重新应用本地化状态。
    /// </summary>
    private void ApplyLocalizedState()
    {
        // 静态控件文本由 LocalizationService.Apply 自动处理
        // 这里可补充动态内容的本地化刷新
    }

    /// 显示消息对话框。
    private async Task ShowMessageDialogAsync(string message, bool isSuccess)
    {
        var dialog = new SukiWindow
        {
            Title = isSuccess ? LocalizationService.T("提示") : LocalizationService.T("错误"),
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
                        Content = LocalizationService.T("确定"),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Classes = { isSuccess ? "Outlined" : "Standard" }
                    }
                }
            }
        };

        if (dialog.Content is StackPanel panel && panel.Children[1] is Button button)
            button.Click += (_, _) => dialog.Close();

        await dialog.ShowDialog(this);
    }
}