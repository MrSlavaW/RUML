using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using RimWorld;
using Verse;

namespace RUML
{
    public static class RUMLFolderManager
    {
        private static List<string> originalFolders = null;

        public static string GetExternalTranslationsDir()
        {
            string path = Path.Combine(GenFilePaths.SaveDataFolderPath, "RUML_Translations");
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

        public static void ApplyFilter(ModContentPack content, RUMLSettings settings)
        {
            if (content == null || originalFolders == null) return;

            FieldInfo field = typeof(ModContentPack).GetField("foldersToLoadDescendingOrder", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) return;

            // 1. Discover built-in translation folders in Mods/
            try
            {
                string modsDir = Path.Combine(content.RootDir, "Mods");
                if (Directory.Exists(modsDir))
                {
                    string[] subDirs = Directory.GetDirectories(modsDir);
                    for (int i = 0; i < subDirs.Length; i++)
                    {
                        string dir = subDirs[i];
                        bool exists = false;
                        for (int j = 0; j < originalFolders.Count; j++)
                        {
                            if (string.Equals(originalFolders[j], dir, StringComparison.OrdinalIgnoreCase))
                            {
                                exists = true;
                                break;
                            }
                        }
                        if (!exists)
                        {
                            originalFolders.Add(dir);
                        }
                    }
                }
            }
            catch { }

            // 2. Discover external translation folders in AppData RUML_Translations/
            try
            {
                string extDir = GetExternalTranslationsDir();
                if (Directory.Exists(extDir))
                {
                    string[] extSubDirs = Directory.GetDirectories(extDir);
                    for (int i = 0; i < extSubDirs.Length; i++)
                    {
                        string dir = extSubDirs[i];
                        bool exists = false;
                        for (int j = 0; j < originalFolders.Count; j++)
                        {
                            if (string.Equals(originalFolders[j], dir, StringComparison.OrdinalIgnoreCase))
                            {
                                exists = true;
                                break;
                            }
                        }
                        if (!exists)
                        {
                            originalFolders.Add(dir);
                        }
                    }
                }
            }
            catch { }

            List<string> filtered = new List<string>();
            foreach (string folder in originalFolders)
            {
                string norm = folder.Replace('\\', '/');
                string sub = GetSubfolderName(norm);

                if (string.IsNullOrEmpty(sub) || settings.IsModEnabled(sub))
                {
                    filtered.Add(folder);
                }
            }

            field.SetValue(content, filtered);
            Log.Message("[RUML] Applied active translation filter: " + filtered.Count + " / " + originalFolders.Count + " folders active.");
        }

        public static string GetSubfolderName(string path)
        {
            string norm = path.Replace('\\', '/');

            // Check external AppData path first
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

        public static List<string> GetAllDiscoveredModFolders(ModContentPack content)
        {
            List<string> result = new List<string>();

            // Built-in folders
            if (content != null)
            {
                try
                {
                    string modsDir = Path.Combine(content.RootDir, "Mods");
                    if (Directory.Exists(modsDir))
                    {
                        string[] subDirs = Directory.GetDirectories(modsDir);
                        for (int i = 0; i < subDirs.Length; i++)
                        {
                            string subName = Path.GetFileName(subDirs[i]);
                            if (!string.IsNullOrEmpty(subName) && !result.Contains(subName))
                            {
                                result.Add(subName);
                            }
                        }
                    }
                }
                catch { }
            }

            // External AppData folders
            try
            {
                string extDir = GetExternalTranslationsDir();
                if (Directory.Exists(extDir))
                {
                    string[] extSubDirs = Directory.GetDirectories(extDir);
                    for (int i = 0; i < extSubDirs.Length; i++)
                    {
                        string subName = Path.GetFileName(extSubDirs[i]);
                        if (!string.IsNullOrEmpty(subName) && !result.Contains(subName))
                        {
                            result.Add(subName);
                        }
                    }
                }
            }
            catch { }

            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        public static bool DeleteTranslationFolder(string modFolder, ModContentPack content, RUMLSettings settings)
        {
            if (string.IsNullOrEmpty(modFolder)) return false;
            bool deleted = false;
            try
            {
                // Check external AppData directory
                string extDir = Path.Combine(GetExternalTranslationsDir(), modFolder);
                if (Directory.Exists(extDir))
                {
                    Directory.Delete(extDir, true);
                    deleted = true;
                }

                // If in originalFolders, clean it up
                if (originalFolders != null)
                {
                    originalFolders.RemoveAll(delegate(string p)
                    {
                        return string.Equals(GetSubfolderName(p), modFolder, StringComparison.OrdinalIgnoreCase);
                    });
                }

                settings.SetModEnabled(modFolder, false);
                ApplyFilter(content, settings);
                ReloadLanguage();
                Messages.Message("RUML: Перевод для " + modFolder + " успешно удалён!", MessageTypeDefOf.PositiveEvent, false);
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
                if (LanguageDatabase.activeLanguage != null)
                {
                    LanguageDatabase.SelectLanguage(LanguageDatabase.activeLanguage);
                    Messages.Message("RUML: Переводы успешно перезагружены в памяти игры!", MessageTypeDefOf.PositiveEvent, false);
                }
            }
            catch (Exception ex)
            {
                Log.Error("[RUML] Failed to reload language: " + ex);
            }
        }
    }
}
