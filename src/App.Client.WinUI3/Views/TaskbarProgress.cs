using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using App.Client.WinUI3.Services;
using Windows.Win32.Foundation;

namespace App.Client.WinUI3.Views;

/// <summary>
/// Shows a progress on the taskbar button of a window. Only what changed is set, so it can be reported as often as it changes.
/// </summary>
internal sealed partial class TaskbarProgress(HWND window)
{
    private static readonly Guid TaskbarListClassId = new("56FDF344-FD6D-11D0-958A-006097C9A090");
    private static readonly StrategyBasedComWrappers ComWrappers = new();

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
                taskbar.SetProgressState((nint)window, progress.State switch
                {
                    ProgressState.Normal => TaskbarProgressFlags.Normal,
                    ProgressState.Error => TaskbarProgressFlags.Error,
                    ProgressState.Indeterminate => TaskbarProgressFlags.Indeterminate,
                    ProgressState.Paused => TaskbarProgressFlags.Paused,
                    _ => TaskbarProgressFlags.NoProgress,
                });
            }

            // Setting a value would leave the indeterminate state, and without progress there is nothing to set it for.
            if (progress is { State: not (ProgressState.None or ProgressState.Indeterminate), Value: { } value })
            {
                taskbar.SetProgressValue((nint)window, (ulong)value, 100);
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
        if (_taskbar is not null)
        {
            return _taskbar;
        }

        // created through the source generated com wrappers, as casting a runtime callable wrapper isn't supported with native aot
        var interfaceId = typeof(ITaskbarList3).GUID;
        Marshal.ThrowExceptionForHR(CoCreateInstance(TaskbarListClassId, 0, ClassContext.InProcessServer, interfaceId, out var pointer));
        try
        {
            var taskbar = (ITaskbarList3)ComWrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.None);
            taskbar.HrInit();
            return _taskbar = taskbar;
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid classId, nint outer, ClassContext context, in Guid interfaceId, out nint pointer);

    private enum ClassContext : uint
    {
        InProcessServer = 0x1,
    }

    internal enum TaskbarProgressFlags
    {
        NoProgress = 0x0,
        Indeterminate = 0x1,
        Normal = 0x2,
        Error = 0x4,
        Paused = 0x8,
    }

    // only the methods up to the ones called are declared, the vtable slots after them aren't used
    [GeneratedComInterface]
    [Guid("56FDF342-FD6D-11D0-958A-006097C9A090")]
    internal partial interface ITaskbarList
    {
        void HrInit();

        void AddTab(nint window);

        void DeleteTab(nint window);

        void ActivateTab(nint window);

        void SetActiveAlt(nint window);
    }

    [GeneratedComInterface]
    [Guid("602D4995-B13A-429B-A66E-1935E44F4317")]
    internal partial interface ITaskbarList2 : ITaskbarList
    {
        void MarkFullscreenWindow(nint window, int fullscreen);
    }

    [GeneratedComInterface]
    [Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF")]
    internal partial interface ITaskbarList3 : ITaskbarList2
    {
        void SetProgressValue(nint window, ulong completed, ulong total);

        void SetProgressState(nint window, TaskbarProgressFlags flags);
    }
}
