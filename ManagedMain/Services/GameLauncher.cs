using System.Diagnostics;
using System.IO;
using System;

namespace ManagedMain.Services
{
    public static class GameLauncher
    {
        private const string AppId = "553850"; // Helldivers 2

        public static void LaunchHelldivers2(ILogService log)
        {
            try
            {
                // Try protocol first
                try
                {
                    var psiProto = new ProcessStartInfo($"steam://run/{AppId}") { UseShellExecute = true };
                    Process.Start(psiProto);
                    log.Log(ManagedMain.Resources.Strings.SR_Log_LaunchGame_Protocol);
                    return;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[GameLauncher] 协议启动失败，回退到 steam.exe: " + ex.Message);
                }

                var steamRoot = SteamLocator.FindSteamRoot();
                if (string.IsNullOrWhiteSpace(steamRoot))
                {
                    log.Log(ManagedMain.Resources.Strings.SR_Log_NoSteamRoot);
                    return;
                }
                var exe = Path.Combine(steamRoot, "steam.exe");
                if (!File.Exists(exe)) exe = Path.Combine(steamRoot, "Steam.exe");
                if (!File.Exists(exe))
                {
                    log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_SteamExeNotFound, steamRoot));
                    return;
                }
                var psi = new ProcessStartInfo(exe, $"-applaunch {AppId}")
                {
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(exe)!
                };
                Process.Start(psi);
                log.Log(ManagedMain.Resources.Strings.SR_Log_LaunchGame_SteamExe);
            }
            catch (System.Exception ex)
            {
                log.Log(string.Format(ManagedMain.Resources.Strings.SR_Log_LaunchGame_Failed, ex.Message));
            }
        }

        public static bool IsHelldivers2Running()
        {
            try
            {
                var procs = Process.GetProcessesByName("helldivers2");
                if (procs != null && procs.Length > 0) return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[GameLauncher] 检测进程失败: " + ex.Message);
            }
            return false;
        }
    }
}
