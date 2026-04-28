using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MMTGui.Localization;

namespace MMTGui;

/// <summary>
/// MainWindow 本地化分部类。
/// 负责运行时语言切换、语言菜单状态同步，以及按语言加载示例脚本。
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// 初始化主窗口本地化行为。
    /// </summary>
    private void InitializeLocalization()
    {
        LocalizationService.RegisterWindow(this, ApplyLocalizedRuntimeState);
    }

    /// <summary>
    /// 处理语言菜单点击事件。
    /// </summary>
    private void OnLanguageSelected(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem || menuItem.Tag is not string languageTag)
            return;

        var language = languageTag switch
        {
            "en" => AppLanguage.English, // 英文
            "zh-Hant" => AppLanguage.TraditionalChinese, // 繁体中文
            "ja" => AppLanguage.Japanese, // 日文
            _ => AppLanguage.SimplifiedChinese // 简体中文
        };

        LocalizationService.SetLanguage(language);
    }

    /// <summary>
    /// 本地化应用后，刷新动态文本和当前语言示例脚本。
    /// </summary>
    private void ApplyLocalizedRuntimeState()
    {
        UpdateLanguageMenuChecks();
        UpdateStatusBar();
        SetStatus(_lastStatusMessage);

        if (_isUsingLocalizedExample && _loadedExampleLanguage != LocalizationService.CurrentLanguage)
            LoadLocalizedExampleScript(LocalizationService.CurrentLanguage);
    }

    /// <summary>
    /// 根据当前语言刷新语言菜单的勾选状态。
    /// </summary>
    private void UpdateLanguageMenuChecks()
    {
        if (MenuLanguageEnglish != null)
            MenuLanguageEnglish.IsChecked = LocalizationService.CurrentLanguage == AppLanguage.English;

        if (MenuLanguageSimplifiedChinese != null)
            MenuLanguageSimplifiedChinese.IsChecked = LocalizationService.CurrentLanguage == AppLanguage.SimplifiedChinese;

        if (MenuLanguageTraditionalChinese != null)
            MenuLanguageTraditionalChinese.IsChecked = LocalizationService.CurrentLanguage == AppLanguage.TraditionalChinese;

        if (MenuLanguageJapanese != null)
            MenuLanguageJapanese.IsChecked = LocalizationService.CurrentLanguage == AppLanguage.Japanese;
    }

    /// <summary>
    /// 按当前语言加载 example 目录中的示例脚本。
    /// </summary>
    private void LoadLocalizedExampleScript(AppLanguage language)
    {
        var script = LocalizedExampleScriptService.Load(language);
        if (string.IsNullOrWhiteSpace(script))
            return;

        if (ScriptEditor != null)
        {
            ScriptEditor.Text = script;
            ScriptEditor.MoveCaretToStart();
        }

        _subtitles.Clear();
        ParseScriptToSubtitles();
        RefreshSubtitleList();
        _isModified = false;
        _currentFilePath = null;
        _isUsingLocalizedExample = true;
        _loadedExampleLanguage = language;
        UpdateStatusBar();
    }
}
