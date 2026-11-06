using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using SolidWorks.Interop.sldworks;

/// <summary>
/// One SolidWorks instance per MCP server. The server passes SOLIDWORKS_MCP_OWNED_INSTANCE=1 and its PID as
/// SOLIDWORKS_MCP_OWNER_ID. The instance PID is kept in a state file, so oneshot workers reuse it. Attaches only
/// through the ROT entry for that PID and never touches another SolidWorks process.
/// Matches hardware/tools/solidworks/sw_instance.py in the marengo repo, including its launch lock.
/// </summary>
internal static class OwnedSolidWorks
{
    private const string DefaultExe = @"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SLDWORKS.exe";
    private const int BmClick = 0x00F5;
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(300);
    private static ISldWorks? cached;

    public static bool Enabled =>
        System.Environment.GetEnvironmentVariable("SOLIDWORKS_MCP_OWNED_INSTANCE") == "1" && !string.IsNullOrWhiteSpace(OwnerId);

    private static string? OwnerId => System.Environment.GetEnvironmentVariable("SOLIDWORKS_MCP_OWNER_ID");

    /// <summary>Per-owner in owned mode, so one session's long solve does not block another's calls.</summary>
    public static string ComMutexName => Enabled ? $"{WorkerConstants.ComMutexName}_{OwnerId}" : WorkerConstants.ComMutexName;

