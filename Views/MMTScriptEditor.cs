using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace MMTGui.Controls;

/// <summary>
/// MMT脚本编辑器控件
/// 提供语法高亮、行号显示、滚动同步等功能
/// 支持MMT脚本格式的编辑，包括命令、时间码、注释等语法高亮
/// </summary>
public class MmtScriptEditor : UserControl
{
    /// <summary>编辑器行高（像素）</summary>
    private const double EditorLineHeight = 24.0;

    /// <summary>文本输入框</summary>
    private TextBox? _textBox;

    /// <summary>行号显示区域</summary>
    private TextBlock? _lineNumbers;

    /// <summary>语法高亮显示层</summary>
    private TextBlock? _highlightLayer;

    /// <summary>滚动查看器</summary>
    private ScrollViewer? _scrollViewer;

    /// <summary>高亮层的滚动变换</summary>
    private TranslateTransform? _highlightTransform;

    /// <summary>行号层的滚动变换</summary>
    private TranslateTransform? _lineNumbersTransform;

    /// <summary>是否已挂接滚动事件</summary>
    private bool _scrollHooked;

    /// <summary>最后记住的光标位置（用于失去焦点后仍能插入）</summary>
    private int _lastCaretIndex;

    /// <summary>防止Text属性与TextBox之间循环同步</summary>
    private bool _isSyncingText;

    /// <summary>
    /// 文本内容依赖属性
    /// </summary>
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<MmtScriptEditor, string>(nameof(Text), string.Empty);

    /// <summary>
    /// 获取或设置编辑器文本内容
    /// </summary>
    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    static MmtScriptEditor()
    {
        TextProperty.Changed.AddClassHandler<MmtScriptEditor>((x, e) => x.OnTextPropertyChanged(e));
    }

    /// <summary>
    /// Text属性变化时同步到TextBox
    /// </summary>
    private void OnTextPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (_isSyncingText) return;
        if (_textBox == null || e.NewValue is not string newValue) return;
        if (_textBox.Text == newValue) return;

