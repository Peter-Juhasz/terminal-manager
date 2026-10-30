using System.Runtime.InteropServices;
using App.Client.WinUI3.Services;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace App.Client.WinUI3.Views;

/// <summary>
/// Shows a progress on the taskbar button of a window. Only what changed is set, so it can be reported as often as it changes.
/// </summary>
internal sealed class TaskbarProgress(HWND window)
{
    private ITaskbarList3? _taskbar;
    private ProgressReport _shown;

    public void Report(ProgressReport progress)
    {
        if (progress == _shown)
        {
            return;
        }

        try
        {
            var taskbar = GetTaskbar();
            if (progress.State != _shown.State)
            {
                taskbar.SetProgressState(window, progress.State switch
                {
                    ProgressState.Normal => TBPFLAG.TBPF_NORMAL,
                    ProgressState.Error => TBPFLAG.TBPF_ERROR,
                    ProgressState.Indeterminate => TBPFLAG.TBPF_INDETERMINATE,
                    ProgressState.Paused => TBPFLAG.TBPF_PAUSED,
                    _ => TBPFLAG.TBPF_NOPROGRESS,
                });
            }

            // Setting a value would leave the indeterminate state, and without progress there is nothing to set it for.
            if (progress is { State: not (ProgressState.None or ProgressState.Indeterminate), Value: { } value })
            {
                taskbar.SetProgressValue(window, (ulong)value, 100);
            }

            _shown = progress;
        }
        catch (COMException)
        {
            // There is no taskbar, such as when Explorer isn't running; the progress is still shown in the app.
        }
    }

    private ITaskbarList3 GetTaskbar()
    {
        if (_taskbar is null)
        {
            var taskbar = (ITaskbarList3)new TaskbarList();
            taskbar.HrInit();
            _taskbar = taskbar;
        }

        return _taskbar;
    }
}
