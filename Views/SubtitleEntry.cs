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
}
