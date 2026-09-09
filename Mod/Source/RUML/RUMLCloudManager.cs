using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Threading;
using RimWorld;
using UnityEngine;
using Verse;

namespace RUML
{
    public class CloudTranslationItem
    {
        public string Id = "";
        public string ModName = "";
        public string PackageId = "";
        public string Author = "";
        public string Version = "1.0";
        public string Hash = "";
        public string LocalVersion = "";
        public string LocalHash = "";
        public bool HasUpdate = false;
        public string DownloadUrl = "";
        public string Description = "";
        public string TargetFolder = "";
        public string ModFolder = "";
        public string AuthorFolder = "";
        public bool IsInstalled = false;
        public bool IsTargetModActive = false;
    }

    public class CloudModGroup
    {
        public string ModName = "";
        public string PackageId = "";
        public List<CloudTranslationItem> Versions = new List<CloudTranslationItem>();
    }

    public static class RUMLCloudManager
    {
        public static List<CloudTranslationItem> Items = new List<CloudTranslationItem>();
        public static bool IsBusy = false;
        public static string StatusMessage = "Готов к синхронизации";
        public static float DownloadPercent = 0f;
        public static string CurrentActionItem = "";

        public static Dictionary<string, int> GetAuthorCounts()
        {
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < Items.Count; i++)
            {
                string a = Items[i].Author;
                if (string.IsNullOrEmpty(a)) a = "Неизвестный";
                if (!counts.ContainsKey(a))
                {
                    counts[a] = 0;
                }
                counts[a]++;
            }
            return counts;
        }

