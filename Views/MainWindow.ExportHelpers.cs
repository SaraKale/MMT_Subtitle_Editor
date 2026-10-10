using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace MMTGui;

/// <summary>
/// MainWindow 导出与退出辅助分部类。
/// 负责贴图序列导出、FX/X 文件生成，以及退出前的保存确认。
/// </summary>
public partial class MainWindow
{
    #region 文件操作

    // 导出图片序列 - 参照MMTSystem.cs完整实现
    private void ExportImageSequence(string dir, List<SubtitleEntry> subtitles)
    {
        for (int i = 0; i < subtitles.Count; i++)
        {
            var sub = subtitles[i];
            var pngPath = Path.Combine(dir, $"{i}.png");

        // ========== 新增：图片条目导出 ==========
        if (sub.IsImage && !string.IsNullOrEmpty(sub.ImagePath))
        {
            ExportImageEntry(pngPath, sub);
            continue;
        }
            var screenWidth = sub.ScreenWidth;
            var screenHeight = sub.ScreenHeight;

            var fontFamily = sub.FontName;
            var fontSize = sub.FontSize;
            var edgeWidth = sub.EdgeWidth;
            var fsizepx = TextureMetrics.GetFontSizeInPixels(screenHeight, fontSize);

            var fontColor = sub.FontColor;
            var strokeColor = sub.EdgeColor;
            var shadowColor = sub.ShadowColor;
            var bgColor = sub.BackColor;

            var shadowDist = sub.ShadowDistance;
            var shadowBlur = sub.ShadowBlur;
            var marginX = sub.MarginX;
            var marginY = sub.MarginY;
            var edgeRound = sub.EdgeRound;
            var underline = sub.Underline;
            var strikeout = sub.Strikeout;

            // 注：贴图紧贴墨水盒，对齐不再影响贴图尺寸与文本落点，
            // 因此这里不再取 sub.HAlign / sub.VAlign。
            var isTate = sub.Tate;

            var scaleX = sub.ScaleX;
            var scaleY = sub.ScaleY;
            var rotation = sub.Rotation;
            var spacing = sub.Spacing;

            var text = sub.Text;
            var typeface = new Typeface(fontFamily,
                sub.Italic ? FontStyle.Italic : FontStyle.Normal,
                sub.Bold ? FontWeight.Bold : FontWeight.Normal);

            var measured = MeasureRenderedText(text, typeface, fsizepx, isTate, spacing);

            // 尺寸口径唯一来源：解析阶段（MeasureTextureSize）与导出阶段共用同一算法，
            // 保证 :pos_next / :pos_nextline 的前进量与磁盘上 PNG 的真实宽度一致。
            // 内部按真实墨水包围盒（BuildGeometry）计算四向安全边距，
            // 描边/发光/斜体/圆体字的外扩都会被容纳，不再被纹理边界裁掉。
            var layout = TextureMetrics.ComputeTextureLayout(
                text,
                typeface,
                isTate,
                spacing,
                measured.Width,
                measured.Height,
                fsizepx,
                marginX,
                marginY,
                scaleX,
                scaleY,
                rotation,
                edgeWidth,
                shadowDist,
                shadowBlur,
                sub.Edge2Width,
                sub.Edge3Width,
                sub.Edge4Width,
                sub.GlowRadius,
                edgeRound);

            var texW = layout.TextureWidth;
            var texH = layout.TextureHeight;

            sub.TextureWidth = texW;
            sub.TextureHeight = texH;

            var renderBitmap = new RenderTargetBitmap(
                new PixelSize(texW, texH),
                new Vector(96, 96));

            using (var ctx = renderBitmap.CreateDrawingContext())
            {
                ctx.DrawRectangle(new SolidColorBrush(bgColor), null,
                    new Rect(0, 0, texW, texH));

                var transform = TextureMetrics.CreateLayerTransform(
                    layout.ContentWidth,
                    layout.ContentHeight,
                    scaleX,
                    scaleY,
                    rotation,
                    layout.OffsetX,
                    layout.OffsetY);

                // 文本排版原点：内容盒左上角相对排版原点(0,0)的偏移取反。
                // 例如墨水左溢出 3px、margin 2px 时，原点要右移 5px 才能让墨水落进内容盒。
                var originX = -layout.ContentLeft;
                var originY = -layout.ContentTop;

                using var transformDisposable = ctx.PushTransform(transform.Matrix);
                RenderTextCore(
                    ctx,
                    new Point(originX, originY),
                    new Point(0, 0),
                    text,
                    typeface,
                    fsizepx,
                    fontColor,
                    strokeColor,
                    shadowColor,
                    edgeWidth,
                    shadowDist,
                    shadowBlur,
                    underline,
                    strikeout,
                    edgeRound,
                    isTate,
                    // 水平/垂直对齐都已由墨水包围盒布局决定，这里传 0，
                    // 否则 RenderTextCore 内的对齐处理会再套一次，造成双重偏移。
                    0,
                    0,
                    spacing,
                    sub.Edge2Width,
                    sub.Edge2Color,
                    sub.Edge3Width,
                    sub.Edge3Color,
                    sub.Edge4Width,
                    sub.Edge4Color,
                    sub.GlowRadius,
                    sub.GlowColor,
                    sub.GlowAlpha,
                    sub.EdgeGradientEnabled,
                    sub.EdgeGradientTop,
                    sub.EdgeGradientBottom,
                    sub.TextGradientEnabled,
                    sub.TextGradientTop,
                    sub.TextGradientBottom,
                    sub.TextGradientDirection);
            }

            using (var stream = File.Create(pngPath))
            {
                renderBitmap.Save(stream);
            }
        }
    }

    // 反转文本中的行
    private static string ReverseLineForTate(string text)
    {
        var lines = text.Replace(Environment.NewLine, "\n").Split('\n');
        for (int i = 0; i < lines.Length / 2; i++)
        {
            int num = lines.Length - 1 - i;
            string tmp = lines[i];
            lines[i] = lines[num];
            lines[num] = tmp;
        }
        return string.Join(Environment.NewLine, lines);
    }

    // 计算渲染安全边距（转发到 TextureMetrics，保持预览调用点不变）
    private static double GetRenderSafetyPadding(double fontSize, double edgeWidth, double shadowDistance, double shadowBlur)
        => TextureMetrics.GetRenderSafetyPadding(fontSize, edgeWidth, shadowDistance, shadowBlur);

    // 计算变换边距缩放比例
    private static double GetTransformPaddingScale(double scaleX, double scaleY)
        => TextureMetrics.GetTransformPaddingScale(scaleX, scaleY);

    // 创建层变换矩阵
    private static (Matrix Matrix, double Width, double Height) CreateLayerTransform(
        double width,
        double height,
        double scaleX,
        double scaleY,
        double rotation,
        double offsetX = 0,
        double offsetY = 0)
        => TextureMetrics.CreateLayerTransform(width, height, scaleX, scaleY, rotation, offsetX, offsetY);

    // 计算渲染原点
    private static (double X, double Y) ComputeRenderOrigin(
        double canvasW,
        double canvasH,
        double textW,
        double textH,
        double fontSize,
        double marginX,
        double marginY,
        double safePad,
        bool isTate,
        int hAlign,
        int vAlign)
    {
        var marginPxX = fontSize * marginX / 100.0;
        var marginPxY = fontSize * marginY / 100.0;

        double x = hAlign switch
        {
            1 => (canvasW - textW) / 2.0,
            2 => Math.Max(safePad, canvasW - safePad - marginPxX - textW),
            _ => safePad + marginPxX
        };

        double y = vAlign switch
        {
            1 => (canvasH - textH) / 2.0,
            2 => Math.Max(safePad, canvasH - safePad - marginPxY - textH),
            _ => safePad + marginPxY
        };

        return (Math.Max(0.0, x), Math.Max(0.0, y));
    }

