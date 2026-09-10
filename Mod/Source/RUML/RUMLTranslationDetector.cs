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

            if (list.Count > 1)
            {
                return " <color=#78D070>[Есть перевод (+" + (list.Count - 1) + ")]</color>";
            }
            return " <color=#78D070>[Есть перевод]</color>";
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
                return "Для этого мода в игре включен отдельный мод перевода:\n• " + list[0].ModName + " (" + list[0].PackageIdPlayerFacing + ")";
            }

            string text = "Для этого мода в игре включено несколько модов перевода (" + list.Count + "):\n";
            for (int i = 0; i < list.Count; i++)
            {
                text += "• " + list[i].ModName + " (" + list[i].PackageIdPlayerFacing + ")\n";
            }
            return text.TrimEnd();
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

        public static string GetTranslationModSelfBadge(string transPackageId, int maxChars = 24)
        {
            List<TargetModInfo> targets;
            if (!IsTranslationMod(transPackageId, out targets))
            {
                return "";
            }

            if (targets.Count == 1)
            {
                string tName = targets[0].TargetName;
                if (tName.Length > maxChars)
                {
                    tName = tName.Substring(0, maxChars - 2) + "..";
                }
                return " <color=#40E0D0>[Перевод для: " + tName + "]</color>";
            }

            if (targets.Count > 1)
            {
                string first = targets[0].TargetName;
                if (first.Length > 16) first = first.Substring(0, 14) + "..";
                return " <color=#40E0D0>[Перевод: " + first + " (+" + (targets.Count - 1) + ")]</color>";
            }

            return " <color=#40E0D0>[Мод-перевод]</color>";
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
                return "Этот мод является сторонней локализацией для:\n• " + targets[0].TargetName + " (" + targets[0].PackageId + ")";
            }

            string text = "Этот языковой пакет переводит следующие моды (" + targets.Count + "):\n";
            for (int i = 0; i < targets.Count; i++)
            {
                text += "• " + targets[i].TargetName + " (" + targets[i].PackageId + ")\n";
            }
            return text.TrimEnd();
        }

        // =========================================================================
        // INTERNAL SCANNING & SMART MATCHING
        // =========================================================================

        private static void ScanRunningMods()
        {
            targetToTranslations.Clear();
            translationToTargets.Clear();

            var running = LoadedModManager.RunningMods.ToList();
            if (running == null || running.Count == 0)
            {
                isInitialized = true;
                return;
            }

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

            // Pass 1: Find all translation mods
            var detectedTransMods = new List<ModContentPack>();
            for (int i = 0; i < running.Count; i++)
            {
                var mod = running[i];
                if (mod == null || string.IsNullOrEmpty(mod.PackageId)) continue;
                if (IgnoredPackageIds.Contains(mod.PackageId)) continue;

                if (IsEligibleTranslationMod(mod))
                {
                    detectedTransMods.Add(mod);
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

        private static bool IsEligibleTranslationMod(ModContentPack mod)
        {
            if (mod == null) return false;
            string pid = mod.PackageId;
            if (string.IsNullOrEmpty(pid) || IgnoredPackageIds.Contains(pid)) return false;

            if (!HasRussianLanguageFolder(mod)) return false;

            string nameLower = (mod.Name ?? "").ToLowerInvariant();
            string pidLower = pid.ToLowerInvariant();

            bool hasTransName = nameLower.Contains("russian") ||
                                nameLower.Contains("[ru]") ||
                                nameLower.Contains("(ru)") ||
                                nameLower.Contains("[rus]") ||
                                nameLower.Contains("(rus)") ||
                                nameLower.Contains("русский") ||
                                nameLower.Contains("перевод") ||
                                nameLower.Contains("language pack");

            bool hasTransPid = pidLower.Contains(".ru") ||
                               pidLower.Contains("_ru") ||
                               pidLower.Contains("translation") ||
                               pidLower.Contains(".rus") ||
                               pidLower.Contains("_rus");

            bool isPureTranslation = (mod.assemblies == null || mod.assemblies.loadedAssemblies.Count == 0) &&
                                     (mod.AllDefs == null || !mod.AllDefs.Any());

            return hasTransName || hasTransPid || isPureTranslation;
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
            string n = Regex.Replace(name, @"(?i)\b(russian|language|pack|translation|rus|ru|перевод|русский|язык)\b", "");
            n = Regex.Replace(n, @"[\[\]\(\)\-_:—]", " ");
            string[] parts = n.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", parts).ToLowerInvariant();
        }

        private static bool HasRussianLanguageFolder(ModContentPack mod)
        {
            if (mod == null) return false;
            try
            {
                if (mod.foldersToLoadDescendingOrder != null)
                {
                    for (int i = 0; i < mod.foldersToLoadDescendingOrder.Count; i++)
                    {
                        string folder = mod.foldersToLoadDescendingOrder[i];
                        if (!string.IsNullOrEmpty(folder))
                        {
                            string ruDir = Path.Combine(folder, "Languages", "Russian");
                            if (Directory.Exists(ruDir)) return true;
                            string ruTar = Path.Combine(folder, "Languages", "Russian.tar");
                            if (File.Exists(ruTar)) return true;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(mod.RootDir))
                {
                    string ruDir = Path.Combine(mod.RootDir, "Languages", "Russian");
                    if (Directory.Exists(ruDir)) return true;
                    string ruTar = Path.Combine(mod.RootDir, "Languages", "Russian.tar");
                    if (File.Exists(ruTar)) return true;
                }
            }
            catch
            {
            }
            return false;
        }
    }
}
