using System;
using System.Linq;
using Avalonia;
using Avalonia.Media;

namespace MMTGui;

/// <summary>
/// 贴图尺寸与变换度量的单一来源。
///
/// 背景：过去解析阶段（<see cref="MmtScriptParser.MeasureTextureSize"/>）与导出阶段
/// （MainWindow.ExportImageSequence）各自实现了一套尺寸算法，两者结果不一致，
/// 导致 <c>:pos_next</c> / <c>:pos_nextline</c> 的前进量与实际贴图宽度对不上，
/// 相邻字幕在 MMD 中互相重叠，表现为"字幕缺失"。
///
/// 现在解析与导出都只走这里，保证 <c>TextureWidth</c> / <c>TextureHeight</c>
/// 与磁盘上 PNG 的 <c>PixelSize</c> 逐条相等。
/// </summary>
public static class TextureMetrics
{
    /// <summary>边距余量的兜底像素值（边框完全贴边时的最小留白）。</summary>
    public const double EdgeBleedFloor = 16.0;

    /// <summary>
    /// 计算覆盖描边、阴影与字形墨水外扩所需的余量。
    /// 取边距百分比与阴影百分比中的较大者，再加上固定的兜底留白。
    /// </summary>
    public static double GetRenderSafetyPadding(
        double fontSize,
        double edgeWidth,
        double shadowDistance,
        double shadowBlur,
        double edge2Width = 0,
        double edge3Width = 0,
        double edge4Width = 0,
        double glowRadius = 0)
    {
        var maxEdge = Math.Max(Math.Max(edgeWidth, edge2Width), Math.Max(edge3Width, edge4Width));

        if (glowRadius > maxEdge)
            maxEdge = glowRadius;

        var edgePad = fontSize * maxEdge * 2 / 100.0;
        var shadowPad = fontSize * Math.Max(shadowDistance, shadowBlur) / 100.0;

        return Math.Ceiling(Math.Max(edgePad, shadowPad) + EdgeBleedFloor);
    }

    /// <summary>
    /// 缩放变换带来的余量放大比例。
    /// </summary>
    public static double GetTransformPaddingScale(double scaleX, double scaleY)
        => Math.Max(Math.Abs(scaleX), Math.Abs(scaleY)) / 100.0;

    /// <summary>
    /// 统一的字体尺寸换算：字号(pt) → 像素。
    /// 约定 <c>:screen</c> 高度对应 200pt，与 MikuMikuTelop 原版一致。
    /// </summary>
    public static double GetFontSizeInPixels(double screenHeight, double fontSize)
        => screenHeight * fontSize / 200.0;

    /// <summary>
    /// 计算屏幕宽高比（用于 <c>:pos_next</c> 的边距折算）。
    /// 高度无效时回退为 1，避免除零。
    /// </summary>
    public static double GetScreenAspect(double screenWidth, double screenHeight)
        => screenHeight <= 0 ? 1.0 : screenWidth / screenHeight;

    /// <summary>
    /// 创建以图层中心为基准的缩放 + 旋转矩阵，并把变换后的包围盒平移到位。
    /// </summary>
    public static (Matrix Matrix, double Width, double Height) CreateLayerTransform(
        double width,
        double height,
        double scaleX,
        double scaleY,
        double rotation,
        double offsetX = 0,
        double offsetY = 0)
    {
        var centerX = width / 2.0;
        var centerY = height / 2.0;

        var scaleMatrix = Matrix.CreateScale(scaleX / 100.0, scaleY / 100.0);
        var rotationMatrix = Matrix.CreateRotation(Math.PI * rotation / 180.0);
        var centeredMatrix =
            Matrix.CreateTranslation(-centerX, -centerY) *
            scaleMatrix *
            rotationMatrix *
            Matrix.CreateTranslation(centerX, centerY);

        var corners = new[]
        {
            new Point(0, 0),
            new Point(width, 0),
            new Point(width, height),
            new Point(0, height)
        };

        var transformedCorners = corners.Select(c => c.Transform(centeredMatrix)).ToArray();
        var minX = transformedCorners.Min(c => c.X);
        var maxX = transformedCorners.Max(c => c.X);
        var minY = transformedCorners.Min(c => c.Y);
        var maxY = transformedCorners.Max(c => c.Y);

        var positionedMatrix = centeredMatrix * Matrix.CreateTranslation(offsetX - minX, offsetY - minY);

        return (positionedMatrix, maxX - minX, maxY - minY);
    }

