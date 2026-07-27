using System;
using System.Collections.Generic;
using System.IO;
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

    private TransformBoxManager? _transformBoxManager; // 图片缩放管理器
    private SubtitleEntry? _selectedImageEntry; // 选中的图片字幕条目
    private readonly Dictionary<string, Bitmap> _imageBitmapCache = new(StringComparer.OrdinalIgnoreCase); // 按路径缓存的图片位图

    /// <summary>
    /// 根据图片路径获取或创建 Bitmap，使用缓存避免重复加载和误释放。
    /// </summary>
    private Bitmap GetOrCreateImageBitmap(string imagePath)
    {
        var key = Path.GetFullPath(imagePath);
        if (!_imageBitmapCache.TryGetValue(key, out var bitmap))
        {
            bitmap = new Bitmap(imagePath);
            _imageBitmapCache[key] = bitmap;
        }
        return bitmap;
    }

    /// <summary>
    /// 清空图片位图缓存，在切换文件或关闭项目时调用。
    /// </summary>
    private void ClearImageBitmapCache()
    {
        foreach (var kv in _imageBitmapCache)
        {
            try { kv.Value.Dispose(); } catch { }
        }
        _imageBitmapCache.Clear();
    }

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
        // 音频同步
        if (_audioPlayer?.IsLoaded == true)
        {
            var subStart = TimeSpan.FromSeconds(_subtitles[startIndex].StartTime);
            if (Math.Abs((_audioPlayer.CurrentTime - subStart).TotalSeconds) > 3)
            {
                _audioPlayer.Seek(subStart);
            }
            _audioPlayer.Play();
            _audioProgressTimer?.Start();
            UpdateAudioPlayIcon();
        }

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

        if (_audioPlayer?.IsPlaying == true)
        {
            _previewStartTime = _audioPlayer.CurrentTime.TotalSeconds;
        }
        else
        {
            _previewStartTime += 0.033;
        }

        RenderPreviewAtTime(_previewStartTime);
    }

    /// <summary>
    /// 在指定时间点渲染预览画布。
    /// 由 OnPreviewTick 和音频拖动跳转共用。
    /// </summary>
    private void RenderPreviewAtTime(double currentTime)
    {
        var activeSubs = new List<SubtitleEntry>();
        for (int i = 0; i < _subtitles.Count; i++)
        {
            if (currentTime >= _subtitles[i].StartTime && currentTime <= _subtitles[i].EndTime)
            {
                activeSubs.Add(_subtitles[i]);
            }
        }

        if (activeSubs.Count > 0)
        {
            RenderPreviewFromSubtitles(activeSubs, currentTime);

            var lastSub = activeSubs[^1];
            if (PreviewTime != null)
            {
                var timeText = $"{FormatTime(currentTime)} / {FormatTime(lastSub.EndTime)}";
                PreviewTime.Text = timeText;
                if (PreviewTimeShadow != null)
                    PreviewTimeShadow.Text = timeText;
            }

            if (StatusTime != null)
                StatusTime.Text = $"{FormatTime(currentTime)} | {GetAnimationStatusText(lastSub)}";
        }
        else
        {
            if (_subtitles.Count > 0 && currentTime > _subtitles[^1].EndTime)
            {
                OnStopPreview(this, new RoutedEventArgs());
                return;
            }

            ClearPreviewCanvas();

            if (PreviewTime != null)
            {
                PreviewTime.Text = FormatTime(currentTime);
                if (PreviewTimeShadow != null)
                    PreviewTimeShadow.Text = FormatTime(currentTime);
            }
        }

        if (StatusTime != null)
            StatusTime.Text = FormatTime(currentTime);
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

        // 高级效果参数
        var edge2Width = (double?)(NudEdge2?.Value) ?? 0;
        var edge2Color = Edge2ColorPreview?.Background is SolidColorBrush e2b ? e2b.Color : Colors.White;
        var edge3Width = 0.0; // TODO: 连接 UI 控件
        var edge3Color = Color.FromRgb(128, 128, 128);
        var edge4Width = 0.0; // TODO: 连接 UI 控件
        var edge4Color = Color.FromRgb(64, 64, 64);
        var glowRadius = (double?)(NudGlow?.Value) ?? 0;
        var glowColor = GlowColorPreview?.Background is SolidColorBrush gcb ? gcb.Color : Color.FromRgb(255, 200, 100);
        var glowAlpha = (double?)(NudGlowAlpha?.Value) ?? 0.8;
        var edgeGradientEnabled = ChkEdgeGradient?.IsChecked == true;
        var edgeGradientTop = EdgeGradientTopPreview?.Background is SolidColorBrush egt ? egt.Color : Colors.Red;
        var edgeGradientBottom = EdgeGradientBottomPreview?.Background is SolidColorBrush egb ? egb.Color : Colors.Blue;
        var textGradientEnabled = ChkTextGradient?.IsChecked == true;
        var textGradientTop = TextGradientTopPreview?.Background is SolidColorBrush tgt ? tgt.Color : Colors.Red;
        var textGradientBottom = TextGradientBottomPreview?.Background is SolidColorBrush tgb ? tgb.Color : Colors.Blue;
        var textGradientDir = CmbTextGradientDir?.SelectedIndex == 1 ? "horizontal" : "vertical";

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

            // === 正确渲染顺序：从外到内 ===
            // 1. 外发光（最底层晕染，12层叠加模拟平滑发光）
            if (glowRadius > 0)
            {
                var glowFormatted = new FormattedText(
                    text,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    scaledFontSize,
                    Brushes.White);

                if (hAlign == 2) glowFormatted.TextAlignment = TextAlignment.Right;
                else if (hAlign == 1) glowFormatted.TextAlignment = TextAlignment.Center;

                var glowGeo = glowFormatted.BuildGeometry(new Point(x, y));
                if (glowGeo != null)
                {
                    var baseAlpha = glowAlpha * 255.0;
                    var glowRad = fsizepx * glowRadius / 100.0 * scale;
                    // 12层叠加，指数衰减模拟 PS 外发光
                    for (int i = 12; i >= 1; i--)
                    {
                        var t = (double)i / 12.0;
                        var layerAlpha = (byte)(baseAlpha * Math.Pow(0.75, i));
                        var layerWidth = glowRad * (1.0 + t * 1.5);
                        var layerColor = Color.FromArgb(layerAlpha, glowColor.R, glowColor.G, glowColor.B);
                        var layerPen = new Pen(new SolidColorBrush(layerColor), layerWidth)
                        {
                            LineJoin = PenLineJoin.Round
                        };
                        ctx.DrawGeometry(null, layerPen, glowGeo);
                    }
                }
            }

            // 2. 第四层描边（最外层）
            if (edge4Width > 0)
            {
                var e4Formatted = new FormattedText(
                    text,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    scaledFontSize,
                    Brushes.White);

                if (hAlign == 2) e4Formatted.TextAlignment = TextAlignment.Right;
                else if (hAlign == 1) e4Formatted.TextAlignment = TextAlignment.Center;

                var e4Geo = e4Formatted.BuildGeometry(new Point(x, y));
                if (e4Geo != null)
                {
                    var e4PenWidth = fsizepx * edge4Width * 2 / 100.0 * scale;
                    var e4Pen = new Pen(new SolidColorBrush(edge4Color), e4PenWidth)
                    {
                        LineJoin = PenLineJoin.Round
                    };
                    ctx.DrawGeometry(null, e4Pen, e4Geo);
                }
            }

            // 3. 第三层描边
            if (edge3Width > 0)
            {
                var e3Formatted = new FormattedText(
                    text,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    scaledFontSize,
                    Brushes.White);

                if (hAlign == 2) e3Formatted.TextAlignment = TextAlignment.Right;
                else if (hAlign == 1) e3Formatted.TextAlignment = TextAlignment.Center;

                var e3Geo = e3Formatted.BuildGeometry(new Point(x, y));
                if (e3Geo != null)
                {
                    var e3PenWidth = fsizepx * edge3Width * 2 / 100.0 * scale;
                    var e3Pen = new Pen(new SolidColorBrush(edge3Color), e3PenWidth)
                    {
                        LineJoin = PenLineJoin.Round
                    };
                    ctx.DrawGeometry(null, e3Pen, e3Geo);
                }
            }

            // 4. 外层描边（edge2）
            if (edge2Width > 0)
            {
                var outerFormatted = new FormattedText(
                    text,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    scaledFontSize,
                    Brushes.White);

                if (hAlign == 2) outerFormatted.TextAlignment = TextAlignment.Right;
                else if (hAlign == 1) outerFormatted.TextAlignment = TextAlignment.Center;

                var outerGeo = outerFormatted.BuildGeometry(new Point(x, y));
                if (outerGeo != null)
                {
                    var outerPenWidth = fsizepx * edge2Width * 2 / 100.0 * scale;
                    var outerPen = new Pen(new SolidColorBrush(edge2Color), outerPenWidth)
                    {
                        LineJoin = PenLineJoin.Round
                    };
                    ctx.DrawGeometry(null, outerPen, outerGeo);
                }
            }

            // 5. 内层描边（edge，最细）
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
                    var edgeBrush = edgeGradientEnabled
                        ? CreatePreviewGradientBrush(edgeGradientTop, edgeGradientBottom, textGradientDir)
                        : (IBrush)new SolidColorBrush(strokeColor);
                    var pen = new Pen(edgeBrush, penWidth)
                    {
                        LineJoin = PenLineJoin.Round
                    };
                    ctx.DrawGeometry(null, pen, geo);
                }
            }

            // 4. 文字本体（最上层）
            IBrush mainBrush = textGradientEnabled
                ? CreatePreviewGradientBrush(textGradientTop, textGradientBottom, textGradientDir)
                : new SolidColorBrush(fontColor);
            var mainFormatted = new FormattedText(
                text,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                scaledFontSize,
                mainBrush);

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
    /// 创建预览用的渐变画刷（用于描边渐变和文字渐变）。
    /// </summary>
    private static IBrush CreatePreviewGradientBrush(Color top, Color bottom, string direction)
    {
        if (string.IsNullOrEmpty(direction))
            direction = "vertical";

        if (direction == "horizontal")
        {
            return new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(top, 0),
                    new GradientStop(bottom, 1)
                }
            };
        }

        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(top, 0),
                new GradientStop(bottom, 1)
            }
        };
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
        // 音频同步
        if (_audioPlayer?.IsLoaded == true && _audioPlayer.IsPlaying)
        {
            _audioPlayer.Pause();
            _audioProgressTimer?.Stop();
            UpdateAudioPlayIcon();
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
        _zoomLevel = Math.Min(_zoomLevel + 0.1, 8.0);
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
            _zoomLevel = Math.Min(_zoomLevel + 0.1, 8.0);
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

        /// <summary>
        /// 创建线性渐变画刷（用于描边渐变和文字渐变）。
        /// </summary>
        private static IBrush CreateGradientBrush(Color top, Color bottom, double size, string direction = "vertical")
        {
            if (string.IsNullOrEmpty(direction))
                direction = "vertical";

            if (direction == "horizontal")
            {
                return new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(top, 0),
                        new GradientStop(bottom, 1)
                    }
                };
            }

            return new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(top, 0),
                    new GradientStop(bottom, 1)
                }
            };
        }
    }

    #endregion
