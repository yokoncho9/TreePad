using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace TreePad.Views
{
    public partial class AboutDialog : Wpf.Ui.Controls.FluentWindow
    {
        public AboutDialog()
        {
            InitializeComponent();
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
