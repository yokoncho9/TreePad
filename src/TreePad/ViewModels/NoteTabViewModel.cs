using System;
using System.Collections.Generic;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TreePad.ViewModels
{
    public readonly record struct EditorSnapshot(string Text, int CaretIndex);

    public partial class NoteTabViewModel : ObservableObject
    {
        private const int MaxUndoSteps = 100;
        private const int MaxUndoTotalChars = 2_000_000;

        public string FilePath { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public Encoding FileEncoding { get; set; } = new UTF8Encoding(false);
        public DateTime? LastSyncedWriteTimeUtc { get; set; }

        [ObservableProperty]
        private string _title = string.Empty;

        [ObservableProperty]
        private string _content = string.Empty;

        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private int _caretIndex;

        public double VerticalOffset { get; set; }
        public double HorizontalOffset { get; set; }
        public bool IsDirty { get; set; }

        private readonly LinkedList<EditorSnapshot> _undoStack = new();
        private readonly Stack<EditorSnapshot> _redoStack = new();
        private int _totalUndoChars = 0;
        private string _lastRecordedUndoText;
        private int _lastRecordedCaretIndex;

        private readonly MainViewModel _mainVm;

        public NoteTabViewModel(
            string filePath,
            string relativePath,
            string title,
            string content,
            MainViewModel mainVm,
            Encoding? fileEncoding = null,
            DateTime? lastSyncedWriteTimeUtc = null)
        {
            FilePath = filePath;
            RelativePath = relativePath;
            _title = title;
            _content = content;
            _mainVm = mainVm;
            FileEncoding = fileEncoding ?? new UTF8Encoding(false);
            LastSyncedWriteTimeUtc = lastSyncedWriteTimeUtc;
            _lastRecordedUndoText = content;
            _lastRecordedCaretIndex = 0;
        }

        public bool CanUndo => _undoStack.Count > 0 || !string.Equals(Content, _lastRecordedUndoText, StringComparison.Ordinal);
        public bool CanRedo => _redoStack.Count > 0;

        public void RecordUndoCheckpoint(string currentText, int caretIndex)
        {
            if (string.Equals(currentText, _lastRecordedUndoText, StringComparison.Ordinal))
            {
                _lastRecordedCaretIndex = caretIndex;
                return;
            }

            PushUndoSnapshot(new EditorSnapshot(_lastRecordedUndoText, _lastRecordedCaretIndex));

            _redoStack.Clear();
            _lastRecordedUndoText = currentText;
            _lastRecordedCaretIndex = caretIndex;
        }

        public void ResetUndoBaseline(string currentText, int caretIndex)
        {
            _lastRecordedUndoText = currentText;
            _lastRecordedCaretIndex = caretIndex;
        }

        public bool TryUndo(string currentEditorText, int currentCaret, out EditorSnapshot restored)
        {
            if (!string.Equals(currentEditorText, _lastRecordedUndoText, StringComparison.Ordinal))
            {
                _redoStack.Push(new EditorSnapshot(currentEditorText, currentCaret));
                restored = new EditorSnapshot(_lastRecordedUndoText, _lastRecordedCaretIndex);
                return true;
            }

            if (_undoStack.Count > 0)
            {
                _redoStack.Push(new EditorSnapshot(_lastRecordedUndoText, _lastRecordedCaretIndex));
                restored = _undoStack.Last!.Value;
                _undoStack.RemoveLast();
                _totalUndoChars = Math.Max(0, _totalUndoChars - restored.Text.Length);
                _lastRecordedUndoText = restored.Text;
                _lastRecordedCaretIndex = restored.CaretIndex;
                return true;
            }

            restored = default;
            return false;
        }

        public bool TryRedo(out EditorSnapshot restored)
        {
            if (_redoStack.Count > 0)
            {
                PushUndoSnapshot(new EditorSnapshot(_lastRecordedUndoText, _lastRecordedCaretIndex));

                restored = _redoStack.Pop();
                _lastRecordedUndoText = restored.Text;
                _lastRecordedCaretIndex = restored.CaretIndex;
                return true;
            }

            restored = default;
            return false;
        }

        private void PushUndoSnapshot(EditorSnapshot snapshot)
        {
            _undoStack.AddLast(snapshot);
            _totalUndoChars += snapshot.Text.Length;

            while (_undoStack.Count > MaxUndoSteps || (_undoStack.Count > 1 && _totalUndoChars > MaxUndoTotalChars))
            {
                var oldest = _undoStack.First!.Value;
                _undoStack.RemoveFirst();
                _totalUndoChars = Math.Max(0, _totalUndoChars - oldest.Text.Length);
            }
        }

        [RelayCommand]
        public void Close()
        {
            _mainVm.CloseTab(this);
        }
    }
}
