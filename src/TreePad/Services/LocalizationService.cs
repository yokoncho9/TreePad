using System;
using System.Globalization;
using System.Windows;

namespace TreePad.Services
{
    public enum SaveStateKind
    {
        Ready,
        Typing,
        Saving,
        Saved,
        NoOpenNote,
        StorageChanged,
        Refreshed,
        ExternalSynced,
        Error
    }

    public class LocalizationService
    {
        private static LocalizationService? _instance;
        public static LocalizationService Instance => _instance ??= new LocalizationService();

        private string _languageSetting = "Auto";
        private string _activeLanguage = "ko";

        public string LanguageSetting => _languageSetting;
        public string ActiveLanguage => _activeLanguage;
        public bool IsEnglish => string.Equals(_activeLanguage, "en", StringComparison.OrdinalIgnoreCase);

        public event Action? LanguageChanged;

        public void Initialize(string? savedSetting)
        {
            _languageSetting = string.IsNullOrWhiteSpace(savedSetting) ? "Auto" : savedSetting;
            ApplyLanguage();
        }

        public void SetLanguage(string setting)
        {
            _languageSetting = setting;
            ApplyLanguage();
            LanguageChanged?.Invoke();
        }

        private void ApplyLanguage()
        {
            if (string.Equals(_languageSetting, "Auto", StringComparison.OrdinalIgnoreCase))
            {
                string sysLang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
                _activeLanguage = sysLang == "ko" ? "ko" : "en";
            }
            else
            {
                _activeLanguage = string.Equals(_languageSetting, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "ko";
            }

            try
            {
                if (Application.Current == null) return;

                string dictUri = $"/TreePad;component/Resources/Strings.{_activeLanguage}.xaml";
                var newDict = new ResourceDictionary { Source = new Uri(dictUri, UriKind.RelativeOrAbsolute) };

                var appResources = Application.Current.Resources;
                var merged = appResources.MergedDictionaries;

                ResourceDictionary? oldLangDict = null;
                foreach (var md in merged)
                {
                    if (md.Source != null && md.Source.OriginalString.Contains("Strings."))
                    {
                        oldLangDict = md;
                        break;
                    }
                }

                if (oldLangDict != null)
                {
                    merged.Remove(oldLangDict);
                }
                merged.Add(newDict);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load language dictionary: {ex.Message}");
            }
        }

        public string GetString(string key)
        {
            try
            {
                if (Application.Current != null && Application.Current.Resources.Contains(key))
                {
                    return Application.Current.Resources[key]?.ToString() ?? key;
                }
            }
            catch { }
            return key;
        }

        // ==========================================
        // 🌟 기본 생성 이름 & 환영 데이터 다국어 🌟
        // ==========================================

        public string GetDefaultNoteName() => IsEnglish ? "New Note" : "새 메모";
        public string GetDefaultFolderName() => IsEnglish ? "New Folder" : "새 폴더";

        public string GetWelcomeFileName() => IsEnglish ? "Welcome to TreePad.txt" : "환영합니다 (TreePad 사용법).txt";
        public string GetWelcomeSampleFolderName() => IsEnglish ? "Work & Ideas" : "업무 및 아이디어";
        public string GetWelcomeSampleNoteName() => IsEnglish ? "Project Ideas.txt" : "신규 프로젝트 아이디어.txt";

        public string GetWelcomeNoteContent()
        {
            if (IsEnglish)
            {
                return
@"# 🌲 TreePad - Hierarchical Plain-Text Notepad

Welcome to TreePad!
A fast, lightweight, distraction-free plain-text notepad built for Windows 11.

======================================================================
1. Key Highlights
======================================================================
• Pure Plain Text (.txt & .md):
  - Clean, distraction-free writing with standard UTF-8 .txt and .md files.
• Zero-Save & Instant Session Restore:
  - No need to press Ctrl+S. Your notes auto-save as you type, and your open tabs,
    scroll/cursor positions, and window layout restore automatically on launch.
• Portable Single-File Executable:
  - Runs out of the box with zero runtime installation required.

======================================================================
2. Keyboard Shortcuts
======================================================================
• New Note:          Ctrl + N
• New Folder:        Ctrl + Shift + N
• Rename Item:       F2 (when tree is focused)
• Delete Item:       Delete (moves safely to Recycle Bin)
• Refresh Tree:      F5
• Find / Replace:    Ctrl + F / Ctrl + H
• Toggle Word Wrap:  Alt + Z
• Toggle Sidebar:    Ctrl + B
• Close Tab:         Ctrl + W
• Zoom Font Size:    Ctrl + Mouse Wheel (or Ctrl + + / - / 0)

======================================================================
3. Cloud & Smartphone Sync Guide
======================================================================
All notes in TreePad are stored directly as real folders and UTF-8 text files.

1) Set your cloud sync folder:
   - Click the [≡ Menu] button -> [Change Storage...] and select a folder inside
     Google Drive, OneDrive, or Dropbox.
2) Read & edit on mobile (Android / iOS):
   - Open your synced .txt or .md notes with any mobile text/markdown app
     (such as Markor, Obsidian, QuickEdit, or Google Drive).
3) Automatic live detection:
   - TreePad automatically detects external cloud updates while running!
";
            }
            else
            {
                return
@"# 🌲 TreePad - 나만의 트리형 텍스트 메모장

TreePad에 오신 것을 환영합니다!
Windows 11 전용으로 제작된 가볍고 군더더기 없는 순수 텍스트 메모장입니다.

======================================================================
1. 핵심 특징
======================================================================
• 순수 텍스트 표준 파일 (.txt & .md 지원):
  - 서식 없는 깔끔한 UTF-8 .txt 및 .md 파일을 그대로 읽고 저장합니다.
• 무손실 자동 세션 (Zero-Save):
  - 별도로 저장 버튼을 누를 필요가 없습니다. 타이핑 즉시 자동 저장되며,
    창을 닫았다가 다시 열면 열려 있던 탭과 커서 위치가 그대로 복원됩니다.
• 무설치 단일 실행 파일:
  - 별도 런타임 설치 없이 실행 파일 하나로 가볍고 빠르게 작동합니다.

======================================================================
2. 주요 단축키
======================================================================
• 새 메모 만들기:    Ctrl + N
• 새 폴더 만들기:    Ctrl + Shift + N
• 이름 바꾸기:       F2 (트리 선택 시)
• 안전 삭제:         Delete (Windows 휴지통으로 이동)
• 트리 새로고침:     F5
• 찾기 / 바꾸기:     Ctrl + F / Ctrl + H
• 자동 줄바꿈 토글:  Alt + Z
• 사이드바 접기/펴기: Ctrl + B
• 현재 탭 닫기:      Ctrl + W
• 글꼴 크기 조절:    Ctrl + 마우스 휠 (또는 Ctrl + + / - / 0)

======================================================================
3. 스마트폰 (안드로이드) 및 클라우드 연동 가이드
======================================================================
TreePad의 모든 메모는 사용자 PC의 실제 폴더와 UTF-8 텍스트 파일로 저장됩니다.

1) 클라우드 드라이브 동기화 폴더 연결:
   - 좌측 상단 [≡ 메뉴] → [저장 위치 변경...]을 눌러 Google Drive, OneDrive,
     또는 Dropbox 동기화 폴더를 지정하세요.
2) 스마트폰에서 확인 및 편집:
   - 스마트폰의 클라우드 앱이나 텍스트/마크다운 편집기(Markor, QuickEdit, Obsidian 등)로
     언제 어디서나 동일하게 열람하고 편집할 수 있습니다.
