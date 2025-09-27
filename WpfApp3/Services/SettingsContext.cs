namespace LiberTeaManager.Services
{
    /// <summary>
    /// 全局只读访问当前运行期的设置服务。用于仍为 static 的遗留帮助类。
    /// 通过 MainWindow 启动时调用 Initialize 进行注入，避免直接依赖旧的 AppSettings 静态类。
    /// </summary>
    internal static class SettingsContext
    {
        public static ISettingsService? Instance { get; private set; }
        public static void Initialize(ISettingsService service) => Instance = service;
        public static string ModFolder => Instance?.ModFolder ?? System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "mod");
        public static string GameFolder => Instance?.GameFolder ?? string.Empty;
    }
}
