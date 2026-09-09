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
        public string DownloadUrl = "";
        public string Description = "";
        public string TargetFolder = "";
        public bool IsInstalled = false;
        public bool IsTargetModActive = false;
    }

    public static class RUMLCloudManager
    {
        public static List<CloudTranslationItem> Items = new List<CloudTranslationItem>();
        public static bool IsBusy = false;
        public static string StatusMessage = "Готов к синхронизации";
        public static float DownloadPercent = 0f;
        public static string CurrentActionItem = "";

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
                string targetDir = Path.Combine(extDir, item.TargetFolder);
                item.IsInstalled = Directory.Exists(targetDir);

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
                string tempZip = Path.Combine(Path.GetTempPath(), "RUML_" + item.TargetFolder + "_" + Guid.NewGuid().ToString("N") + ".zip");
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
                    string targetDir = Path.Combine(RUMLFolderManager.GetExternalTranslationsDir(), item.TargetFolder);

                    if (Directory.Exists(targetDir))
                    {
                        Directory.Delete(targetDir, true);
                    }
                    Directory.CreateDirectory(targetDir);

                    ZipFile.ExtractToDirectory(tempZip, targetDir);

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

                    // Ensure mod folder is enabled in settings
                    settings.SetModEnabled(item.TargetFolder, true);

                    // Re-apply folder filter and hot-reload language
                    RUMLFolderManager.ApplyFilter(content, settings);
                    RUMLFolderManager.ReloadLanguage();

                    item.IsInstalled = true;
                    StatusMessage = "Перевод " + item.ModName + " успешно сохранён в AppData и активирован!";
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

        public static void Uninstall(CloudTranslationItem item, ModContentPack content, RUMLSettings settings)
        {
            if (item == null || content == null) return;
            RUMLFolderManager.DeleteTranslationFolder(item.TargetFolder, content, settings);
            item.IsInstalled = false;
            StatusMessage = "Перевод " + item.ModName + " удалён из AppData.";
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
            sb.AppendLine("    \"downloadUrl\": \"https://raw.githubusercontent.com/USER/REPO/main/packs/ModName.zip\",");
            sb.AppendLine("    \"description\": \"Описание перевода, версия мода и примечания\",");
            sb.AppendLine("    \"targetFolder\": \"ModFolderName\"");
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
                item.DownloadUrl = ExtractJsonField(block, "downloadUrl");
                item.Description = ExtractJsonField(block, "description");
                item.TargetFolder = ExtractJsonField(block, "targetFolder");

                if (!string.IsNullOrEmpty(item.ModName) && !string.IsNullOrEmpty(item.DownloadUrl))
                {
                    if (string.IsNullOrEmpty(item.TargetFolder)) item.TargetFolder = item.Id;
                    list.Add(item);
                }

                idx = end + 1;
            }
            return list;
        }

        private static string ExtractJsonField(string block, string key)
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
