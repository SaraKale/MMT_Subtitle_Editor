using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace MMTGui.Localization;

/// <summary>
/// 运行时本地化服务。
/// 负责读取 RESX 资源、切换界面语言，并对窗口可见控件执行动态文本替换。
/// </summary>
public static class LocalizationService
{
    private static readonly ConditionalWeakTable<object, Dictionary<string, string>> OriginalTexts = new();
    private static readonly ResourceManager ResourceManager = CreateResourceManager();

    private static CultureInfo _currentCulture = AppLanguage.SimplifiedChinese.ToCultureInfo();

    /// <summary>当前界面语言。</summary>
    public static AppLanguage CurrentLanguage { get; private set; } = AppLanguage.SimplifiedChinese;

    /// <summary>语言切换后触发。</summary>
    public static event EventHandler? LanguageChanged;

    /// <summary>
    /// 初始化默认语言环境。
    /// </summary>
    public static void Initialize(AppLanguage? language = null)
    {
        var savedLanguage = LoadSavedLanguage();
        var targetLanguage = savedLanguage ?? language ?? AppLanguageExtensions.FromCultureInfo(CultureInfo.CurrentUICulture);
        SetLanguage(targetLanguage, raiseEvent: false);
    }

    /// <summary>
    /// 切换当前语言。
    /// </summary>
    public static void SetLanguage(AppLanguage language, bool raiseEvent = true)
    {
        CurrentLanguage = language;
        _currentCulture = language.ToCultureInfo();
        Thread.CurrentThread.CurrentCulture = _currentCulture;
        Thread.CurrentThread.CurrentUICulture = _currentCulture;

        SaveLanguage(language);

        if (raiseEvent)
            LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    private static string GetSettingsFilePath()
    {
        var exePath = Environment.ProcessPath ?? AppContext.BaseDirectory;
        var exeDir = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;
        return Path.Combine(exeDir, "settings.json");
    }

    private static AppLanguage? LoadSavedLanguage()
    {
        try
        {
            var path = GetSettingsFilePath();
            if (!File.Exists(path))
                return null;

            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json);
            return settings?.Language;
        }
        catch
        {
            return null;
        }
    }

    private static void SaveLanguage(AppLanguage language)
    {
        try
        {
            var path = GetSettingsFilePath();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var settings = new AppSettings { Language = language };
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch
        {
        }
    }

    private class AppSettings
    {
        public AppLanguage? Language { get; set; }
    }

    /// <summary>
    /// 根据原始文本获取当前语言的翻译。
    /// 缺少翻译时回退到原文。
    /// </summary>
    public static string T(string sourceText)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
            return sourceText;

        try
        {
            return ResourceManager.GetString(sourceText, _currentCulture) ?? sourceText;
        }
        catch (MissingManifestResourceException)
        {
            return sourceText;
        }
    }

    /// <summary>
    /// 读取带格式占位符的本地化文本。
    /// </summary>
    public static string F(string sourceFormat, params object[] args)
    {
        return string.Format(_currentCulture, T(sourceFormat), args);
    }

    /// <summary>
    /// 注册窗口的本地化刷新逻辑。
    /// 语言切换后会自动重新应用静态文本；窗口关闭时自动解绑。
    /// </summary>
    public static void RegisterWindow(Window window, Action? afterApply = null)
    {
        void ApplyAll()
        {
            Apply(window);
            afterApply?.Invoke();
        }

        EventHandler handler = (_, _) => ApplyAll();
        LanguageChanged += handler;
        window.Closed += (_, _) => LanguageChanged -= handler;

        ApplyAll();
    }

