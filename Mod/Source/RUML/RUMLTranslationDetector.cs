// RimWorld Version: 1.6 | .NET Framework: 4.7.2/4.8
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RimWorld;
using Verse;

namespace RUML
{
    public class ActiveTranslationModInfo
    {
        public string ModName;
        public string PackageId;
        public string PackageIdPlayerFacing;

        public ActiveTranslationModInfo(string name, string pid, string pidPlayerFacing)
        {
            ModName = name ?? "";
            PackageId = pid ?? "";
            PackageIdPlayerFacing = pidPlayerFacing ?? pid ?? "";
        }
    }

    public class TargetModInfo
    {
        public string TargetName;
        public string PackageId;

        public TargetModInfo(string name, string pid)
        {
            TargetName = name ?? "";
            PackageId = pid ?? "";
        }
    }

    public static class RUMLTranslationDetector
    {
        private static bool isInitialized = false;

        // TargetPackageId -> List of Translation Mods translating it
        private static readonly Dictionary<string, List<ActiveTranslationModInfo>> targetToTranslations =
            new Dictionary<string, List<ActiveTranslationModInfo>>(StringComparer.OrdinalIgnoreCase);

        // TranslationPackageId -> List of Target Mods it translates
        private static readonly Dictionary<string, List<TargetModInfo>> translationToTargets =
            new Dictionary<string, List<TargetModInfo>>(StringComparer.OrdinalIgnoreCase);

        // PackageIds of content mods that contain their own built-in translation for active/target language
        private static readonly HashSet<string> builtInTranslationMods =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> IgnoredPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ludeon.rimworld",
            "ludeon.rimworld.royalty",
            "ludeon.rimworld.ideology",
            "ludeon.rimworld.biotech",
            "ludeon.rimworld.anomaly",
            "ludeon.rimworld.odyssey",
            "brrainz.harmony",
            "unlimitedhugs.hugslib",
            "ruml.rimworlduniversalmodslocalization"
        };

