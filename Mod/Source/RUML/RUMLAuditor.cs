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
        public int TotalDefs;
        public int TranslatedDefs;
        public int MissingDefs;
        public float Percent;
        public bool IsRUMLMod;
        public List<string> MissingList = new List<string>();
    }

    public static class RUMLAuditor
    {
        public static List<ModAuditReport> RunAudit(HashSet<string> targetPackageIds = null, Dictionary<string, string> rumlKnownMods = null)
        {
            List<ModAuditReport> reports = new List<ModAuditReport>();

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

                int total = 0;
                int translated = 0;

                foreach (Type defType in GenDefDatabase.AllDefTypesWithDatabases())
                {
                    try
                    {
                        string dtName = defType.Name;
                        if (dtName.Contains("Fleck") || dtName.Contains("Effecter") || dtName.Contains("SubEffecter") ||
                            dtName.Equals("ModDef") || dtName.Contains("TMMSettingDef") || dtName.Contains("SettingsMenuDef") ||
                            dtName.Contains("ModOptionCategoryDef") ||
                            dtName.Equals("ToolCapacityDef") || dtName.Equals("WorkGiverDef") || dtName.Contains("ThinkTree") ||
                            dtName.Contains("Duty") || dtName.Contains("JobDef") || dtName.Contains("KeyBindingDef") ||
                            dtName.Contains("ShaderTypeDef") || dtName.Contains("SoundDef"))
                        {
                            continue;
                        }

                        foreach (Def def in GenDefDatabase.GetAllDefsInDatabaseForDef(defType))
                        {
                            if (def.modContentPack != mod) continue;

                            // Skip internal defs that have no label in English (non-translatable)
                            if (string.IsNullOrEmpty(def.label)) continue;

                            string cleanLabel = def.label.Replace("\u3164", "").Trim();
                            if (string.IsNullOrEmpty(cleanLabel)) continue;

                            // Skip purely visual/internal VFX particles (motes, projectiles, flecks)
                            if (cleanLabel.Equals("Mote", StringComparison.OrdinalIgnoreCase) ||
                                cleanLabel.Equals("bullet", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            ThingDef td = def as ThingDef;
                            if (td != null)
                            {
                                if (td.mote != null || td.projectile != null ||
                                    td.category == ThingCategory.Mote || td.category == ThingCategory.Projectile ||
                                    td.category == ThingCategory.Ethereal)
                                {
                                    continue;
                                }
                            }

                            total++;
                            bool isRu = ContainsCyrillic(def.label);

                            if (isRu)
                            {
                                translated++;
                            }
                            else
                            {
                                rep.MissingList.Add(defType.Name + "." + def.defName + ".label: \"" + def.label + "\"");
                            }
                        }
                    }
                    catch { }
                }

                rep.TotalDefs = total;
                rep.TranslatedDefs = translated;
                rep.MissingDefs = total - translated;
                rep.Percent = total > 0 ? (float)translated / total * 100f : 100f;
                reports.Add(rep);
            }

            // Sort reports: mods with missing items first (lowest % first), then 100% mods by name
            reports.Sort(delegate(ModAuditReport a, ModAuditReport b)
            {
                if (a.MissingDefs > 0 && b.MissingDefs == 0) return -1;
                if (a.MissingDefs == 0 && b.MissingDefs > 0) return 1;
                if (a.MissingDefs > 0 && b.MissingDefs > 0)
                {
                    return a.Percent.CompareTo(b.Percent);
                }
                return string.Compare(a.ModName, b.ModName, StringComparison.OrdinalIgnoreCase);
            });

            return reports;
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
                sb.AppendLine("  Покрытие: " + r.Percent.ToString("F1") + "% (" + r.TranslatedDefs + " / " + r.TotalDefs + ")");
                sb.AppendLine("  Не переведено: " + r.MissingDefs);
                if (r.MissingList.Count > 0)
                {
                    sb.AppendLine("  Примеры отсутствующих строк (" + r.MissingList.Count + "):");
                    int maxExamples = Math.Min(r.MissingList.Count, 50);
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
