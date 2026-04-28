using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace MMTGui;

/// <summary>
/// MainWindow 预览与缩放操作分部类。
/// 负责播放字幕预览、静态文字渲染，以及预览区域的缩放控制。
/// </summary>
public partial class MainWindow
{
    #region 预览操作

    /// <summary>
    /// 开始预览播放，根据字幕时间区间驱动预览区域刷新。
    /// </summary>
    private void OnPreview(object? sender, RoutedEventArgs e)
    {
        if (_subtitles.Count == 0)
        {
            SetStatus("没有可预览的字幕");  // 没有可预览的字幕
            return;
        }

        if (_previewTimer != null)
        {
            _previewTimer.Stop();
            _previewTimer.Tick -= OnPreviewTick;
            _previewTimer = null;
        }

        int startIndex = 0;
        if (SubtitleList?.SelectedItem is ListBoxItem lbi && lbi.Content is Grid grid)
        {
            if (grid.Children.Count > 0 && grid.Children[0] is TextBlock idxBlock
                && int.TryParse(idxBlock.Text, out var selIdx) && selIdx > 0 && selIdx <= _subtitles.Count)
            {
                startIndex = selIdx - 1;
            }
        }

        _isPreviewing = true;
        _previewIndex = startIndex;
        _previewStartTime = _subtitles[startIndex].StartTime;
        _previewTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _previewTimer.Tick += OnPreviewTick;
        _previewTimer.Start();
        SetStatus("预览中...");  // 预览中...
    }

    /// <summary>
    /// 预览计时器回调。
    /// 根据当前播放时间选择字幕并刷新预览显示。
    /// </summary>
    private void OnPreviewTick(object? sender, EventArgs e)
    {
        if (!_isPreviewing) return;
        _previewStartTime += 0.033;

        SubtitleEntry? currentSub = null;
        for (int i = 0; i < _subtitles.Count; i++)
        {
            if (_previewStartTime >= _subtitles[i].StartTime && _previewStartTime <= _subtitles[i].EndTime)
            {
                currentSub = _subtitles[i];
                break;
            }
        }

        if (currentSub != null)
        {
            RenderPreviewFromSubtitle(currentSub, _previewStartTime);

            if (PreviewTime != null)
                {
                    var timeText = $"{FormatTime(_previewStartTime)} / {FormatTime(currentSub.EndTime)}";
                    PreviewTime.Text = timeText;
                    if (PreviewTimeShadow != null)
                        PreviewTimeShadow.Text = timeText;
                }

            if (StatusTime != null)
                StatusTime.Text = $"{FormatTime(_previewStartTime)} | {GetAnimationStatusText(currentSub)}";
        }
        else
        {
            if (_subtitles.Count > 0 && _previewStartTime > _subtitles[^1].EndTime)
            {
                OnStopPreview(this, new RoutedEventArgs());
                return;
            }

            ClearPreviewCanvas();

            if (PreviewTime != null)
            {
                PreviewTime.Text = FormatTime(_previewStartTime);
                if (PreviewTimeShadow != null)
                    PreviewTimeShadow.Text = FormatTime(_previewStartTime);
            }
        }

        if (StatusTime != null)
            StatusTime.Text = FormatTime(_previewStartTime);
    }

