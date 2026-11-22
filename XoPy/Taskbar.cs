using System.Drawing;
using System.Runtime.InteropServices;

namespace XoPy;

/// <summary>Barra de progreso e icono de porcentaje sobre el botón de XoPy en la barra de tareas.</summary>
public static class Taskbar
{
    private static ITaskbarList3? _list;
    private static bool _failed;
    private static IntPtr _overlayIcon;

    public static void SetProgress(IntPtr window, int percent, bool paused, bool error, bool running)
    {
        var list = List();
        if (list is null || window == IntPtr.Zero) return;
        try
        {
            var state = !running ? Tbpflags.NoProgress
                : error ? Tbpflags.Error
                : paused ? Tbpflags.Paused
                : Tbpflags.Normal;
            list.SetProgressState(window, state);
            if (running)
            {
                var value = (ulong)Math.Clamp(percent, 0, 100);
                list.SetProgressValue(window, value, 100);
            }
        }
        catch { }
    }

    public static void SetOverlay(IntPtr window, int percent, bool paused, bool error, bool dark)
    {
        var list = List();
        if (list is null || window == IntPtr.Zero) return;
        try
        {
            var icon = Icons.PercentBadge(percent, dark, paused, error);
            var handle = icon.Handle;
            list.SetOverlayIcon(window, handle, $"{percent} %");
            if (_overlayIcon != IntPtr.Zero) DestroyIcon(_overlayIcon);
            _overlayIcon = handle;
        }
        catch { }
    }

    public static void Clear(IntPtr window)
    {
        var list = List();
        if (list is null || window == IntPtr.Zero) return;
        try
        {
            list.SetOverlayIcon(window, IntPtr.Zero, string.Empty);
            list.SetProgressState(window, Tbpflags.NoProgress);
        }
        catch { }
        if (_overlayIcon != IntPtr.Zero)
        {
            DestroyIcon(_overlayIcon);
            _overlayIcon = IntPtr.Zero;
        }
    }

    private static ITaskbarList3? List()
    {
        if (_failed) return null;
        if (_list is not null) return _list;
        try
        {
            _list = (ITaskbarList3)new TaskbarInstance();
            _list.HrInit();
            return _list;
        }
        catch
        {
            _failed = true;
            return null;
        }
    }

    [Flags]
    private enum Tbpflags
    {
        NoProgress = 0,
        Indeterminate = 0x1,
        Normal = 0x2,
        Error = 0x4,
        Paused = 0x8
    }

    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    [ClassInterface(ClassInterfaceType.None)]
    private class TaskbarInstance
    {
    }

    [ComImport]
    [Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
        void SetProgressState(IntPtr hwnd, Tbpflags flags);
        void RegisterTab(IntPtr hwndTab, IntPtr hwndMdi);
        void UnregisterTab(IntPtr hwndTab);
        void SetTabOrder(IntPtr hwndTab, IntPtr hwndInsertBefore);
        void SetTabActive(IntPtr hwndTab, IntPtr hwndMdi, uint reserved);
        void ThumbBarAddButtons(IntPtr hwnd, uint count, IntPtr buttons);
        void ThumbBarUpdateButtons(IntPtr hwnd, uint count, IntPtr buttons);
        void ThumbBarSetImageList(IntPtr hwnd, IntPtr imageList);
        void SetOverlayIcon(IntPtr hwnd, IntPtr icon, [MarshalAs(UnmanagedType.LPWStr)] string description);
        void SetThumbnailTooltip(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] string tooltip);
        void SetThumbnailClip(IntPtr hwnd, IntPtr clip);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
