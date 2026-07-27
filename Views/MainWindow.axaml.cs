using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MMTGui.Localization;
using MMTGui.Controls;
using SukiUI.Controls;

namespace MMTGui;

/// <summary>
/// MMT Subtitle Editor 主窗口
/// 负责字幕脚本编辑、预览、属性面板交互、文件操作和导出功能
/// </summary>
public partial class MainWindow : SukiWindow
{
    private sealed record ScriptSubtitleBlock(int TimeLineIndex, int TextEndLineIndexExclusive);

    /// <summary>字幕条目集合，用于字幕列表显示</summary>
    private ObservableCollection<SubtitleEntry> _subtitles = new();

    /// <summary>当前打开的文件路径</summary>
    private string? _currentFilePath;

    /// <summary>文件是否已修改（未保存）</summary>
    private bool _isModified;

    /// <summary>预览区缩放级别</summary>
    private double _zoomLevel = 1.0;

    /// <summary>预览播放定时器</summary>
    private DispatcherTimer? _previewTimer;

    private int _previewIndex = -1;

    private double _previewStartTime;

    private bool _isPreviewing;

    /// <summary>音频播放服务（基于 NAudio）。</summary>
    private AudioPlayerService? _audioPlayer;

    /// <summary>音频进度同步计时器（用于刷新进度条和时间显示）。</summary>
    private DispatcherTimer? _audioProgressTimer;

    /// <summary>是否正在程序代码中修改音频进度条（避免递归）。</summary>
    private bool _isUpdatingAudioProgress;

    /// <summary>剪贴板：复制的字幕条目</summary>
    private SubtitleEntry? _clipboardSubtitle;

    /// <summary>属性同步脚本时是否正在更新（防止递归）</summary>
    private bool _isSyncingFromProperty;

    /// <summary>撤销历史栈</summary>
    private Stack<string> _undoStack = new();

    /// <summary>重做历史栈</summary>
    private Stack<string> _redoStack = new();

    /// <summary>最大历史记录数</summary>
    private const int MaxHistorySize = 50;

    /// <summary>是否正在执行撤销/重做操作</summary>
    private bool _isUndoRedoing;
    private string _lastStatusMessage = "就绪";
    private AppLanguage? _loadedExampleLanguage;
    private bool _isUsingLocalizedExample = true;

    private const double PreviewBaseWidth = 640;
    private const double PreviewBaseHeight = 360;
    private Color _previewCanvasColor = Color.FromRgb(22, 33, 62);
    private string? _previewBackgroundImagePath;
    private Image? _previewBackgroundImage;
    private ComboBox? _cmbAnimationType;
    private ComboBox? _cmbAnimationDirection;
    private NumericUpDown? _nudAnimationDuration;
    private NumericUpDown? _nudAnimationBlindsCount;
    private NumericUpDown? _nudAnimationBlurStrength;

    /// <summary>新建文件命令</summary>
    public ICommand NewFileCommand { get; private set; } = null!;

    /// <summary>打开文件命令</summary>
    public ICommand OpenFileCommand { get; private set; } = null!;

    /// <summary>保存文件命令</summary>
    public ICommand SaveFileCommand { get; private set; } = null!;

    /// <summary>撤销命令</summary>
    public ICommand UndoCommand { get; private set; } = null!;

    /// <summary>重做命令</summary>
    public ICommand RedoCommand { get; private set; } = null!;

    /// <summary>剪切命令</summary>
    public ICommand CutCommand { get; private set; } = null!;

    /// <summary>复制命令</summary>
    public ICommand CopyCommand { get; private set; } = null!;

    /// <summary>粘贴命令</summary>
    public ICommand PasteCommand { get; private set; } = null!;

    /// <summary>全屏命令</summary>
    public ICommand FullScreenCommand { get; private set; } = null!;

    public MainWindow()
    {
        InitializeComponent();

        if (Design.IsDesignMode)
            return;

        InitializePreviewCanvasAppearance();
        InitializeAnimationControls();
        InitializeDebounceTimer();
        InitializeDefaults();
        LoadSystemFonts();
        SetupScriptEditor();
        InitializeCommands();
        InitializeLocalization();
        InitializeAudio();
        Closed += (_, _) => ShutdownAudio();

        // 初始化变换框管理器
         if (PreviewCanvas != null)
         {
             _transformBoxManager = new TransformBoxManager(PreviewCanvas);
             _transformBoxManager.TransformChanged += OnTransformBoxChanged;
         }
    }

