using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TreePad.ViewModels
{
    public partial class NoteTabViewModel : ObservableObject
    {
        public string FilePath { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;

        [ObservableProperty]
        private string _title = string.Empty;

        [ObservableProperty]
        private string _content = string.Empty;

        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private int _caretIndex;

        private readonly MainViewModel _mainVm;

        public NoteTabViewModel(string filePath, string relativePath, string title, string content, MainViewModel mainVm)
        {
            FilePath = filePath;
            RelativePath = relativePath;
            _title = title;
            _content = content;
            _mainVm = mainVm;
        }

        [RelayCommand]
        public void Close()
        {
            _mainVm.CloseTab(this);
        }
    }
}
