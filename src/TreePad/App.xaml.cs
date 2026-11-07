using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;

namespace TreePad
{
    public partial class App : Application
    {
        private const string SingleInstanceMutexName = @"Local\TreePad_SingleInstance_Mutex_v1";
        private const string ActivateEventName = @"Local\TreePad_Activate_Event_v1";
        private const long MaxCrashLogBytes = 1024 * 1024; // 1MB 롤오버 제한

        private Mutex? _instanceMutex;
        private bool _ownsMutex;
        private EventWaitHandle? _activateEvent;
        private ManualResetEvent? _exitEvent;

        public App()
        {
            // 한국어 레거시 인코딩(CP949 / EUC-KR) 지원을 위한 코드 페이지 공급자 등록
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                WriteCrashLog("UnhandledException", e.ExceptionObject?.ToString());
            };

            DispatcherUnhandledException += (s, e) =>
            {
                WriteCrashLog("DispatcherUnhandledException", e.Exception?.ToString());
                e.Handled = false;
            };

            SessionEnding += OnAppSessionEnding;
        }

        private void OnAppSessionEnding(object sender, SessionEndingCancelEventArgs e)
        {
            try
            {
                if (MainWindow is MainWindow mainWin)
                {
                    mainWin.FlushSessionOnSystemShutdown();
                }
            }
            catch (Exception ex)
            {
                WriteCrashLog("SessionEndingException", ex.ToString());
            }
        }

        private static void WriteCrashLog(string category, string? details)
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string logDir = Path.Combine(appData, "TreePad");
                Directory.CreateDirectory(logDir);
                string logPath = Path.Combine(logDir, "crash.log");

                if (File.Exists(logPath))
                {
                    var info = new FileInfo(logPath);
                    if (info.Length > MaxCrashLogBytes)
                    {
                        string backupPath = Path.Combine(logDir, "crash.old.log");
                        File.Move(logPath, backupPath, overwrite: true);
                    }
                }

                string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {category}:{Environment.NewLine}{details}{Environment.NewLine}{new string('-', 60)}{Environment.NewLine}";
                File.AppendAllText(logPath, entry, new UTF8Encoding(false));
            }
            catch
            {
                // 로그 기록 실패로 인한 2차 비정상 종료 방지
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                _instanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out _ownsMutex);
                _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);

                if (!_ownsMutex)
                {
                    // 이미 실행 중인 인스턴스가 있으면 기존 창을 앞으로 활성화하도록 신호 전달 후 즉시 종료
                    try
                    {
                        _activateEvent.Set();
                    }
                    catch { }

                    Shutdown();
                    return;
                }

                _exitEvent = new ManualResetEvent(false);
                var listenerThread = new Thread(ListenForSecondInstanceActivation)
                {
                    IsBackground = true,
                    Name = "TreePadSingleInstanceListener"
                };
                listenerThread.Start();

                base.OnStartup(e);

                var mainWindow = new MainWindow();
                MainWindow = mainWindow;
                mainWindow.Show();
            }
            catch (Exception ex)
            {
                WriteCrashLog("OnStartupException", ex.ToString());
                throw;
            }
        }

        private void ListenForSecondInstanceActivation()
        {
            if (_activateEvent == null || _exitEvent == null) return;

            WaitHandle[] handles = { _activateEvent, _exitEvent };
            while (true)
            {
                int index;
                try
                {
                    index = WaitHandle.WaitAny(handles);
                }
                catch
                {
                    break;
                }

                if (index == 1) break; // 앱 종료 이벤트

                Dispatcher?.BeginInvoke(new Action(() =>
                {
                    var win = Current?.MainWindow;
                    if (win == null) return;

                    if (win.WindowState == WindowState.Minimized)
                    {
                        win.WindowState = WindowState.Normal;
                    }

                    win.Show();
                    win.Activate();
                    win.Topmost = true;
                    win.Topmost = false;
                    win.Focus();
                }));
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                _exitEvent?.Set();
                _exitEvent?.Dispose();
                _activateEvent?.Dispose();

                if (_ownsMutex && _instanceMutex != null)
                {
                    _instanceMutex.ReleaseMutex();
                }
                _instanceMutex?.Dispose();
            }
            catch { }

            base.OnExit(e);
        }
    }
}