        public static List<CloudModGroup> GetGroupedItems(string authorFilter = "", string searchFilter = "")
        {
            Dictionary<string, CloudModGroup> map = new Dictionary<string, CloudModGroup>(StringComparer.OrdinalIgnoreCase);
            List<CloudModGroup> result = new List<CloudModGroup>();

            for (int i = 0; i < Items.Count; i++)
            {
                CloudTranslationItem item = Items[i];

                if (!string.IsNullOrEmpty(authorFilter) && !string.Equals(item.Author, authorFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(searchFilter))
                {
                    bool match = (item.ModName != null && item.ModName.IndexOf(searchFilter, StringComparison.OrdinalIgnoreCase) >= 0) ||
                                 (item.Author != null && item.Author.IndexOf(searchFilter, StringComparison.OrdinalIgnoreCase) >= 0) ||
                                 (item.PackageId != null && item.PackageId.IndexOf(searchFilter, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!match) continue;
                }

                string key = string.IsNullOrEmpty(item.PackageId) ? item.ModName : item.PackageId;
                if (string.IsNullOrEmpty(key)) key = item.Id;

                CloudModGroup grp;
                if (!map.TryGetValue(key, out grp))
                {
                    grp = new CloudModGroup();
                    grp.ModName = item.ModName;
                    grp.PackageId = item.PackageId;
                    map[key] = grp;
                    result.Add(grp);
                }
                grp.Versions.Add(item);
            }

            return result;
        }

        public static void OpenTranslationsFolderInExplorer()
        {
            try
            {
                string dir = RUMLFolderManager.GetExternalTranslationsDir();
                if (Directory.Exists(dir))
                {
                    Application.OpenURL("file://" + dir.Replace('\\', '/'));
                }
            }
            catch (Exception ex)
            {
                Log.Error("[RUML] Failed to open translations folder: " + ex);
            }
        }

        public static void RefreshLocalState(ModContentPack content)
        {
            string extDir = RUMLFolderManager.GetExternalTranslationsDir();

            for (int i = 0; i < Items.Count; i++)
            {
                CloudTranslationItem item = Items[i];
                string targetDir = Path.Combine(Path.Combine(extDir, item.ModFolder), item.AuthorFolder);
                item.IsInstalled = Directory.Exists(targetDir);
                item.HasUpdate = false;
                item.LocalVersion = "";
                item.LocalHash = "";

                if (item.IsInstalled)
                {
                    string infoPath = Path.Combine(targetDir, "info.json");
                    if (File.Exists(infoPath))
                    {
                        try
                        {
                            string text = File.ReadAllText(infoPath);
                            item.LocalVersion = ExtractJsonField(text, "version");
                            item.LocalHash = ExtractJsonField(text, "hash");
                        }
                        catch { }
                    }

                    if (!string.IsNullOrEmpty(item.Hash) && !string.IsNullOrEmpty(item.LocalHash))
                    {
                        item.HasUpdate = !string.Equals(item.Hash, item.LocalHash, StringComparison.OrdinalIgnoreCase);
                    }
                    else if (!string.IsNullOrEmpty(item.Version) && !string.IsNullOrEmpty(item.LocalVersion))
                    {
                        item.HasUpdate = !string.Equals(item.Version, item.LocalVersion, StringComparison.OrdinalIgnoreCase);
                    }
                }

                item.IsTargetModActive = false;
                foreach (ModContentPack running in LoadedModManager.RunningMods)
                {
                    if (string.Equals(running.PackageIdPlayerFacing, item.PackageId, StringComparison.OrdinalIgnoreCase))
                    {
                        item.IsTargetModActive = true;
                        break;
                    }
                }
            }
        }

        public static void SaveInstalledMeta(string targetDir, CloudTranslationItem item)
        {
            if (string.IsNullOrEmpty(targetDir) || item == null) return;
            try
            {
                string infoPath = Path.Combine(targetDir, "info.json");
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine("  \"packageId\": \"" + (item.PackageId ?? "") + "\",");
                sb.AppendLine("  \"version\": \"" + (item.Version ?? "1.0.0") + "\",");
                sb.AppendLine("  \"hash\": \"" + (item.Hash ?? "") + "\",");
                sb.AppendLine("  \"author\": \"" + (item.Author ?? "") + "\",");
                sb.AppendLine("  \"description\": \"" + (item.Description ?? "").Replace("\"", "\\\"") + "\"");
                sb.AppendLine("}");
                File.WriteAllText(infoPath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Log.Warning("[RUML] Failed to save info.json meta: " + ex);
            }
        }

        public static void FetchManifestAsync(string url, ModContentPack content, Action onComplete = null)
        {
            if (IsBusy) return;
            IsBusy = true;
            StatusMessage = "Загрузка каталога с GitHub...";

            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                try
                {
                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768 | SecurityProtocolType.Tls;
                    using (WebClient client = new WebClient())
                    {
                        client.Encoding = Encoding.UTF8;
                        string json = client.DownloadString(url);
                        List<CloudTranslationItem> parsed = ParseManifestJson(json);
                        if (parsed.Count > 0)
                        {
                            Items = parsed;
                            StatusMessage = "Успешно загружено " + Items.Count + " переводов из каталога GitHub.";
                        }
                        else
                        {
                            StatusMessage = "Манифест пуст или не содержит переводов.";
                        }
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = "Ошибка подключения к GitHub: " + ex.Message;
                    Log.Warning("[RUML] Failed to fetch manifest from " + url + ": " + ex);

                    // If empty, load default showcase items
                    if (Items.Count == 0)
                    {
                        LoadDefaultShowcaseItems();
                    }
                }
                finally
                {
                    IsBusy = false;
                    RefreshLocalState(content);
                    if (onComplete != null)
                    {
                        onComplete();
                    }
                }
            });
        }

        public static void DownloadAndInstallAsync(CloudTranslationItem item, ModContentPack content, RUMLSettings settings, Action onComplete = null)
        {
            if (IsBusy || item == null || content == null) return;
            IsBusy = true;
            CurrentActionItem = item.Id;
            DownloadPercent = 0f;
            StatusMessage = "Скачивание архива перевода " + item.ModName + "...";

            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                string safeName = (item.ModFolder + "_" + item.AuthorFolder).Replace("/", "_").Replace("\\", "_");
                string tempZip = Path.Combine(Path.GetTempPath(), "RUML_" + safeName + "_" + Guid.NewGuid().ToString("N") + ".zip");
                try
                {
                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768 | SecurityProtocolType.Tls;
                    using (WebClient client = new WebClient())
                    {
                        client.DownloadProgressChanged += delegate(object sender, DownloadProgressChangedEventArgs e)
                        {
                            DownloadPercent = e.ProgressPercentage;
                        };
                        client.DownloadFile(new Uri(item.DownloadUrl), tempZip);
                    }

                    StatusMessage = "Распаковка перевода в AppData...";
                    string extDir = RUMLFolderManager.GetExternalTranslationsDir();
                    string targetDir = Path.Combine(Path.Combine(extDir, item.ModFolder), item.AuthorFolder);

                    if (Directory.Exists(targetDir))
                    {
                        Directory.Delete(targetDir, true);
                    }
                    Directory.CreateDirectory(targetDir);

                    // Robust entry-by-entry extraction with Zip Slip and binary payload protection
                    string canonicalTarget = Path.GetFullPath(targetDir);
                    if (!canonicalTarget.EndsWith(Path.DirectorySeparatorChar.ToString()))
                    {
                        canonicalTarget += Path.DirectorySeparatorChar;
                    }

                    using (ZipArchive archive = ZipFile.OpenRead(tempZip))
                    {
                        for (int e = 0; e < archive.Entries.Count; e++)
                        {
                            ZipArchiveEntry entry = archive.Entries[e];
                            string entryRelPath = entry.FullName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                            string fullDestPath = Path.GetFullPath(Path.Combine(canonicalTarget, entryRelPath));

                            // Security Check 1: Path Traversal (Zip Slip) Protection
                            if (!fullDestPath.StartsWith(canonicalTarget, StringComparison.OrdinalIgnoreCase))
                            {
                                Log.Error("[RUML Security] Blocked Zip Slip attempt: entry '" + entry.FullName + "' targets outside of target folder!");
                                continue;
                            }

                            // Security Check 2: Disallow executable and binary files in translation packs
                            string ext = Path.GetExtension(fullDestPath).ToLowerInvariant();
                            if (ext == ".dll" || ext == ".exe" || ext == ".bat" || ext == ".cmd" || ext == ".ps1" ||
                                ext == ".vbs" || ext == ".so" || ext == ".dylib" || ext == ".msi" || ext == ".com" || ext == ".scr")
                            {
                                Log.Error("[RUML Security] Blocked dangerous file in translation pack: '" + entry.FullName + "'");
                                continue;
                            }

                            if (string.IsNullOrEmpty(entry.Name))
                            {
                                if (!Directory.Exists(fullDestPath))
                                {
                                    Directory.CreateDirectory(fullDestPath);
                                }
                                continue;
                            }

                            string parentDir = Path.GetDirectoryName(fullDestPath);
                            if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                            {
                                Directory.CreateDirectory(parentDir);
                            }

                            entry.ExtractToFile(fullDestPath, true);
                        }
                    }

                    // Auto-flatten if archive was packed with a redundant root folder (e.g. CombatExtended/Languages/...)
                    try
                    {
                        string directLanguages = Path.Combine(targetDir, "Languages");
                        if (!Directory.Exists(directLanguages))
                        {
                            string[] subDirs = Directory.GetDirectories(targetDir);
                            if (subDirs.Length == 1)
                            {
                                string nestedLang = Path.Combine(subDirs[0], "Languages");
                                if (Directory.Exists(nestedLang))
                                {
                                    string[] nestedFiles = Directory.GetFiles(subDirs[0]);
                                    for (int f = 0; f < nestedFiles.Length; f++)
                                    {
                                        string dest = Path.Combine(targetDir, Path.GetFileName(nestedFiles[f]));
                                        if (File.Exists(dest)) File.Delete(dest);
                                        File.Move(nestedFiles[f], dest);
                                    }
                                    string[] nestedFolders = Directory.GetDirectories(subDirs[0]);
                                    for (int d = 0; d < nestedFolders.Length; d++)
                                    {
                                        string dest = Path.Combine(targetDir, Path.GetFileName(nestedFolders[d]));
                                        if (Directory.Exists(dest)) Directory.Delete(dest, true);
                                        Directory.Move(nestedFolders[d], dest);
                                    }
                                    try { Directory.Delete(subDirs[0], true); } catch { }
                                }
                            }
                        }
                    }
                    catch { }

                    SaveInstalledMeta(targetDir, item);
                    item.IsInstalled = true;
                    item.LocalVersion = item.Version;
                    item.LocalHash = item.Hash;
                    item.HasUpdate = false;

                    // Ensure mod folder is enabled in settings and this author is selected
                    settings.SetModEnabled(item.ModFolder, true);
                    settings.SetSelectedAuthor(item.ModFolder, item.AuthorFolder);

                    if (settings != null && settings.manualApplyMode)
                    {
                        LongEventHandler.ExecuteWhenFinished(delegate()
                        {
                            RUMLFolderManager.ApplyFilter(content, settings);
                            RUMLFolderManager.hasPendingChanges = true;
                            Messages.Message("RUML: Перевод " + item.ModName + " скачан и распакован. Нажмите 'Применить настройки' для активации в игре.", MessageTypeDefOf.NeutralEvent, false);
                        });
                        StatusMessage = "Перевод " + item.ModName + " скачан. Нажмите 'Применить настройки' внизу.";
                    }
                    else
                    {
                        LongEventHandler.ExecuteWhenFinished(delegate()
                        {
                            RUMLFolderManager.ApplyFilter(content, settings);
                            RUMLFolderManager.ReloadLanguage();
                        });
                        StatusMessage = "Перевод " + item.ModName + " от " + item.Author + " успешно сохранён в AppData и активирован!";
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = "Ошибка установки: " + ex.Message;
                    Log.Error("[RUML] Failed to install cloud translation: " + ex);
                }
                finally
                {
                    try
                    {
                        if (File.Exists(tempZip)) File.Delete(tempZip);
                    }
                    catch { }

                    IsBusy = false;
                    CurrentActionItem = "";
                    RefreshLocalState(content);
                    if (onComplete != null)
                    {
                        onComplete();
                    }
                }
            });
        }

        public static void CheckUpdatesAsync(string url, ModContentPack content, Action<int> onComplete = null)
        {
            if (IsBusy) return;
            IsBusy = true;
            StatusMessage = "Проверка наличия обновлений переводов...";

            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                int updatesCount = 0;
                try
                {
                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768 | SecurityProtocolType.Tls;
                    using (WebClient client = new WebClient())
                    {
                        client.Encoding = Encoding.UTF8;
                        string json = client.DownloadString(url);
                        List<CloudTranslationItem> parsed = ParseManifestJson(json);
                        if (parsed.Count > 0)
                        {
                            Items = parsed;
                        }
                    }
                    RefreshLocalState(content);
                    for (int i = 0; i < Items.Count; i++)
                    {
                        if (Items[i].IsInstalled && Items[i].HasUpdate)
                        {
                            updatesCount++;
                        }
                    }

                    if (updatesCount > 0)
                    {
                        StatusMessage = "Найдено доступных обновлений: " + updatesCount;
                    }
                    else
                    {
                        StatusMessage = "Все установленные переводы актуальны (обновлений нет).";
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = "Ошибка проверки обновлений: " + ex.Message;
                    Log.Warning("[RUML] Failed to check updates: " + ex);
                }
                finally
                {
                    IsBusy = false;
                    RefreshLocalState(content);
                    LongEventHandler.ExecuteWhenFinished(delegate()
                    {
                        if (updatesCount > 0)
                        {
                            Messages.Message("RUML: Найдено доступных обновлений переводов: " + updatesCount, MessageTypeDefOf.NeutralEvent, false);
                        }
                        else
                        {
                            Messages.Message("RUML: Все установленные переводы актуальны.", MessageTypeDefOf.PositiveEvent, false);
                        }
                        if (onComplete != null)
                        {
                            onComplete(updatesCount);
                        }
                    });
                }
            });
        }

