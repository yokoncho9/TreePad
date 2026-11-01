using System.Windows;

namespace TreePad.Views
{
    public partial class DeleteConfirmDialog : Wpf.Ui.Controls.FluentWindow
    {
        public bool IsConfirmed { get; private set; } = false;

        public DeleteConfirmDialog(string targetName, bool isFolder)
        {
            InitializeComponent();
            TargetNameText.Text = targetName;

            bool isEnglish = Services.LocalizationService.Instance.IsEnglish;
            if (isEnglish)
            {
                string itemType = isFolder ? "folder and all its contents" : "note";
                MessageText.Text = $"Are you sure you want to move this {itemType} to the Recycle Bin?";
            }
            else
            {
                string itemType = isFolder ? "폴더 및 하위 모든 내용" : "메모";
                MessageText.Text = $"이 {itemType}을(를) 휴지통으로 이동하시겠습니까?";
            }
        }

        private void OnConfirmClick(object sender, RoutedEventArgs e)
        {
            IsConfirmed = true;
            DialogResult = true;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            IsConfirmed = false;
            DialogResult = false;
            Close();
        }
    }
}
