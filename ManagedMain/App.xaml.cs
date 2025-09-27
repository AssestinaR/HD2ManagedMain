using System.Configuration;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Threading;
using System;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using ManagedMain.Resources;

namespace ManagedMain
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        private const string UniqueAppName = "ManagedMain_SingleInstance_9f1e2d3f-8b89-4d3a-9a8a-2c9e2be0f8e1";
        private const string ActivateEventName = UniqueAppName + ".Activate";
        private static Mutex? _instanceMutex;
        private static EventWaitHandle? _activateEvent;
        private static Thread? _signalThread;

        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private const int SW_RESTORE = 9;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Apply localization BEFORE any XAML is parsed/created
            ApplyLocalization();

            // Single instance gate
            bool createdNew = false;
            _instanceMutex = new Mutex(true, UniqueAppName, out createdNew);
            if (!createdNew)
            {
                try
                {
                    using var evt = EventWaitHandle.OpenExisting(ActivateEventName);
                    evt.Set();
                }
                catch { }
                // Exit this secondary process
                Shutdown();
                return;
            }

            // Primary instance: create activation event and listener
            _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
            _signalThread = new Thread(() =>
            {
                try
                {
                    while (true)
                    {
                        _activateEvent.WaitOne();
                        try
                        {
                            Dispatcher.Invoke(BringMainWindowToFront);
                        }
                        catch { }
                    }
                }
                catch { }
            }) { IsBackground = true, Name = "ActivateSignalListener" };
            _signalThread.Start();

            base.OnStartup(e);
        }

        private void BringMainWindowToFront()
        {
            try
            {
                var win = Current?.MainWindow ?? Current?.Windows.OfType<Window>().FirstOrDefault();
                if (win == null) return;
                if (!win.IsVisible) win.Show();
                if (win.WindowState == WindowState.Minimized) win.WindowState = WindowState.Normal;
                // Toggle Topmost to force Z-order raise
                bool wasTop = win.Topmost;
                win.Topmost = true; win.Topmost = false; win.Topmost = wasTop;

                win.Activate();
                win.Focus();

                // Native fallback to ensure foreground
                var hwnd = new WindowInteropHelper(win).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    ShowWindow(hwnd, SW_RESTORE);
                    SetForegroundWindow(hwnd);
                }
            }
            catch { }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                _activateEvent?.Set(); // release any waiters
            }
            catch { }
            try { _activateEvent?.Dispose(); } catch { }
            try { _instanceMutex?.ReleaseMutex(); } catch { }
            try { _instanceMutex?.Dispose(); } catch { }
            base.OnExit(e);
        }

        private void ApplyLocalization()
        {
            // Set resx culture (satellite assembly will be used automatically by ResourceManager)
            var culture = CultureInfo.CurrentUICulture;
            Strings.Culture = culture;
            // Also set WPF element language so number/date formatting follows UI culture
            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage(culture.IetfLanguageTag)));
        }
    }
}
