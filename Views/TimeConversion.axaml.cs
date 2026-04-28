using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MMTGui.Localization;
using SukiUI.Controls;

namespace MMTGui;

/// <summary>
/// 时间换算窗口代码后置。
/// 用于在标准时间码、秒数与帧表达式之间做双向换算。
/// </summary>
public partial class TimeConversion : SukiWindow
{
    /// <summary>
    /// 标记本次联动更新由哪个输入源触发。
    /// </summary>
    private enum EditSource
    {
        TimeCode,
        FrameValue
    }

    /// <summary>是否正在执行联动更新。</summary>
    private bool _isUpdating;
    /// <summary>最近一次成功解析出的秒数。</summary>
    private double _lastSeconds = 60.0;

    /// <summary>
    /// 初始化时间换算窗口。
    /// </summary>
    public TimeConversion()
    {
        InitializeComponent();
        UpdateFromSeconds(_lastSeconds);
        LocalizationService.RegisterWindow(this, () => UpdateFromSeconds(_lastSeconds));
    }

    /// <summary>
    /// 当时间码文本变化时，只更新提示，不联动更新其他字段。
    /// </summary>
    private void OnTimeCodeChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdating)
            return;

        if (TryParseTimeCode(TxtTimeCode?.Text, out var seconds))
        {
            UpdateHint(LocalizationService.F("秒数：{0}s", seconds.ToString("0.###")));
        }
        else
        {
            UpdateHint(LocalizationService.T("正在输入时间码..."));
        }
    }

    /// <summary>
    /// 当帧值文本变化时，只更新提示，不联动更新其他字段。
    /// </summary>
    private void OnFrameValueChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdating)
            return;

        if (TryParseFrameExpression(TxtFrameValue?.Text, out var seconds))
        {
            UpdateHint(LocalizationService.F("秒数：{0}s", seconds.ToString("0.###")));
        }
        else
        {
            UpdateHint(LocalizationService.T("正在输入时间帧..."));
        }
    }

    /// <summary>
    /// 时间码输入框失去焦点时，更新联动字段。
    /// </summary>
    private void OnTimeCodeLostFocus(object? sender, RoutedEventArgs e)
    {
        if (_isUpdating)
            return;

        if (TryParseTimeCode(TxtTimeCode?.Text, out var seconds))
        {
            _lastSeconds = seconds;
            UpdateLinkedFields(EditSource.TimeCode, seconds);
        }
        else
        {
            UpdateFromSeconds(_lastSeconds);
        }
    }

    /// <summary>
    /// 帧值输入框失去焦点时，更新联动字段。
    /// </summary>
    private void OnFrameValueLostFocus(object? sender, RoutedEventArgs e)
    {
        if (_isUpdating)
            return;

        if (TryParseFrameExpression(TxtFrameValue?.Text, out var seconds))
        {
            _lastSeconds = seconds;
            UpdateLinkedFields(EditSource.FrameValue, seconds);
        }
        else
        {
            UpdateFromSeconds(_lastSeconds);
        }
    }

    /// <summary>
    /// 时间码输入框按Enter键时，更新联动字段。
    /// </summary>
    private void OnTimeCodeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (TryParseTimeCode(TxtTimeCode?.Text, out var seconds))
            {
                _lastSeconds = seconds;
                UpdateLinkedFields(EditSource.TimeCode, seconds);
            }
            else
            {
                UpdateFromSeconds(_lastSeconds);
            }
            e.Handled = true;
        }
    }

    /// <summary>
    /// 帧值输入框按Enter键时，更新联动字段。
    /// </summary>
    private void OnFrameValueKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (TryParseFrameExpression(TxtFrameValue?.Text, out var seconds))
            {
                _lastSeconds = seconds;
                UpdateLinkedFields(EditSource.FrameValue, seconds);
            }
            else
            {
                UpdateFromSeconds(_lastSeconds);
            }
            e.Handled = true;
        }
    }

    /// <summary>
    /// 使用给定秒数完整刷新所有显示字段。
    /// </summary>
    private void UpdateFromSeconds(double seconds)
    {
        _lastSeconds = Math.Max(0, seconds);

        _isUpdating = true;
        try
        {
            if (TxtTimeCode != null)
                TxtTimeCode.Text = FormatTimeCode(_lastSeconds);

            if (TxtFrameValue != null)
                TxtFrameValue.Text = $"{_lastSeconds * 30.0:0.##}F";

            UpdateHint(LocalizationService.F("秒数：{0}s", _lastSeconds.ToString("0.###")));
        }
        finally
        {
            _isUpdating = false;
        }
    }

    /// <summary>
    /// 保持当前输入源不变，仅刷新联动的另一组字段。
    /// </summary>
    private void UpdateLinkedFields(EditSource source, double seconds)
    {
        _isUpdating = true;
        try
        {
            if (source != EditSource.TimeCode && TxtTimeCode != null)
                TxtTimeCode.Text = FormatTimeCode(seconds);

            if (source != EditSource.FrameValue && TxtFrameValue != null)
                TxtFrameValue.Text = $"{seconds * 30.0:0.##}F";

            UpdateHint(LocalizationService.F("秒数：{0}s", seconds.ToString("0.###")));
        }
        finally
        {
            _isUpdating = false;
        }
    }

    /// <summary>
    /// 更新底部秒数提示文本。
    /// </summary>
    private void UpdateHint(string text)
    {
        if (TxtSecondsHint != null)
            TxtSecondsHint.Text = text;
    }

    /// <summary>
    /// 解析帧表达式、纯秒数或 MMT 风格时间表达式。
    /// </summary>
    private static bool TryParseFrameExpression(string? text, out double seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var value = text.Trim();
        if (value.StartsWith("@", StringComparison.Ordinal))
        {
            value = value[1..];
            var separators = new[] { ',', '-', '~' };
            var splitIndex = value.IndexOfAny(separators);
            if (splitIndex >= 0)
                value = value[..splitIndex];
        }

        value = value.Trim();
        if (value.EndsWith("F", StringComparison.OrdinalIgnoreCase))
        {
            if (double.TryParse(value[..^1], out var frames))
            {
                seconds = frames / 30.0;
                return true;
            }

            return false;
        }

        if (double.TryParse(value, out var numeric))
        {
            seconds = numeric;
            return true;
        }

        return TryParseTimeCode(value, out seconds);
    }

    /// <summary>
    /// 解析标准时间码。
    /// </summary>
    private static bool TryParseTimeCode(string? text, out double seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var value = text.Trim();
        if (double.TryParse(value, out var numeric))
        {
            seconds = numeric;
            return true;
        }

        var parts = value.Split(':');
        if (parts.Length < 2 || parts.Length > 3)
            return false;

        int hours = 0;
        int minutes;
        string secText;

        if (parts.Length == 3)
        {
            if (!int.TryParse(parts[0], out hours))
                return false;
            if (!int.TryParse(parts[1], out minutes))
                return false;
            secText = parts[2];
        }
        else
        {
            if (!int.TryParse(parts[0], out minutes))
                return false;
            secText = parts[1];
        }

        var secParts = secText.Split('.', 2);
        if (!int.TryParse(secParts[0], out var sec))
            return false;

        double fraction = 0;
        if (secParts.Length > 1)
        {
            var fractionText = secParts[1].Trim();
            if (fractionText.Length > 3)
                fractionText = fractionText[..3];
            while (fractionText.Length < 3)
                fractionText += "0";

            if (!int.TryParse(fractionText, out var ms))
                return false;

            fraction = ms / 1000.0;
        }

        seconds = hours * 3600 + minutes * 60 + sec + fraction;
        return true;
    }

    /// <summary>
    /// 将秒数格式化为标准时间码文本。
    /// </summary>
    private static string FormatTimeCode(double seconds)
    {
        if (seconds < 0)
            seconds = 0;

        var time = TimeSpan.FromSeconds(seconds);
        return $"{(int)time.TotalHours:D2}:{time.Minutes:D2}:{time.Seconds:D2}.{time.Milliseconds:D3}";
    }

    /// <summary>
    /// 计算按钮
    /// </summary>
    private void OnCalculate(object? sender, RoutedEventArgs e)
    {
        if (_isUpdating) return;

        if (TryParseTimeCode(TxtTimeCode?.Text, out var tcSeconds))
        {
            _lastSeconds = tcSeconds;
            UpdateLinkedFields(EditSource.TimeCode, tcSeconds);
            return;
        }

        if (TryParseFrameExpression(TxtFrameValue?.Text, out var fvSeconds))
        {
            _lastSeconds = fvSeconds;
            UpdateLinkedFields(EditSource.FrameValue, fvSeconds);
            return;
        }

        UpdateFromSeconds(_lastSeconds);
    }

    /// <summary>
    /// 关闭时间换算窗口。
    /// </summary>
    private void OnClose(object? sender, RoutedEventArgs e)
    {
        Close();
    }


    // ========= END ==========
}
