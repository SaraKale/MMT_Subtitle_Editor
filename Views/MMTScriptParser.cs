using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;

namespace MMTGui;

/// <summary>
/// MMTScript 脚本解析器。
/// 负责解析 MikuMikuTelop 脚本格式，并生成统一的字幕条目集合。
/// </summary>
public static class MmtScriptParser
{
    /// <summary>
    /// 解析器状态快照。
    /// 用于保存当前命令块下的样式、位置、动画与时间等上下文信息。
    /// </summary>
    private sealed class ParserState
    {
        public double Fade { get; set; } = 0.38;
        public double AutoTime { get; set; } = 0.16;
        public double PosX { get; set; }
        public double PosY { get; set; }
        public double PosZ { get; set; }
        public int HAlign { get; set; } = 1;
        public int VAlign { get; set; } = 2;
        public double Alpha { get; set; } = 1.0;
        public bool Billboard { get; set; }
        public double TimeBase { get; set; }

        public string FontName { get; set; } = "Yu Gothic";
        public double FontSize { get; set; } = 13;
        public Color FontColor { get; set; } = Colors.White;
        public Color EdgeColor { get; set; } = Colors.Black;
        public double EdgeWidth { get; set; } = 8;
        public bool EdgeRound { get; set; } = true;
        public Color ShadowColor { get; set; } = Color.FromRgb(0, 0, 100);
        public double ShadowDistance { get; set; }
        public double ShadowBlur { get; set; }
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public bool Underline { get; set; }
        public bool Strikeout { get; set; }
        public bool Tate { get; set; }
        public Color BackColor { get; set; } = Color.FromArgb(0, 255, 255, 255);
        public double MarginX { get; set; } = 5;
        public double MarginY { get; set; } = 5;
        public double ScreenWidth { get; set; } = 1280;
        public double ScreenHeight { get; set; } = 720;
        public double ScaleX { get; set; } = 100;
        public double ScaleY { get; set; } = 100;
        public double Rotation { get; set; }
        public double Spacing { get; set; }
        public string AnimationType { get; set; } = "fade";
        public double AnimationDuration { get; set; }
        public string AnimationDirection { get; set; } = "ltr";
        public int AnimationBlindsCount { get; set; } = 8;
        public double AnimationBlurStrength { get; set; } = 1.0;

        public ParserState Clone() => new()
        {
            Fade = Fade,
            AutoTime = AutoTime,
            PosX = PosX,
            PosY = PosY,
            PosZ = PosZ,
            HAlign = HAlign,
            VAlign = VAlign,
            Alpha = Alpha,
            Billboard = Billboard,
            TimeBase = TimeBase,
            FontName = FontName,
            FontSize = FontSize,
            FontColor = FontColor,
            EdgeColor = EdgeColor,
            EdgeWidth = EdgeWidth,
            EdgeRound = EdgeRound,
            ShadowColor = ShadowColor,
            ShadowDistance = ShadowDistance,
            ShadowBlur = ShadowBlur,
            Bold = Bold,
            Italic = Italic,
            Underline = Underline,
            Strikeout = Strikeout,
            Tate = Tate,
            BackColor = BackColor,
            MarginX = MarginX,
            MarginY = MarginY,
            ScreenWidth = ScreenWidth,
            ScreenHeight = ScreenHeight,
            ScaleX = ScaleX,
            ScaleY = ScaleY,
            Rotation = Rotation,
            Spacing = Spacing,
            AnimationType = AnimationType,
            AnimationDuration = AnimationDuration,
            AnimationDirection = AnimationDirection,
            AnimationBlindsCount = AnimationBlindsCount,
            AnimationBlurStrength = AnimationBlurStrength
        };

