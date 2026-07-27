using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace MMTGui;

/// <summary>
/// 变换框管理器
/// 负责在预览Canvas上显示可拖拽的变换框，支持移动、缩放、旋转图片
/// </summary>
public class TransformBoxManager
{
    private readonly Canvas _canvas;
    private Image? _targetImage;
    private SubtitleEntry? _currentEntry;
    
    // 变换框元素
    private Border? _selectionBox;
    private readonly List<Ellipse> _handles = new();
    private Ellipse? _rotationHandle;
    
    // 拖拽状态
    private bool _isDragging;
    private Point _dragStart;
    private TransformHandleType _activeHandle = TransformHandleType.None;
    private double _initialRotation;
    private Point _initialMousePos;
    private double _initialScaleX;
    private double _initialScaleY;
    private double _initialPosX;
    private double _initialPosY;
    private IPointer? _capturedPointer;
    
    // 常量
    private const double HandleSize = 10;
    private const double RotationHandleDistance = 30;
    
    public event Action<TransformChangedEventArgs>? TransformChanged;
    
    public TransformBoxManager(Canvas canvas)
    {
        _canvas = canvas;
    }
    
    /// <summary>
    /// 显示变换框
    /// </summary>
    public void Show(Image image, SubtitleEntry entry)
    {
        Clear();
        
        _targetImage = image;
        _currentEntry = entry;
        
        var pos = GetImagePosition();
        var width = image.Width;
        var height = image.Height;
        
        _selectionBox = new Border
        {
            BorderBrush = Brushes.Cyan,
            BorderThickness = new Thickness(2),
            Background = Brushes.Transparent,
            Width = width,
            Height = height,
            Cursor = new Cursor(StandardCursorType.SizeAll)
        };
        
        Canvas.SetLeft(_selectionBox, pos.X);
        Canvas.SetTop(_selectionBox, pos.Y);
        
        _selectionBox.PointerPressed += OnSelectionBoxPointerPressed;
        _selectionBox.PointerMoved += OnPointerMoved;
        _selectionBox.PointerReleased += OnPointerReleased;
        
        _canvas.Children.Add(_selectionBox);
        
        CreateHandles();
        CreateRotationHandle();
    }
    
    private void CreateHandles()
    {
        if (_targetImage == null) return;
        
        var handlePositions = new (double rx, double ry, TransformHandleType type)[]
        {
            (0, 0.5, TransformHandleType.Left),
            (1, 0.5, TransformHandleType.Right),
            (0.5, 0, TransformHandleType.Top),
            (0.5, 1, TransformHandleType.Bottom),
            (0, 0, TransformHandleType.TopLeft),
            (1, 0, TransformHandleType.TopRight),
            (0, 1, TransformHandleType.BottomLeft),
            (1, 1, TransformHandleType.BottomRight)
        };
        
        foreach (var (rx, ry, type) in handlePositions)
        {
            var handle = new Ellipse
            {
                Width = HandleSize,
                Height = HandleSize,
                Fill = Brushes.White,
                Stroke = Brushes.Cyan,
                StrokeThickness = 2,
                Cursor = GetCursorForHandle(type)
            };
            
            UpdateHandlePosition(handle, rx, ry);
            handle.Tag = type;
            
            handle.PointerPressed += OnHandlePointerPressed;
            handle.PointerMoved += OnPointerMoved;
            handle.PointerReleased += OnPointerReleased;
            
            _handles.Add(handle);
            _canvas.Children.Add(handle);
        }
    }
    
