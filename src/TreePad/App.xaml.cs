using System;
using System.IO;
using System.Windows;

namespace TreePad
{
    public partial class App : Application
    {
        public App()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                File.WriteAllText(@"D:\Projects\TreePad\crash.log", $"Unhandled: {e.ExceptionObject}");
            };

            DispatcherUnhandledException += (s, e) =>
            {
                File.WriteAllText(@"D:\Projects\TreePad\crash.log", $"DispatcherUnhandled: {e.Exception}");
                e.Handled = false;
            };
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                base.OnStartup(e);
            }
            catch (Exception ex)
            {
                File.WriteAllText(@"D:\Projects\TreePad\crash.log", $"OnStartup Exception: {ex}");
                throw;
            }
        }
    }
}