        public SubtitleEntry ToSubtitleEntry(string text, double startTime, double endTime, string preset) => new()
        {
            Text = text,
            StartTime = startTime,
            EndTime = endTime,
            Preset = preset,
            PosX = PosX,
            PosY = PosY,
            PosZ = PosZ,
            Fade = Fade,
            Alpha = Alpha,
            Billboard = Billboard,
            HAlign = HAlign,
            VAlign = VAlign,
            FontName = FontName,
            FontSize = FontSize,
            FontColor = FontColor,
            EdgeColor = EdgeColor,
            EdgeWidth = EdgeWidth,
            EdgeRound = EdgeRound,
            ShadowColor = ShadowColor,
            ShadowDistance = ShadowDistance,
            ShadowBlur = ShadowBlur,
            Bold = Bold,
            Italic = Italic,
            Underline = Underline,
            Strikeout = Strikeout,
            Tate = Tate,
            BackColor = BackColor,
            MarginX = MarginX,
            MarginY = MarginY,
            ScreenWidth = ScreenWidth,
            ScreenHeight = ScreenHeight,
            ScaleX = ScaleX,
            ScaleY = ScaleY,
            Rotation = Rotation,
            Spacing = Spacing,
            AnimationType = AnimationType,
            AnimationDuration = AnimationDuration,
            AnimationDirection = AnimationDirection,
            AnimationBlindsCount = AnimationBlindsCount,
            AnimationBlurStrength = AnimationBlurStrength
        };
    }

