using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MMTGui.Localization;

namespace MMTGui;

/// <summary>
/// MainWindow 属性面板事件分部类。
/// 负责属性控件与脚本文本之间的双向同步，以及颜色选择等辅助交互。
/// </summary>
public partial class MainWindow
{
    private DispatcherTimer? _debounceTimer;
    private NumericUpDown? _lastChangedNud;

    private void InitializeDebounceTimer()
    {
        _debounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _debounceTimer.Tick += OnDebounceTimerTick;

        RegisterNumericKeyDownHandlers();
    }

    private void RegisterNumericKeyDownHandlers()
    {
        var nuds = new NumericUpDown?[]
        {
            NudFontSize, NudScaleX, NudScaleY, NudRotation, NudSpacing,
            NudPosX, NudPosY, NudPosZ, NudEdge, NudShadow, NudShadowBlur,
            NudFade, NudAutoTime, NudTimeBase, NudMarginX, NudMarginY,
            NudScreenWidth, NudScreenHeight, NudOpacity
        };

        foreach (var nud in nuds)
        {
            if (nud != null)
                nud.KeyDown += OnNumericKeyDown;
        }
    }
    private void OnNumericKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _isSyncingFromProperty) return;
        if (sender is not NumericUpDown nud) return;

        e.Handled = true;

        _debounceTimer?.Stop();
        _lastChangedNud = null;

        _isSyncingFromProperty = true;
        try
        {
            ProcessNudChange(nud);
        }
        finally
        {
            _isSyncingFromProperty = false;
        }
    }

    private void OnDebounceTimerTick(object? sender, EventArgs e)
    {
        _debounceTimer?.Stop();
        if (_lastChangedNud == null || _isSyncingFromProperty) return;

        _isSyncingFromProperty = true;
        try
        {
            ProcessNudChange(_lastChangedNud);
        }
        finally
        {
            _isSyncingFromProperty = false;
            _lastChangedNud = null;
        }
    }

    private void ProcessNudChange(NumericUpDown nud)
    {
        // 智能检测下一个描边层数
        if (nud == NudEdge)
        {
            var layer = GetNextEdgeLayer();
            var cmd = layer == 1 ? "edge" : $"edge{layer}";
            InsertOrReplaceCommand($":{cmd} {nud.Value}", cmd);
        }
        else if (nud == NudFontSize)
            InsertOrReplaceCommand($":size {nud.Value}");
        else if (nud == NudPosX)
            InsertOrReplaceCommand($":pos {(double)(NudPosX?.Value ?? 0m)},{(double)(NudPosY?.Value ?? 0m)}");
        else if (nud == NudPosY)
            InsertOrReplaceCommand($":pos {(double)(NudPosX?.Value ?? 0m)},{(double)(NudPosY?.Value ?? 0m)}");
        else if (nud == NudPosZ)
            InsertOrReplaceCommand($":pos_z {nud.Value}");
        else if (nud == NudShadow)
            InsertOrReplaceCommand($":shadow {nud.Value}");
        else if (nud == NudShadowBlur)
            InsertOrReplaceCommand($":shadowblur {nud.Value}");
        else if (nud == NudFade)
            InsertOrReplaceCommand($":fade {nud.Value:0.00}");
        else if (nud == NudAutoTime)
            InsertOrReplaceCommand($":autotime {nud.Value:0.00}");
        else if (nud == NudTimeBase)
            InsertOrReplaceCommand($":timebase {nud.Value:0.0}");
        else if (nud == NudMarginX)
            InsertOrReplaceCommand($":margin {(double)(NudMarginX?.Value ?? 5m)},{(double)(NudMarginY?.Value ?? 5m)}");
        else if (nud == NudMarginY)
            InsertOrReplaceCommand($":margin {(double)(NudMarginX?.Value ?? 5m)},{(double)(NudMarginY?.Value ?? 5m)}");
        else if (nud == NudScreenWidth)
            InsertOrReplaceCommand($":screen {(double)(NudScreenWidth?.Value ?? 1280m)},{(double)(NudScreenHeight?.Value ?? 720m)}");
        else if (nud == NudScreenHeight)
            InsertOrReplaceCommand($":screen {(double)(NudScreenWidth?.Value ?? 1280m)},{(double)(NudScreenHeight?.Value ?? 720m)}");
        else if (nud == NudOpacity)
            InsertOrReplaceCommand($":alpha {nud.Value:0.00}");
        else if (nud == NudScaleX)
            InsertOrReplaceCommand($":scalex {nud.Value}");
        else if (nud == NudScaleY)
            InsertOrReplaceCommand($":scaley {nud.Value}");
        else if (nud == NudRotation)
            InsertOrReplaceCommand($":rotate {nud.Value}");
        else if (nud == NudSpacing)
            InsertOrReplaceCommand($":spacing {nud.Value}");
        else if (_nudAnimationDuration != null && nud == _nudAnimationDuration)
            InsertOrReplaceCommand($":animtime {nud.Value:0.00}", "animtime");
        else if (_nudAnimationBlindsCount != null && nud == _nudAnimationBlindsCount)
            InsertOrReplaceCommand($":animblinds {nud.Value:0}", "animblinds");
        else if (_nudAnimationBlurStrength != null && nud == _nudAnimationBlurStrength)
            InsertOrReplaceCommand($":animblur {nud.Value:0.0}", "animblur");
        else if (nud == NudEdge2)
        {
            var layer = GetNextEdgeLayer();
            var cmd = layer == 1 ? "edge" : $"edge{layer}";
            InsertOrReplaceCommand($":{cmd} {nud.Value:0}", cmd);
        }
        else if (nud == NudGlow)
            InsertOrReplaceCommand($":glow {nud.Value:0}", "glow");
        else if (nud == NudGlowAlpha)
            InsertOrReplaceCommand($":glowalpha {nud.Value:0.0}", "glowalpha");
    }

    /// <summary>
    /// 检测脚本中已有的描边层数，返回下一个可用的层数（1-4）。
    /// 规则：返回已存在的最高层数+1（追加模式）；如果都不存在，返回 1；超过4则循环回1。
    /// </summary>
    private int GetNextEdgeLayer()
    {
        if (ScriptEditor == null) return 1;
        var text = ScriptEditor.Text ?? string.Empty;
        var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

        int maxLayer = 0;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith(":edge4 ") || trimmed.StartsWith(":edge4\t"))
                maxLayer = Math.Max(maxLayer, 4);
            else if (trimmed.StartsWith(":edge3 ") || trimmed.StartsWith(":edge3\t"))
                maxLayer = Math.Max(maxLayer, 3);
            else if (trimmed.StartsWith(":edge2 ") || trimmed.StartsWith(":edge2\t"))
                maxLayer = Math.Max(maxLayer, 2);
            else if (trimmed.StartsWith(":edge ") || trimmed.StartsWith(":edge\t"))
                maxLayer = Math.Max(maxLayer, 1);
        }

        if (maxLayer == 0) return 1;
        if (maxLayer >= 4) return 1;
        return maxLayer + 1;
    }

    /// <summary>
    /// 检测脚本中已有的描边颜色层数，返回下一个可用的层数（1-4）。
    /// 规则：返回已存在的最高层数+1（追加模式）；如果都不存在，返回 1；超过4则循环回1。
    /// </summary>
    private int GetNextEdgeColorLayer()
    {
        if (ScriptEditor == null) return 1;
        var text = (ScriptEditor.Text ?? string.Empty)
            .Replace("\r\n", "\n")
            .Replace("\r", "\n");
        var lines = text.Split(new[] { "\n" }, StringSplitOptions.None);

        int maxLayer = 0;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith(":edgecolor4 ") || trimmed.StartsWith(":edgecolor4\t"))
                maxLayer = Math.Max(maxLayer, 4);
            else if (trimmed.StartsWith(":edgecolor3 ") || trimmed.StartsWith(":edgecolor3\t"))
                maxLayer = Math.Max(maxLayer, 3);
            else if (trimmed.StartsWith(":edgecolor2 ") || trimmed.StartsWith(":edgecolor2\t"))
                maxLayer = Math.Max(maxLayer, 2);
            else if (trimmed.StartsWith(":edgecolor ") || trimmed.StartsWith(":edgecolor\t"))
                maxLayer = Math.Max(maxLayer, 1);
        }

        if (maxLayer == 0) return 1;
        if (maxLayer >= 4) return 1;
        return maxLayer + 1;
    }

    #region 属性面板事件

    /// <summary>
    /// 透明度滑块变化
    /// </summary>
    /// <summary>
    /// 画面尺寸变化
    /// </summary>
    private void OnScreenSizeChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (PreviewResolution != null && NudScreenWidth != null && NudScreenHeight != null)
            PreviewResolution.Text = $"{NudScreenWidth.Value} × {NudScreenHeight.Value}";

        UpdatePreviewFrame((double)(NudScreenWidth?.Value ?? 1280m), (double)(NudScreenHeight?.Value ?? 720m));

        if (!_isSyncingFromProperty)
            InsertOrReplaceCommand($":screen {(double)(NudScreenWidth?.Value ?? 1280m)},{(double)(NudScreenHeight?.Value ?? 720m)}");

        UpdateStatusBar();
    }

    /// <summary>
    /// 字体名称变化时同步到脚本
    /// </summary>
    private void OnFontNameChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (CmbFontName == null || _isSyncingFromProperty) return;
        var item = CmbFontName.SelectedItem as ComboBoxItem;
        if (item?.Content is string fontName)
        {
            InsertOrReplaceCommand($":font {fontName}");
        }
    }

    // :time 命令插入，格式为 :time hh:mm:ss.cs - hh:mm:ss.cs
    private void OnInsertTimeRange(object? sender, RoutedEventArgs e)
    {
        var start = NormalizeTimeCode(TxtTimeStart?.Text ?? "00:00:00.000");
        var end = NormalizeTimeCode(TxtTimeEnd?.Text ?? "00:00:00.000");
        InsertOrReplaceCommand($"@{start} - {end}");
    }

    // :time 命令插入，格式为 :time hh:mm:ss.cs
    private static string NormalizeTimeCode(string text)
    {
        return (text ?? string.Empty).Trim().Replace(',', '.');
    }

    // :set 命令插入，格式为 :set name
    private void OnInsertSet(object? sender, RoutedEventArgs e)
    {
        var name = TxtSetName?.Text?.Trim() ?? "";
        if (!string.IsNullOrEmpty(name))
            InsertOrReplaceCommand($":set {name}");
    }

    // :get 命令插入，格式为 :get name
    private void OnInsertGet(object? sender, RoutedEventArgs e)
    {
        var name = TxtGetName?.Text?.Trim() ?? "";
        if (!string.IsNullOrEmpty(name))
            InsertOrReplaceCommand($":get {name}");
    }

    // :skip 跳过命令
    private void OnInsertSkip(object? sender, RoutedEventArgs e)
    {
        InsertOrReplaceCommand(":skip");
    }

    // :end 结束命令
    private void OnInsertEnd(object? sender, RoutedEventArgs e)
    {
        if (ScriptEditor == null)
            return;

        var normalized = (ScriptEditor.Text ?? string.Empty)
            .Replace("\r\n", "\n")
            .Replace("\r", "\n");

        var lines = normalized
            .Split('\n')
            .Where(line => !string.Equals(line.Trim(), ":end", StringComparison.OrdinalIgnoreCase))
            .ToList();

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
            lines.RemoveAt(lines.Count - 1);

        if (lines.Count > 0)
            lines.Add(string.Empty);
        lines.Add(":end");

        ScriptEditor.Text = string.Join(Environment.NewLine, lines);
        ScriptEditor.FocusEditor();
    }


    /// <summary>
    /// 属性面板任意值变化时，将对应命令同步插入/替换到脚本
    /// 使用防抖机制，避免频繁更新
    /// </summary>
    private void OnPropertyValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_isSyncingFromProperty || sender is not NumericUpDown nud) return;

        _lastChangedNud = nud;
        _debounceTimer?.Stop();
        _debounceTimer?.Start();
    }

    /// <summary>
    /// 插入按钮点击时，将对应NumericUpDown的值插入到脚本
    /// </summary>
    private void OnInsertNudValue(object? sender, RoutedEventArgs e)
    {
        if (_isSyncingFromProperty) return;
        if (sender is not Button btn || btn.Tag is not string nudName) return;

        var nud = FindNudByName(nudName);
        if (nud == null) return;

        _debounceTimer?.Stop();
        _lastChangedNud = null;

        _isSyncingFromProperty = true;
        try
        {
            ProcessNudChange(nud);
        }
        finally
        {
            _isSyncingFromProperty = false;
        }
    }

    private NumericUpDown? FindNudByName(string name)
    {
        return name switch
        {
            "NudFontSize" => NudFontSize,
            "NudScaleX" => NudScaleX,
            "NudScaleY" => NudScaleY,
            "NudRotation" => NudRotation,
            "NudSpacing" => NudSpacing,
            "NudPosX" => NudPosX,
            "NudPosY" => NudPosY,
            "NudPosZ" => NudPosZ,
            "NudEdge" => NudEdge,
            "NudShadow" => NudShadow,
            "NudShadowBlur" => NudShadowBlur,
            "NudOpacity" => NudOpacity,
            "NudFade" => NudFade,
            "NudAutoTime" => NudAutoTime,
            "NudTimeBase" => NudTimeBase,
            "NudScreenWidth" => NudScreenWidth,
            "NudScreenHeight" => NudScreenHeight,
            "NudMarginX" => NudMarginX,
            "NudMarginY" => NudMarginY,
            "NudEdge2" => NudEdge2,
            "NudGlow" => NudGlow,
            "NudGlowAlpha" => NudGlowAlpha,
            _ => null
        };
    }

    /// <summary>
    /// 动画属性插入按钮点击处理
    /// </summary>
    private void OnInsertAnimNudValue(object? sender, RoutedEventArgs e)
    {
        if (_isSyncingFromProperty) return;
        if (sender is not Button btn || btn.Tag is not string tag) return;

        NumericUpDown? nud = null;
        string commandPrefix = tag;
        switch (tag)
        {
            case "animtime":
                nud = _nudAnimationDuration;
                break;
            case "animblinds":
                nud = _nudAnimationBlindsCount;
                break;
            case "animblur":
                nud = _nudAnimationBlurStrength;
                break;
        }

        if (nud == null) return;

        _debounceTimer?.Stop();
        _lastChangedNud = null;

        _isSyncingFromProperty = true;
        try
        {
            ProcessNudChange(nud);
        }
        finally
        {
            _isSyncingFromProperty = false;
        }
    }

    // 复选框变化时，将对应命令同步插入/替换到脚本
    private void OnCheckBoxChanged(object? sender, RoutedEventArgs e)
    {
        if (_isSyncingFromProperty) return;
        _isSyncingFromProperty = true;

        try
        {
            if (sender is CheckBox chk)
            {
                if (chk == ChkVertical)
                    InsertOrReplaceCommand(chk.IsChecked == true ? ":tate" : ":tate_off");
                else if (chk == ChkBillboard)
                    InsertOrReplaceCommand(chk.IsChecked == true ? ":billboard" : ":billboard_off");
            }
        }
        finally
        {
            _isSyncingFromProperty = false;
        }
    }

    // 单选框变化时，将对应命令同步插入/替换到脚本
    private void OnPropertyValueChanged(object? sender, RoutedEventArgs e)
    {
        if (_isSyncingFromProperty) return;
        _isSyncingFromProperty = true;

        try
        {
            if (sender is RadioButton rdo)
            {
                // 只有被选中时才插入命令
                if (rdo.IsChecked != true) return;

                var name = rdo.Name;
                if (name == "RdoAlignLeft")
                    InsertOrReplaceCommand(":align_left", "align");
                else if (name == "RdoAlignCenter")
                    InsertOrReplaceCommand(":align_center", "align");
                else if (name == "RdoAlignRight")
                    InsertOrReplaceCommand(":align_right", "align");
                else if (name == "RdoValignTop")
                    InsertOrReplaceCommand(":valign_top", "valign");
                else if (name == "RdoValignCenter")
                    InsertOrReplaceCommand(":valign_center", "valign");
                else if (name == "RdoValignBottom")
                    InsertOrReplaceCommand(":valign_bottom", "valign");
            }
            else if (sender is ToggleButton tgl)
            {
                if (tgl == TglBold)
                    InsertOrReplaceCommand(tgl.IsChecked == true ? ":bold" : ":bold_off", "bold");
                else if (tgl == TglItalic)
                    InsertOrReplaceCommand(tgl.IsChecked == true ? ":italic" : ":italic_off", "italic");
                else if (tgl == TglUnderline)
                    InsertOrReplaceCommand(tgl.IsChecked == true ? ":underline" : ":underline_off", "underline");
                else if (tgl == TglStrike)
                    InsertOrReplaceCommand(tgl.IsChecked == true ? ":strikeout" : ":strikeout_off", "strikeout");
            }
        }
        finally
        {
            _isSyncingFromProperty = false;
        }
    }

    // 动画类型变化时，将对应命令同步插入/替换到脚本
    private void OnAnimationTypeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingFromProperty || _cmbAnimationType?.SelectedItem is not ComboBoxItem item)
            return;

        var value = NormalizeAnimationType(item.Tag?.ToString());
        InsertOrReplaceCommand($":anim {value}", "anim");
    }

    // 动画方向变化时，将对应命令同步插入/替换到脚本
    private void OnAnimationDirectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingFromProperty || _cmbAnimationDirection?.SelectedItem is not ComboBoxItem item)
            return;

        var value = NormalizeAnimationDirection(item.Tag?.ToString());
        InsertOrReplaceCommand($":animdir {value}", "animdir");
    }

    /// <summary>
    /// 在脚本中插入命令到当前光标所在行
    /// </summary>
    private void InsertOrReplaceCommand(string command, string? commandPrefix = null)
    {
        if (ScriptEditor == null) return;

        var family = commandPrefix ?? GetCommandFamily(command);
        if (!string.IsNullOrWhiteSpace(family))
        {
            var replaced = ScriptEditor.ReplaceNearestLineInCommandBlock(
                line => string.Equals(GetCommandFamily(line), family, StringComparison.OrdinalIgnoreCase),
                AddCommentToCommand(command));

            if (replaced)
            {
                EnsureEndCode();
                return;
            }
        }

        ScriptEditor.InsertAtCurrentLine(AddCommentToCommand(command));

        EnsureEndCode();
    }

    /// <summary>
    /// 为命令添加注释（在命令前插入注释行）
    /// </summary>
    private string AddCommentToCommand(string command)
    {
        var comment = GetCommandComment(command);
        if (string.IsNullOrEmpty(comment))
            return command;

        return $"// {comment}\n{command}";
    }

    /// <summary>
    /// 获取命令对应的注释文本
    /// </summary>
    private static string GetCommandComment(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return string.Empty;

        var trimmed = command.Trim();
        if (!trimmed.StartsWith(":", StringComparison.Ordinal))
            return string.Empty;

        var commandText = trimmed[1..];
        var separatorIndex = commandText.IndexOfAny(new[] { ' ', '\t' });
        var commandKey = separatorIndex >= 0 ? commandText[..separatorIndex] : commandText;
        commandKey = commandKey.Trim().ToLowerInvariant();

        return commandKey switch
        {
            "font" => LocalizationService.T("指定字体"),
            "size" => LocalizationService.T("字体大小"),
            "fontcolor" => LocalizationService.T("字体颜色"),
            "align_left" => LocalizationService.T("左对齐"),
            "align_center" => LocalizationService.T("水平居中"),
            "align_right" => LocalizationService.T("右对齐"),
            "valign_top" => LocalizationService.T("顶部对齐"),
            "valign_center" => LocalizationService.T("垂直居中"),
            "valign_bottom" => LocalizationService.T("底部对齐"),
            "screen" => LocalizationService.T("屏幕尺寸"),
            "pos" => LocalizationService.T("字幕位置"),
            "alpha" => LocalizationService.T("不透明度"),
            "edge" => LocalizationService.T("指定描边粗细"),
            "edgecolor" => LocalizationService.T("描边颜色"),
            "edge_round" => LocalizationService.T("将描边边角设为圆形"),
            "edge_round_off" => LocalizationService.T("将描边边角设为圆形"),
            "edge2" => LocalizationService.T("外层描边粗细"),
            "edgecolor2" => LocalizationService.T("外层描边颜色"),
            "edge3" => LocalizationService.T("第三层描边粗细"),
            "edgecolor3" => LocalizationService.T("第三层描边颜色"),
            "edge4" => LocalizationService.T("第四层描边粗细"),
            "edgecolor4" => LocalizationService.T("第四层描边颜色"),
            "glow" => LocalizationService.T("外发光半径"),
            "glowcolor" => LocalizationService.T("外发光颜色"),
            "glowalpha" => LocalizationService.T("外发光不透明度"),
            "edgegradient_on" => LocalizationService.T("启用描边渐变"),
            "edgegradient_off" => LocalizationService.T("关闭描边渐变"),
            "edgegradient" => LocalizationService.T("描边渐变颜色"),
            "textgradient_on" => LocalizationService.T("启用文字渐变"),
            "textgradient_off" => LocalizationService.T("关闭文字渐变"),
            "textgradient" => LocalizationService.T("文字渐变颜色"),
            "textgradientdir" => LocalizationService.T("文字渐变方向"),
            "shadow" => LocalizationService.T("阴影距离"),
            "shadowcolor" => LocalizationService.T("阴影颜色"),
            "shadowblur" => LocalizationService.T("阴影模糊"),
            "fade" => LocalizationService.T("淡入淡出"),
            "autotime" => LocalizationService.T("自动时长"),
            "timebase" => LocalizationService.T("时间基准"),
            "tate" => LocalizationService.T("竖排文字"),
            "tate_off" => LocalizationService.T("竖排文字"),
            "billboard" => LocalizationService.T("公告板模式"),
            "billboard_off" => LocalizationService.T("公告板模式"),
            "anim" => LocalizationService.T("动画方式"),
            "animtime" => LocalizationService.T("动画时间"),
            "animdir" => LocalizationService.T("擦除方向"),
            "animblinds" => LocalizationService.T("百叶数量"),
            "animblur" => LocalizationService.T("模糊强度"),
            "set" => LocalizationService.T("定义样式集合"),
            "get" => LocalizationService.T("调用样式集合"),
            "skip" => LocalizationService.T("跳过下一段文本"),
            "bold" => LocalizationService.T("粗体"),
            "bold_off" => LocalizationService.T("粗体"),
            "italic" => LocalizationService.T("斜体"),
            "italic_off" => LocalizationService.T("斜体"),
            "underline" => LocalizationService.T("下划线"),
            "underline_off" => LocalizationService.T("下划线"),
            "strikeout" => LocalizationService.T("删除线"),
            "strikeout_off" => LocalizationService.T("删除线"),
            "margin" => LocalizationService.T("间距"),
            "scalex" => LocalizationService.T("宽度"),
            "scaley" => LocalizationService.T("高度"),
            "rotate" => LocalizationService.T("旋转"),
            "spacing" => LocalizationService.T("间距"),
            "backcolor" => LocalizationService.T("背景"),
            _ => string.Empty
        };
    }

    // 从命令行提取命令家族，用于判断同类型命令的替换位置
    private static string? GetCommandFamily(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        var trimmed = line.Trim();
        if (!trimmed.StartsWith(":", StringComparison.Ordinal))
            return null;

        var commandText = trimmed[1..];
        var separatorIndex = commandText.IndexOfAny(new[] { ' ', '\t' });
        var commandKey = separatorIndex >= 0 ? commandText[..separatorIndex] : commandText;
        commandKey = commandKey.Trim().ToLowerInvariant();

        return commandKey switch
        {
            "align_left" or "align_center" or "align_right" => "align", // 对齐
            "valign_top" or "valign_center" or "valign_bottom" => "valign", // 垂直对齐
            "bold" or "bold_off" => "bold", // 加粗
            "italic" or "italic_off" => "italic", // 斜体
            "underline" or "underline_off" => "underline", // 下划线
            "strikeout" or "strikeout_off" => "strikeout", // 删除线
            "tate" or "tate_off" => "tate", // 倾斜
            "billboard" or "billboard_off" => "billboard", // 广告牌
            "edge_round" or "edge_round_off" => "edge_round", // 圆角
            "edgegradient_on" or "edgegradient_off" or "edgegradient" => "edgegradient", // 描边渐变
            "textgradient_on" or "textgradient_off" or "textgradient" => "textgradient", // 文字渐变
            "textgradientdir" => "textgradientdir", // 文字渐变方向
            "anim" or "animation" => "anim", // 动画
            "animdir" or "animation_direction" => "animdir", // 动画方向
            "animblinds" or "animation_blinds" => "animblinds", // 百叶窗动画
            "animblur" or "animation_blur" => "animblur", // 模糊动画
            "animtime" or "animation_time" => "animtime", // 动画时间
            _ => commandKey
        };
    }

    /// <summary>
    /// 确保脚本有:end结束标记
    /// </summary>
    private void EnsureEndCode()
    {
        if (ScriptEditor == null) return;

        var text = ScriptEditor.Text ?? string.Empty;
        if (!text.Contains(":end", StringComparison.OrdinalIgnoreCase))
        {
            ScriptEditor.AppendText("\n\n:end");
        }
    }

    /// <summary>
    /// 字体颜色点击 - 使用系统颜色选择器
    /// </summary>
    private async void OnFontColorClick(object? sender, PointerPressedEventArgs e)
    {
        var currentColor = FontColorPreview?.Background is SolidColorBrush sb ? sb.Color : Colors.White;
        var color = await PickColor(LocalizationService.T("选择字体颜色"), currentColor);
        if (color.HasValue && FontColorPreview != null && FontColorText != null)
        {
            FontColorPreview.Background = new SolidColorBrush(color.Value);
            FontColorText.Text = $"{color.Value.R},{color.Value.G},{color.Value.B}";
            InsertOrReplaceCommand($":fontcolor {color.Value.R},{color.Value.G},{color.Value.B}", "fontcolor");
        }
    }

    /// <summary>
    /// 描边颜色点击 - 智能检测层数，自动插入下一层颜色命令
    /// </summary>
    private async void OnStrokeColorClick(object? sender, PointerPressedEventArgs e)
    {
        var currentColor = StrokeColorPreview?.Background is SolidColorBrush sb ? sb.Color : Colors.Black;
        var color = await PickColor(LocalizationService.T("选择描边颜色"), currentColor);
        if (color.HasValue && StrokeColorPreview != null && StrokeColorText != null)
        {
            StrokeColorPreview.Background = new SolidColorBrush(color.Value);
            StrokeColorText.Text = $"{color.Value.R},{color.Value.G},{color.Value.B}";

            // 智能检测下一层
            var layer = GetNextEdgeColorLayer();
            var cmd = layer == 1 ? "edgecolor" : $"edgecolor{layer}";
            InsertOrReplaceCommand($":{cmd} {color.Value.R},{color.Value.G},{color.Value.B}", cmd);
        }
    }

    /// <summary>
    /// 阴影颜色点击 - 使用系统颜色选择器
    /// </summary>
    private async void OnShadowColorClick(object? sender, PointerPressedEventArgs e)
    {
        var currentColor = ShadowColorPreview?.Background is SolidColorBrush sb ? sb.Color : Color.FromRgb(0, 0, 100);
        var color = await PickColor(LocalizationService.T("选择阴影颜色"), currentColor);
        if (color.HasValue && ShadowColorPreview != null && ShadowColorText != null)
        {
            ShadowColorPreview.Background = new SolidColorBrush(color.Value);
            ShadowColorText.Text = $"{color.Value.R},{color.Value.G},{color.Value.B}";
            InsertOrReplaceCommand($":shadowcolor {color.Value.R},{color.Value.G},{color.Value.B}", "shadowcolor");
        }
    }

    /// <summary>
    /// 背景颜色点击 - 使用系统颜色选择器
    /// </summary>
    private async void OnBgColorClick(object? sender, PointerPressedEventArgs e)
    {
        var currentColor = BgColorPreview?.Background is SolidColorBrush sb ? sb.Color : Color.FromArgb(0, 255, 255, 255);
        var color = await PickColor(LocalizationService.T("选择背景颜色"), currentColor);
        if (color.HasValue && BgColorPreview != null && BgColorText != null)
        {
            BgColorPreview.Background = new SolidColorBrush(color.Value);
            BgColorText.Text = $"{color.Value.A},{color.Value.R},{color.Value.G},{color.Value.B}";
            InsertOrReplaceCommand($":backcolor {color.Value.R},{color.Value.G},{color.Value.B},{color.Value.A}", "backcolor");
        }
    }

    /// <summary>
    /// 高级效果颜色选择按钮点击（双色渐变、外发光、双层描边）。
    /// 通过 Button.Tag 区分具体要设置的颜色。
    /// </summary>
    private async void OnPickColor(object? sender, RoutedEventArgs e)
    {       if (_isSyncingFromProperty) return;
        if (sender is not Button btn || btn.Tag is not string tag) return;
        await PickAdvancedColorAsync(tag);
    }

    /// <summary>
    /// 高级效果颜色预览框点击（与按钮等效）。
    /// </summary>
    private async void OnColorPreviewClick(object? sender, PointerPressedEventArgs e)
    {
        if (_isSyncingFromProperty) return;
        if (sender is not Border border || border.Tag is not string tag) return;
        await PickAdvancedColorAsync(tag);
    }

    /// <summary>
    /// 根据颜色标签弹出颜色选择器并写入对应命令到脚本。
    /// </summary>
    private async Task PickAdvancedColorAsync(string tag)
    {
        Border? preview = tag switch
        {
            "edgecolor2" => Edge2ColorPreview,
            "glowcolor" => GlowColorPreview,
            "edgegradienttop" => EdgeGradientTopPreview,
            "edgegradientbottom" => EdgeGradientBottomPreview,
            "textgradienttop" => TextGradientTopPreview,
            "textgradientbottom" => TextGradientBottomPreview,
            _ => null
        };
        if (preview == null) return;

        var currentColor = preview.Background is SolidColorBrush sb ? sb.Color : Colors.White;
        var color = await PickColor(LocalizationService.T("选择颜色"), currentColor);
        if (!color.HasValue) return;

        preview.Background = new SolidColorBrush(color.Value);

        switch (tag)
        {
            case "edgecolor2":
                {
                    var layer = GetNextEdgeColorLayer();
                    var cmd = layer == 1 ? "edgecolor" : $"edgecolor{layer}";
                    InsertOrReplaceCommand($":{cmd} {color.Value.R},{color.Value.G},{color.Value.B}", cmd);
                }
                break;
            case "glowcolor":
                InsertOrReplaceCommand($":glowcolor {color.Value.R},{color.Value.G},{color.Value.B}", "glowcolor");
                break;
            case "edgegradienttop":
            case "edgegradientbottom":
                WriteEdgeGradientCommand();
                break;
            case "textgradienttop":
            case "textgradientbottom":
                WriteTextGradientCommand();
                break;
        }
    }

    /// <summary>
    /// 根据 UI 颜色预览生成 :edgegradient 命令。
    /// </summary>
    private void WriteEdgeGradientCommand()
    {
        var top = EdgeGradientTopPreview?.Background is SolidColorBrush t ? t.Color : Colors.Red;
        var bottom = EdgeGradientBottomPreview?.Background is SolidColorBrush b ? b.Color : Colors.Blue;
        InsertOrReplaceCommand($":edgegradient {top.R},{top.G},{top.B},{bottom.R},{bottom.G},{bottom.B}", "edgegradient");
    }

    /// <summary>
    /// 根据 UI 颜色预览生成 :textgradient 命令。
    /// </summary>
    private void WriteTextGradientCommand()
    {
        var top = TextGradientTopPreview?.Background is SolidColorBrush t ? t.Color : Colors.Red;
        var bottom = TextGradientBottomPreview?.Background is SolidColorBrush b ? b.Color : Colors.Blue;
        InsertOrReplaceCommand($":textgradient {top.R},{top.G},{top.B},{bottom.R},{bottom.G},{bottom.B}", "textgradient");
    }

    /// <summary>
    /// 高级效果的复选框变化处理（描边渐变、文字渐变开关）。
    /// </summary>
    private void OnAdvancedCheckBoxChanged(object? sender, RoutedEventArgs e)
    {
        if (_isSyncingFromProperty) return;
        if (sender is not CheckBox chk || chk.Tag is not string tag) return;

        _isSyncingFromProperty = true;
        try
        {
            var isOn = chk.IsChecked == true;
            switch (tag)
            {
                case "edgegradient":
                    InsertOrReplaceCommand(isOn ? ":edgegradient_on" : ":edgegradient_off", "edgegradient");
                    if (isOn) WriteEdgeGradientCommand();
                    break;
                case "textgradient":
                    InsertOrReplaceCommand(isOn ? ":textgradient_on" : ":textgradient_off", "textgradient");
                    if (isOn) WriteTextGradientCommand();
                    break;
            }
        }
        finally
        {
            _isSyncingFromProperty = false;
        }
    }

    /// <summary>
    /// 高级效果下拉框变化处理（文字渐变方向）。
    /// </summary>
    private void OnAdvancedComboBoxChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingFromProperty) return;
        if (sender is not ComboBox cmb || cmb.Tag is not string tag) return;
        if (cmb.SelectedItem == null) return;

        _isSyncingFromProperty = true;
        try
        {
            if (tag == "textgradientdir")
            {
                var dir = cmb.SelectedIndex == 1 ? "horizontal" : "vertical";
                InsertOrReplaceCommand($":textgradientdir {dir}", "textgradientdir");
            }
        }
        finally
        {
            _isSyncingFromProperty = false;
        }
    }

    private async Task<Color?> PickColor(string title, Color currentColor)
    {
        if (OperatingSystem.IsWindows())
        {
            var handle = TryGetPlatformHandle();
            if (handle != null)
            {
                return await Task.Run(() => NativeColorDialog.Show(handle.Handle, currentColor));
            }
        }

        var picker = new ColorPicker
        {
            IsAlphaEnabled = title.Contains("背景"),  // 背景
            IsColorPaletteVisible = true,
            Color = currentColor,
            Width = 280,
            Height = 350,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var dialog = new Window
        {
            Title = title,
            Width = 340,
            Height = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 46)),
            Content = new Border
            {
                Padding = new Thickness(16),
                Child = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White },
                        picker,
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Spacing = 8,
                            Children =
                            {
                                new Button { Content = "取消", MinWidth = 60 },  // 取消
                                new Button { Content = "确定", MinWidth = 60 }  // 确定
                            }
                        }
                    }
                }
            }
        };

        Color? result = null;

        if (dialog.Content is Border border && border.Child is StackPanel panel &&
            panel.Children[2] is StackPanel btnPanel)
        {
            if (btnPanel.Children[0] is Button cancelBtn)
                cancelBtn.Click += (_, _) => { result = null; dialog.Close(); };
            if (btnPanel.Children[1] is Button okBtn)
                okBtn.Click += (_, _) => { result = picker.Color; dialog.Close(); };
        }

        LocalizationService.Apply(dialog);
        await dialog.ShowDialog(this);

        return result;
    }

    // ========== 图片相关事件处理 ==========
    
    /// <summary>
    /// 浏览图片文件
    /// </summary>
    private async void OnBrowseImage(object? sender, RoutedEventArgs e)
    {
        var storage = GetTopLevel(this).StorageProvider;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationService.T("选择图片文件"),
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(LocalizationService.T("图片文件"))
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif","*.tga", "*.dds", "*.svg", "*.webp", "*.tiff", "*.avif" }
                },
                new FilePickerFileType(LocalizationService.T("所有文件"))
                {
                    Patterns = new[] { "*.*" }
                }
            }
        });
        
        if (files.Count > 0)
        {
            var path = files[0].Path.LocalPath;
            TxtImagePath.Text = path;

            // 自动预览
            try
            {
                var bitmap = new Bitmap(path);
                if (NudImageScaleX != null)
                {
                    // 设置合适的缩放比例，使图片适应预览区域
                    var maxDim = Math.Max(bitmap.PixelSize.Width, bitmap.PixelSize.Height);
                    var scale = Math.Min(640, 360) * 100.0 / maxDim;
                    NudImageScaleX.Value = (decimal)scale;
                    NudImageScaleY.Value = (decimal)scale;
                }
            }
            catch { }

            // 若当前已选中图片条目，则替换其图片路径并刷新预览
            if (_selectedImageEntry != null && _selectedImageEntry.IsImage)
            {
                var relativePath = GetRelativeImagePath(path);
                var oldPath = _selectedImageEntry.ImagePath;
                _selectedImageEntry.ImagePath = relativePath;

                UpdateImagePathInScript(_selectedImageEntry, relativePath);

                RenderPreviewFromSubtitle(_selectedImageEntry);
                SetStatus(LocalizationService.T("图片已更新"));
            }
        }
    }

    /// <summary>
    /// 更新脚本中指定图片条目的 :imagepath 行。
    /// </summary>
    private void UpdateImagePathInScript(SubtitleEntry entry, string newPath)
    {
        if (ScriptEditor == null || entry == null) return;

        var script = ScriptEditor.Text;
        if (string.IsNullOrEmpty(script)) return;

        var lines = script.Split('\n');
        int imagepathLineIndex = -1;

        if (entry.ImageIndex > 0)
        {
            int imageCount = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim().StartsWith(":imagepath "))
                {
                    imageCount++;
                    if (imageCount == entry.ImageIndex)
                    {
                        imagepathLineIndex = i;
                        break;
                    }
                }
            }
        }
        else
        {
            var targetPath = entry.ImagePath ?? "";
            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                if (trimmed.StartsWith(":imagepath "))
                {
                    imagepathLineIndex = i;
                    break;
                }
            }
        }

        if (imagepathLineIndex < 0) return;

        lines[imagepathLineIndex] = $":imagepath {newPath}";

        _isSyncingFromProperty = true;
        try
        {
            ScriptEditor.Text = string.Join("\n", lines);
        }
        finally
        {
            _isSyncingFromProperty = false;
        }
    }
    
    /// <summary>
    /// 插入图片命令到脚本
    /// </summary>
    private void OnInsertImage(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(TxtImagePath?.Text))
        {
            SetStatus(LocalizationService.T("请先选择图片文件"));
            return;
        }

        var absolutePath = TxtImagePath.Text;
        var relativePath = GetRelativeImagePath(absolutePath);
        var scaleX = (double)(NudImageScaleX?.Value ?? 100m);
        var scaleY = (double)(NudImageScaleY?.Value ?? 100m);
        var posX = (double)(NudImagePosX?.Value ?? 0m);
        var posY = (double)(NudImagePosY?.Value ?? 0m);
        var posZ = (double)(NudImagePosZ?.Value ?? 0m);
        var rotation = (double)(NudImageRotation?.Value ?? 0m);
        var alpha = (SliderImageAlpha?.Value ?? 100) / 100.0;

        var commands = new StringBuilder();
        commands.AppendLine($"// {LocalizationService.T("图片路径")}");
        commands.AppendLine($":imagepath {relativePath}");
        commands.AppendLine($"// {LocalizationService.T("图片缩放比例")}");
        commands.AppendLine($":imagescale {scaleX},{scaleY}");
        commands.AppendLine($"// {LocalizationService.T("图片位置")}");
        commands.AppendLine($":imagepos {posX},{posY},{posZ}");

        if (rotation != 0)
        {
            commands.AppendLine($"// {LocalizationService.T("图片旋转角度")}");
            commands.AppendLine($":imagerotation {rotation}");
        }

        if (alpha < 1.0)
        {
            commands.AppendLine($"// {LocalizationService.T("图片透明度")}");
            commands.AppendLine($":imagealpha {alpha:F2}");
        }

        ScriptEditor?.InsertAtCaret(commands.ToString());

        // 解析脚本同步字幕列表，查找并选中刚插入的图片条目
        ParseScriptToSubtitles();

        // 反向搜索：找到最后一个匹配路径的图片条目（即刚插入的）
        var insertedIndex = -1;
        for (int i = _subtitles.Count - 1; i >= 0; i--)
        {
            if (_subtitles[i].IsImage && _subtitles[i].ImagePath == relativePath)
            {
                insertedIndex = i;
                break;
            }
        }

        if (insertedIndex >= 0)
        {
            _selectedImageEntry = _subtitles[insertedIndex];
            if (SubtitleList != null)
                SubtitleList.SelectedIndex = insertedIndex;
            RenderPreviewFromSubtitle(_selectedImageEntry);
        }

        SetStatus(LocalizationService.T("图片命令已插入"));
    }
    
    private string GetRelativeImagePath(string absolutePath)
    {
        if (string.IsNullOrEmpty(_currentFilePath))
            return absolutePath;
        
        var scriptDir = Path.GetDirectoryName(_currentFilePath);
        if (string.IsNullOrEmpty(scriptDir))
            return absolutePath;
        
        var uri1 = new Uri(scriptDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
        var uri2 = new Uri(absolutePath);
        
        var relativeUri = uri1.MakeRelativeUri(uri2);
        return Uri.UnescapeDataString(relativeUri.ToString().Replace('/', Path.DirectorySeparatorChar));
    }
    
    private void OnImageScaleChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_isSyncingFromProperty) return;
        UpdateImageEntryProperty("scale");
    }
    
    private void OnImagePosChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_isSyncingFromProperty) return;
        UpdateImageEntryProperty("pos");
    }
    
    private void OnImageRotationChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_isSyncingFromProperty) return;
        UpdateImageEntryProperty("rotation");
    }
    
    private void OnImageAlphaChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isSyncingFromProperty) return;
        UpdateImageEntryProperty("alpha");
    }
    
    private void UpdateImageEntryProperty(string propertyName)
    {
        if (_selectedImageEntry == null) return;
        
        switch (propertyName)
        {
            case "scale":
                _selectedImageEntry.ImageScaleX = (double)(NudImageScaleX?.Value ?? 100m);
                _selectedImageEntry.ImageScaleY = (double)(NudImageScaleY?.Value ?? 100m);
                break;
            case "pos":
                _selectedImageEntry.PosX = (double)(NudImagePosX?.Value ?? 0m);
                _selectedImageEntry.PosY = (double)(NudImagePosY?.Value ?? 0m);
                _selectedImageEntry.PosZ = (double)(NudImagePosZ?.Value ?? 0m);
                break;
            case "rotation":
                _selectedImageEntry.ImageRotation = (double)(NudImageRotation?.Value ?? 0m);
                break;
            case "alpha":
                _selectedImageEntry.ImageAlpha = (SliderImageAlpha?.Value ?? 100) / 100.0;
                break;
        }
        
        UpdateImageCommandsInScript(_selectedImageEntry);
        RenderPreviewFromSubtitle(_selectedImageEntry);
    }
    
    private void UpdateImageCommandsInScript(SubtitleEntry entry)
    {
        if (ScriptEditor == null || entry == null) return;

        var script = ScriptEditor.Text;
        if (string.IsNullOrEmpty(script)) return;

        var lines = script.Split('\n');
        int imagepathLineIndex = -1;

        // 按 ImageIndex 定位正确的 :imagepath 块
        if (entry.ImageIndex > 0)
        {
            int imageCount = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim().StartsWith(":imagepath "))
                {
                    imageCount++;
                    if (imageCount == entry.ImageIndex)
                    {
                        imagepathLineIndex = i;
                        break;
                    }
                }
            }
        }
        else
        {
            // 回退：按路径匹配
            var targetPath = entry.ImagePath ?? "";
            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                if (trimmed.StartsWith(":imagepath ") && trimmed.Substring(11).Trim() == targetPath)
                {
                    imagepathLineIndex = i;
                    break;
                }
            }
        }

        if (imagepathLineIndex < 0) return;

        // 更新后续的图片命令（跳过注释行，遇到非图片命令时退出）
        for (int i = imagepathLineIndex + 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            if (line.StartsWith("//"))
                continue;

            if (!line.StartsWith(":image"))
                break;

            if (line.StartsWith(":imagescale "))
                lines[i] = $":imagescale {entry.ImageScaleX},{entry.ImageScaleY}";
            else if (line.StartsWith(":imagepos "))
                lines[i] = $":imagepos {entry.PosX},{entry.PosY},{entry.PosZ}";
            else if (line.StartsWith(":imagerotation "))
                lines[i] = $":imagerotation {entry.ImageRotation}";
            else if (line.StartsWith(":imagealpha "))
                lines[i] = $":imagealpha {entry.ImageAlpha:F2}";
        }

        _isSyncingFromProperty = true;
        try
        {
            ScriptEditor.Text = string.Join("\n", lines);
        }
        finally
        {
            _isSyncingFromProperty = false;
        }
    }
    
    #endregion
}
