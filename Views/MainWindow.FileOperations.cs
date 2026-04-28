using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MMTGui.Localization;

namespace MMTGui;

/// <summary>
/// MainWindow 文件操作分部类。
/// 负责新建、打开、保存、导出等与脚本文件生命周期相关的行为。
/// </summary>
public partial class MainWindow
{
    #region 文件操作

    /// <summary>
    /// 新建脚本文件。
    /// 若当前脚本已修改，会先提示用户是否保存。
    /// </summary>
    private async void OnNewFile(object? sender, RoutedEventArgs e)
    {
        if (_isModified)
        {
            var result = await ShowConfirmDialog(LocalizationService.T("当前文件已修改，是否保存？")); // 当前文件已修改，是否保存？
            if (result == true)
                await SaveFile();
            else if (result == null)
                return;
        }

        _currentFilePath = null;
        _isModified = false;
        _isUsingLocalizedExample = false;
        _loadedExampleLanguage = null;
        _subtitles.Clear();
        RefreshSubtitleList();

        if (ScriptEditor != null)
        {
            ScriptEditor.Text = ":end";
            ScriptEditor.MoveCaretToStart();
            ScriptEditor.FocusEditor();
        }

        UpdateStatusBar();
        SetStatus("新建文件");  // 新建文件
    }

