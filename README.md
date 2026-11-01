# 🌲 TreePad

> **A minimal, distraction-free hierarchical plain-text notepad for Windows 11.**  
> *Windows 11 전용 미니멀 계층형 순수 텍스트 트리 메모장*

[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Windows 11](https://img.shields.io/badge/Platform-Windows%2011%20x64-0078D4?logo=windows)](https://microsoft.com/windows)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Languages](https://img.shields.io/badge/Language-English%20%7C%20한국어-success.svg)]()

---

## 🌟 Highlights

- **🌲 Hierarchical Tree Structure**: Organize your ideas and documents in folders and notes with drag-and-drop reordering.
- **⚡ Zero-Save & Instant Restore**: No "Save" dialogs. Typing auto-saves in 300ms, and your open tabs, window size, and exact cursor position restore on relaunch.
- **🎨 Eye-Pleasing Gruvbox Theme**: Comfortable warm vanilla paper background (`#FCF8EC`) designed for hours of writing without eye strain.
- **☁️ Cloud & Mobile Friendly**: Notes are stored directly as standard folders and UTF-8 `.txt` files on your disk. Sync effortlessly with **Google Drive**, **OneDrive**, or **Dropbox** to read and write on Android/iOS (using Markor, QuickEdit, etc.).
- **🌐 Bilingual Support**: Seamlessly switch between **English** and **한국어 (Korean)**. Defaults automatically to your system language.
- **🗑️ Safe Delete**: Deleted items go to the Windows **Recycle Bin**, so you never lose important notes by accident.
- **🔍 Fast Find & Replace**: Built-in `Ctrl+F` (Find) and `Ctrl+H` (Find & Replace) with instant match count.
- **📦 Single-File Executable**: Runs as a self-contained `.exe` with zero installation or runtimes required.

---

## ⌨️ Shortcuts

| Shortcut | Description (English) | 설명 (한국어) |
|:---:|:---|:---|
| `Ctrl + N` | Create a new note | 새 메모 만들기 |
| `Ctrl + Shift + N` | Create a new folder | 새 폴더 만들기 |
| `F2` | Rename selected item | 이름 바꾸기 |
| `Delete` | Move item to Recycle Bin | 휴지통으로 안전 삭제 |
| `Ctrl + F` | Open Find bar | 텍스트 찾기 |
| `Ctrl + H` | Open Find & Replace | 찾아 바꾸기 |
| `F3` / `Shift + F3` | Next / Previous match | 다음 / 이전 일치 단어 |
| `Alt + Z` | Toggle Word Wrap | 자동 줄바꿈 토글 |
| `Ctrl + B` | Toggle Sidebar | 사이드바 접기 / 펼치기 |
| `Ctrl + W` | Close active tab | 현재 탭 닫기 |
| `Ctrl + Wheel` | Zoom in / out font size | 글꼴 크기 확대 / 축소 |

---

## 📥 Download & Usage

1. Download the latest `TreePad.exe` from the [Releases](https://github.com/yokoncho9/TreePad/releases) tab.
2. Double-click `TreePad.exe` to run. No setup or installation is required.

---

## 🛠️ Building from Source

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 10/11 64-bit

### Build & Run
```bash
git clone https://github.com/yokoncho9/TreePad.git
cd TreePad
dotnet build -c Release
```

### Publish Single-File Executable
```bash
dotnet publish src/TreePad/TreePad.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

---

## 🇰🇷 한국어 소개 (About for Korean Users)

**TreePad**는 Windows 11 기본 메모장의 군더더기 없는 가벼움에 **폴더/노트 트리 계층 구조**와 **클라우드 연동성**을 더한 순수 텍스트 메모장입니다.

### 핵심 강점
1. **표준 텍스트 기반**: 전용 데이터베이스에 갇히지 않고 실제 폴더와 UTF-8 `.txt` 파일로 저장되므로 구글 드라이브나 스마트폰에서도 자유롭게 열고 편집할 수 있습니다.
2. **자동 세션 복원**: 저장 버튼을 신경 쓸 필요 없이 타이핑 즉시 저장되며, 창을 닫았다가 다시 열면 작업하던 탭과 커서 위치가 100% 그대로 복원됩니다.
3. **편안한 가독성**: 장시간 집중해도 눈이 피로하지 않은 따뜻한 바닐라 크림 종이 테마를 제공합니다.
4. **마우스 드래그 이동 & 휴지통 보호**: 트리 항목을 끌어다 놓아 손쉽게 분류하고, 삭제 시 윈도우 휴지통으로 이동하여 안전합니다.

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).
Copyright (c) 2026 yokoncho9.
