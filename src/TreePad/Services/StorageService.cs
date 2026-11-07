using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TreePad.ViewModels;

namespace TreePad.Services
{
    public readonly record struct NoteReadResult(string Content, Encoding Encoding);

    public class StorageService
    {
        private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
        private static readonly UTF8Encoding Utf8Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

        private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        private string _rootDirectory;
        private readonly SemaphoreSlim _fileLock = new(1, 1);
        private long _suppressAllWatcherUntilTicks = 0;
        private readonly ConcurrentDictionary<string, long> _suppressedPaths = new(StringComparer.OrdinalIgnoreCase);

        public string RootDirectory => _rootDirectory;

        public bool IsWatcherSuppressed => DateTime.UtcNow.Ticks < Interlocked.Read(ref _suppressAllWatcherUntilTicks);

        public void SuppressWatcher(int milliseconds = 1200)
        {
            long targetTicks = DateTime.UtcNow.AddMilliseconds(milliseconds).Ticks;
            long current;
            do
            {
                current = Interlocked.Read(ref _suppressAllWatcherUntilTicks);
                if (current >= targetTicks) break;
            }
            while (Interlocked.CompareExchange(ref _suppressAllWatcherUntilTicks, targetTicks, current) != current);
        }

        public void SuppressPathWatcher(string path, int milliseconds = 1500)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                string fullPath = Path.GetFullPath(path);
                long targetTicks = DateTime.UtcNow.AddMilliseconds(milliseconds).Ticks;
                _suppressedPaths[fullPath] = targetTicks;
            }
            catch { }
        }

        public bool IsPathSuppressed(string? path)
        {
            if (IsWatcherSuppressed) return true;
            if (string.IsNullOrWhiteSpace(path)) return false;

            try
            {
                string fullPath = Path.GetFullPath(path);
                long now = DateTime.UtcNow.Ticks;
                if (_suppressedPaths.TryGetValue(fullPath, out long untilTicks))
                {
                    if (now < untilTicks) return true;
                    _suppressedPaths.TryRemove(fullPath, out _);
                }
            }
            catch { }

            return false;
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
                File.WriteAllText(welcomeFile, loc.GetWelcomeNoteContent(), Utf8NoBom);

                string sampleDir = Path.Combine(_rootDirectory, loc.GetWelcomeSampleFolderName());
                Directory.CreateDirectory(sampleDir);

                string projectFile = Path.Combine(sampleDir, loc.GetWelcomeSampleNoteName());
                File.WriteAllText(projectFile, loc.GetWelcomeSampleNoteContent(), Utf8NoBom);
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

        /// <summary>
        /// 바이트 배열로부터 BOM(UTF-8/UTF-16) 유무 및 유효한 UTF-8 / 레거시(CP949) 인코딩을 정밀 판별합니다.
        /// 새 파일 및 BOM 없는 UTF-8은 Utf8NoBom을 반환하고, 기존 BOM이나 CP949는 원본 인코딩을 그대로 보존합니다.
        /// </summary>
        private static NoteReadResult DecodeBytesPreservingEncoding(byte[] bytes)
        {
            if (bytes.Length == 0)
            {
                return new NoteReadResult(string.Empty, Utf8NoBom);
            }

            // 1. UTF-8 BOM (EF BB BF)
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                return new NoteReadResult(Utf8WithBom.GetString(bytes, 3, bytes.Length - 3), Utf8WithBom);
            }

            // 2. UTF-16 LE BOM (FF FE)
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                var enc = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
                return new NoteReadResult(enc.GetString(bytes, 2, bytes.Length - 2), enc);
            }

            // 3. UTF-16 BE BOM (FE FF)
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                var enc = new UnicodeEncoding(bigEndian: true, byteOrderMark: true);
                return new NoteReadResult(enc.GetString(bytes, 2, bytes.Length - 2), enc);
            }

            // 4. BOM 없는 순수 UTF-8 검증 (유효하지 않은 바이트 시퀀스가 있으면 CP949 폴백)
            try
            {
                string utf8Text = Utf8Strict.GetString(bytes);
                return new NoteReadResult(utf8Text, Utf8NoBom);
            }
            catch (DecoderFallbackException)
            {
                try
                {
                    Encoding cp949 = Encoding.GetEncoding(949);
                    return new NoteReadResult(cp949.GetString(bytes), cp949);
                }
                catch
                {
                    return new NoteReadResult(Encoding.UTF8.GetString(bytes), Utf8NoBom);
                }
            }
        }

        public NoteReadResult ReadNoteWithEncodingSync(string filePath)
        {
            if (!File.Exists(filePath)) return new NoteReadResult(string.Empty, Utf8NoBom);

            _fileLock.Wait();
            try
            {
                Exception? lastEx = null;
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    try
                    {
                        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        int length = (int)Math.Min(stream.Length, int.MaxValue);
                        byte[] bytes = new byte[length];
                        int totalRead = 0;
                        while (totalRead < length)
                        {
                            int read = stream.Read(bytes, totalRead, length - totalRead);
                            if (read == 0) break;
                            totalRead += read;
                        }
                        if (totalRead != length)
                        {
                            Array.Resize(ref bytes, totalRead);
                        }
                        return DecodeBytesPreservingEncoding(bytes);
                    }
                    catch (IOException ex)
                    {
                        lastEx = ex;
                        if (attempt < 3)
                        {
                            Thread.Sleep(50 * (attempt + 1));
                        }
                    }
                }
                throw lastEx!;
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public string ReadNoteContentSync(string filePath)
        {
            return ReadNoteWithEncodingSync(filePath).Content;
        }

        public async Task<NoteReadResult> ReadNoteWithEncodingAsync(string filePath)
        {
            if (!File.Exists(filePath)) return new NoteReadResult(string.Empty, Utf8NoBom);

            await _fileLock.WaitAsync().ConfigureAwait(false);
            try
            {
                Exception? lastEx = null;
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    try
                    {
                        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        int length = (int)Math.Min(stream.Length, int.MaxValue);
                        byte[] bytes = new byte[length];
                        int totalRead = 0;
                        while (totalRead < length)
                        {
                            int read = await stream.ReadAsync(bytes.AsMemory(totalRead, length - totalRead)).ConfigureAwait(false);
                            if (read == 0) break;
                            totalRead += read;
                        }
                        if (totalRead != length)
                        {
                            Array.Resize(ref bytes, totalRead);
                        }
                        return DecodeBytesPreservingEncoding(bytes);
                    }
                    catch (IOException ex)
                    {
                        lastEx = ex;
                        if (attempt < 3)
                        {
                            await Task.Delay(50 * (attempt + 1)).ConfigureAwait(false);
                        }
                    }
                }
                throw lastEx!;
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public void SaveNoteContentSync(string filePath, string content, Encoding? encoding = null)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;
            Encoding targetEncoding = encoding ?? Utf8NoBom;

            _fileLock.Wait();
            try
            {
                SuppressPathWatcher(filePath, 1500);
                string? dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                Exception? lastEx = null;
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    try
                    {
                        WriteFileContentInPlaceSync(filePath, content, targetEncoding);
                        SuppressPathWatcher(filePath, 1500);
                        return;
                    }
                    catch (IOException ex)
                    {
                        lastEx = ex;
                        if (attempt < 3)
                        {
                            Thread.Sleep(50 * (attempt + 1));
                        }
                    }
                }

                if (lastEx != null) throw lastEx;
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public async Task SaveNoteContentAsync(string filePath, string content, Encoding? encoding = null)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;
            Encoding targetEncoding = encoding ?? Utf8NoBom;

            await _fileLock.WaitAsync().ConfigureAwait(false);
            try
            {
                SuppressPathWatcher(filePath, 1500);
                string? dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                Exception? lastEx = null;
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    try
                    {
                        await WriteFileContentInPlaceAsync(filePath, content, targetEncoding).ConfigureAwait(false);
                        SuppressPathWatcher(filePath, 1500);
                        return;
                    }
                    catch (IOException ex)
                    {
                        lastEx = ex;
                        if (attempt < 3)
                        {
                            await Task.Delay(50 * (attempt + 1)).ConfigureAwait(false);
                        }
                    }
                }

                if (lastEx != null) throw lastEx;
            }
            finally
            {
                _fileLock.Release();
            }
        }

        private static void WriteFileContentInPlaceSync(string filePath, string content, Encoding encoding)
        {
            // 인코딩 변환 시 CP949 등에서 표현 불가능한 유니코드 문자(이모지 등)가 추가된 경우 안전하게 UTF-8(No BOM)로 승격
            byte[] preamble = encoding.GetPreamble();
            byte[] bodyBytes;
            try
            {
                var strictEncoding = Encoding.GetEncoding(
                    encoding.CodePage,
                    EncoderFallback.ExceptionFallback,
                    DecoderFallback.ReplacementFallback);
                bodyBytes = strictEncoding.GetBytes(content);
            }
            catch (EncoderFallbackException)
            {
                preamble = Array.Empty<byte>();
                bodyBytes = Utf8NoBom.GetBytes(content);
            }

            // NTFS 파일 ID 및 클라우드 드라이브 메타데이터를 보존하기 위해 In-Place 스트림 기록 사용
            using var stream = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
            stream.SetLength(0);
            if (preamble.Length > 0)
            {
                stream.Write(preamble, 0, preamble.Length);
            }
            if (bodyBytes.Length > 0)
            {
                stream.Write(bodyBytes, 0, bodyBytes.Length);
            }
            stream.Flush(flushToDisk: true);
        }

        private static async Task WriteFileContentInPlaceAsync(string filePath, string content, Encoding encoding)
        {
            byte[] preamble = encoding.GetPreamble();
            byte[] bodyBytes;
            try
            {
                var strictEncoding = Encoding.GetEncoding(
                    encoding.CodePage,
                    EncoderFallback.ExceptionFallback,
                    DecoderFallback.ReplacementFallback);
                bodyBytes = strictEncoding.GetBytes(content);
            }
            catch (EncoderFallbackException)
            {
                preamble = Array.Empty<byte>();
                bodyBytes = Utf8NoBom.GetBytes(content);
            }

            using var stream = new FileStream(filePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read, bufferSize: 4096, useAsync: true);
            stream.SetLength(0);
            if (preamble.Length > 0)
            {
                await stream.WriteAsync(preamble.AsMemory()).ConfigureAwait(false);
            }
            if (bodyBytes.Length > 0)
            {
                await stream.WriteAsync(bodyBytes.AsMemory()).ConfigureAwait(false);
            }
            await stream.FlushAsync().ConfigureAwait(false);
        }

        public string CreateNote(string parentDir, string? title = null)
        {
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

            baseFileName = SanitizeReservedBaseName(baseFileName, defaultTitle);

            string filePath = Path.Combine(targetDir, $"{baseFileName}{ext}");
            int counter = 1;

            while (File.Exists(filePath))
            {
                filePath = Path.Combine(targetDir, $"{baseFileName} ({counter}){ext}");
                counter++;
            }

            SuppressPathWatcher(filePath, 1500);
            File.WriteAllText(filePath, string.Empty, Utf8NoBom);
            SuppressPathWatcher(filePath, 1500);
            return filePath;
        }

        public string CreateFolder(string parentDir, string? name = null)
        {
            string targetDir = string.IsNullOrWhiteSpace(parentDir) || !Directory.Exists(parentDir)
                ? _rootDirectory
                : parentDir;

            string defaultName = LocalizationService.Instance.GetDefaultFolderName();
            string safeName = GetSafeFileName(string.IsNullOrWhiteSpace(name) ? defaultName : name);
            string baseName = SanitizeReservedBaseName(string.IsNullOrWhiteSpace(safeName) ? defaultName : safeName, defaultName);
            string dirPath = Path.Combine(targetDir, baseName);
            int counter = 1;

            while (Directory.Exists(dirPath))
            {
                dirPath = Path.Combine(targetDir, $"{baseName} ({counter})");
                counter++;
            }

            SuppressPathWatcher(dirPath, 1500);
            Directory.CreateDirectory(dirPath);
            SuppressPathWatcher(dirPath, 1500);
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
                    string stem = SanitizeReservedBaseName(Path.GetFileNameWithoutExtension(safeName), "_");
                    string ext = Path.GetExtension(safeName);
                    newPath = Path.Combine(parentDir, $"{stem}{ext}");
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
                    SuppressWatcher(1500);
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
                    SuppressWatcher(1500);
                    return newPath;
                }
                return fullPath;
            }

            SuppressWatcher(1500);
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
            SuppressWatcher(1500);

            return newPath;
        }

        /// <summary>
        /// 항목을 Windows 휴지통으로 안전하게 이동합니다.
        /// 휴지통을 지원하지 않는 드라이브(네트워크/가상 클라우드 드라이브 등)에서 실패할 경우 절대 자동으로 영구 삭제하지 않고 false를 반환합니다.
        /// </summary>
        public bool TryMoveToRecycleBin(string fullPath, bool isFolder, out Exception? failureReason)
        {
            failureReason = null;
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
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,
                            Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
                    }
                }
                else
                {
                    if (File.Exists(fullPath))
                    {
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                            fullPath,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,
                            Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
                    }
                }
                return true;
            }
            catch (OperationCanceledException)
            {
                // 사용자가 Windows Shell 대화상자에서 직접 취소를 누른 경우
                failureReason = null;
                return false;
            }
            catch (Exception ex)
            {
                failureReason = ex;
                return false;
            }
            finally
            {
                SuppressWatcher(1500);
            }
        }

        public void DeletePermanently(string fullPath, bool isFolder)
        {
            SuppressWatcher(2000);
            try
            {
                if (isFolder && Directory.Exists(fullPath))
                {
                    Directory.Delete(fullPath, recursive: true);
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

            SuppressWatcher(1500);
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
            SuppressWatcher(1500);

            return destinationPath;
        }

        private static string SanitizeReservedBaseName(string candidate, string fallback)
        {
            string trimmed = candidate.Trim().TrimEnd('.');
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return fallback;
            }

            // CON, PRN, AUX, NUL, COM1..9, LPT1..9 예약어 방어
            string stemOnly = Path.GetFileNameWithoutExtension(trimmed);
            if (WindowsReservedNames.Contains(trimmed) || WindowsReservedNames.Contains(stemOnly))
            {
                return $"_{trimmed}";
            }

            return trimmed;
        }

        private static string GetSafeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            // Windows NTFS에서 파일/폴더 이름 끝의 공백이나 마침표('.')는 경로 오류를 유발하므로 제거
            string cleaned = name.Trim().TrimEnd('.');
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return string.Empty;
            }

            return SanitizeReservedBaseName(cleaned, "_");
        }
    }
}
