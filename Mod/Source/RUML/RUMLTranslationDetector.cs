// RimWorld Version: 1.6 | .NET Framework: 4.7.2/4.8
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    public static class RUMLTranslationDetector
    {
        private static bool isInitialized = false;
        private static readonly Dictionary<string, List<ActiveTranslationModInfo>> targetToTranslations =
            new Dictionary<string, List<ActiveTranslationModInfo>>(StringComparer.OrdinalIgnoreCase);

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

        public static HashSet<string> GetAllTargetPackageIds()
        {
            EnsureInitialized();
            return new HashSet<string>(targetToTranslations.Keys, StringComparer.OrdinalIgnoreCase);
        }

        public static bool HasTranslationMod(string targetPackageId, out List<ActiveTranslationModInfo> translationMods)
        {
            EnsureInitialized();
            if (string.IsNullOrEmpty(targetPackageId))
            {
                translationMods = null;
                return false;
            }
            return targetToTranslations.TryGetValue(targetPackageId, out translationMods) && translationMods != null && translationMods.Count > 0;
        }

        public static bool HasTranslationMod(string targetPackageId, out string primaryModName)
        {
            List<ActiveTranslationModInfo> list;
            if (HasTranslationMod(targetPackageId, out list))
            {
                primaryModName = list[0].ModName;
                return true;
            }
            primaryModName = null;
            return false;
        }

        public static string GetTranslationBadge(string targetPackageId, int maxChars = 24)
        {
            List<ActiveTranslationModInfo> list;
            if (!HasTranslationMod(targetPackageId, out list))
            {
                return "";
            }

            string rawName = list[0].ModName;
            string clean = CleanTranslationModName(rawName);
            if (clean.Length > maxChars)
            {
                clean = clean.Substring(0, maxChars - 2) + "..";
            }

            if (list.Count > 1)
            {
                return " <color=#78D070>[Перевод: " + clean + " (+" + (list.Count - 1) + ")]</color>";
            }
            return " <color=#78D070>[Перевод: " + clean + "]</color>";
        }

        public static string GetTranslationTooltip(string targetPackageId)
        {
            List<ActiveTranslationModInfo> list;
            if (!HasTranslationMod(targetPackageId, out list))
            {
                return null;
            }

            if (list.Count == 1)
            {
                return "Для этого мода в игре уже включен отдельный мод перевода:\n• " + list[0].ModName + " (" + list[0].PackageIdPlayerFacing + ")";
            }

            string text = "Для этого мода в игре включено несколько отдельных модов перевода (" + list.Count + "):\n";
            for (int i = 0; i < list.Count; i++)
            {
                text += "• " + list[i].ModName + " (" + list[i].PackageIdPlayerFacing + ")\n";
            }
            return text.TrimEnd();
        }

        private static string CleanTranslationModName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Мод перевода";
            string clean = name;
            clean = clean.Replace("Russian Language Pack", "").Replace("Russian Translation", "")
                         .Replace("Русский перевод", "").Replace("Русский язык", "")
                         .Replace("Russian", "").Replace("[RU]", "").Replace("[RUS]", "")
                         .Replace("(RU)", "").Replace("(RUS)", "").Trim();
            clean = clean.Trim('-', ':', '—', ' ').Trim();
            if (string.IsNullOrEmpty(clean)) return name;
            return clean;
        }

        private static void ScanRunningMods()
        {
            targetToTranslations.Clear();
            var running = LoadedModManager.RunningMods.ToList();
            if (running == null || running.Count == 0)
            {
                isInitialized = true;
                return;
            }

            var runningPidSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < running.Count; i++)
            {
                var m = running[i];
                if (!string.IsNullOrEmpty(m.PackageId))
                {
                    runningPidSet.Add(m.PackageId);
                }
                if (!string.IsNullOrEmpty(m.PackageIdPlayerFacing))
                {
                    runningPidSet.Add(m.PackageIdPlayerFacing);
                }
            }

            for (int i = 0; i < running.Count; i++)
            {
                var mod = running[i];
                if (mod == null || string.IsNullOrEmpty(mod.PackageId)) continue;
                if (IgnoredPackageIds.Contains(mod.PackageId)) continue;

                if (!IsStandaloneTranslationMod(mod)) continue;

                var targets = ExtractTargetPackageIds(mod, runningPidSet);
                var info = new ActiveTranslationModInfo(mod.Name, mod.PackageId, mod.PackageIdPlayerFacing);

                foreach (var targetPid in targets)
                {
                    List<ActiveTranslationModInfo> list;
                    if (!targetToTranslations.TryGetValue(targetPid, out list))
                    {
                        list = new List<ActiveTranslationModInfo>();
                        targetToTranslations[targetPid] = list;
                    }
                    if (!list.Any(x => string.Equals(x.PackageId, info.PackageId, StringComparison.OrdinalIgnoreCase)))
                    {
                        list.Add(info);
                    }
                }
            }

            isInitialized = true;
        }

        private static bool IsStandaloneTranslationMod(ModContentPack mod)
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
                // Ignore filesystem read exceptions during mod folder check
            }
            return false;
        }

        private static HashSet<string> ExtractTargetPackageIds(ModContentPack transMod, HashSet<string> runningPidSet)
        {
            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (transMod == null || transMod.ModMetaData == null) return targets;

            if (transMod.ModMetaData.LoadAfter != null)
            {
                for (int i = 0; i < transMod.ModMetaData.LoadAfter.Count; i++)
                {
                    string p = transMod.ModMetaData.LoadAfter[i];
                    if (IsValidTarget(p, transMod.PackageId, runningPidSet))
                    {
                        targets.Add(p);
                    }
                }
            }

            if (transMod.ModMetaData.ForceLoadAfter != null)
            {
                for (int i = 0; i < transMod.ModMetaData.ForceLoadAfter.Count; i++)
                {
                    string p = transMod.ModMetaData.ForceLoadAfter[i];
                    if (IsValidTarget(p, transMod.PackageId, runningPidSet))
                    {
                        targets.Add(p);
                    }
                }
            }

            if (transMod.ModMetaData.Dependencies != null)
            {
                for (int i = 0; i < transMod.ModMetaData.Dependencies.Count; i++)
                {
                    var dep = transMod.ModMetaData.Dependencies[i];
                    if (dep != null && IsValidTarget(dep.packageId, transMod.PackageId, runningPidSet))
                    {
                        targets.Add(dep.packageId);
                    }
                }
            }

            return targets;
        }

        private static bool IsValidTarget(string pid, string selfPid, HashSet<string> runningPidSet)
        {
            if (string.IsNullOrEmpty(pid)) return false;
            if (IgnoredPackageIds.Contains(pid)) return false;
            if (string.Equals(pid, selfPid, StringComparison.OrdinalIgnoreCase)) return false;
            return runningPidSet.Contains(pid);
        }
    }
}
