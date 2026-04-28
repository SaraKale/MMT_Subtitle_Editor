using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MMTGui.Localization;

/// <summary>
/// 本地化示例脚本服务。
/// 负责按照当前语言读取 example 目录中的示例脚本，并为常见命令插入语言注释。
/// </summary>
public static class LocalizedExampleScriptService
{
    private static readonly string[] ExampleBaseNames =
    {
        "MikuMikutelop実用編",
        "MikuMikuTelop紹介編"
    };

    /// <summary>
    /// 读取当前语言对应的示例脚本。
    /// 支持多种编码自动检测：UTF-8、Shift-JIS、GBK、默认系统编码。
    /// </summary>
    public static string? Load(AppLanguage language)
    {
        var suffix = language.ToExampleSuffix();
        var exampleDir = Path.Combine(AppContext.BaseDirectory, "example");
        if (!Directory.Exists(exampleDir))
            return null;

        foreach (var baseName in ExampleBaseNames)
        {
            var path = Path.Combine(exampleDir, $"{baseName}_{suffix}.mmt");
            if (File.Exists(path))
            {
                var raw = ReadFileWithAutoEncoding(path);
                return AnnotateCommands(raw, language);
            }
        }

        return null;
    }

    /// <summary>
    /// 使用多种编码尝试读取文件，自动检测最合适的编码。
    /// 优先级：UTF-8 > Shift-JIS > GBK > Default
    /// </summary>
    private static string ReadFileWithAutoEncoding(string path)
    {
        var bytes = File.ReadAllBytes(path);

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

        var ratio = (double)replacementCharCount / totalCharCount;
        return ratio < 0.01;
    }

    /// <summary>
    /// 为常见命令行添加本地化注释。
    /// 仅在上一行不是注释时插入，避免重复堆叠。
    /// </summary>
    public static string AnnotateCommands(string script, AppLanguage language)
    {
        var lines = script.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        var output = new List<string>(lines.Length * 2);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            if (TryGetCommentForCommand(trimmed, out var comment))
            {
                var previous = output.Count > 0 ? output[^1].Trim() : string.Empty;
                var localizedComment = LocalizationService.T(comment);
                if (!previous.StartsWith("//", StringComparison.Ordinal))
                    output.Add($"// {localizedComment}");
            }

            output.Add(line);
        }

        return string.Join(Environment.NewLine, output);
    }

    private static bool TryGetCommentForCommand(string line, out string commentKey)
    {
        commentKey = string.Empty;
        if (!line.StartsWith(":", StringComparison.Ordinal))
            return false;

        commentKey = line switch
        {
            var value when value.StartsWith(":font ", StringComparison.Ordinal) => "指定字体",
            var value when value.StartsWith(":size ", StringComparison.Ordinal) => "字体大小",
            var value when value.StartsWith(":fontcolor ", StringComparison.Ordinal) => "字体颜色",
            var value when value.StartsWith(":align_left", StringComparison.Ordinal) => "左对齐",
            var value when value.StartsWith(":align_center", StringComparison.Ordinal) => "水平居中",
            var value when value.StartsWith(":align_right", StringComparison.Ordinal) => "右对齐",
            var value when value.StartsWith(":valign_top", StringComparison.Ordinal) => "顶部对齐",
            var value when value.StartsWith(":valign_center", StringComparison.Ordinal) => "垂直居中",
            var value when value.StartsWith(":valign_bottom", StringComparison.Ordinal) => "底部对齐",
            var value when value.StartsWith(":screen ", StringComparison.Ordinal) => "屏幕尺寸",
            var value when value.StartsWith(":pos ", StringComparison.Ordinal) || value.StartsWith(":pos_", StringComparison.Ordinal) => "字幕位置",
            var value when value.StartsWith(":alpha ", StringComparison.Ordinal) => "不透明度",
            var value when value.StartsWith(":edge ", StringComparison.Ordinal) => "指定描边粗细",
            var value when value.StartsWith(":edgecolor ", StringComparison.Ordinal) => "描边颜色",
            var value when value.StartsWith(":edge_round", StringComparison.Ordinal) => "将描边边角设为圆形",
            var value when value.StartsWith(":shadow ", StringComparison.Ordinal) => "阴影距离",
            var value when value.StartsWith(":shadowcolor ", StringComparison.Ordinal) => "阴影颜色",
            var value when value.StartsWith(":shadowblur ", StringComparison.Ordinal) => "阴影模糊",
            var value when value.StartsWith(":fade ", StringComparison.Ordinal) => "淡入淡出",
            var value when value.StartsWith(":autotime ", StringComparison.Ordinal) => "自动时长",
            var value when value.StartsWith(":timebase ", StringComparison.Ordinal) => "时间基准",
            var value when value.StartsWith(":tate", StringComparison.Ordinal) => "竖排文字",
            var value when value.StartsWith(":billboard", StringComparison.Ordinal) => "公告板模式",
            var value when value.StartsWith(":anim ", StringComparison.Ordinal) => "动画方式",
            var value when value.StartsWith(":animtime ", StringComparison.Ordinal) => "动画时间",
            var value when value.StartsWith(":animdir ", StringComparison.Ordinal) => "擦除方向",
            var value when value.StartsWith(":animblinds ", StringComparison.Ordinal) => "百叶数量",
            var value when value.StartsWith(":animblur ", StringComparison.Ordinal) => "模糊强度",
            var value when value.StartsWith(":set ", StringComparison.Ordinal) => "定义样式集合",
            var value when value.StartsWith(":get ", StringComparison.Ordinal) => "调用样式集合",
            var value when value.StartsWith(":skip", StringComparison.Ordinal) => "跳过下一段文本",
            var value when value.StartsWith(":end", StringComparison.Ordinal) => "结束标记",
            _ => string.Empty
        };

        return !string.IsNullOrWhiteSpace(commentKey);
    }
}