        private static readonly Dictionary<string, string[]> LanguageTokens = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "English", new string[] { "[en]", "(en)", "[eng]", "(eng)", "english", ".en", "_en", ".eng", "_eng" } },
            { "Russian", new string[] { "[ru]", "(ru)", "[rus]", "(rus)", "russian", "русский", "перевод", ".ru", "_ru", ".rus", "_rus" } },
            { "ChineseSimplified", new string[] { "[zh]", "(zh)", "[cn]", "(cn)", "chinese", "simplified", "中文", "汉化", "简体", ".zh", ".cn" } },
            { "ChineseTraditional", new string[] { "[zh]", "(zh)", "[cht]", "(cht)", "traditional", "繁體", "繁体", "中文", "漢化", ".zh", ".cht" } },
            { "German", new string[] { "[de]", "(de)", "[ger]", "(ger)", "german", "deutsch", ".de" } },
            { "French", new string[] { "[fr]", "(fr)", "[fra]", "(fra)", "french", "français", "francais", ".fr" } },
            { "Spanish", new string[] { "[es]", "(es)", "[esp]", "(esp)", "spanish", "español", "espanol", ".es" } },
            { "Korean", new string[] { "[ko]", "(ko)", "[kor]", "(kor)", "korean", "한국어", "한글", ".ko" } },
            { "Japanese", new string[] { "[ja]", "(ja)", "[jpn]", "(jpn)", "japanese", "日本語", ".ja" } },
            { "Polish", new string[] { "[pl]", "(pl)", "[pol]", "(pol)", "polish", "polski", ".pl" } },
            { "Ukrainian", new string[] { "[uk]", "(uk)", "[ua]", "(ua)", "ukrainian", "українська", "украинский", ".uk", ".ua" } }
        };

        public static void EnsureInitialized()
        {
            if (isInitialized) return;
            ScanRunningMods();
        }

        public static void ForceRescan()
        {
            ScanRunningMods();
        }

        public static int TotalTargetsCount
        {
            get
            {
                EnsureInitialized();
                return targetToTranslations.Count;
            }
        }

        public static int TotalTranslationModsCount
        {
            get
            {
                EnsureInitialized();
                return translationToTargets.Count;
            }
        }

        public static int TotalBuiltInCount
        {
            get
            {
                EnsureInitialized();
                return builtInTranslationMods.Count;
            }
        }

        public static HashSet<string> GetAllTargetPackageIds()
        {
            EnsureInitialized();
            return new HashSet<string>(targetToTranslations.Keys, StringComparer.OrdinalIgnoreCase);
        }

        public static HashSet<string> GetAllTranslationPackageIds()
        {
            EnsureInitialized();
            return new HashSet<string>(translationToTargets.Keys, StringComparer.OrdinalIgnoreCase);
        }

        public static HashSet<string> GetAllBuiltInPackageIds()
        {
            EnsureInitialized();
            return new HashSet<string>(builtInTranslationMods, StringComparer.OrdinalIgnoreCase);
        }

        // =========================================================================
        // BUILT-IN TRANSLATION QUERIES
        // =========================================================================

        public static bool HasBuiltInTranslation(string packageId)
        {
            EnsureInitialized();
            if (string.IsNullOrEmpty(packageId)) return false;
            return builtInTranslationMods.Contains(packageId);
        }

        public static bool HasBuiltInRussianTranslation(string packageId)
        {
            return HasBuiltInTranslation(packageId);
        }

        public static string GetBuiltInModBadge(string packageId)
        {
            if (!HasBuiltInTranslation(packageId))
            {
                return "";
            }
            string badgeText = "RUML_BadgeBuiltIn".CanTranslate() ? (string)"RUML_BadgeBuiltIn".Translate() : "[Встроенный перевод]";
            return " <color=#64B5F6>" + badgeText + "</color>";
        }

        public static string GetBuiltInModTooltip(string packageId)
        {
            if (!HasBuiltInTranslation(packageId))
            {
                return null;
            }
            string targetLang = RUMLFolderManager.GetTargetLanguageFolder();
            if ("RUML_TipBuiltIn".CanTranslate())
            {
                return "RUML_TipBuiltIn".Translate(targetLang);
            }
            return "Этот мод содержит встроенную локализацию от автора (папка Languages/" + targetLang + ").";
        }

        // =========================================================================
        // TARGET MOD QUERIES (Does base mod X have an active translation mod?)
        // =========================================================================

        public static bool HasActiveTranslation(string targetPackageId, out List<ActiveTranslationModInfo> translationMods)
        {
            EnsureInitialized();
            if (string.IsNullOrEmpty(targetPackageId))
            {
                translationMods = null;
                return false;
            }
            return targetToTranslations.TryGetValue(targetPackageId, out translationMods) && translationMods != null && translationMods.Count > 0;
        }

        public static string GetTargetModBadge(string targetPackageId)
        {
            List<ActiveTranslationModInfo> list;
            if (!HasActiveTranslation(targetPackageId, out list))
            {
                return "";
            }

            string badgeText = "RUML_BadgeExternal".CanTranslate() ? (string)"RUML_BadgeExternal".Translate() : "[Переведено внешним модом]";
            if (list.Count > 1)
            {
                return " <color=#78D070>" + badgeText.TrimEnd(']') + " (+" + (list.Count - 1) + ")]</color>";
            }
            return " <color=#78D070>" + badgeText + "</color>";
        }

        public static string GetTargetModTooltip(string targetPackageId)
        {
            List<ActiveTranslationModInfo> list;
            if (!HasActiveTranslation(targetPackageId, out list))
            {
                return null;
            }

            if (list.Count == 1)
            {
                string tSingle = "RUML_TipTargetSingle".CanTranslate() 
                    ? (string)"RUML_TipTargetSingle".Translate() 
                    : "Для этого мода в игре включен отдельный внешний мод перевода:\n";
                return tSingle + "• " + list[0].ModName + " (" + list[0].PackageIdPlayerFacing + ")";
            }

            string tMulti = "RUML_TipTargetMulti".CanTranslate()
                ? (string)"RUML_TipTargetMulti".Translate(list.Count)
                : ("Для этого мода в игре включено несколько внешних модов перевода (" + list.Count + "):\n");
            for (int i = 0; i < list.Count; i++)
            {
                tMulti += "• " + list[i].ModName + " (" + list[i].PackageIdPlayerFacing + ")\n";
            }
            return tMulti.TrimEnd();
        }

        // =========================================================================
        // TRANSLATION MOD QUERIES (Is mod X itself a translation mod for mod Y?)
        // =========================================================================

        public static bool IsTranslationMod(string transPackageId, out List<TargetModInfo> targetMods)
        {
            EnsureInitialized();
            if (string.IsNullOrEmpty(transPackageId))
            {
                targetMods = null;
                return false;
            }
            return translationToTargets.TryGetValue(transPackageId, out targetMods) && targetMods != null && targetMods.Count > 0;
        }

        public static string GetTranslationModSelfBadge(string transPackageId, string transModName = null, int maxChars = 20)
        {
            List<TargetModInfo> targets;
            if (!IsTranslationMod(transPackageId, out targets))
            {
                return "";
            }

            string packBadge = "RUML_BadgeTranslationMod".CanTranslate() ? (string)"RUML_BadgeTranslationMod".Translate() : "[Мод-перевод]";
            string transPrefix = "RUML_BadgeTranslationFor".CanTranslate() ? (string)"RUML_BadgeTranslationFor".Translate() : "Перевод: ";

            // If the translation mod's own name already contains the target mod's name,
            // a concise [Мод-перевод] badge is clean, readable, and prevents line overflow.
            if (!string.IsNullOrEmpty(transModName) && targets.Count == 1)
            {
                string cleanTrans = CleanModNameForMatching(transModName);
                string cleanTarget = CleanModNameForMatching(targets[0].TargetName);
                if (!string.IsNullOrEmpty(cleanTarget) && cleanTrans.Contains(cleanTarget))
                {
                    return " <color=#40E0D0>" + packBadge + "</color>";
                }
            }

            if (targets.Count == 1)
            {
                string tName = targets[0].TargetName;
                if (tName.Length > maxChars)
                {
                    tName = tName.Substring(0, maxChars - 2) + "..";
                }
                return " <color=#40E0D0>[" + transPrefix + tName + "]</color>";
            }

            if (targets.Count > 1)
            {
                string first = targets[0].TargetName;
                if (first.Length > 14) first = first.Substring(0, 12) + "..";
                return " <color=#40E0D0>[" + transPrefix + first + " (+" + (targets.Count - 1) + ")]</color>";
            }

            return " <color=#40E0D0>" + packBadge + "</color>";
        }

        public static string GetTranslationModSelfTooltip(string transPackageId)
        {
            List<TargetModInfo> targets;
            if (!IsTranslationMod(transPackageId, out targets))
            {
                return null;
            }

            if (targets.Count == 1)
            {
                string tSingle = "RUML_TipTransModSingle".CanTranslate()
                    ? (string)"RUML_TipTransModSingle".Translate()
                    : "Этот мод является сторонней локализацией для:\n";
                return tSingle + "• " + targets[0].TargetName + " (" + targets[0].PackageId + ")";
            }

            string text = "RUML_TipTransModMulti".CanTranslate()
                ? (string)"RUML_TipTransModMulti".Translate(targets.Count)
                : ("Этот языковой пакет переводит следующие моды (" + targets.Count + "):\n");
            for (int i = 0; i < targets.Count; i++)
            {
                text += "• " + targets[i].TargetName + " (" + targets[i].PackageId + ")\n";
            }
            return text.TrimEnd();
        }

        // =========================================================================
        // INTERNAL SCANNING & SMART MATCHING
        // =========================================================================

        public static void ScanRunningMods()
        {
            targetToTranslations.Clear();
            translationToTargets.Clear();
            builtInTranslationMods.Clear();

            var running = LoadedModManager.RunningMods.ToList();
            if (running == null || running.Count == 0)
            {
                isInitialized = true;
                return;
            }

            string targetLang = RUMLFolderManager.GetTargetLanguageFolder();

            var runningPidMap = new Dictionary<string, ModContentPack>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < running.Count; i++)
            {
                var m = running[i];
                if (!string.IsNullOrEmpty(m.PackageId) && !runningPidMap.ContainsKey(m.PackageId))
                {
                    runningPidMap[m.PackageId] = m;
                }
                if (!string.IsNullOrEmpty(m.PackageIdPlayerFacing) && !runningPidMap.ContainsKey(m.PackageIdPlayerFacing))
                {
                    runningPidMap[m.PackageIdPlayerFacing] = m;
                }
            }

            // Pass 1: Find all translation mods and mods with built-in translations for target language
            var detectedTransMods = new List<ModContentPack>();
            for (int i = 0; i < running.Count; i++)
            {
                var mod = running[i];
                if (mod == null || string.IsNullOrEmpty(mod.PackageId)) continue;
                if (IgnoredPackageIds.Contains(mod.PackageId)) continue;

                if (IsEligibleTranslationMod(mod, targetLang))
                {
                    detectedTransMods.Add(mod);
                }
                else if (HasLanguageFolder(mod, targetLang))
                {
                    builtInTranslationMods.Add(mod.PackageId);
                    if (!string.IsNullOrEmpty(mod.PackageIdPlayerFacing))
                    {
                        builtInTranslationMods.Add(mod.PackageIdPlayerFacing);
                    }
                }
            }

            // Pass 2: Smart-match each translation mod to its true target mod(s)
            for (int i = 0; i < detectedTransMods.Count; i++)
            {
                var transMod = detectedTransMods[i];
                var targetPids = ResolveBestTargetMods(transMod, running);

                var transInfo = new ActiveTranslationModInfo(transMod.Name, transMod.PackageId, transMod.PackageIdPlayerFacing);

                var targetInfoList = new List<TargetModInfo>();
                for (int t = 0; t < targetPids.Count; t++)
                {
                    string tPid = targetPids[t];
                    ModContentPack targetMod;
                    string tName = runningPidMap.TryGetValue(tPid, out targetMod) ? targetMod.Name : tPid;

                    targetInfoList.Add(new TargetModInfo(tName, tPid));

                    // Register target -> translation
                    List<ActiveTranslationModInfo> tList;
                    if (!targetToTranslations.TryGetValue(tPid, out tList))
                    {
                        tList = new List<ActiveTranslationModInfo>();
                        targetToTranslations[tPid] = tList;
                    }
                    if (!tList.Any(x => string.Equals(x.PackageId, transInfo.PackageId, StringComparison.OrdinalIgnoreCase)))
                    {
                        tList.Add(transInfo);
                    }
                }

                if (targetInfoList.Count > 0)
                {
                    translationToTargets[transMod.PackageId] = targetInfoList;
                    if (!string.Equals(transMod.PackageId, transMod.PackageIdPlayerFacing, StringComparison.OrdinalIgnoreCase))
                    {
                        translationToTargets[transMod.PackageIdPlayerFacing] = targetInfoList;
                    }
                }
                else
                {
                    // Pure translation mod with no explicit target found
                    var genericTarget = new List<TargetModInfo>();
                    translationToTargets[transMod.PackageId] = genericTarget;
                }
            }

            isInitialized = true;
        }

        private static bool IsEligibleTranslationMod(ModContentPack mod, string targetLang)
        {
            if (mod == null) return false;
            string pid = mod.PackageId;
            if (string.IsNullOrEmpty(pid) || IgnoredPackageIds.Contains(pid)) return false;

            if (!HasLanguageFolder(mod, targetLang)) return false;

            string nameLower = (mod.Name ?? "").ToLowerInvariant();
            string pidLower = pid.ToLowerInvariant();

            bool hasLangToken = false;
            string[] tokens;
            if (LanguageTokens.TryGetValue(targetLang, out tokens) && tokens != null)
            {
                for (int i = 0; i < tokens.Length; i++)
                {
                    if (nameLower.Contains(tokens[i]) || pidLower.Contains(tokens[i]))
                    {
                        hasLangToken = true;
                        break;
                    }
                }
            }

            bool hasGenericTransName = nameLower.Contains("translation") ||
                                       nameLower.Contains("language pack") ||
                                       nameLower.Contains("localization");

            bool hasGenericTransPid = pidLower.Contains("translation") ||
                                      pidLower.Contains("localization");

            bool isPureTranslation = (mod.assemblies == null || mod.assemblies.loadedAssemblies.Count == 0) &&
                                     (mod.AllDefs == null || !mod.AllDefs.Any());

            return hasLangToken || hasGenericTransName || hasGenericTransPid || isPureTranslation;
        }

        private static List<string> ResolveBestTargetMods(ModContentPack transMod, List<ModContentPack> allRunning)
        {
            var result = new List<string>();
            if (transMod == null || transMod.ModMetaData == null) return result;

            string tPid = transMod.PackageId.ToLowerInvariant();
            string cleanTName = CleanModNameForMatching(transMod.Name);
            string[] tTokens = cleanTName.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var tTokenSet = new HashSet<string>(tTokens, StringComparer.OrdinalIgnoreCase);

            // Collect all potential candidate target packageIds
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (transMod.ModMetaData.LoadAfter != null)
            {
                for (int i = 0; i < transMod.ModMetaData.LoadAfter.Count; i++)
                {
                    string p = transMod.ModMetaData.LoadAfter[i];
                    if (IsValidCandidatePid(p, transMod.PackageId)) candidates.Add(p);
                }
            }
            if (transMod.ModMetaData.ForceLoadAfter != null)
            {
                for (int i = 0; i < transMod.ModMetaData.ForceLoadAfter.Count; i++)
                {
                    string p = transMod.ModMetaData.ForceLoadAfter[i];
                    if (IsValidCandidatePid(p, transMod.PackageId)) candidates.Add(p);
                }
            }
            if (transMod.ModMetaData.Dependencies != null)
            {
                for (int i = 0; i < transMod.ModMetaData.Dependencies.Count; i++)
                {
                    var dep = transMod.ModMetaData.Dependencies[i];
                    if (dep != null && IsValidCandidatePid(dep.packageId, transMod.PackageId))
                    {
                        candidates.Add(dep.packageId);
                    }
                }
            }

            // Also check running mods whose packageId is contained in transMod.PackageId
            for (int i = 0; i < allRunning.Count; i++)
            {
                var cand = allRunning[i];
                if (cand == null || string.IsNullOrEmpty(cand.PackageId)) continue;
                string cPid = cand.PackageId.ToLowerInvariant();
                if (IsValidCandidatePid(cPid, transMod.PackageId))
                {
                    if (tPid.Contains(cPid) || cPid.Contains(tPid))
                    {
                        candidates.Add(cand.PackageId);
                    }
                }
            }

            if (candidates.Count == 0) return result;

            // Score each candidate
            var scored = new List<Tuple<int, string>>();
            foreach (var candPid in candidates)
            {
                var candMod = allRunning.FirstOrDefault(m =>
                    string.Equals(m.PackageId, candPid, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(m.PackageIdPlayerFacing, candPid, StringComparison.OrdinalIgnoreCase));

                if (candMod == null) continue;

                string candName = CleanModNameForMatching(candMod.Name);
                string[] cTokens = candName.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                int overlap = 0;
                for (int j = 0; j < cTokens.Length; j++)
                {
                    if (tTokenSet.Contains(cTokens[j])) overlap++;
                }

                string cPidLower = candMod.PackageId.ToLowerInvariant();
                int pidBonus = (tPid.Contains(cPidLower) || cPidLower.Contains(tPid)) ? 3 : 0;
                int nameBonus = (cleanTName.Contains(candName) || candName.Contains(cleanTName)) ? 2 : 0;

                int score = overlap + pidBonus + nameBonus;
                scored.Add(new Tuple<int, string>(score, candMod.PackageIdPlayerFacing));
            }

            if (scored.Count == 0) return result;

            scored.Sort((a, b) => b.Item1.CompareTo(a.Item1));
            int maxScore = scored[0].Item1;

            if (maxScore >= 2)
            {
                for (int i = 0; i < scored.Count; i++)
                {
                    if (scored[i].Item1 >= 2 && scored[i].Item1 >= maxScore - 1)
                    {
                        result.Add(scored[i].Item2);
                    }
                }
            }
            else if (candidates.Count == 1)
            {
                result.Add(scored[0].Item2);
            }

            return result;
        }

        private static bool IsValidCandidatePid(string pid, string selfPid)
        {
            if (string.IsNullOrEmpty(pid)) return false;
            if (IgnoredPackageIds.Contains(pid)) return false;
            if (string.Equals(pid, selfPid, StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        private static string CleanModNameForMatching(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            string n = Regex.Replace(name, @"(?i)\b(russian|english|chinese|german|french|spanish|korean|japanese|polish|ukrainian|language|pack|translation|patch|rus|ru|en|zh|cn|de|fr|es|ko|ja|pl|uk|ua|перевод|русский|язык|中文|汉化|日本語|한국어)\b", "");
            n = Regex.Replace(n, @"[\[\]\(\)\-_:—]", " ");
            string[] parts = n.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", parts).ToLowerInvariant();
        }

        public static bool HasLanguageFolder(ModContentPack mod, string langFolder)
        {
            if (mod == null || string.IsNullOrEmpty(langFolder)) return false;
            string norm = RUMLFolderManager.NormalizeLanguageName(langFolder);
            try
            {
                if (mod.foldersToLoadDescendingOrder != null)
                {
                    for (int i = 0; i < mod.foldersToLoadDescendingOrder.Count; i++)
                    {
                        string folder = mod.foldersToLoadDescendingOrder[i];
                        if (!string.IsNullOrEmpty(folder))
                        {
                            if (Directory.Exists(Path.Combine(folder, "Languages", langFolder))) return true;
                            if (File.Exists(Path.Combine(folder, "Languages", langFolder + ".tar"))) return true;

                            if (!string.Equals(norm, langFolder, StringComparison.OrdinalIgnoreCase))
                            {
                                if (Directory.Exists(Path.Combine(folder, "Languages", norm))) return true;
                                if (File.Exists(Path.Combine(folder, "Languages", norm + ".tar"))) return true;
                            }
                        }
                    }
                }

                if (!string.IsNullOrEmpty(mod.RootDir))
                {
                    if (Directory.Exists(Path.Combine(mod.RootDir, "Languages", langFolder))) return true;
                    if (File.Exists(Path.Combine(mod.RootDir, "Languages", langFolder + ".tar"))) return true;

                    if (!string.Equals(norm, langFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        if (Directory.Exists(Path.Combine(mod.RootDir, "Languages", norm))) return true;
                        if (File.Exists(Path.Combine(mod.RootDir, "Languages", norm + ".tar"))) return true;
                    }
                }
            }
            catch
            {
            }
            return false;
        }

        public static bool HasRussianLanguageFolder(ModContentPack mod)
        {
            return HasLanguageFolder(mod, "Russian");
        }
    }
}
