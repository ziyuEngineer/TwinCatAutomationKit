using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using EnvDTE;
using TCatSysManagerLib;
using DiagnosticsProcess = System.Diagnostics.Process;
using ThreadingThread = System.Threading.Thread;

namespace TwinCatAutomationKit.TwinCat;

public sealed class TwinCatEngineeringSession : IDisposable
{
    private static readonly object MessageFilterLock = new();
    private static bool _messageFilterRegistered;
    private bool _disposed;

    private readonly TwinCatDialogAutoDismissScope? _dialogAutoDismissScope;
    private readonly IReadOnlyDictionary<string, int> _initialAutoDismissedDialogs;

    public TwinCatEngineeringSession(
        DTE dte,
        bool attachedToExisting = true,
        IReadOnlyCollection<int>? targetProcessIds = null,
        bool enableDialogAutoDismiss = false,
        int dialogPollIntervalMs = 500,
        IReadOnlyDictionary<string, int>? initialAutoDismissedDialogs = null)
    {
        Dte = dte ?? throw new ArgumentNullException(nameof(dte));
        AttachedToExisting = attachedToExisting;
        TargetProcessIds = ResolveTargetProcessIds(dte, targetProcessIds);
        _initialAutoDismissedDialogs = initialAutoDismissedDialogs is { Count: > 0 }
            ? new Dictionary<string, int>(initialAutoDismissedDialogs, StringComparer.OrdinalIgnoreCase)
            : EmptyDialogSnapshot;
        EnsureMessageFilterRegistered();
        if (enableDialogAutoDismiss && TargetProcessIds.Count > 0)
        {
            _dialogAutoDismissScope = TwinCatDialogAutoDismissScope.Start(TargetProcessIds, dialogPollIntervalMs);
        }
    }

    internal DTE Dte { get; }

    public bool AttachedToExisting { get; }

    public IReadOnlyCollection<int> TargetProcessIds { get; }

    public IReadOnlyDictionary<string, int> AutoDismissedDialogs => MergeAutoDismissedDialogs(
        _initialAutoDismissedDialogs,
        _dialogAutoDismissScope?.Snapshot());

    internal Project? TwinCatProject { get; private set; }

    internal ITcSysManager? SysManager { get; private set; }

    public string? CurrentSolutionDirectory { get; internal set; }

    public string? CurrentCppProjectName { get; internal set; }

    internal void AttachProject(Project project, ITcSysManager sysManager, string solutionDirectory)
    {
        TwinCatProject = project;
        SysManager = sysManager;
        CurrentSolutionDirectory = solutionDirectory;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _dialogAutoDismissScope?.Dispose();
        ReleaseComObjectIfNeeded(SysManager);
        ReleaseComObjectIfNeeded(TwinCatProject);
        ReleaseComObjectIfNeeded(Dte);
        _disposed = true;
    }

    private static readonly IReadOnlyDictionary<string, int> EmptyDialogSnapshot =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, int> MergeAutoDismissedDialogs(
        IReadOnlyDictionary<string, int>? first,
        IReadOnlyDictionary<string, int>? second)
    {
        if ((first is null || first.Count == 0) && (second is null || second.Count == 0))
        {
            return EmptyDialogSnapshot;
        }

        Dictionary<string, int> merged = new(StringComparer.OrdinalIgnoreCase);
        AddAutoDismissedDialogs(merged, first);
        AddAutoDismissedDialogs(merged, second);
        return merged;
    }

    private static void AddAutoDismissedDialogs(
        Dictionary<string, int> target,
        IReadOnlyDictionary<string, int>? source)
    {
        if (source is null)
        {
            return;
        }

        foreach ((string key, int count) in source)
        {
            if (string.IsNullOrWhiteSpace(key) || count <= 0)
            {
                continue;
            }

            target[key] = target.TryGetValue(key, out int existing) ? existing + count : count;
        }
    }

    private static void EnsureMessageFilterRegistered()
    {
        lock (MessageFilterLock)
        {
            if (_messageFilterRegistered)
            {
                return;
            }

            MessageFilter.Register();
            _messageFilterRegistered = true;
        }
    }

