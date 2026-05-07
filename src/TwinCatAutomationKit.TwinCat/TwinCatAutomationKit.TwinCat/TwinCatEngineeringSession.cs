using System.Runtime.InteropServices;
using EnvDTE;
using TCatSysManagerLib;

namespace TwinCatAutomationKit.TwinCat;

public sealed class TwinCatEngineeringSession : IDisposable
{
    private static readonly object MessageFilterLock = new();
    private static bool _messageFilterRegistered;
    private bool _disposed;

    public TwinCatEngineeringSession(DTE dte)
    {
        Dte = dte ?? throw new ArgumentNullException(nameof(dte));
        EnsureMessageFilterRegistered();
    }

    internal DTE Dte { get; }

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

        ReleaseComObjectIfNeeded(SysManager);
        ReleaseComObjectIfNeeded(TwinCatProject);
        ReleaseComObjectIfNeeded(Dte);
        _disposed = true;
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
}
