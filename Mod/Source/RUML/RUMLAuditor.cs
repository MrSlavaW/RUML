// MC Version: 1.5 | Loader: RimWorld | Mappings: Official
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Verse;

namespace RUML
{
    public class ModAuditReport
    {
        public string ModName;
        public string PackageId;
        public bool IsRUMLMod;

        // Def breakdown
        public int TotalDefLabels;
        public int TranslatedDefLabels;

        public int TotalDefDescriptions;
        public int TranslatedDefDescriptions;

        public int TotalDefOther;
        public int TranslatedDefOther;

        // Def summary
        public int TotalDefs;
        public int TranslatedDefs;
        public int MissingDefs;

        // Keyed breakdown
        public int TotalKeyed;
        public int TranslatedKeyed;
        public int MissingKeyed;

        // Overall summary
        public int TotalItems;
        public int TranslatedItems;
        public int MissingItems;
        public float Percent;

        // Categorized missing lists
        public List<string> MissingDefLabels = new List<string>();
        public List<string> MissingDefDescriptions = new List<string>();
        public List<string> MissingDefOther = new List<string>();
        public List<string> MissingKeyedList = new List<string>();

        // Unified list for backward-compatibility and export
        public List<string> MissingList = new List<string>();
    }

    public static class RUMLAuditor
    {
        public static List<ModAuditReport> RunAudit(HashSet<string> targetPackageIds = null, Dictionary<string, string> rumlKnownMods = null)
        {
            List<ModAuditReport> reports = new List<ModAuditReport>();

            // Ensure English default language data is loaded so all mods' English keyed strings exist in memory
            if (LanguageDatabase.defaultLanguage != null)
            {
                try
                {
                    LanguageDatabase.defaultLanguage.LoadData();
                }
                catch { }
            }

            foreach (ModContentPack mod in LoadedModManager.RunningMods)
            {
                string pid = mod.PackageIdPlayerFacing;
                if (string.IsNullOrEmpty(pid)) continue;

                if (targetPackageIds != null && targetPackageIds.Count > 0)
                {
                    if (!targetPackageIds.Contains(pid)) continue;
                }

                bool isRuml = false;
                if (rumlKnownMods != null)
                {
                    foreach (var kv in rumlKnownMods)
                    {
                        if (string.Equals(kv.Value, pid, StringComparison.OrdinalIgnoreCase))
                        {
                            isRuml = true;
                            break;
                        }
                    }
                }

                ModAuditReport rep = new ModAuditReport();
                rep.ModName = mod.Name;
                rep.PackageId = pid;
                rep.IsRUMLMod = isRuml;

                ModMetaData modMeta = ModLister.GetModWithIdentifier(pid);

                // -------------------------------------------------------------
                // 1. AUDIT DEFS (Labels, Descriptions, and Nested Fields)
                // -------------------------------------------------------------
                foreach (Type defType in GenDefDatabase.AllDefTypesWithDatabases())
                {
                    try
                    {
                        string dtName = defType.Name;
                        if (dtName.Contains("Fleck") || dtName.Contains("Effecter") || dtName.Contains("SubEffecter") ||
                            dtName.Equals("ModDef") || dtName.Contains("TMMSettingDef") || dtName.Contains("SettingsMenuDef") ||
                            dtName.Contains("ModOptionCategoryDef") ||
                            dtName.Equals("ToolCapacityDef") || dtName.Equals("WorkGiverDef") || dtName.Contains("ThinkTree") ||
                            dtName.Contains("Duty") || dtName.Contains("ShaderTypeDef") || dtName.Contains("SoundDef"))
                        {
                            continue;
                        }

                        DefInjectionUtility.ForEachPossibleDefInjection(defType, delegate(
                            string suggestedPath,
                            string normalizedPath,
                            bool isCollection,
                            string currentValue,
                            IEnumerable<string> currentValueCollection,
                            bool translationAllowed,
                            bool fullListTranslationAllowed,
                            System.Reflection.FieldInfo fieldInfo,
                            Def currentDef)
                        {
                            if (!translationAllowed || currentDef == null) return;
                            if (currentDef.modContentPack != mod) return;

                            ThingDef td = currentDef as ThingDef;
                            if (td != null)
                            {
                                if (td.mote != null || td.projectile != null ||
                                    td.category == ThingCategory.Mote || td.category == ThingCategory.Projectile ||
                                    td.category == ThingCategory.Ethereal)
                                {
                                    return;
                                }
                            }

                            if (!isCollection)
                            {
                                if (!DefInjectionUtility.ShouldCheckMissingInjection(currentValue, fieldInfo, currentDef)) return;

                                string cleanVal = currentValue != null ? currentValue.Trim() : "";
                                if (string.IsNullOrEmpty(cleanVal) || cleanVal.Length <= 1) return;
                                if (cleanVal.Equals("Mote", StringComparison.OrdinalIgnoreCase) ||
                                    cleanVal.Equals("bullet", StringComparison.OrdinalIgnoreCase))
                                {
                                    return;
                                }

                                bool isRu = ContainsCyrillic(cleanVal);
                                if (!isRu && LanguageDatabase.activeLanguage != null && LanguageDatabase.activeLanguage.defInjections != null)
                                {
                                    for (int pIdx = 0; pIdx < LanguageDatabase.activeLanguage.defInjections.Count; pIdx++)
                                    {
                                        var pkg = LanguageDatabase.activeLanguage.defInjections[pIdx];
                                        if (pkg != null && pkg.defType == defType && pkg.injections != null)
                                        {
                                            DefInjectionPackage.DefInjection inj;
                                            if (pkg.injections.TryGetValue(suggestedPath, out inj))
                                            {
                                                if (inj != null && !inj.isPlaceholder && !string.Equals(inj.injection, "TODO", StringComparison.OrdinalIgnoreCase))
                                                {
                                                    isRu = true;
                                                }
                                            }
                                            break;
                                        }
                                    }
                                }

                                if (fieldInfo != null && fieldInfo.Name == "label")
                                {
                                    rep.TotalDefLabels++;
                                    if (isRu) rep.TranslatedDefLabels++;
                                    else
                                    {
                                        string entry = "[Def.Label] " + defType.Name + "." + currentDef.defName + ": \"" + cleanVal + "\"";
                                        rep.MissingDefLabels.Add(entry);
                                        rep.MissingList.Add(entry);
                                    }
                                }
                                else if (fieldInfo != null && fieldInfo.Name == "description")
                                {
                                    rep.TotalDefDescriptions++;
                                    if (isRu) rep.TranslatedDefDescriptions++;
                                    else
                                    {
                                        string entry = "[Def.Desc] " + defType.Name + "." + currentDef.defName + ": \"" + cleanVal + "\"";
                                        rep.MissingDefDescriptions.Add(entry);
                                        rep.MissingList.Add(entry);
                                    }
                                }
                                else
                                {
                                    rep.TotalDefOther++;
                                    if (isRu) rep.TranslatedDefOther++;
                                    else
                                    {
                                        string entry = "[Def.Field] " + defType.Name + "." + suggestedPath + ": \"" + cleanVal + "\"";
                                        rep.MissingDefOther.Add(entry);
                                        rep.MissingList.Add(entry);
                                    }
                                }
                            }
                            else if (currentValueCollection != null)
                            {
                                foreach (string itemVal in currentValueCollection)
                                {
                                    if (!DefInjectionUtility.ShouldCheckMissingInjection(itemVal, fieldInfo, currentDef)) continue;

                                    string cleanItem = itemVal != null ? itemVal.Trim() : "";
                                    if (string.IsNullOrEmpty(cleanItem) || cleanItem.Length <= 1) continue;

                                    bool isRuItem = ContainsCyrillic(cleanItem);
                                    rep.TotalDefOther++;
                                    if (isRuItem) rep.TranslatedDefOther++;
                                    else
                                    {
                                        string entry = "[Def.List] " + defType.Name + "." + suggestedPath + ": \"" + cleanItem + "\"";
                                        rep.MissingDefOther.Add(entry);
                                        rep.MissingList.Add(entry);
                                    }
                                }
                            }
                        }, modMeta);
                    }
                    catch { }
                }

                // -------------------------------------------------------------
                // 2. AUDIT KEYED STRINGS (Mod Settings, UI, Menus, Dialogs)
                // -------------------------------------------------------------
                if (LanguageDatabase.defaultLanguage != null && LanguageDatabase.defaultLanguage.keyedReplacements != null)
                {
                    foreach (KeyValuePair<string, LoadedLanguage.KeyedReplacement> kv in LanguageDatabase.defaultLanguage.keyedReplacements)
                    {
                        LoadedLanguage.KeyedReplacement kr = kv.Value;
                        if (kr == null || string.IsNullOrEmpty(kr.key)) continue;

                        if (!BelongsToMod(kr.fileSourceFullPath, mod)) continue;

                        string engVal = kr.value != null ? kr.value.Trim() : "";
                        if (string.IsNullOrEmpty(engVal) || engVal.Length <= 1) continue;

                        rep.TotalKeyed++;

                        bool isTranslated = false;
                        if (LanguageDatabase.activeLanguage != null)
                        {
                            TaggedString ts;
                            if (LanguageDatabase.activeLanguage.TryGetTextFromKey(kr.key, out ts))
                            {
                                string transStr = ts.RawText != null ? ts.RawText.Trim() : "";
                                if (ContainsCyrillic(transStr))
                                {
                                    isTranslated = true;
                                }
                                else
                                {
                                    LoadedLanguage.KeyedReplacement activeRep;
                                    if (LanguageDatabase.activeLanguage.keyedReplacements != null &&
                                        LanguageDatabase.activeLanguage.keyedReplacements.TryGetValue(kr.key, out activeRep))
                                    {
                                        if (activeRep != null && !activeRep.isPlaceholder &&
                                            !string.Equals(activeRep.value, "TODO", StringComparison.OrdinalIgnoreCase))
                                        {
                                            string fPath = activeRep.fileSourceFullPath;
                                            if (!string.IsNullOrEmpty(fPath) &&
                                                (fPath.IndexOf("Russian", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                 fPath.IndexOf("RUML", StringComparison.OrdinalIgnoreCase) >= 0))
                                            {
                                                if (!string.Equals(transStr, engVal, StringComparison.Ordinal) || engVal.Length < 4)
                                                {
                                                    isTranslated = true;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        if (isTranslated)
                        {
                            rep.TranslatedKeyed++;
                        }
                        else
                        {
                            string fName = !string.IsNullOrEmpty(kr.fileSource) ? kr.fileSource : Path.GetFileName(kr.fileSourceFullPath);
                            string entry = "[Keyed] " + fName + " -> " + kr.key + ": \"" + engVal + "\"";
                            rep.MissingKeyedList.Add(entry);
                            rep.MissingList.Add(entry);
                        }
                    }
                }

                // Calculate aggregates
                rep.TotalDefs = rep.TotalDefLabels + rep.TotalDefDescriptions + rep.TotalDefOther;
                rep.TranslatedDefs = rep.TranslatedDefLabels + rep.TranslatedDefDescriptions + rep.TranslatedDefOther;
                rep.MissingDefs = rep.TotalDefs - rep.TranslatedDefs;

                rep.MissingKeyed = rep.TotalKeyed - rep.TranslatedKeyed;

                rep.TotalItems = rep.TotalDefs + rep.TotalKeyed;
                rep.TranslatedItems = rep.TranslatedDefs + rep.TranslatedKeyed;
                rep.MissingItems = rep.TotalItems - rep.TranslatedItems;

                rep.Percent = rep.TotalItems > 0 ? ((float)rep.TranslatedItems / rep.TotalItems * 100f) : 100f;
                reports.Add(rep);
            }

            // Sort: mods with missing items first (lowest percent first), then fully translated by name
            reports.Sort(delegate(ModAuditReport a, ModAuditReport b)
            {
                if (a.MissingItems > 0 && b.MissingItems == 0) return -1;
                if (a.MissingItems == 0 && b.MissingItems > 0) return 1;
                if (a.MissingItems > 0 && b.MissingItems > 0)
                {
                    return a.Percent.CompareTo(b.Percent);
                }
                return string.Compare(a.ModName, b.ModName, StringComparison.OrdinalIgnoreCase);
            });

            return reports;
        }

        private static bool BelongsToMod(string filePath, ModContentPack mod)
        {
            if (string.IsNullOrEmpty(filePath) || mod == null) return false;
            try
            {
                string normPath = Path.GetFullPath(filePath);
                if (!string.IsNullOrEmpty(mod.RootDir))
                {
                    string normRoot = Path.GetFullPath(mod.RootDir);
                    if (normPath.StartsWith(normRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                if (mod.foldersToLoadDescendingOrder != null)
                {
                    foreach (string folder in mod.foldersToLoadDescendingOrder)
                    {
                        if (!string.IsNullOrEmpty(folder))
                        {
                            string normFolder = Path.GetFullPath(folder);
                            if (normPath.StartsWith(normFolder, StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        public static bool ContainsCyrillic(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c >= 0x0400 && c <= 0x04FF) return true;
            }
            return false;
        }

        public static string ExportReportToFile(List<ModAuditReport> reports)
        {
            string desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string file = Path.Combine(desk, "RUML_Translation_Audit_Report.txt");

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("================================================================================");
            sb.AppendLine("ОТЧЁТ ПОКРЫТИЯ ПЕРЕВОДОВ RUML (АВТОМАТИЧЕСКИЙ АУДИТ ДВИЖКА RIMWORLD)");
            sb.AppendLine("Дата создания: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Всего проверено модов: " + reports.Count);
            sb.AppendLine("================================================================================");
            sb.AppendLine();

            foreach (var r in reports)
            {
                string tag = r.IsRUMLMod ? " [RUML]" : "";
                sb.AppendLine("[" + r.ModName + " (" + r.PackageId + ")" + tag + "]");
                sb.AppendLine("  Общее покрытие: " + r.Percent.ToString("F1") + "% (" + r.TranslatedItems + " / " + r.TotalItems + ")");
                sb.AppendLine("  Дефы (всего " + r.TotalDefs + "): Названия (" + r.TranslatedDefLabels + "/" + r.TotalDefLabels + "), " +
                              "Описания (" + r.TranslatedDefDescriptions + "/" + r.TotalDefDescriptions + "), " +
                              "Прочее (" + r.TranslatedDefOther + "/" + r.TotalDefOther + ")");
                sb.AppendLine("  Keyed / Настройки: " + r.TranslatedKeyed + " / " + r.TotalKeyed + " (не переведено: " + r.MissingKeyed + ")");
                sb.AppendLine("  Всего не переведено: " + r.MissingItems);

                if (r.MissingList.Count > 0)
                {
                    sb.AppendLine("  Примеры отсутствующих строк (" + r.MissingList.Count + "):");
                    int maxExamples = Math.Min(r.MissingList.Count, 100);
                    for (int i = 0; i < maxExamples; i++)
                    {
                        sb.AppendLine("    * " + r.MissingList[i]);
                    }
                    if (r.MissingList.Count > maxExamples)
                    {
                        sb.AppendLine("    ... и ещё " + (r.MissingList.Count - maxExamples) + " непереведённых строк.");
                    }
                }
                sb.AppendLine();
            }

            File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
            return file;
        }
    }
}
