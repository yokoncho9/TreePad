using System;
using System.Globalization;
using System.Windows;

namespace TreePad.Services
{
    public class LocalizationService
    {
        private static LocalizationService? _instance;
        public static LocalizationService Instance => _instance ??= new LocalizationService();

        private string _languageSetting = "Auto";
        private string _activeLanguage = "ko";

        public string LanguageSetting => _languageSetting;
        public string ActiveLanguage => _activeLanguage;

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
                string dictUri = $"/TreePad;component/Resources/Strings.{_activeLanguage}.xaml";
                var newDict = new ResourceDictionary { Source = new Uri(dictUri, UriKind.RelativeOrAbsolute) };

                // 기존 언어 사전 교체
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
                if (Application.Current.Resources.Contains(key))
                {
                    return Application.Current.Resources[key]?.ToString() ?? key;
                }
            }
            catch { }
            return key;
        }

        // ==========================================
        // 🌟 다국어 대화상자 & 시스템 메시지 헬퍼 🌟
        // ==========================================

        public string GetDeleteConfirmMessage(string targetName, bool isFolder)
        {
            if (_activeLanguage == "en")
            {
                string type = isFolder ? "folder and all its contents" : "note";
                return $"Are you sure you want to move '{targetName}' {type} to the Recycle Bin?\n(You can restore it anytime from the Recycle Bin)";
            }
            else
            {
                string type = isFolder ? "폴더 및 하위 모든 내용" : "메모";
                return $"정말로 '{targetName}' {type}을(를) 휴지통으로 이동하시겠습니까?\n(언제든 Windows 휴지통에서 복원할 수 있습니다)";
            }
        }

        public string GetDeleteConfirmTitle() => _activeLanguage == "en" ? "Move to Recycle Bin" : "휴지통으로 이동";
        public string GetErrorTitle() => _activeLanguage == "en" ? "Error" : "오류";
        public string GetDeleteFailedMessage(string msg) => _activeLanguage == "en" ? $"Delete failed: {msg}" : $"삭제 실패: {msg}";
        public string GetMoveFailedTitle() => _activeLanguage == "en" ? "Move Notice" : "이동 안내";
        public string GetMoveFailedMessage(string msg) => _activeLanguage == "en" ? $"Failed to move item: {msg}" : $"항목 이동 실패: {msg}";

        public string GetAboutTitle() => _activeLanguage == "en" ? "About TreePad" : "TreePad 정보";
        public string GetAboutContent()
        {
            if (_activeLanguage == "en")
            {
                return 
@"🌲 TreePad v1.0.0

A minimal, hierarchical plain-text notepad for Windows 11.
Gruvbox Material Theme applied.

• Developer: yokoncho9
• GitHub: https://github.com/yokoncho9/TreePad
• License: MIT License

Experience fast, distraction-free writing with tree organization and zero-save session restore.";
            }
            else
            {
                return 
@"🌲 TreePad v1.0.0

나만의 윈도우 11 순수 텍스트 트리 메모장
구글 안티그래비티 Gruvbox Material 테마 적용

• 개발자: yokoncho9
• 깃허브: https://github.com/yokoncho9/TreePad
• 라이선스: MIT License

군더더기 없는 가벼운 속도와 계층적 트리 메모, 무손실 자동 세션 복원을 경험해 보세요.";
            }
        }

        public string GetCloudGuideTitle() => _activeLanguage == "en" ? "Cloud Sync Guide" : "스마트폰(안드로이드) 및 클라우드 동기화 안내";
        public string GetCloudGuideContent()
        {
            if (_activeLanguage == "en")
            {
                return 
@"📱 Smartphone (Android) & Cloud Sync Guide

All TreePad data is stored directly on your disk as standard UTF-8 .txt files and folders.

1) Set your cloud sync folder:
   - Click [Change Storage...] in the menu.
   - Choose a folder inside your Google Drive, OneDrive, or Dropbox.

2) Edit on mobile:
   - Open and edit notes on your smartphone using Google Drive or text editor apps (Markor, QuickEdit, etc.).

3) Seamless zero-save sync:
   - Changes sync across devices via standard cloud storage without proprietary databases!";
            }
            else
            {
                return 
@"📱 스마트폰(안드로이드) 및 클라우드 동기화 안내

TreePad의 모든 데이터는 실제 디스크의 폴더와 표준 UTF-8 .txt 파일로 저장됩니다.

1) 클라우드 드라이브 동기화 폴더 설정:
   - 메뉴의 [저장 위치 변경...]을 눌러 Google Drive, OneDrive, Dropbox의 동기화 폴더로 지정하세요.

2) 스마트폰(안드로이드)에서 확인 및 편집:
   - 스마트폰의 Google Drive 앱 또는 텍스트 뷰어(Markor, QuickEdit 등)로 메모를 언제 어디서나 열고 편집할 수 있습니다.

3) 무손실 실시간 동기화:
   - 특정 데이터베이스에 갇히지 않고 표준 텍스트 파일로 자유롭게 동기화됩니다!";
            }
        }

        public string GetFindNoMatch() => _activeLanguage == "en" ? "No matching text found." : "일치하는 단어가 없습니다.";
        public string GetFindEmptyDoc() => _activeLanguage == "en" ? "Document is empty." : "문서가 비어 있습니다.";
        public string GetFindLocation(int pos) => _activeLanguage == "en" ? $"Position: character {pos}" : $"위치: {pos}번째 문자";
        public string GetFindReplaced() => _activeLanguage == "en" ? "Replaced." : "바꾸었습니다.";
        public string GetFindReplacedAll(int count) => _activeLanguage == "en" ? $"Replaced {count} occurrence(s)." : $"총 {count}개 항목을 바꾸었습니다.";

        public string GetSaveStatusReady() => _activeLanguage == "en" ? "Ready" : "준비";
        public string GetSaveStatusSaving() => _activeLanguage == "en" ? "Saving..." : "저장 중...";
        public string GetSaveStatusSaved() => _activeLanguage == "en" ? "Saved" : "저장됨";

        public string GetWordWrapTooltip(bool isWrap)
        {
            if (_activeLanguage == "en")
                return isWrap ? "Word Wrap: On (Alt+Z)" : "Word Wrap: Off (Alt+Z)";
            else
                return isWrap ? "자동 줄바꿈: 켜짐 (Alt+Z)" : "자동 줄바꿈: 꺼짐 (Alt+Z)";
        }

        public string GetCharCountFormat(int total, int noSpaces)
        {
            if (_activeLanguage == "en")
                return $"{total:N0} chars (no spaces {noSpaces:N0})";
            else
                return $"{total:N0}자 (공백제외 {noSpaces:N0}자)";
        }
    }
}
