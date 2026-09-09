using System;
using System.Collections.Generic;
using Verse;

namespace RUML
{
    public class RUMLSettings : ModSettings
    {
        // Translation folder toggles (for built-in RUML mods)
        public Dictionary<string, bool> modStates = new Dictionary<string, bool>();

        // Selected active author for each mod: modFolder -> authorName
        public Dictionary<string, string> selectedAuthors = new Dictionary<string, string>();

        // Selected packageIds for translation audit
        public HashSet<string> auditSelectedPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public bool auditInitialized = false;

        // GitHub Cloud Translations configuration
        public string cloudManifestUrl = "https://raw.githubusercontent.com/MrSlavaW/RUML/main/manifest.json";

        // Performance & Workflow: Apply translations on button click to avoid UI freezes
        public bool manualApplyMode = true;

        public string GetSelectedAuthor(string modFolder)
        {
            if (selectedAuthors == null)
            {
                selectedAuthors = new Dictionary<string, string>();
            }
            string author;
            if (selectedAuthors.TryGetValue(modFolder, out author))
            {
                return author;
            }
            return null;
        }

        public void SetSelectedAuthor(string modFolder, string author)
        {
            if (selectedAuthors == null)
            {
                selectedAuthors = new Dictionary<string, string>();
            }
            selectedAuthors[modFolder] = author;
        }

        public bool IsModEnabled(string modFolder)
        {
            if (modStates == null)
            {
                modStates = new Dictionary<string, bool>();
            }
            if (!modStates.ContainsKey(modFolder))
            {
                modStates[modFolder] = true;
            }
            return modStates[modFolder];
        }

        public void SetModEnabled(string modFolder, bool enabled)
        {
            if (modStates == null)
            {
                modStates = new Dictionary<string, bool>();
            }
            modStates[modFolder] = enabled;
        }

        public bool IsAuditModSelected(string packageId)
        {
            if (auditSelectedPackageIds == null)
            {
                auditSelectedPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            return auditSelectedPackageIds.Contains(packageId);
        }

        public void SetAuditModSelected(string packageId, bool selected)
        {
            if (auditSelectedPackageIds == null)
            {
                auditSelectedPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            if (selected)
            {
                auditSelectedPackageIds.Add(packageId);
            }
            else
            {
                auditSelectedPackageIds.Remove(packageId);
            }
        }

        public void SelectAllAuditMods(IEnumerable<string> packageIds)
        {
            if (auditSelectedPackageIds == null)
            {
                auditSelectedPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            foreach (string pid in packageIds)
            {
                auditSelectedPackageIds.Add(pid);
            }
        }

        public void DeselectAllAuditMods()
        {
            if (auditSelectedPackageIds == null)
            {
                auditSelectedPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                auditSelectedPackageIds.Clear();
            }
        }

        public void SelectAuditMods(IEnumerable<string> packageIdsToSelect)
        {
            if (auditSelectedPackageIds == null)
            {
                auditSelectedPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            auditSelectedPackageIds.Clear();
            foreach (string pid in packageIdsToSelect)
            {
                auditSelectedPackageIds.Add(pid);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref modStates, "modStates", LookMode.Value, LookMode.Value);
            if (modStates == null)
            {
                modStates = new Dictionary<string, bool>();
            }

            Scribe_Collections.Look(ref selectedAuthors, "selectedAuthors", LookMode.Value, LookMode.Value);
            if (selectedAuthors == null)
            {
                selectedAuthors = new Dictionary<string, string>();
            }

            Scribe_Collections.Look(ref auditSelectedPackageIds, "auditSelectedPackageIds", LookMode.Value);
            if (auditSelectedPackageIds == null)
            {
                auditSelectedPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            Scribe_Values.Look(ref auditInitialized, "auditInitialized", false);
            Scribe_Values.Look(ref cloudManifestUrl, "cloudManifestUrl", "https://raw.githubusercontent.com/MrSlavaW/RUML/main/manifest.json");
            Scribe_Values.Look(ref manualApplyMode, "manualApplyMode", true);
        }
    }
}
