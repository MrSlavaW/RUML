using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using RimWorld;
using Verse;

namespace RUML
{
    public class InstalledModItem
    {
        public string ModFolder;
        public List<string> Authors = new List<string>();
    }

    public static class RUMLFolderManager
    {
        private static List<string> originalFolders = null;
        public static bool hasPendingChanges = false;

        public static string GetExternalTranslationsDir()
        {
            string raw = Path.Combine(GenFilePaths.SaveDataFolderPath, "RUML_Translations");
            string path = Path.GetFullPath(raw);
            if (!Directory.Exists(path))
            {
                try
                {
                    Directory.CreateDirectory(path);
                }
                catch { }
            }
            return path;
        }

        public static string GetInstalledVersion(string modFolder, string author)
        {
            if (string.IsNullOrEmpty(modFolder) || string.IsNullOrEmpty(author)) return "1.0.0";
            try
            {
                string infoFile = Path.Combine(Path.Combine(GetExternalTranslationsDir(), modFolder), Path.Combine(author, "info.json"));
                if (File.Exists(infoFile))
                {
                    string text = File.ReadAllText(infoFile);
                    string ver = RUMLCloudManager.ExtractJsonField(text, "version");
                    if (!string.IsNullOrEmpty(ver)) return ver;
                }
            }
            catch { }
            return "1.0.0";
        }

        public static bool IsExternalModFolder(string modFolder)
        {
            if (string.IsNullOrEmpty(modFolder)) return false;
            try
            {
                string extDir = GetExternalTranslationsDir();
                string target = Path.Combine(extDir, modFolder);
                return Directory.Exists(target);
            }
            catch
            {
                return false;
            }
        }

        public static void Initialize(ModContentPack content, RUMLSettings settings)
        {
            if (content == null) return;

            FieldInfo field = typeof(ModContentPack).GetField("foldersToLoadDescendingOrder", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) return;

            List<string> currentFolders = field.GetValue(content) as List<string>;
            if (currentFolders == null) return;

            if (originalFolders == null)
            {
                originalFolders = new List<string>(currentFolders);
            }

            ApplyFilter(content, settings);
        }

