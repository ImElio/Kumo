using System.Runtime.InteropServices;
using Kumo.Windows.Interop;

namespace Kumo.Windows.Shell;

/// <summary>
/// Reads the files selected in the foreground File Explorer window or on the desktop through the documented
/// shell automation interfaces (IShellWindows → IShellBrowser → IShellView → ShellFolderView).
/// </summary>
/// <remarks>
/// The calls are cross-process COM calls into Explorer, so they run on a dedicated STA thread with a timeout:
/// a busy or hung Explorer must not freeze Kumo's UI thread.
/// </remarks>
public static unsafe partial class ExplorerSelection
{
    private const int SWC_DESKTOP = 8;
    private const int SWFO_NEEDDISPATCH = 1;
    private const uint SVGIO_BACKGROUND = 0;

    private static readonly Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid IID_IShellBrowser = new("000214E2-0000-0000-C000-000000000046");
    private static readonly Guid IID_IDispatch = new("00020400-0000-0000-C000-000000000046");

    public static Task<IReadOnlyList<string>> GetForForegroundAsync(TimeSpan timeout)
    {
        nint foreground = GetForegroundWindow();
        var completion = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.TrySetResult(Read(foreground));
            }
            // This runs on its own thread, where any escaping exception would end the process; a failed
            // read must only mean "nothing selected".
            catch (Exception)
            {
                completion.TrySetResult([]);
            }
        })
        {
            IsBackground = true,
            Name = "Kumo shell selection",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(timeout).ContinueWith(
            t => t.IsCompletedSuccessfully ? t.Result : (IReadOnlyList<string>)[],
            TaskScheduler.Default);
    }

    private static IReadOnlyList<string> Read(nint foreground)
    {
        if (foreground == 0)
            return [];
        nint root = NativeMethods.GetAncestor(foreground, NativeMethods.GA_ROOT);
        string className = ClassName(root);

        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType == null)
            return [];
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            var windows = (IShellWindows)shell.Windows();
            if (className is "Progman" or "WorkerW")
                return FromDesktop(windows);
            if (className is "CabinetWClass" or "ExploreWClass")
                return FromExplorer(windows, root);
            return [];
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }

    private static IReadOnlyList<string> FromDesktop(IShellWindows windows)
    {
        object location = 0;
        object empty = Type.Missing;
        object? desktop = windows.FindWindowSW(ref location, ref empty, SWC_DESKTOP, out _, SWFO_NEEDDISPATCH);
        return desktop == null ? [] : SelectedPaths(desktop);
    }

    private static IReadOnlyList<string> FromExplorer(IShellWindows windows, nint root)
    {
        // With tabs, every tab of a window reports the same HWND. The active tab is the topmost
        // ShellTabWindowClass child, which is matched against each browser's own window.
        nint activeTab = FindWindowExW(root, 0, "ShellTabWindowClass", null);
        int count = windows.Count;
        IReadOnlyList<string> fallback = [];
        for (int i = 0; i < count; i++)
        {
            object? window = windows.Item(i);
            if (window == null)
                continue;
            try
            {
                dynamic browser = window;
                if ((nint)(long)browser.HWND != root)
                    continue;

                var browserWindow = BrowserWindow(window);
                var paths = SelectedPaths(window);
                if (activeTab == 0 || browserWindow == activeTab)
                    return paths;
                if (fallback.Count == 0)
                    fallback = paths;
            }
            finally
            {
                Marshal.ReleaseComObject(window);
            }
        }
        return fallback;
    }

    private static nint BrowserWindow(object window)
    {
        var shellBrowser = ShellBrowser(window);
        if (shellBrowser == null)
            return 0;
        try
        {
            return shellBrowser.GetWindow(out nint hwnd) == 0 ? hwnd : 0;
        }
        finally
        {
            Marshal.ReleaseComObject(shellBrowser);
        }
    }

    private static IShellBrowser? ShellBrowser(object window)
    {
        if (window is not IServiceProvider provider)
            return null;
        var service = SID_STopLevelBrowser;
        var iid = IID_IShellBrowser;
        return provider.QueryService(ref service, ref iid, out object browser) == 0 ? browser as IShellBrowser : null;
    }

    private static IReadOnlyList<string> SelectedPaths(object window)
    {
        var browser = ShellBrowser(window);
        if (browser == null)
            return [];
        IShellView? view = null;
        object? folderView = null;
        try
        {
            if (browser.QueryActiveShellView(out view) != 0 || view == null)
                return [];
            var iid = IID_IDispatch;
            if (view.GetItemObject(SVGIO_BACKGROUND, ref iid, out folderView) != 0 || folderView == null)
                return [];

            dynamic items = ((dynamic)folderView).SelectedItems();
            var paths = new List<string>();
            int count = items.Count;
            for (int i = 0; i < count; i++)
            {
                dynamic item = items.Item(i);
                bool isFile = item.IsFileSystem && !item.IsFolder;
                string? path = item.Path as string;
                if (isFile && path != null && File.Exists(path))
                    paths.Add(path);
            }
            return paths;
        }
        finally
        {
            if (folderView != null)
                Marshal.ReleaseComObject(folderView);
            if (view != null)
                Marshal.ReleaseComObject(view);
            Marshal.ReleaseComObject(browser);
        }
    }

    private static string ClassName(nint hwnd)
    {
        char* buffer = stackalloc char[64];
        int length = NativeMethods.GetClassName(hwnd, buffer, 64);
        return new string(buffer, 0, Math.Max(0, length));
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowExW(nint hWndParent, nint hWndChildAfter, string? lpszClass, string? lpszWindow);

    [ComImport]
    [Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85")]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    private interface IShellWindows
    {
        int Count { [DispId(0x60020000)] get; }

        [DispId(0)]
        [return: MarshalAs(UnmanagedType.IDispatch)]
        object? Item([MarshalAs(UnmanagedType.Struct)] object index);

        [DispId(0x60020007)]
        [return: MarshalAs(UnmanagedType.IDispatch)]
        object? FindWindowSW(
            [MarshalAs(UnmanagedType.Struct)] ref object location,
            [MarshalAs(UnmanagedType.Struct)] ref object locationRoot,
            int windowClass,
            out int hwnd,
            int options);
    }

    [ComImport]
    [Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid service, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object result);
    }

    [ComImport]
    [Guid("000214E2-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        [PreserveSig] int GetWindow(out nint hwnd);
        [PreserveSig] int ContextSensitiveHelp(int enterMode);
        [PreserveSig] int InsertMenusSB(nint hmenuShared, nint lpMenuWidths);
        [PreserveSig] int SetMenuSB(nint hmenuShared, nint holemenuRes, nint hwndActiveObject);
        [PreserveSig] int RemoveMenusSB(nint hmenuShared);
        [PreserveSig] int SetStatusTextSB(nint pszStatusText);
        [PreserveSig] int EnableModelessSB(int enable);
        [PreserveSig] int TranslateAcceleratorSB(nint pmsg, ushort wID);
        [PreserveSig] int BrowseObject(nint pidl, uint wFlags);
        [PreserveSig] int GetViewStateStream(uint grfMode, out nint stream);
        [PreserveSig] int GetControlWindow(uint id, out nint hwnd);
        [PreserveSig] int SendControlMsg(uint id, uint uMsg, nint wParam, nint lParam, out nint pret);
        [PreserveSig] int QueryActiveShellView([MarshalAs(UnmanagedType.Interface)] out IShellView? view);
    }

    [ComImport]
    [Guid("000214E3-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellView
    {
        [PreserveSig] int GetWindow(out nint hwnd);
        [PreserveSig] int ContextSensitiveHelp(int enterMode);
        [PreserveSig] int TranslateAccelerator(nint pmsg);
        [PreserveSig] int EnableModeless(int enable);
        [PreserveSig] int UIActivate(uint state);
        [PreserveSig] int Refresh();
        [PreserveSig] int CreateViewWindow(nint previous, nint folderSettings, nint browser, nint rect, out nint hwnd);
        [PreserveSig] int DestroyViewWindow();
        [PreserveSig] int GetCurrentInfo(nint folderSettings);
        [PreserveSig] int AddPropertySheetPages(uint reserved, nint callback, nint lParam);
        [PreserveSig] int SaveViewState();
        [PreserveSig] int SelectItem(nint pidl, uint flags);
        [PreserveSig] int GetItemObject(uint item, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object? result);
    }
}