    /// <summary>
    /// 计算变换后的边界框
    /// </summary>
    private static (double Width, double Height) GetTransformedBounds(double width, double height, double scaleX, double scaleY, double rotation)
    {
        if (scaleX == 100 && scaleY == 100 && rotation == 0)
            return (width, height);

        var scaleMatrix = Matrix.CreateScale(scaleX / 100.0, scaleY / 100.0);
        var rotationMatrix = Matrix.CreateRotation(Math.PI * rotation / 180.0);
        var combinedMatrix = scaleMatrix * rotationMatrix;

        var corners = new Point[]
        {
            new Point(0, 0),
            new Point(width, 0),
            new Point(width, height),
            new Point(0, height)
        };

        var transformedCorners = corners.Select(c => c.Transform(combinedMatrix)).ToArray();

        var minX = transformedCorners.Min(c => c.X);
        var maxX = transformedCorners.Max(c => c.X);
        var minY = transformedCorners.Min(c => c.Y);
        var maxY = transformedCorners.Max(c => c.Y);

        return (maxX - minX, maxY - minY);
    }

    // 计算渲染后的文本宽度和高度
    private static (double Width, double Height) MeasureRenderedText(string text, Typeface typeface, double fontSize, bool isTate, double spacing = 0)
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

                return (
                    Math.Max(1.0, formatted.Width),
                    Math.Max(1.0, formatted.Height));
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