    /// <summary>
    /// 对指定窗口或控件树应用本地化。
    /// </summary>
    public static void Apply(object root)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        Traverse(root, visited);
    }

    private static void Traverse(object? target, HashSet<object> visited)
    {
        if (target == null || !visited.Add(target))
            return;

        LocalizeObject(target);

        if (target is ILogical logical)
        {
            foreach (var child in logical.LogicalChildren)
                Traverse(child, visited);
        }

        if (target is DataGrid dataGrid)
        {
            foreach (var column in dataGrid.Columns)
                Traverse(column, visited);
        }

        if (target is MenuItem menuItem && menuItem.Items is IEnumerable<object> menuItems)
        {
            foreach (var item in menuItems)
                Traverse(item, visited);
        }

        if (target is ComboBox comboBox && comboBox.Items is IEnumerable<object> comboItems)
        {
            foreach (var item in comboItems)
                Traverse(item, visited);
        }

        if (target is Expander expander && expander.Header != null)
            Traverse(expander.Header, visited);

        if (target is Control control)
        {
            var contextMenu = control.ContextMenu;
            if (contextMenu != null)
            {
                Traverse(contextMenu, visited);
                foreach (var item in contextMenu.Items)
                    Traverse(item, visited);
            }
        }
    }

    private static void LocalizeObject(object target)
    {
        switch (target)
        {
            case Window window:
                window.Title = GetLocalized(target, nameof(Window.Title), window.Title);
                break;
            case TextBlock textBlock when !ShouldSkip(textBlock.Name):
                textBlock.Text = GetLocalized(target, nameof(TextBlock.Text), textBlock.Text);
                break;
            case TextBox textBox:
                if (textBox.Watermark is string watermark)
                    textBox.Watermark = GetLocalized(target, nameof(TextBox.Watermark), watermark);
                break;
            case MenuItem menuItem when menuItem.Header is string header:
                menuItem.Header = GetLocalized(target, nameof(MenuItem.Header), header);
                break;
            case Button button when button.Content is string content:
                button.Content = GetLocalized(target, nameof(Button.Content), content);
                break;
            case ToggleButton toggleButton when toggleButton.Content is string content:
                toggleButton.Content = GetLocalized(target, nameof(ToggleButton.Content), content);
                break;
            case CheckBox checkBox when checkBox.Content is string content:
                checkBox.Content = GetLocalized(target, nameof(CheckBox.Content), content);
                break;
            case ComboBoxItem comboBoxItem when comboBoxItem.Content is string content:
                comboBoxItem.Content = GetLocalized(target, nameof(ComboBoxItem.Content), content);
                break;
            case DataGridTextColumn textColumn when textColumn.Header is string header:
                textColumn.Header = GetLocalized(target, nameof(DataGridTextColumn.Header), header);
                break;
            case DataGridTemplateColumn templateColumn when templateColumn.Header is string header:
                templateColumn.Header = GetLocalized(target, nameof(DataGridTemplateColumn.Header), header);
                break;
        }

        if (target is Control control && ToolTip.GetTip(control) is string tip)
        {
            ToolTip.SetTip(control, GetLocalized(target, "ToolTip.Tip", tip));
        }
    }

    private static string GetLocalized(object target, string propertyName, string? currentValue)
    {
        currentValue ??= string.Empty;

        if (!OriginalTexts.TryGetValue(target, out var map))
        {
            map = new Dictionary<string, string>(StringComparer.Ordinal);
            OriginalTexts.Add(target, map);
        }

        if (!map.TryGetValue(propertyName, out var originalValue))
        {
            originalValue = currentValue;
            map[propertyName] = currentValue;
        }

        return T(originalValue);
    }

    private static bool ShouldSkip(string? controlName)
    {
        if (string.IsNullOrWhiteSpace(controlName))
            return false;

        return controlName is
            "StatusText" or
            "StatusSubtitleCount" or
            "StatusResolution" or
            "StatusTime" or
            "SubtitleCountLabel" or
            "PreviewResolution" or
            "PreviewTime" or
            "TxtZoom" or
            "TxtSongName" or
            "TxtSinger" or
            "TxtAlbum" or
            "TxtDuration" or
            "TxtPublishDate" or
            "TxtSongSource" or
            "TxtSongLinkDetail" or
            "TxtLyrics" or
            "TxtLyricsPreview" or
            "TxtFileCount" or
            "TxtStatus" or
            "CanvasColorValue" or
            "TxtBackgroundImagePath" or
            "TxtSecondsHint";
    }

    private static ResourceManager CreateResourceManager()
    {
        var assembly = typeof(LocalizationService).Assembly;
        const string fixedBaseName = "MMT_Subtitle_Editor.Resources.Strings";
        return new ResourceManager(fixedBaseName, assembly);
    }
}
