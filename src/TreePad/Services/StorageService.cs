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
        private long _suppressWatcherUntilTicks = 0;

        public string RootDirectory => _rootDirectory;

        public bool IsWatcherSuppressed => DateTime.UtcNow.Ticks < Interlocked.Read(ref _suppressWatcherUntilTicks);

        public void SuppressWatcher(int milliseconds = 1200)
        {
            long targetTicks = DateTime.UtcNow.AddMilliseconds(milliseconds).Ticks;
            long current;
            do
            {
                current = Interlocked.Read(ref _suppressWatcherUntilTicks);
                if (current >= targetTicks) break;
            }
            while (Interlocked.CompareExchange(ref _suppressWatcherUntilTicks, targetTicks, current) != current);
        }

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
            SuppressWatcher(2000);
            Directory.CreateDirectory(newPath);
            _rootDirectory = newPath;
            EnsureStorageInitialized();
        }

        public static bool IsSupportedNoteFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return false;
            string fileName = Path.GetFileName(filePath);
            if (string.IsNullOrEmpty(fileName) || fileName.StartsWith('.') || fileName.StartsWith("~$"))
                return false;

            string ext = Path.GetExtension(fileName);
            return string.Equals(ext, ".txt", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(ext, ".md", StringComparison.OrdinalIgnoreCase);
        }

        public static string GetNoteDisplayName(string filePath)
        {
            string ext = Path.GetExtension(filePath);
            if (string.Equals(ext, ".txt", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFileNameWithoutExtension(filePath);
            }
            return Path.GetFileName(filePath);
        }

        private void EnsureStorageInitialized()
        {
            try
            {
                if (!Directory.Exists(_rootDirectory))
                {
                    Directory.CreateDirectory(_rootDirectory);
                }

                // 폴더가 완전히 비어 있을 때만 시작 환영 메모 및 예시 폴더 생성
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
                SuppressWatcher(2000);
                var loc = LocalizationService.Instance;

                string welcomeFile = Path.Combine(_rootDirectory, loc.GetWelcomeFileName());
                File.WriteAllText(welcomeFile, loc.GetWelcomeNoteContent(), Encoding.UTF8);

                string sampleDir = Path.Combine(_rootDirectory, loc.GetWelcomeSampleFolderName());
                Directory.CreateDirectory(sampleDir);

                string projectFile = Path.Combine(sampleDir, loc.GetWelcomeSampleNoteName());
                File.WriteAllText(projectFile, loc.GetWelcomeSampleNoteContent(), Encoding.UTF8);
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
                // 하위 폴더 탐색 (숨김/시스템 및 '.'으로 시작하는 내부 폴더 제외)
                var subDirs = Directory.GetDirectories(currentDir);
                foreach (var dir in subDirs)
                {
                    string dirName = Path.GetFileName(dir);
                    if (string.IsNullOrEmpty(dirName) || dirName.StartsWith('.')) continue;

                    try
                    {
                        var attr = File.GetAttributes(dir);
                        if ((attr & FileAttributes.Hidden) != 0 || (attr & FileAttributes.System) != 0)
                            continue;
                    }
                    catch { }

                    string relPath = Path.GetRelativePath(_rootDirectory, dir);
                    var folderItem = new TreeItemViewModel(dirName, dir, relPath, true, parent)
                    {
                        IsExpanded = expandedPaths.Contains(relPath)
                    };

                    targetList.Add(folderItem);
                    LoadDirectoryRecursive(dir, folderItem, folderItem.Children, expandedPaths);
                    folderItem.SortChildren();
                }

                // 하위 텍스트 파일 (.txt 및 .md) 탐색
                var files = Directory.GetFiles(currentDir);
                foreach (var file in files)
                {
                    if (!IsSupportedNoteFile(file)) continue;

                    string displayName = GetNoteDisplayName(file);
                    string relPath = Path.GetRelativePath(_rootDirectory, file);
                    var fileItem = new TreeItemViewModel(displayName, file, relPath, false, parent);
                    targetList.Add(fileItem);
                }

                // 정렬 (폴더 우선, 그 후 이름순)
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

        public string ReadNoteContentSync(string filePath)
        {
            if (!File.Exists(filePath)) return string.Empty;

            _fileLock.Wait();
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                return reader.ReadToEnd();
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public async Task<string> ReadNoteContentAsync(string filePath)
        {
            if (!File.Exists(filePath)) return string.Empty;

            await _fileLock.WaitAsync().ConfigureAwait(false);
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                return await reader.ReadToEndAsync().ConfigureAwait(false);
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

        public void SaveNoteContentSync(string filePath, string content)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;

            _fileLock.Wait();
            try
            {
                SuppressWatcher();
                string? dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string tempFile = filePath + ".tmp";
                File.WriteAllText(tempFile, content, Encoding.UTF8);
                File.Move(tempFile, filePath, overwrite: true);
                SuppressWatcher();
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public async Task SaveNoteContentAsync(string filePath, string content)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;

            await _fileLock.WaitAsync().ConfigureAwait(false);
            try
            {
                SuppressWatcher();
                string? dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string tempFile = filePath + ".tmp";
                await File.WriteAllTextAsync(tempFile, content, Encoding.UTF8).ConfigureAwait(false);
                File.Move(tempFile, filePath, overwrite: true);
                SuppressWatcher();
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public string CreateNote(string parentDir, string? title = null)
        {
            SuppressWatcher();
            string targetDir = string.IsNullOrWhiteSpace(parentDir) || !Directory.Exists(parentDir)
                ? _rootDirectory
                : parentDir;

            string defaultTitle = LocalizationService.Instance.GetDefaultNoteName();
            string safeTitle = GetSafeFileName(string.IsNullOrWhiteSpace(title) ? defaultTitle : title);
            string baseFileName = string.IsNullOrWhiteSpace(safeTitle) ? defaultTitle : safeTitle;

            string ext = ".txt";
            if (baseFileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                ext = ".md";
                baseFileName = Path.GetFileNameWithoutExtension(baseFileName);
            }
            else if (baseFileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                baseFileName = Path.GetFileNameWithoutExtension(baseFileName);
            }

            string filePath = Path.Combine(targetDir, $"{baseFileName}{ext}");
            int counter = 1;

            while (File.Exists(filePath))
            {
                filePath = Path.Combine(targetDir, $"{baseFileName} ({counter}){ext}");
                counter++;
            }

            File.WriteAllText(filePath, string.Empty, Encoding.UTF8);
            SuppressWatcher();
            return filePath;
        }

        public string CreateFolder(string parentDir, string? name = null)
        {
            SuppressWatcher();
            string targetDir = string.IsNullOrWhiteSpace(parentDir) || !Directory.Exists(parentDir)
                ? _rootDirectory
                : parentDir;

            string defaultName = LocalizationService.Instance.GetDefaultFolderName();
            string safeName = GetSafeFileName(string.IsNullOrWhiteSpace(name) ? defaultName : name);
            string baseName = string.IsNullOrWhiteSpace(safeName) ? defaultName : safeName;
            string dirPath = Path.Combine(targetDir, baseName);
            int counter = 1;

            while (Directory.Exists(dirPath))
            {
                dirPath = Path.Combine(targetDir, $"{baseName} ({counter})");
                counter++;
            }

            Directory.CreateDirectory(dirPath);
            SuppressWatcher();
            return dirPath;
        }

        public string RenameItem(string fullPath, string newName, bool isFolder)
        {
            string safeName = GetSafeFileName(newName);
            if (string.IsNullOrWhiteSpace(safeName)) return fullPath;

            string parentDir = Path.GetDirectoryName(fullPath)!;
            string newPath;

            if (isFolder)
            {
                newPath = Path.Combine(parentDir, safeName);
            }
            else
            {
                if (safeName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
                    safeName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                {
                    newPath = Path.Combine(parentDir, safeName);
                }
                else
                {
                    string origExt = Path.GetExtension(fullPath);
                    if (string.IsNullOrEmpty(origExt)) origExt = ".txt";
                    newPath = Path.Combine(parentDir, $"{safeName}{origExt}");
                }
            }

            if (string.Equals(fullPath, newPath, StringComparison.OrdinalIgnoreCase))
            {
                // 대소문자만 변경된 경우 허용 (임시 경로 경유)
                if (!string.Equals(fullPath, newPath, StringComparison.Ordinal))
                {
                    SuppressWatcher();
                    string tempRename = fullPath + ".renametmp_" + Guid.NewGuid().ToString("N")[..6];
                    if (isFolder)
                    {
                        Directory.Move(fullPath, tempRename);
                        Directory.Move(tempRename, newPath);
                    }
                    else
                    {
                        File.Move(fullPath, tempRename);
                        File.Move(tempRename, newPath);
                    }
                    SuppressWatcher();
                    return newPath;
                }
                return fullPath;
            }

            SuppressWatcher();
            if (isFolder)
            {
                if (Directory.Exists(newPath))
                    throw new InvalidOperationException(LocalizationService.Instance.GetDuplicateFolderError());
                Directory.Move(fullPath, newPath);
            }
            else
            {
                if (File.Exists(newPath))
                    throw new InvalidOperationException(LocalizationService.Instance.GetDuplicateNoteError());
                File.Move(fullPath, newPath);
            }
            SuppressWatcher();

            return newPath;
        }

        public void DeleteItem(string fullPath, bool isFolder)
        {
            SuppressWatcher(2000);
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
                if (isFolder && Directory.Exists(fullPath))
                {
                    Directory.Delete(fullPath, true);
                }
                else if (!isFolder && File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }
            }
            finally
            {
                SuppressWatcher(1500);
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
                return sourcePath;
            }

            SuppressWatcher();
            if (isFolder)
            {
                string normalizedSource = Path.GetFullPath(sourcePath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string normalizedTarget = Path.GetFullPath(targetDirPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (normalizedTarget.StartsWith(normalizedSource, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(LocalizationService.Instance.GetCannotMoveIntoSubfolderError());
                }

                if (Directory.Exists(destinationPath))
                {
                    throw new InvalidOperationException(LocalizationService.Instance.GetDuplicateFolderError(itemName));
                }

                Directory.Move(sourcePath, destinationPath);
            }
            else
            {
                if (File.Exists(destinationPath))
                {
                    throw new InvalidOperationException(LocalizationService.Instance.GetDuplicateNoteError(itemName));
                }

                File.Move(sourcePath, destinationPath);
            }
            SuppressWatcher();

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
