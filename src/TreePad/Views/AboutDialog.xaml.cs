using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace TreePad.Views
{
    public partial class AboutDialog : Wpf.Ui.Controls.FluentWindow
    {
        public AboutDialog()
        {
            InitializeComponent();
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            if (version != null)
            {
                VersionBadgeText.Text = $"v{version.Major}.{version.Minor}.{version.Build}";
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OnHyperlinkRequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
            }
            catch { }
            e.Handled = true;
        }
    }
}