    /// <summary>
    /// 初始化命令绑定
    /// </summary>
    private void InitializeCommands()
    {
        NewFileCommand = new SimpleCommand(_ => OnNewFile(null, null!));
        OpenFileCommand = new SimpleCommand(_ => OnOpenFile(null, null!));
        SaveFileCommand = new SimpleCommand(_ => OnSaveFile(null, null!));
        UndoCommand = new SimpleCommand(_ => OnUndo(null, null!));
        RedoCommand = new SimpleCommand(_ => OnRedo(null, null!));
        CutCommand = new SimpleCommand(_ => OnCut(null, null!));
        CopyCommand = new SimpleCommand(_ => OnCopy(null, null!));
        PasteCommand = new SimpleCommand(_ => OnPaste(null, null!));
        FullScreenCommand = new SimpleCommand(_ => OnToggleFullScreen(null, null!));

        DataContext = this;
    }

    /// <summary>
    /// 加载系统已安装的字体到字体下拉框
    /// </summary>
    private void LoadSystemFonts()
    {
        if (CmbFontName == null) return;

        CmbFontName.Items.Clear();

        // 字体常见列表
        // 常见字体：宋体、 黑体、 微软雅黑、 楷体、 ＭＳ ゴシック、 Meiryo、 Consolas
        var preferredFonts = new[] { "宋体", "黑体", "微软雅黑", "楷体", "ＭＳ ゴシック", "Meiryo", "Consolas" };  
        foreach (var font in preferredFonts)
        {
            CmbFontName.Items.Add(new ComboBoxItem { Content = font });
        }

        try
        {
            var systemFonts = FontManager.Current.SystemFonts
                .Select(f => f.Name)
                .Where(name => !preferredFonts.Contains(name))
                .OrderBy(name => name)
                .ToList();

            foreach (var font in systemFonts)
            {
                CmbFontName.Items.Add(new ComboBoxItem { Content = font });
            }
        }
        catch { }

        CmbFontName.SelectedIndex = 0;
    }

    /// <summary>
    /// 初始化默认脚本和字幕数据
    /// </summary>
    private void InitializeDefaults()
    {
        LoadLocalizedExampleScript(LocalizationService.CurrentLanguage);
        UpdateStatusBar();
    }

    /// <summary>
    /// 添加示例字幕
    /// </summary>
    private void AddSampleSubtitles()
    {
        _subtitles.Add(new SubtitleEntry { Index = 1, Text = "MikuMikuTelop 实用篇", StartTime = 0.5, EndTime = 6.0, Preset = "title" });  // MikuMikuTelop 实用篇
        _subtitles.Add(new SubtitleEntry { Index = 2, Text = "无论是看了上次介绍篇的朋友，还是没看的朋友", StartTime = 7.0, EndTime = 8.5, Preset = "def" });  // 无论是看了上次介绍篇的朋友，还是没看的朋友
        _subtitles.Add(new SubtitleEntry { Index = 3, Text = "欢迎来到MikuMikuTelop实用篇。", StartTime = 7.0, EndTime = 9.0, Preset = "def" });  // 欢迎来到MikuMikuTelop实用篇。
        _subtitles.Add(new SubtitleEntry { Index = 4, Text = "在这个视频中，我们将介绍字幕特效制作工具", StartTime = 8.0, EndTime = 10.5, Preset = "def" });  // 在这个视频中，我们将介绍字幕特效制作工具
        _subtitles.Add(new SubtitleEntry { Index = 5, Text = "※注意", StartTime = 9.0, EndTime = 10.8, Preset = "def" });  // ※注意
        _subtitles.Add(new SubtitleEntry { Index = 6, Text = "动画公开时的最新版本是1.2。", StartTime = 9.0, EndTime = 11.5, Preset = "def" });  // 动画公开时的最新版本是1.2。
        _subtitles.Add(new SubtitleEntry { Index = 7, Text = "１．开篇", StartTime = 13.0, EndTime = 18.0, Preset = "title" });  // １．开篇
        _subtitles.Add(new SubtitleEntry { Index = 8, Text = "首先必须说明的是 MikuMikuTelop就像视频编辑...", StartTime = 19.0, EndTime = 21.0, Preset = "def" });  // 首先必须说明的是 MikuMikuTelop就像视频编辑...
        _subtitles.Add(new SubtitleEntry { Index = 9, Text = "２．工具本体的使用方法", StartTime = 21.0, EndTime = 26.0, Preset = "title" });  // ２．工具本体的使用方法

        RefreshSubtitleList();
    }

