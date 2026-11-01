using System;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TreePad.ViewModels;

namespace TreePad
{
    public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
    {
        private readonly MainViewModel _viewModel;
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

            // Windows 11 시스템 테마 자동 감지 및 적용
            try
            {
                Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this);
            }
            catch { }

            _viewModel.PropertyChanged += ViewModel_PropertyChanged;

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.IsSidebarVisible))
            {
                UpdateSidebarVisibility(_viewModel.IsSidebarVisible);
            }
        }

        private void UpdateSidebarVisibility(bool isVisible)
        {
            if (!isVisible)
            {
                if (SidebarColumn.ActualWidth > 50)
                {
                    _savedSidebarWidth = SidebarColumn.ActualWidth;
                }
                SidebarColumn.MinWidth = 0;
                SidebarColumn.Width = new GridLength(0);
                SidebarSplitter.Visibility = Visibility.Collapsed;
            }
            else
            {
                SidebarColumn.MinWidth = 180;
                SidebarColumn.Width = new GridLength(_savedSidebarWidth >= 180 ? _savedSidebarWidth : 280);
                SidebarSplitter.Visibility = Visibility.Visible;
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var config = _viewModel.GetSessionConfig();

            // 이전 사이드바 폭 및 가시성 복원
            _savedSidebarWidth = config.SidebarWidth >= 180 ? config.SidebarWidth : 280;
            UpdateSidebarVisibility(config.IsSidebarVisible);

            // 이전 창 위치 및 크기 복원
            if (config.WindowWidth >= MinWidth && config.WindowHeight >= MinHeight)
            {
                Width = config.WindowWidth;
                Height = config.WindowHeight;
            }

            if (config.WindowLeft >= 0 && config.WindowTop >= 0)
            {
                Left = config.WindowLeft;
                Top = config.WindowTop;
            }

            if (config.IsMaximized)
            {
                WindowState = WindowState.Maximized;
            }

            // 에디터 커서 위치 복원
            if (config.CaretIndex > 0 && config.CaretIndex <= EditorTextBox.Text.Length)
            {
                EditorTextBox.CaretIndex = config.CaretIndex;
            }

            EditorTextBox.Focus();
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            bool isMaximized = WindowState == WindowState.Maximized;
            double left = isMaximized ? RestoreBounds.Left : Left;
            double top = isMaximized ? RestoreBounds.Top : Top;
            double width = isMaximized ? RestoreBounds.Width : Width;
            double height = isMaximized ? RestoreBounds.Height : Height;

            double currentSidebarWidth = _viewModel.IsSidebarVisible && SidebarColumn.ActualWidth > 50
                ? SidebarColumn.ActualWidth 
                : _savedSidebarWidth;

            _viewModel.SidebarWidth = currentSidebarWidth;
            _viewModel.SaveSessionBeforeClose(left, top, width, height, isMaximized, EditorTextBox.CaretIndex);
        }

        private void OnTabsScrollViewerScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // 좌측에 숨겨진 탭이 있으면 좌측 화살표 표시
            TabScrollLeftBtn.Visibility = TabsScrollViewer.HorizontalOffset > 1 
                ? Visibility.Visible 
                : Visibility.Collapsed;

            // 오른쪽에 숨겨진 탭이 있으면 우측 화살표 표시
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

        private void OnAboutClick(object sender, RoutedEventArgs e)
        {
            string aboutText = TreePad.Services.LocalizationService.Instance.GetAboutContent();
            string aboutTitle = TreePad.Services.LocalizationService.Instance.GetAboutTitle();
            MessageBox.Show(aboutText, aboutTitle, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnEditorTextChanged(object sender, TextChangedEventArgs e)
        {
            _viewModel.NotifyContentChanged();
        }

        private void OnTreeViewSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is TreeItemViewModel item)
            {
                _viewModel.SelectedItem = item;
            }
        }

        private void OnTreeViewPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            DependencyObject? dep = e.OriginalSource as DependencyObject;
            while (dep != null && dep != TreePadView)
            {
                if (dep is TreeViewItem)
                {
                    return; // 아이템을 클릭한 경우는 정상 처리
                }
                dep = System.Windows.Media.VisualTreeHelper.GetParent(dep);
            }

            // 트리의 빈 공간(여백)을 클릭하면 선택 해제(최상단 모드)
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

        private void OnItemNameEditLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                textBox.Focus();
                textBox.SelectAll();
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
            int caretIndex = EditorTextBox.CaretIndex;
            int line = EditorTextBox.GetLineIndexFromCharacterIndex(caretIndex);
            int lineCharIndex = EditorTextBox.GetCharacterIndexFromLineIndex(line);
            int col = caretIndex - lineCharIndex;

            _viewModel.UpdateCursorPosition(line + 1, col + 1);
        }

        private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
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
            else if (e.Key == Key.F3)
            {
                if (Keyboard.Modifiers == ModifierKeys.Shift)
                {
                    FindPrevious();
                }
                else
                {
                    FindNext();
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

            if (!string.IsNullOrEmpty(EditorTextBox.SelectedText))
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
            EditorTextBox.Focus();
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
                    FindPrevious();
                }
                else
                {
                    FindNext();
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
                ReplaceCurrent();
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
            FindNext();
        }

        private void OnFindPreviousClick(object sender, RoutedEventArgs e)
        {
            FindPrevious();
        }

        private void FindNext()
        {
            string query = FindInputBox.Text;
            if (string.IsNullOrEmpty(query)) return;

            string content = EditorTextBox.Text;
            if (string.IsNullOrEmpty(content))
            {
                FindStatusText.Text = TreePad.Services.LocalizationService.Instance.GetFindEmptyDoc();
                return;
            }

            int startIndex = _lastFindIndex + 1;
            if (startIndex >= content.Length) startIndex = 0;

            int foundIndex = content.IndexOf(query, startIndex, StringComparison.CurrentCultureIgnoreCase);
            if (foundIndex == -1 && startIndex > 0)
            {
                foundIndex = content.IndexOf(query, 0, StringComparison.CurrentCultureIgnoreCase);
            }

            if (foundIndex != -1)
            {
                _lastFindIndex = foundIndex;
                EditorTextBox.Focus();
                EditorTextBox.Select(foundIndex, query.Length);
                FindStatusText.Text = TreePad.Services.LocalizationService.Instance.GetFindLocation(foundIndex + 1);
            }
            else
            {
                FindStatusText.Text = TreePad.Services.LocalizationService.Instance.GetFindNoMatch();
                _lastFindIndex = -1;
            }
        }

        private void FindPrevious()
        {
            string query = FindInputBox.Text;
            if (string.IsNullOrEmpty(query)) return;

            string content = EditorTextBox.Text;
            if (string.IsNullOrEmpty(content))
            {
                FindStatusText.Text = TreePad.Services.LocalizationService.Instance.GetFindEmptyDoc();
                return;
            }

            int startIndex = EditorTextBox.SelectionStart - 1;
            if (startIndex < 0) startIndex = content.Length - 1;

            int foundIndex = content.LastIndexOf(query, startIndex, StringComparison.CurrentCultureIgnoreCase);
            if (foundIndex == -1 && startIndex < content.Length - 1)
            {
                foundIndex = content.LastIndexOf(query, content.Length - 1, StringComparison.CurrentCultureIgnoreCase);
            }

            if (foundIndex != -1)
            {
                _lastFindIndex = foundIndex;
                EditorTextBox.Focus();
                EditorTextBox.Select(foundIndex, query.Length);
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
            ReplaceCurrent();
        }

        private void ReplaceCurrent()
        {
            string query = FindInputBox.Text;
            if (string.IsNullOrEmpty(query)) return;

            string replaceWith = ReplaceInputBox.Text ?? string.Empty;

            // 현재 선택된 텍스트가 검색어와 일치하는지 확인
            if (!string.IsNullOrEmpty(EditorTextBox.SelectedText) &&
                string.Equals(EditorTextBox.SelectedText, query, StringComparison.CurrentCultureIgnoreCase))
            {
                int selStart = EditorTextBox.SelectionStart;
                EditorTextBox.SelectedText = replaceWith;
                _lastFindIndex = selStart + replaceWith.Length - 1;
                FindStatusText.Text = TreePad.Services.LocalizationService.Instance.GetFindReplaced();
                FindNext();
            }
            else
            {
                // 아직 선택되지 않았으면 먼저 찾기
                FindNext();
            }
        }

        private void OnReplaceAllClick(object sender, RoutedEventArgs e)
        {
            string query = FindInputBox.Text;
            if (string.IsNullOrEmpty(query)) return;

            string replaceWith = ReplaceInputBox.Text ?? string.Empty;
            string content = EditorTextBox.Text;
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
                string escapedQuery = Regex.Escape(query);
                string safeReplacement = replaceWith.Replace("$", "$$");
                string newContent = Regex.Replace(content, escapedQuery, safeReplacement, RegexOptions.IgnoreCase);

                EditorTextBox.Text = newContent;
                FindStatusText.Text = TreePad.Services.LocalizationService.Instance.GetFindReplacedAll(count);
                _lastFindIndex = -1;
            }
            else
            {
                FindStatusText.Text = TreePad.Services.LocalizationService.Instance.GetFindNoMatch();
            }
        }

        // ==========================================
        // 🌟 트리 항목 드래그 앤 드롭 (Drag & Drop) 🌟
        // ==========================================

        private void OnTreeViewPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            _draggedItem = FindTreeItemFromVisual(e.OriginalSource as DependencyObject);
        }

        private void OnTreeViewPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _draggedItem == null) return;

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
                    _draggedItem = null;
                }
            }
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

            // 시각적 피드백: 타겟 아이템 하이라이트
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

        private void OnCloudGuideClick(object sender, RoutedEventArgs e)
        {
            string guideMessage = TreePad.Services.LocalizationService.Instance.GetCloudGuideContent();
            string guideTitle = TreePad.Services.LocalizationService.Instance.GetCloudGuideTitle();
            MessageBox.Show(guideMessage, guideTitle, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnEditorContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            bool hasSelection = EditorTextBox.SelectionLength > 0;

            // 1. 선택이 있을 때만 "복사", "잘라내기" 표시
            MenuCopy.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
            MenuCut.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;

            // 2. "전체선택", "붙여넣기"는 항상 표시
            MenuSelectAll.Visibility = Visibility.Visible;
            MenuPaste.Visibility = Visibility.Visible;

            // 클립보드에 텍스트가 있을 때만 붙여넣기 활성화
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