    /// <summary>
    /// 解析整段 MMT 脚本文本，输出字幕条目集合。
    /// </summary>
    public static List<SubtitleEntry> Parse(string script)
    {
        var entries = new List<SubtitleEntry>();
        var lines = script.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        var defaultState = new ParserState();
        var state = defaultState.Clone();
        var presets = new Dictionary<string, ParserState>(StringComparer.Ordinal);

        string currentPreset = "def";
        double currentTime = 0;
        double lastEndTime = 0;
        bool nextSkip = false;
        SubtitleEntry? lastEntry = null;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            if (string.IsNullOrEmpty(line) || line.StartsWith("//"))
                continue;

            if (line.StartsWith(":set ")) // 定义预设
            {
                currentPreset = line.Substring(5).Trim();
                if (!string.IsNullOrEmpty(currentPreset))
                    presets[currentPreset] = state.Clone();
                continue;
            }

            if (line.StartsWith(":get ")) // 应用预设
            {
                var presetName = line.Substring(5).Trim();
                if (presetName.Equals("default", StringComparison.OrdinalIgnoreCase))
                {
                    state = defaultState.Clone();
                    currentPreset = "def";
                }
                else if (presets.TryGetValue(presetName, out var preset))
                {
                    state = preset.Clone();
                    currentPreset = presetName;
                }
                else
                {
                    currentPreset = presetName;
                }
                continue;
            }

            if (line.StartsWith(":fade ")) // 设置淡入淡出时间
            {
                if (double.TryParse(line.Substring(6).Trim(), out var f))
                    state.Fade = f;
                continue;
            }

            if (line.StartsWith(":autotime ")) // 设置自动时间
            {
                if (double.TryParse(line.Substring(10).Trim(), out var a))
                    state.AutoTime = a;
                continue;
            }

            if (line.StartsWith(":timebase ")) // 设置时间基准
            {
                if (double.TryParse(line.Substring(10).Trim(), out var tb))
                    state.TimeBase = tb;
                continue;
            }

            if (line.StartsWith(":pos ")) // 设置位置
            {
                var parts = line.Substring(5).Trim().Split(',');
                if (parts.Length >= 1 && double.TryParse(parts[0].Trim(), out var px)) state.PosX = px;
                if (parts.Length >= 2 && double.TryParse(parts[1].Trim(), out var py)) state.PosY = py;
                continue;
            }

            if (line.StartsWith(":pos_x ")) // 设置位置X轴
            {
                ApplyAxisPosition(line.Substring(7).Trim(), value => state.PosX = value, state.PosX);
                continue;
            }

            if (line.StartsWith(":pos_y ")) // 设置位置Y轴
            {
                ApplyAxisPosition(line.Substring(7).Trim(), value => state.PosY = value, state.PosY);
                continue;
            }

            if (line.StartsWith(":pos_nextline")) // 设置位置下一行
            {
                if (lastEntry != null && state.VAlign == 0 && lastEntry.VAlign == 0)
                {
                    state.PosY = lastEntry.PosY - lastEntry.TextureHeight * 200.0 / Math.Max(1.0, lastEntry.ScreenHeight);
                    state.PosY += lastEntry.FontSize / 50.0 * lastEntry.MarginY;
                    state.PosY += state.FontSize / 50.0 * state.MarginY;
                }
                continue;
            }

            if (line.StartsWith(":pos_next")) // 设置位置下一个字幕
            {
                if (lastEntry != null && state.HAlign == 0 && lastEntry.HAlign == 0)
                {
                    state.PosX = lastEntry.PosX + lastEntry.TextureWidth * 200.0 / Math.Max(1.0, lastEntry.ScreenWidth);
                    state.PosX -= lastEntry.FontSize / 50.0 * lastEntry.MarginX / GetScreenAspect(lastEntry.ScreenWidth, lastEntry.ScreenHeight);
                    state.PosX -= state.FontSize / 50.0 * state.MarginX / GetScreenAspect(state.ScreenWidth, state.ScreenHeight);
                }
                continue;
            }

            if (line.StartsWith(":pos_z ")) // 设置位置Z轴
            {
                if (double.TryParse(line.Substring(7).Trim(), out var pz)) state.PosZ = pz;
                continue;
            }

            if (line.StartsWith(":alpha ")) // 设置透明度
            {
                if (double.TryParse(line.Substring(7).Trim(), out var a)) state.Alpha = a;
                continue;
            }

            if (line.StartsWith(":billboard")) // 设置是否为字幕
            {
                state.Billboard = !line.Contains("_off", StringComparison.Ordinal);
                continue;
            }

            if (line.StartsWith(":align_left")) { state.HAlign = 0; continue; } // 设置水平对齐为左对齐
            if (line.StartsWith(":align_center")) { state.HAlign = 1; continue; } // 设置水平对齐为居中对齐
            if (line.StartsWith(":align_right")) { state.HAlign = 2; continue; } // 设置水平对齐为右对齐
            if (line.StartsWith(":valign_top")) { state.VAlign = 0; continue; } // 设置垂直对齐为顶部对齐
            if (line.StartsWith(":valign_center")) { state.VAlign = 1; continue; } // 设置垂直对齐为居中对齐
            if (line.StartsWith(":valign_bottom")) { state.VAlign = 2; continue; } // 设置垂直对齐为底部对齐

            if (line.StartsWith(":font ")) // 设置字体
            {
                state.FontName = line.Substring(6).Trim();
                continue;
            }

            if (line.StartsWith(":size ")) // 设置字体大小
            {
                if (double.TryParse(line.Substring(6).Trim(), out var s))
                    state.FontSize = s;
                continue;
            }

            if (line.StartsWith(":fontcolor ")) // 设置字体颜色
            {
                state.FontColor = ParseColor(line.Substring(10).Trim(), state.FontColor);
                continue;
            }

            if (line.StartsWith(":edgecolor ")) // 设置边框颜色
            {
                state.EdgeColor = ParseColor(line.Substring(11).Trim(), state.EdgeColor);
                continue;
            }

            if (line.StartsWith(":edge ")) // 设置边框宽度
            {
                if (double.TryParse(line.Substring(6).Trim(), out var e))
                    state.EdgeWidth = e;
                continue;
            }

            if (line.StartsWith(":edge_round")) // 设置边框圆角
            {
                state.EdgeRound = !line.Contains("_off", StringComparison.Ordinal);
                continue;
            }

            if (line.StartsWith(":shadowcolor ")) // 设置阴影颜色
            {
                state.ShadowColor = ParseColor(line.Substring(13).Trim(), state.ShadowColor);
                continue;
            }

            if (line.StartsWith(":shadow ")) // 设置阴影距离
            {
                if (double.TryParse(line.Substring(8).Trim(), out var sd))
                    state.ShadowDistance = sd;
                continue;
            }

            if (line.StartsWith(":shadowblur ")) // 设置阴影模糊
            {
                if (double.TryParse(line.Substring(12).Trim(), out var sb))
                    state.ShadowBlur = sb;
                continue;
            }

            if (line.StartsWith(":backcolor ")) // 设置背景颜色
            {
                state.BackColor = ParseColor(line.Substring(11).Trim(), state.BackColor);
                continue;
            }

            if (line.StartsWith(":bold")) // 设置是否加粗
            {
                state.Bold = !line.Contains("_off", StringComparison.Ordinal);
                continue;
            }

            if (line.StartsWith(":italic")) // 设置是否斜体
            {
                state.Italic = !line.Contains("_off", StringComparison.Ordinal);
                continue;
            }

            if (line.StartsWith(":underline")) // 设置是否下划线
            {
                state.Underline = !line.Contains("_off", StringComparison.Ordinal);
                continue;
            }

            if (line.StartsWith(":strikeout")) // 设置是否删除线
            {
                state.Strikeout = !line.Contains("_off", StringComparison.Ordinal);
                continue;
            }

            if (line.StartsWith(":tate")) // 设置是否竖排
            {
                state.Tate = !line.Contains("_off", StringComparison.Ordinal);
                continue;
            }

            if (line.StartsWith(":margin ")) // 设置边距
            {
                var parts = line.Substring(8).Trim().Split(',');
                if (parts.Length >= 1 && double.TryParse(parts[0].Trim(), out var mx)) state.MarginX = mx;
                if (parts.Length >= 2 && double.TryParse(parts[1].Trim(), out var my)) state.MarginY = my;
                continue;
            }

            if (line.StartsWith(":screen ")) // 设置屏幕尺寸
            {
                var parts = line.Substring(8).Trim().Split(',');
                if (parts.Length >= 1 && double.TryParse(parts[0].Trim(), out var sw)) state.ScreenWidth = sw;
                if (parts.Length >= 2 && double.TryParse(parts[1].Trim(), out var sh)) state.ScreenHeight = sh;
                continue;
            }

            if (line.StartsWith(":screen_height ")) // 设置屏幕高度
            {
                if (double.TryParse(line.Substring(15).Trim(), out var sh))
                    state.ScreenHeight = sh;
                continue;
            }

            if (line.StartsWith(":scalex ")) // 设置缩放比例
            {
                if (double.TryParse(line.Substring(8).Trim(), out var sx)) state.ScaleX = sx;
                continue;
            }

            if (line.StartsWith(":scaley ")) // 设置缩放比例
            {
                if (double.TryParse(line.Substring(8).Trim(), out var sy)) state.ScaleY = sy;
                continue;
            }

            if (line.StartsWith(":rotate ")) // 设置旋转角度
            {
                if (double.TryParse(line.Substring(8).Trim(), out var r)) state.Rotation = r;
                continue;
            }

            if (line.StartsWith(":spacing ")) // 设置间距
            {
                if (double.TryParse(line.Substring(9).Trim(), out var sp)) state.Spacing = sp;
                continue;
            }

            if (line.StartsWith(":anim ")) // 设置动画类型
            {
                state.AnimationType = NormalizeParserAnimationType(line.Substring(6).Trim());
                continue;
            }

            if (line.StartsWith(":animation ")) // 设置动画类型
            {
                state.AnimationType = NormalizeParserAnimationType(line.Substring(11).Trim());
                continue;
            }

            if (line.StartsWith(":animtime ")) // 设置动画时间
            {
                if (double.TryParse(line.Substring(10).Trim(), out var at))
                    state.AnimationDuration = at;
                continue;
            }

            if (line.StartsWith(":animation_time ")) // 设置动画时间
            {
                if (double.TryParse(line.Substring(16).Trim(), out var at))
                    state.AnimationDuration = at;
                continue;
            }

            if (line.StartsWith(":animdir ")) // 设置动画方向
            {
                state.AnimationDirection = NormalizeParserAnimationDirection(line.Substring(9).Trim());
                continue;
            }

            if (line.StartsWith(":animation_direction ")) // 设置动画方向
            {
                state.AnimationDirection = NormalizeParserAnimationDirection(line.Substring(21).Trim());
                continue;
            }

            if (line.StartsWith(":animblinds ")) // 设置动画模糊强度
            {
                if (int.TryParse(line.Substring(12).Trim(), out var blinds))
                    state.AnimationBlindsCount = Math.Max(2, blinds);
                continue;
            }

            if (line.StartsWith(":animation_blinds ")) // 设置动画模糊强度
            {
                if (int.TryParse(line.Substring(18).Trim(), out var blinds))
                    state.AnimationBlindsCount = Math.Max(2, blinds);
                continue;
            }

            if (line.StartsWith(":animblur ")) // 设置动画模糊强度
            {
                if (double.TryParse(line.Substring(10).Trim(), out var blurStrength))
                    state.AnimationBlurStrength = Math.Max(0, blurStrength);
                continue;
            }

            if (line.StartsWith(":animation_blur ")) // 设置动画模糊强度
            {
                if (double.TryParse(line.Substring(16).Trim(), out var blurStrength))
                    state.AnimationBlurStrength = Math.Max(0, blurStrength);
                continue;
            }

            if (line.StartsWith(":skip")) // 跳过下一行
            {
                nextSkip = true;
                continue;
            }

            if (line.StartsWith(":end")) // 结束
                break;

            if (line.StartsWith("@"))
            {
                var timePart = line.Substring(1).Trim();
                double startTime = 0;
                double endTime = -1;

                bool endtimeMode = timePart.Contains('~');
                bool dashMode = !endtimeMode && timePart.Contains('-');

                string[] prms;
                if (endtimeMode)
                    prms = timePart.Split('~', 2);
                else if (dashMode)
                    prms = timePart.Split('-', 2);
                else
                    prms = timePart.Split(',', 2);

                var stt = prms[0].Trim();

                if (stt.StartsWith("+"))
                {
                    startTime = lastEndTime + TimeParse(stt.Substring(1));
                }
                else if (stt.StartsWith("*"))
                {
                    startTime = currentTime + TimeParse(stt.Substring(1));
                }
                else
                {
                    startTime = state.TimeBase + TimeParse(stt);
                }

                if (prms.Length == 1)
                {
                    if (stt != "*0") endTime = -1;
                }
                else if (prms.Length == 2)
                {
                    if (endtimeMode)
                    {
                        endTime = TimeParse(prms[1].Trim());
                    }
                    else
                    {
                        endTime = startTime + TimeParse(prms[1].Trim());
                    }
                }

                if (endTime < 0)
                {
                    var text = CollectSubtitleText(lines, i + 1);
                    var charCount = text.Replace("\n", "").Length;
                    endTime = startTime + charCount * state.AutoTime + state.Fade * 2;
                }

                var subText = CollectSubtitleText(lines, i + 1);
                if (!string.IsNullOrEmpty(subText) && !nextSkip)
                {
                    var entry = state.ToSubtitleEntry(subText, startTime, endTime, currentPreset);
                    var textureSize = MeasureTextureSize(entry);
                    entry.TextureWidth = (int)Math.Round(textureSize.Width);
                    entry.TextureHeight = (int)Math.Round(textureSize.Height);
                    entries.Add(entry);
                    lastEntry = entry;
                }

                nextSkip = false;
                currentTime = startTime;
                lastEndTime = endTime;
                continue;
            }
        }