    private void CreateRotationHandle()
    {
        if (_targetImage == null || _selectionBox == null) return;
        
        _rotationHandle = new Ellipse
        {
            Width = HandleSize,
            Height = HandleSize,
            Fill = Brushes.Green,
            Stroke = Brushes.White,
            StrokeThickness = 2,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        
        _rotationHandle.PointerPressed += OnRotationHandlePointerPressed;
        _rotationHandle.PointerMoved += OnPointerMoved;
        _rotationHandle.PointerReleased += OnPointerReleased;
        
        UpdateRotationHandlePosition();
        _canvas.Children.Add(_rotationHandle);
    }
    
    private void OnSelectionBoxPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(_canvas).Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            _activeHandle = TransformHandleType.Move;
            _dragStart = e.GetPosition(_canvas);
            _initialPosX = _currentEntry?.PosX ?? 0;
            _initialPosY = _currentEntry?.PosY ?? 0;
            e.Pointer.Capture(_canvas);
            e.Handled = true;
        }
    }
    
    private void OnHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Ellipse handle) return;
        
        if (e.GetCurrentPoint(_canvas).Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            _activeHandle = (TransformHandleType)handle.Tag!;
            _dragStart = e.GetPosition(_canvas);
            _initialScaleX = _currentEntry?.ImageScaleX ?? 100;
            _initialScaleY = _currentEntry?.ImageScaleY ?? 100;
            e.Pointer.Capture(_canvas);
            e.Handled = true;
        }
    }
    
    private void OnRotationHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_rotationHandle == null) return;
        
        if (e.GetCurrentPoint(_canvas).Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            _activeHandle = TransformHandleType.Rotation;
            _initialMousePos = e.GetPosition(_canvas);
            _initialRotation = _currentEntry?.ImageRotation ?? 0;
            e.Pointer.Capture(_canvas);
            e.Handled = true;
        }
    }
    
    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging || _currentEntry == null || _targetImage == null) return;
        
        var currentPos = e.GetPosition(_canvas);
        var delta = currentPos - _dragStart;
        
        switch (_activeHandle)
        {
            case TransformHandleType.Move:
                _currentEntry.PosX = _initialPosX + delta.X;
                _currentEntry.PosY = _initialPosY + delta.Y;
                break;
                
            case TransformHandleType.Left:
                _currentEntry.ImageScaleX = Math.Max(1, _initialScaleX - delta.X * 0.5);
                break;
                
            case TransformHandleType.Right:
                _currentEntry.ImageScaleX = Math.Max(1, _initialScaleX + delta.X * 0.5);
                break;
                
            case TransformHandleType.Top:
                _currentEntry.ImageScaleY = Math.Max(1, _initialScaleY - delta.Y * 0.5);
                break;
                
            case TransformHandleType.Bottom:
                _currentEntry.ImageScaleY = Math.Max(1, _initialScaleY + delta.Y * 0.5);
                break;
                
            case TransformHandleType.TopLeft:
            case TransformHandleType.TopRight:
            case TransformHandleType.BottomLeft:
            case TransformHandleType.BottomRight:
                var scaleDelta = (delta.X + delta.Y) * 0.25;
                _currentEntry.ImageScaleX = Math.Max(1, _initialScaleX + scaleDelta);
                _currentEntry.ImageScaleY = Math.Max(1, _initialScaleY + scaleDelta);
                break;
                
            case TransformHandleType.Rotation:
                var center = GetImageCenter();
                var angle1 = Math.Atan2(_initialMousePos.Y - center.Y, _initialMousePos.X - center.X);
                var angle2 = Math.Atan2(currentPos.Y - center.Y, currentPos.X - center.X);
                var angleDelta = (angle2 - angle1) * 180 / Math.PI;
                _currentEntry.ImageRotation = _initialRotation + angleDelta;
                break;
        }
        
        TransformChanged?.Invoke(new TransformChangedEventArgs
        {
            Entry = _currentEntry,
            PropertyName = GetPropertyNameForHandle(_activeHandle)
        });
        
        UpdatePositions();
    }
    
    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDragging)
        {
            e.Pointer.Capture(null);
        }
        
        _isDragging = false;
        _activeHandle = TransformHandleType.None;
        _capturedPointer = null;
    }
    
    private void UpdatePositions()
    {
        if (_targetImage == null || _selectionBox == null) return;
        
        var pos = GetImagePosition();
        Canvas.SetLeft(_selectionBox, pos.X);
        Canvas.SetTop(_selectionBox, pos.Y);
        
        for (int i = 0; i < _handles.Count; i++)
        {
            var (rx, ry, _) = GetHandleRelativePosition(i);
            UpdateHandlePosition(_handles[i], rx, ry);
        }
        
        UpdateRotationHandlePosition();
    }
    
    private Point GetImagePosition()
    {
        if (_targetImage == null || _currentEntry == null) 
            return new Point(0, 0);
        
        return new Point(_currentEntry.PosX, _currentEntry.PosY);
    }
    
    private Point GetImageCenter()
    {
        var pos = GetImagePosition();
        return new Point(
            pos.X + _targetImage!.Width / 2,
            pos.Y + _targetImage!.Height / 2
        );
    }
    
    private void UpdateHandlePosition(Ellipse handle, double rx, double ry)
    {
        var pos = GetImagePosition();
        Canvas.SetLeft(handle, pos.X + rx * _targetImage!.Width - HandleSize / 2);
        Canvas.SetTop(handle, pos.Y + ry * _targetImage!.Height - HandleSize / 2);
    }
    
    private void UpdateRotationHandlePosition()
    {
        if (_rotationHandle == null || _targetImage == null) return;
        
        var center = GetImageCenter();
        Canvas.SetLeft(_rotationHandle, center.X - HandleSize / 2);
        Canvas.SetTop(_rotationHandle, center.Y - RotationHandleDistance - HandleSize / 2);
    }
    
    private (double rx, double ry, TransformHandleType type) GetHandleRelativePosition(int index)
    {
        return index switch
        {
            0 => (0, 0.5, TransformHandleType.Left),
            1 => (1, 0.5, TransformHandleType.Right),
            2 => (0.5, 0, TransformHandleType.Top),
            3 => (0.5, 1, TransformHandleType.Bottom),
            4 => (0, 0, TransformHandleType.TopLeft),
            5 => (1, 0, TransformHandleType.TopRight),
            6 => (0, 1, TransformHandleType.BottomLeft),
            7 => (1, 1, TransformHandleType.BottomRight),
            _ => (0, 0, TransformHandleType.None)
        };
    }
    
    private Cursor GetCursorForHandle(TransformHandleType type)
    {
        return type switch
        {
            TransformHandleType.Left or TransformHandleType.Right 
                => new Cursor(StandardCursorType.SizeWestEast),
            TransformHandleType.Top or TransformHandleType.Bottom 
                => new Cursor(StandardCursorType.SizeNorthSouth),
            TransformHandleType.TopLeft or TransformHandleType.BottomRight 
                => new Cursor(StandardCursorType.BottomRightCorner),
            TransformHandleType.TopRight or TransformHandleType.BottomLeft 
                => new Cursor(StandardCursorType.TopRightCorner),
            _ => new Cursor(StandardCursorType.Arrow)
        };
    }
    
    private string GetPropertyNameForHandle(TransformHandleType type)
    {
        return type switch
        {
            TransformHandleType.Move => "Position",
            TransformHandleType.Left or TransformHandleType.Right or
            TransformHandleType.Top or TransformHandleType.Bottom or
            TransformHandleType.TopLeft or TransformHandleType.TopRight or
            TransformHandleType.BottomLeft or TransformHandleType.BottomRight 
                => "Scale",
            TransformHandleType.Rotation => "Rotation",
            _ => ""
        };
    }
    
    public void Clear()
    {
        if (_selectionBox != null)
        {
            _canvas.Children.Remove(_selectionBox);
            _selectionBox = null;
        }
        
        foreach (var handle in _handles)
        {
            _canvas.Children.Remove(handle);
        }
        _handles.Clear();
        
        if (_rotationHandle != null)
        {
            _canvas.Children.Remove(_rotationHandle);
            _rotationHandle = null;
        }
        
        _targetImage = null;
        _currentEntry = null;
    }
}

public enum TransformHandleType
{
    None,
    Move,
    Left,
    Right,
    Top,
    Bottom,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    Rotation
}

public class TransformChangedEventArgs : EventArgs
{
    public SubtitleEntry? Entry { get; set; }
    public string PropertyName { get; set; } = "";
}