    private static string StatePath
    {
        get
        {
            string dir = Path.Combine(Path.GetTempPath(), "solidworks-mcp-owned");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, $"{OwnerId}.pid");
        }
    }

    public static ISldWorks Get(bool startIfMissing)
    {
        if (cached is not null)
        {
            return cached;
        }

        int? pid = ReadPid();
        if (pid is int known && IsSolidWorks(known))
        {
            cached = Verified(AttachByPid(known) ?? throw new InvalidOperationException($"Owned SolidWorks PID {known} is not in the ROT."), known);
            return cached;
        }

        if (!startIfMissing)
        {
            throw new InvalidOperationException("This session has no SolidWorks instance yet. Pass start_if_missing=true to launch it.");
        }

        cached = Launch();
        return cached;
    }

    /// <summary>Exit this owner's instance. Leaves it running if a document has unsaved changes.</summary>
    public static object Release()
    {
        int? pid = ReadPid();
        if (pid is not int own || !IsSolidWorks(own))
        {
            TryDeleteState();
            return new { released = false, reason = "no instance" };
        }

        ISldWorks? app = cached ?? AttachByPid(own);
        if (app is not null)
        {
            app = Verified(app, own);
            for (ModelDoc2? doc = app.GetFirstDocument() as ModelDoc2; doc is not null; doc = doc.GetNext() as ModelDoc2)
            {
                if (doc.GetSaveFlag())
                {
                    return new { released = false, reason = "unsaved changes", pid = own, document = doc.GetTitle() };
                }
            }

            app.CloseAllDocuments(true);
            app.ExitApp();
        }

        cached = null;
        using Process process = Process.GetProcessById(own);
        if (!process.WaitForExit(60_000))
        {
            // ExitApp can leave a half-closed process with no documents and no Simulation license.
            process.Kill();
            process.WaitForExit(30_000);
        }

        TryDeleteState();
        return new { released = true, pid = own };
    }

    private static ISldWorks Launch()
    {
        string exe = System.Environment.GetEnvironmentVariable("SOLIDWORKS_EXE") ?? DefaultExe;
        using FileStream launchLock = TakeLaunchLock();
        // UseShellExecute: the instance must not inherit the worker's stdio pipes, or a oneshot call never ends.
        using Process process = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true })
            ?? throw new InvalidOperationException("SolidWorks did not start.");
        int pid = process.Id;
        Stopwatch clock = Stopwatch.StartNew();
        ISldWorks? app = null;
        while (clock.Elapsed < StartTimeout && app is null)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException($"SolidWorks exited during start, code {process.ExitCode}.");
            }

            app = AttachByPid(pid);
            if (app is null)
            {
                Thread.Sleep(1000);
            }
        }

        while (app is not null && clock.Elapsed < StartTimeout)
        {
            ClearJournalPrompt(pid);
            try
            {
                if (app.StartupProcessCompleted)
                {
                    break;
                }
            }
            catch (COMException)
            {
                // Busy while starting.
            }

            Thread.Sleep(1000);
        }

        if (app is null || !app.StartupProcessCompleted)
        {
            process.Kill();
            throw new InvalidOperationException($"SolidWorks PID {pid} did not finish starting.");
        }

        Verified(app, pid);
        app.Visible = true;
        File.WriteAllText(StatePath, pid.ToString());
        return app;
    }

    private static ISldWorks Verified(ISldWorks app, int pid)
    {
        int got = app.GetProcessID();
        if (got != pid)
        {
            throw new InvalidOperationException($"Attached to SolidWorks PID {got}, expected {pid}.");
        }

        return app;
    }

    private static FileStream TakeLaunchLock()
    {
        string path = Path.Combine(Path.GetTempPath(), "sw_instance_launch.lock");
        FileStream file = new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
        Stopwatch clock = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                file.Lock(0, 1);
                return file;
            }
            catch (IOException) when (clock.Elapsed < 2 * StartTimeout)
            {
                Thread.Sleep(1000);
            }
        }
    }

    private static int? ReadPid()
    {
        try
        {
            return int.TryParse(File.ReadAllText(StatePath).Trim(), out int pid) ? pid : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static void TryDeleteState()
    {
        try
        {
            File.Delete(StatePath);
        }
        catch (IOException)
        {
            // Stale state is harmless: the PID check rejects it.
        }
    }

    private static bool IsSolidWorks(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            return process.ProcessName.Equals("SLDWORKS", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static ISldWorks? AttachByPid(int pid)
    {
        string tag = $"SolidWorks_PID_{pid}";
        Marshal.ThrowExceptionForHR(GetRunningObjectTable(0, out IRunningObjectTable rot));
        Marshal.ThrowExceptionForHR(CreateBindCtx(0, out IBindCtx context));
        rot.EnumRunning(out IEnumMoniker monikers);
        IMoniker[] one = new IMoniker[1];
        while (monikers.Next(1, one, IntPtr.Zero) == 0)
        {
            one[0].GetDisplayName(context, null, out string name);
            if (name.TrimStart('!').Equals(tag, StringComparison.OrdinalIgnoreCase))
            {
                rot.GetObject(one[0], out object instance);
                return (ISldWorks)instance;
            }
        }

        return null;
    }

    /// <summary>A third SolidWorks process stops at this modal prompt. Clicks OK on exactly it, in this PID only.</summary>
    private static void ClearJournalPrompt(int pid)
    {
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out uint owner);
            if (owner == pid && IsWindowVisible(window) && ClassName(window) == "#32770")
            {
                IntPtr ok = IntPtr.Zero;
                bool journal = false;
                EnumChildWindows(window, (child, _) =>
                {
                    string text = WindowText(child);
                    journal |= text.Contains("journal file could not be created", StringComparison.OrdinalIgnoreCase);
                    if (text == "OK")
                    {
                        ok = child;
                    }

                    return true;
                }, IntPtr.Zero);
                if (journal && ok != IntPtr.Zero)
                {
                    PostMessage(ok, BmClick, IntPtr.Zero, IntPtr.Zero);
                }
            }

            return true;
        }, IntPtr.Zero);
    }

    private static string ClassName(IntPtr window)
    {
        StringBuilder text = new(64);
        GetClassName(window, text, text.Capacity);
        return text.ToString();
    }

    private static string WindowText(IntPtr window)
    {
        StringBuilder text = new(512);
        GetWindowText(window, text, text.Capacity);
        return text.ToString();
    }

    private delegate bool EnumProc(IntPtr window, IntPtr parameter);

    [DllImport("ole32.dll")]
    private static extern int GetRunningObjectTable(uint reserved, out IRunningObjectTable table);

    [DllImport("ole32.dll")]
    private static extern int CreateBindCtx(uint reserved, out IBindCtx context);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder text, int capacity);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int capacity);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}
