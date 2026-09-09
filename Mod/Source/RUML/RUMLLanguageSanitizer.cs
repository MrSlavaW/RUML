// MC Version: 1.5 | Loader: RimWorld | Mappings: Official
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;
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
                    if (pkg == null || pkg.injections == null) continue;
                    bool isHediff = pkg.defType == typeof(HediffDef);
                    bool isThought = pkg.defType == typeof(ThoughtDef);
                    if (!isHediff && !isThought) continue;

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

                        string stageLabel = null;
                        if (isHediff)
                        {
                            HediffDef hediff = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
                            if (hediff != null && hediff.stages != null && stageIndex >= 0 && stageIndex < hediff.stages.Count)
                            {
                                HediffStage stage = hediff.stages[stageIndex];
                                if (stage != null) stageLabel = stage.label;
                            }
                        }
                        else if (isThought)
                        {
                            ThoughtDef thought = DefDatabase<ThoughtDef>.GetNamedSilentFail(defName);
                            if (thought != null && thought.stages != null && stageIndex >= 0 && stageIndex < thought.stages.Count)
                            {
                                ThoughtStage stage = thought.stages[stageIndex];
                                if (stage != null) stageLabel = stage.label;
                            }
                        }

                        if (string.IsNullOrEmpty(stageLabel)) continue;

                        string sanitizedLabel = SanitizeLabel(stageLabel);
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

        /// <summary>
        /// Directly injects Def field values (labels, descriptions, stages, custom fields) into in-memory Def instances.
        /// Resolves issues where RimWorld's internal SetDefFieldAtPath fails on custom stage keys or modded def types.
        /// </summary>
        public static int ApplyDirectInjections(LoadedLanguage lang)
        {
            int totalInjected = 0;

            // 1. Process all in-memory DefInjectionPackages in active language
            if (lang != null && lang.defInjections != null)
            {
                for (int pIdx = 0; pIdx < lang.defInjections.Count; pIdx++)
                {
                    try
                    {
                        DefInjectionPackage pkg = lang.defInjections[pIdx];
                        if (pkg == null || pkg.defType == null || pkg.injections == null) continue;

                        // Snapshot entries to avoid InvalidOperationException if collections are modified
                        List<KeyValuePair<string, DefInjectionPackage.DefInjection>> entries =
                            new List<KeyValuePair<string, DefInjectionPackage.DefInjection>>(pkg.injections);

                        for (int e = 0; e < entries.Count; e++)
                        {
                            string key = entries[e].Key;
                            DefInjectionPackage.DefInjection inj = entries[e].Value;
                            if (string.IsNullOrEmpty(key) || inj == null || inj.isPlaceholder || string.IsNullOrEmpty(inj.injection) || string.Equals(inj.injection, "TODO", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            if (ApplySingleInjection(pkg.defType, key, inj.injection, lang, false))
                            {
                                totalInjected++;
                            }
                        }
                    }
                    catch (Exception pex)
                    {
                        Log.Warning("[RUML] Error processing defInjection package: " + pex);
                    }
                }
            }

            // 2. Scan active external RUML translation XML files on disk as an authoritative fallback
            try
            {
                string extDir = RUMLFolderManager.GetExternalTranslationsDir();
                if (Directory.Exists(extDir))
                {
                    List<InstalledModItem> installed = RUMLFolderManager.GetAllInstalledMods();
                    RUMLSettings settings = LoadedModManager.GetMod<RUMLMod>() != null ? LoadedModManager.GetMod<RUMLMod>().GetSettings<RUMLSettings>() : null;

                    for (int i = 0; i < installed.Count; i++)
                    {
                        InstalledModItem item = installed[i];
                        if (settings != null && !settings.IsModEnabled(item.ModFolder)) continue;

                        string author = settings != null ? settings.GetSelectedAuthor(item.ModFolder) : null;
                        if (string.IsNullOrEmpty(author) && item.Authors != null && item.Authors.Count > 0)
                        {
                            author = item.Authors[0];
                        }
                        if (string.IsNullOrEmpty(author)) continue;

                        string defInjPath = Path.Combine(Path.Combine(Path.Combine(Path.Combine(extDir, item.ModFolder), author), "Languages"), "Russian");
                        defInjPath = Path.Combine(defInjPath, "DefInjected");
                        if (!Directory.Exists(defInjPath)) continue;

                        string[] typeDirs = Directory.GetDirectories(defInjPath);
                        for (int t = 0; t < typeDirs.Length; t++)
                        {
                            string typeDir = typeDirs[t];
                            string typeName = Path.GetFileName(typeDir);
                            Type defType = ResolveDefType(typeName);
                            if (defType == null) continue;

                            string[] xmlFiles = Directory.GetFiles(typeDir, "*.xml", SearchOption.AllDirectories);
                            for (int f = 0; f < xmlFiles.Length; f++)
                            {
                                try
                                {
                                    XmlDocument doc = new XmlDocument();
                                    doc.Load(xmlFiles[f]);
                                    if (doc.DocumentElement == null) continue;

                                    foreach (XmlNode node in doc.DocumentElement.ChildNodes)
                                    {
                                        if (node.NodeType != XmlNodeType.Element) continue;
                                        string key = node.Name;
                                        string val = node.InnerText;
                                        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(val) || val.Trim() == "TODO") continue;

                                        val = val.Trim();
                                        if (ApplySingleInjection(defType, key, val, lang, true))
                                        {
                                            totalInjected++;
                                        }
                                    }
                                }
                                catch (Exception fex)
                                {
                                    Log.Warning("[RUML] Error parsing translation XML " + xmlFiles[f] + ": " + fex.Message);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception dex)
            {
                Log.Warning("[RUML] Error scanning external translation files: " + dex);
            }

            if (totalInjected > 0)
            {
                Log.Message("[RUML] Directly applied " + totalInjected + " translation injections into live Defs.");
            }

            return totalInjected;
        }

        public static Type ResolveDefType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;

            try
            {
                Type t = typeof(Def).Assembly.GetType("Verse." + typeName);
                if (t != null && typeof(Def).IsAssignableFrom(t)) return t;

                t = typeof(Def).Assembly.GetType("RimWorld." + typeName);
                if (t != null && typeof(Def).IsAssignableFrom(t)) return t;

                t = GenTypes.GetTypeInAnyAssembly(typeName);
                if (t != null && typeof(Def).IsAssignableFrom(t)) return t;

                foreach (Type sub in typeof(Def).AllSubclassesNonAbstract())
                {
                    if (string.Equals(sub.Name, typeName, StringComparison.OrdinalIgnoreCase))
                    {
                        return sub;
                    }
                }
            }
            catch { }

            return null;
        }

        public static bool ApplySingleInjection(Type defType, string key, string val, LoadedLanguage lang, bool registerInPackage)
        {
            if (defType == null || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(val)) return false;

            int dotIdx = key.IndexOf('.');
            if (dotIdx <= 0) return false;

            string defName = key.Substring(0, dotIdx);
            string fieldPath = key.Substring(dotIdx + 1);

            Def targetDef = GenDefDatabase.GetDefSilentFail(defType, defName, false);
            if (targetDef == null) return false;

            bool applied = false;

            // 1. Handle stages (HediffDef, ThoughtDef)
            if (fieldPath.StartsWith("stages."))
            {
                string stageSub = fieldPath.Substring(7);
                int lastDot = stageSub.LastIndexOf('.');
                if (lastDot > 0)
                {
                    string stageIdent = stageSub.Substring(0, lastDot);
                    string stageField = stageSub.Substring(lastDot + 1);

                    // HediffDef stages
                    HediffDef hd = targetDef as HediffDef;
                    if (hd != null && hd.stages != null)
                    {
                        HediffStage matchedStage = null;
                        int sIdx;
                        int matchedIndex = -1;
                        if (int.TryParse(stageIdent, out sIdx) && sIdx >= 0 && sIdx < hd.stages.Count)
                        {
                            matchedStage = hd.stages[sIdx];
                            matchedIndex = sIdx;
                        }
                        else
                        {
                            for (int i = 0; i < hd.stages.Count; i++)
                            {
                                var st = hd.stages[i];
                                if (st == null) continue;
                                string raw = !string.IsNullOrEmpty(st.untranslatedLabel) ? st.untranslatedLabel : st.label;
                                string sLabel = SanitizeLabel(raw);
                                if (string.Equals(sLabel, stageIdent, StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(st.label, stageIdent, StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(st.untranslatedLabel, stageIdent, StringComparison.OrdinalIgnoreCase))
                                {
                                    matchedStage = st;
                                    matchedIndex = i;
                                    break;
                                }
                            }
                        }

                        if (matchedStage != null)
                        {
                            if (stageField == "label")
                            {
                                if (string.IsNullOrEmpty(matchedStage.untranslatedLabel))
                                {
                                    matchedStage.untranslatedLabel = matchedStage.label;
                                }
                                matchedStage.label = val;
                                applied = true;
                            }
                            else
                            {
                                FieldInfo fi = GetFieldRecursive(typeof(HediffStage), stageField);
                                if (fi != null && fi.FieldType == typeof(string))
                                {
                                    fi.SetValue(matchedStage, val);
                                    applied = true;
                                }
                            }

                            if (registerInPackage)
                            {
                                EnsurePackageRegistration(lang, defType, defName, matchedIndex, matchedStage.untranslatedLabel ?? matchedStage.label, stageField, val);
                            }
                        }
                    }

                    // ThoughtDef stages
                    ThoughtDef td = targetDef as ThoughtDef;
                    if (td != null && td.stages != null)
                    {
                        ThoughtStage matchedStage = null;
                        int sIdx;
                        int matchedIndex = -1;
                        if (int.TryParse(stageIdent, out sIdx) && sIdx >= 0 && sIdx < td.stages.Count)
                        {
                            matchedStage = td.stages[sIdx];
                            matchedIndex = sIdx;
                        }
                        else
                        {
                            for (int i = 0; i < td.stages.Count; i++)
                            {
                                var st = td.stages[i];
                                if (st == null) continue;
                                string raw = !string.IsNullOrEmpty(st.untranslatedLabel) ? st.untranslatedLabel : st.label;
                                string sLabel = SanitizeLabel(raw);
                                if (string.Equals(sLabel, stageIdent, StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(st.label, stageIdent, StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(st.untranslatedLabel, stageIdent, StringComparison.OrdinalIgnoreCase))
                                {
                                    matchedStage = st;
                                    matchedIndex = i;
                                    break;
                                }
                            }
                        }

                        if (matchedStage != null)
                        {
                            if (stageField == "label")
                            {
                                if (string.IsNullOrEmpty(matchedStage.untranslatedLabel))
                                {
                                    matchedStage.untranslatedLabel = matchedStage.label;
                                }
                                matchedStage.label = val;
                                FieldInfo tCap = GetFieldRecursive(typeof(ThoughtStage), "cachedLabelCap");
                                if (tCap != null)
                                {
                                    tCap.SetValue(matchedStage, null);
                                }
                                applied = true;
                            }
                            else if (stageField == "description")
                            {
                                matchedStage.description = val;
                                applied = true;
                            }
                            else
                            {
                                FieldInfo fi = GetFieldRecursive(typeof(ThoughtStage), stageField);
                                if (fi != null && fi.FieldType == typeof(string))
                                {
                                    fi.SetValue(matchedStage, val);
                                    applied = true;
                                }
                            }

                            if (registerInPackage)
                            {
                                EnsurePackageRegistration(lang, defType, defName, matchedIndex, matchedStage.untranslatedLabel ?? matchedStage.label, stageField, val);
                            }
                        }
                    }
                }
            }
            else
            {
                // 2. Direct field on Def (label, description, endMessage, letterText, etc.)
                FieldInfo fi = GetFieldRecursive(targetDef.GetType(), fieldPath);
                if (fi != null && fi.FieldType == typeof(string))
                {
                    fi.SetValue(targetDef, val);
                    if (fieldPath == "label")
                    {
                        FieldInfo capFi = GetFieldRecursive(typeof(Def), "cachedLabelCap");
                        if (capFi != null)
                        {
                            capFi.SetValue(targetDef, default(TaggedString));
                        }
                    }
                    applied = true;
                }
                else if (fieldPath.Contains("."))
                {
                    applied = SetNestedValue(targetDef, fieldPath.Split('.'), 0, val);
                }

                if (registerInPackage)
                {
                    EnsurePackageRegistrationDirect(lang, defType, key, val);
                }
            }

            return applied;
        }

        private static bool SetNestedValue(object obj, string[] parts, int index, string val)
        {
            if (obj == null || index >= parts.Length) return false;
            string part = parts[index];
            if (index == parts.Length - 1)
            {
                FieldInfo fi = GetFieldRecursive(obj.GetType(), part);
                if (fi != null && fi.FieldType == typeof(string))
                {
                    fi.SetValue(obj, val);
                    return true;
                }
                return false;
            }

            int listIdx;
            if (int.TryParse(part, out listIdx))
            {
                System.Collections.IList list = obj as System.Collections.IList;
                if (list != null && listIdx >= 0 && listIdx < list.Count)
                {
                    return SetNestedValue(list[listIdx], parts, index + 1, val);
                }
                return false;
            }

            FieldInfo nextFi = GetFieldRecursive(obj.GetType(), part);
            if (nextFi != null)
            {
                object nextObj = nextFi.GetValue(obj);
                return SetNestedValue(nextObj, parts, index + 1, val);
            }
            return false;
        }

        private static void EnsurePackageRegistration(LoadedLanguage lang, Type defType, string defName, int stageIndex, string stageLabel, string fieldName, string val)
        {
            if (lang == null || lang.defInjections == null || defType == null) return;

            DefInjectionPackage pkg = null;
            for (int i = 0; i < lang.defInjections.Count; i++)
            {
                if (lang.defInjections[i] != null && lang.defInjections[i].defType == defType)
                {
                    pkg = lang.defInjections[i];
                    break;
                }
            }
            if (pkg == null)
            {
                pkg = new DefInjectionPackage(defType);
                lang.defInjections.Add(pkg);
            }

            if (pkg.injections == null) return;

            // Numeric key
            if (stageIndex >= 0)
            {
                string numKey = defName + ".stages." + stageIndex + "." + fieldName;
                DefInjectionPackage.DefInjection inj;
                if (!pkg.injections.TryGetValue(numKey, out inj) || inj == null || string.IsNullOrEmpty(inj.injection))
                {
                    inj = new DefInjectionPackage.DefInjection();
                    inj.path = numKey;
                    inj.normalizedPath = numKey;
                    inj.suggestedPath = numKey;
                    inj.injection = val;
                    inj.isPlaceholder = false;
                    inj.injected = true;
                    pkg.injections[numKey] = inj;
                }
            }

            // Named key
            if (!string.IsNullOrEmpty(stageLabel))
            {
                string sanitized = SanitizeLabel(stageLabel);
                if (!string.IsNullOrEmpty(sanitized))
                {
                    string nameKey = defName + ".stages." + sanitized + "." + fieldName;
                    DefInjectionPackage.DefInjection inj;
                    if (!pkg.injections.TryGetValue(nameKey, out inj) || inj == null || string.IsNullOrEmpty(inj.injection))
                    {
                        inj = new DefInjectionPackage.DefInjection();
                        inj.path = nameKey;
                        inj.normalizedPath = nameKey;
                        inj.suggestedPath = nameKey;
                        inj.injection = val;
                        inj.isPlaceholder = false;
                        inj.injected = true;
                        pkg.injections[nameKey] = inj;
                    }
                }
            }
        }

        private static void EnsurePackageRegistrationDirect(LoadedLanguage lang, Type defType, string key, string val)
        {
            if (lang == null || lang.defInjections == null || defType == null || string.IsNullOrEmpty(key)) return;

            DefInjectionPackage pkg = null;
            for (int i = 0; i < lang.defInjections.Count; i++)
            {
                if (lang.defInjections[i] != null && lang.defInjections[i].defType == defType)
                {
                    pkg = lang.defInjections[i];
                    break;
                }
            }
            if (pkg == null)
            {
                pkg = new DefInjectionPackage(defType);
                lang.defInjections.Add(pkg);
            }

            if (pkg.injections == null) return;

            DefInjectionPackage.DefInjection inj;
            if (!pkg.injections.TryGetValue(key, out inj) || inj == null || string.IsNullOrEmpty(inj.injection))
            {
                inj = new DefInjectionPackage.DefInjection();
                inj.path = key;
                inj.normalizedPath = key;
                inj.suggestedPath = key;
                inj.injection = val;
                inj.isPlaceholder = false;
                inj.injected = true;
                pkg.injections[key] = inj;
            }
        }

        public static FieldInfo GetFieldRecursive(Type t, string name)
        {
            Type curr = t;
            while (curr != null && curr != typeof(object))
            {
                FieldInfo fi = curr.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (fi != null) return fi;
                curr = curr.BaseType;
            }
            return null;
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
                    RUMLLanguageSanitizer.SanitizeAndAlias(LanguageDatabase.activeLanguage);
                    RUMLLanguageSanitizer.ApplyDirectInjections(LanguageDatabase.activeLanguage);
                    LanguageDatabase.activeLanguage.InjectIntoData_AfterImpliedDefs();
                    GenLabel.ClearCache();
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[RUML] Startup sanitizer encountered an issue: " + ex);
            }
        }
    }
}
