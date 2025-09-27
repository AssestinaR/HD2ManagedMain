using System.Configuration;
using System.Data;
using System.Windows;
using LiberTeaManager.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LiberTeaManager
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public IHost? Host { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            // 构建宿主与基础服务
            Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    // 基础日志（启动阶段占位，窗口内会使用 UI 日志覆盖显示）
                    services.AddSingleton<ILogService>(_ => new UiLogService(_ => { }));
                    // 设置服务（单例，加载配置）
                    services.AddSingleton<ISettingsService>(sp =>
                    {
                        var log = sp.GetRequiredService<ILogService>();
                        var settings = new SettingsService(log);
                        settings.Load();
                        return settings;
                    });
                })
                .Build();

            // 初始化全局 SettingsContext（兼容现有代码）
            var settingsSvc = Host.Services.GetRequiredService<ISettingsService>();
            LiberTeaManager.Services.SettingsContext.Initialize(settingsSvc);

            base.OnStartup(e);

            // 创建并显示主窗口
            var mainWin = new MainWindow();
            mainWin.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { Host?.Dispose(); } catch { }
            base.OnExit(e);
        }
    }
}
