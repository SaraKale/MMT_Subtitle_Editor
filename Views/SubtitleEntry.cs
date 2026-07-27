using Avalonia.Media;

namespace MMTGui;

/// <summary>
/// 字幕条目数据模型。
/// 描述一条字幕在脚本、预览和导出过程中需要的全部属性。
/// </summary>
public class SubtitleEntry
{
    public int Index { get; set; }
    public string Text { get; set; } = "";
    public double StartTime { get; set; }
    public double EndTime { get; set; }
    public string Preset { get; set; } = "def";
    public double PosX { get; set; }
    public double PosY { get; set; }
    public double PosZ { get; set; }
    public double Fade { get; set; } = 0.38;
    public double Alpha { get; set; } = 1.0;
    public bool Billboard { get; set; }
    public int HAlign { get; set; } = 1;
    public int VAlign { get; set; } = 2;
    public int TextureWidth { get; set; }
    public int TextureHeight { get; set; }

    public string FontName { get; set; } = "Yu Gothic";
    public double FontSize { get; set; } = 13;
    public Color FontColor { get; set; } = Colors.White;
    public Color EdgeColor { get; set; } = Colors.Black;
    public double EdgeWidth { get; set; } = 8;
    public bool EdgeRound { get; set; } = true;

    /// <summary>外层描边粗细（百分比，0=禁用）。用于双层描边效果。</summary>
    public double Edge2Width { get; set; } = 0;

    /// <summary>外层描边颜色。</summary>
    public Color Edge2Color { get; set; } = Colors.White;

    /// <summary>第三层描边粗细（百分比，0=禁用）。</summary>
    public double Edge3Width { get; set; } = 0;

    /// <summary>第三层描边颜色。</summary>
    public Color Edge3Color { get; set; } = Color.FromRgb(128, 128, 128);

    /// <summary>第四层描边粗细（百分比，0=禁用）。</summary>
    public double Edge4Width { get; set; } = 0;

    /// <summary>第四层描边颜色。</summary>
    public Color Edge4Color { get; set; } = Color.FromRgb(64, 64, 64);

    /// <summary>外发光半径（百分比，0=禁用）。</summary>
    public double GlowRadius { get; set; } = 0;

    /// <summary>外发光颜色。</summary>
    public Color GlowColor { get; set; } = Color.FromRgb(255, 200, 100);

    /// <summary>外发光不透明度（0-1.0）。</summary>
    public double GlowAlpha { get; set; } = 0.8;

    /// <summary>是否启用描边渐变色。</summary>
    public bool EdgeGradientEnabled { get; set; }

    /// <summary>描边渐变顶部颜色。</summary>
    public Color EdgeGradientTop { get; set; } = Colors.Red;

    /// <summary>描边渐变底部颜色。</summary>
    public Color EdgeGradientBottom { get; set; } = Colors.Blue;

    /// <summary>是否启用文字渐变色。</summary>
    public bool TextGradientEnabled { get; set; }

    /// <summary>文字渐变顶部颜色。</summary>
    public Color TextGradientTop { get; set; } = Colors.Red;

    /// <summary>文字渐变底部颜色。</summary>
    public Color TextGradientBottom { get; set; } = Colors.Blue;

    /// <summary>文字渐变方向（vertical/horizontal）。</summary>
    public string TextGradientDirection { get; set; } = "vertical";

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
    public double Rotation { get; set; } = 0;
    public double Spacing { get; set; } = 0;
    public string AnimationType { get; set; } = "fade";
    public double AnimationDuration { get; set; }
    public string AnimationDirection { get; set; } = "ltr";
    public int AnimationBlindsCount { get; set; } = 8;
    public double AnimationBlurStrength { get; set; } = 1.0;

        /// <summary>是否为图片条目</summary>
    public bool IsImage { get; set; }
    
    /// <summary>图片路径（相对路径或绝对路径）</summary>
    public string? ImagePath { get; set; }
    
    /// <summary>图片水平缩放百分比（100 = 原始大小）</summary>
    public double ImageScaleX { get; set; } = 100;
    
    /// <summary>图片垂直缩放百分比</summary>
    public double ImageScaleY { get; set; } = 100;
    
    /// <summary>图片旋转角度（度）</summary>
    public double ImageRotation { get; set; } = 0;
    
    /// <summary>图片透明度（0-1）</summary>
    public double ImageAlpha { get; set; } = 1.0;
    
    /// <summary>图片序号（1-based，用于字幕列表显示“图片1”、“图片2”等）</summary>
    public int ImageIndex { get; set; }

    /// <summary>原始图片宽度（像素，加载时自动获取）</summary>
    public int OriginalImageWidth { get; set; }
    
    /// <summary>原始图片高度（像素，加载时自动获取）</summary>
    public int OriginalImageHeight { get; set; }
}