        for (int i = 0; i < entries.Count - 1; i++)
        {
            var nextStart = entries[i + 1].StartTime;
            if (entries[i].EndTime > nextStart)
                entries[i].EndTime = nextStart;
        }

        return entries;
    }

    /// <summary>
    /// 处理带增量语义的位置参数。
    /// </summary>
    private static void ApplyAxisPosition(string valueText, Action<double> setter, double currentValue)
    {
        if (valueText.StartsWith("+", StringComparison.Ordinal))
        {
            if (double.TryParse(valueText[1..].Trim(), out var delta))
                setter(currentValue + delta);
        }
        else if (double.TryParse(valueText, out var absolute))
        {
            setter(absolute);
        }
    }

    /// <summary>
    /// 从时间行后面收集字幕正文。
    /// </summary>
    private static string CollectSubtitleText(string[] lines, int startIndex)
    {
        var textLines = new List<string>();

        for (int j = startIndex; j < lines.Length; j++)
        {
            var rawLine = lines[j];
            var trimmed = rawLine.Trim();

            if (trimmed.StartsWith(":", StringComparison.Ordinal))
                break;

            if (trimmed.StartsWith("@", StringComparison.Ordinal) && !trimmed.StartsWith("@@", StringComparison.Ordinal))
                break;

            if (string.IsNullOrEmpty(trimmed))
            {
                textLines.Add("");
                continue;
            }

            if (trimmed.StartsWith("//", StringComparison.Ordinal))
                continue;

            textLines.Add(rawLine.Replace("@@", "@", StringComparison.Ordinal));
        }

        return string.Join("\n", textLines).TrimEnd('\n');
    }

    /// <summary>
    /// 规范化动画类型关键字。
    /// </summary>
    private static string NormalizeParserAnimationType(string? value)
    {
        var key = (value ?? string.Empty).Trim().ToLowerInvariant();
        return key switch
        {
            "random_blur" or "blur_random" or "blur" => "random_blur",
            "slide_up" or "slide" => "slide_up",
            "scale_pop" or "pop" or "scale" => "scale_pop",
            "wipe" or "erase" => "wipe",
            "blinds" or "venetian_blinds" => "blinds",
            _ => "fade"
        };
    }

    /// <summary>
    /// 规范化动画方向关键字。
    /// </summary>
    private static string NormalizeParserAnimationDirection(string? value)
    {
        var key = (value ?? string.Empty).Trim().ToLowerInvariant();
        return key switch
        {
            "rtl" or "right_to_left" or "right" => "rtl",
            _ => "ltr"
        };
    }

    /// <summary>
    /// 估算字幕贴图尺寸。
    /// </summary>
    private static Size MeasureTextureSize(SubtitleEntry entry)
    {
        var fontSizePx = entry.ScreenHeight * entry.FontSize / 200.0;
        var typeface = new Typeface(
            entry.FontName,
            entry.Italic ? FontStyle.Italic : FontStyle.Normal,
            entry.Bold ? FontWeight.Bold : FontWeight.Normal);

        var measured = MeasureRenderedText(entry.Text, typeface, fontSizePx, entry.Tate, entry.Spacing);
        var textureWidth = Math.Max(1, (int)Math.Ceiling(measured.Width + fontSizePx * entry.MarginX / 50.0));
        var textureHeight = Math.Max(1, (int)Math.Ceiling(measured.Height + fontSizePx * entry.MarginY / 50.0));

        return new Size(textureWidth, textureHeight);
    }

    /// <summary>
    /// 测量文本在横排或竖排下的渲染尺寸。
    /// </summary>
    private static (double Width, double Height) MeasureRenderedText(string text, Typeface typeface, double fontSize, bool isTate, double spacing)
    {
        if (!isTate)
        {
            if (spacing == 0)
            {
                var formatted = new FormattedText(
                    text,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    fontSize,
                    Brushes.White);

                return (Math.Max(1.0, formatted.Width), Math.Max(1.0, formatted.Height));
            }

            var textLines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            var maxLineWidth = 0.0;
            var totalHeight = 0.0;
            var lineHeight = 0.0;

            for (int li = 0; li < textLines.Length; li++)
            {
                var line = textLines[li];

                if (string.IsNullOrEmpty(line))
                {
                    if (lineHeight == 0)
                    {
                        var spaceGlyph = new FormattedText(
                            " ",
                            System.Globalization.CultureInfo.CurrentCulture,
                            FlowDirection.LeftToRight,
                            typeface,
                            fontSize,
                            Brushes.White);
                        lineHeight = spaceGlyph.Height;
                    }
                    totalHeight += lineHeight;
                    continue;
                }

                var lineWidth = 0.0;
                var chars = line.ToCharArray();

                for (int i = 0; i < chars.Length; i++)
                {
                    var ch = chars[i];
                    var glyph = new FormattedText(
                        ch.ToString(),
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        fontSize,
                        Brushes.White);

                    lineWidth += glyph.Width;
                    lineHeight = Math.Max(lineHeight, glyph.Height);

                    if (i < chars.Length - 1)
                        lineWidth += spacing;
                }

                maxLineWidth = Math.Max(maxLineWidth, lineWidth);
                totalHeight += lineHeight;
            }

            return (Math.Max(1.0, Math.Ceiling(maxLineWidth)), Math.Max(1.0, Math.Ceiling(totalHeight)));
        }

        text = ReverseLineForTate(text);
        var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        var columnGap = Math.Max(1.0, fontSize * 0.15);
        var tateTotalWidth = 0.0;
        var tateMaxHeight = 0.0;

        foreach (var line in lines)
        {
            var columnWidth = fontSize * 0.6;
            var columnHeight = fontSize;

            if (!string.IsNullOrEmpty(line))
            {
                columnWidth = 0.0;
                columnHeight = 0.0;

                foreach (var ch in line)
                {
                    var glyph = new FormattedText(
                        ch.ToString(),
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        fontSize,
                        Brushes.White);

                    columnWidth = Math.Max(columnWidth, glyph.Width);
                    columnHeight += glyph.Height + spacing;
                }

                if (line.Length > 0)
                    columnHeight -= spacing;
            }

            tateTotalWidth += columnWidth + columnGap;
            tateMaxHeight = Math.Max(tateMaxHeight, columnHeight);
        }

        if (lines.Length > 0)
            tateTotalWidth -= columnGap;

        return (Math.Max(1.0, Math.Ceiling(tateTotalWidth)), Math.Max(1.0, Math.Ceiling(tateMaxHeight)));
    }

    /// <summary>
    /// 竖排文本时反转行顺序。
    /// </summary>
    private static string ReverseLineForTate(string text)
    {
        var lines = text.Replace(Environment.NewLine, "\n").Split('\n');
        for (int i = 0; i < lines.Length / 2; i++)
        {
            var reverseIndex = lines.Length - 1 - i;
            (lines[i], lines[reverseIndex]) = (lines[reverseIndex], lines[i]);
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// 计算画面宽高比。
    /// </summary>
    private static double GetScreenAspect(double width, double height)
    {
        return height <= 0 ? 1.0 : width / height;
    }

    /// <summary>
    /// 从字符串解析颜色值，失败时使用回退颜色。
    /// </summary>
    private static Color ParseColor(string text, Color fallback)
    {
        var parts = text.Split(',');
        if (parts.Length >= 3 &&
            byte.TryParse(parts[0].Trim(), out var r) &&
            byte.TryParse(parts[1].Trim(), out var g) &&
            byte.TryParse(parts[2].Trim(), out var b))
        {
            if (parts.Length >= 4 && byte.TryParse(parts[3].Trim(), out var a))
                return Color.FromArgb(a, r, g, b);

            return Color.FromRgb(r, g, b);
        }

        return fallback;
    }

    /// <summary>
    /// 解析脚本中的时间文本。
    /// 支持秒数、标准时间码以及帧数表达式。
    /// </summary>
    private static double TimeParse(string text)
    {
        text = text.Trim();
        bool frame = false;

        if (text.EndsWith("F", StringComparison.InvariantCultureIgnoreCase))
        {
            frame = true;
            text = text.Substring(0, text.Length - 1);
        }

        if (double.TryParse(text, out var val))
            return frame ? val / 30 : val;

        var parts = text.Split(':');
        if (parts.Length == 3 || parts.Length == 2)
        {
            int hours = 0;
            int minutes = 0;
            string secText;

            if (parts.Length == 3)
            {
                if (!int.TryParse(parts[0], out hours)) return 0;
                if (!int.TryParse(parts[1], out minutes)) return 0;
                secText = parts[2];
            }
            else
            {
                if (!int.TryParse(parts[0], out minutes)) return 0;
                secText = parts[1];
            }

            var secParts = secText.Split(new[] { '.', ',', ':' }, 2);
            if (!int.TryParse(secParts[0], out var seconds)) return 0;

            double fraction = 0;
            if (secParts.Length > 1)
            {
                var fracText = secParts[1].Trim();
                if (fracText.Length > 3)
                    fracText = fracText.Substring(0, 3);
                while (fracText.Length < 3)
                    fracText += "0";

                if (!int.TryParse(fracText, out var fracMs))
                    return 0;

                fraction = fracMs / 1000.0;
            }

            var total = hours * 3600 + minutes * 60 + seconds + fraction;
            return frame ? total / 30 : total;
        }

        return 0;
    }
}