    /// <summary>
    /// 打开本地脚本文件选择对话框。
    /// </summary>
    private async void OnOpenFile(object? sender, RoutedEventArgs e)
    {
        var storageProvider = GetTopLevel(this)?.StorageProvider;
        if (storageProvider == null) return;

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationService.T("打开 MMT 脚本文件"), // 打开 MMT 脚本文件
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(LocalizationService.T("脚本文件")) { Patterns = new[] { "*.txt", "*.mmt" } }, // 脚本文件
                new FilePickerFileType(LocalizationService.T("所有文件")) { Patterns = new[] { "*.*" } } // 所有文件
            }
        });

        if (files.Count > 0)
        {
            await LoadFile(files[0].Path.LocalPath);
        }
    }

    /// <summary>
    /// 读取脚本文件内容，并同步到脚本编辑器与状态栏。
    /// 支持多种编码自动检测：UTF-8、Shift-JIS、GBK、默认系统编码。
    /// </summary>
    private async Task LoadFile(string path)
    {
        try
        {
            var content = await ReadFileWithAutoEncoding(path);
            content = EnsureEndCode(content);
            _currentFilePath = path;
            _isModified = false;
            _isUsingLocalizedExample = false;
            _loadedExampleLanguage = null;

            if (ScriptEditor != null)
            {
                ScriptEditor.Text = content;
                ScriptEditor.MoveCaretToStart();
                ScriptEditor.FocusEditor();
            }

            SetStatus(LocalizationService.F("已打开: {0}", Path.GetFileName(path)));
            UpdateStatusBar();
        }
        catch (Exception ex)
        {
            await ShowErrorDialog(LocalizationService.F("无法打开文件: {0}", ex.Message));
        }
    }

    /// <summary>
    /// 使用多种编码尝试读取文件，自动检测最合适的编码。
    /// 优先级：UTF-8 > Shift-JIS > GBK > Default
    /// </summary>
    private static async Task<string> ReadFileWithAutoEncoding(string path)
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
                var content = encoding.GetString(bytes);
                if (IsValidTextContent(content))
                {
                    return content;
                }
            }
            catch
            {
                continue;
            }
        }

        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// 检查文本内容是否有效（无乱码）。
    /// 通过检查是否存在Unicode替换字符来判断。
    /// </summary>
    private static bool IsValidTextContent(string content)
    {
        if (string.IsNullOrEmpty(content))
            return true;

        var replacementCharCount = 0;
        var totalCharCount = content.Length;

        foreach (var c in content)
        {
            if (c == '\uFFFD')
                replacementCharCount++;
        }

        if (totalCharCount == 0)
            return true;

        var errorRate = (double)replacementCharCount / totalCharCount;
        return errorRate < 0.01;
    }

    /// <summary>
    /// 保存当前脚本文件。
    /// </summary>
    private async void OnSaveFile(object? sender, RoutedEventArgs e)
    {
        await SaveFile();
    }

    /// <summary>
    /// 另存为当前脚本文件。
    /// </summary>
    private async void OnSaveAsFile(object? sender, RoutedEventArgs e)
    {
        await SaveFileAs();
    }

    /// <summary>
    /// 保存脚本文本到当前文件路径。
    /// 若当前没有路径，则自动转入“另存为”流程。
    /// </summary>
    private async Task SaveFile()
    {
        if (_currentFilePath == null)
        {
            await SaveFileAs();
            return;
        }

        try
        {
            var content = ScriptEditor?.Text ?? string.Empty;
            content = EnsureEndCode(content);
            if (ScriptEditor != null)
                ScriptEditor.Text = content;
            await File.WriteAllTextAsync(_currentFilePath, content, new UTF8Encoding(false));
            _isModified = false;
            SetStatus(LocalizationService.F("已保存: {0}", Path.GetFileName(_currentFilePath))); // 已保存: {0}
            UpdateStatusBar();
        }
        catch (Exception ex)
        {
            await ShowErrorDialog(LocalizationService.F("无法保存文件: {0}", ex.Message)); // 无法保存文件: 错误信息    
        }
    }

    /// <summary>
    /// 打开“另存为”对话框并保存脚本。
    /// </summary>
    private async Task SaveFileAs()
    {
        var storageProvider = GetTopLevel(this)?.StorageProvider;
        if (storageProvider == null) return;

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LocalizationService.T("保存 MMT 脚本文件"), // 保存 MMT 脚本文件
            SuggestedFileName = _currentFilePath != null ? Path.GetFileName(_currentFilePath) : "untitled.mmt",
            FileTypeChoices = new[]
            {
                new FilePickerFileType(LocalizationService.T("MMT文件")) { Patterns = new[] { "*.mmt" } }, // MMT文件
                new FilePickerFileType(LocalizationService.T("TXT文件")) { Patterns = new[] { "*.txt" } } // TXT文件
            }
        });

        if (file != null)
        {
            _currentFilePath = file.Path.LocalPath;
            await SaveFile();
        }
    }

    /// <summary>
    /// 触发导出流程，生成贴图序列与 FX/X 文件。
    /// </summary>
    private async void OnExportFile(object? sender, RoutedEventArgs e)
    {
        var storageProvider = GetTopLevel(this)?.StorageProvider;
        if (storageProvider == null) return;

        var folder = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = LocalizationService.T("选择导出位置（将在此处创建子文件夹）"), // 选择导出位置（将在此处创建子文件夹）
            AllowMultiple = false
        });

        if (folder.Count == 0) return;

        try
        {
            var script = ScriptEditor?.Text ?? string.Empty;
            var exportDir = await ExportScriptPackageAsync(script, folder[0].Path.LocalPath, _currentFilePath);
            SetStatus(LocalizationService.F("已导出到: {0}", exportDir)); // 已导出到: 导出目录
        }
        catch (Exception ex)
        {
            await ShowErrorDialog(LocalizationService.F("导出失败: {0}", ex.Message)); // 导出失败: 错误信息
        }
    }

    /// <summary>
    /// 供批量导出窗口调用的单文件导出入口。
    /// </summary>
    internal async Task<string> ExportScriptFileAsync(string sourceFilePath, string parentDir)
    {
        var script = await File.ReadAllTextAsync(sourceFilePath, Encoding.UTF8);
        return await ExportScriptPackageAsync(script, parentDir, sourceFilePath);
    }

    /// <summary>
    /// 将脚本文本导出为完整的 MMT 输出包。
    /// </summary>
    private Task<string> ExportScriptPackageAsync(string script, string parentDir, string? sourceFilePath)
    {
        script = EnsureEndCode(script);

        var validationErrors = ValidateScript(script);
        if (validationErrors.Count > 0)
        {
            var errorMsg = string.Join("\n", validationErrors);
            throw new InvalidOperationException($"{LocalizationService.T("脚本存在以下错误：")}\n\n{errorMsg}"); // 脚本存在以下错误：
        }

        var parsed = MmtScriptParser.Parse(script);
        if (parsed.Count == 0)
            throw new InvalidOperationException("没有可导出的字幕数据");  // 没有可导出的字幕数据

        var exportDir = BuildExportDirectoryPath(parentDir, sourceFilePath);
        if (!Directory.Exists(exportDir))
            Directory.CreateDirectory(exportDir);

        ExportImageSequence(exportDir, parsed);
        ExportMikuMikuTelopFx(exportDir, parsed);
        ExportMikuMikuTelopX(exportDir);
        ExportPostTelopFx(exportDir);
        ExportPostTelopX(exportDir);

        return Task.FromResult(exportDir);
    }

    /// <summary>
    /// 根据源文件名与时间戳生成导出目录路径。
    /// </summary>
    private static string BuildExportDirectoryPath(string parentDir, string? sourceFilePath)
    {
        var dateStr = DateTime.Now.ToString("yyyyMMddHHmm");
        if (!string.IsNullOrWhiteSpace(sourceFilePath))
        {
            var fileName = Path.GetFileNameWithoutExtension(sourceFilePath);
            return Path.Combine(parentDir, $"{fileName}_{dateStr}");
        }

        return Path.Combine(parentDir, $"MMT_{dateStr}");
    }

    /// <summary>
    /// 对脚本文本做基本语法校验，返回错误信息列表。
    /// </summary>
    private List<string> ValidateScript(string script)
    {
        var errors = new List<string>();
        var lines = script.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

        bool hasEnd = false;
        int timeLineCount = 0;
        int textLineCount = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            var lineNum = i + 1;

            if (string.IsNullOrEmpty(line) || line.StartsWith("//") || line.StartsWith("'"))
                continue;

            if (line.StartsWith(":end"))
            {
                hasEnd = true;
                continue;
            }

            if (line.StartsWith("@@"))
            {
                textLineCount++;
                continue;
            }

            if (line.StartsWith("@"))
            {
                timeLineCount++;

                var timePart = line.Substring(1).Trim();
                if (string.IsNullOrWhiteSpace(timePart))
                {
                    errors.Add(LocalizationService.F("第 {0} 行: 时间格式为空", lineNum)); // 第 {0} 行: 时间格式为空
                    continue;
                }

                string? startPart = null;
                string? endPart = null;

                if (timePart.Contains("-->", StringComparison.Ordinal))
                {
                    var parts = timePart.Split(new[] { "-->" }, 2, StringSplitOptions.None);
                    startPart = parts[0].Trim();
                    endPart = parts.Length > 1 ? parts[1].Trim() : null;
                }
                else if (timePart.Contains('~'))
                {
                    var parts = timePart.Split('~', 2);
                    startPart = parts[0].Trim();
                    endPart = parts.Length > 1 ? parts[1].Trim() : null;
                }
                else if (timePart.Contains('-'))
                {
                    var parts = timePart.Split('-', 2);
                    startPart = parts[0].Trim();
                    endPart = parts.Length > 1 ? parts[1].Trim() : null;
                }
                else if (timePart.Contains(','))
                {
                    var parts = timePart.Split(',', 2);
                    startPart = parts[0].Trim();
                    endPart = parts.Length > 1 ? parts[1].Trim() : null;
                }
                else
                {
                    startPart = timePart.Trim();
                }

                if (!string.IsNullOrWhiteSpace(startPart) &&
                    !startPart.StartsWith("+") &&
                    !startPart.StartsWith("*"))
                {
                    if (!IsValidTimeFormat(startPart))
                        errors.Add(LocalizationService.F("第 {0} 行: 时间格式无效 '{1}'", lineNum, startPart)); // 第 {0} 行: 时间格式无效 '{1}'
                }

                if (!string.IsNullOrWhiteSpace(endPart) && !timePart.Contains('~'))
                {
                    if (!IsValidTimeFormat(endPart))
                        errors.Add(LocalizationService.F("第 {0} 行: 结束时间格式无效 '{1}'", lineNum, endPart)); // 第 {0} 行: 结束时间格式无效 '{1}'
                }

                continue;
            }

            if (line.StartsWith(":"))
            {
                if (line.StartsWith(":pos "))
                {
                    var posPart = line.Substring(5).Trim();
                    var coords = posPart.Split(',');
                    if (coords.Length < 2)
                        errors.Add(LocalizationService.F("第 {0} 行: :pos 需要两个坐标值 (X,Y)", lineNum)); // 第 {0} 行: :pos 需要两个坐标值 (X,Y)
                }
                continue;
            }

            if (!line.StartsWith(":") && !line.StartsWith("@"))
            {
                textLineCount++;
            }
        }

        if (timeLineCount == 0 && textLineCount > 0)
            errors.Add(LocalizationService.T("脚本中没有时间标记 (@开头的时间行)")); // 脚本中没有时间标记 (@开头的时间行)

        if (!hasEnd)
            errors.Add(LocalizationService.T("脚本缺少 :end 结束标记")); // 脚本缺少 :end 结束标记

        return errors;
    }

    /// <summary>
    /// 判断一段时间文本是否满足当前脚本允许的格式。
    /// </summary>
    private bool IsValidTimeFormat(string time)
    {
        if (string.IsNullOrWhiteSpace(time))
            return false;

        time = time.Trim();

        if (time.EndsWith("F", StringComparison.OrdinalIgnoreCase))
        {
            var numPart = time.Substring(0, time.Length - 1);
            return double.TryParse(numPart, out _);
        }

        if (double.TryParse(time, out _))
            return true;

        var parts = time.Split(':');
        if (parts.Length == 3)
        {
            if (!int.TryParse(parts[0], out _)) return false;
            if (!int.TryParse(parts[1], out _)) return false;

            var secParts = parts[2].Split(new[] { '.', ',', ':' }, 2);
            if (!int.TryParse(secParts[0], out _)) return false;
            if (secParts.Length > 1 && !int.TryParse(secParts[1], out _)) return false;
            return true;
        }

        if (parts.Length == 2)
        {
            if (!int.TryParse(parts[0], out _)) return false;

            var secParts = parts[1].Split(new[] { '.', ',', ':' }, 2);
            if (!int.TryParse(secParts[0], out _)) return false;
            if (secParts.Length > 1 && !int.TryParse(secParts[1], out _)) return false;
            return true;
        }

        return false;
    }

    #endregion
}