3) 실시간 외부 변경 감지:
   - 클라우드를 통해 외부에서 추가·수정된 파일도 자동으로 감지하여 반영합니다!
";
            }
        }

        public string GetWelcomeSampleNoteContent()
        {
            if (IsEnglish)
            {
                return
@"Project Idea Notes:
1. Keep the interface clean and calm
2. Instant startup and zero-lag typing
3. Reliable plain-text storage you truly own";
            }
            else
            {
                return
@"프로젝트 아이디어 메모:
1. 심플하고 눈이 편안한 인터페이스 유지
2. 지연 없는 즉각적인 반응 속도
3. 특정 앱에 종속되지 않는 안전한 텍스트 보관";
            }
        }

        // ==========================================
        // 🌟 상태표시줄 포맷 헬퍼 🌟
        // ==========================================

        public string GetSaveStatusText(SaveStateKind kind, string? errorDetail = null)
        {
            if (IsEnglish)
            {
                return kind switch
                {
                    SaveStateKind.Ready => "Ready",
                    SaveStateKind.Typing => "Typing...",
                    SaveStateKind.Saving => "Saving...",
                    SaveStateKind.Saved => "Saved",
                    SaveStateKind.NoOpenNote => "No open note",
                    SaveStateKind.StorageChanged => "Storage updated",
                    SaveStateKind.Refreshed => "Refreshed",
                    SaveStateKind.ExternalSynced => "Synced from disk",
                    SaveStateKind.Error => $"Save failed: {errorDetail}",
                    _ => "Ready"
                };
            }
            else
            {
                return kind switch
                {
                    SaveStateKind.Ready => "준비",
                    SaveStateKind.Typing => "입력 중...",
                    SaveStateKind.Saving => "저장 중...",
                    SaveStateKind.Saved => "저장됨",
                    SaveStateKind.NoOpenNote => "열린 메모 없음",
                    SaveStateKind.StorageChanged => "저장 위치 변경 완료",
                    SaveStateKind.Refreshed => "새로고침 완료",
                    SaveStateKind.ExternalSynced => "외부 변경 반영됨",
                    SaveStateKind.Error => $"저장 실패: {errorDetail}",
                    _ => "준비"
                };
            }
        }

        public string GetWordWrapTooltip(bool isWrap)
        {
            if (IsEnglish)
                return isWrap ? "Word Wrap: On (Alt+Z)" : "Word Wrap: Off (Alt+Z)";
            else
                return isWrap ? "자동 줄바꿈: 켜짐 (Alt+Z)" : "자동 줄바꿈: 꺼짐 (Alt+Z)";
        }

        public string GetCharCountPrimary(int total)
            => IsEnglish ? $"{total:N0} chars" : $"{total:N0}자";

        public string GetCharCountNoSpaces(int noSpaces)
            => IsEnglish ? $"(no spaces {noSpaces:N0})" : $"(공백제외 {noSpaces:N0}자)";

        public string GetLineCountText(int lines)
            => IsEnglish ? $"{lines:N0} lines" : $"{lines:N0}줄";

        public string GetCursorPosText(int line, int col)
            => IsEnglish ? $"Ln {line}, Col {col}" : $"줄 {line}, 열 {col}";

        // ==========================================
        // 🌟 다국어 대화상자 & 오류 메시지 헬퍼 🌟
        // ==========================================

        public string GetErrorTitle() => IsEnglish ? "Error" : "오류";
        public string GetNoticeTitle() => IsEnglish ? "Notice" : "알림";

        public string GetDeleteConfirmTitle() => IsEnglish ? "Move to Recycle Bin" : "휴지통으로 이동";
        public string GetDeleteConfirmMessage(string targetName, bool isFolder)
        {
            if (IsEnglish)
            {
                string type = isFolder ? "folder and all its contents" : "note";
                return $"Move '{targetName}' ({type}) to the Recycle Bin?\n(You can restore it anytime from the Windows Recycle Bin.)";
            }
            else
            {
                string type = isFolder ? "폴더 및 하위 모든 내용" : "메모";
                return $"정말로 '{targetName}' {type}을(를) 휴지통으로 이동하시겠습니까?\n(언제든 Windows 휴지통에서 복원할 수 있습니다)";
            }
        }

        public string GetDeleteFailedMessage(string msg) => IsEnglish ? $"Delete failed: {msg}" : $"삭제 실패: {msg}";
        public string GetMoveFailedTitle() => IsEnglish ? "Move Item" : "이동 안내";
        public string GetMoveFailedMessage(string msg) => IsEnglish ? $"Could not move item: {msg}" : $"항목 이동 실패: {msg}";

        public string GetCreateNoteFailed(string msg) => IsEnglish ? $"Failed to create note: {msg}" : $"메모 생성 중 오류 발생: {msg}";
        public string GetCreateFolderFailed(string msg) => IsEnglish ? $"Failed to create folder: {msg}" : $"폴더 생성 중 오류 발생: {msg}";
        public string GetRenameFailed(string msg) => IsEnglish ? $"Rename failed: {msg}" : $"이름 변경 실패: {msg}";
        public string GetFileReadFailed(string msg) => IsEnglish ? $"Error reading file: {msg}" : $"파일을 읽는 중 오류 발생: {msg}";
        public string GetExplorerFailed(string msg) => IsEnglish ? $"Failed to open File Explorer: {msg}" : $"탐색기 열기 실패: {msg}";

        public string GetDuplicateFolderError(string? name = null)
            => IsEnglish
                ? (string.IsNullOrEmpty(name) ? "A folder with the same name already exists." : $"A folder with the same name already exists: {name}")
                : (string.IsNullOrEmpty(name) ? "이미 동일한 이름의 폴더가 존재합니다." : $"이미 대상 위치에 동일한 이름의 폴더가 존재합니다: {name}");

        public string GetDuplicateNoteError(string? name = null)
            => IsEnglish
                ? (string.IsNullOrEmpty(name) ? "A note with the same name already exists." : $"A note with the same name already exists: {name}")
                : (string.IsNullOrEmpty(name) ? "이미 동일한 이름의 메모가 존재합니다." : $"이미 대상 위치에 동일한 이름의 메모가 존재합니다: {name}");

        public string GetCannotMoveIntoSubfolderError()
            => IsEnglish
                ? "Cannot move a folder into one of its own subfolders."
                : "폴더를 자기 자신의 하위 폴더로 이동할 수 없습니다.";

        public string GetChangeStorageDialogTitle()
            => IsEnglish
                ? "Select TreePad Notes Folder (Google Drive, OneDrive, Dropbox, etc.)"
                : "TreePad 메모 저장 폴더 선택 (Google Drive, OneDrive, Dropbox 동기화 폴더 등)";

        public string GetMigrateStorageTitle()
            => IsEnglish ? "Copy Existing Notes?" : "메모 데이터 이전 안내";

        public string GetMigrateStorageMessage(string oldPath)
        {
            if (IsEnglish)
            {
                return $"Would you like to copy your existing notes and folders from:\n{oldPath}\nto the new storage folder?\n\n" +
                       "• [Yes] : Safely copy all existing notes and folders to the new location. (Recommended)\n" +
                       "• [No] : Keep existing notes where they are and connect the new folder as is.\n" +
                       "• [Cancel] : Cancel changing storage location.";
            }
            else
            {
                return $"기존 저장 위치({oldPath})에 있는 메모와 폴더들을 새 위치로 함께 복사하시겠습니까?\n\n" +
                       "• [예] : 현재 트리에 저장된 모든 메모와 폴더를 새 위치로 손실 없이 안전하게 복사합니다. (권장)\n" +
                       "• [아니오] : 기존 메모는 원본 위치에 보존하고, 새 폴더를 그대로 연결합니다.\n" +
                       "• [취소] : 저장 위치 변경을 취소합니다.";
            }
        }

        public string GetMigrateCopyWarning(string msg)
            => IsEnglish
                ? $"Some items could not be copied: {msg}"
                : $"메모 복사 중 일부 항목에서 오류가 발생했습니다: {msg}";

        public string GetAboutTitle() => IsEnglish ? "About TreePad" : "TreePad 정보";
        public string GetAboutContent()
        {
            if (IsEnglish)
            {
                return
@"🌲 TreePad v1.0.1

A minimal, hierarchical plain-text notepad for Windows 11.
Warm Gruvbox Material Paper Theme.

• Developer: yokoncho9
• GitHub: https://github.com/yokoncho9/TreePad
• License: MIT License

Fast, distraction-free writing with folder trees, live cloud sync (.txt / .md), and instant zero-save session restore.";
            }
            else
            {
                return
@"🌲 TreePad v1.0.1

나만의 윈도우 11 순수 텍스트 트리 메모장
편안한 Gruvbox Material 종이 테마 적용

• 개발자: yokoncho9
• 깃허브: https://github.com/yokoncho9/TreePad
• 라이선스: MIT License

군더더기 없는 가벼운 속도와 계층적 트리 정리, 실시간 클라우드 연동(.txt / .md), 무손실 자동 세션 복원을 경험해 보세요.";
            }
        }

        public string GetCloudGuideTitle() => IsEnglish ? "Cloud & Mobile Sync Guide" : "스마트폰(안드로이드) 및 클라우드 동기화 안내";
        public string GetCloudGuideContent()
        {
            if (IsEnglish)
            {
                return
@"📱 Smartphone & Cloud Sync Guide

All TreePad data is stored directly on your disk as standard UTF-8 .txt and .md files inside real folders.

1) Set your cloud sync folder:
   - Click [≡ Menu] -> [Change Storage...].
   - Choose a folder inside Google Drive, OneDrive, or Dropbox.

2) Read & edit on your smartphone:
   - Open and edit your synced .txt / .md files anytime using Google Drive, Markor, Obsidian, or QuickEdit.