    /// <summary>
    /// 使用当前属性面板参数，将一段纯文本直接渲染到预览画布。
    /// 通常用于未选择具体字幕时的即时预览。
    /// </summary>
    private void RenderPreviewText(string text)
    {
        if (PreviewCanvas == null) return;
        PreviewCanvas.Children.Clear();

        var screenWidth = (double)(NudScreenWidth?.Value ?? 1280);
        var screenHeight = (double)(NudScreenHeight?.Value ?? 720);
        var previewSize = UpdatePreviewFrame(screenWidth, screenHeight);
        var canvasW = previewSize.Width;
        var canvasH = previewSize.Height;
        if (canvasW <= 0 || canvasH <= 0) return;

        var scale = canvasH / screenHeight;

        var fontSize = (double)(NudFontSize?.Value ?? 13);
        var fsizepx = screenHeight * fontSize / 200.0;

        var fontFamily = "Yu Gothic";
        if (CmbFontName?.SelectedItem is ComboBoxItem cbi && cbi.Content is string fn)
            fontFamily = fn;

        var typeface = new Typeface(fontFamily,
            TglItalic?.IsChecked == true ? FontStyle.Italic : FontStyle.Normal,
            TglBold?.IsChecked == true ? FontWeight.Bold : FontWeight.Normal);

        var fontColor = Colors.White;
        if (FontColorPreview?.Background is SolidColorBrush fcb)
            fontColor = fcb.Color;

        var strokeColor = Colors.Black;
        if (StrokeColorPreview?.Background is SolidColorBrush scb)
            strokeColor = scb.Color;

        var shadowColor = Color.FromRgb(0, 0, 100);
        if (ShadowColorPreview?.Background is SolidColorBrush shcb)
            shadowColor = shcb.Color;

        var bgColor = Colors.Transparent;
        if (BgColorPreview?.Background is SolidColorBrush bgcb)
            bgColor = bgcb.Color;

        var edgeWidth = (double)(NudEdge?.Value ?? 8);
        var shadowDist = (double)(NudShadow?.Value ?? 0);
        var shadowBlur = (double)(NudShadowBlur?.Value ?? 0);
        var marginX = (double)(NudMarginX?.Value ?? 5);
        var marginY = (double)(NudMarginY?.Value ?? 5);

        var hAlign = RdoAlignLeft?.IsChecked == true ? 0
            : RdoAlignCenter?.IsChecked == true ? 1 : 2;
        var vAlign = RdoValignTop?.IsChecked == true ? 0
            : RdoValignCenter?.IsChecked == true ? 1 : 2;

        var scaledFontSize = fsizepx * scale;

        var displayText = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            scaledFontSize,
            Brushes.White);

        var textW = displayText.Width;
        var textH = displayText.Height;

        double x = 0, y = 0;
        if (hAlign == 0) x = fsizepx * marginX / 100.0 * scale;
        else if (hAlign == 1) x = (canvasW - textW) / 2;
        else x = canvasW - textW - fsizepx * marginX / 100.0 * scale;

        if (vAlign == 0) y = fsizepx * marginY / 100.0 * scale;
        else if (vAlign == 1) y = (canvasH - textH) / 2;
        else y = canvasH - textH - fsizepx * marginY / 100.0 * scale;

        var pixelW = (int)canvasW;
        var pixelH = (int)canvasH;
        if (pixelW < 1) pixelW = 1;
        if (pixelH < 1) pixelH = 1;

        var renderBitmap = new RenderTargetBitmap(
            new PixelSize(pixelW, pixelH),
            new Vector(96, 96));

        using (var ctx = renderBitmap.CreateDrawingContext())
        {
            if (bgColor.A > 0)
            {
                ctx.DrawRectangle(new SolidColorBrush(bgColor), null,
                    new Rect(0, 0, pixelW, pixelH));
            }

            if (shadowDist > 0)
            {
                var shadowOffset = fsizepx * shadowDist / 100.0 * scale;
                var shadowFormatted = new FormattedText(
                    text,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    scaledFontSize,
                    new SolidColorBrush(shadowColor));

                if (hAlign == 2) shadowFormatted.TextAlignment = TextAlignment.Right;
                else if (hAlign == 1) shadowFormatted.TextAlignment = TextAlignment.Center;

                ctx.DrawText(shadowFormatted, new Point(x + shadowOffset, y + shadowOffset));
            }

            if (edgeWidth > 0)
            {
                var strokeFormatted = new FormattedText(
                    text,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    scaledFontSize,
                    Brushes.White);

                if (hAlign == 2) strokeFormatted.TextAlignment = TextAlignment.Right;
                else if (hAlign == 1) strokeFormatted.TextAlignment = TextAlignment.Center;

                var geo = strokeFormatted.BuildGeometry(new Point(x, y));
                if (geo != null)
                {
                    var penWidth = fsizepx * edgeWidth * 2 / 100.0 * scale;
                    var pen = new Pen(new SolidColorBrush(strokeColor), penWidth)
                    {
                        LineJoin = PenLineJoin.Round
                    };
                    ctx.DrawGeometry(null, pen, geo);
                }
            }

            var mainFormatted = new FormattedText(
                text,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                scaledFontSize,
                new SolidColorBrush(fontColor));

            if (hAlign == 2) mainFormatted.TextAlignment = TextAlignment.Right;
            else if (hAlign == 1) mainFormatted.TextAlignment = TextAlignment.Center;

            ctx.DrawText(mainFormatted, new Point(x, y));
        }

