using System.Runtime.InteropServices;

namespace Nekomata.UI.Services;

/// <summary>Uses a running classic Outlook instance only; never starts a registered older installation.</summary>
internal static class ActiveOutlookHandoff
{
    public static bool OpenDrafts()
    {
        // New Outlook does not expose the classic Outlook COM session. Never fall
        // through to an older classic installation when the new client is running.
        foreach (var process in System.Diagnostics.Process.GetProcessesByName("olk"))
        {
            using (process)
            {
                var handle = process.MainWindowHandle;
                if (handle == IntPtr.Zero) continue;
                ShowWindowAsync(handle, 9);
                SetForegroundWindow(handle);
                return false; // User opens Drafts in the already-running new client.
            }
        }
        Marshal.ThrowExceptionForHR(CLSIDFromProgID("Outlook.Application", out var classId));
        Marshal.ThrowExceptionForHR(GetActiveObject(ref classId, IntPtr.Zero, out var instance));
        object? session = null;
        object? folder = null;
        object? explorer = null;
        try
        {
            dynamic outlook = instance;
            session = outlook.Session;
            folder = ((dynamic)session).GetDefaultFolder(16); // olFolderDrafts
            explorer = outlook.ActiveExplorer();
            if (explorer is null)
                ((dynamic)folder).Display();
            else
            {
                ((dynamic)explorer).CurrentFolder = folder;
                ((dynamic)explorer).Activate();
            }
            return true;
        }
        finally
        {
            foreach (var value in new[] { explorer, folder, session, instance })
                if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
        }
    }

    [DllImport("ole32.dll", CharSet = CharSet.Unicode)]
    private static extern int CLSIDFromProgID(string progId, out Guid clsid);

    [DllImport("oleaut32.dll", PreserveSig = true)]
    private static extern int GetActiveObject(ref Guid clsid, IntPtr reserved,
        [MarshalAs(UnmanagedType.IUnknown)] out object instance);

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);
}
