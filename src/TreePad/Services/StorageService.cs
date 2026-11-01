using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TreePad.ViewModels;

namespace TreePad.Services
{
    public class StorageService
    {
        private string _rootDirectory;
        private readonly SemaphoreSlim _fileLock = new(1, 1);

        public string RootDirectory => _rootDirectory;

        public StorageService(string? customPath = null)
        {
            if (string.IsNullOrWhiteSpace(customPath) || !Directory.Exists(customPath))
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                _rootDirectory = Path.Combine(appData, "TreePad", "Notes");
            }
            else
            {
                _rootDirectory = customPath;
            }

            EnsureStorageInitialized();
        }

        public void SetRootDirectory(string newPath)
        {
            if (string.IsNullOrWhiteSpace(newPath)) return;
            Directory.CreateDirectory(newPath);
            _rootDirectory = newPath;
            EnsureStorageInitialized();
        }

        private void EnsureStorageInitialized()
        {
            try
            {
                if (!Directory.Exists(_rootDirectory))
                {
                    Directory.CreateDirectory(_rootDirectory);
                }

                // 폴더가 비어 있으면 시작 환영 메모 및 예시 폴더 생성
                if (Directory.GetFileSystemEntries(_rootDirectory).Length == 0)
                {
                    CreateInitialWelcomeData();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Storage init error: {ex.Message}");
            }
        }

        private void CreateInitialWelcomeData()
        {
            try
            {
                string welcomeFile = Path.Combine(_rootDirectory, "환영합니다 (TreePad 사용법).txt");
                string welcomeText =
@"# 🌲 TreePad - 나만의 트리형 텍스트 메모장

TreePad에 오신 것을 환영합니다!
Windows 11 64-bit 전용으로 제작된 가볍고 군더더기 없는 순수 텍스트 메모장입니다.

======================================================================
1. 핵심 특징
======================================================================
• 순수 텍스트 (Plain Text):
  - 서식 없는 깔끔한 텍스트만 지원하여 속도가 빠르고 눈이 편안합니다.
• 무손실 자동 세션 (Zero-Save):
  - 별도로 저장 버튼을 누를 필요가 없습니다.
  - 타이핑하는 즉시 자동으로 저장되며, 프로그램을 닫았다가 다시 열면
    마지막에 보고 있던 메모와 커서 위치가 100% 그대로 복원됩니다.
• 무설치 단일 실행 파일:
  - 프레임워크나 런타임 설치 없이 단일 실행 파일 하나로 어디서나 작동합니다.

======================================================================
2. 주요 단축키
======================================================================
• 새 메모 생성: Ctrl + N
• 새 폴더 생성: Ctrl + Shift + N
• 이름 바꾸기:  F2
• 삭제:        Delete
• 줄바꿈 토글:  Alt + Z
• 글꼴 크기:    Ctrl + 마우스 휠
• 내용 검색:    Ctrl + F

======================================================================
3. 스마트폰 (안드로이드) 및 클라우드 연동 가이드
======================================================================
TreePad의 모든 메모는 사용자 PC의 표준 폴더와 UTF-8 .txt 파일로 저장됩니다.
따라서 다음과 같이 클라우드를 활용하면 모바일에서도 끊김 없이 작업할 수 있습니다:

1) 클라우드 드라이브 동기화 폴더 설정:
   - 상단 메뉴의 [설정 / 저장소 폴더 변경]을 눌러 저장 위치를
     Google Drive, OneDrive, 또는 Dropbox의 동기화 폴더로 지정하세요.
2) 안드로이드 스마트폰에서 확인:
   - 스마트폰의 Google Drive 또는 OneDrive 앱에서 폴더와 .txt 파일을 바로 열고 편집할 수 있습니다.
   - 마크다운/텍스트 뷰어(예: Markor, QuickEdit 등)를 사용하시면 더욱 편리합니다.
3) 향후 전용 안드로이드 앱 배포 시:
   - 동일한 클라우드 폴더를 연동하면 모바일에서도 PC와 동일한 트리 메모장 화면으로 이어갈 수 있습니다!
";

