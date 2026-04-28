using System;
using System.Globalization;

namespace MMTGui.Localization;

/// <summary>
/// 应用支持的界面语言。
/// </summary>
public enum AppLanguage
{
    SimplifiedChinese,
    TraditionalChinese,
    English,
    Japanese
}

/// <summary>
/// 语言相关的辅助扩展。
/// </summary>
public static class AppLanguageExtensions
{
    /// <summary>
    /// 根据系统文化信息推断最接近的应用语言。
    /// </summary>
    public static AppLanguage FromCultureInfo(CultureInfo? culture)
    {
        var name = culture?.Name ?? string.Empty;
        var language = culture?.TwoLetterISOLanguageName ?? string.Empty;

        if (string.Equals(language, "ja", StringComparison.OrdinalIgnoreCase))
            return AppLanguage.Japanese;

        if (string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
            return AppLanguage.English;

        if (name.Contains("Hant", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("-TW", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("-HK", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("-MO", StringComparison.OrdinalIgnoreCase))
        {
            return AppLanguage.TraditionalChinese;
        }

        if (string.Equals(language, "zh", StringComparison.OrdinalIgnoreCase))
            return AppLanguage.SimplifiedChinese;

        return AppLanguage.English;
    }

    /// <summary>
    /// 将应用语言映射为 .NET 文化信息。
    /// </summary>
    public static CultureInfo ToCultureInfo(this AppLanguage language)
    {
        return language switch
        {
            AppLanguage.English => new CultureInfo("en"),
            AppLanguage.TraditionalChinese => new CultureInfo("zh-Hant"),
            AppLanguage.Japanese => new CultureInfo("ja"),
            _ => new CultureInfo("zh-Hans")
        };
    }

    /// <summary>
    /// 获取示例脚本后缀。
    /// </summary>
    public static string ToExampleSuffix(this AppLanguage language)
    {
        return language switch
        {
            AppLanguage.English => "EN",
            AppLanguage.TraditionalChinese => "TC",
            AppLanguage.Japanese => "JP",
            _ => "SC"
        };
    }
}
