using System;
using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TreePad.ViewModels;

namespace TreePad
{
    public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
    {
        private readonly MainViewModel _viewModel;
        private readonly DispatcherTimer _undoSnapshotTimer;
        private bool _isApplyingUndoRedo = false;
        private int _lastFindIndex = -1;
        private double _savedSidebarWidth = 280;
        private Point _dragStartPoint;
        private TreeItemViewModel? _draggedItem;
        private TreeItemViewModel? _currentDropTarget;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            DataContext = _viewModel;

            // 💡 Gruvbox Material Light 고정 테마가 Windows 11 다크 모드와 충돌하여 드롭다운/스크롤바가 깨지지 않도록 Light 테마로 명시적 고정
            try
            {
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Light);
            }
            catch { }

            _undoSnapshotTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(350)
            };
            _undoSnapshotTimer.Tick += (s, e) =>
            {
                _undoSnapshotTimer.Stop();
                CommitCurrentTabUndoCheckpoint();
            };

            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            _viewModel.ActiveTabChanging += OnViewModelActiveTabChanging;
            _viewModel.ActiveTabChanged += OnViewModelActiveTabChanged;
            _viewModel.ExternalContentReloaded += OnViewModelExternalContentReloaded;
            _viewModel.ConfirmDeleteCallback = ShowDeleteConfirmModal;

            // 창이 화면에 그려지기 전(SourceInitialized)에 위치/크기/최대화 상태를 먼저 적용하여 시작 시 깜빡임/순간이동 제거
            SourceInitialized += MainWindow_SourceInitialized;
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void CommitCurrentTabUndoCheckpoint()
        {
            if (_isApplyingUndoRedo || _viewModel.IsSwitchingOrExternalUpdate) return;
            var tab = _viewModel.ActiveTab;
            if (tab != null)
            {
                tab.RecordUndoCheckpoint(tab.Content, EditorTextBox.CaretIndex);
            }
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.IsSidebarVisible))
            {
                UpdateSidebarVisibility(_viewModel.IsSidebarVisible);
            }
        }

        private void OnViewModelActiveTabChanging(NoteTabViewModel? outgoingTab)
        {
            _undoSnapshotTimer.Stop();

            if (outgoingTab != null)
            {
                outgoingTab.CaretIndex = EditorTextBox.CaretIndex;
                outgoingTab.VerticalOffset = EditorTextBox.VerticalOffset;
                outgoingTab.HorizontalOffset = EditorTextBox.HorizontalOffset;
                outgoingTab.RecordUndoCheckpoint(outgoingTab.Content, outgoingTab.CaretIndex);
            }

            // 탭 전환 중 Text 바인딩 교체가 네이티브 TextBox Undo 스택에 섞이지 않도록 초기화
            EditorTextBox.IsUndoEnabled = false;
        }

        private void OnViewModelActiveTabChanged(NoteTabViewModel? incomingTab)
        {
            _lastFindIndex = -1;

            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                EditorTextBox.IsUndoEnabled = true;

                if (incomingTab != null && _viewModel.ActiveTab == incomingTab)
                {
                    int contentLen = incomingTab.Content?.Length ?? 0;
                    int targetCaret = Math.Clamp(incomingTab.CaretIndex, 0, contentLen);
                    EditorTextBox.CaretIndex = targetCaret;

                    if (incomingTab.VerticalOffset > 0 || incomingTab.HorizontalOffset > 0)
                    {
                        EditorTextBox.ScrollToVerticalOffset(incomingTab.VerticalOffset);
                        EditorTextBox.ScrollToHorizontalOffset(incomingTab.HorizontalOffset);
                    }
                    else if (targetCaret > 0)
                    {
                        int lineIdx = EditorTextBox.GetLineIndexFromCharacterIndex(targetCaret);
                        if (lineIdx > 0)
                        {
                            EditorTextBox.ScrollToLine(lineIdx);
                        }
                    }

                    UpdateEditorCursorLineCol();
                }
            }));
        }

        private void OnViewModelExternalContentReloaded()
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                int contentLen = _viewModel.ActiveTab?.Content?.Length ?? 0;
                int safeCaret = Math.Clamp(EditorTextBox.CaretIndex, 0, contentLen);
                EditorTextBox.CaretIndex = safeCaret;
                UpdateEditorCursorLineCol();
            }));
        }

        private void UpdateSidebarVisibility(bool isVisible)
        {
            if (!isVisible)
            {
                if (SidebarColumn.ActualWidth > 50)
                {
                    _savedSidebarWidth = SidebarColumn.ActualWidth;
                    _viewModel.SidebarWidth = _savedSidebarWidth;
                }
                SidebarColumn.MinWidth = 0;
                SidebarColumn.Width = new GridLength(0);
                SidebarSplitter.Visibility = Visibility.Collapsed;
            }
            else
            {
                double restoreWidth = _savedSidebarWidth >= 180 ? _savedSidebarWidth : 280;
                _viewModel.SidebarWidth = restoreWidth;
                SidebarColumn.MinWidth = 180;
                SidebarColumn.Width = new GridLength(restoreWidth);
                SidebarSplitter.Visibility = Visibility.Visible;
            }
        }

        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            var config = _viewModel.GetSessionConfig();

            _savedSidebarWidth = config.SidebarWidth >= 180 ? config.SidebarWidth : 280;
            UpdateSidebarVisibility(config.IsSidebarVisible);

            // 다중 모니터 및 화면 이탈 방지 안전 좌표 복원 (창이 표시되기 전에 선적용)
            ApplySafeWindowBounds(config.WindowLeft, config.WindowTop, config.WindowWidth, config.WindowHeight);

            if (config.IsMaximized)
            {
                WindowState = WindowState.Maximized;
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var config = _viewModel.GetSessionConfig();
            var activeTab = _viewModel.ActiveTab;

            // 에디터 커서 및 스크롤 위치 복원
            int contentLen = activeTab?.Content?.Length ?? 0;
            int initialCaret = activeTab?.CaretIndex > 0 ? activeTab.CaretIndex : config.CaretIndex;
            if (initialCaret > 0 && initialCaret <= contentLen)
            {
                EditorTextBox.CaretIndex = initialCaret;
            }

            if (activeTab != null && (activeTab.VerticalOffset > 0 || activeTab.HorizontalOffset > 0))
            {
                EditorTextBox.ScrollToVerticalOffset(activeTab.VerticalOffset);
                EditorTextBox.ScrollToHorizontalOffset(activeTab.HorizontalOffset);
            }
            else if (initialCaret > 0 && initialCaret <= contentLen)
            {
                int lineIdx = EditorTextBox.GetLineIndexFromCharacterIndex(initialCaret);
                if (lineIdx > 0)
                {
                    EditorTextBox.ScrollToLine(lineIdx);
                }
            }

            UpdateEditorCursorLineCol();

            if (_viewModel.HasOpenTabs)
            {
                EditorTextBox.Focus();
            }
        }

        private void ApplySafeWindowBounds(double savedLeft, double savedTop, double savedWidth, double savedHeight)
        {
            double vLeft = SystemParameters.VirtualScreenLeft;
            double vTop = SystemParameters.VirtualScreenTop;
            double vWidth = SystemParameters.VirtualScreenWidth;
            double vHeight = SystemParameters.VirtualScreenHeight;

            double targetWidth = (savedWidth >= MinWidth && !double.IsNaN(savedWidth)) ? Math.Min(savedWidth, vWidth) : 1100;
            double targetHeight = (savedHeight >= MinHeight && !double.IsNaN(savedHeight)) ? Math.Min(savedHeight, vHeight) : 720;

            Width = targetWidth;
            Height = targetHeight;

            // 창 상단 타이틀 영역이 실제 연결된 모니터(VirtualScreen) 안에 최소 120x60px 이상 걸쳐 있는지 검증
            bool isValidPosition = !double.IsNaN(savedLeft) && !double.IsNaN(savedTop)
                && (savedLeft + targetWidth >= vLeft + 120)
                && (savedLeft <= vLeft + vWidth - 120)
                && (savedTop >= vTop - 10)
                && (savedTop <= vTop + vHeight - 60);

            if (isValidPosition)
            {
                Left = savedLeft;
                Top = savedTop;
            }
            else
            {
                // 보조 모니터 분리 등으로 화면 밖 좌표일 경우 주 모니터 작업 영역 중앙에 안전 배치
                Rect workArea = SystemParameters.WorkArea;
                Width = Math.Min(targetWidth, workArea.Width);
                Height = Math.Min(targetHeight, workArea.Height);
                Left = workArea.Left + Math.Max(0, (workArea.Width - Width) / 2);
                Top = workArea.Top + Math.Max(0, (workArea.Height - Height) / 2);
            }
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            PersistWindowSession();
        }

        public void FlushSessionOnSystemShutdown()
        {
            PersistWindowSession();
        }

        private void PersistWindowSession()
        {
            _undoSnapshotTimer.Stop();

            bool isMaximized = WindowState == WindowState.Maximized;
            double left = isMaximized ? RestoreBounds.Left : Left;
            double top = isMaximized ? RestoreBounds.Top : Top;
            double width = isMaximized ? RestoreBounds.Width : Width;
            double height = isMaximized ? RestoreBounds.Height : Height;

            double currentSidebarWidth = _viewModel.IsSidebarVisible && SidebarColumn.ActualWidth > 50
                ? SidebarColumn.ActualWidth
                : _savedSidebarWidth;

            _viewModel.SidebarWidth = currentSidebarWidth;
            _viewModel.SaveSessionBeforeClose(
                left,
                top,
                width,
                height,
                isMaximized,
                EditorTextBox.CaretIndex,
                EditorTextBox.VerticalOffset,
                EditorTextBox.HorizontalOffset);
        }

        private void OnTabsScrollViewerScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            TabScrollLeftBtn.Visibility = TabsScrollViewer.HorizontalOffset > 1
                ? Visibility.Visible
                : Visibility.Collapsed;

            TabScrollRightBtn.Visibility = (TabsScrollViewer.ScrollableWidth - TabsScrollViewer.HorizontalOffset) > 1
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void OnTabScrollLeftClick(object sender, RoutedEventArgs e)
        {
            TabsScrollViewer.ScrollToHorizontalOffset(Math.Max(0, TabsScrollViewer.HorizontalOffset - 160));
        }

        private void OnTabScrollRightClick(object sender, RoutedEventArgs e)
        {
            TabsScrollViewer.ScrollToHorizontalOffset(Math.Min(TabsScrollViewer.ScrollableWidth, TabsScrollViewer.HorizontalOffset + 160));
        }

        private void OnTabsScrollViewerPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer sv)
            {
                sv.ScrollToHorizontalOffset(sv.HorizontalOffset - (e.Delta * 0.7));
                e.Handled = true;
            }
        }

        private void OnTabItemMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.DataContext is NoteTabViewModel tab)
            {
                _viewModel.ActiveTab = tab;
                elem.BringIntoView();
            }
        }

        private void OnTabItemMouseDown(object sender, MouseButtonEventArgs e)
        {
            // 마우스 휠(가운데 버튼) 클릭으로 탭 빠르게 닫기 지원
            if (e.ChangedButton == MouseButton.Middle && sender is FrameworkElement elem && elem.DataContext is NoteTabViewModel tab)
            {
                _viewModel.CloseTab(tab);
                e.Handled = true;
            }
        }

        private void OnHamburgerMenuClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.ContextMenu != null)
            {
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.IsOpen = true;
            }
        }

        private void OnMenuFindClick(object sender, RoutedEventArgs e)
        {
            ShowFindBar(showReplace: false);
        }

        private void OnMenuReplaceClick(object sender, RoutedEventArgs e)
        {
            ShowFindBar(showReplace: true);
        }

        private void OnInsertTimeDateClick(object sender, RoutedEventArgs e)
        {
            InsertCurrentTimeDateAtCaret();
        }

        private void InsertCurrentTimeDateAtCaret()
        {
            if (!_viewModel.HasOpenTabs || _viewModel.ActiveTab == null) return;

            CommitCurrentTabUndoCheckpoint();

            // 전통 Windows 메모장 표준 F5 타임스탬프 형식 (현재 문화권 기준 시간 + 날짜)
            string timestamp = DateTime.Now.ToString("tt h:mm yyyy-MM-dd");
            int selStart = EditorTextBox.SelectionStart;
            EditorTextBox.SelectedText = timestamp;
            EditorTextBox.SelectionLength = 0;
            EditorTextBox.CaretIndex = selStart + timestamp.Length;
            EditorTextBox.Focus();

            CommitCurrentTabUndoCheckpoint();
        }

        private void OnMenuUndoClick(object sender, RoutedEventArgs e)
        {
            PerformTabAwareUndo();
        }

        private void OnMenuRedoClick(object sender, RoutedEventArgs e)
        {
            PerformTabAwareRedo();
        }

        private bool PerformTabAwareUndo()
        {
            var tab = _viewModel.ActiveTab;
            if (tab == null) return false;

            if (EditorTextBox.CanUndo)
            {
                EditorTextBox.Undo();
                tab.ResetUndoBaseline(tab.Content, EditorTextBox.CaretIndex);
                return true;
            }

            if (tab.TryUndo(tab.Content, EditorTextBox.CaretIndex, out EditorSnapshot snapshot))
            {
                ApplySnapshotToEditor(tab, snapshot);
                return true;
            }

            return false;
        }

        private bool PerformTabAwareRedo()
        {
            var tab = _viewModel.ActiveTab;
            if (tab == null) return false;

            if (EditorTextBox.CanRedo)
            {
                EditorTextBox.Redo();
                tab.ResetUndoBaseline(tab.Content, EditorTextBox.CaretIndex);
                return true;
            }

            if (tab.TryRedo(out EditorSnapshot snapshot))
            {
                ApplySnapshotToEditor(tab, snapshot);
                return true;
            }

            return false;
        }

        private void ApplySnapshotToEditor(NoteTabViewModel tab, EditorSnapshot snapshot)
        {
            _isApplyingUndoRedo = true;
            try
            {
                EditorTextBox.IsUndoEnabled = false;
                tab.Content = snapshot.Text;
                EditorTextBox.IsUndoEnabled = true;

                int safeCaret = Math.Clamp(snapshot.CaretIndex, 0, snapshot.Text.Length);
                EditorTextBox.CaretIndex = safeCaret;
                int lineIdx = EditorTextBox.GetLineIndexFromCharacterIndex(safeCaret);
                if (lineIdx >= 0)
                {
                    EditorTextBox.ScrollToLine(lineIdx);
                }

                _viewModel.NotifyContentChanged();
                UpdateEditorCursorLineCol();
            }
            finally
            {
                _isApplyingUndoRedo = false;
            }
        }

        private void OnAboutClick(object sender, RoutedEventArgs e)
        {
            var aboutDialog = new Views.AboutDialog
            {
                Owner = this
            };
            aboutDialog.ShowDialog();
        }

        private bool ShowDeleteConfirmModal(string targetName, bool isFolder)
        {
            var dialog = new Views.DeleteConfirmDialog(targetName, isFolder)
            {
                Owner = this
            };
            dialog.ShowDialog();
            return dialog.IsConfirmed;
        }

        private void OnEditorTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isApplyingUndoRedo || _viewModel.IsSwitchingOrExternalUpdate) return;

            _viewModel.NotifyContentChanged();
            _undoSnapshotTimer.Stop();
            _undoSnapshotTimer.Start();
        }

        private void OnEditorPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (e.Delta > 0)
                {
                    _viewModel.ZoomIn();
                }
                else if (e.Delta < 0)
                {
                    _viewModel.ZoomOut();
                }
                e.Handled = true;
            }
        }

        private void OnTreeViewSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is TreeItemViewModel item)
            {
                _viewModel.SelectedItem = item;
            }
        }

        private void OnTreeViewPreviewKeyDown(object sender, KeyEventArgs e)
        {
            // 인라인 이름 편집 TextBox 입력 중일 때는 트리 단축키(Delete/F2/Esc/F5) 가로채기 금지
            if (e.OriginalSource is TextBox)
            {
                return;
            }

            if (e.Key == Key.F2 && Keyboard.Modifiers == ModifierKeys.None)
            {
                if (_viewModel.SelectedItem != null)
                {
                    _viewModel.RenameItem(_viewModel.SelectedItem);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
            {
                if (_viewModel.SelectedItem != null)
                {
                    _viewModel.DeleteSelectedItem();
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None)
            {
                _viewModel.ClearTreeSelection();
                e.Handled = true;
            }
            else if (e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.None)
            {
                _viewModel.RefreshTree();
                e.Handled = true;
            }
        }

        private void OnTreeViewPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            DependencyObject? dep = e.OriginalSource as DependencyObject;
            while (dep != null && dep != TreePadView)
            {
                if (dep is TreeViewItem)
                {
                    return;
                }
                dep = VisualTreeHelper.GetParent(dep);
            }

            // 트리의 빈 공간 클릭 시 선택 해제 (ROOT 모드)
            _viewModel.ClearTreeSelection();
        }

        private void OnTreeViewItemMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TreeViewItem treeViewItem)
            {
                treeViewItem.Focus();
                treeViewItem.IsSelected = true;
                e.Handled = true;
            }
        }

        private void PrepareInlineRenameTextBox(TextBox textBox)
        {
            if (textBox.DataContext is TreeItemViewModel item && item.IsEditing)
            {
                textBox.Text = item.Name;
                textBox.Focus();

                // .md 등 확장자가 표시된 파일인 경우 확장자 앞 본문 이름만 스마트 선택
                if (!item.IsFolder && item.Name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && item.Name.Length > 3)
                {
                    textBox.Select(0, item.Name.Length - 3);
                }
                else
                {
                    textBox.SelectAll();
                }
            }
        }

        private void OnItemNameEditLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                PrepareInlineRenameTextBox(textBox);
            }
        }

        private void OnItemNameEditIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is TextBox textBox && textBox.IsVisible)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => PrepareInlineRenameTextBox(textBox)));
            }
        }

        private void OnItemNameEditKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is TextBox textBox && textBox.DataContext is TreeItemViewModel item)
            {
                if (e.Key == Key.Enter)
                {
                    _viewModel.CommitRename(item, textBox.Text);
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    item.IsEditing = false;
                    textBox.Text = item.Name;
                    e.Handled = true;
                }
            }
        }

        private void OnItemNameEditLostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox && textBox.DataContext is TreeItemViewModel item)
            {
                if (item.IsEditing)
                {
                    _viewModel.CommitRename(item, textBox.Text);
                }
            }
        }

        private void OnEditorSelectionChanged(object sender, RoutedEventArgs e)
        {
            UpdateEditorCursorLineCol();
        }

        private void UpdateEditorCursorLineCol()
        {
            try
            {
                // ⚠️ EditorTextBox.Text는 호출할 때마다 전체 문자열을 힙에 새로 복사하므로 ActiveTab.Content 참조를 재사용하여 GC 할당 제로화
                string text = _viewModel.ActiveTab?.Content ?? string.Empty;
                int caretIndex = Math.Clamp(EditorTextBox.CaretIndex, 0, text.Length);

                // 자동 줄바꿈(WordWrap)으로 접힌 시각적 줄이 아닌 실제 개행(\n) 기준 논리적 줄/열 번호를 계산하여 총 줄 수와 완벽히 일치시킴
                int logicalLine = 1;
                int lastNewLinePos = -1;
                for (int i = 0; i < caretIndex; i++)
                {
                    if (text[i] == '\n')
                    {
                        logicalLine++;
                        lastNewLinePos = i;
                    }
                }

                int logicalCol = caretIndex - lastNewLinePos;
                _viewModel.UpdateCursorPosition(logicalLine, logicalCol);
            }
            catch
            {
                _viewModel.UpdateCursorPosition(1, 1);
            }
        }

        private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F)
            {
                if (_viewModel.HasOpenTabs)
                {
                    ShowFindBar(showReplace: false);
                }
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.H)
            {
                if (_viewModel.HasOpenTabs)
                {
                    ShowFindBar(showReplace: true);
                }
                e.Handled = true;
            }
            else if (e.Key == Key.F3)
            {
                if (_viewModel.HasOpenTabs)
                {
                    if (Keyboard.Modifiers == ModifierKeys.Shift)
                    {
                        FindPrevious(keepInputFocus: false);
                    }
                    else
                    {
                        FindNext(keepInputFocus: false);
                    }
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && FindBar.Visibility == Visibility.Visible)
            {
                HideFindBar();
                e.Handled = true;
            }
        }

        private void OnEditorPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F)
            {
                ShowFindBar(showReplace: false);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.H)
            {
                ShowFindBar(showReplace: true);
                e.Handled = true;
            }
            else if (e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.None)
            {
                // 전통 Windows 메모장 방식: 에디터에서 F5 키를 누르면 현재 커서 위치에 시간/날짜 삽입
                InsertCurrentTimeDateAtCaret();
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z)
            {
                if (PerformTabAwareUndo())
                {
                    e.Handled = true;
                }
            }
            else if ((Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Y) ||
                     (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Z))
            {
                if (PerformTabAwareRedo())
                {
                    e.Handled = true;
                }
            }
        }

        private void OnToggleFindBarClick(object sender, RoutedEventArgs e)
        {
            if (FindBar.Visibility == Visibility.Visible && ReplaceRowGrid.Visibility != Visibility.Visible)
            {
                HideFindBar();
            }
            else
            {
                ShowFindBar(showReplace: false);
            }
        }

        private void OnToggleReplaceBarClick(object sender, RoutedEventArgs e)
        {
            if (FindBar.Visibility == Visibility.Visible && ReplaceRowGrid.Visibility == Visibility.Visible)
            {
                HideFindBar();
            }
            else
            {
                ShowFindBar(showReplace: true);
            }
        }

        private void OnToggleReplaceRowClick(object sender, RoutedEventArgs e)
        {
            bool willShow = ReplaceRowGrid.Visibility != Visibility.Visible;
            SetReplaceRowVisibility(willShow);
        }

        private void SetReplaceRowVisibility(bool visible)
        {
            ReplaceRowGrid.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            ToggleReplaceIcon.Symbol = visible ? Wpf.Ui.Controls.SymbolRegular.ChevronDown16 : Wpf.Ui.Controls.SymbolRegular.ChevronRight16;
            if (visible)
            {
                ReplaceInputBox.Focus();
                ReplaceInputBox.SelectAll();
            }
        }

        private void ShowFindBar(bool showReplace = false)
        {
            FindBar.Visibility = Visibility.Visible;
            SetReplaceRowVisibility(showReplace);

            if (!string.IsNullOrEmpty(EditorTextBox.SelectedText) && !EditorTextBox.SelectedText.Contains('\n'))
            {
                FindInputBox.Text = EditorTextBox.SelectedText;
            }

            if (showReplace && !string.IsNullOrEmpty(FindInputBox.Text))
            {
                ReplaceInputBox.Focus();
                ReplaceInputBox.SelectAll();
            }
            else
            {
                FindInputBox.Focus();
                FindInputBox.SelectAll();
            }
        }

        private void HideFindBar()
        {
            FindBar.Visibility = Visibility.Collapsed;
            if (_viewModel.HasOpenTabs)
            {
                EditorTextBox.Focus();
            }
        }

        private void OnCloseFindBarClick(object sender, RoutedEventArgs e)
        {
            HideFindBar();
        }

        private void OnFindInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (Keyboard.Modifiers == ModifierKeys.Shift)
                {
                    FindPrevious(keepInputFocus: true);
                }
                else
                {
                    FindNext(keepInputFocus: true);
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                HideFindBar();
                e.Handled = true;
            }
        }

        private void OnFindInputTextChanged(object sender, TextChangedEventArgs e)
        {
            _lastFindIndex = -1;
        }

        private void OnReplaceInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ReplaceCurrent(keepInputFocus: true);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                HideFindBar();
                e.Handled = true;
            }
        }

        private void OnFindNextClick(object sender, RoutedEventArgs e)
        {
            FindNext(keepInputFocus: false);
        }

        private void OnFindPreviousClick(object sender, RoutedEventArgs e)
        {
            FindPrevious(keepInputFocus: false);
        }

        private void FindNext(bool keepInputFocus = false)
        {
            string query = FindInputBox.Text;
            if (string.IsNullOrEmpty(query)) return;

            string content = _viewModel.ActiveTab?.Content ?? EditorTextBox.Text;
            if (string.IsNullOrEmpty(content))
            {
                FindStatusText.Text = TreePad.Services.LocalizationService.Instance.GetFindEmptyDoc();
                return;
            }

            int startIndex;
            if (_lastFindIndex >= 0)
            {
                startIndex = _lastFindIndex + 1;
            }
            else
            {
                startIndex = EditorTextBox.SelectionStart + EditorTextBox.SelectionLength;
            }

            if (startIndex >= content.Length || startIndex < 0) startIndex = 0;

            int foundIndex = content.IndexOf(query, startIndex, StringComparison.CurrentCultureIgnoreCase);
            if (foundIndex == -1 && startIndex > 0)
            {
                foundIndex = content.IndexOf(query, 0, StringComparison.CurrentCultureIgnoreCase);
            }

            if (foundIndex != -1)
            {
                _lastFindIndex = foundIndex;
                EditorTextBox.Select(foundIndex, query.Length);
                int lineIdx = EditorTextBox.GetLineIndexFromCharacterIndex(foundIndex);
                if (lineIdx >= 0) EditorTextBox.ScrollToLine(lineIdx);
                if (!keepInputFocus)
                {
                    EditorTextBox.Focus();
                }
                FindStatusText.Text = TreePad.Services.LocalizationService.Instance.GetFindLocation(foundIndex + 1);
            }
            else
            {
                FindStatusText.Text = TreePad.Services.LocalizationService.Instance.GetFindNoMatch();
                _lastFindIndex = -1;
            }
        }

        private void FindPrevious(bool keepInputFocus = false)
        {
            string query = FindInputBox.Text;
            if (string.IsNullOrEmpty(query)) return;

            string content = _viewModel.ActiveTab?.Content ?? EditorTextBox.Text;
            if (string.IsNullOrEmpty(content))
            {
                FindStatusText.Text = TreePad.Services.LocalizationService.Instance.GetFindEmptyDoc();
                return;
            }

            int startIndex = EditorTextBox.SelectionStart - 1;
            if (startIndex < 0 || startIndex >= content.Length) startIndex = content.Length - 1;

            int foundIndex = content.LastIndexOf(query, startIndex, StringComparison.CurrentCultureIgnoreCase);
            if (foundIndex == -1 && startIndex < content.Length - 1)
            {
                foundIndex = content.LastIndexOf(query, content.Length - 1, StringComparison.CurrentCultureIgnoreCase);
            }

            if (foundIndex != -1)
            {
                _lastFindIndex = foundIndex;
                EditorTextBox.Select(foundIndex, query.Length);
                int lineIdx = EditorTextBox.GetLineIndexFromCharacterIndex(foundIndex);
                if (lineIdx >= 0) EditorTextBox.ScrollToLine(lineIdx);
                if (!keepInputFocus)
                {
                    EditorTextBox.Focus();
                }
                FindStatusText.Text = TreePad.Services.LocalizationService.Instance.GetFindLocation(foundIndex + 1);
            }
            else
            {
                FindStatusText.Text = TreePad.Services.LocalizationService.Instance.GetFindNoMatch();
                _lastFindIndex = -1;
            }
        }

        private void OnReplaceCurrentClick(object sender, RoutedEventArgs e)
        {
            ReplaceCurrent(keepInputFocus: false);
        }

        private void ReplaceCurrent(bool keepInputFocus = false)
        {
            string query = FindInputBox.Text;
            if (string.IsNullOrEmpty(query)) return;

            string replaceWith = ReplaceInputBox.Text ?? string.Empty;

            if (!string.IsNullOrEmpty(EditorTextBox.SelectedText) &&
                string.Equals(EditorTextBox.SelectedText, query, StringComparison.CurrentCultureIgnoreCase))
            {
                CommitCurrentTabUndoCheckpoint();
                int selStart = EditorTextBox.SelectionStart;
                EditorTextBox.SelectedText = replaceWith;
                CommitCurrentTabUndoCheckpoint();

                _lastFindIndex = selStart + replaceWith.Length - 1;
                FindStatusText.Text = TreePad.Services.LocalizationService.Instance.GetFindReplaced();
                FindNext(keepInputFocus);
            }
            else
            {
                FindNext(keepInputFocus);
            }
        }

        private void OnReplaceAllClick(object sender, RoutedEventArgs e)
        {
            string query = FindInputBox.Text;
            if (string.IsNullOrEmpty(query)) return;

            string replaceWith = ReplaceInputBox.Text ?? string.Empty;
            string content = _viewModel.ActiveTab?.Content ?? EditorTextBox.Text;
            if (string.IsNullOrEmpty(content)) return;

            int count = 0;
            int idx = 0;
            while ((idx = content.IndexOf(query, idx, StringComparison.CurrentCultureIgnoreCase)) != -1)
            {
                count++;
                idx += query.Length;
            }

            if (count > 0)
            {
                CommitCurrentTabUndoCheckpoint();

                string escapedQuery = Regex.Escape(query);
                string safeReplacement = replaceWith.Replace("$", "$$");
                string newContent = Regex.Replace(content, escapedQuery, safeReplacement, RegexOptions.IgnoreCase);

                // SelectAll + SelectedText 교체를 통해 네이티브 Undo 스택과 탭별 Undo 체크포인트 양쪽에서 모두 Ctrl+Z 복원이 가능하도록 처리
                EditorTextBox.SelectAll();
                EditorTextBox.SelectedText = newContent;
                EditorTextBox.CaretIndex = Math.Min(EditorTextBox.CaretIndex, newContent.Length);
                CommitCurrentTabUndoCheckpoint();

                FindStatusText.Text = TreePad.Services.LocalizationService.Instance.GetFindReplacedAll(count);
                _lastFindIndex = -1;
            }
            else
            {
                FindStatusText.Text = TreePad.Services.LocalizationService.Instance.GetFindNoMatch();
            }
        }

        // ==========================================
        // 🌟 트리 항목 드래그 앤 드롭 (ROOT 바 포함) 🌟
        // ==========================================

        private void OnTreeViewPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is TextBox)
            {
                _draggedItem = null;
                return;
            }
            _dragStartPoint = e.GetPosition(null);
            _draggedItem = FindTreeItemFromVisual(e.OriginalSource as DependencyObject);
        }

        private void OnTreeViewPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _draggedItem == null || _draggedItem.IsEditing) return;

            Point currentPoint = e.GetPosition(null);
            Vector diff = _dragStartPoint - currentPoint;

            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                try
                {
                    DragDrop.DoDragDrop(TreePadView, _draggedItem, DragDropEffects.Move);
                }
                finally
                {
                    ClearDragOver();
                    ResetRootBarHighlight();
                    _draggedItem = null;
                }
            }
        }

        private void OnRootBarDragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(TreeItemViewModel)))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            ClearDragOver();
            RootBarBorder.Background = new SolidColorBrush(Color.FromArgb(0x25, 0xD9, 0x6B, 0x27));
            RootBarBorder.BorderBrush = (Brush)FindResource("GruvAccentOrange");
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void OnRootBarDragLeave(object sender, DragEventArgs e)
        {
            ResetRootBarHighlight();
        }

        private void OnRootBarDrop(object sender, DragEventArgs e)
        {
            try
            {
                if (!e.Data.GetDataPresent(typeof(TreeItemViewModel))) return;
                if (e.Data.GetData(typeof(TreeItemViewModel)) is TreeItemViewModel sourceItem)
                {
                    _viewModel.MoveItem(sourceItem, null);
                }
            }
            finally
            {
                ResetRootBarHighlight();
                ClearDragOver();
                _draggedItem = null;
                e.Handled = true;
            }
        }

        private void ResetRootBarHighlight()
        {
            RootBarBorder.Background = (Brush)FindResource("GruvPanelBg");
            RootBarBorder.BorderBrush = Brushes.Transparent;
        }

        private void OnTreeViewDragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(TreeItemViewModel)))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            var sourceItem = e.Data.GetData(typeof(TreeItemViewModel)) as TreeItemViewModel;
            var targetItem = FindTreeItemUnderMouse(e.GetPosition(TreePadView));

            if (sourceItem == null || targetItem == sourceItem || (targetItem != null && IsDescendant(sourceItem, targetItem)))
            {
                e.Effects = DragDropEffects.None;
                ClearDragOver();
                e.Handled = true;
                return;
            }

            if (_currentDropTarget != targetItem)
            {
                ClearDragOver();
                _currentDropTarget = targetItem;
                if (_currentDropTarget != null)
                {
                    _currentDropTarget.IsDragOver = true;
                }
            }

            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void OnTreeViewDragLeave(object sender, DragEventArgs e)
        {
            ClearDragOver();
        }

        private void OnTreeViewDrop(object sender, DragEventArgs e)
        {
            try
            {
                if (!e.Data.GetDataPresent(typeof(TreeItemViewModel))) return;

                var sourceItem = e.Data.GetData(typeof(TreeItemViewModel)) as TreeItemViewModel;
                var targetItem = FindTreeItemUnderMouse(e.GetPosition(TreePadView));

                if (sourceItem == null || targetItem == sourceItem || (targetItem != null && IsDescendant(sourceItem, targetItem)))
                {
                    return;
                }

                _viewModel.MoveItem(sourceItem, targetItem);
            }
            finally
            {
                ClearDragOver();
                _draggedItem = null;
                e.Handled = true;
            }
        }

        private TreeItemViewModel? FindTreeItemFromVisual(DependencyObject? visual)
        {
            while (visual != null && visual != TreePadView)
            {
                if (visual is FrameworkElement fe && fe.DataContext is TreeItemViewModel item)
                {
                    return item;
                }
                visual = VisualTreeHelper.GetParent(visual);
            }
            return null;
        }

        private TreeItemViewModel? FindTreeItemUnderMouse(Point position)
        {
            var hit = VisualTreeHelper.HitTest(TreePadView, position);
            if (hit == null) return null;
            return FindTreeItemFromVisual(hit.VisualHit);
        }

        private bool IsDescendant(TreeItemViewModel ancestor, TreeItemViewModel candidate)
        {
            TreeItemViewModel? current = candidate.Parent;
            while (current != null)
            {
                if (current == ancestor) return true;
                current = current.Parent;
            }
            return false;
        }

        private void ClearDragOver()
        {
            if (_currentDropTarget != null)
            {
                _currentDropTarget.IsDragOver = false;
                _currentDropTarget = null;
            }
        }

        private void OnEditorContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            bool hasSelection = EditorTextBox.SelectionLength > 0;
            var activeTab = _viewModel.ActiveTab;

            MenuUndo.IsEnabled = EditorTextBox.CanUndo || (activeTab?.CanUndo ?? false);
            MenuRedo.IsEnabled = EditorTextBox.CanRedo || (activeTab?.CanRedo ?? false);

            MenuCopy.IsEnabled = hasSelection;
            MenuCut.IsEnabled = hasSelection;
            MenuSelectAll.IsEnabled = !string.IsNullOrEmpty(activeTab?.Content);

            try
            {
                MenuPaste.IsEnabled = Clipboard.ContainsText();
            }
            catch
            {
                MenuPaste.IsEnabled = true;
            }
        }
    }
}
