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
        private static readonly double[] FontSizeSteps = { 10, 11, 12, 13, 14, 15, 16, 18, 20, 22, 24, 28, 32, 36, 40 };

        private readonly StorageService _storageService;
        private readonly SessionService _sessionService;
        private readonly SessionConfig _sessionConfig;
        private readonly DispatcherTimer _autoSaveTimer;
        private readonly DispatcherTimer _externalSyncTimer;
        private FileSystemWatcher? _fileWatcher;

        private bool _isSwitchingTab = false;
        private bool _isUpdatingExternalContent = false;
        private bool _suppressAutoOpenOnSelect = false;
        private SaveStateKind _currentSaveState = SaveStateKind.Ready;
        private string? _lastSaveErrorDetail = null;

        public event Action<NoteTabViewModel?>? ActiveTabChanging;
        public event Action<NoteTabViewModel?>? ActiveTabChanged;
        public event Action? ExternalContentReloaded;
        public Func<string, bool, bool>? ConfirmDeleteCallback { get; set; }

        public ObservableCollection<TreeItemViewModel> RootItems { get; } = new();
        public ObservableCollection<NoteTabViewModel> Tabs { get; } = new();
        public List<string> AvailableFontSizes { get; } = FontSizeSteps.Select(s => $"{(int)s}pt").ToList();

        public bool IsSwitchingOrExternalUpdate => _isSwitchingTab || _isUpdatingExternalContent;

        [ObservableProperty]
        private string _selectedFontSize = "15pt";

        [ObservableProperty]
        private TreeItemViewModel? _selectedItem;

        [ObservableProperty]
        private NoteTabViewModel? _activeTab;

        [ObservableProperty]
        private bool _hasOpenTabs = false;

        [ObservableProperty]
        private string _saveStatus = string.Empty;

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
        private string _charCountText = string.Empty;

        [ObservableProperty]
        private string _charCountNoSpacesText = string.Empty;

        [ObservableProperty]
        private string _lineCountText = string.Empty;

        [ObservableProperty]
        private string _cursorPositionText = string.Empty;

        [ObservableProperty]
        private double _fontSize = 15.0;

        [ObservableProperty]
        private bool _wordWrap = true;

        [ObservableProperty]
        private string _wordWrapStatusText = string.Empty;

        [ObservableProperty]
        private string _wordWrapTooltip = string.Empty;

        [ObservableProperty]
        private bool _isDarkMode = false;

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

            double initialSize = SnapToNearestFontSize(_sessionConfig.FontSize > 0 ? _sessionConfig.FontSize : 15.0);
            _fontSize = initialSize;
            _selectedFontSize = $"{(int)Math.Round(initialSize)}pt";
            _wordWrap = _sessionConfig.WordWrap;
            UpdateWordWrapTexts();

            _sidebarWidth = _sessionConfig.SidebarWidth >= 180 ? _sessionConfig.SidebarWidth : 280;
            _isSidebarVisible = _sessionConfig.IsSidebarVisible;

            SetSaveState(SaveStateKind.Ready);
            UpdateFormattedStatusBarTexts();

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

            // 외부(클라우드/탐색기) 파일 변경 감지 디바운스 타이머 (500ms)
            _externalSyncTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _externalSyncTimer.Tick += (s, e) =>
            {
                _externalSyncTimer.Stop();
                HandleExternalFileSystemChanges();
            };

            LoadTreeAndRestoreSession();
            InitializeFileWatcher(_storageService.RootDirectory);
        }

        public SessionConfig GetSessionConfig() => _sessionConfig;

        private static double SnapToNearestFontSize(double requested)
        {
            double best = 15.0;
            double minDiff = double.MaxValue;
            foreach (double step in FontSizeSteps)
            {
                double diff = Math.Abs(step - requested);
                if (diff < minDiff)
                {
                    minDiff = diff;
                    best = step;
                }
            }
            return best;
        }

        private void SetSaveState(SaveStateKind state, string? errorDetail = null)
        {
            _currentSaveState = state;
            _lastSaveErrorDetail = errorDetail;
            SaveStatus = LocalizationService.Instance.GetSaveStatusText(state, errorDetail);
        }

        private void UpdateFormattedStatusBarTexts()
        {
            var loc = LocalizationService.Instance;
            CharCountText = loc.GetCharCountPrimary(CharacterCount);
            CharCountNoSpacesText = loc.GetCharCountNoSpaces(CharacterCountNoSpaces);
            LineCountText = loc.GetLineCountText(LineCount);
            CursorPositionText = loc.GetCursorPosText(CurrentLine, CurrentColumn);
        }

        // ==========================================
        // 🌟 실시간 외부 변경 감지 (FileSystemWatcher) 🌟
        // ==========================================

        private void InitializeFileWatcher(string directoryPath)
        {
            DisposeFileWatcher();

            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
                return;

            try
            {
                _fileWatcher = new FileSystemWatcher(directoryPath)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
                };

                _fileWatcher.Created += OnExternalFileEvent;
                _fileWatcher.Deleted += OnExternalFileEvent;
                _fileWatcher.Changed += OnExternalFileEvent;
                _fileWatcher.Renamed += OnExternalFileRenamed;
                _fileWatcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FileSystemWatcher init failed: {ex.Message}");
            }
        }

        private void DisposeFileWatcher()
        {
            if (_fileWatcher != null)
            {
                try
                {
                    _fileWatcher.EnableRaisingEvents = false;
                    _fileWatcher.Created -= OnExternalFileEvent;
                    _fileWatcher.Deleted -= OnExternalFileEvent;
                    _fileWatcher.Changed -= OnExternalFileEvent;
                    _fileWatcher.Renamed -= OnExternalFileRenamed;
                    _fileWatcher.Dispose();
                }
                catch { }
                _fileWatcher = null;
            }
        }

        private static bool IsTemporaryOrHiddenPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return true;
            string name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(name)) return false;
            return name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
                || name.Contains(".renametmp_", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith('.')
                || name.StartsWith("~$", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "desktop.ini", StringComparison.OrdinalIgnoreCase);
        }

        private void OnExternalFileEvent(object sender, FileSystemEventArgs e)
        {
            if (_storageService.IsWatcherSuppressed) return;
            if (IsTemporaryOrHiddenPath(e.FullPath)) return;

            ScheduleExternalSync();
        }

        private void OnExternalFileRenamed(object sender, RenamedEventArgs e)
        {
            if (_storageService.IsWatcherSuppressed) return;
            if (IsTemporaryOrHiddenPath(e.FullPath) && IsTemporaryOrHiddenPath(e.OldFullPath)) return;

            ScheduleExternalSync();
        }

        private void ScheduleExternalSync()
        {
            Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (_storageService.IsWatcherSuppressed) return;
                _externalSyncTimer.Stop();
                _externalSyncTimer.Start();
            }));
        }

        private bool IsAnyItemEditing(IEnumerable<TreeItemViewModel> items)
        {
            foreach (var item in items)
            {
                if (item.IsEditing) return true;
                if (item.IsFolder && IsAnyItemEditing(item.Children)) return true;
            }
            return false;
        }

        private void HandleExternalFileSystemChanges()
        {
            if (_storageService.IsWatcherSuppressed) return;

            // 사용자가 F2 인라인 이름 편집 중이면 완료될 때까지 대기
            if (IsAnyItemEditing(RootItems))
            {
                _externalSyncTimer.Start();
                return;
            }

            // 1. 트리 구조 무간섭 리로드 (포커스를 뺏지 않고 펼침/선택/검색필터 유지)
            string? prevSelectedRel = SelectedItem?.RelativePath;
            var expanded = CollectExpandedPaths(RootItems);
            var loaded = _storageService.LoadTree(expanded);

            RootItems.Clear();
            foreach (var item in loaded)
            {
                RootItems.Add(item);
            }

            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                FilterTree(RootItems, SearchQuery.Trim().ToLowerInvariant());
            }

            if (!string.IsNullOrEmpty(prevSelectedRel))
            {
                var restoredSel = FindItemByRelativePath(RootItems, prevSelectedRel);
                if (restoredSel != null)
                {
                    restoredSel.IsSelected = true;
                    _suppressAutoOpenOnSelect = true;
                    try
                    {
                        SelectedItem = restoredSel;
                    }
                    finally
                    {
                        _suppressAutoOpenOnSelect = false;
                    }
                }
            }

            // 2. 열려 있는 탭 중 외부에서 수정되거나 삭제된 파일 반영
            bool anyTabUpdated = false;
            var openTabsSnapshot = Tabs.ToList();
            foreach (var tab in openTabsSnapshot)
            {
                if (tab.IsDirty) continue; // 사용자가 현재 수정 중인 미저장 탭은 덮어쓰지 않음

                if (!File.Exists(tab.FilePath))
                {
                    CloseTabInternal(tab, saveBeforeClose: false);
                    continue;
                }

                try
                {
                    string diskContent = _storageService.ReadNoteContentSync(tab.FilePath);
                    if (!string.Equals(diskContent, tab.Content, StringComparison.Ordinal))
                    {
                        _isUpdatingExternalContent = true;
                        try
                        {
                            tab.Content = diskContent;
                            if (tab == ActiveTab)
                            {
                                UpdateTextStatistics(diskContent);
                                anyTabUpdated = true;
                            }
                        }
                        finally
                        {
                            _isUpdatingExternalContent = false;
                        }
                    }
                }
                catch { }
            }

            if (anyTabUpdated)
            {
                SetSaveState(SaveStateKind.ExternalSynced);
                ExternalContentReloaded?.Invoke();
            }
        }

        [RelayCommand]
        public void RefreshTree()
        {
            FlushDirtyTabsSync();
            HandleExternalFileSystemChanges();
            SetSaveState(SaveStateKind.Refreshed);
        }

        // ==========================================
        // 🌟 세션 복원 및 탭 관리 🌟
        // ==========================================

        private void LoadTreeAndRestoreSession()
        {
            RootItems.Clear();
            var expandedPaths = new HashSet<string>(_sessionConfig.ExpandedFolders ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var loaded = _storageService.LoadTree(expandedPaths);

            foreach (var item in loaded)
            {
                RootItems.Add(item);
            }

            Tabs.Clear();
            var tabsToRestore = _sessionConfig.OpenTabs != null && _sessionConfig.OpenTabs.Count > 0
                ? _sessionConfig.OpenTabs
                : new List<string>();

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
                // 마지막 활성 탭 커서 복원 보정
                if (targetTab.CaretIndex == 0 && _sessionConfig.CaretIndex > 0)
                {
                    targetTab.CaretIndex = _sessionConfig.CaretIndex;
                }
                ActiveTab = targetTab;
            }
            else
            {
                var firstNote = FindFirstNote(RootItems);
                if (firstNote != null)
                {
                    OpenNoteInTab(firstNote);
                }
            }

            HasOpenTabs = Tabs.Count > 0;
            if (!HasOpenTabs)
            {
                SetSaveState(SaveStateKind.NoOpenNote);
            }
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
            if (!_suppressAutoOpenOnSelect && value != null && !value.IsFolder)
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

        partial void OnActiveTabChanging(NoteTabViewModel? value)
        {
            // 탭이 바뀌기 직전: 현재 에디터 커서/스크롤 저장 이벤트 발생 및 미저장 텍스트 즉시 플러시
            ActiveTabChanging?.Invoke(ActiveTab);

            if (ActiveTab != null && ActiveTab.IsDirty)
            {
                _autoSaveTimer.Stop();
                FlushDirtyTabsSync();
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
                    SetSaveState(SaveStateKind.Saved);
                }
                else
                {
                    UpdateTextStatistics(string.Empty);
                    UpdateCursorPosition(1, 1);
                    SetSaveState(SaveStateKind.NoOpenNote);
                }
                HasOpenTabs = (value != null);
            }
            finally
            {
                _isSwitchingTab = false;
            }

            ActiveTabChanged?.Invoke(value);
        }

        public void NotifyContentChanged()
        {
            if (_isSwitchingTab || _isUpdatingExternalContent || ActiveTab == null) return;

            ActiveTab.IsDirty = true;
            UpdateTextStatistics(ActiveTab.Content);
            SetSaveState(SaveStateKind.Typing);
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
                UpdateFormattedStatusBarTexts();
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
            UpdateFormattedStatusBarTexts();
        }

        public void OpenNoteInTab(TreeItemViewModel note)
        {
            if (note.IsFolder) return;

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
                    content = _storageService.ReadNoteContentSync(note.FullPath);
                }
            }
            catch (Exception ex)
            {
                content = LocalizationService.Instance.GetFileReadFailed(ex.Message);
            }

            var tab = new NoteTabViewModel(note.FullPath, note.RelativePath, note.Name, content, this);
            if (_sessionConfig.TabCaretPositions != null &&
                _sessionConfig.TabCaretPositions.TryGetValue(note.RelativePath, out int savedCaret))
            {
                tab.CaretIndex = Math.Clamp(savedCaret, 0, content.Length);
            }

            Tabs.Add(tab);
            HasOpenTabs = true;

            if (makeActive)
            {
                ActiveTab = tab;
            }
        }

        public void CloseTab(NoteTabViewModel tab)
        {
            CloseTabInternal(tab, saveBeforeClose: true);
        }

        private void CloseTabInternal(NoteTabViewModel tab, bool saveBeforeClose)
        {
            _autoSaveTimer.Stop();
            if (saveBeforeClose && tab.IsDirty && File.Exists(tab.FilePath))
            {
                try
                {
                    _storageService.SaveNoteContentSync(tab.FilePath, tab.Content);
                    tab.IsDirty = false;
                }
                catch { }
            }

            int index = Tabs.IndexOf(tab);
            if (index < 0) return;

            bool wasActive = (ActiveTab == tab);
            Tabs.Remove(tab);

            if (wasActive)
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

        private void FlushDirtyTabsSync()
        {
            foreach (var tab in Tabs)
            {
                if (!tab.IsDirty) continue;
                try
                {
                    _storageService.SaveNoteContentSync(tab.FilePath, tab.Content);
                    tab.IsDirty = false;
                }
                catch (Exception ex)
                {
                    SetSaveState(SaveStateKind.Error, ex.Message);
                }
            }
        }

        private async Task CommitAutoSaveAsync()
        {
            var dirtyTabs = Tabs.Where(t => t.IsDirty).ToList();
            if (dirtyTabs.Count == 0) return;

            SetSaveState(SaveStateKind.Saving);
            try
            {
                foreach (var tab in dirtyTabs)
                {
                    string snapshot = tab.Content;
                    tab.IsDirty = false;
                    await _storageService.SaveNoteContentAsync(tab.FilePath, snapshot);
                    // 저장 도중 추가 입력이 발생했으면 다시 Dirty 상태 유지
                    if (!string.Equals(tab.Content, snapshot, StringComparison.Ordinal))
                    {
                        tab.IsDirty = true;
                    }
                }

                if (Tabs.Any(t => t.IsDirty))
                {
                    _autoSaveTimer.Start();
                }
                else
                {
                    SetSaveState(SaveStateKind.Saved);
                }
            }
            catch (Exception ex)
            {
                SetSaveState(SaveStateKind.Error, ex.Message);
            }
        }

        public void UpdateCursorPosition(int line, int column)
        {
            CurrentLine = line;
            CurrentColumn = column;
            CursorPositionText = LocalizationService.Instance.GetCursorPosText(line, column);
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

        // ==========================================
        // 🌟 메모 / 폴더 생성, 이름 변경, 삭제, 이동 🌟
        // ==========================================

        [RelayCommand]
        public void CreateNewNote()
        {
            string parentDir = GetSelectedDirectory();
            try
            {
                string newFilePath = _storageService.CreateNote(parentDir);
                RefreshTreeAndSelect(newFilePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(LocalizationService.Instance.GetCreateNoteFailed(ex.Message),
                                LocalizationService.Instance.GetErrorTitle(),
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public void CreateNewFolder()
        {
            string parentDir = GetSelectedDirectory();
            try
            {
                string newDirPath = _storageService.CreateFolder(parentDir);
                RefreshTreeAndSelect(newDirPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(LocalizationService.Instance.GetCreateFolderFailed(ex.Message),
                                LocalizationService.Instance.GetErrorTitle(),
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public void CreateRootNote()
        {
            try
            {
                string newFilePath = _storageService.CreateNote(_storageService.RootDirectory);
                RefreshTreeAndSelect(newFilePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(LocalizationService.Instance.GetCreateNoteFailed(ex.Message),
                                LocalizationService.Instance.GetErrorTitle(),
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public void CreateRootFolder()
        {
            try
            {
                string newDirPath = _storageService.CreateFolder(_storageService.RootDirectory);
                RefreshTreeAndSelect(newDirPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(LocalizationService.Instance.GetCreateFolderFailed(ex.Message),
                                LocalizationService.Instance.GetErrorTitle(),
                                MessageBoxButton.OK, MessageBoxImage.Error);
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
            string trimmed = newName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmed) || string.Equals(item.Name, trimmed, StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                FlushDirtyTabsSync();

                string oldPath = item.FullPath;
                string newPath = _storageService.RenameItem(item.FullPath, trimmed, item.IsFolder);
                item.Name = item.IsFolder ? Path.GetFileName(newPath) : StorageService.GetNoteDisplayName(newPath);
                item.FullPath = newPath;
                item.RelativePath = Path.GetRelativePath(_storageService.RootDirectory, newPath);

                // 열려 있는 탭 경로 갱신 (폴더 이름 변경 시 하위 파일 탭들까지 모두 갱신)
                if (item.IsFolder)
                {
                    string oldPrefix = oldPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    foreach (var tab in Tabs)
                    {
                        if (tab.FilePath.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            string subRel = tab.FilePath.Substring(oldPath.Length);
                            tab.FilePath = newPath + subRel;
                            tab.RelativePath = Path.GetRelativePath(_storageService.RootDirectory, tab.FilePath);
                        }
                    }
                    RefreshTreeAndSelect(newPath);
                }
                else
                {
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

                UpdateCurrentLocationDescription();
            }
            catch (Exception ex)
            {
                MessageBox.Show(LocalizationService.Instance.GetRenameFailed(ex.Message),
                                LocalizationService.Instance.GetErrorTitle(),
                                MessageBoxButton.OK, MessageBoxImage.Warning);
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
            if (SelectedItem == null || SelectedItem.IsEditing) return;

            string targetName = SelectedItem.Name;
            bool isFolder = SelectedItem.IsFolder;

            bool isConfirmed = false;
            if (ConfirmDeleteCallback != null)
            {
                isConfirmed = ConfirmDeleteCallback(targetName, isFolder);
            }
            else
            {
                string msg = LocalizationService.Instance.GetDeleteConfirmMessage(targetName, isFolder);
                string title = LocalizationService.Instance.GetDeleteConfirmTitle();
                var result = MessageBox.Show(msg, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
                isConfirmed = (result == MessageBoxResult.Yes);
            }

            if (!isConfirmed) return;

            try
            {
                string targetPath = SelectedItem.FullPath;
                string folderPrefix = targetPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

                var tabsToRemove = Tabs.Where(t => isFolder
                    ? t.FilePath.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase)
                    : string.Equals(t.FilePath, targetPath, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // 휴지통 이동 전에 미저장 내용을 먼저 기록해 두면 휴지통 복원 시에도 마지막 글자까지 살아있음
                foreach (var t in tabsToRemove)
                {
                    if (t.IsDirty)
                    {
                        try { _storageService.SaveNoteContentSync(t.FilePath, t.Content); t.IsDirty = false; } catch { }
                    }
                }

                _storageService.DeleteItem(targetPath, isFolder);

                foreach (var t in tabsToRemove)
                {
                    CloseTabInternal(t, saveBeforeClose: false);
                }

                SelectedItem = null;
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
                targetDirPath = Path.GetDirectoryName(targetItem.FullPath) ?? _storageService.RootDirectory;
            }

            string currentParentDir = Path.GetDirectoryName(sourceItem.FullPath) ?? _storageService.RootDirectory;
            if (string.Equals(Path.GetFullPath(currentParentDir).TrimEnd(Path.DirectorySeparatorChar),
                              Path.GetFullPath(targetDirPath).TrimEnd(Path.DirectorySeparatorChar),
                              StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                FlushDirtyTabsSync();

                string oldFullPath = sourceItem.FullPath;
                bool isFolder = sourceItem.IsFolder;

                string newFullPath = _storageService.MoveItem(oldFullPath, targetDirPath, isFolder);

                string oldPrefix = oldFullPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                foreach (var tab in Tabs)
                {
                    if (isFolder)
                    {
                        if (tab.FilePath.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
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
                            tab.Title = StorageService.GetNoteDisplayName(newFullPath);
                        }
                    }
                }

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
            if (!string.IsNullOrEmpty(targetPath))
            {
                // 새로 생성/이동된 항목의 상위 폴더들은 자동으로 펼쳐 보이게 처리
                string? parentDir = Path.GetDirectoryName(targetPath);
                while (!string.IsNullOrEmpty(parentDir) &&
                       parentDir.StartsWith(_storageService.RootDirectory, StringComparison.OrdinalIgnoreCase) &&
                       !string.Equals(parentDir.TrimEnd(Path.DirectorySeparatorChar),
                                      _storageService.RootDirectory.TrimEnd(Path.DirectorySeparatorChar),
                                      StringComparison.OrdinalIgnoreCase))
                {
                    expanded.Add(Path.GetRelativePath(_storageService.RootDirectory, parentDir));
                    parentDir = Path.GetDirectoryName(parentDir);
                }
            }

            var loaded = _storageService.LoadTree(expanded);

            RootItems.Clear();
            foreach (var item in loaded)
            {
                RootItems.Add(item);
            }

            if (!string.IsNullOrWhiteSpace(SearchQuery))
            {
                FilterTree(RootItems, SearchQuery.Trim().ToLowerInvariant());
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
            else
            {
                UpdateCurrentLocationDescription();
            }
        }

        private HashSet<string> CollectExpandedPaths(IEnumerable<TreeItemViewModel> items)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

        // ==========================================
        // 🌟 보기 설정, 언어 전환, 확대/축소, 저장소 변경 🌟
        // ==========================================

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
            UpdateFormattedStatusBarTexts();
            SetSaveState(_currentSaveState, _lastSaveErrorDetail);
            _sessionService.SaveSession(_sessionConfig);
        }

        private void UpdateLanguageState()
        {
            CurrentLanguageSetting = _sessionConfig.Language ?? "Auto";
            IsLangAuto = string.Equals(CurrentLanguageSetting, "Auto", StringComparison.OrdinalIgnoreCase);
            IsLangKo = string.Equals(CurrentLanguageSetting, "ko", StringComparison.OrdinalIgnoreCase);
            IsLangEn = string.Equals(CurrentLanguageSetting, "en", StringComparison.OrdinalIgnoreCase);
        }

        partial void OnFontSizeChanged(double value)
        {
            string formatted = $"{(int)Math.Round(value)}pt";
            if (!string.Equals(SelectedFontSize, formatted, StringComparison.OrdinalIgnoreCase))
            {
                SelectedFontSize = formatted;
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
            foreach (double step in FontSizeSteps)
            {
                if (step > FontSize + 0.1)
                {
                    FontSize = step;
                    _sessionConfig.FontSize = FontSize;
                    return;
                }
            }
        }

        [RelayCommand]
        public void ZoomOut()
        {
            for (int i = FontSizeSteps.Length - 1; i >= 0; i--)
            {
                if (FontSizeSteps[i] < FontSize - 0.1)
                {
                    FontSize = FontSizeSteps[i];
                    _sessionConfig.FontSize = FontSize;
                    return;
                }
            }
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
                MessageBox.Show(LocalizationService.Instance.GetExplorerFailed(ex.Message),
                                LocalizationService.Instance.GetErrorTitle(),
                                MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        [RelayCommand]
        public void ChangeStorageLocation()
        {
            var loc = LocalizationService.Instance;
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = loc.GetChangeStorageDialogTitle(),
                InitialDirectory = _storageService.RootDirectory,
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                string selectedPath = dialog.FolderName;
                if (!string.IsNullOrWhiteSpace(selectedPath) &&
                    !string.Equals(Path.GetFullPath(selectedPath).TrimEnd(Path.DirectorySeparatorChar),
                                   Path.GetFullPath(_storageService.RootDirectory).TrimEnd(Path.DirectorySeparatorChar),
                                   StringComparison.OrdinalIgnoreCase))
                {
                    // 1. 열려 있는 모든 탭 즉시 무손실 저장
                    _autoSaveTimer.Stop();
                    FlushDirtyTabsSync();

                    // 2. 세션 상태 동기화
                    _sessionConfig.OpenTabs = Tabs.Select(t => t.RelativePath).ToList();
                    if (ActiveTab != null)
                    {
                        _sessionConfig.LastSelectedFilePath = ActiveTab.RelativePath;
                    }
                    _sessionConfig.ExpandedFolders = CollectExpandedPaths(RootItems).ToList();

                    // 3. 기존 데이터 안전 이전 여부 확인
                    string oldPath = _storageService.RootDirectory;
                    bool hasExistingData = Directory.Exists(oldPath) &&
                        (Directory.EnumerateFiles(oldPath).Any() || Directory.EnumerateDirectories(oldPath).Any());

                    if (hasExistingData)
                    {
                        var confirm = MessageBox.Show(
                            loc.GetMigrateStorageMessage(oldPath),
                            loc.GetMigrateStorageTitle(),
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
                                _storageService.SuppressWatcher(3000);
                                CopyDirectoryRecursively(oldPath, selectedPath);
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show(loc.GetMigrateCopyWarning(ex.Message),
                                                loc.GetNoticeTitle(),
                                                MessageBoxButton.OK, MessageBoxImage.Warning);
                            }
                        }
                    }

                    // 4. 새 저장소 반영 및 감시자 재연결
                    DisposeFileWatcher();
                    _storageService.SetRootDirectory(selectedPath);
                    _sessionConfig.StorageDirectory = selectedPath;
                    _sessionService.SaveSession(_sessionConfig);
                    StorageDirectoryPath = selectedPath;

                    Tabs.Clear();
                    LoadTreeAndRestoreSession();
                    InitializeFileWatcher(selectedPath);
                    SetSaveState(SaveStateKind.StorageChanged);
                }
            }
        }

        private static void CopyDirectoryRecursively(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);

            string normSrc = Path.GetFullPath(sourceDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string normDst = Path.GetFullPath(destinationDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (normDst.StartsWith(normSrc, StringComparison.OrdinalIgnoreCase))
            {
                // 상위 폴더를 자신의 하위 폴더로 복사할 때 무한 재귀 방지
                var filesToCopy = Directory.GetFiles(sourceDir);
                var dirsToCopy = Directory.GetDirectories(sourceDir)
                    .Where(d => !string.Equals(Path.GetFullPath(d).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                                               normDst, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (string file in filesToCopy)
                {
                    File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)), overwrite: true);
                }
                foreach (string dir in dirsToCopy)
                {
                    CopyDirectoryRecursively(dir, Path.Combine(destinationDir, Path.GetFileName(dir)));
                }
                return;
            }

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

        public void SaveSessionBeforeClose(double left, double top, double width, double height, bool isMaximized, int activeCaretIndex)
        {
            _autoSaveTimer.Stop();
            _externalSyncTimer.Stop();
            DisposeFileWatcher();

            // 데드락 없는 동기 플러시로 모든 미저장 탭 안전 저장
            FlushDirtyTabsSync();

            if (ActiveTab != null)
            {
                ActiveTab.CaretIndex = activeCaretIndex;
            }

            _sessionConfig.WindowLeft = left;
            _sessionConfig.WindowTop = top;
            _sessionConfig.WindowWidth = width;
            _sessionConfig.WindowHeight = height;
            _sessionConfig.IsMaximized = isMaximized;
            _sessionConfig.SidebarWidth = SidebarWidth;
            _sessionConfig.IsSidebarVisible = IsSidebarVisible;
            _sessionConfig.CaretIndex = activeCaretIndex;
            _sessionConfig.FontSize = FontSize;
            _sessionConfig.WordWrap = WordWrap;
            _sessionConfig.Theme = IsDarkMode ? "Dark" : "Light";
            _sessionConfig.ExpandedFolders = CollectExpandedPaths(RootItems).ToList();
            _sessionConfig.OpenTabs = Tabs.Select(t => t.RelativePath).ToList();

            var caretMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var tab in Tabs)
            {
                caretMap[tab.RelativePath] = tab.CaretIndex;
            }
            _sessionConfig.TabCaretPositions = caretMap;

            if (ActiveTab != null)
            {
                _sessionConfig.LastSelectedFilePath = ActiveTab.RelativePath;
            }
            else
            {
                _sessionConfig.LastSelectedFilePath = null;
            }

            _sessionService.SaveSession(_sessionConfig);
        }
    }
}