    /// <summary>
    /// 计算文本图层的画布尺寸（不含安全边距）。
    /// </summary>
    public static (double Width, double Height) GetLayerSize(
        double measuredWidth,
        double measuredHeight,
        double fontSize,
        double marginX,
        double marginY)
    {
        var marginPxX = fontSize * marginX / 100.0;
        var marginPxY = fontSize * marginY / 100.0;

        return (
            Math.Max(1.0, measuredWidth + marginPxX * 2.0),
            Math.Max(1.0, measuredHeight + marginPxY * 2.0));
    }

    /// <summary>
    /// 测量文本的真实墨水包围盒（相对排版原点）。
    ///
    /// <see cref="FormattedText.Width"/> 只是**排版前进宽度(advance)**，
    /// 不含字形外扩、斜体倾斜、圆体字的长撇捺。
    /// 对中日文字形（如「生」「脱」「骨」）以及斜体英文，
    /// 墨水边界会明显超出 advance —— 这正是字幕两端笔画被切平的原因。
    ///
    /// 本方法通过 <see cref="FormattedText.BuildGeometry"/> 拿到真实轮廓，
    /// 返回相对 (0,0) 的四向外扩量，供画布尺寸与渲染原点使用。
    /// </summary>
    public static InkBounds MeasureInkBounds(
        string text,
        Typeface typeface,
        double fontSize,
        bool isTate,
        double spacing)
    {
        if (string.IsNullOrEmpty(text))
            return default;

        // 轮廓用于确定字形本身的墨水范围。
        // 这里刻意保持**左对齐**：调用方（ComputeTextureLayout）会依据墨水盒
        // 自己决定文本落点，若在此再套 TextAlignment 会造成二次偏移。
        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.White);

        var geometry = formatted.BuildGeometry(new Point(0, 0));
        if (geometry == null)
        {
            // 拿不到轮廓时退化为 advance，至少不比原来差。
            return new InkBounds(0, formatted.Width, 0, formatted.Height);
        }

        var b = geometry.Bounds;