        public static void UpdateOutdatedOnlyAsync(ModContentPack content, RUMLSettings settings, Action onComplete = null)
        {
            if (IsBusy || content == null || settings == null) return;

            if (Items.Count == 0)
            {
                StatusMessage = "Загрузка каталога перед обновлением...";
                FetchManifestAsync(settings.cloudManifestUrl, content, delegate()
                {
                    UpdateOutdatedOnlyAsync(content, settings, onComplete);
                });
                return;
            }

            List<CloudTranslationItem> toUpdate = new List<CloudTranslationItem>();
            for (int i = 0; i < Items.Count; i++)
            {
                CloudTranslationItem it = Items[i];
                if (it.IsInstalled && settings.IsModEnabled(it.ModFolder) && it.HasUpdate)
                {
                    string selAuthor = settings.GetSelectedAuthor(it.ModFolder);
                    if (string.IsNullOrEmpty(selAuthor) || string.Equals(it.AuthorFolder, selAuthor, StringComparison.OrdinalIgnoreCase))
                    {
                        toUpdate.Add(it);
                    }
                }
            }

            if (toUpdate.Count == 0)
            {
                StatusMessage = "Все активные переводы актуальны (нет доступных обновлений).";
                Messages.Message("RUML: Все активные переводы уже актуальны! Обновлений не требуется.", MessageTypeDefOf.PositiveEvent, false);
                return;
            }

            ExecuteBatchUpdate(toUpdate, content, settings, onComplete);
        }

