// MC Version: 1.5 | Loader: RimWorld | Mappings: Official
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using RimWorld;
using Verse;

namespace RUML
{
    public static class RUMLLanguageSanitizer
    {
        private static readonly Regex StageIndexRegex = new Regex(@"^([^.]+)\.stages\.([0-9]+)\.(.+)$", RegexOptions.Compiled);

        /// <summary>
        /// Scans active language DefInjections, locates numeric stage keys (e.g., .stages.0.label),
        /// resolves canonical stage labels from DefDatabase<HediffDef>, and injects canonical named keys.
        /// </summary>
        public static int SanitizeAndAlias(LoadedLanguage lang)
        {
            if (lang == null || lang.defInjections == null) return 0;

            int totalAliased = 0;

            try
            {
                for (int pIdx = 0; pIdx < lang.defInjections.Count; pIdx++)
                {
                    DefInjectionPackage pkg = lang.defInjections[pIdx];
                    if (pkg == null || pkg.defType != typeof(HediffDef) || pkg.injections == null) continue;

                    List<KeyValuePair<string, DefInjectionPackage.DefInjection>> numericEntries = 
                        new List<KeyValuePair<string, DefInjectionPackage.DefInjection>>();

                    foreach (var kv in pkg.injections)
                    {
                        if (kv.Key != null && kv.Key.Contains(".stages.") && StageIndexRegex.IsMatch(kv.Key))
                        {
                            numericEntries.Add(kv);
                        }
                    }

                    for (int i = 0; i < numericEntries.Count; i++)
                    {
                        var entry = numericEntries[i];
                        Match match = StageIndexRegex.Match(entry.Key);
                        if (!match.Success) continue;

                        string defName = match.Groups[1].Value;
                        int stageIndex;
                        if (!int.TryParse(match.Groups[2].Value, out stageIndex)) continue;
                        string fieldName = match.Groups[3].Value;

                        HediffDef hediff = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
                        if (hediff == null || hediff.stages == null || stageIndex < 0 || stageIndex >= hediff.stages.Count) continue;

                        HediffStage stage = hediff.stages[stageIndex];
                        if (stage == null || string.IsNullOrEmpty(stage.label)) continue;

                        string sanitizedLabel = SanitizeLabel(stage.label);
                        if (string.IsNullOrEmpty(sanitizedLabel)) continue;

                        string canonicalKey = defName + ".stages." + sanitizedLabel + "." + fieldName;

                        DefInjectionPackage.DefInjection existing;
                        bool needsInjection = false;

                        if (!pkg.injections.TryGetValue(canonicalKey, out existing))
                        {
                            needsInjection = true;
                        }
                        else if (existing == null || existing.isPlaceholder || string.IsNullOrEmpty(existing.injection) || existing.injection == "TODO")
                        {
                            needsInjection = true;
                        }

                        if (needsInjection)
                        {
                            DefInjectionPackage.DefInjection srcInj = entry.Value;
                            if (srcInj != null && !srcInj.isPlaceholder && !string.IsNullOrEmpty(srcInj.injection))
                            {
                                DefInjectionPackage.DefInjection newInj = new DefInjectionPackage.DefInjection();
                                newInj.path = canonicalKey;
                                newInj.normalizedPath = canonicalKey;
                                newInj.suggestedPath = canonicalKey;
                                newInj.injection = srcInj.injection;
                                newInj.fileSource = srcInj.fileSource;
                                newInj.isPlaceholder = false;
                                newInj.injected = false;

                                pkg.injections[canonicalKey] = newInj;
                                totalAliased++;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[RUML] Error during runtime stage aliasing: " + ex);
            }

            if (totalAliased > 0)
            {
                Log.Message("[RUML] Automatically mapped and injected " + totalAliased + " canonical Hediff stage keys from numeric indices.");
            }

            return totalAliased;
        }

        public static string SanitizeLabel(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            string s = Regex.Replace(raw.Trim(), @"[^a-zA-Z0-9_]", "_");
            return s.Trim('_');
        }
    }

    [StaticConstructorOnStartup]
    public static class RUMLStartup
    {
        static RUMLStartup()
        {
            try
            {
                if (LanguageDatabase.activeLanguage != null)
                {
                    int aliased = RUMLLanguageSanitizer.SanitizeAndAlias(LanguageDatabase.activeLanguage);
                    if (aliased > 0)
                    {
                        LanguageDatabase.activeLanguage.InjectIntoData_AfterImpliedDefs();
                        GenLabel.ClearCache();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[RUML] Startup sanitizer encountered an issue: " + ex);
            }
        }
    }
}