    private static void ReleaseComObjectIfNeeded(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            try
            {
                Marshal.ReleaseComObject(value);
            }
            catch
            {
            }
        }
    }

    private static IReadOnlyCollection<int> ResolveTargetProcessIds(DTE dte, IReadOnlyCollection<int>? targetProcessIds)
    {
        HashSet<int> result = targetProcessIds is { Count: > 0 }
            ? new HashSet<int>(targetProcessIds)
            : [];

        try
        {
            IntPtr hwnd = new(dte.MainWindow.HWnd);
            if (hwnd != IntPtr.Zero)
            {
                GetWindowThreadProcessId(hwnd, out int processId);
                if (processId > 0)
                {
                    result.Add(processId);
                }
            }
        }
        catch
        {
            // Process-id discovery is best-effort; caller-supplied ids still protect newly launched hosts.
        }

        return result.ToArray();
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int processId);

    [ComImport]
    [Guid("00000016-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IOleMessageFilter
    {
        [PreserveSig]
        int HandleInComingCall(int dwCallType, IntPtr hTaskCaller, int dwTickCount, IntPtr lpInterfaceInfo);

        [PreserveSig]
        int RetryRejectedCall(IntPtr hTaskCallee, int dwTickCount, int dwRejectType);

        [PreserveSig]
        int MessagePending(IntPtr hTaskCallee, int dwTickCount, int dwPendingType);
    }

    private sealed class MessageFilter : IOleMessageFilter
    {
        public static void Register() => CoRegisterMessageFilter(new MessageFilter(), out _);

        public static void Revoke() => CoRegisterMessageFilter(null, out _);

        int IOleMessageFilter.HandleInComingCall(int dwCallType, IntPtr hTaskCaller, int dwTickCount, IntPtr lpInterfaceInfo) => 0;

        int IOleMessageFilter.RetryRejectedCall(IntPtr hTaskCallee, int dwTickCount, int dwRejectType) =>
            dwRejectType == 2 ? 250 : -1;

        int IOleMessageFilter.MessagePending(IntPtr hTaskCallee, int dwTickCount, int dwPendingType) => 2;

        [DllImport("Ole32.dll")]
        private static extern int CoRegisterMessageFilter(IOleMessageFilter? newFilter, out IOleMessageFilter? oldFilter);
    }

    internal sealed class TwinCatDialogAutoDismissScope : IDisposable
    {
        private const int BmClick = 0x00F5;
        private const int GwOwner = 4;
        private const uint WmClose = 0x0010;

        private static readonly string[] SafeConfirmTextHints =
        [
            "ok",
            "&ok",
            "确定",
        ];

        private static readonly string[] SafeCancelTextHints =
        [
            "cancel",
            "&cancel",
            "取消",
            "否(&n)",
            "否",
            "no",
            "&no",
        ];

        private static readonly string[] PositiveConfirmationTitleHints =
        [
            "activate",
            "restart",
            "twincat",
            "reload",
            "activate configuration",
            "run mode",
            "save",
            "save changes",
            "保存",
            "激活",
            "重启",
            "重新启动",
        ];

        private static readonly string[] PositiveConfirmationButtonHints =
        [
            "yes",
            "&yes",
            "是(&y)",
            "是",
        ];

        private static readonly string[] KnownHostTitleHints =
        [
            "visual studio",
            "tcxaeshell",
            "twincat",
            "microsoft visual studio",
        ];

        private readonly HashSet<int> _targetProcessIds;
        private readonly int _pollIntervalMs;
        private readonly CancellationTokenSource _cancellation = new();
        private readonly ConcurrentDictionary<string, int> _dismissed = new(StringComparer.OrdinalIgnoreCase);
        private readonly ThreadingThread _thread;
        private bool _disposed;

        private TwinCatDialogAutoDismissScope(IReadOnlyCollection<int> targetProcessIds, int pollIntervalMs)
        {
            _targetProcessIds = new HashSet<int>(targetProcessIds);
            _pollIntervalMs = pollIntervalMs > 0 ? pollIntervalMs : 500;
            _thread = new ThreadingThread(Loop)
            {
                IsBackground = true,
                Name = "TwinCAT unattended dialog auto-dismiss"
            };
        }

        public static TwinCatDialogAutoDismissScope Start(IReadOnlyCollection<int> targetProcessIds, int pollIntervalMs)
        {
            TwinCatDialogAutoDismissScope scope = new(targetProcessIds, pollIntervalMs);
            scope._thread.Start();
            return scope;
        }

        public void AddTargetProcessIds(IEnumerable<int> processIds)
        {
            lock (_targetProcessIds)
            {
                foreach (int processId in processIds)
                {
                    if (processId > 0)
                    {
                        _targetProcessIds.Add(processId);
                    }
                }
            }
        }

        public IReadOnlyDictionary<string, int> Snapshot() =>
            _dismissed.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _cancellation.Cancel();
            if (!_thread.Join(Math.Min(_pollIntervalMs * 2, 2000)))
            {
                // The watcher is a background thread; never block teardown on it.
            }

            _cancellation.Dispose();
            _disposed = true;
        }

        private void Loop()
        {
            while (!_cancellation.IsCancellationRequested)
            {
                try
                {
                    ScanOnce();
                }
                catch
                {
                    // Keep unattended protection best-effort; COM automation must remain the primary result.
                }

                try
                {
                    _cancellation.Token.WaitHandle.WaitOne(_pollIntervalMs);
                }
                catch
                {
                    return;
                }
            }
        }

        private void ScanOnce()
        {
            HashSet<int> targetProcessIds;
            lock (_targetProcessIds)
            {
                if (_targetProcessIds.Count == 0)
                {
                    return;
                }

                targetProcessIds = new HashSet<int>(_targetProcessIds);
            }

            if (targetProcessIds.Count == 0)
            {
                return;
            }

            EnumWindows((hwnd, _) =>
            {
                try
                {
                    if (!IsWindowVisible(hwnd))
                    {
                        return true;
                    }

                    GetWindowThreadProcessId(hwnd, out int processId);
                    if (!targetProcessIds.Contains(processId))
                    {
                        return true;
                    }

                    string className = GetClassNameText(hwnd);
                    string title = GetWindowTextString(hwnd);
                    bool ownedDialog = GetWindow(hwnd, GwOwner) != IntPtr.Zero;
                    bool standardDialog = string.Equals(className, "#32770", StringComparison.Ordinal);
                    if (!ownedDialog && !standardDialog)
                    {
                        return true;
                    }

                    if (!IsLikelyDialog(className, title))
                    {
                        return true;
                    }

                    IReadOnlyList<IntPtr> buttons = GetChildButtons(hwnd);
                    string dialogText = ReadDialogText(hwnd, title);
                    IntPtr button = SelectPreferredButton(dialogText, buttons);
                    if (button != IntPtr.Zero)
                    {
                        string buttonText = Normalize(GetWindowTextString(button));
                        SendMessage(button, BmClick, IntPtr.Zero, IntPtr.Zero);
                        Record(title, buttonText.Length == 0 ? "button" : buttonText, dialogText);
                    }
                    else
                    {
                        PostMessage(hwnd, WmClose, IntPtr.Zero, IntPtr.Zero);
                        Record(title, "wm-close", dialogText);
                    }
                }
                catch
                {
                }

                return true;
            }, IntPtr.Zero);
        }

        private static bool IsLikelyDialog(string className, string title)
        {
            if (string.Equals(className, "#32770", StringComparison.Ordinal))
            {
                return true;
            }

            string normalized = Normalize(title);
            return normalized.Length > 0 && KnownHostTitleHints.Any(hint => normalized.Contains(hint, StringComparison.OrdinalIgnoreCase));
        }

        private static IReadOnlyList<IntPtr> GetChildButtons(IntPtr hwnd)
        {
            List<IntPtr> buttons = [];
            EnumChildWindows(hwnd, (child, _) =>
            {
                if (string.Equals(GetClassNameText(child), "Button", StringComparison.OrdinalIgnoreCase))
                {
                    buttons.Add(child);
                }

                return true;
            }, IntPtr.Zero);
            return buttons;
        }

        private static string ReadDialogText(IntPtr hwnd, string title)
        {
            List<string> parts = [];
            if (!string.IsNullOrWhiteSpace(title))
            {
                parts.Add(title);
            }

            EnumChildWindows(hwnd, (child, _) =>
            {
                string text = GetWindowTextString(child);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    parts.Add(text);
                }

                return true;
            }, IntPtr.Zero);

            return string.Join("\n", parts);
        }

        private static IntPtr SelectPreferredButton(string dialogText, IReadOnlyList<IntPtr> buttons)
        {
            IntPtr? safeConfirm = FindButton(buttons, SafeConfirmTextHints);
            if (safeConfirm.HasValue)
            {
                return safeConfirm.Value;
            }

            string normalizedDialogText = Normalize(dialogText);
            bool isPositiveConfirmation =
                PositiveConfirmationTitleHints.Any(hint => normalizedDialogText.Contains(hint, StringComparison.OrdinalIgnoreCase));
            if (isPositiveConfirmation)
            {
                IntPtr? positiveConfirm = FindButton(buttons, PositiveConfirmationButtonHints);
                if (positiveConfirm.HasValue)
                {
                    return positiveConfirm.Value;
                }
            }

            IntPtr? cancel = FindButton(buttons, SafeCancelTextHints);
            if (cancel.HasValue)
            {
                return cancel.Value;
            }

            return IntPtr.Zero;
        }

        private static IntPtr? FindButton(IReadOnlyList<IntPtr> buttons, IReadOnlyCollection<string> hints)
        {
            foreach (IntPtr button in buttons)
            {
                string text = Normalize(GetWindowTextString(button));
                if (hints.Any(hint => text.Equals(hint, StringComparison.OrdinalIgnoreCase)))
                {
                    return button;
                }
            }

            return null;
        }

        private void Record(string title, string action, string dialogText)
        {
            string key = string.IsNullOrWhiteSpace(title) ? "(untitled)" : title.Trim();
            key = $"{key} -> {action}; text={NormalizeDiagnosticText(dialogText)}";
            _dismissed.AddOrUpdate(key, 1, (_, count) => count + 1);
        }

        private static string Normalize(string value) => value.Trim().TrimEnd(':').ToLowerInvariant();

        private static string NormalizeDiagnosticText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "(empty)";
            }

            string normalized = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return normalized.Length <= 120 ? normalized : normalized[..117] + "...";
        }

        private static string GetWindowTextString(IntPtr hwnd)
        {
            int length = GetWindowTextLength(hwnd);
            if (length <= 0)
            {
                return string.Empty;
            }

            StringBuilder builder = new(length + 1);
            _ = GetWindowText(hwnd, builder, builder.Capacity);
            return builder.ToString();
        }

        private static string GetClassNameText(IntPtr hwnd)
        {
            StringBuilder builder = new(256);
            _ = GetClassName(hwnd, builder, builder.Capacity);
            return builder.ToString();
        }

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hwndParent, EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hwnd, int command);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr hwnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    }
}