        // 逐字渲染（spacing != 0 或竖排）时几何体是按单字拼的，
        // BuildGeometry 仍然给出整段轮廓，行为一致。
        return new InkBounds(b.Left, b.Right, b.Top, b.Bottom);
    }

    /// <summary>
    /// 文本墨水包围盒（相对排版原点的四向坐标，单位为像素）。
    /// </summary>
    public readonly record struct InkBounds(double Left, double Right, double Top, double Bottom)
    {
        /// <summary>墨水向左溢出的像素数（相对 advance 原点 0）。</summary>
        public double OverflowLeft => Math.Max(0.0, -Left);

        /// <summary>墨水向右溢出的像素数（相对 advance 宽度）。</summary>
        public double OverflowRight(double advanceWidth) => Math.Max(0.0, Right - advanceWidth);

        /// <summary>墨水向上溢出的像素数。</summary>
        public double OverflowTop => Math.Max(0.0, -Top);

        /// <summary>墨水向下溢出的像素数。</summary>
        public double OverflowBottom(double lineHeight) => Math.Max(0.0, Bottom - lineHeight);
    }

    /// <summary>
    /// 计算最终的安全边距：把描边/阴影/发光的**笔宽外扩**与
    /// 字形墨水的**四向溢出**一并计入，并保留少量兜底留白。
    /// </summary>
    /// <param name="fontSize">字号（像素）。</param>
    /// <param name="ink">墨水包围盒；传 default 则只按笔宽计算（保守回退）。</param>
    /// <param name="advanceWidth">排版前进宽度，用于算右溢出。</param>
    /// <param name="lineHeight">排版行高，用于算下溢出。</param>
    /// <param name="strokeOutset">所有描边/发光层中最大的**半笔宽**外扩。</param>
    public static (double Left, double Right, double Top, double Bottom) GetInkAwarePadding(
        InkBounds ink,
        double advanceWidth,
        double lineHeight,
        double strokeOutset,
        double shadowOffset = 0)
    {
        // 字形墨水溢出 + 笔宽外扩 + 阴影偏移，取四向各自的最大值。
        var left = Math.Max(ink.OverflowLeft, strokeOutset) + Math.Max(0.0, -shadowOffset);
        var right = Math.Max(ink.OverflowRight(advanceWidth), strokeOutset) + Math.Max(0.0, shadowOffset);
        var top = Math.Max(ink.OverflowTop, strokeOutset);
        var bottom = Math.Max(ink.OverflowBottom(lineHeight), strokeOutset) + Math.Max(0.0, shadowOffset);

        // 兜底留白，避免抗锯齿边缘贴到纹理边界。
        return (
            Math.Ceiling(left + EdgeBleedFloor),
            Math.Ceiling(right + EdgeBleedFloor),
            Math.Ceiling(top + EdgeBleedFloor),
            Math.Ceiling(bottom + EdgeBleedFloor));
    }

    /// <summary>
    /// 计算描边/阴影/发光带来的**半笔宽**外扩。
    ///
    /// 渲染用的是 <c>DrawGeometry(null, pen, geo)</c>，pen 以字形轮廓为中心线，
    /// 所以实际外扩是 <c>penWidth / 2</c>（再加圆角连接的少许增量）。
    /// 过去这里误用了整个 <c>penWidth</c>，且只看 edgeWidth、漏掉多层描边与发光。
    /// </summary>
    public static double GetStrokeOutset(
        double fontSize,
        double edgeWidth,
        double edge2Width,
        double edge3Width,
        double edge4Width,
        double glowRadius,
        bool edgeRound)
    {
        var maxWidth = Math.Max(Math.Max(edgeWidth, edge2Width), Math.Max(edge3Width, edge4Width));

        // 外发光是 12 层叠加，最外层半径约 glowRad * 2.5（见 RenderTextCore）。
        var glowOutset = glowRadius > 0 ? fontSize * glowRadius / 100.0 * 2.5 : 0.0;

        var strokeOutset = fontSize * maxWidth * 2 / 100.0 / 2.0;

        // 圆角连接会在拐角处再外扩约半个笔宽。
        if (edgeRound && maxWidth > 0)
            strokeOutset *= 1.5;

        return Math.Ceiling(Math.Max(strokeOutset, glowOutset));
    }

    /// <summary>
    /// 计算导出贴图的最终像素尺寸（四向独立的安全边距）。
    ///
    /// 解析阶段与导出阶段必须都调用本方法，才能保证
    /// <c>TextureWidth</c> / <c>TextureHeight</c> 与实际 PNG 尺寸一致。
    /// </summary>
    /// <returns>
    /// 贴图像素尺寸；绘制时的变换偏移；以及文本在贴图内的排版原点偏移。
    /// </returns>
    /// <remarks>
    /// 贴图是**紧贴墨水包围盒**裁出来的，因此 <c>:align_*</c> / <c>:valign_*</c>
    /// 不再影响贴图尺寸与文本落点 —— 在 MMD 中定位由 <c>:pos_x</c> / <c>:pos_y</c>
    /// 配合 .x 的对齐子集完成。这保证了字幕两边永远不会有不对称的空白被切掉。
    /// </remarks>
    public static TextureLayout ComputeTextureLayout(
        string text,
        Typeface typeface,
        bool isTate,
        double spacing,
        double measuredWidth,
        double measuredHeight,
        double fontSize,
        double marginX,
        double marginY,
        double scaleX,
        double scaleY,
        double rotation,
        double edgeWidth,
        double shadowDistance,
        double shadowBlur,
        double edge2Width = 0,
        double edge3Width = 0,
        double edge4Width = 0,
        double glowRadius = 0,
        bool edgeRound = true)
    {
        // --- 1. 真实墨水包围盒（左对齐基准；对齐由布局自己处理）---
        var ink = MeasureInkBounds(text, typeface, fontSize, isTate, spacing);
        if (ink.Right <= ink.Left && ink.Bottom <= ink.Top)
            ink = new InkBounds(0, measuredWidth, 0, measuredHeight);

        // --- 2. 笔宽外扩（取各层描边/发光的最大值）---
        var strokeOutset = GetStrokeOutset(
            fontSize, edgeWidth, edge2Width, edge3Width, edge4Width, glowRadius, edgeRound);

        // --- 3. 阴影偏移量 ---
        var shadowOffset = fontSize * shadowDistance / 100.0;

        // --- 4. 四向安全边距 ---
        var pad = GetInkAwarePadding(ink, measuredWidth, measuredHeight, strokeOutset, shadowOffset);

        // --- 5. 图层内容区：在墨水范围外再留出 marginX/marginY ---
        var marginPxX = fontSize * marginX / 100.0;
        var marginPxY = fontSize * marginY / 100.0;

        // 墨水相对排版原点 (0,0) 的偏移，决定文本该画在哪里。
        var inkLeft = ink.Left;
        var inkTop = ink.Top;
        var inkW = Math.Max(1.0, ink.Right - ink.Left);
        var inkH = Math.Max(1.0, ink.Bottom - ink.Top);

        // 内容盒 = 墨水盒 + 四周 margin
        var contentW = inkW + marginPxX * 2.0;
        var contentH = inkH + marginPxY * 2.0;

        // 内容盒相对排版原点(0,0)的左上角。
        // 墨水左溢出 3px、margin 2px 时内容盒左上角在 -5px，
        // 从该点起算，墨水恰好落在内容盒内并留出 margin。
        var contentLeft = inkLeft - marginPxX;
        var contentTop = inkTop - marginPxY;

        // --- 6. 叠加缩放 / 旋转，并按四向安全边距平移到位 ---
        var safeScale = GetTransformPaddingScale(scaleX, scaleY);
        var scaledPadLeft = Math.Ceiling(pad.Left * safeScale);
        var scaledPadRight = Math.Ceiling(pad.Right * safeScale);
        var scaledPadTop = Math.Ceiling(pad.Top * safeScale);
        var scaledPadBottom = Math.Ceiling(pad.Bottom * safeScale);

        // offset 把变换后的内容盒推到安全边距之内（各自独立，不再强制对称）。
        var transformed = CreateLayerTransform(
            contentW, contentH, scaleX, scaleY, rotation,
            scaledPadLeft, scaledPadTop);

        var texW = (int)Math.Ceiling(transformed.Width + scaledPadLeft + scaledPadRight);
        var texH = (int)Math.Ceiling(transformed.Height + scaledPadTop + scaledPadBottom);

        if (texW < 1) texW = 1;
        if (texH < 1) texH = 1;

        return new TextureLayout(
            texW,
            texH,
            contentW,
            contentH,
            scaledPadLeft,
            scaledPadTop,
            contentLeft,
            contentTop);
    }

    /// <summary>
    /// 贴图布局结果。
    /// </summary>
    /// <param name="TextureWidth">贴图宽度（像素）。</param>
    /// <param name="TextureHeight">贴图高度（像素）。</param>
    /// <param name="ContentWidth">缩放前的图层内容宽度。</param>
    /// <param name="ContentHeight">缩放前的图层内容高度。</param>
    /// <param name="OffsetX">变换后内容的左上角在贴图中的 X 偏移。</param>
    /// <param name="OffsetY">变换后内容的左上角在贴图中的 Y 偏移。</param>
    /// <param name="ContentLeft">内容盒左上角相对排版原点的 X（通常为负）。</param>
    /// <param name="ContentTop">内容盒左上角相对排版原点的 Y（通常为负）。</param>
    public readonly record struct TextureLayout(
        int TextureWidth,
        int TextureHeight,
        double ContentWidth,
        double ContentHeight,
        double OffsetX,
        double OffsetY,
        double ContentLeft,
        double ContentTop);
}