            return (
                Math.Max(1.0, Math.Ceiling(maxLineWidth)),
                Math.Max(1.0, Math.Ceiling(totalHeight)));
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
                    columnHeight += glyph.Height;
                    columnHeight += spacing;
                }

                if (line.Length > 0)
                    columnHeight -= spacing;
            }

            tateTotalWidth += columnWidth;
            tateMaxHeight = Math.Max(tateMaxHeight, columnHeight);
            tateTotalWidth += columnGap;
        }

        if (lines.Length > 0)
            tateTotalWidth -= columnGap;

        return (
            Math.Max(1.0, Math.Ceiling(tateTotalWidth)),
            Math.Max(1.0, Math.Ceiling(tateMaxHeight)));
    }

    // 导出图片条目
    private void ExportImageEntry(string outputPath, SubtitleEntry sub)
    {
        var imagePath = ResolveImagePath(sub.ImagePath);
        if (!File.Exists(imagePath)) return;
        
        using var srcBitmap = new Bitmap(imagePath);
        
        var scaledWidth = (int)(srcBitmap.PixelSize.Width * sub.ImageScaleX / 100.0);
        var scaledHeight = (int)(srcBitmap.PixelSize.Height * sub.ImageScaleY / 100.0);
        
        // 考虑旋转后的边界
        var rotation = sub.ImageRotation;
        var radians = rotation * Math.PI / 180.0;
        var cos = Math.Abs(Math.Cos(radians));
        var sin = Math.Abs(Math.Sin(radians));
        
        var rotatedWidth = (int)(scaledWidth * cos + scaledHeight * sin);
        var rotatedHeight = (int)(scaledWidth * sin + scaledHeight * cos);
        
        // 添加安全边距
        var safetyPadding = 20;
        var texW = rotatedWidth + safetyPadding * 2;
        var texH = rotatedHeight + safetyPadding * 2;
        
        if (texW < 1) texW = 1;
        if (texH < 1) texH = 1;
        
        sub.TextureWidth = texW;
        sub.TextureHeight = texH;
        
        using var renderBitmap = new RenderTargetBitmap(
            new PixelSize(texW, texH),
            new Vector(96, 96));
        
        using (var ctx = renderBitmap.CreateDrawingContext())
        {
            // 绘制透明背景
            ctx.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, texW, texH));
            
            // 应用变换
            var scaleTransform = new ScaleTransform(sub.ImageScaleX / 100.0, sub.ImageScaleY / 100.0);
            var rotateTransform = new RotateTransform(sub.ImageRotation);
            var translateTransform = new TranslateTransform(safetyPadding, safetyPadding);
            
            var transformMatrix = scaleTransform.Value * rotateTransform.Value * translateTransform.Value;
            
            using var transformDisposable = ctx.PushTransform(transformMatrix);
            
            // 绘制图片
            ctx.DrawImage(
                srcBitmap,
                new Rect(0, 0, srcBitmap.PixelSize.Width, srcBitmap.PixelSize.Height),
                new Rect(0, 0, srcBitmap.PixelSize.Width, srcBitmap.PixelSize.Height)
            );
        }
        
        using (var stream = File.Create(outputPath))
        {
            renderBitmap.Save(stream);
        }
    }
    // 渲染文本到上下文
    private void RenderTextToContext(
        DrawingContext ctx,
        Point originPoint,
        string text,
        Typeface typeface,
        double fontSize,
        Color fontColor,
        Color strokeColor,
        Color shadowColor,
        double edgeWidth,
        double shadowDistance,
        double shadowBlur,
        bool underline,
        bool strikeout,
        bool edgeRound,
        bool isTate,
        int hAlign,
        int vAlign,
        double spacing = 0,
        double scaleX = 100,
        double scaleY = 100,
        double rotation = 0,
        PreviewAnimationState? animationState = null,
        double edge2Width = 0,
        Color edge2Color = default,
        double edge3Width = 0,
        Color edge3Color = default,
        double edge4Width = 0,
        Color edge4Color = default,
        double glowRadius = 0,
        Color glowColor = default,
        double glowAlpha = 0.8,
        bool edgeGradientEnabled = false,
        Color edgeGradientTop = default,
        Color edgeGradientBottom = default,
        bool textGradientEnabled = false,
        Color textGradientTop = default,
        Color textGradientBottom = default,
        string textGradientDirection = "vertical")
    {
        var anim = animationState ?? PreviewAnimationState.Default;
        var effectiveOrigin = new Point(originPoint.X + anim.OffsetX, originPoint.Y + anim.OffsetY);
        var effectiveScaleX = scaleX * anim.ScaleMultiplier;
        var effectiveScaleY = scaleY * anim.ScaleMultiplier;

        var textBounds = MeasureRenderedText(text, typeface, fontSize, isTate, spacing);
        var transformedBounds = GetTransformedBounds(textBounds.Width, textBounds.Height, effectiveScaleX, effectiveScaleY, rotation);
        var hasTransform = effectiveScaleX != 100 || effectiveScaleY != 100 || rotation != 0;

        void DrawLayer(Point layerOrigin)
        {
            if (hasTransform)
            {
                var scaleMatrix = Matrix.CreateScale(effectiveScaleX / 100.0, effectiveScaleY / 100.0);
                var rotationMatrix = Matrix.CreateRotation(Math.PI * rotation / 180.0);
                var combinedMatrix = scaleMatrix * rotationMatrix;

                var textCenterX = textBounds.Width / 2.0;
                var textCenterY = textBounds.Height / 2.0;

                var transformOffsetX = (transformedBounds.Width - textBounds.Width) / 2.0;
                var transformOffsetY = (transformedBounds.Height - textBounds.Height) / 2.0;

                var adjustedOrigin = new Point(
                    layerOrigin.X - transformOffsetX + textCenterX,
                    layerOrigin.Y - transformOffsetY + textCenterY);

                using var transformDisposable = ctx.PushTransform(combinedMatrix);
                var renderOrigin = new Point(-textCenterX, -textCenterY);

                RenderTextCore(ctx, adjustedOrigin, renderOrigin, text, typeface, fontSize, fontColor, strokeColor, shadowColor, edgeWidth, shadowDistance, shadowBlur, underline, strikeout, edgeRound, isTate, hAlign, vAlign, spacing, edge2Width, edge2Color, edge3Width, edge3Color, edge4Width, edge4Color, glowRadius, glowColor, glowAlpha, edgeGradientEnabled, edgeGradientTop, edgeGradientBottom, textGradientEnabled, textGradientTop, textGradientBottom, textGradientDirection);
            }
            else
            {
                RenderTextCore(ctx, layerOrigin, new Point(0, 0), text, typeface, fontSize, fontColor, strokeColor, shadowColor, edgeWidth, shadowDistance, shadowBlur, underline, strikeout, edgeRound, isTate, hAlign, vAlign, spacing, edge2Width, edge2Color, edge3Width, edge3Color, edge4Width, edge4Color, glowRadius, glowColor, glowAlpha, edgeGradientEnabled, edgeGradientTop, edgeGradientBottom, textGradientEnabled, textGradientTop, textGradientBottom, textGradientDirection);
            }
        }

        void DrawWithClipAndOpacity(Point layerOrigin, double opacity)
        {
            if (opacity <= 0)
                return;

            IDisposable? opacityDisposable = opacity < 1 ? ctx.PushOpacity(opacity) : null;
            using (opacityDisposable)
            {
                if (anim.ClipMode == PreviewClipMode.None)
                {
                    DrawLayer(layerOrigin);
                    return;
                }

                var clipBounds = new Rect(effectiveOrigin.X, effectiveOrigin.Y, transformedBounds.Width, transformedBounds.Height);
                if (clipBounds.Width <= 0 || clipBounds.Height <= 0)
                    return;

                if (anim.ClipMode == PreviewClipMode.Wipe)
                {
                    var wipeWidth = Math.Max(1.0, clipBounds.Width * anim.ClipProgress);
                    var wipeRect = new Rect(
                        anim.ReverseDirection ? clipBounds.Right - wipeWidth : clipBounds.X,
                        clipBounds.Y,
                        wipeWidth,
                        clipBounds.Height);

                    using var clipDisposable = ctx.PushClip(wipeRect);
                    DrawLayer(layerOrigin);
                    return;
                }

                if (anim.ClipMode == PreviewClipMode.Blinds)
                {
                    var stripeCount = Math.Max(2, anim.StripeCount);
                    var stripeHeight = clipBounds.Height / stripeCount;

                    for (int stripe = 0; stripe < stripeCount; stripe++)
                    {
                        var visibleHeight = stripeHeight * anim.ClipProgress;
                        if (visibleHeight <= 0)
                            continue;

                        var stripeRect = new Rect(
                            clipBounds.X,
                            clipBounds.Y + stripe * stripeHeight,
                            clipBounds.Width,
                            Math.Max(1.0, visibleHeight));

                        using var clipDisposable = ctx.PushClip(stripeRect);
                        DrawLayer(layerOrigin);
                    }
                }
            }
        }

        if (anim.BlurStrength > 0)
        {
            var blurOffset = Math.Max(0.5, fontSize * 0.12 * anim.BlurStrength);
            var blurOpacity = anim.Alpha * 0.18;
            var offsets = new[]
            {
                new Point(-blurOffset, 0),
                new Point(blurOffset, 0),
                new Point(0, -blurOffset),
                new Point(0, blurOffset),
                new Point(-blurOffset * 0.6, -blurOffset * 0.6),
                new Point(blurOffset * 0.6, blurOffset * 0.6),
            };

            foreach (var offset in offsets)
                DrawWithClipAndOpacity(new Point(effectiveOrigin.X + offset.X, effectiveOrigin.Y + offset.Y), blurOpacity);
        }

        DrawWithClipAndOpacity(effectiveOrigin, anim.Alpha);
    }

    /// <summary>
    /// 核心文本渲染方法
    /// </summary>
    private static void RenderTextCore(
        DrawingContext ctx,
        Point originPoint,
        Point renderOffset,
        string text,
        Typeface typeface,
        double fontSize,
        Color fontColor,
        Color strokeColor,
        Color shadowColor,
        double edgeWidth,
        double shadowDistance,
        double shadowBlur,
        bool underline,
        bool strikeout,
        bool edgeRound,
        bool isTate,
        int hAlign,
        int vAlign,
        double spacing,
        double edge2Width,
        Color edge2Color,
        double edge3Width,
        Color edge3Color,
        double edge4Width,
        Color edge4Color,
        double glowRadius,
        Color glowColor,
        double glowAlpha,
        bool edgeGradientEnabled,
        Color edgeGradientTop,
        Color edgeGradientBottom,
        bool textGradientEnabled,
        Color textGradientTop,
        Color textGradientBottom,
        string textGradientDirection)
    {
        var actualOrigin = new Point(originPoint.X + renderOffset.X, originPoint.Y + renderOffset.Y);

        if (!isTate)
        {
            if (spacing == 0)
            {
                var shadowOffset = fontSize * shadowDistance / 100.0;

                if (shadowDistance > 0)
                {
                    var shadowFormatted = new FormattedText(
                        text,
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        fontSize,
                        new SolidColorBrush(shadowColor));

                    if (hAlign == 2) shadowFormatted.TextAlignment = TextAlignment.Right;
                    else if (hAlign == 1) shadowFormatted.TextAlignment = TextAlignment.Center;

                    ctx.DrawText(shadowFormatted, new Point(actualOrigin.X + shadowOffset, actualOrigin.Y + shadowOffset));
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
                        fontSize,
                        Brushes.White);

                    if (hAlign == 2) glowFormatted.TextAlignment = TextAlignment.Right;
                    else if (hAlign == 1) glowFormatted.TextAlignment = TextAlignment.Center;

                    var glowGeo = glowFormatted.BuildGeometry(actualOrigin);
                    if (glowGeo != null)
                    {
                        var baseAlpha = glowAlpha * 255.0;
                        var glowRad = fontSize * glowRadius / 100.0;
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
                        fontSize,
                        Brushes.White);

                    if (hAlign == 2) e4Formatted.TextAlignment = TextAlignment.Right;
                    else if (hAlign == 1) e4Formatted.TextAlignment = TextAlignment.Center;

                    var e4Geo = e4Formatted.BuildGeometry(actualOrigin);
                    if (e4Geo != null)
                    {
                        var e4PenWidth = fontSize * edge4Width * 2 / 100.0;
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
                        fontSize,
                        Brushes.White);

                    if (hAlign == 2) e3Formatted.TextAlignment = TextAlignment.Right;
                    else if (hAlign == 1) e3Formatted.TextAlignment = TextAlignment.Center;

                    var e3Geo = e3Formatted.BuildGeometry(actualOrigin);
                    if (e3Geo != null)
                    {
                        var e3PenWidth = fontSize * edge3Width * 2 / 100.0;
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
                        fontSize,
                        Brushes.White);

                    if (hAlign == 2) outerFormatted.TextAlignment = TextAlignment.Right;
                    else if (hAlign == 1) outerFormatted.TextAlignment = TextAlignment.Center;

                    var outerGeo = outerFormatted.BuildGeometry(actualOrigin);
                    if (outerGeo != null)
                    {
                        var outerPenWidth = fontSize * edge2Width * 2 / 100.0;
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
                        fontSize,
                        Brushes.White);

                    if (hAlign == 2) strokeFormatted.TextAlignment = TextAlignment.Right;
                    else if (hAlign == 1) strokeFormatted.TextAlignment = TextAlignment.Center;

                    var geo = strokeFormatted.BuildGeometry(actualOrigin);
                    if (geo != null)
                    {
                        var penWidth = fontSize * edgeWidth * 2 / 100.0;
                        var edgeBrush = edgeGradientEnabled
                            ? CreateExportGradientBrush(edgeGradientTop, edgeGradientBottom, textGradientDirection)
                            : (IBrush)new SolidColorBrush(strokeColor);
                        var pen = new Pen(edgeBrush, penWidth)
                        {
                            LineJoin = edgeRound ? PenLineJoin.Round : PenLineJoin.Miter
                        };
                        ctx.DrawGeometry(null, pen, geo);
                    }
                }

                // 4. 文字本体（最上层）
                var mainFormatted = new FormattedText(
                    text,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    fontSize,
                    textGradientEnabled
                        ? CreateExportGradientBrush(textGradientTop, textGradientBottom, textGradientDirection)
                        : new SolidColorBrush(fontColor));

                if (hAlign == 2) mainFormatted.TextAlignment = TextAlignment.Right;
                else if (hAlign == 1) mainFormatted.TextAlignment = TextAlignment.Center;

                ctx.DrawText(mainFormatted, actualOrigin);
                DrawTextDecorations(ctx, mainFormatted, actualOrigin, fontColor, fontSize, underline, strikeout);
                return;
            }

            var textLines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            var lineHeight = 0.0;

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

            var y = actualOrigin.Y;

            foreach (var textLine in textLines)
            {
                var x = actualOrigin.X;

                if (string.IsNullOrEmpty(textLine))
                {
                    y += lineHeight;
                    continue;
                }

                var lineChars = textLine.ToCharArray();

                for (int i = 0; i < lineChars.Length; i++)
                {
                    var ch = lineChars[i];
                    var chText = ch.ToString();

                    if (shadowDistance > 0)
                    {
                        var shadowOffset = fontSize * shadowDistance / 100.0;
                        var shadowFormatted = new FormattedText(
                            chText,
                            System.Globalization.CultureInfo.CurrentCulture,
                            FlowDirection.LeftToRight,
                            typeface,
                            fontSize,
                            new SolidColorBrush(shadowColor));

                        ctx.DrawText(shadowFormatted, new Point(x + shadowOffset, y + shadowOffset));
                    }

                    if (edgeWidth > 0)
                    {
                        var penWidth = fontSize * edgeWidth * 2 / 100.0;
                        var pen = new Pen(new SolidColorBrush(strokeColor), penWidth)
                        {
                            LineJoin = edgeRound ? PenLineJoin.Round : PenLineJoin.Miter
                        };

                        var strokeFormatted = new FormattedText(
                            chText,
                            System.Globalization.CultureInfo.CurrentCulture,
                            FlowDirection.LeftToRight,
                            typeface,
                            fontSize,
                            Brushes.White);

                        var geo = strokeFormatted.BuildGeometry(new Point(x, y));
                        if (geo != null)
                            ctx.DrawGeometry(null, pen, geo);
                    }

                    var mainFormatted = new FormattedText(
                        chText,
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        fontSize,
                        new SolidColorBrush(fontColor));

                    var glyphOrigin = new Point(x, y);
                    ctx.DrawText(mainFormatted, glyphOrigin);
                    DrawTextDecorations(ctx, mainFormatted, glyphOrigin, fontColor, fontSize, underline, strikeout);
                    x += mainFormatted.Width + spacing;
                    lineHeight = Math.Max(lineHeight, mainFormatted.Height);
                }

                y += lineHeight;
            }

            return;
        }

        text = ReverseLineForTate(text);
        var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        var columnGap = Math.Max(1.0, fontSize * 0.15);
        var tateX = actualOrigin.X;

        foreach (var line in lines)
        {
            var glyphs = new List<(string Text, FormattedText Main, FormattedText Shadow, double Height)>();
            var columnWidth = fontSize * 0.6;

            if (string.IsNullOrEmpty(line))
            {
                var main = new FormattedText(
                    " ",
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    fontSize,
                    new SolidColorBrush(fontColor));

                var shadow = new FormattedText(
                    " ",
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    fontSize,
                    new SolidColorBrush(shadowColor));

                glyphs.Add((" ", main, shadow, main.Height));
                columnWidth = Math.Max(columnWidth, main.Width);
            }
            else
            {
                foreach (var ch in line)
                {
                    var chText = ch.ToString();
                    var main = new FormattedText(
                        chText,
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        fontSize,
                        new SolidColorBrush(fontColor));

                    var shadow = new FormattedText(
                        chText,
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        fontSize,
                        new SolidColorBrush(shadowColor));

                    glyphs.Add((chText, main, shadow, main.Height));
                    columnWidth = Math.Max(columnWidth, main.Width);
                }
            }

            var y = actualOrigin.Y;

            foreach (var glyph in glyphs)
            {
                if (shadowDistance > 0)
                {
                    var offset = fontSize * shadowDistance / 100.0;
                    ctx.DrawText(glyph.Shadow, new Point(tateX + offset, y + offset));
                }

                if (edgeWidth > 0)
                {
                    var penWidth = fontSize * edgeWidth * 2 / 100.0;
                    var pen = new Pen(new SolidColorBrush(strokeColor), penWidth)
                    {
                        LineJoin = edgeRound ? PenLineJoin.Round : PenLineJoin.Miter
                    };

                    var strokeFormatted = new FormattedText(
                        glyph.Text,
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        fontSize,
                        Brushes.White);

                    var geo = strokeFormatted.BuildGeometry(new Point(tateX, y));
                    if (geo != null)
                        ctx.DrawGeometry(null, pen, geo);
                }

                var glyphOrigin = new Point(tateX, y);
                ctx.DrawText(glyph.Main, glyphOrigin);
                DrawTextDecorations(ctx, glyph.Main, glyphOrigin, fontColor, fontSize, underline, strikeout, isVertical: true);
                y += glyph.Height;
            }

            tateX += columnWidth + columnGap;
        }
    }

    // 创建渐变画刷（用于导出渲染的描边渐变和文字渐变）
    private static IBrush CreateExportGradientBrush(Color top, Color bottom, string direction)
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

    // 绘制下划线和删除线
    private static void DrawTextDecorations(
        DrawingContext ctx,
        FormattedText formattedText,
        Point originPoint,
        Color color,
        double fontSize,
        bool underline,
        bool strikeout,
        bool isVertical = false)
    {
        if ((!underline && !strikeout) || isVertical)
            return;

        var geometry = formattedText.BuildGeometry(originPoint);
        var bounds = geometry?.Bounds ?? new Rect(originPoint, new Size(formattedText.Width, formattedText.Height));
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var thickness = Math.Max(1.0, fontSize * 0.06);
        var pen = new Pen(new SolidColorBrush(color), thickness);

        if (underline)
        {
            var underlineY = bounds.Bottom - thickness;
            ctx.DrawLine(pen, new Point(bounds.Left, underlineY), new Point(bounds.Right, underlineY));
        }

        if (strikeout)
        {
            var strikeY = bounds.Top + bounds.Height * 0.55;
            ctx.DrawLine(pen, new Point(bounds.Left, strikeY), new Point(bounds.Right, strikeY));
        }
    }

    // ----------------
    // 导出 MikuMikuTelop.FX 文件
    // ----------------
    private void ExportMikuMikuTelopFx(string dir, List<SubtitleEntry> subtitles)
    {
        var sb = new StringBuilder();

        sb.AppendLine("////////////////////////////////////////////////////////////////////////////////////////////////");
        sb.AppendLine("// MMT Subtitle Editor Export - ps_2_0");
        sb.AppendLine();

        sb.AppendLine("#define ALIGN_LEFT_TOP      \"0\"");
        sb.AppendLine("#define ALIGN_LEFT_CENTER   \"1\"");
        sb.AppendLine("#define ALIGN_LEFT_BOTTOM   \"2\"");
        sb.AppendLine("#define ALIGN_CENTER_TOP    \"3\"");
        sb.AppendLine("#define ALIGN_CENTER_CENTER \"4\"");
        sb.AppendLine("#define ALIGN_CENTER_BOTTOM \"5\"");
        sb.AppendLine("#define ALIGN_RIGHT_TOP     \"6\"");
        sb.AppendLine("#define ALIGN_RIGHT_CENTER  \"7\"");
        sb.AppendLine("#define ALIGN_RIGHT_BOTTOM  \"8\"");
        sb.AppendLine();

        sb.AppendLine("float alpha1 : CONTROLOBJECT < string name = \"(self)\"; string item = \"Tr\"; >;");
        sb.AppendLine();

        sb.AppendLine("float4x4 WorldViewProjMatrix      : WORLDVIEWPROJECTION;");
        sb.AppendLine("float4x4 WorldMatrix              : WORLD;");
        sb.AppendLine("float4x4 ViewMatrix               : VIEW;");
        sb.AppendLine("float4x4 WorldViewMatrixInverse        : WORLDVIEWINVERSE;");
        sb.AppendLine("float4x4 LightWorldViewProjMatrix : WORLDVIEWPROJECTION < string Object = \"Light\"; >;");
        sb.AppendLine();
        sb.AppendLine("static float3x3 BillboardMatrix = {");
        sb.AppendLine("    normalize(WorldViewMatrixInverse[0].xyz),");
        sb.AppendLine("    normalize(WorldViewMatrixInverse[1].xyz),");
        sb.AppendLine("    normalize(WorldViewMatrixInverse[2].xyz),");
        sb.AppendLine("};");
        sb.AppendLine();

        sb.AppendLine("float2 ViewportSize : VIEWPORTPIXELSIZE;");
        sb.AppendLine("static float screen_aspect = ViewportSize.x / ViewportSize.y;");
        sb.AppendLine();
        sb.AppendLine("float ftime : TIME <bool SyncInEditMode = true;>;");
        sb.AppendLine();
        sb.AppendLine("float elapsed_time1 : ELAPSEDTIME<bool SyncInEditMode=true;>;");
        sb.AppendLine("float elapsed_time2 : ELAPSEDTIME<bool SyncInEditMode=false;>;");
        sb.AppendLine("static bool IsPlaying = (abs(elapsed_time1 - elapsed_time2) < 0.01);");
        sb.AppendLine();
        sb.AppendLine("bool flag1 : CONTROLOBJECT < string name = \"PostTelop.x\"; >;");
        sb.AppendLine("bool flag2 : CONTROLOBJECT < string name = \"(OffscreenOwner)\"; >;");
        sb.AppendLine("static bool hide_by_post = flag1 && !flag2;");
        sb.AppendLine();

        for (int i = 0; i < subtitles.Count; i++)
        {
            sb.AppendLine($"texture2D tex_text{i} <");
            sb.AppendLine($"    string ResourceName = \"{i}.png\";");
            sb.AppendLine($"    int MipLevels = 0;");
            sb.AppendLine($">;");
            sb.AppendLine($"sampler samp_text{i} = sampler_state {{");
            sb.AppendLine($"    texture = <tex_text{i}>;");
            sb.AppendLine($"    MINFILTER = LINEAR;");
            sb.AppendLine($"    MAGFILTER = LINEAR;");
            sb.AppendLine($"    MIPFILTER = NONE;");
            sb.AppendLine($"    AddressU  = CLAMP;");
            sb.AppendLine($"    AddressV = CLAMP;");
            sb.AppendLine($"}};");
            sb.AppendLine($"sampler samp_at_text{i} = sampler_state {{");
            sb.AppendLine($"    texture = <tex_text{i}>;");
            sb.AppendLine($"    MINFILTER = ANISOTROPIC;");
            sb.AppendLine($"    MAGFILTER = ANISOTROPIC;");
            sb.AppendLine($"    MIPFILTER = LINEAR;");
            sb.AppendLine($"    MAXANISOTROPY = 16;");
            sb.AppendLine($"    AddressU  = CLAMP;");
            sb.AppendLine($"    AddressV = CLAMP;");
            sb.AppendLine($"}};");
            sb.AppendLine();
        }

        sb.AppendLine("///////////////////////////////////////////////////////////////////////////////////////////////");
        sb.AppendLine();
        sb.AppendLine("struct VS_OUTPUT");
        sb.AppendLine("{");
        sb.AppendLine("    float4 Pos        : POSITION;");
        sb.AppendLine("    float4 Tex        : TEXCOORD0;");
        sb.AppendLine("    float4 Anim       : TEXCOORD1;");
        sb.AppendLine("    float4 Color      : COLOR0;");
        sb.AppendLine("};");
        sb.AppendLine();

        sb.AppendLine("VS_OUTPUT VS_Text(float4 Pos : POSITION, float4 Tex : TEXCOORD0,");
        sb.AppendLine("    uniform bool PanelMode, uniform float3 ViewPos, uniform float size, uniform float aspect,");
        sb.AppendLine("    uniform float starttime, uniform float endtime, uniform float fade, uniform bool billboard, uniform float anim_type, uniform float anim_time, uniform float anim_param1)");
        sb.AppendLine("{");
        sb.AppendLine("    VS_OUTPUT Out;");
        sb.AppendLine("    Out.Pos = Pos;");
        sb.AppendLine("    Out.Pos.x *= size * aspect / screen_aspect;");
        sb.AppendLine("    Out.Pos.y *= size;");
        sb.AppendLine("    Out.Pos.zw = float2(0, 1);");
        sb.AppendLine("    float effective_anim_time = (anim_time <= 0) ? fade : anim_time;");
        sb.AppendLine("    effective_anim_time = max(0.0001, effective_anim_time);");
        sb.AppendLine("    float anim_in = saturate((ftime - starttime) / effective_anim_time);");
        sb.AppendLine("    float anim_out = saturate((endtime - ftime) / effective_anim_time);");
        sb.AppendLine("    float anim_edge = min(anim_in, anim_out);");
        sb.AppendLine("    if(anim_type == 2) {");
        sb.AppendLine("        Out.Pos.y -= (1 - anim_in) * size * 0.35;");
        sb.AppendLine("        Out.Pos.y += (1 - anim_out) * size * 0.18;");
        sb.AppendLine("    }");
        sb.AppendLine("    if(anim_type == 3) {");
        sb.AppendLine("        float pop_scale = (anim_in < 0.65) ? lerp(0.68, 1.10, anim_in / 0.65) : lerp(1.10, 1.0, (anim_in - 0.65) / 0.35);");
        sb.AppendLine("        pop_scale *= lerp(0.90, 1.0, anim_out);");
        sb.AppendLine("        Out.Pos.xy *= pop_scale;");
        sb.AppendLine("    }");
        sb.AppendLine("    if(PanelMode) Out.Pos.x *= screen_aspect;");
        sb.AppendLine("    if(billboard) Out.Pos.xyz = mul( Out.Pos.xyz, BillboardMatrix );");
        sb.AppendLine("    Out.Pos.xyz += ViewPos;");
        sb.AppendLine("    if(PanelMode) Out.Pos = mul(Out.Pos, WorldViewProjMatrix);");
        sb.AppendLine("    bool visivle = (starttime <= ftime && ftime < endtime) && !hide_by_post;");
        sb.AppendLine("    Out.Pos.y += !visivle * 1000;");
        sb.AppendLine("    float alpha = saturate(min(((ftime - starttime) / fade), ((endtime - ftime) / fade)));");
        sb.AppendLine("    alpha = (fade == 0) ? 1 : alpha;");
        sb.AppendLine("    Out.Tex = Tex;");
        sb.AppendLine("    Out.Anim = float4(anim_in, anim_param1, anim_edge, anim_type);");
        sb.AppendLine("    Out.Color = float4(1,1,1,alpha);");
        sb.AppendLine("    return Out;");
        sb.AppendLine("}");
        sb.AppendLine();

        sb.AppendLine("float4 PS_Text( VS_OUTPUT IN, uniform sampler samp_text , uniform float malpha) : COLOR0");
        sb.AppendLine("{");
        sb.AppendLine("    float4 color;");
        sb.AppendLine("    if(IN.Anim.w == 1) {");
        sb.AppendLine("        float blur = (1 - IN.Anim.z) * max(0.2, IN.Anim.y);");
        sb.AppendLine("        float2 blur_step = (frac(IN.Tex.yx * float2(17.0, 13.0) + ftime * 0.35) - 0.5) * blur * 0.02;");
        sb.AppendLine("        color  = tex2D(samp_text, IN.Tex) * 0.50;");
        sb.AppendLine("        color += tex2D(samp_text, IN.Tex + blur_step) * 0.25;");
        sb.AppendLine("        color += tex2D(samp_text, IN.Tex - blur_step) * 0.25;");
        sb.AppendLine("    } else {");
        sb.AppendLine("        color = tex2D(samp_text, IN.Tex);");
        sb.AppendLine("    }");
        sb.AppendLine("    if(IN.Anim.w == 4) {");
        sb.AppendLine("        float wipeMask = (IN.Anim.y > 0.5) ? step(1 - IN.Tex.x, IN.Anim.z) : step(IN.Tex.x, IN.Anim.z);");
        sb.AppendLine("        color *= wipeMask;");
        sb.AppendLine("    }");
        sb.AppendLine("    if(IN.Anim.w == 5) {");
        sb.AppendLine("        float stripe = frac(IN.Tex.y * max(2.0, IN.Anim.y));");
        sb.AppendLine("        color *= step(stripe, IN.Anim.z);");
        sb.AppendLine("    }");
        sb.AppendLine("    color *= IN.Color * alpha1 * malpha;");
        sb.AppendLine("    return color;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("///////////////////////////////////////////////////////////////////////////////////////////////");
        sb.AppendLine();

        var alignGroups = new Dictionary<int, List<int>>();
        for (int i = 0; i < subtitles.Count; i++)
        {
            int alignKey = subtitles[i].HAlign * 3 + subtitles[i].VAlign;
            if (!alignGroups.ContainsKey(alignKey))
                alignGroups[alignKey] = new List<int>();
            alignGroups[alignKey].Add(i);
        }

        for (int k = 0; k < 9; k++)
        {
            sb.AppendLine($"technique Align{k} < string MMDPass = \"object\"; string Subset = \"{k}\"; > {{");
            if (alignGroups.ContainsKey(k))
            {
                foreach (var idx in alignGroups[k])
                {
                    var sub = subtitles[idx];
                    var x = sub.PosX / 100.0;
                    var y = sub.PosY / 100.0;
                    var z = sub.PosZ;
                    var size = sub.TextureHeight > 0 ? (double)sub.TextureHeight * 2 / Math.Max(1.0, sub.ScreenHeight) : 0.15;
                    var aspect = sub.TextureHeight > 0 ? (double)sub.TextureWidth / sub.TextureHeight : 1.0;
                    var stime = sub.StartTime;
                    var etime = sub.EndTime;
                    var fade = sub.Fade;
                    var billboard = sub.Billboard ? "true" : "false";
                    var malpha = sub.IsImage ? sub.ImageAlpha.ToString("F4") : sub.Alpha.ToString("F4");
                    var animType = GetAnimationTypeId(sub.AnimationType);
                    var animTime = sub.AnimationDuration > 0 ? sub.AnimationDuration : sub.Fade;
                    var animParam1 = GetAnimationParam1(sub);

                    sb.AppendLine($"    pass Text{idx} {{");
                    sb.AppendLine($"        ZEnable = false;");
                    sb.AppendLine($"        CullMode = None;");
                    sb.AppendLine($"        VertexShader = compile vs_2_0 VS_Text(false, float3({x:F4}, {y:F4}, {z:F4}), {size:F7}, {aspect:F6}, {stime:F2}, {etime:F2}, {fade:F2}, {billboard}, {animType:F0}, {animTime:F2}, {animParam1:F2});");
                    sb.AppendLine($"        PixelShader  = compile ps_2_0 PS_Text(samp_text{idx}, {malpha});");
                    sb.AppendLine($"    }}");
                }
            }
            sb.AppendLine("}");
            sb.AppendLine();

            sb.AppendLine($"technique Align{k}SS < string MMDPass = \"object_ss\"; string Subset = \"{k}\"; > {{");
            if (alignGroups.ContainsKey(k))
            {
                foreach (var idx in alignGroups[k])
                {
                    var sub = subtitles[idx];
                    var x = sub.PosX / 100.0;
                    var y = sub.PosY / 100.0;
                    var z = sub.PosZ;
                    var size = sub.TextureHeight > 0 ? (double)sub.TextureHeight * 2 / Math.Max(1.0, sub.ScreenHeight) : 0.15;
                    var aspect = sub.TextureHeight > 0 ? (double)sub.TextureWidth / sub.TextureHeight : 1.0;
                    var stime = sub.StartTime;
                    var etime = sub.EndTime;
                    var fade = sub.Fade;
                    var billboard = sub.Billboard ? "true" : "false";
                    var malpha = sub.IsImage ? sub.ImageAlpha.ToString("F4") : sub.Alpha.ToString("F4");
                    var animType = GetAnimationTypeId(sub.AnimationType);
                    var animTime = sub.AnimationDuration > 0 ? sub.AnimationDuration : sub.Fade;
                    var animParam1 = GetAnimationParam1(sub);

                    sb.AppendLine($"    pass Text{idx} {{");
                    sb.AppendLine($"        ZWriteEnable = false;");
                    sb.AppendLine($"        CullMode = None;");
                    sb.AppendLine($"        VertexShader = compile vs_2_0 VS_Text(true, float3({x:F4}, {y:F4}, {z:F4}), {size:F7}, {aspect:F6}, {stime:F2}, {etime:F2}, {fade:F2}, {billboard}, {animType:F0}, {animTime:F2}, {animParam1:F2});");
                    sb.AppendLine($"        PixelShader  = compile ps_2_0 PS_Text(samp_at_text{idx}, {malpha});");
                    sb.AppendLine($"    }}");
                }
            }
            sb.AppendLine("}");
            sb.AppendLine();
        }

        sb.AppendLine("////////////////////////////////////////////////////////////////////////////////////");
        sb.AppendLine();
        sb.AppendLine("VS_OUTPUT VS_BackScreen(float4 Pos : POSITION, float4 Tex : TEXCOORD0 ) {");
        sb.AppendLine("    VS_OUTPUT Out = (VS_OUTPUT)0;");
        sb.AppendLine("    Out.Pos = Pos;");
        sb.AppendLine("    Out.Pos.x *= screen_aspect;");
        sb.AppendLine("    Out.Pos = mul(Out.Pos, WorldViewProjMatrix);");
        sb.AppendLine("    return Out;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("float4 PS_BackScreen( VS_OUTPUT IN ) : COLOR0");
        sb.AppendLine("{");
        sb.AppendLine("    float4 color;");
        sb.AppendLine("    color = float4(0, 0, 1, 0.07 * !IsPlaying * !hide_by_post);");
        sb.AppendLine("    return color;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("technique BackScreen < string MMDPass = \"object\"; string Subset = \"9\"; > { }");
        sb.AppendLine();
        sb.AppendLine("technique BackScreenSS < string MMDPass = \"object_ss\"; string Subset = \"9\"; > {");
        sb.AppendLine("    pass BackScreenPass {");
        sb.AppendLine("        ZWRITEENABLE = false;");
        sb.AppendLine("        CullMode = None;");
        sb.AppendLine("        VertexShader = compile vs_2_0 VS_BackScreen();");
        sb.AppendLine("        PixelShader  = compile ps_2_0 PS_BackScreen();");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("technique EdgeTec < string MMDPass = \"edge\"; > { }");
        sb.AppendLine("technique ShadowTec < string MMDPass = \"shadow\"; > { }");
        sb.AppendLine("technique ZplotTec < string MMDPass = \"zplot\"; > { }");

        File.WriteAllText(Path.Combine(dir, "MikuMikuTelop.fx"), sb.ToString(), new UTF8Encoding(false));
    }

    // ----------------
    // 导出 MikuMikuTelop.X 文件
    // ----------------
    private void ExportMikuMikuTelopX(string dir)
    {
        var sb = new StringBuilder();

        int alignCount = 9;
        int vertexCount = alignCount * 4 + 4;
        int faceCount = alignCount * 2 + 2;

        var alignVerts = new double[][]
        {
            new[] { 1.0, 0.0, 0.0, -1.0, 1.0, -1.0, 0.0, 0.0 },
            new[] { 1.0, 0.5, 0.0, -0.5, 1.0, -0.5, 0.0, 0.5 },
            new[] { 1.0, 1.0, 0.0, 0.0, 1.0, 0.0, 0.0, 1.0 },
            new[] { 0.5, 0.0, -0.5, -1.0, 0.5, -1.0, -0.5, 0.0 },
            new[] { 0.5, 0.5, -0.5, -0.5, 0.5, -0.5, -0.5, 0.5 },
            new[] { 0.5, 1.0, -0.5, 0.0, 0.5, 0.0, -0.5, 1.0 },
            new[] { 0.0, 0.0, -1.0, -1.0, 0.0, -1.0, -1.0, 0.0 },
            new[] { 0.0, 0.5, -1.0, -0.5, 0.0, -0.5, -1.0, 0.5 },
            new[] { 0.0, 1.0, -1.0, 0.0, 0.0, 0.0, -1.0, 1.0 },
        };

        sb.AppendLine("xof 0303txt 0032");
        sb.AppendLine("template Vector {");
        sb.AppendLine(" <3d82ab5e-62da-11cf-ab39-0020af71e433>");
        sb.AppendLine(" FLOAT x;");
        sb.AppendLine(" FLOAT y;");
        sb.AppendLine(" FLOAT z;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("template MeshFace {");
        sb.AppendLine(" <3d82ab5f-62da-11cf-ab39-0020af71e433>");
        sb.AppendLine(" DWORD nFaceVertexIndices;");
        sb.AppendLine(" array DWORD faceVertexIndices[nFaceVertexIndices];");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("template Mesh {");
        sb.AppendLine(" <3d82ab44-62da-11cf-ab39-0020af71e433>");
        sb.AppendLine(" DWORD nVertices;");
        sb.AppendLine(" array Vector vertices[nVertices];");
        sb.AppendLine(" DWORD nFaces;");
        sb.AppendLine(" array MeshFace faces[nFaces];");
        sb.AppendLine(" [...]");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("template MeshNormals {");
        sb.AppendLine(" <f6f23f43-7686-11cf-8f52-0040333594a3>");
        sb.AppendLine(" DWORD nNormals;");
        sb.AppendLine(" array Vector normals[nNormals];");
        sb.AppendLine(" DWORD nFaceNormals;");
        sb.AppendLine(" array MeshFace faceNormals[nFaceNormals];");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("template Coords2d {");
        sb.AppendLine(" <f6f23f44-7686-11cf-8f52-0040333594a3>");
        sb.AppendLine(" FLOAT u;");
        sb.AppendLine(" FLOAT v;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("template MeshTextureCoords {");
        sb.AppendLine(" <f6f23f40-7686-11cf-8f52-0040333594a3>");
        sb.AppendLine(" DWORD nTextureCoords;");
        sb.AppendLine(" array Coords2d textureCoords[nTextureCoords];");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("template ColorRGBA {");
        sb.AppendLine(" <35ff44e0-6c7c-11cf-8f52-0040333594a3>");
        sb.AppendLine(" FLOAT red;");
        sb.AppendLine(" FLOAT green;");
        sb.AppendLine(" FLOAT blue;");
        sb.AppendLine(" FLOAT alpha;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("template ColorRGB {");
        sb.AppendLine(" <d3e16e81-7835-11cf-8f52-0040333594a3>");
        sb.AppendLine(" FLOAT red;");
        sb.AppendLine(" FLOAT green;");
        sb.AppendLine(" FLOAT blue;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("template Material {");
        sb.AppendLine(" <3d82ab4d-62da-11cf-ab39-0020af71e433>");
        sb.AppendLine(" ColorRGBA faceColor;");
        sb.AppendLine(" FLOAT power;");
        sb.AppendLine(" ColorRGB specularColor;");
        sb.AppendLine(" ColorRGB emissiveColor;");
        sb.AppendLine(" [...]");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("template MeshMaterialList {");
        sb.AppendLine(" <f6f23f42-7686-11cf-8f52-0040333594a3>");
        sb.AppendLine(" DWORD nMaterials;");
        sb.AppendLine(" DWORD nFaceIndexes;");
        sb.AppendLine(" array DWORD faceIndexes[nFaceIndexes];");
        sb.AppendLine(" [Material <3d82ab4d-62da-11cf-ab39-0020af71e433>]");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine();

        sb.AppendLine("Mesh {");
        sb.AppendLine($"  {vertexCount};");

        for (int i = 0; i < alignCount; i++)
        {
            var v = alignVerts[i];
            var sep = ",";
            sb.AppendLine($"  {v[0]:F6};{v[1]:F6};0.000000;{sep}");
            sb.AppendLine($"  {v[2]:F6};{v[3]:F6};0.000000;{sep}");
            sb.AppendLine($"  {v[4]:F6};{v[5]:F6};0.000000;{sep}");
            sb.AppendLine($"  {v[6]:F6};{v[7]:F6};0.000000;{sep}");
        }

        sb.AppendLine("  1.000000;1.000000;0.000000;,");
        sb.AppendLine("  -1.000000;-1.000000;0.000000;,");
        sb.AppendLine("  1.000000;-1.000000;0.000000;,");
        sb.AppendLine("  -1.000000;1.000000;0.000000;;");

        sb.AppendLine($"  {faceCount};");

        for (int i = 0; i < alignCount; i++)
        {
            var baseIdx = i * 4;
            sb.AppendLine($"  3;{baseIdx + 2},{baseIdx + 1},{baseIdx + 0};,");
            sb.AppendLine($"  3;{baseIdx + 3},{baseIdx + 0},{baseIdx + 1};,");
        }

        var bsBase = alignCount * 4;
        sb.AppendLine($"  3;{bsBase + 2},{bsBase + 1},{bsBase + 0};,");
        sb.AppendLine($"  3;{bsBase + 3},{bsBase + 0},{bsBase + 1};;");

        sb.AppendLine();
        sb.AppendLine(" MeshNormals {");
        sb.AppendLine($"  {vertexCount};");
        for (int i = 0; i < alignCount; i++)
        {
            sb.AppendLine("  0.000000;0.000000;-1.000000;,");
            sb.AppendLine("  0.000000;0.000000;-1.000000;,");
            sb.AppendLine("  0.000000;0.000000;-1.000000;,");
            sb.AppendLine("  0.000000;0.000000;-1.000000;,");
        }
        sb.AppendLine("  0.000000;0.000000;-1.000000;,");
        sb.AppendLine("  0.000000;0.000000;-1.000000;,");
        sb.AppendLine("  0.000000;0.000000;-1.000000;,");
        sb.AppendLine("  0.000000;0.000000;-1.000000;;");
        sb.AppendLine($"  {faceCount};");
        for (int i = 0; i < alignCount; i++)
        {
            var baseIdx = i * 4;
            sb.AppendLine($"  3;{baseIdx + 2},{baseIdx + 1},{baseIdx + 0};,");
            sb.AppendLine($"  3;{baseIdx + 3},{baseIdx + 0},{baseIdx + 1};,");
        }
        sb.AppendLine($"  3;{bsBase + 2},{bsBase + 1},{bsBase + 0};,");
        sb.AppendLine($"  3;{bsBase + 3},{bsBase + 0},{bsBase + 1};;");
        sb.AppendLine(" }");
        sb.AppendLine();

        sb.AppendLine(" MeshTextureCoords {");
        sb.AppendLine($"  {vertexCount};");
        for (int i = 0; i < alignCount; i++)
        {
            sb.AppendLine("  1.000000;0.000000;,");
            sb.AppendLine("  0.000000;1.000000;,");
            sb.AppendLine("  1.000000;1.000000;,");
            sb.AppendLine("  0.000000;0.000000;,");
        }
        sb.AppendLine("  1.000000;0.000000;,");
        sb.AppendLine("  0.000000;1.000000;,");
        sb.AppendLine("  1.000000;1.000000;,");
        sb.AppendLine("  0.000000;0.000000;;");
        sb.AppendLine(" }");
        sb.AppendLine();

        int materialCount = alignCount + 1;
        sb.AppendLine(" MeshMaterialList {");
        sb.AppendLine($"  {materialCount};");
        sb.AppendLine($"  {faceCount};");
        for (int i = 0; i < alignCount; i++)
        {
            sb.AppendLine($"  {i},");
            sb.AppendLine($"  {i},");
        }
        sb.AppendLine($"  {alignCount},");
        sb.AppendLine($"  {alignCount};");
        sb.AppendLine();

        for (int i = 0; i < materialCount; i++)
        {
            sb.AppendLine("  Material {");
            sb.AppendLine("   0.000000;0.000000;0.000000;0.000000;;");
            sb.AppendLine("   5.000000;");
            sb.AppendLine("   0.000000;0.000000;0.000000;;");
            sb.AppendLine("   0.000000;0.000000;0.000000;;");
            sb.AppendLine("  }");
        }
        sb.AppendLine(" }");
        sb.AppendLine("}");

        File.WriteAllText(Path.Combine(dir, "MikuMikuTelop.x"), sb.ToString(), new UTF8Encoding(false));
    }

    // ----------------
    // 导出 PostTelop.Fx 文件
    // ----------------
    private void ExportPostTelopFx(string dir)
    {
        var sb = new StringBuilder();

        sb.AppendLine("////////////////////////////////////////////////////////////////////////////////////////////////");
        sb.AppendLine("// PostTelop.fx - ps_2_0");
        sb.AppendLine();

        sb.AppendLine("bool flag1 : CONTROLOBJECT < string name = \"MikuMikuTelop.x\"; >;");
        sb.AppendLine();
        sb.AppendLine("texture TelopDraw: OFFSCREENRENDERTARGET <");
        sb.AppendLine("    string Description = \"PostTelopDraw for MikuMikuTelop.fx\";");
        sb.AppendLine("    float2 ViewPortRatio = {1.0,1.0};");
        sb.AppendLine("    float4 ClearColor = { 0, 0, 0, 0 };");
        sb.AppendLine("    float ClearDepth = 1.0;");
        sb.AppendLine("    bool AntiAlias = true;");
        sb.AppendLine("    string DefaultEffect =");
        sb.AppendLine("        \"self = hide;\"");
        sb.AppendLine("        \"MikuMikuTelop.x = MikuMikuTelop.fx;\"");
        sb.AppendLine("        \"* = hide;\"");
        sb.AppendLine("    ;");
        sb.AppendLine(">;");
        sb.AppendLine();

        sb.AppendLine("sampler sampTelop = sampler_state {");
        sb.AppendLine("    texture = <TelopDraw>;");
        sb.AppendLine("    MinFilter = LINEAR;");
        sb.AppendLine("    MagFilter = LINEAR;");
        sb.AppendLine("    MipFilter = NONE;");
        sb.AppendLine("    AddressU  = CLAMP;");
        sb.AppendLine("    AddressV = CLAMP;");
        sb.AppendLine("};");
        sb.AppendLine();

        sb.AppendLine("float Script : STANDARDSGLOBAL <");
        sb.AppendLine("    string ScriptOutput = \"color\";");
        sb.AppendLine("    string ScriptClass = \"scene\";");
        sb.AppendLine("    string ScriptOrder = \"postprocess\";");
        sb.AppendLine("> = 0.8;");
        sb.AppendLine();
        sb.AppendLine();

        sb.AppendLine("float2 ViewportSize : VIEWPORTPIXELSIZE;");
        sb.AppendLine("static const float2 ViewportOffset = (float2(0.5,0.5)/ViewportSize);");
        sb.AppendLine();

        sb.AppendLine("texture2D DepthBuffer : RENDERDEPTHSTENCILTARGET <");
        sb.AppendLine("    float2 ViewPortRatio = {1.0,1.0};");
        sb.AppendLine("    string Format = \"D24S8\";");
        sb.AppendLine(">;");
        sb.AppendLine();

        sb.AppendLine("float4 ClearColor = {1,1,1,0};");
        sb.AppendLine("float ClearDepth  = 1.0;");
        sb.AppendLine();

        sb.AppendLine("////////////////////////////////////////////////////////////////////////////////////////////////");
        sb.AppendLine();

        sb.AppendLine("struct VS_OUTPUT {");
        sb.AppendLine("    float4 Pos            : POSITION;");
        sb.AppendLine("    float2 Tex            : TEXCOORD0;");
        sb.AppendLine("};");
        sb.AppendLine();

        sb.AppendLine("VS_OUTPUT VS_passDraw( float4 Pos : POSITION, float4 Tex : TEXCOORD0 ) {");
        sb.AppendLine("    VS_OUTPUT Out = (VS_OUTPUT)0;");
        sb.AppendLine("    Out.Pos = Pos;");
        sb.AppendLine("    Out.Tex = Tex + ViewportOffset;");
        sb.AppendLine("    return Out;");
        sb.AppendLine("}");
        sb.AppendLine();

        sb.AppendLine("float4 PS_passDraw( float2 Tex: TEXCOORD0 ) : COLOR {");
        sb.AppendLine("    float4 Color;");
        sb.AppendLine("    Color = tex2D( sampTelop, Tex );");
        sb.AppendLine("    Color.rgb /= Color.a;");
        sb.AppendLine("    Color = flag1 ? Color : float4(1,0,0,1);");
        sb.AppendLine("    return Color;");
        sb.AppendLine("}");
        sb.AppendLine();

        sb.AppendLine("////////////////////////////////////////////////////////////////////////////////////////////////");
        sb.AppendLine();

        sb.AppendLine("technique PostTelop <");
        sb.AppendLine("    string Script =");
        sb.AppendLine("        \"RenderColorTarget0=;\"");
        sb.AppendLine("        \"RenderDepthStencilTarget=;\"");
        sb.AppendLine("        \"ClearSetColor=ClearColor;\"");
        sb.AppendLine("        \"ClearSetDepth=ClearDepth;\"");
        sb.AppendLine("        \"Clear=Color;\"");
        sb.AppendLine("        \"Clear=Depth;\"");
        sb.AppendLine("        \"ScriptExternal=Color;\"");
        sb.AppendLine("        \"Pass=DrawTelop;\"");
        sb.AppendLine("    ;");
        sb.AppendLine("> {");
        sb.AppendLine("    pass DrawTelop < string Script= \"Draw=Buffer;\"; > {");
        sb.AppendLine("        AlphaBlendEnable = true;");
        sb.AppendLine("        ZEnable = false;");
        sb.AppendLine("        VertexShader = compile vs_2_0 VS_passDraw();");
        sb.AppendLine("        PixelShader  = compile ps_2_0 PS_passDraw();");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();

        File.WriteAllText(Path.Combine(dir, "PostTelop.fx"), sb.ToString(), new UTF8Encoding(false));
    }

    // ----------------
    // 导出 PostTelop .X 文件
    // ----------------
    private void ExportPostTelopX(string dir)
    {
        var sb = new StringBuilder();

        sb.AppendLine("xof 0303txt 0032");
        sb.AppendLine("Material __Color_A01_ {");
        sb.AppendLine("1.0000;0.0000;0.0000;1.0000;;");
        sb.AppendLine("5.0000;");
        sb.AppendLine("0.1000;0.0000;0.0000;;");
        sb.AppendLine("0.2000;0.0000;0.0000;;");
        sb.AppendLine("}");
        sb.AppendLine("Mesh mesh_0{");
        sb.AppendLine("4;");
        sb.AppendLine("0.1000;0.0000;0.1000;,");
        sb.AppendLine("-0.1000;0.0000;-0.1000;,");
        sb.AppendLine("-0.1000;0.0000;0.1000;,");
        sb.AppendLine("0.1000;0.0000;-0.1000;;");
        sb.AppendLine("2;");
        sb.AppendLine("3;0,1,2,");
        sb.AppendLine("3;1,0,3;;");
        sb.AppendLine("  MeshMaterialList {");
        sb.AppendLine("1;");
        sb.AppendLine("2;");
        sb.AppendLine("0,");
        sb.AppendLine("0;");
        sb.AppendLine("{ __Color_A01_ }");
        sb.AppendLine("  }");
        sb.AppendLine("  MeshTextureCoords {");
        sb.AppendLine("4;");
        sb.AppendLine("0.0000,0.0000;");
        sb.AppendLine("0.0000,0.0000;");
        sb.AppendLine("0.0000,0.0000;");
        sb.AppendLine("0.0000,0.0000;");
        sb.AppendLine("  }");
        sb.AppendLine("  MeshNormals {");
        sb.AppendLine("1;");
        sb.AppendLine("-0.0000;1.0000;-0.0000;;");
        sb.AppendLine("2;");
        sb.AppendLine("3;0,0,0;");
        sb.AppendLine("3;0,0,0;;");
        sb.AppendLine("  }");
        sb.AppendLine("}");

        File.WriteAllText(Path.Combine(dir, "PostTelop.x"), sb.ToString(), new UTF8Encoding(false));
    }

    // 退出前提示
    private async void OnExit(object? sender, RoutedEventArgs e)
    {
        if (_isModified)
        {
            var result = await ShowConfirmDialog("当前文件已修改，是否保存？");  // 当前文件已修改，是否保存？
            if (result == true)
                await SaveFile();
            else if (result == null)
                return;
        }

        Close();
    }

    #endregion
}
