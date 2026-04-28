using System;
using System.Windows.Input;

namespace MMTGui;

/// <summary>
/// 简单命令实现类。
/// 用于把普通委托快速包装成可绑定到菜单、按钮和快捷键的命令对象。
/// </summary>
public class SimpleCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    /// <summary>
    /// 使用带参数的执行委托构造命令。
    /// </summary>
    public SimpleCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    /// <summary>
    /// 使用无参数的执行委托构造命令。
    /// </summary>
    public SimpleCommand(Action execute, Func<bool>? canExecute = null)
        : this(obj => { execute(); }, canExecute != null ? _ => canExecute() : null)
    {
        if (execute == null) throw new ArgumentNullException(nameof(execute));
    }

    /// <summary>
    /// 当命令可执行状态变化时触发。
    /// </summary>
    public event EventHandler? CanExecuteChanged;

    /// <summary>
    /// 判断命令当前是否可执行。
    /// </summary>
    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    /// <summary>
    /// 执行命令。
    /// </summary>
    public void Execute(object? parameter) => _execute(parameter);

    /// <summary>
    /// 主动通知界面重新查询命令可执行状态。
    /// </summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