                File.WriteAllText(welcomeFile, welcomeText, Encoding.UTF8);

                // 안내용 예시 폴더 생성
                string sampleDir = Path.Combine(_rootDirectory, "업무 및 아이디어");
                Directory.CreateDirectory(sampleDir);

                string projectFile = Path.Combine(sampleDir, "신규 프로젝트 아이디어.txt");
                string projectText =
@"프로젝트 아이디어 메모:
1. 심플한 인터페이스 유지
2. 지연 없는 반응 속도
3. 신뢰할 수 있는 데이터 보관";
                File.WriteAllText(projectFile, projectText, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create welcome data: {ex.Message}");
            }
        }

        public List<TreeItemViewModel> LoadTree(HashSet<string> expandedPaths)
        {
            var rootItems = new List<TreeItemViewModel>();
            if (!Directory.Exists(_rootDirectory)) return rootItems;

            LoadDirectoryRecursive(_rootDirectory, null, rootItems, expandedPaths);
            return rootItems;
        }

        private void LoadDirectoryRecursive(string currentDir, TreeItemViewModel? parent, IList<TreeItemViewModel> targetList, HashSet<string> expandedPaths)
        {
            try
            {
                // 하위 폴더 탐색
                var subDirs = Directory.GetDirectories(currentDir);
                foreach (var dir in subDirs)
                {
                    string dirName = Path.GetFileName(dir);
                    string relPath = Path.GetRelativePath(_rootDirectory, dir);
                    var folderItem = new TreeItemViewModel(dirName, dir, relPath, true, parent)
                    {
                        IsExpanded = expandedPaths.Contains(relPath)
                    };

                    targetList.Add(folderItem);
                    LoadDirectoryRecursive(dir, folderItem, folderItem.Children, expandedPaths);
                    folderItem.SortChildren();
                }

                // 하위 텍스트 파일 (.txt) 탐색
                var files = Directory.GetFiles(currentDir, "*.txt");
                foreach (var file in files)
                {
                    string fileName = Path.GetFileNameWithoutExtension(file);
                    string relPath = Path.GetRelativePath(_rootDirectory, file);
                    var fileItem = new TreeItemViewModel(fileName, file, relPath, false, parent);
                    targetList.Add(fileItem);
                }

                // 정렬 (폴더 우선, 그 후 가나다순)
                if (targetList is List<TreeItemViewModel> list)
                {
                    list.Sort((a, b) =>
                    {
                        if (a.IsFolder != b.IsFolder)
                        {
                            return a.IsFolder ? -1 : 1;
                        }
                        return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error scanning directory {currentDir}: {ex.Message}");
            }
        }

        public async Task<string> ReadNoteContentAsync(string filePath)
        {
            if (!File.Exists(filePath)) return string.Empty;

            await _fileLock.WaitAsync();
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                return await reader.ReadToEndAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Read error for {filePath}: {ex.Message}");
                return string.Empty;
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public async Task SaveNoteContentAsync(string filePath, string content)
        {
            await _fileLock.WaitAsync();
            try
            {
                string dir = Path.GetDirectoryName(filePath)!;
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string tempFile = filePath + ".tmp";
                await File.WriteAllTextAsync(tempFile, content, Encoding.UTF8);
                File.Move(tempFile, filePath, overwrite: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Save error for {filePath}: {ex.Message}");
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public string CreateNote(string parentDir, string title)
        {
            string targetDir = string.IsNullOrWhiteSpace(parentDir) || !Directory.Exists(parentDir) 
                ? _rootDirectory 
                : parentDir;

            string safeTitle = GetSafeFileName(title);
            string baseFileName = string.IsNullOrWhiteSpace(safeTitle) ? "새 메모" : safeTitle;
            string filePath = Path.Combine(targetDir, $"{baseFileName}.txt");
            int counter = 1;

            while (File.Exists(filePath))
            {
                filePath = Path.Combine(targetDir, $"{baseFileName} ({counter}).txt");
                counter++;
            }

            File.WriteAllText(filePath, "", Encoding.UTF8);
            return filePath;
        }

        public string CreateFolder(string parentDir, string name)
        {
            string targetDir = string.IsNullOrWhiteSpace(parentDir) || !Directory.Exists(parentDir) 
                ? _rootDirectory 
                : parentDir;

            string safeName = GetSafeFileName(name);
            string baseName = string.IsNullOrWhiteSpace(safeName) ? "새 폴더" : safeName;
            string dirPath = Path.Combine(targetDir, baseName);
            int counter = 1;

            while (Directory.Exists(dirPath))
            {
                dirPath = Path.Combine(targetDir, $"{baseName} ({counter})");
                counter++;
            }

            Directory.CreateDirectory(dirPath);
            return dirPath;
        }

        public string RenameItem(string fullPath, string newName, bool isFolder)
        {
            string safeName = GetSafeFileName(newName);
            if (string.IsNullOrWhiteSpace(safeName)) return fullPath;

            string parentDir = Path.GetDirectoryName(fullPath)!;
            string newPath = isFolder 
                ? Path.Combine(parentDir, safeName)
                : Path.Combine(parentDir, $"{safeName}.txt");

            if (string.Equals(fullPath, newPath, StringComparison.OrdinalIgnoreCase))
            {
                return fullPath;
            }

            if (isFolder)
            {
                if (Directory.Exists(newPath)) throw new InvalidOperationException("이미 동일한 이름의 폴더가 존재합니다.");
                Directory.Move(fullPath, newPath);
            }
            else
            {
                if (File.Exists(newPath)) throw new InvalidOperationException("이미 동일한 이름의 메모가 존재합니다.");
                File.Move(fullPath, newPath);
            }

            return newPath;
        }

        public void DeleteItem(string fullPath, bool isFolder)
        {
            try
            {
                if (isFolder)
                {
                    if (Directory.Exists(fullPath))
                    {
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                            fullPath,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    }
                }
                else
                {
                    if (File.Exists(fullPath))
                    {
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                            fullPath,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Recycle bin delete failed, fallback to permanent delete: {ex.Message}");
                // Fallback: 휴지통 지원이 안 되는 드라이브/네트워크 경로의 경우 일반 삭제
                if (isFolder && Directory.Exists(fullPath))
                {
                    Directory.Delete(fullPath, true);
                }
                else if (!isFolder && File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }
            }
        }

        public string MoveItem(string sourcePath, string targetDirPath, bool isFolder)
        {
            if (string.IsNullOrWhiteSpace(targetDirPath) || !Directory.Exists(targetDirPath))
            {
                targetDirPath = _rootDirectory;
            }

            string itemName = Path.GetFileName(sourcePath);
            string destinationPath = Path.Combine(targetDirPath, itemName);

            if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
            {
                return sourcePath; // 동일 위치면 이동 없음
            }

            if (isFolder)
            {
                // 자기 자신의 하위 디렉토리로 이동 방지
                string normalizedSource = Path.GetFullPath(sourcePath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string normalizedTarget = Path.GetFullPath(targetDirPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (normalizedTarget.StartsWith(normalizedSource, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("폴더를 자기 자신의 하위 폴더로 이동할 수 없습니다.");
                }

                if (Directory.Exists(destinationPath))
                {
                    throw new InvalidOperationException($"이미 대상 위치에 동일한 이름의 폴더가 존재합니다: {itemName}");
                }

                Directory.Move(sourcePath, destinationPath);
            }
            else
            {
                if (File.Exists(destinationPath))
                {
                    throw new InvalidOperationException($"이미 대상 위치에 동일한 이름의 메모가 존재합니다: {itemName}");
                }

                File.Move(sourcePath, destinationPath);
            }

            return destinationPath;
        }

        private static string GetSafeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name.Trim();
        }
    }
}