        _isSyncingText = true;
        try
        {
            _textBox.Text = newValue;
        }
        finally
        {
            _isSyncingText = false;
        }
    }

    /// <summary>
    /// 文本变化路由事件
    /// </summary>
    public static readonly RoutedEvent<TextChangedEventArgs> TextChangedEvent =
        RoutedEvent.Register<MmtScriptEditor, TextChangedEventArgs>(nameof(TextChanged), RoutingStrategies.Bubble);

    /// <summary>
    /// 文本变化事件
    /// </summary>
    public event EventHandler<TextChangedEventArgs> TextChanged
    {
        add => AddHandler(TextChangedEvent, value);
        remove => RemoveHandler(TextChangedEvent, value);
    }

    /// <summary>
    /// 获取光标位置偏移量
    /// </summary>
    public int CaretOffset => _textBox?.CaretIndex ?? 0;

    /// <summary>
    /// 获取当前光标所在行号
    /// </summary>
    public int LineNumber
    {
        get
        {
            if (_textBox == null || string.IsNullOrEmpty(_textBox.Text))
                return 1;

            var text = _textBox.Text.Substring(0, Math.Min(_textBox.CaretIndex, _textBox.Text.Length));
            return text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n').Length;
        }
    }

    /// <summary>
    /// 获取当前光标所在行号（别名）
    /// </summary>
    public int CaretLine => LineNumber;

    public bool IsEditorFocused => _textBox?.IsKeyboardFocusWithin == true;

    /// <summary>
    /// 构造函数：初始化编辑器控件
    /// </summary>
    public MmtScriptEditor()
    {
        BuildVisualTree();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    /// <summary>
    /// 构建可视化树：创建行号区、高亮层、文本框等UI元素
    /// </summary>
    private void BuildVisualTree()
    {
        _lineNumbersTransform = new TranslateTransform();
        _highlightTransform = new TranslateTransform();

        // 行号显示区域
        _lineNumbers = new TextBlock
        {
            Background = new SolidColorBrush(Color.FromRgb(25, 35, 55)),
            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 16,
            LineHeight = EditorLineHeight,
            Padding = new Thickness(8, 8, 12, 8),
            Width = 56,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            TextAlignment = TextAlignment.Right,
            IsHitTestVisible = false,
            RenderTransform = _lineNumbersTransform,
            RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative),
        };

        // 语法高亮显示层
        _highlightLayer = new TextBlock
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            FontFamily = new FontFamily("Consolas, Sarasa Mono SC"),
            FontSize = 16,
            LineHeight = EditorLineHeight,
            Padding = new Thickness(8),
            TextWrapping = TextWrapping.NoWrap,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            RenderTransform = _highlightTransform,
            RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative),
            ClipToBounds = true,
        };

        // 文本编辑框样式
        _textBox = new TextBox
        {
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255)),
            CaretBrush = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
            SelectionBrush = new SolidColorBrush(Color.FromArgb(90, 59, 130, 246)),
            FontFamily = new FontFamily("Consolas, Sarasa Mono SC"),
            FontSize = 16,
            LineHeight = EditorLineHeight,
            Padding = new Thickness(8),
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
        };

        _textBox.TextChanged += OnEditorTextChanged; // 文本变化事件处理
        _textBox.TextChanged += UpdateLineNumbers; // 更新行号显示
        _textBox.TextChanged += UpdateHighlighting; // 更新语法高亮显示
        _textBox.LostFocus += OnTextBoxLostFocus; // 记住光标位置

        // 编辑器主机网格
        _textBox.KeyDown += OnTextBoxKeyDown;

        var editorHost = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ClipToBounds = true,
        };

        editorHost.Children.Add(_highlightLayer); // 语法高亮显示层
        editorHost.Children.Add(_textBox); // 文本编辑框

        var grid = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("Auto,*"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ClipToBounds = true,
        };

        Grid.SetColumn(_lineNumbers, 0); // 行号显示区域
        Grid.SetColumn(editorHost, 1); // 编辑器主机网格

        grid.Children.Add(_lineNumbers); // 行号显示区域
        grid.Children.Add(editorHost); // 编辑器主机网格

        Content = grid;

        UpdateLineNumbers(null, null); // 更新行号显示
        UpdateHighlighting(null, null); // 更新语法高亮显示
        UpdateDocumentMetrics(); // 更新文档元数据
        SyncScrollOverlay(); // 同步滚动覆盖层
    }

    /// <summary>
    /// 控件附加到可视化树时的处理：延迟挂接滚动同步
    /// </summary>
    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        Dispatcher.UIThread.Post(AttachScrollSync, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// 控件从可视化树分离时的处理：解除滚动事件绑定
    /// </summary>
    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_scrollViewer != null)
            _scrollViewer.ScrollChanged -= OnScrollChanged;

        _scrollViewer = null;
        _scrollHooked = false;
    }

    /// <summary>
    /// 挂接滚动同步：查找内部ScrollViewer并绑定滚动事件
    /// </summary>
    private void AttachScrollSync()
    {
        if (_scrollHooked || _textBox == null)
            return;

        _scrollViewer = this.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (_scrollViewer == null)
            return;

        _scrollViewer.ScrollChanged += OnScrollChanged;
        _scrollHooked = true;
        SyncScrollOverlay();
    }

    /// <summary>
    /// 滚动事件处理：同步高亮层和行号层的滚动位置
    /// </summary>
    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        SyncScrollOverlay();
    }

    /// <summary>
    /// 同步滚动覆盖层：将高亮层和行号层的滚动位置与文本框同步
    /// </summary>
    private void SyncScrollOverlay()
    {
        if (_scrollViewer == null)
            return;

        var offset = _scrollViewer.Offset;

        if (_highlightTransform != null)
        {
            _highlightTransform.X = -offset.X;
            _highlightTransform.Y = -offset.Y;
        }

        if (_lineNumbersTransform != null)
        {
            _lineNumbersTransform.X = 0;
            _lineNumbersTransform.Y = -offset.Y;
        }
    }

    /// <summary>
    /// 更新文档尺寸：根据行数计算高亮层和行号层的高度
    /// </summary>
    private void UpdateDocumentMetrics()
    {
        if (_textBox == null || _highlightLayer == null || _lineNumbers == null)
            return;

        var rawText = _textBox.Text ?? string.Empty;
        var lines = rawText.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

        var lineCount = Math.Max(1, lines.Length);
        var lineHeight = EditorLineHeight;
        var verticalPadding = 16.0;

        var height = Math.Ceiling(lineCount * lineHeight + verticalPadding);
        _highlightLayer.Height = height;
        _lineNumbers.Height = height;
    }

    /// <summary>
    /// 更新行号显示：根据文本内容生成行号
    /// </summary>
    private void UpdateLineNumbers(object? sender, TextChangedEventArgs? e)
    {
        if (_textBox == null || _lineNumbers == null)
            return;

        var lines = (_textBox.Text ?? string.Empty)
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Split('\n');

        var lineNums = new System.Text.StringBuilder();
        for (int i = 1; i <= lines.Length; i++)
            lineNums.AppendLine(i.ToString());

        _lineNumbers.Text = lineNums.ToString().TrimEnd();
        UpdateDocumentMetrics();
        SyncScrollOverlay();
    }

    /// <summary>
    /// 更新语法高亮：解析文本并应用语法高亮
    /// </summary>
    private void UpdateHighlighting(object? sender, TextChangedEventArgs? e)
    {
        if (_highlightLayer == null)
            return;

        var text = _textBox?.Text ?? string.Empty;
        _highlightLayer.Inlines?.Clear();

        var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            foreach (var inline in BuildLineInlines(lines[i]))
                _highlightLayer.Inlines?.Add(inline);

            if (i < lines.Length - 1)
                _highlightLayer.Inlines?.Add(new LineBreak());
        }

        UpdateDocumentMetrics();
    }

    /// <summary>
    /// 构建单行的语法高亮Inline集合
    /// 支持MMT脚本的命令、时间码、注释等语法高亮
    /// </summary>
    /// <param name="line">要处理的文本行</param>
    /// <returns>Inline集合</returns>
    private static IEnumerable<Inline> BuildLineInlines(string line)
    {
        var indentLength = line.TakeWhile(char.IsWhiteSpace).Count();
        var indent = indentLength > 0 ? line[..indentLength] : string.Empty;
        var rest = line[indentLength..];

        if (string.IsNullOrEmpty(rest))
        {
            yield return new Run(line)
            {
                Foreground = GetDefaultBrush()
            };
            yield break;
        }

        if (rest.StartsWith("//", StringComparison.Ordinal))
        {
            yield return new Run(line)
            {
                Foreground = GetCommentBrush()
            };
            yield break;
        }

        if (rest.StartsWith(":", StringComparison.Ordinal))
        {
            var commandEnd = rest.IndexOfAny(new[] { ' ', '\t' });
            if (commandEnd < 0)
            {
                yield return new Run(indent)
                {
                    Foreground = GetDefaultBrush()
                };
                yield return new Run(rest)
                {
                    Foreground = GetCommandBrush()
                };
                yield break;
            }

            var command = rest[..commandEnd];
            var parameters = rest[commandEnd..];

            yield return new Run(indent)
            {
                Foreground = GetDefaultBrush()
            };

            yield return new Run(command)
            {
                Foreground = GetCommandBrush()
            };

            yield return new Run(parameters)
            {
                Foreground = GetParameterBrush()
            };
            yield break;
        }

        if (rest.StartsWith("@", StringComparison.Ordinal))
        {
            // 时间码整行高亮，避免只高亮前半段。
            yield return new Run(line)
            {
                Foreground = GetTimeBrush()
            };
            yield break;
        }

        yield return new Run(line)
        {
            Foreground = GetDefaultBrush()
        };
    }

    /// <summary>获取默认文本画刷（浅灰色）</summary>
    private static IBrush GetDefaultBrush() => new SolidColorBrush(Color.FromRgb(212, 212, 212));

    /// <summary>获取命令画刷（蓝色）</summary>
    private static IBrush GetCommandBrush() => new SolidColorBrush(Color.FromRgb(96, 165, 250));

    /// <summary>获取参数画刷（浅白色）</summary>
    private static IBrush GetParameterBrush() => new SolidColorBrush(Color.FromRgb(226, 232, 240));

    /// <summary>获取注释画刷（灰色）</summary>
    private static IBrush GetCommentBrush() => new SolidColorBrush(Color.FromRgb(148, 163, 184));

    /// <summary>获取时间码画刷（橙色）</summary>
    private static IBrush GetTimeBrush() => new SolidColorBrush(Color.FromRgb(251, 191, 36));

    /// <summary>
    /// 在光标位置插入文本
    /// </summary>
    /// <param name="text">要插入的文本</param>
    public void InsertAtCaret(string text)
    {
        if (_textBox == null)
            return;

        var pos = _textBox.CaretIndex;
        var current = _textBox.Text ?? string.Empty;
        _textBox.Text = current.Insert(pos, text);
        _textBox.CaretIndex = pos + text.Length;
    }

    /// <summary>
    /// 在文本末尾追加内容
    /// </summary>
    public void AppendText(string text)
    {
        if (_textBox == null)
            return;

        var current = _textBox.Text ?? string.Empty;
        _textBox.Text = current + text;
        _textBox.CaretIndex = _textBox.Text.Length;
    }

    /// <summary>
    /// 在:end标记前插入一行文本
    /// </summary>
    /// <param name="line">要插入的文本行</param>
    public void InsertLineBeforeEnd(string line)
    {
        if (_textBox == null)
            return;

        var text = _textBox.Text ?? string.Empty;
        var endIdx = text.LastIndexOf(":end", StringComparison.OrdinalIgnoreCase);
        if (endIdx < 0)
            _textBox.Text = text + "\n" + line;
        else
            _textBox.Text = text.Insert(endIdx, line + "\n");
    }

    /// <summary>
    /// 在当前行插入文本
    /// </summary>
    /// <param name="line">要插入的文本行</param>
    public void InsertAtCurrentLine(string line)
    {
        if (_textBox == null)
            return;

        var text = _textBox.Text ?? string.Empty;
        var caretIndex = _textBox.IsKeyboardFocusWithin ? _textBox.CaretIndex : _lastCaretIndex;

        if (string.IsNullOrEmpty(text))
        {
            _textBox.Text = line + "\n";
            _lastCaretIndex = line.Length + 1;
            _textBox.CaretIndex = _lastCaretIndex;
            return;
        }

        caretIndex = Math.Clamp(caretIndex, 0, text.Length);

        var lineStart = 0;
        for (int i = caretIndex - 1; i >= 0; i--)
        {
            if (text[i] == '\n')
            {
                lineStart = i + 1;
                break;
            }
        }

        _textBox.Text = text.Insert(lineStart, line + "\n");
        _lastCaretIndex = lineStart + line.Length + 1;
        _textBox.CaretIndex = _lastCaretIndex;
    }

    /// <summary>
    /// TextBox 失去焦点时记住光标位置
    /// </summary>
    private void OnTextBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (_textBox != null)
            _lastCaretIndex = _textBox.CaretIndex;
    }

    /// <summary>
    /// 在当前命令块中查找最近匹配行并替换。
    /// 找不到时返回 false，由调用方决定是否执行插入。
    /// </summary>
    public bool ReplaceNearestLineInCommandBlock(Func<string, bool> matcher, string replacement)
    {
        if (_textBox == null)
            return false;

        var normalizedText = (_textBox.Text ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = normalizedText.Split('\n').ToList();
        if (lines.Count == 0)
            lines.Add(string.Empty);

        var caretIndex = _textBox.IsKeyboardFocusWithin ? _textBox.CaretIndex : _lastCaretIndex;
        var caretLineIndex = GetCaretLineIndex(normalizedText, caretIndex);
        caretLineIndex = Math.Clamp(caretLineIndex, 0, lines.Count - 1);

        var blockStart = caretLineIndex;
        while (blockStart > 0 && IsCommandBlockLine(lines[blockStart - 1]))
            blockStart--;

        var blockEnd = caretLineIndex;
        while (blockEnd < lines.Count - 1 && IsCommandBlockLine(lines[blockEnd + 1]))
            blockEnd++;

        var replacementLine = FindNearestMatchingLine(lines, blockStart, blockEnd, caretLineIndex, matcher);
        if (replacementLine < 0)
            return false;

        lines[replacementLine] = replacement;
        _textBox.Text = string.Join(Environment.NewLine, lines);
        _lastCaretIndex = GetLineStartOffset(lines, replacementLine) + replacement.Length;
        _textBox.CaretIndex = _lastCaretIndex;
        return true;
    }

    /// <summary>
    /// 在当前行开头插入文本
    /// </summary>
    /// <param name="text">要插入的文本</param>
    public void InsertAtLineStart(string text)
    {
        InsertAtCurrentLine(text);
    }

    /// <summary>
    /// 编辑器文本变化事件处理：同步Text属性并触发TextChanged事件
    /// </summary>
    private void OnEditorTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isSyncingText) return;

        _isSyncingText = true;
        try
        {
            Text = _textBox?.Text ?? string.Empty;
        }
        finally
        {
            _isSyncingText = false;
        }

        if (_textBox != null)
            _lastCaretIndex = _textBox.CaretIndex;
        RaiseEvent(new TextChangedEventArgs(TextChangedEvent));
    }

    /// <summary>
    /// 属性变化处理：当Text属性变化时同步到文本框
    /// </summary>
    private async void OnTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (_textBox == null || (e.KeyModifiers & KeyModifiers.Control) == 0)
            return;

        switch (e.Key)
        {
            case Key.C:
                await CopySelectionAsync();
                e.Handled = true;
                break;
            case Key.X:
                await CutSelectionAsync();
                e.Handled = true;
                break;
            case Key.V:
                await PasteSelectionAsync();
                e.Handled = true;
                break;
            case Key.A:
                _textBox.SelectionStart = 0;
                _textBox.SelectionEnd = (_textBox.Text ?? string.Empty).Length;
                _textBox.CaretIndex = _textBox.SelectionEnd;
                e.Handled = true;
                break;
        }
    }

    // 复制选中文本到剪贴板
    private async Task CopySelectionAsync()
    {
        if (_textBox == null || string.IsNullOrEmpty(_textBox.SelectedText))
            return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
            await clipboard.SetTextAsync(_textBox.SelectedText);
    }

    // 剪切选中文本到剪贴板
    private async Task CutSelectionAsync()
    {
        if (_textBox == null || string.IsNullOrEmpty(_textBox.SelectedText))
            return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
            await clipboard.SetTextAsync(_textBox.SelectedText);

        ReplaceSelection(string.Empty);
    }

    // 粘贴剪贴板文本到当前位置
    private async Task PasteSelectionAsync()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null)
            return;

        var text = await clipboard.GetTextAsync();
        if (string.IsNullOrEmpty(text))
            return;

        ReplaceSelection(text);
    }

    // 替换选中文本
    private void ReplaceSelection(string text)
    {
        if (_textBox == null)
            return;

        var current = _textBox.Text ?? string.Empty;
        var start = Math.Min(_textBox.SelectionStart, _textBox.SelectionEnd);
        var end = Math.Max(_textBox.SelectionStart, _textBox.SelectionEnd);

        if (start < 0 || end < start || end > current.Length)
        {
            start = Math.Clamp(_textBox.CaretIndex, 0, current.Length);
            end = start;
        }

        _textBox.Text = current.Remove(start, end - start).Insert(start, text);
        _textBox.CaretIndex = start + text.Length;
        _textBox.SelectionStart = _textBox.CaretIndex;
        _textBox.SelectionEnd = _textBox.CaretIndex;
    }

    // 复制选中文本到剪贴板
    public Task CopyAsync() => CopySelectionAsync();
    // 剪切选中文本到剪贴板
    public Task CutAsync() => CutSelectionAsync();
    // 粘贴剪贴板文本到当前位置
    public Task PasteAsync() => PasteSelectionAsync();

    // 全选文本
    public void SelectAll()
    {
        if (_textBox == null)
            return;

        _textBox.SelectionStart = 0;
        _textBox.SelectionEnd = (_textBox.Text ?? string.Empty).Length;
        _textBox.CaretIndex = _textBox.SelectionEnd;
    }

    /// <summary>
    /// 重写属性变更事件处理方法
    /// </summary>
    /// <param name="change">属性变更事件参数</param>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        // 调用基类的属性变更处理方法
        base.OnPropertyChanged(change);

        // 当TextProperty属性发生变化且文本框控件已初始化时
        if (change.Property == TextProperty && _textBox != null)
        {
            // 获取新的文本值，若为null则使用空字符串
            var newText = change.GetNewValue<string>() ?? string.Empty;
            // 仅当文本框的当前文本与新值不同时才进行更新
            if (_textBox.Text != newText)
            {
                // 更新文本框的文本内容
                _textBox.Text = newText;
                // 更新行号显示
                UpdateLineNumbers(null, null);
                // 更新语法高亮
                UpdateHighlighting(null, null);
                // 更新文档度量信息
                UpdateDocumentMetrics();
                // 同步滚动覆盖层
                SyncScrollOverlay();
            }
        }
    }

    /// <summary>
    /// 聚焦编辑器文本框
    /// </summary>
    public void FocusEditor()
    {
        _textBox?.Focus();
    }

    /// <summary>
    /// 设置光标位置
    /// </summary>
    /// <param name="offset">光标偏移量</param>
    public void SetCaretPosition(int offset)
    {
        if (_textBox == null)
            return;

        _textBox.CaretIndex = Math.Clamp(offset, 0, (_textBox.Text ?? string.Empty).Length);
    }

    /// <summary>
    /// 将光标移动到文本开头
    /// </summary>
    public void MoveCaretToStart()
    {
        SetCaretPosition(0);
    }

    /// <summary>
    /// 将光标移动到文本末尾
    /// </summary>
    public void MoveCaretToEnd()
    {
        if (_textBox == null)
            return;

        SetCaretPosition((_textBox.Text ?? string.Empty).Length);
    }

    // 获取光标所在行索引
    private static int GetCaretLineIndex(string normalizedText, int caretIndex)
    {
        if (string.IsNullOrEmpty(normalizedText))
            return 0;

        var safeCaret = Math.Clamp(caretIndex, 0, normalizedText.Length);
        var lineIndex = 0;

        for (int i = 0; i < safeCaret; i++)
        {
            if (normalizedText[i] == '\n')
                lineIndex++;
        }

        return lineIndex;
    }

    /// <summary>
    /// 判断给定的文本是否为命令块行（以":"或"//"开头）
    /// </summary>
    /// <param name="line">要判断的文本行</param>
    /// <returns>是否为命令块行</returns>
    private static bool IsCommandBlockLine(string line)
    {
        var trimmed = line.Trim();
        return trimmed.StartsWith(":", StringComparison.Ordinal) ||
               trimmed.StartsWith("//", StringComparison.Ordinal);
    }

    // 在指定范围内查找最近的匹配行，优先搜索光标所在行
    private static int FindNearestMatchingLine(
        IReadOnlyList<string> lines,
        int start,
        int end,
        int caretLineIndex,
        Func<string, bool> matcher)
    {
        for (int distance = 0; distance <= Math.Max(caretLineIndex - start, end - caretLineIndex); distance++)
        {
            var upper = caretLineIndex - distance;
            if (upper >= start && matcher(lines[upper]))
                return upper;

            if (distance == 0)
                continue;

            var lower = caretLineIndex + distance;
            if (lower <= end && matcher(lines[lower]))
                return lower;
        }

        return -1;
    }

    // 计算指定行索引的文本偏移量
    private static int GetLineStartOffset(IReadOnlyList<string> lines, int targetLineIndex)
    {
        var offset = 0;
        for (int i = 0; i < targetLineIndex; i++)
            offset += lines[i].Length + Environment.NewLine.Length;

        return offset;
    }
}