        public static List<InstalledModItem> GetAllInstalledMods()
        {
            List<InstalledModItem> list = new List<InstalledModItem>();
            try
            {
                string extDir = GetExternalTranslationsDir();
                if (!Directory.Exists(extDir)) return list;

                string[] modDirs = Directory.GetDirectories(extDir);
                for (int i = 0; i < modDirs.Length; i++)
                {
                    string modPath = modDirs[i];
                    string modName = Path.GetFileName(modPath);

                    // Auto-migration: if old flat structure (modPath/Languages exists)
                    string oldLang = Path.Combine(modPath, "Languages");
                    if (Directory.Exists(oldLang))
                    {
                        string author = "MrSlavaV";
                        string infoFile = Path.Combine(modPath, "info.json");
                        if (File.Exists(infoFile))
                        {
                            try
                            {
                                string text = File.ReadAllText(infoFile);
                                int aIdx = text.IndexOf("\"author\"", StringComparison.OrdinalIgnoreCase);
                                if (aIdx >= 0)
                                {
                                    int colon = text.IndexOf(':', aIdx);
                                    int q1 = text.IndexOf('"', colon + 1);
                                    int q2 = text.IndexOf('"', q1 + 1);
                                    if (q1 >= 0 && q2 > q1)
                                    {
                                        author = text.Substring(q1 + 1, q2 - q1 - 1).Trim();
                                    }
                                }
                            }
                            catch { }
                        }
                        try
                        {
                            string targetAuthorDir = Path.Combine(modPath, author);
                            Directory.CreateDirectory(targetAuthorDir);
                            string newLang = Path.Combine(targetAuthorDir, "Languages");
                            if (Directory.Exists(newLang)) Directory.Delete(newLang, true);
                            Directory.Move(oldLang, newLang);
                            if (File.Exists(infoFile))
                            {
                                string newInfo = Path.Combine(targetAuthorDir, "info.json");
                                if (File.Exists(newInfo)) File.Delete(newInfo);
                                File.Move(infoFile, newInfo);
                            }
                        }
                        catch (Exception mex)
                        {
                            Log.Warning("[RUML] Failed to auto-migrate flat folder " + modName + ": " + mex);
                        }
                    }

                    // Scan author directories inside modPath
                    InstalledModItem item = new InstalledModItem();
                    item.ModFolder = modName;
                    string[] authorDirs = Directory.GetDirectories(modPath);
                    for (int a = 0; a < authorDirs.Length; a++)
                    {
                        string aDir = authorDirs[a];
                        string aName = Path.GetFileName(aDir);
                        string langDir = Path.Combine(aDir, "Languages");

                        // Auto-heal flat folder structure (e.g. if Keyed/DefInjected were placed directly inside author dir)
                        if (!Directory.Exists(langDir))
                        {
                            if (Directory.Exists(Path.Combine(aDir, "Keyed")) || Directory.Exists(Path.Combine(aDir, "DefInjected")))
                            {
                                try
                                {
                                    string ruDir = Path.Combine(langDir, "Russian");
                                    Directory.CreateDirectory(ruDir);
                                    string[] subDirsToWrap = new string[] { "Keyed", "DefInjected", "Strings" };
                                    for (int w = 0; w < subDirsToWrap.Length; w++)
                                    {
                                        string src = Path.Combine(aDir, subDirsToWrap[w]);
                                        if (Directory.Exists(src))
                                        {
                                            string dst = Path.Combine(ruDir, subDirsToWrap[w]);
                                            if (Directory.Exists(dst)) Directory.Delete(dst, true);
                                            Directory.Move(src, dst);
                                        }
                                    }
                                }
                                catch (Exception wrapEx)
                                {
                                    Log.Warning("[RUML] Failed to auto-wrap flat author folder " + aDir + ": " + wrapEx);
                                }
                            }
                        }

                        if (Directory.Exists(Path.Combine(aDir, "Languages")))
                        {
                            item.Authors.Add(aName);
                        }
                    }

                    if (item.Authors.Count > 0)
                    {
                        item.Authors.Sort(StringComparer.OrdinalIgnoreCase);
                        list.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("[RUML] Failed to scan installed translations: " + ex);
            }

            list.Sort(delegate(InstalledModItem a, InstalledModItem b)
            {
                return string.Compare(a.ModFolder, b.ModFolder, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        public static List<string> GetAllDiscoveredModFolders(ModContentPack content)
        {
            List<InstalledModItem> installed = GetAllInstalledMods();
            List<string> result = new List<string>();
            for (int i = 0; i < installed.Count; i++)
            {
                result.Add(installed[i].ModFolder);
            }
            return result;
        }

        public static void ApplyFilter(ModContentPack content, RUMLSettings settings)
        {
            if (content == null) return;

            FieldInfo field = typeof(ModContentPack).GetField("foldersToLoadDescendingOrder", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) return;

            List<string> currentFolders = field.GetValue(content) as List<string>;
            if (currentFolders == null) return;

            if (originalFolders == null)
            {
                originalFolders = new List<string>(currentFolders);
            }

            // Always keep core mod root folder (e.g. RUML and version subfolders 1.5, 1.6)
            List<string> filtered = new List<string>();
            string rootNorm = (content.RootDir ?? "").Replace('\\', '/').TrimEnd('/');
            foreach (string folder in originalFolders)
            {
                string norm = folder.Replace('\\', '/').TrimEnd('/');
                if (norm.Equals(rootNorm, StringComparison.OrdinalIgnoreCase) ||
                    norm.StartsWith(rootNorm + "/", StringComparison.OrdinalIgnoreCase))
                {
                    if (norm.IndexOf(rootNorm + "/Mods/", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        filtered.Add(Path.GetFullPath(folder));
                    }
                }
            }

            // Add active author folder for each enabled mod
            List<InstalledModItem> installed = GetAllInstalledMods();
            string extDir = GetExternalTranslationsDir();
            for (int i = 0; i < installed.Count; i++)
            {
                InstalledModItem item = installed[i];
                if (settings.IsModEnabled(item.ModFolder))
                {
                    string author = settings.GetSelectedAuthor(item.ModFolder);
                    if (string.IsNullOrEmpty(author) || !item.Authors.Contains(author))
                    {
                        if (item.Authors.Count > 0)
                        {
                            author = item.Authors[0];
                            settings.SetSelectedAuthor(item.ModFolder, author);
                        }
                    }

                    if (!string.IsNullOrEmpty(author))
                    {
                        string targetPath = Path.GetFullPath(Path.Combine(Path.Combine(extDir, item.ModFolder), author));
                        if (Directory.Exists(targetPath))
                        {
                            filtered.Add(targetPath);
                        }
                    }
                }
            }

            field.SetValue(content, filtered);
            Log.Message("[RUML] Applied active translation filter: " + filtered.Count + " folders active.");
        }

        public static string GetSubfolderName(string path)
        {
            string norm = path.Replace('\\', '/');

            // Check external AppData path: /RUML_Translations/<ModFolder>/<Author>
            int extIdx = norm.LastIndexOf("/RUML_Translations/", StringComparison.OrdinalIgnoreCase);
            if (extIdx >= 0)
            {
                string rel = norm.Substring(extIdx + 19);
                int slash = rel.IndexOf('/');
                return slash > 0 ? rel.Substring(0, slash) : rel;
            }

            // Check built-in Mods/ path
            int idx = norm.LastIndexOf("/Mods/", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                string rel = norm.Substring(idx + 6);
                int slash = rel.IndexOf('/');
                string name = slash > 0 ? rel.Substring(0, slash) : rel;
                if (name.Equals("RUML_RimworldUniversalModsLocalization", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("RUML", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
                return name;
            }
            return null;
        }

        public static bool DeleteAuthorTranslation(string modFolder, string author, ModContentPack content, RUMLSettings settings)
        {
            if (string.IsNullOrEmpty(modFolder) || string.IsNullOrEmpty(author)) return false;
            bool deleted = false;
            try
            {
                string extDir = GetExternalTranslationsDir();
                string authorDir = Path.Combine(Path.Combine(extDir, modFolder), author);
                if (Directory.Exists(authorDir))
                {
                    Directory.Delete(authorDir, true);
                    deleted = true;
                }

                // Check remaining authors in modFolder
                string modDir = Path.Combine(extDir, modFolder);
                if (Directory.Exists(modDir))
                {
                    string[] remaining = Directory.GetDirectories(modDir);
                    if (remaining.Length > 0)
                    {
                        string nextAuthor = Path.GetFileName(remaining[0]);
                        settings.SetSelectedAuthor(modFolder, nextAuthor);
                    }
                    else
                    {
                        try { Directory.Delete(modDir, true); } catch { }
                        settings.SetModEnabled(modFolder, false);
                    }
                }

                ApplyFilter(content, settings);
                if (settings != null && settings.manualApplyMode)
                {
                    hasPendingChanges = true;
                    Messages.Message("RUML: Перевод для " + modFolder + " (" + author + ") удалён с диска. Нажмите 'Применить настройки' для обновления в игре.", MessageTypeDefOf.NeutralEvent, false);
                }
                else
                {
                    ReloadLanguage();
                    Messages.Message("RUML: Перевод для " + modFolder + " (" + author + ") успешно удалён!", MessageTypeDefOf.PositiveEvent, false);
                }
            }
            catch (Exception ex)
            {
                Log.Error("[RUML] Failed to delete author translation: " + ex);
                Messages.Message("RUML: Ошибка удаления: " + ex.Message, MessageTypeDefOf.RejectInput, false);
            }
            return deleted;
        }

        public static bool DeleteTranslationFolder(string modFolder, ModContentPack content, RUMLSettings settings)
        {
            if (string.IsNullOrEmpty(modFolder)) return false;
            bool deleted = false;
            try
            {
                string extDir = Path.Combine(GetExternalTranslationsDir(), modFolder);
                if (Directory.Exists(extDir))
                {
                    Directory.Delete(extDir, true);
                    deleted = true;
                }

                settings.SetModEnabled(modFolder, false);
                ApplyFilter(content, settings);
                if (settings != null && settings.manualApplyMode)
                {
                    hasPendingChanges = true;
                    Messages.Message("RUML: Все переводы для " + modFolder + " удалены с диска. Нажмите 'Применить настройки' для обновления в игре.", MessageTypeDefOf.NeutralEvent, false);
                }
                else
                {
                    ReloadLanguage();
                    Messages.Message("RUML: Все переводы для " + modFolder + " успешно удалены!", MessageTypeDefOf.PositiveEvent, false);
                }
            }
            catch (Exception ex)
            {
                Log.Error("[RUML] Failed to delete translation folder: " + ex);
                Messages.Message("RUML: Ошибка удаления: " + ex.Message, MessageTypeDefOf.RejectInput, false);
            }
            return deleted;
        }

        public static void ReloadLanguage()
        {
            try
            {
                LoadedLanguage lang = LanguageDatabase.activeLanguage;
                if (lang != null)
                {
                    // 1. Reset LoadedLanguage internal state so LoadData actually re-scans active folders
                    FieldInfo dataLoadedField = typeof(LoadedLanguage).GetField("dataIsLoaded", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (dataLoadedField != null)
                    {
                        dataLoadedField.SetValue(lang, false);
                    }

                    FieldInfo keyedField = typeof(LoadedLanguage).GetField("keyedReplacements", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (keyedField != null)
                    {
                        IDictionary keyed = keyedField.GetValue(lang) as IDictionary;
                        if (keyed != null) keyed.Clear();
                    }

                    FieldInfo defInjField = typeof(LoadedLanguage).GetField("defInjections", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (defInjField != null)
                    {
                        IList defInj = defInjField.GetValue(lang) as IList;
                        if (defInj != null) defInj.Clear();
                    }

                    FieldInfo strFilesField = typeof(LoadedLanguage).GetField("stringFiles", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (strFilesField != null)
                    {
                        IDictionary strFiles = strFilesField.GetValue(lang) as IDictionary;
                        if (strFiles != null) strFiles.Clear();
                    }

                    FieldInfo tmpFilesField = typeof(LoadedLanguage).GetField("tmpAlreadyLoadedFiles", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (tmpFilesField != null)
                    {
                        IDictionary tmpFiles = tmpFilesField.GetValue(lang) as IDictionary;
                        if (tmpFiles != null) tmpFiles.Clear();
                    }

                    // 2. Reload translation strings and def-injections from all active mod folders
                    lang.LoadData();

                    // 2.1. Sanitize Hediff stages and alias numeric indices to canonical named keys
                    try
                    {
                        RUMLLanguageSanitizer.SanitizeAndAlias(lang);
                    }
                    catch { }

                    // 3. Inject updated translations into all Def instances in DefDatabase
                    lang.InjectIntoData_AfterImpliedDefs();

                    // 4. Update legacy backstories if present
                    try
                    {
                        BackstoryTranslationUtility.LoadAndInjectBackstoryData(lang.AllDirectories, new List<string>());
                    }
                    catch { }

                    // 5. Clear label cache for UI elements, items, and pawns
                    GenLabel.ClearCache();

                    hasPendingChanges = false;
                    Messages.Message("RUML: Переводы успешно применены на лету!", MessageTypeDefOf.PositiveEvent, false);
                }
            }
            catch (Exception ex)
            {
                Log.Error("[RUML] Failed to reload language: " + ex);
            }
        }
    }
}