        public static void UpdateAllActiveAsync(ModContentPack content, RUMLSettings settings, Action onComplete = null)
        {
            if (IsBusy || content == null || settings == null) return;

            if (Items.Count == 0)
            {
                StatusMessage = "Загрузка каталога перед обновлением...";
                FetchManifestAsync(settings.cloudManifestUrl, content, delegate()
                {
                    UpdateAllActiveAsync(content, settings, onComplete);
                });
                return;
            }

            List<CloudTranslationItem> toUpdate = new List<CloudTranslationItem>();
            for (int i = 0; i < Items.Count; i++)
            {
                CloudTranslationItem it = Items[i];
                if (it.IsInstalled && settings.IsModEnabled(it.ModFolder))
                {
                    string selAuthor = settings.GetSelectedAuthor(it.ModFolder);
                    if (string.IsNullOrEmpty(selAuthor) || string.Equals(it.AuthorFolder, selAuthor, StringComparison.OrdinalIgnoreCase))
                    {
                        toUpdate.Add(it);
                    }
                }
            }

            if (toUpdate.Count == 0)
            {
                StatusMessage = "Нет активных переводов для обновления.";
                Messages.Message("RUML: Нет активных переводов для обновления.", MessageTypeDefOf.NeutralEvent, false);
                return;
            }

            ExecuteBatchUpdate(toUpdate, content, settings, onComplete);
        }

