# Runs an exe on a private, invisible Windows desktop so no window appears on the user's screen (used for the UI smoke check).
# Prints exit=<code> or TIMEOUT.
param([Parameter(Mandatory)][string]$Exe, [string]$Arguments = '', [int]$TimeoutSeconds = 120)
# Runs an exe on a private, invisible desktop so nothing appears on the user's screen. Prints the exit code (or TIMEOUT).
if (-not ('Hidden' -as [type])) {
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public static class Hidden {
  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct STARTUPINFO { public int cb; public string lpReserved, lpDesktop, lpTitle; public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags; public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError; }
  [StructLayout(LayoutKind.Sequential)] public struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }
  [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateDesktop(string name, IntPtr dev, IntPtr mode, int flags, uint access, IntPtr sa);
  [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr h);
  [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CreateProcess(string app, string cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr env, string dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
  [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr h, uint ms);
  [DllImport("kernel32.dll")] static extern bool GetExitCodeProcess(IntPtr h, out uint code);
  [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr h, uint code);
  [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
  public static long Run(string exe, string args, int seconds) {
    string name = "hanki-" + Guid.NewGuid().ToString("N");
    var desk = CreateDesktop(name, IntPtr.Zero, IntPtr.Zero, 0, 0x10000000, IntPtr.Zero);
    if (desk == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
    try {
      var si = new STARTUPINFO { lpDesktop = "winsta0\\" + name }; si.cb = Marshal.SizeOf(si);
      PROCESS_INFORMATION pi;
      if (!CreateProcess(null, "\"" + exe + "\" " + args, IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, System.IO.Path.GetDirectoryName(exe), ref si, out pi)) throw new System.ComponentModel.Win32Exception();
      try {
        if (WaitForSingleObject(pi.hProcess, (uint)(seconds * 1000)) != 0) { TerminateProcess(pi.hProcess, 99); return -1; }
        uint code; GetExitCodeProcess(pi.hProcess, out code); return code;
      } finally { CloseHandle(pi.hProcess); CloseHandle(pi.hThread); }
    } finally { CloseDesktop(desk); }
  }
}
'@
}
$code = [Hidden]::Run($Exe, $Arguments, $TimeoutSeconds)
if ($code -eq -1) { 'TIMEOUT' } else { "exit=$code" }
