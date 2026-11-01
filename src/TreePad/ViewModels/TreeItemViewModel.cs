using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TreePad.ViewModels
{
    public partial class TreeItemViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private string _fullPath = string.Empty;

        [ObservableProperty]
        private string _relativePath = string.Empty;

        [ObservableProperty]
        private bool _isFolder;

        [ObservableProperty]
        private bool _isExpanded;

        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private bool _isEditing;

        [ObservableProperty]
        private bool _isVisible = true;

        [ObservableProperty]
        private bool _isDragOver;

        public TreeItemViewModel? Parent { get; set; }

        public ObservableCollection<TreeItemViewModel> Children { get; } = new();

        public TreeItemViewModel(string name, string fullPath, string relativePath, bool isFolder, TreeItemViewModel? parent = null)
        {
            _name = name;
            _fullPath = fullPath;
            _relativePath = relativePath;
            _isFolder = isFolder;
            Parent = parent;
        }

        public void SortChildren()
        {
            var sorted = Children.OrderByDescending(c => c.IsFolder)
                                 .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
                                 .ToList();

            for (int i = 0; i < sorted.Count; i++)
            {
                int oldIndex = Children.IndexOf(sorted[i]);
                if (oldIndex != i)
                {
                    Children.Move(oldIndex, i);
                }
            }

            foreach (var child in Children)
            {
                if (child.IsFolder)
                {
                    child.SortChildren();
                }
            }
        }
    }
}