3) Real-time external detection:
   - TreePad watches the folder in real time and automatically reflects external changes (or press F5 anytime).";
            }
            else
            {
                return
@"📱 스마트폰(안드로이드) 및 클라우드 동기화 안내

TreePad의 모든 데이터는 실제 디스크의 폴더와 표준 UTF-8 .txt / .md 파일로 저장됩니다.

1) 클라우드 드라이브 동기화 폴더 설정:
   - 좌측 상단 [≡ 메뉴] → [저장 위치 변경...]을 눌러 Google Drive, OneDrive, Dropbox의 동기화 폴더로 지정하세요.

2) 스마트폰에서 확인 및 편집:
   - 스마트폰의 Google Drive 앱 또는 마크다운/텍스트 편집기(Markor, Obsidian, QuickEdit 등)로 메모를 언제 어디서나 열고 편집할 수 있습니다.

3) 실시간 외부 감지 지원:
   - 외부 기기에서 추가·수정된 파일도 실시간으로 자동 반영됩니다 (언제든 F5로 수동 새로고침 가능).";
            }
        }

        public string GetFindNoMatch() => IsEnglish ? "No matching text found." : "일치하는 단어가 없습니다.";
        public string GetFindEmptyDoc() => IsEnglish ? "Document is empty." : "문서가 비어 있습니다.";
        public string GetFindLocation(int pos) => IsEnglish ? $"Match at character {pos}" : $"위치: {pos}번째 문자";
        public string GetFindReplaced() => IsEnglish ? "Replaced." : "바꾸었습니다.";
        public string GetFindReplacedAll(int count) => IsEnglish ? $"Replaced {count} occurrence(s)." : $"총 {count}개 항목을 바꾸었습니다.";
    }
}