        var img = new Image
        {
            Source = renderBitmap,
            Width = canvasW,
            Height = canvasH,
        };

        Canvas.SetLeft(img, 0);
        Canvas.SetTop(img, 0);
        PreviewCanvas.Children.Add(img);
    }

    /// <summary>
    /// 清空预览画布内容。
    /// </summary>
    private void ClearPreviewCanvas()
    {
        PreviewCanvas?.Children.Clear();
    }

    /// <summary>
    /// 停止预览播放并清空预览状态。
    /// </summary>
    private void OnStopPreview(object? sender, RoutedEventArgs e)
    {
        _isPreviewing = false;

        if (_previewTimer != null)
        {
            _previewTimer.Stop();
            _previewTimer.Tick -= OnPreviewTick;
            _previewTimer = null;
        }
        _previewIndex = -1;
        _previewStartTime = 0;

        ClearPreviewCanvas();

        if (StatusTime != null)
            StatusTime.Text = "";
        if (PreviewTime != null)
            {
                PreviewTime.Text = "";
                if (PreviewTimeShadow != null)
                    PreviewTimeShadow.Text = "";
            }

        SetStatus("预览已停止");  // 预览已停止
    }

    #endregion

    #region 缩放操作

    /// <summary>
    /// 将预览缩放恢复到适合窗口的默认倍率。
    /// </summary>
    private void OnZoomFit(object? sender, RoutedEventArgs e)
    {
        _zoomLevel = 1.0;
        ApplyZoom();
    }

    /// <summary>
    /// 放大预览画面。
    /// </summary>
    private void OnZoomIn(object? sender, RoutedEventArgs e)
    {
        _zoomLevel = Math.Min(_zoomLevel + 0.1, 3.0);
        ApplyZoom();
    }

    /// <summary>
    /// 缩小预览画面。
    /// </summary>
    private void OnZoomOut(object? sender, RoutedEventArgs e)
    {
        _zoomLevel = Math.Max(_zoomLevel - 0.1, 0.25);
        ApplyZoom();
    }

    /// <summary>
    /// 通过鼠标滚轮调整预览缩放比例。
    /// </summary>
    private void OnPreviewMouseWheel(object? sender, PointerWheelEventArgs e)
    {
        var delta = e.Delta.Y;
        if (delta > 0)
            _zoomLevel = Math.Min(_zoomLevel + 0.1, 3.0);
        else if (delta < 0)
            _zoomLevel = Math.Max(_zoomLevel - 0.1, 0.25);

        ApplyZoom();
        e.Handled = true;
    }

    /// <summary>
    /// 应用当前缩放比例，并在必要时重绘预览内容。
    /// </summary>
    private void ApplyZoom()
    {
        UpdatePreviewFrame((double)(NudScreenWidth?.Value ?? 1280), (double)(NudScreenHeight?.Value ?? 720));

        if (TxtZoom != null)
            TxtZoom.Text = $"{(_zoomLevel * 100):F0}%";

        if (_isPreviewing || PreviewCanvas?.Children.Count > 0)
        {
            var selectedSub = SubtitleList?.SelectedIndex >= 0 && SubtitleList.SelectedIndex < _subtitles.Count
                ? _subtitles[SubtitleList.SelectedIndex]
                : null;
            if (selectedSub != null)
                RenderPreviewFromSubtitle(selectedSub, _isPreviewing ? _previewStartTime : null);
        }
    }

    #endregion
}