    /// 设置脚本编辑器事件处理
    private void SetupScriptEditor()
    {
        if (ScriptEditor != null)
        {
            ScriptEditor.TextChanged += OnScriptTextChanged;
        }
    }

    /// <summary>
    /// 脚本文本变化时的处理：解析脚本更新字幕列表
    /// </summary>
    private void OnScriptTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isSyncingFromProperty) return;

        SaveToHistory();

        _isModified = true;
        ParseScriptToSubtitles();
    }

    /// <summary>
    /// 解析MMTScript脚本内容，更新字幕列表
    /// </summary>
    private void ParseScriptToSubtitles()
    {
        var script = ScriptEditor?.Text ?? string.Empty;
        var parsed = MmtScriptParser.Parse(script);

        var scriptScreen = ExtractScreenSettings(script);
        ApplyScreenSettingsToUi(scriptScreen.Width, scriptScreen.Height);

        _subtitles.Clear();
        int imageIndex = 0;
        for (int i = 0; i < parsed.Count; i++)
        {
            parsed[i].Index = i + 1;
            if (parsed[i].IsImage)
            {
                imageIndex++;
                parsed[i].ImageIndex = imageIndex;
            }
            _subtitles.Add(parsed[i]);
        }

        RefreshSubtitleList();
        UpdateStatusBar();
    }

    // 包括预览画布的初始化和动画控制初始化
    private void InitializePreviewCanvasAppearance()
    {
        EnsurePreviewBackgroundLayer();
        ApplyPreviewCanvasAppearance();
        UpdatePreviewFrame(1280, 720);
    }

    // 应用预览画布外观设置，包括背景颜色和背景图片
    private void ApplyPreviewCanvasAppearance()
    {
        if (PreviewBorder != null)
            PreviewBorder.Background = new SolidColorBrush(_previewCanvasColor);

        if (_previewBackgroundImage == null)
            return;

        if (string.IsNullOrWhiteSpace(_previewBackgroundImagePath) || !File.Exists(_previewBackgroundImagePath))
        {
            _previewBackgroundImage.Source = null;
            _previewBackgroundImage.IsVisible = false;
            return;
        }

        using var stream = File.OpenRead(_previewBackgroundImagePath);
        _previewBackgroundImage.Source = new Bitmap(stream);
        _previewBackgroundImage.IsVisible = true;
    }

    // 确保预览画布有背景图层，以便显示背景图片
    private void EnsurePreviewBackgroundLayer()
    {
        if (_previewBackgroundImage != null || PreviewBorder == null)
            return;

        if (PreviewBorder.Child is not Grid grid)
            return;

        _previewBackgroundImage = new Image
        {
            Stretch = Stretch.Fill,
            IsVisible = false
        };

        grid.Children.Insert(0, _previewBackgroundImage);
    }

    // 初始化动画控制
    private void InitializeAnimationControls()
    {
        if (_cmbAnimationType != null || NudTimeBase?.Parent?.Parent is not StackPanel timePanel)
            return;

        if (NudTimeBase.Parent is not Control timeBaseRow)
            return;

        var insertIndex = Math.Max(0, timePanel.Children.IndexOf(timeBaseRow) + 1);
        var lowTextBrush = new SolidColorBrush(Color.FromRgb(148, 163, 184));

        var typeRow = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("60,*"),
            Margin = new Thickness(0, 2, 0, 0)
        };

        typeRow.Children.Add(new TextBlock
        {
            Text = "动画方式",  // 动画方式
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = lowTextBrush
        });

        // 动画类型：淡入淡出 / 随机模糊 / 上滑进入 / 缩放弹出 / 擦除 / 百叶窗
        _cmbAnimationType = new ComboBox
        {
            Width = 140,
            SelectedIndex = 0,
            ItemsSource = new[]
            {
                CreateAnimationComboItem("淡入淡出", "fade"),  // 淡入淡出
                CreateAnimationComboItem("随机模糊", "random_blur"),  // 随机模糊
                CreateAnimationComboItem("上滑进入", "slide_up"),  // 上滑进入
                CreateAnimationComboItem("缩放弹出", "scale_pop"),  // 缩放弹出
                CreateAnimationComboItem("擦除", "wipe"),  // 擦除
                CreateAnimationComboItem("百叶窗", "blinds")  // 百叶窗
            }
        };
        _cmbAnimationType.SelectionChanged += OnAnimationTypeChanged;
        Grid.SetColumn(_cmbAnimationType, 1);
        typeRow.Children.Add(_cmbAnimationType);

        var durationRow = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("60,*,40,Auto"),
            Margin = new Thickness(0, 2, 0, 0)
        };
        // 动画时间：数值输入，单位秒，步长0.01，默认0.38
        durationRow.Children.Add(new TextBlock
        {
            Text = "动画时间",  // 动画时间
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = lowTextBrush
        });
        // 动画时间输入框
        _nudAnimationDuration = new NumericUpDown
        {
            Value = 0.38m,
            Minimum = 0,
            Increment = 0.01m,
            Width = 70,
            ShowButtonSpinner = false,
            FormatString = "0.00"
        };
        _nudAnimationDuration.KeyDown += OnNumericKeyDown;
        Grid.SetColumn(_nudAnimationDuration, 1);
        durationRow.Children.Add(_nudAnimationDuration);
        // 动画时间单位：秒
        var secLabel = new TextBlock
        {
            Text = "秒",  // 秒
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = lowTextBrush
        };
        Grid.SetColumn(secLabel, 2);
        durationRow.Children.Add(secLabel);
        var durationInsertBtn = new Button
        {
            Content = "插入",
            FontSize = 12,
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
            BorderThickness = new Thickness(1),
            Tag = "animtime",
            Margin = new Thickness(4, 0, 0, 0)
        };
        durationInsertBtn.Click += OnInsertAnimNudValue;
        Grid.SetColumn(durationInsertBtn, 3);
        durationRow.Children.Add(durationInsertBtn);

        timePanel.Children.Insert(insertIndex, typeRow);
        timePanel.Children.Insert(insertIndex + 1, durationRow);

        var directionRow = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("60,*"),
            Margin = new Thickness(0, 2, 0, 0)
        };
        // 擦除方向：左到右 / 右到左
        directionRow.Children.Add(new TextBlock
        {
            Text = "擦除方向",  // 擦除方向
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = lowTextBrush
        });
        // 擦除方向下拉框
        _cmbAnimationDirection = new ComboBox
        {
            Width = 140,
            SelectedIndex = 0,
            ItemsSource = new[]
            {
                CreateAnimationComboItem("左到右", "ltr"),  // 左到右
                CreateAnimationComboItem("右到左", "rtl")  // 右到左
            }
        };
        _cmbAnimationDirection.SelectionChanged += OnAnimationDirectionChanged;
        Grid.SetColumn(_cmbAnimationDirection, 1);
        directionRow.Children.Add(_cmbAnimationDirection);

        var blindsRow = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("60,*,40,Auto"),
            Margin = new Thickness(0, 2, 0, 0)
        };
        // 百叶窗条数：数值输入，整数，步长1，默认8
        blindsRow.Children.Add(new TextBlock
        {
            Text = "百叶数量",  // 百叶数量
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = lowTextBrush
        });

        _nudAnimationBlindsCount = new NumericUpDown
        {
            Value = 8,
            Minimum = 2,
            Maximum = 32,
            Increment = 1,
            Width = 70,
            ShowButtonSpinner = false,
            FormatString = "0"
        };
        _nudAnimationBlindsCount.KeyDown += OnNumericKeyDown;
        Grid.SetColumn(_nudAnimationBlindsCount, 1);
        blindsRow.Children.Add(_nudAnimationBlindsCount);
        // 百叶窗条数单位：条
        var blindsSuffix = new TextBlock
        {
            Text = "条",  // 条
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = lowTextBrush
        };
        Grid.SetColumn(blindsSuffix, 2);
        blindsRow.Children.Add(blindsSuffix);
        var blindsInsertBtn = new Button
        {
            Content = "插入",
            FontSize = 12,
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
            BorderThickness = new Thickness(1),
            Tag = "animblinds",
            Margin = new Thickness(4, 0, 0, 0)
        };
        blindsInsertBtn.Click += OnInsertAnimNudValue;
        Grid.SetColumn(blindsInsertBtn, 3);
        blindsRow.Children.Add(blindsInsertBtn);

        var blurRow = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("60,*,40,Auto"),
            Margin = new Thickness(0, 2, 0, 0)
        };
        // 模糊强度：数值输入，单位倍，步长0.1，默认1.0
        blurRow.Children.Add(new TextBlock
        {
            Text = "模糊强度",  // 模糊强度
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = lowTextBrush
        });

        _nudAnimationBlurStrength = new NumericUpDown
        {
            Value = 1.0m,
            Minimum = 0,
            Maximum = 4,
            Increment = 0.1m,
            Width = 70,
            ShowButtonSpinner = false,
            FormatString = "0.0"
        };
        _nudAnimationBlurStrength.KeyDown += OnNumericKeyDown;
        Grid.SetColumn(_nudAnimationBlurStrength, 1);
        blurRow.Children.Add(_nudAnimationBlurStrength);

        var blurSuffix = new TextBlock
        {
            Text = "倍",  // 倍
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = lowTextBrush
        };
        Grid.SetColumn(blurSuffix, 2);
        blurRow.Children.Add(blurSuffix);

        var blurInsertBtn = new Button
        {
            Content = "插入",
            FontSize = 12,
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
            BorderThickness = new Thickness(1),
            Tag = "animblur",
            Margin = new Thickness(4, 0, 0, 0)
        };
        blurInsertBtn.Click += OnInsertAnimNudValue;
        Grid.SetColumn(blurInsertBtn, 3);
        blurRow.Children.Add(blurInsertBtn);

        timePanel.Children.Insert(insertIndex + 2, directionRow);
        timePanel.Children.Insert(insertIndex + 3, blindsRow);
        timePanel.Children.Insert(insertIndex + 4, blurRow);
    }

    // 创建下拉框项
    private static ComboBoxItem CreateAnimationComboItem(string title, string value)
    {
        return new ComboBoxItem
        {
            Content = title,
            Tag = value
        };
    }

    // 规范动画类型
    private static string NormalizeAnimationType(string? value)
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

    // 获取动画类型ID
    private static int GetAnimationTypeId(string? value)
    {
        return NormalizeAnimationType(value) switch
        {
            "random_blur" => 1,
            "slide_up" => 2,
            "scale_pop" => 3,
            "wipe" => 4,
            "blinds" => 5,
            _ => 0
        };
    }

    // 获取动画参数1（根据动画类型返回不同的参数值）
    private static double GetAnimationParam1(SubtitleEntry sub)
    {
        return NormalizeAnimationType(sub.AnimationType) switch
        {
            "random_blur" => Math.Max(0, sub.AnimationBlurStrength),
            "wipe" => NormalizeAnimationDirection(sub.AnimationDirection) == "rtl" ? 1 : 0,
            "blinds" => Math.Max(2, sub.AnimationBlindsCount),
            _ => 0
        };
    }

    // 规范擦除方向
    private static string NormalizeAnimationDirection(string? value)
    {
        var key = (value ?? string.Empty).Trim().ToLowerInvariant();
        return key switch
        {
            "rtl" or "right_to_left" or "right" => "rtl",
            _ => "ltr"
        };
    }

    private enum PreviewClipMode
    {
        None,
        Wipe,
        Blinds
    }

    private readonly record struct PreviewAnimationState(
        double Alpha,
        double OffsetX,
        double OffsetY,
        double ScaleMultiplier,
        double BlurStrength,
        PreviewClipMode ClipMode,
        double ClipProgress,
        bool ReverseDirection,
        int StripeCount)
    {
        public static PreviewAnimationState Default => new(1.0, 0, 0, 1.0, 0, PreviewClipMode.None, 1.0, false, 8);
    }

    // 获取预览动画状态
    private static PreviewAnimationState GetPreviewAnimationState(SubtitleEntry sub, double currentTime, double fontSize)
    {
        var fadeDuration = sub.Fade > 0 ? sub.Fade : 0;
        var alpha = 1.0;
        if (fadeDuration > 0)
        {
            var fadeIn = Math.Clamp((currentTime - sub.StartTime) / fadeDuration, 0, 1);
            var fadeOut = Math.Clamp((sub.EndTime - currentTime) / fadeDuration, 0, 1);
            alpha = Math.Min(fadeIn, fadeOut);
        }

        var animDuration = sub.AnimationDuration > 0
            ? sub.AnimationDuration
            : (sub.Fade > 0 ? sub.Fade : 0.38);
        animDuration = Math.Max(0.0001, animDuration);

        var animIn = Math.Clamp((currentTime - sub.StartTime) / animDuration, 0, 1);
        var animOut = Math.Clamp((sub.EndTime - currentTime) / animDuration, 0, 1);
        var animEdge = Math.Min(animIn, animOut);

        var type = NormalizeAnimationType(sub.AnimationType);
        var state = PreviewAnimationState.Default with { Alpha = alpha };

        return type switch
        {
            "random_blur" => state with { BlurStrength = (1 - animEdge) * Math.Max(0, sub.AnimationBlurStrength) },
            "slide_up" => state with
            {
                OffsetY = (1 - animIn) * (-fontSize * 0.35) + (1 - animOut) * (fontSize * 0.18)
            },
            "scale_pop" => state with
            {
                ScaleMultiplier = ((animIn < 0.65
                    ? Lerp(0.68, 1.10, animIn / 0.65)
                    : Lerp(1.10, 1.0, (animIn - 0.65) / 0.35))) * Lerp(0.90, 1.0, animOut)
            },
            "wipe" => state with
            {
                ClipMode = PreviewClipMode.Wipe,
                ClipProgress = Math.Max(0.02, animEdge),
                ReverseDirection = NormalizeAnimationDirection(sub.AnimationDirection) == "rtl"
            },
            "blinds" => state with
            {
                ClipMode = PreviewClipMode.Blinds,
                ClipProgress = Math.Max(0.02, animEdge),
                StripeCount = Math.Max(2, sub.AnimationBlindsCount)
            },
            _ => state
        };
    }

    // 获取预览样本时间
    private static double GetPreviewSampleTime(SubtitleEntry sub)
    {
        var animDuration = sub.AnimationDuration > 0
            ? sub.AnimationDuration
            : (sub.Fade > 0 ? sub.Fade : 0.38);
        var sampleOffset = Math.Min(Math.Max(animDuration * 0.65, 0.18), Math.Max(0.18, (sub.EndTime - sub.StartTime) * 0.45));
        return Math.Clamp(sub.StartTime + sampleOffset, sub.StartTime, sub.EndTime);
    }

    // 线性插值函数
    private static double Lerp(double from, double to, double t)
    {
        return from + (to - from) * Math.Clamp(t, 0, 1);
    }

    // 获取动画显示名称
    private static string GetAnimationDisplayName(string? value)
    {
        return NormalizeAnimationType(value) switch
        {
            "random_blur" => LocalizationService.T("随机模糊"),
            "slide_up" => LocalizationService.T("上滑进入"),
            "scale_pop" => LocalizationService.T("缩放弹出"),
            "wipe" => LocalizationService.T("擦除"),
            "blinds" => LocalizationService.T("百叶窗"),
            _ => LocalizationService.T("淡入淡出")
        };
    }

    // 获取动画状态文本
    private static string GetAnimationStatusText(SubtitleEntry sub)
    {
        var duration = sub.AnimationDuration > 0 ? sub.AnimationDuration : sub.Fade;
        var baseText = $"{LocalizationService.T("动画方式")}: {GetAnimationDisplayName(sub.AnimationType)} {duration:0.##}{LocalizationService.T("秒")}";

        return NormalizeAnimationType(sub.AnimationType) switch
        {
            "wipe" => $"{baseText} {(NormalizeAnimationDirection(sub.AnimationDirection) == "rtl" ? LocalizationService.T("右到左") : LocalizationService.T("左到右"))}",
            "blinds" => $"{baseText} {Math.Max(2, sub.AnimationBlindsCount)}{LocalizationService.T("条")}",
            "random_blur" => $"{baseText} {LocalizationService.T("模糊强度")}{Math.Max(0, sub.AnimationBlurStrength):0.##}",
            _ => baseText
        };
    }

    // 根据字幕条目的动画属性更新动画控制UI
    private void UpdateAnimationControlsFromSubtitle(SubtitleEntry sub)
    {
        if (_cmbAnimationType == null || _nudAnimationDuration == null)
            return;

        _isSyncingFromProperty = true;
        try
        {
            var animationType = NormalizeAnimationType(sub.AnimationType);
            foreach (var item in _cmbAnimationType.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Tag?.ToString(), animationType, StringComparison.OrdinalIgnoreCase))
                {
                    _cmbAnimationType.SelectedItem = item;
                    break;
                }
            }

            var effectiveDuration = sub.AnimationDuration > 0 ? sub.AnimationDuration : sub.Fade;
            _nudAnimationDuration.Value = (decimal)effectiveDuration;
            if (_cmbAnimationDirection != null)
            {
                var direction = NormalizeAnimationDirection(sub.AnimationDirection);
                foreach (var item in _cmbAnimationDirection.Items.OfType<ComboBoxItem>())
                {
                    if (string.Equals(item.Tag?.ToString(), direction, StringComparison.OrdinalIgnoreCase))
                    {
                        _cmbAnimationDirection.SelectedItem = item;
                        break;
                    }
                }
            }

            if (_nudAnimationBlindsCount != null)
                _nudAnimationBlindsCount.Value = sub.AnimationBlindsCount;

            if (_nudAnimationBlurStrength != null)
                _nudAnimationBlurStrength.Value = (decimal)sub.AnimationBlurStrength;
        }
        finally
        {
            _isSyncingFromProperty = false;
        }
    }

    // 应用屏幕设置到UI
       private void ApplyScreenSettingsToUi(double width, double height)
    {
        width = width > 0 ? width : 1280;
        height = height > 0 ? height : 720;

        _isSyncingFromProperty = true;
        try
        {
            if (NudScreenWidth != null)
                NudScreenWidth.Value = (decimal)width;
            if (NudScreenHeight != null)
                NudScreenHeight.Value = (decimal)height;
        }
        finally
        {
            _isSyncingFromProperty = false;
        }

        UpdatePreviewFrame(width, height);
        UpdateStatusBar();
    }

    // 从脚本中提取屏幕设置
    private static (double Width, double Height) ExtractScreenSettings(string script)
    {
        var width = 1280.0;
        var height = 720.0;

        var lines = script.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("//", StringComparison.Ordinal))
                continue;

            if (line.StartsWith(":screen ", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Substring(8).Trim().Split(',');
                if (parts.Length >= 1 && double.TryParse(parts[0].Trim(), out var parsedWidth) && parsedWidth > 0)
                    width = parsedWidth;
                if (parts.Length >= 2 && double.TryParse(parts[1].Trim(), out var parsedHeight) && parsedHeight > 0)
                    height = parsedHeight;
            }
            else if (line.StartsWith(":screen_height ", StringComparison.OrdinalIgnoreCase))
            {
                if (double.TryParse(line.Substring(15).Trim(), out var parsedHeight) && parsedHeight > 0)
                    height = parsedHeight;
            }
        }

        return (width, height);
    }

    /// <summary>
    /// 刷新字幕列表UI
    /// </summary>
    private void RefreshSubtitleList()
    {
        if (SubtitleList == null) return;

        SubtitleList.Items.Clear();
        foreach (var sub in _subtitles)
        {
            var item = CreateSubtitleListItem(sub);
            SubtitleList.Items.Add(item);
        }

        if (SubtitleCountLabel != null)
            SubtitleCountLabel.Text = LocalizationService.F("共 {0} 条字幕", _subtitles.Count);  // 共 条字幕
    }

    /// <summary>
    /// 创建字幕列表项
    /// </summary>
    private ListBoxItem CreateSubtitleListItem(SubtitleEntry sub)
    {
        var grid = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("36, *, 180"),
            Height = 28
        };

        var indexBlock = new TextBlock
        {
            Text = sub.Index.ToString(),
            FontSize = 11,
            FontFamily = new FontFamily("Consolas"),
            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(indexBlock, 0);

        var textBlock = new TextBlock
        {
            Text = sub.IsImage
                ? LocalizationService.F("图片{0}", sub.ImageIndex)
                : sub.Text,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(textBlock, 1);

        var timeBlock = new TextBlock
        {
            Text = FormatTime(sub.StartTime) + " → " + FormatTime(sub.EndTime),
            FontSize = 11,
            FontFamily = new FontFamily("Consolas"),
            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        Grid.SetColumn(timeBlock, 2);

        grid.Children.Add(indexBlock);
        grid.Children.Add(textBlock);
        grid.Children.Add(timeBlock);

        return new ListBoxItem { Padding = new Thickness(0), Content = grid };
    }

    /// <summary>
    /// 将秒数格式化为时间字符串 (H:MM:SS.CC)
    /// </summary>
    private static string FormatTime(double seconds)
    {
        if (seconds < 0) seconds = 0;
        var ts = TimeSpan.FromSeconds(seconds);
        return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds / 10:D2}";
    }

    /// <summary>
    /// 更新底部状态栏信息
    /// </summary>
    private void UpdateStatusBar()
    {
        // 状态栏显示逻辑：根据当前字幕数量、分辨率和预设状态更新显示内容
        if (StatusSubtitleCount != null)
            StatusSubtitleCount.Text = LocalizationService.F("字幕: {0} 条", _subtitles.Count);  // 字幕: 条

        // 分辨率显示逻辑：如果脚本中有 :screen 设置，则显示分辨率；否则显示默认提示
        if (StatusResolution != null && NudScreenWidth != null && NudScreenHeight != null)
            StatusResolution.Text = LocalizationService.F("画面: {0}×{1}", NudScreenWidth.Value, NudScreenHeight.Value);  // 画面: ×

        // 预设显示逻辑：如果当前选中字幕有预设，则显示预设名称；否则显示默认提示
        //if (StatusPreset != null && CmbPreset != null && CmbPreset.SelectedIndex >= 0)
        //{
        //    var item = CmbPreset.Items.Cast<ComboBoxItem>().ElementAtOrDefault(CmbPreset.SelectedIndex);
        //    if (item?.Content is string presetName)
        //        StatusPreset.Text = $"预设: {presetName.Split(' ').FirstOrDefault()}";
        //}

        // 窗口标题显示当前文件名和修改状态
        var fileName = _currentFilePath != null ? Path.GetFileNameWithoutExtension(_currentFilePath) : "未命名";  // 未命名
        var modified = _isModified ? "*" : "";
        Title = $"{fileName}{modified} - MMT Subtitle Editor";
    }

    /// <summary>
    /// 变换框管理器变换事件处理
    /// </summary>
    private void OnTransformBoxChanged(TransformChangedEventArgs e)
    {
        if (e.Entry == null) return;

        // 更新属性面板数值、脚本与预览
        Dispatcher.UIThread.Post(() =>
        {
            _isSyncingFromProperty = true;
            try
            {
                NudImagePosX.Value = (decimal)e.Entry.PosX;
                NudImagePosY.Value = (decimal)e.Entry.PosY;
                NudImageScaleX.Value = (decimal)e.Entry.ImageScaleX;
                NudImageScaleY.Value = (decimal)e.Entry.ImageScaleY;
                NudImageRotation.Value = (decimal)e.Entry.ImageRotation;
            }
            finally
            {
                _isSyncingFromProperty = false;
            }

            // 实时更新脚本与预览画面
            UpdateImageCommandsInScript(e.Entry);
            RenderPreviewFromSubtitle(e.Entry);
        });
    }
    // ========= END ============

}

