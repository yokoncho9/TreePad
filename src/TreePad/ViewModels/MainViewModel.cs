using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TreePad.Models;
using TreePad.Services;

namespace TreePad.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly StorageService _storageService;
        private readonly SessionService _sessionService;
        private readonly SessionConfig _sessionConfig;
        private readonly DispatcherTimer _autoSaveTimer;
        private bool _isSwitchingTab = false;

        public ObservableCollection<TreeItemViewModel> RootItems { get; } = new();
        public ObservableCollection<NoteTabViewModel> Tabs { get; } = new();
        public List<string> AvailableFontSizes { get; } = new() { "11pt", "12pt", "13pt", "14pt", "15pt", "16pt", "18pt", "20pt", "24pt", "28pt" };

        [ObservableProperty]
        private string _selectedFontSize = "15pt";

        [ObservableProperty]
        private TreeItemViewModel? _selectedItem;

        [ObservableProperty]
        private NoteTabViewModel? _activeTab;

        [ObservableProperty]
        private bool _hasOpenTabs = false;

        [ObservableProperty]
        private string _saveStatus = "준비";

        [ObservableProperty]
        private string _searchQuery = string.Empty;

        [ObservableProperty]
        private int _characterCount = 0;

        [ObservableProperty]
        private int _characterCountNoSpaces = 0;

        [ObservableProperty]
        private int _lineCount = 1;

        [ObservableProperty]
        private int _currentLine = 1;

        [ObservableProperty]
        private int _currentColumn = 1;

        [ObservableProperty]
        private double _fontSize = 15.0;

        [ObservableProperty]
        private bool _wordWrap = true;

        [ObservableProperty]
        private string _wordWrapStatusText = "줄바꿈 ON";

        [ObservableProperty]
        private string _wordWrapTooltip = "자동 줄바꿈: 켜짐 (Alt+Z)";

        [ObservableProperty]
        private bool _isDarkMode = false;

        [ObservableProperty]
        private string _themeIcon = "WeatherMoon20";

        [ObservableProperty]
        private string _themeTooltip = "테마 전환: 다크 모드로 변경";

        [ObservableProperty]
        private double _sidebarWidth = 280;

        [ObservableProperty]
        private bool _isSidebarVisible = true;

        [ObservableProperty]
        private string _storageDirectoryPath = string.Empty;

        [ObservableProperty]
        private string _currentLocationDescription = "ROOT";

        [ObservableProperty]
        private bool _isFolderSelected = false;

        [ObservableProperty]
        private string _currentLanguageSetting = "Auto";

        [ObservableProperty]
        private bool _isLangAuto = true;

        [ObservableProperty]
        private bool _isLangKo = false;

        [ObservableProperty]
        private bool _isLangEn = false;

        public MainViewModel()
        {
            _sessionService = new SessionService();
            _sessionConfig = _sessionService.LoadSession();

            // 🌐 다국어 지원 초기화 (기본: 시스템 언어 자동 감지)
            LocalizationService.Instance.Initialize(_sessionConfig.Language);
            UpdateLanguageState();

            _storageService = new StorageService(_sessionConfig.StorageDirectory);
            _storageDirectoryPath = _storageService.RootDirectory;

            _fontSize = _sessionConfig.FontSize;
            _selectedFontSize = $"{(int)Math.Round(_fontSize)}pt";
            _wordWrap = _sessionConfig.WordWrap;
            UpdateWordWrapTexts();

            _sidebarWidth = _sessionConfig.SidebarWidth > 50 ? _sessionConfig.SidebarWidth : 280;
            _isSidebarVisible = _sessionConfig.IsSidebarVisible;

            // 자동 저장 디바운스 타이머 (300ms)
            _autoSaveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _autoSaveTimer.Tick += async (s, e) =>
            {
                _autoSaveTimer.Stop();
                await CommitAutoSaveAsync();
            };

            LoadTreeAndRestoreSession();
        }

        public SessionConfig GetSessionConfig() => _sessionConfig;

        private void LoadTreeAndRestoreSession()
        {
            RootItems.Clear();
            var expandedPaths = new HashSet<string>(_sessionConfig.ExpandedFolders);
            var loaded = _storageService.LoadTree(expandedPaths);

            foreach (var item in loaded)
            {
                RootItems.Add(item);
            }

            // 세션에서 열려 있던 탭들 복원
            Tabs.Clear();
            var tabsToRestore = _sessionConfig.OpenTabs != null && _sessionConfig.OpenTabs.Count > 0 
                ? _sessionConfig.OpenTabs 
                : new List<string>();

            // 저장된 탭이 없고 LastSelectedFilePath만 있는 경우 호환
            if (tabsToRestore.Count == 0 && !string.IsNullOrEmpty(_sessionConfig.LastSelectedFilePath))
            {
                tabsToRestore.Add(_sessionConfig.LastSelectedFilePath);
            }

            foreach (var relPath in tabsToRestore)
            {
                var treeItem = FindItemByRelativePath(RootItems, relPath);
                if (treeItem != null && !treeItem.IsFolder)
                {
                    OpenTabInternal(treeItem, makeActive: false);
                }
            }

            // 활성 탭 결정
            NoteTabViewModel? targetTab = null;
            if (!string.IsNullOrEmpty(_sessionConfig.LastSelectedFilePath))
            {
                targetTab = Tabs.FirstOrDefault(t => string.Equals(t.RelativePath, _sessionConfig.LastSelectedFilePath, StringComparison.OrdinalIgnoreCase));
            }

            if (targetTab == null && Tabs.Count > 0)
            {
                targetTab = Tabs[0];
            }

            if (targetTab != null)
            {
                ActiveTab = targetTab;
            }
            else
            {
                // 열린 탭이 전혀 없으면 트리의 첫 번째 메모를 탭으로 오픈
                var firstNote = FindFirstNote(RootItems);
                if (firstNote != null)
                {
                    OpenNoteInTab(firstNote);
                }
            }

            HasOpenTabs = Tabs.Count > 0;
        }

        private TreeItemViewModel? FindItemByRelativePath(IEnumerable<TreeItemViewModel> items, string relPath)
        {
            foreach (var item in items)
            {
                if (string.Equals(item.RelativePath, relPath, StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }

                if (item.IsFolder)
                {
                    var found = FindItemByRelativePath(item.Children, relPath);
                    if (found != null) return found;
                }
            }
            return null;
        }

        private TreeItemViewModel? FindFirstNote(IEnumerable<TreeItemViewModel> items)
        {
            foreach (var item in items)
            {
                if (!item.IsFolder) return item;
                var found = FindFirstNote(item.Children);
                if (found != null) return found;
            }
            return null;
        }

        partial void OnSelectedItemChanged(TreeItemViewModel? value)
        {
            if (value != null && !value.IsFolder)
            {
                OpenNoteInTab(value);
            }
            UpdateCurrentLocationDescription();
        }

        private void UpdateCurrentLocationDescription()
        {
            if (SelectedItem == null)
            {
                CurrentLocationDescription = "ROOT";
                IsFolderSelected = false;
            }
            else if (SelectedItem.IsFolder)
            {
                CurrentLocationDescription = $"ROOT / {SelectedItem.Name}";
                IsFolderSelected = true;
            }
            else
            {
                string? dir = Path.GetDirectoryName(SelectedItem.FullPath);
                if (string.IsNullOrEmpty(dir) || string.Equals(dir, _storageService.RootDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    CurrentLocationDescription = "ROOT";
                    IsFolderSelected = false;
                }
                else
                {
                    string parent = Path.GetFileName(dir);
                    CurrentLocationDescription = $"ROOT / {parent}";
                    IsFolderSelected = true;
                }
            }
        }

        partial void OnActiveTabChanged(NoteTabViewModel? value)
        {
            _isSwitchingTab = true;
            try
            {
                if (value != null)
                {
                    foreach (var tab in Tabs)
                    {
                        tab.IsSelected = (tab == value);
                    }
                    _sessionConfig.LastSelectedFilePath = value.RelativePath;
                    UpdateTextStatistics(value.Content);
                    SaveStatus = "저장됨";
                }
                else
                {
                    UpdateTextStatistics(string.Empty);
                    SaveStatus = "열린 메모 없음";
                }
                HasOpenTabs = (value != null);
            }
            finally
            {
                _isSwitchingTab = false;
            }
        }

        public void NotifyContentChanged()
        {
            if (_isSwitchingTab || ActiveTab == null) return;

            UpdateTextStatistics(ActiveTab.Content);
            SaveStatus = "입력 중...";
            _autoSaveTimer.Stop();
            _autoSaveTimer.Start();
        }

        private void UpdateTextStatistics(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                CharacterCount = 0;
                CharacterCountNoSpaces = 0;
                LineCount = 1;
                return;
            }

            CharacterCount = text.Length;
            CharacterCountNoSpaces = text.Count(c => !char.IsWhiteSpace(c));

            int lines = 1;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n') lines++;
            }
            LineCount = lines;
        }

        public void OpenNoteInTab(TreeItemViewModel note)
        {
            if (note.IsFolder) return;

            // 이미 열려 있는 탭인지 확인
            var existing = Tabs.FirstOrDefault(t => string.Equals(t.FilePath, note.FullPath, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                ActiveTab = existing;
                return;
            }

            OpenTabInternal(note, makeActive: true);
        }

        private void OpenTabInternal(TreeItemViewModel note, bool makeActive)
        {
            string content = string.Empty;
            try
            {
                if (File.Exists(note.FullPath))
                {
                    content = File.ReadAllText(note.FullPath, System.Text.Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                content = $"파일을 읽는 중 오류 발생: {ex.Message}";
            }

            var tab = new NoteTabViewModel(note.FullPath, note.RelativePath, note.Name, content, this);
            Tabs.Add(tab);
            HasOpenTabs = true;

            if (makeActive)
            {
                ActiveTab = tab;
            }
        }

        public void CloseTab(NoteTabViewModel tab)
        {
            // 닫기 전 변경 내용 저장
            _autoSaveTimer.Stop();
            try
            {
                if (File.Exists(tab.FilePath))
                {
                    File.WriteAllText(tab.FilePath, tab.Content, System.Text.Encoding.UTF8);
                }
            }
            catch { }

            int index = Tabs.IndexOf(tab);
            Tabs.Remove(tab);

            if (ActiveTab == tab)
            {
                if (Tabs.Count > 0)
                {
                    int nextIndex = Math.Min(index, Tabs.Count - 1);
                    ActiveTab = Tabs[nextIndex];
                }
                else
                {
                    ActiveTab = null;
                }
            }

            HasOpenTabs = Tabs.Count > 0;
        }

        [RelayCommand]
        public void CloseActiveTab()
        {
            if (ActiveTab != null)
            {
                CloseTab(ActiveTab);
            }
        }

        [RelayCommand]
        public void CloseAllTabs()
        {
            var tabList = Tabs.ToList();
            foreach (var tab in tabList)
            {
                CloseTab(tab);
            }
        }

        [RelayCommand]
        public void CloseOtherTabs(NoteTabViewModel keepTab)
        {
            var tabsToClose = Tabs.Where(t => t != keepTab).ToList();
            foreach (var tab in tabsToClose)
            {
                CloseTab(tab);
            }
            ActiveTab = keepTab;
        }

        private async Task CommitAutoSaveAsync()
        {
            if (ActiveTab == null || _isSwitchingTab) return;

            SaveStatus = "저장 중...";
            try
            {
                await _storageService.SaveNoteContentAsync(ActiveTab.FilePath, ActiveTab.Content);
                SaveStatus = "저장됨";
            }
            catch (Exception ex)
            {
                SaveStatus = $"저장 실패: {ex.Message}";
            }
        }

        public void UpdateCursorPosition(int line, int column)
        {
            CurrentLine = line;
            CurrentColumn = column;
        }

        partial void OnSearchQueryChanged(string value)
        {
            FilterTree(RootItems, value?.Trim().ToLowerInvariant() ?? string.Empty);
        }

        private bool FilterTree(IEnumerable<TreeItemViewModel> items, string query)
        {
            bool anyMatch = false;
            foreach (var item in items)
            {
                if (string.IsNullOrEmpty(query))
                {
                    item.IsVisible = true;
                    if (item.IsFolder)
                    {
                        FilterTree(item.Children, query);
                    }
                    anyMatch = true;
                    continue;
                }

                bool matchThis = item.Name.ToLowerInvariant().Contains(query);
                bool childMatch = false;

                if (item.IsFolder)
                {
                    childMatch = FilterTree(item.Children, query);
                    if (childMatch)
                    {
                        item.IsExpanded = true;
                    }
                }

                item.IsVisible = matchThis || childMatch;
                if (item.IsVisible) anyMatch = true;
            }
            return anyMatch;
        }

        [RelayCommand]
        public void CreateNewNote()
        {
            string parentDir = GetSelectedDirectory();
            try
            {
                string newFilePath = _storageService.CreateNote(parentDir, "새 메모");
                RefreshTreeAndSelect(newFilePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"메모 생성 중 오류 발생: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public void CreateNewFolder()
        {
            string parentDir = GetSelectedDirectory();
            try
            {
                string newDirPath = _storageService.CreateFolder(parentDir, "새 폴더");
                RefreshTreeAndSelect(newDirPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"폴더 생성 중 오류 발생: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public void CreateRootNote()
        {
            try
            {
                string newFilePath = _storageService.CreateNote(_storageService.RootDirectory, "새 메모");
                RefreshTreeAndSelect(newFilePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"최상단 새 메모 생성 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public void CreateRootFolder()
        {
            try
            {
                string newDirPath = _storageService.CreateFolder(_storageService.RootDirectory, "새 폴더");
                RefreshTreeAndSelect(newDirPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"최상단 새 폴더 생성 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public void ClearTreeSelection()
        {
            DeselectAll(RootItems);
            SelectedItem = null;
            UpdateCurrentLocationDescription();
        }

        private void DeselectAll(IEnumerable<TreeItemViewModel> items)
        {
            foreach (var item in items)
            {
                item.IsSelected = false;
                if (item.Children.Count > 0)
                {
                    DeselectAll(item.Children);
                }
            }
        }

        private string GetSelectedDirectory()
        {
            if (SelectedItem == null)
            {
                return _storageService.RootDirectory;
            }

            if (SelectedItem.IsFolder)
            {
                return SelectedItem.FullPath;
            }

            return Path.GetDirectoryName(SelectedItem.FullPath) ?? _storageService.RootDirectory;
        }

        [RelayCommand]
        public void RenameItem(TreeItemViewModel? target)
        {
            var item = target ?? SelectedItem;
            if (item == null) return;
            item.IsEditing = true;
        }

        public void CommitRename(TreeItemViewModel item, string newName)
        {
            item.IsEditing = false;
            if (string.IsNullOrWhiteSpace(newName) || string.Equals(item.Name, newName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                string oldPath = item.FullPath;
                string newPath = _storageService.RenameItem(item.FullPath, newName, item.IsFolder);
                item.Name = Path.GetFileNameWithoutExtension(newPath);
                item.FullPath = newPath;
                item.RelativePath = Path.GetRelativePath(_storageService.RootDirectory, newPath);

                // 열려 있는 탭의 파일 경로 및 제목도 갱신
                var tab = Tabs.FirstOrDefault(t => string.Equals(t.FilePath, oldPath, StringComparison.OrdinalIgnoreCase));
                if (tab != null)
                {
                    tab.FilePath = newPath;
                    tab.RelativePath = item.RelativePath;
                    tab.Title = item.Name;
                }

                if (item.Parent != null)
                {
                    item.Parent.SortChildren();
                }
                else
                {
                    SortRootItems();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"이름 변경 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SortRootItems()
        {
            var sorted = RootItems.OrderByDescending(c => c.IsFolder)
                                  .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
                                  .ToList();

            for (int i = 0; i < sorted.Count; i++)
            {
                int oldIndex = RootItems.IndexOf(sorted[i]);
                if (oldIndex != i)
                {
                    RootItems.Move(oldIndex, i);
                }
            }
        }

        [RelayCommand]
        public void DeleteSelectedItem()
        {
            if (SelectedItem == null) return;

            string targetName = SelectedItem.Name;
            bool isFolder = SelectedItem.IsFolder;
            string msg = LocalizationService.Instance.GetDeleteConfirmMessage(targetName, isFolder);
            string title = LocalizationService.Instance.GetDeleteConfirmTitle();

            var result = MessageBox.Show(msg, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                string targetPath = SelectedItem.FullPath;

                _storageService.DeleteItem(targetPath, isFolder);

                // 삭제된 파일이 열려 있던 탭들 닫기
                var tabsToRemove = Tabs.Where(t => isFolder 
                    ? t.FilePath.StartsWith(targetPath, StringComparison.OrdinalIgnoreCase) 
                    : string.Equals(t.FilePath, targetPath, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var t in tabsToRemove)
                {
                    CloseTab(t);
                }

                RefreshTreeAndSelect(null);
            }
            catch (Exception ex)
            {
                MessageBox.Show(LocalizationService.Instance.GetDeleteFailedMessage(ex.Message), 
                                LocalizationService.Instance.GetErrorTitle(), 
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public bool MoveItem(TreeItemViewModel sourceItem, TreeItemViewModel? targetItem)
        {
            if (sourceItem == null) return false;

            // 이동할 대상 디렉토리 결정
            string targetDirPath;
            if (targetItem == null)
            {
                targetDirPath = _storageService.RootDirectory;
            }
            else if (targetItem.IsFolder)
            {
                targetDirPath = targetItem.FullPath;
            }
            else
            {
                // 타겟이 파일이면 해당 파일이 속한 부모 디렉토리로 이동
                targetDirPath = Path.GetDirectoryName(targetItem.FullPath) ?? _storageService.RootDirectory;
            }

            // 이미 같은 폴더에 있는 경우
            string currentParentDir = Path.GetDirectoryName(sourceItem.FullPath) ?? _storageService.RootDirectory;
            if (string.Equals(Path.GetFullPath(currentParentDir).TrimEnd(Path.DirectorySeparatorChar),
                              Path.GetFullPath(targetDirPath).TrimEnd(Path.DirectorySeparatorChar),
                              StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                string oldFullPath = sourceItem.FullPath;
                bool isFolder = sourceItem.IsFolder;

                string newFullPath = _storageService.MoveItem(oldFullPath, targetDirPath, isFolder);

                // 열려 있는 탭 중 해당 파일(또는 폴더 하위 파일)들의 경로 정보 갱신
                foreach (var tab in Tabs)
                {
                    if (isFolder)
                    {
                        if (tab.FilePath.StartsWith(oldFullPath, StringComparison.OrdinalIgnoreCase))
                        {
                            string subRel = tab.FilePath.Substring(oldFullPath.Length);
                            tab.FilePath = newFullPath + subRel;
                            tab.RelativePath = Path.GetRelativePath(_storageService.RootDirectory, tab.FilePath);
                        }
                    }
                    else
                    {
                        if (string.Equals(tab.FilePath, oldFullPath, StringComparison.OrdinalIgnoreCase))
                        {
                            tab.FilePath = newFullPath;
                            tab.RelativePath = Path.GetRelativePath(_storageService.RootDirectory, newFullPath);
                            tab.Title = Path.GetFileNameWithoutExtension(newFullPath);
                        }
                    }
                }

                // 타겟 폴더 자동 펼치기
                if (targetItem != null && targetItem.IsFolder)
                {
                    targetItem.IsExpanded = true;
                }

                RefreshTreeAndSelect(newFullPath);
                _sessionService.SaveSession(_sessionConfig);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(LocalizationService.Instance.GetMoveFailedMessage(ex.Message), 
                                LocalizationService.Instance.GetMoveFailedTitle(), 
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        public void RefreshTreeAndSelect(string? targetPath)
        {
            var expanded = CollectExpandedPaths(RootItems);
            var loaded = _storageService.LoadTree(expanded);

            RootItems.Clear();
            foreach (var item in loaded)
            {
                RootItems.Add(item);
            }

            if (!string.IsNullOrEmpty(targetPath))
            {
                string rel = Path.GetRelativePath(_storageService.RootDirectory, targetPath);
                var found = FindItemByRelativePath(RootItems, rel);
                if (found != null)
                {
                    found.IsSelected = true;
                    SelectedItem = found;
                    if (!found.IsFolder)
                    {
                        OpenNoteInTab(found);
                    }
                    return;
                }
            }

            if (ActiveTab != null)
            {
                var found = FindItemByRelativePath(RootItems, ActiveTab.RelativePath);
                if (found != null)
                {
                    found.IsSelected = true;
                    SelectedItem = found;
                }
            }
        }

        private HashSet<string> CollectExpandedPaths(IEnumerable<TreeItemViewModel> items)
        {
            var set = new HashSet<string>();
            foreach (var item in items)
            {
                if (item.IsFolder)
                {
                    if (item.IsExpanded) set.Add(item.RelativePath);
                    var childSet = CollectExpandedPaths(item.Children);
                    foreach (var s in childSet) set.Add(s);
                }
            }
            return set;
        }

        [RelayCommand]
        public void ToggleSidebar()
        {
            IsSidebarVisible = !IsSidebarVisible;
            _sessionConfig.IsSidebarVisible = IsSidebarVisible;
        }

        [RelayCommand]
        public void ToggleWordWrap()
        {
            WordWrap = !WordWrap;
            _sessionConfig.WordWrap = WordWrap;
            UpdateWordWrapTexts();
        }

        private void UpdateWordWrapTexts()
        {
            WordWrapStatusText = LocalizationService.Instance.GetString("Status_WordWrap");
            WordWrapTooltip = LocalizationService.Instance.GetWordWrapTooltip(WordWrap);
        }

        [RelayCommand]
        public void ChangeLanguage(string lang)
        {
            _sessionConfig.Language = lang;
            LocalizationService.Instance.SetLanguage(lang);
            UpdateLanguageState();
            UpdateWordWrapTexts();
            SaveStatus = LocalizationService.Instance.GetSaveStatusReady();
            _sessionService.SaveSession(_sessionConfig);
        }

        private void UpdateLanguageState()
        {
            CurrentLanguageSetting = _sessionConfig.Language ?? "Auto";
            IsLangAuto = string.Equals(CurrentLanguageSetting, "Auto", StringComparison.OrdinalIgnoreCase);
            IsLangKo = string.Equals(CurrentLanguageSetting, "ko", StringComparison.OrdinalIgnoreCase);
            IsLangEn = string.Equals(CurrentLanguageSetting, "en", StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyConfiguredTheme(string themeStr)
        {
            bool isDark = false;
            if (string.Equals(themeStr, "Dark", StringComparison.OrdinalIgnoreCase))
            {
                isDark = true;
            }
            else if (string.Equals(themeStr, "Light", StringComparison.OrdinalIgnoreCase))
            {
                isDark = false;
            }
            else
            {
                // 시스템 기본값 감지
                isDark = Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme() == Wpf.Ui.Appearance.ApplicationTheme.Dark;
            }

            SetThemeState(isDark);
        }

        [RelayCommand]
        public void ToggleTheme()
        {
            SetThemeState(!IsDarkMode);
            _sessionConfig.Theme = IsDarkMode ? "Dark" : "Light";
        }

        private void SetThemeState(bool isDark)
        {
            IsDarkMode = isDark;
            ThemeIcon = isDark ? "WeatherSunny20" : "WeatherMoon20";
            ThemeTooltip = isDark ? "테마 전환: 라이트 모드로 변경" : "테마 전환: 다크 모드로 변경";

            try
            {
                Wpf.Ui.Appearance.ApplicationTheme targetTheme = isDark 
                    ? Wpf.Ui.Appearance.ApplicationTheme.Dark 
                    : Wpf.Ui.Appearance.ApplicationTheme.Light;
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(targetTheme);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Theme apply error: {ex.Message}");
            }
        }

        partial void OnFontSizeChanged(double value)
        {
            string formatted = $"{(int)Math.Round(value)}pt";
            if (!string.Equals(_selectedFontSize, formatted, StringComparison.OrdinalIgnoreCase))
            {
                _selectedFontSize = formatted;
                OnPropertyChanged(nameof(SelectedFontSize));
            }
        }

        partial void OnSelectedFontSizeChanged(string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            string numStr = value.Replace("pt", "").Trim();
            if (double.TryParse(numStr, out double sz) && Math.Abs(sz - FontSize) > 0.1)
            {
                FontSize = sz;
                _sessionConfig.FontSize = sz;
            }
        }

        [RelayCommand]
        public void ZoomIn()
        {
            if (FontSize < 40) FontSize += 2;
            _sessionConfig.FontSize = FontSize;
        }

        [RelayCommand]
        public void ZoomOut()
        {
            if (FontSize > 10) FontSize -= 2;
            _sessionConfig.FontSize = FontSize;
        }

        [RelayCommand]
        public void ResetZoom()
        {
            FontSize = 15.0;
            _sessionConfig.FontSize = FontSize;
        }

        [RelayCommand]
        public void OpenInExplorer()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _storageService.RootDirectory,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"탐색기 열기 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        [RelayCommand]
        public void ChangeStorageLocation()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "TreePad 메모 저장 폴더 선택 (Google Drive, OneDrive 동기화 폴더 등)",
                InitialDirectory = _storageService.RootDirectory,
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                string selectedPath = dialog.FolderName;
                if (!string.IsNullOrWhiteSpace(selectedPath) && !string.Equals(selectedPath, _storageService.RootDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    // 1. 현재 타이머 정지 및 열려 있는 모든 탭의 텍스트 즉시 디스크에 무손실 저장
                    _autoSaveTimer.Stop();
                    foreach (var tab in Tabs)
                    {
                        try
                        {
                            if (File.Exists(tab.FilePath))
                            {
                                File.WriteAllText(tab.FilePath, tab.Content, System.Text.Encoding.UTF8);
                            }
                        }
                        catch { }
                    }

                    // 2. 현재 열린 탭들과 세션 상태를 저장 객체에 동기화
                    _sessionConfig.OpenTabs = Tabs.Select(t => t.RelativePath).ToList();
                    if (ActiveTab != null)
                    {
                        _sessionConfig.LastSelectedFilePath = ActiveTab.RelativePath;
                    }
                    _sessionConfig.ExpandedFolders = CollectExpandedPaths(RootItems).ToList();

                    // 3. 기존 저장소에 작성된 메모/폴더가 있는지 확인 후 안전 이전(복사)
                    string oldPath = _storageService.RootDirectory;
                    bool hasExistingData = Directory.Exists(oldPath) && 
                        (Directory.EnumerateFiles(oldPath).Any() || Directory.EnumerateDirectories(oldPath).Any());

                    if (hasExistingData)
                    {
                        var confirm = MessageBox.Show(
                            $"기존 저장 위치({oldPath})에 있는 메모와 폴더들을 새 위치로 함께 복사하시겠습니까?\n\n" +
                            "• [예] : 현재 트리에 저장된 모든 메모와 폴더를 새 위치로 손실 없이 안전하게 복사하여 이전합니다. (권장)\n" +
                            "• [아니오] : 기존 메모는 원본 위치에 보존하고, 새 위치를 빈 작업공간으로 연결합니다.\n" +
                            "• [취소] : 저장 위치 변경을 취소합니다.",
                            "메모 데이터 이전 안내",
                            MessageBoxButton.YesNoCancel,
                            MessageBoxImage.Question);

                        if (confirm == MessageBoxResult.Cancel)
                        {
                            return;
                        }

                        if (confirm == MessageBoxResult.Yes)
                        {
                            try
                            {
                                CopyDirectoryRecursively(oldPath, selectedPath);
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show($"메모 복사 중 일부 항목에서 오류가 발생했습니다: {ex.Message}", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                            }
                        }
                    }

                    // 4. 새 저장소 경로 반영 및 세션 저장
                    _storageService.SetRootDirectory(selectedPath);
                    _sessionConfig.StorageDirectory = selectedPath;
                    _sessionService.SaveSession(_sessionConfig);
                    StorageDirectoryPath = selectedPath;

                    // 5. 새 경로 기준으로 트리 및 열려 있던 탭들 손실 없이 재복원
                    Tabs.Clear();
                    LoadTreeAndRestoreSession();
                    SaveStatus = "저장 위치 변경 완료";
                }
            }
        }

        private static void CopyDirectoryRecursively(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(destinationDir, Path.GetFileName(file));
                File.Copy(file, destFile, overwrite: true);
            }

            foreach (string dir in Directory.GetDirectories(sourceDir))
            {
                string destDir = Path.Combine(destinationDir, Path.GetFileName(dir));
                CopyDirectoryRecursively(dir, destDir);
            }
        }

        public void SaveSessionBeforeClose(double left, double top, double width, double height, bool isMaximized, int caretIndex)
        {
            if (_autoSaveTimer.IsEnabled)
            {
                _autoSaveTimer.Stop();
                CommitAutoSaveAsync().GetAwaiter().GetResult();
            }

            _sessionConfig.WindowLeft = left;
            _sessionConfig.WindowTop = top;
            _sessionConfig.WindowWidth = width;
            _sessionConfig.WindowHeight = height;
            _sessionConfig.IsMaximized = isMaximized;
            _sessionConfig.SidebarWidth = SidebarWidth;
            _sessionConfig.IsSidebarVisible = IsSidebarVisible;
            _sessionConfig.CaretIndex = caretIndex;
            _sessionConfig.FontSize = FontSize;
            _sessionConfig.WordWrap = WordWrap;
            _sessionConfig.Theme = IsDarkMode ? "Dark" : "Light";
            _sessionConfig.ExpandedFolders = CollectExpandedPaths(RootItems).ToList();
            _sessionConfig.OpenTabs = Tabs.Select(t => t.RelativePath).ToList();

            if (ActiveTab != null)
            {
                _sessionConfig.LastSelectedFilePath = ActiveTab.RelativePath;
            }

            _sessionService.SaveSession(_sessionConfig);
        }
    }
}