        private static void ExecuteBatchUpdate(List<CloudTranslationItem> toUpdate, ModContentPack content, RUMLSettings settings, Action onComplete)
        {
            IsBusy = true;
            DownloadPercent = 0f;
            StatusMessage = "Подготовка к обновлению " + toUpdate.Count + " переводов...";

            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                int successCount = 0;
                string extDir = RUMLFolderManager.GetExternalTranslationsDir();

                for (int i = 0; i < toUpdate.Count; i++)
                {
                    CloudTranslationItem item = toUpdate[i];
                    CurrentActionItem = item.Id;
                    DownloadPercent = (float)i / toUpdate.Count * 100f;
                    StatusMessage = "Обновление (" + (i + 1) + "/" + toUpdate.Count + "): " + item.ModName + "...";

                    string safeName = (item.ModFolder + "_" + item.AuthorFolder).Replace("/", "_").Replace("\\", "_");
                    string tempZip = Path.Combine(Path.GetTempPath(), "RUML_upd_" + safeName + "_" + Guid.NewGuid().ToString("N") + ".zip");
                    try
                    {
                        ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768 | SecurityProtocolType.Tls;
                        using (WebClient client = new WebClient())
                        {
                            client.DownloadFile(new Uri(item.DownloadUrl), tempZip);
                        }

                        string targetDir = Path.Combine(Path.Combine(extDir, item.ModFolder), item.AuthorFolder);
                        if (Directory.Exists(targetDir))
                        {
                            Directory.Delete(targetDir, true);
                        }
                        Directory.CreateDirectory(targetDir);

                        // Robust entry-by-entry extraction with Zip Slip and binary payload protection
                        string canonicalTarget = Path.GetFullPath(targetDir);
                        if (!canonicalTarget.EndsWith(Path.DirectorySeparatorChar.ToString()))
                        {
                            canonicalTarget += Path.DirectorySeparatorChar;
                        }

                        using (ZipArchive archive = ZipFile.OpenRead(tempZip))
                        {
                            for (int e = 0; e < archive.Entries.Count; e++)
                            {
                                ZipArchiveEntry entry = archive.Entries[e];
                                string entryRelPath = entry.FullName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                                string fullDestPath = Path.GetFullPath(Path.Combine(canonicalTarget, entryRelPath));

                                // Security Check 1: Path Traversal (Zip Slip) Protection
                                if (!fullDestPath.StartsWith(canonicalTarget, StringComparison.OrdinalIgnoreCase))
                                {
                                    Log.Error("[RUML Security] Blocked Zip Slip attempt: entry '" + entry.FullName + "' targets outside of target folder!");
                                    continue;
                                }

                                // Security Check 2: Disallow executable and binary files in translation packs
                                string ext = Path.GetExtension(fullDestPath).ToLowerInvariant();
                                if (ext == ".dll" || ext == ".exe" || ext == ".bat" || ext == ".cmd" || ext == ".ps1" ||
                                    ext == ".vbs" || ext == ".so" || ext == ".dylib" || ext == ".msi" || ext == ".com" || ext == ".scr")
                                {
                                    Log.Error("[RUML Security] Blocked dangerous file in translation pack: '" + entry.FullName + "'");
                                    continue;
                                }

                                if (string.IsNullOrEmpty(entry.Name))
                                {
                                    if (!Directory.Exists(fullDestPath))
                                    {
                                        Directory.CreateDirectory(fullDestPath);
                                    }
                                    continue;
                                }

                                string parentDir = Path.GetDirectoryName(fullDestPath);
                                if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                                {
                                    Directory.CreateDirectory(parentDir);
                                }

                                entry.ExtractToFile(fullDestPath, true);
                            }
                        }

                        // Auto-flatten if nested
                        try
                        {
                            string directLanguages = Path.Combine(targetDir, "Languages");
                            if (!Directory.Exists(directLanguages))
                            {
                                string[] subDirs = Directory.GetDirectories(targetDir);
                                if (subDirs.Length == 1)
                                {
                                    string nestedLang = Path.Combine(subDirs[0], "Languages");
                                    if (Directory.Exists(nestedLang))
                                    {
                                        string[] nestedFiles = Directory.GetFiles(subDirs[0]);
                                        for (int f = 0; f < nestedFiles.Length; f++)
                                        {
                                            string dest = Path.Combine(targetDir, Path.GetFileName(nestedFiles[f]));
                                            if (File.Exists(dest)) File.Delete(dest);
                                            File.Move(nestedFiles[f], dest);
                                        }
                                        string[] nestedFolders = Directory.GetDirectories(subDirs[0]);
                                        for (int d = 0; d < nestedFolders.Length; d++)
                                        {
                                            string dest = Path.Combine(targetDir, Path.GetFileName(nestedFolders[d]));
                                            if (Directory.Exists(dest)) Directory.Delete(dest, true);
                                            Directory.Move(nestedFolders[d], dest);
                                        }
                                        try { Directory.Delete(subDirs[0], true); } catch { }
                                    }
                                }
                            }
                        }
                        catch { }

                        SaveInstalledMeta(targetDir, item);
                        item.IsInstalled = true;
                        item.LocalVersion = item.Version;
                        item.LocalHash = item.Hash;
                        item.HasUpdate = false;
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        Log.Error("[RUML] Failed to update " + item.ModName + ": " + ex);
                    }
                    finally
                    {
                        try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
                    }
                }

                DownloadPercent = 100f;
                CurrentActionItem = "";
                IsBusy = false;
                StatusMessage = "Успешно обновлено переводов: " + successCount + "/" + toUpdate.Count;

                LongEventHandler.ExecuteWhenFinished(delegate()
                {
                    RUMLFolderManager.ApplyFilter(content, settings);
                    if (settings != null && settings.manualApplyMode)
                    {
                        RUMLFolderManager.hasPendingChanges = true;
                        RefreshLocalState(content);
                        Messages.Message("RUML: Обновлено " + successCount + " активных переводов. Нажмите 'Применить настройки' для активации в игре.", MessageTypeDefOf.PositiveEvent, false);
                    }
                    else
                    {
                        RUMLFolderManager.ReloadLanguage();
                        RefreshLocalState(content);
                        Messages.Message("RUML: Успешно обновлено " + successCount + " активных переводов!", MessageTypeDefOf.PositiveEvent, false);
                    }
                    if (onComplete != null)
                    {
                        onComplete();
                    }
                });
            });
        }

        public static void Uninstall(CloudTranslationItem item, ModContentPack content, RUMLSettings settings)
        {
            if (item == null || content == null) return;
            RUMLFolderManager.DeleteAuthorTranslation(item.ModFolder, item.AuthorFolder, content, settings);
            item.IsInstalled = false;
            StatusMessage = "Перевод " + item.ModName + " (" + item.Author + ") удалён из AppData.";
            RefreshLocalState(content);
        }

        public static void LoadDefaultShowcaseItems()
        {
            Items = new List<CloudTranslationItem>();
        }

        public static string ExportSampleManifest()
        {
            string desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string file = Path.Combine(desk, "RUML_manifest_template.json");

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[");
            sb.AppendLine("  {");
            sb.AppendLine("    \"id\": \"community.modname\",");
            sb.AppendLine("    \"modName\": \"Название Мода\",");
            sb.AppendLine("    \"packageId\": \"author.modpackageid\",");
            sb.AppendLine("    \"author\": \"Ваш Nickname\",");
            sb.AppendLine("    \"version\": \"1.0.0\",");
            sb.AppendLine("    \"downloadUrl\": \"https://raw.githubusercontent.com/USER/REPO/main/packs/ModName/Author/ModName.zip\",");
            sb.AppendLine("    \"description\": \"Описание перевода, версия мода и примечания\",");
            sb.AppendLine("    \"modFolder\": \"ModFolder\",");
            sb.AppendLine("    \"authorFolder\": \"AuthorFolder\",");
            sb.AppendLine("    \"targetFolder\": \"ModFolder/AuthorFolder\"");
            sb.AppendLine("  }");
            sb.AppendLine("]");

            File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
            return file;
        }

        public static List<CloudTranslationItem> ParseManifestJson(string json)
        {
            List<CloudTranslationItem> list = new List<CloudTranslationItem>();
            if (string.IsNullOrEmpty(json)) return list;

            int idx = 0;
            while (idx < json.Length)
            {
                int start = json.IndexOf('{', idx);
                if (start < 0) break;
                int end = json.IndexOf('}', start);
                if (end < 0) break;

                string block = json.Substring(start + 1, end - start - 1);
                CloudTranslationItem item = new CloudTranslationItem();
                item.Id = ExtractJsonField(block, "id");
                item.ModName = ExtractJsonField(block, "modName");
                item.PackageId = ExtractJsonField(block, "packageId");
                item.Author = ExtractJsonField(block, "author");
                item.Version = ExtractJsonField(block, "version");
                item.Hash = ExtractJsonField(block, "hash");
                item.DownloadUrl = ExtractJsonField(block, "downloadUrl");
                item.Description = ExtractJsonField(block, "description");
                item.TargetFolder = ExtractJsonField(block, "targetFolder");
                item.ModFolder = ExtractJsonField(block, "modFolder");
                item.AuthorFolder = ExtractJsonField(block, "authorFolder");

                if (string.IsNullOrEmpty(item.ModFolder)) item.ModFolder = item.ModName;
                if (string.IsNullOrEmpty(item.AuthorFolder)) item.AuthorFolder = item.Author;
                if (string.IsNullOrEmpty(item.TargetFolder))
                {
                    item.TargetFolder = item.ModFolder + "/" + item.AuthorFolder;
                }

                if (!string.IsNullOrEmpty(item.ModName) && !string.IsNullOrEmpty(item.DownloadUrl))
                {
                    list.Add(item);
                }

                idx = end + 1;
            }
            return list;
        }

        public static string ExtractJsonField(string block, string key)
        {
            string pattern = "\"" + key + "\"\\s*:\\s*\"([^\"]*)\"";
            System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(block, pattern);
            if (m.Success && m.Groups.Count > 1)
            {
                return m.Groups[1].Value.Trim();
            }
            return "";
        }
    }
}
