using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace RUML
{
    public static class RUMLUI
    {
        public static readonly Color ColorAccentGreen = new Color(0.2f, 0.95f, 0.45f, 1f);
        public static readonly Color ColorButtonGreen = new Color(0.2f, 0.9f, 0.5f, 1f);
        public static readonly Color ColorAccentGold = new Color(1f, 0.85f, 0.2f, 1f);
        public static readonly Color ColorDestructiveRed = new Color(1f, 0.4f, 0.4f, 1f);
        public static readonly Color ColorActiveTab = new Color(0.3f, 0.8f, 1f, 1f);
        public static readonly Color ColorCardBg = new Color(0.12f, 0.12f, 0.12f, 0.45f);
        public static readonly Color ColorCardBgAlt = new Color(0.15f, 0.15f, 0.15f, 0.45f);

        public static void DrawSearchBar(Rect rect, ref string query)
        {
            bool hasText = !string.IsNullOrEmpty(query);
            float clearBtnW = 28f;
            Rect textRect = hasText ? new Rect(rect.x, rect.y, rect.width - clearBtnW - 4f, rect.height) : rect;
            query = Widgets.TextField(textRect, query);

            if (hasText)
            {
                Rect clearRect = new Rect(rect.xMax - clearBtnW, rect.y, clearBtnW, rect.height);
                Color prevCol = GUI.color;
                GUI.color = new Color(0.85f, 0.85f, 0.85f, 1f);
                if (Widgets.ButtonText(clearRect, "✕"))
                {
                    query = "";
                }
                GUI.color = prevCol;
                string tip = "RUML_ClearSearchTooltip".CanTranslate() ? (string)"RUML_ClearSearchTooltip".Translate() : "Очистить строку поиска";
                TooltipHandler.TipRegion(clearRect, tip);
            }
        }

        public static void DrawStatusRibbon(Rect rect)
        {
            string sText;
            if (RUMLCloudManager.IsBusy)
            {
                sText = "<color=yellow>" + RUMLCloudManager.StatusMessage + " (" + RUMLCloudManager.DownloadPercent.ToString("F0") + "%)</color>";
            }
            else if (RUMLFolderManager.hasPendingChanges)
            {
                sText = "<color=#FFD700>• " + ("RUML_ApplyPending".CanTranslate() ? (string)"RUML_ApplyPending".Translate() : "Есть неприменённые изменения! Нажмите «Применить настройки» внизу окна.") + "</color>";
            }
            else
            {
                sText = "<color=#80D0FF>" + RUMLCloudManager.StatusMessage + "</color>";
            }
            Widgets.Label(rect, sText);
        }

        public static void DrawBottomApplyBar(Rect inRect, ModContentPack content, RUMLSettings settings)
        {
            Rect bottomRect = new Rect(inRect.x, inRect.yMax - 36f, inRect.width, 34f);
            Color prevApplyCol = GUI.color;
            if (RUMLFolderManager.hasPendingChanges)
            {
                GUI.color = ColorAccentGreen;
            }
            string btnText = RUMLFolderManager.hasPendingChanges
                ? ("RUML_ApplyPending".CanTranslate() ? (string)"RUML_ApplyPending".Translate() : "[!] Применить настройки и перезагрузить переводы в памяти игры (есть изменения!)")
                : ("RUML_Apply".CanTranslate() ? (string)"RUML_Apply".Translate() : "Применить настройки и перезагрузить переводы в памяти игры");
            if (Widgets.ButtonText(bottomRect, btnText))
            {
                RUMLFolderManager.ApplyFilter(content, settings);
                RUMLFolderManager.ReloadLanguage();
            }
            GUI.color = prevApplyCol;
        }
    }

    public class RUMLMod : Mod
    {
        public static RUMLSettings Settings;
        public static RUMLMod Instance;

        private Vector2 modsScrollPos = Vector2.zero;
        private Vector2 auditModsScrollPos = Vector2.zero;
        private Vector2 auditResultsScrollPos = Vector2.zero;
        private Vector2 cloudScrollPos = Vector2.zero;
        private Vector2 settingsScrollPos = Vector2.zero;

        private string modsSearchFilter = "";
        private string auditSearchFilter = "";
        private string auditResultSearchFilter = "";
        private string cloudSearchFilter = "";
        private string cloudAuthorFilter = "";
        private string cloudLanguageFilter = "auto";
        private int cloudStatusFilter = 0; // 0: All, 1: Installed, 2: Not Installed, 3: Updates
        private Dictionary<string, int> selectedAuthorIndexByMod = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private List<ModAuditReport> lastAuditReports = null;
        private int currentMainTab = 0; // 0: Built-in Mods, 1: Auditor, 2: GitHub Cloud
        private int auditSubTab = 0;    // 0: Select Active Mods, 1: Audit Results
        private int auditCategoryFilter = 0; // 0: All, 1: None, 2: Built-in, 3: External Mod, 4: RUML, 5: Translation Packs
        private int auditResultMode = 0; // 0: All, 1: With Missing, 2: 100% Translated
        private string expandedAuditMod = "";
        private int expandedAuditCategoryFilter = 0; // 0: All, 1: Defs, 2: Keyed / Settings
        private bool autoCheckUpdatesTriggered = false;

        // Caching structures for UI optimization
        private class AuditSelectionModRow
        {
            public ModContentPack mod;
            public string pid;
            public string displayName;
            public bool isRuml;
            public bool isVanilla;
            public bool isTransMod;
            public bool hasExternal;
            public bool hasBuiltIn;
            public string tag;
            public string builtInBadge;
            public string transBadge;
            public string fullTip;
        }

        private static List<AuditSelectionModRow> cachedAuditSelectionRows = null;
        private static int cachedAuditCountAll = 0;
        private static int cachedAuditCountNoTrans = 0;
        private static int cachedAuditCountBuiltIn = 0;
        private static int cachedAuditCountExternal = 0;
        private static int cachedAuditCountRuml = 0;
        private static int cachedAuditCountTransMods = 0;

        private static string lastAuditSearchFilter = null;
        private static int lastAuditCategoryFilter = -1;
        private static List<AuditSelectionModRow> cachedAuditFilteredRows = null;

        private static string lastCloudAuthorFilter = null;
        private static string lastCloudSearchFilter = null;
        private static string lastCloudLangFilter = null;
        private static int lastCloudStatusFilter = -1;
        private static int lastCloudItemsCount = -1;
        private static List<CloudModGroup> cachedCloudGroups = null;

        public static void InvalidateAuditCache()
        {
            cachedAuditSelectionRows = null;
            cachedAuditFilteredRows = null;
            lastAuditSearchFilter = null;
            lastAuditCategoryFilter = -1;
            cachedCloudGroups = null;
            lastCloudStatusFilter = -1;
        }

        public static readonly Dictionary<string, string> FolderToPackageId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "AlphaAnimals", "sarg.alphaanimals" },
            { "AlphaArmoury", "sarg.alphaarmoury" },
            { "AlphaBiomes", "sarg.alphabiomes" },
            { "AlphaGenes", "sarg.alphagenes" },
            { "AlphaGenesIntegrated", "kupa.alphagenesintegrated" },
            { "AlphaMechs", "sarg.alphamechs" },
            { "AlphaMemes", "sarg.alphamemes" },
            { "AlphaRandom", "sarg.alpharandom" },
            { "ArchotechExpanded", "teok25.archotechexpanded.prosthetics" },
            { "CatsBootsAndGloves", "catlover366.bootsandgloves" },
            { "EPOE_ModularCompat", "asunib.epoeiicompat" },
            { "EPOE_Royalty", "vat.epoeforkedroyalty" },
            { "GeneExtractorTiers", "redmattis.geneextractor" },
            { "InfoCardPlus", "kaamalauppias.infocardplus" },
            { "KabouterXenotype", "soovereign.chipchipchip" },
            { "MoreGroupedBuildings", "wsp.groupedbuildings" },
            { "PlasmaShieldImplant", "virathas.plasmashieldimplant" },
            { "Psycasts2", "astryl.psycasts" },
            { "RebuildDoorsCorners", "rebuild.cotr.doorsandcorners" },
            { "ReelsStorage", "reel.expanded.storage" },
            { "RegrowthAspen", "regrowth.botr.aspenforest" },
            { "RespliceCore", "resplice.xotr.core" },
            { "SbzFridge", "sbz.neatstoragefridge" },
            { "SbzGravshipStorage", "sbz.gravshipstorage" },
            { "SimpleSidearms", "petetimessix.simplesidearms" },
            { "TooManyMods", "wara.toomanymods" },
            { "VOE_Factory", "scorpio.voefactory" },
            { "VanometricGenerator", "hlx.vanometricgenerator" }
        };

        public RUMLMod(ModContentPack content) : base(content)
        {
            Instance = this;
            Settings = GetSettings<RUMLSettings>();
            RUMLFolderManager.Initialize(content, Settings);

            // Initialize default audit selection on first launch
            if (!Settings.auditInitialized)
            {
                InitDefaultAuditSelection();
                Settings.auditInitialized = true;
            }

            // Populate cloud showcase entries if empty
            if (RUMLCloudManager.Items.Count == 0)
            {
                RUMLCloudManager.LoadDefaultShowcaseItems();
            }
            RUMLCloudManager.RefreshLocalState(content);
        }

        private void InitDefaultAuditSelection()
        {
            List<string> pids = new List<string>();
            if (LoadedModManager.RunningMods != null)
            {
                foreach (ModContentPack m in LoadedModManager.RunningMods)
                {
                    if (m != null && !IsVanillaOrDlc(m.PackageIdPlayerFacing))
                    {
                        pids.Add(m.PackageIdPlayerFacing);
                    }
                }
            }
            Settings.SelectAuditMods(pids);
        }

        public static bool IsVanillaOrDlc(string pid)
        {
            if (string.IsNullOrEmpty(pid)) return false;
            string l = pid.ToLower();
            return l.Equals("ludeon.rimworld") ||
                   l.Equals("ludeon.rimworld.royalty") ||
                   l.Equals("ludeon.rimworld.ideology") ||
                   l.Equals("ludeon.rimworld.biotech") ||
                   l.Equals("ludeon.rimworld.anomaly") ||
                   l.Equals("ludeon.rimworld.odyssey");
        }

        public static string GetModDisplayName(ModContentPack mod)
        {
            if (mod == null) return "";
            string pid = mod.PackageIdPlayerFacing ?? "";
            string l = pid.ToLower();
            if (l.Equals("ludeon.rimworld")) return "Core";
            if (l.Equals("ludeon.rimworld.royalty")) return "Royalty";
            if (l.Equals("ludeon.rimworld.ideology")) return "Ideology";
            if (l.Equals("ludeon.rimworld.biotech")) return "Biotech";
            if (l.Equals("ludeon.rimworld.anomaly")) return "Anomaly";
            if (l.Equals("ludeon.rimworld.odyssey")) return "Odyssey";
            if (!string.IsNullOrEmpty(mod.Name)) return mod.Name;
            return pid;
        }

        public override string SettingsCategory()
        {
            return "[RUML] Universal Localization";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            if (!autoCheckUpdatesTriggered)
            {
                autoCheckUpdatesTriggered = true;
                if (RUMLCloudManager.Items.Count == 0 && !RUMLCloudManager.IsBusy)
                {
                    RUMLCloudManager.FetchManifestAsync(Settings.cloudManifestUrl, Content);
                }
            }

            // Main Navigation Header (4 Tabs)
            int installedCount = RUMLFolderManager.GetAllInstalledMods().Count;
            int cloudCount = RUMLCloudManager.Items != null ? RUMLCloudManager.Items.Count : 0;

            string tab0Title = "RUML_TabInstalled".CanTranslate() ? (string)"RUML_TabInstalled".Translate(installedCount) : ("1. Установленные (" + installedCount + ")");
            string tab1Title = "RUML_TabAuditor".CanTranslate() ? (string)"RUML_TabAuditor".Translate() : "2. Аудитор модов";
            string tab2Title = cloudCount > 0
                ? ("RUML_TabCatalogCount".CanTranslate() ? (string)"RUML_TabCatalogCount".Translate(cloudCount) : ("3. Каталог переводов (" + cloudCount + ")"))
                : ("RUML_TabCatalog".CanTranslate() ? (string)"RUML_TabCatalog".Translate() : "3. Каталог переводов");
            string tab3Title = "RUML_TabSettings".CanTranslate() ? (string)"RUML_TabSettings".Translate() : "4. Настройки";

            Rect tabRect = new Rect(inRect.x, inRect.y, inRect.width, 32f);
            float tabWidth = (inRect.width - 30f) / 4f;

            if (DrawTabButton(new Rect(tabRect.x, tabRect.y, tabWidth, 32f), tab0Title, currentMainTab == 0))
            {
                currentMainTab = 0;
            }
            if (DrawTabButton(new Rect(tabRect.x + tabWidth + 10f, tabRect.y, tabWidth, 32f), tab1Title, currentMainTab == 1))
            {
                currentMainTab = 1;
            }
            if (DrawTabButton(new Rect(tabRect.x + (tabWidth + 10f) * 2, tabRect.y, tabWidth, 32f), tab2Title, currentMainTab == 2))
            {
                currentMainTab = 2;
            }
            if (DrawTabButton(new Rect(tabRect.x + (tabWidth + 10f) * 3, tabRect.y, tabWidth, 32f), tab3Title, currentMainTab == 3))
            {
                currentMainTab = 3;
            }

            Rect contentRect = new Rect(inRect.x, inRect.y + 40f, inRect.width, inRect.height - 40f);

            if (currentMainTab == 0)
            {
                DrawModsTab(contentRect);
            }
            else if (currentMainTab == 1)
            {
                DrawAuditTab(contentRect);
            }
            else if (currentMainTab == 2)
            {
                DrawCloudTab(contentRect);
            }
            else
            {
                DrawSettingsTab(contentRect);
            }
        }

        private bool DrawTabButton(Rect r, string label, bool active)
        {
            Color oldColor = GUI.color;
            if (active)
            {
                GUI.color = RUMLUI.ColorActiveTab;
            }
            bool clicked = Widgets.ButtonText(r, label);
            GUI.color = oldColor;
            return clicked;
        }

        // =========================================================================
        // TAB 0: BUILT-IN & INSTALLED TRANSLATIONS
        // =========================================================================
        private void DrawModsTab(Rect inRect)
        {
            List<InstalledModItem> allMods = RUMLFolderManager.GetAllInstalledMods();
            if (allMods.Count == 0)
            {
                Rect infoRect = new Rect(inRect.x + 10f, inRect.y + 20f, inRect.width - 20f, 65f);
                string noModsText = "RUML_NoInstalledTranslations".CanTranslate() ? (string)"RUML_NoInstalledTranslations".Translate() : "Локализации ещё не установлены в RUML.\nRUML работает как автономное ядро. Вы можете скачать нужные переводы во вкладке «Каталог переводов».";
                Widgets.Label(infoRect, noModsText);

                Rect goBtn = new Rect(inRect.x + 10f, infoRect.yMax + 10f, 320f, 34f);
                string goBtnText = "RUML_GoToCatalog".CanTranslate() ? (string)"RUML_GoToCatalog".Translate() : "Перейти в каталог переводов";
                if (Widgets.ButtonText(goBtn, goBtnText))
                {
                    currentMainTab = 2;
                }

                Rect refBtnEmpty = new Rect(inRect.x + 10f, goBtn.yMax + 14f, 200f, 32f);
                string refTextEmpty = "RUML_RefreshList".CanTranslate() ? (string)"RUML_RefreshList".Translate() : "Обновить список";
                if (Widgets.ButtonText(refBtnEmpty, refTextEmpty))
                {
                    RUMLFolderManager.InvalidateCache();
                    allMods = RUMLFolderManager.GetAllInstalledMods();
                    RUMLFolderManager.ApplyFilter(Content, Settings);
                    RUMLCloudManager.RefreshLocalState(Content);
                    string msg = "RUML_ListRefreshedCount".CanTranslate() ? (string)"RUML_ListRefreshedCount".Translate(allMods.Count) : ("Список установленных переводов обновлен (" + allMods.Count + ").");
                    RUMLCloudManager.StatusMessage = msg;
                    Messages.Message(msg, MessageTypeDefOf.TaskCompletion, false);
                }
                TooltipHandler.TipRegion(refBtnEmpty, "RUML_RefreshListTooltip".CanTranslate() ? (string)"RUML_RefreshListTooltip".Translate() : "Пересканировать директорию RUML_Translations и обновить список установленных модификаций без перезапуска игры.");

                Rect emptyDirRect = new Rect(refBtnEmpty.xMax + 10f, goBtn.yMax + 14f, 240f, 32f);
                string openDirText = "RUML_OpenTranslationsFolder".CanTranslate() ? (string)"RUML_OpenTranslationsFolder".Translate() : "Открыть папку переводов";
                if (Widgets.ButtonText(emptyDirRect, openDirText))
                {
                    RUMLCloudManager.OpenTranslationsFolderInExplorer();
                }
                TooltipHandler.TipRegion(emptyDirRect, RUMLFolderManager.GetExternalTranslationsDir());
                return;
            }

            // Top Controls: Row 1 - Search, Refresh List & Batch Toggle
            Rect topRect = new Rect(inRect.x, inRect.y, inRect.width, 30f);
            float refW = 145f;
            float btnW = 115f;

            Rect searchRect = new Rect(topRect.x, topRect.y, inRect.width - (refW + btnW * 2 + 30f), 30f);
            RUMLUI.DrawSearchBar(searchRect, ref modsSearchFilter);

            string refreshListText = "RUML_RefreshList".CanTranslate() ? (string)"RUML_RefreshList".Translate() : "Обновить список";
            Rect refBtnRect = new Rect(searchRect.xMax + 10f, topRect.y, refW, 30f);
            if (Widgets.ButtonText(refBtnRect, refreshListText))
            {
                RUMLFolderManager.InvalidateCache();
                allMods = RUMLFolderManager.GetAllInstalledMods();
                RUMLFolderManager.ApplyFilter(Content, Settings);
                RUMLCloudManager.RefreshLocalState(Content);
                string msg = "RUML_ListRefreshedCount".CanTranslate() ? (string)"RUML_ListRefreshedCount".Translate(allMods.Count) : ("Список установленных переводов обновлен (" + allMods.Count + ").");
                RUMLCloudManager.StatusMessage = msg;
                Messages.Message(msg, MessageTypeDefOf.TaskCompletion, false);
            }
            TooltipHandler.TipRegion(refBtnRect, "RUML_RefreshListTooltip".CanTranslate() ? (string)"RUML_RefreshListTooltip".Translate() : "Пересканировать директорию RUML_Translations и обновить список установленных модификаций без перезапуска игры.");

            string enableAllText = "RUML_EnableAll".CanTranslate() ? (string)"RUML_EnableAll".Translate() : "Включить все";
            Rect enableBtnRect = new Rect(refBtnRect.xMax + 10f, topRect.y, btnW, 30f);
            if (Widgets.ButtonText(enableBtnRect, enableAllText))
            {
                foreach (InstalledModItem m in allMods) Settings.SetModEnabled(m.ModFolder, true);
                RUMLFolderManager.ApplyFilter(Content, Settings);
                if (Settings.manualApplyMode)
                {
                    RUMLFolderManager.hasPendingChanges = true;
                }
                else
                {
                    RUMLFolderManager.ReloadLanguage();
                }
            }

            string disableAllText = "RUML_DisableAll".CanTranslate() ? (string)"RUML_DisableAll".Translate() : "Отключить все";
            Rect disableBtnRect = new Rect(enableBtnRect.xMax + 10f, topRect.y, btnW, 30f);
            if (Widgets.ButtonText(disableBtnRect, disableAllText))
            {
                foreach (InstalledModItem m in allMods) Settings.SetModEnabled(m.ModFolder, false);
                RUMLFolderManager.ApplyFilter(Content, Settings);
                if (Settings.manualApplyMode)
                {
                    RUMLFolderManager.hasPendingChanges = true;
                }
                else
                {
                    RUMLFolderManager.ReloadLanguage();
                }
            }

            // Row 2: Check Updates, Update Outdated Only, Reinstall All, Open AppData
            Rect actRow = new Rect(inRect.x, inRect.y + 36f, inRect.width, 30f);
            float actW1 = 170f;
            float actW2 = 195f;
            float actW3 = 165f;
            float actW4 = 180f;

            string chkUpdText = "RUML_CheckUpdates".CanTranslate() ? (string)"RUML_CheckUpdates".Translate() : "Проверить обновления";
            if (Widgets.ButtonText(new Rect(actRow.x, actRow.y, actW1, 30f), chkUpdText))
            {
                RUMLCloudManager.CheckUpdatesAsync(Settings.cloudManifestUrl, Content);
            }

            Color prevBtnCol = GUI.color;
            GUI.color = RUMLUI.ColorButtonGreen;
            string updOutdatedText = "RUML_UpdateOutdated".CanTranslate() ? (string)"RUML_UpdateOutdated".Translate() : "Обновить актуальные";
            if (Widgets.ButtonText(new Rect(actRow.x + actW1 + 10f, actRow.y, actW2, 30f), updOutdatedText))
            {
                RUMLCloudManager.UpdateOutdatedOnlyAsync(Content, Settings);
            }
            GUI.color = prevBtnCol;
            TooltipHandler.TipRegion(new Rect(actRow.x + actW1 + 10f, actRow.y, actW2, 30f), "Скачать обновления ТОЛЬКО для тех активных переводов, для которых вышли новые версии (без повторной загрузки уже актуальных).");

            string updAllText = "RUML_UpdateAllActive".CanTranslate() ? (string)"RUML_UpdateAllActive".Translate() : "Переустановить все";
            if (Widgets.ButtonText(new Rect(actRow.x + actW1 + actW2 + 20f, actRow.y, actW3, 30f), updAllText))
            {
                RUMLCloudManager.UpdateAllActiveAsync(Content, Settings);
            }
            TooltipHandler.TipRegion(new Rect(actRow.x + actW1 + actW2 + 20f, actRow.y, actW3, 30f), "Принудительно заново перекачать и установить все активные переводы.");

            Rect openDirRect = new Rect(actRow.x + actW1 + actW2 + actW3 + 30f, actRow.y, actW4, 30f);
            string openDirText2 = "RUML_OpenTranslationsFolder".CanTranslate() ? (string)"RUML_OpenTranslationsFolder".Translate() : "Открыть папку переводов";
            if (Widgets.ButtonText(openDirRect, openDirText2))
            {
                RUMLCloudManager.OpenTranslationsFolderInExplorer();
            }
            TooltipHandler.TipRegion(openDirRect, RUMLFolderManager.GetExternalTranslationsDir());

            // Row 3: Status / Progress message
            Rect statusR = new Rect(inRect.x, inRect.y + 70f, inRect.width, 22f);
            RUMLUI.DrawStatusRibbon(statusR);

            // Table Header Bar (Column Labels)
            Rect tableHeaderRect = new Rect(inRect.x, inRect.y + 96f, inRect.width, 24f);
            Widgets.DrawBoxSolid(tableHeaderRect, new Color(0.08f, 0.08f, 0.08f, 0.75f));

            float listContentW = inRect.width - 24f;
            float actionW = 75f;
            float statusW = 105f;
            float authorW = 160f;
            float actionX = listContentW - actionW - 4f;
            float statusX = actionX - statusW - 10f;
            float authorX = statusX - authorW - 10f;
            float nameX = 8f;
            float nameW = authorX - nameX - 10f;

            Rect h1 = new Rect(tableHeaderRect.x + nameX, tableHeaderRect.y + 2f, nameW, 20f);
            string colMod = "RUML_TableColMod".CanTranslate() ? (string)"RUML_TableColMod".Translate() : "Мод / Локализация";
            Widgets.Label(h1, "<color=#C0C0C0><b>" + colMod + "</b></color>");

            Rect h2 = new Rect(tableHeaderRect.x + authorX, tableHeaderRect.y + 2f, authorW, 20f);
            string colAuthor = "RUML_TableColAuthor".CanTranslate() ? (string)"RUML_TableColAuthor".Translate() : "Автор перевода";
            Widgets.Label(h2, "<color=#C0C0C0><b>" + colAuthor + "</b></color>");

            Rect h3 = new Rect(tableHeaderRect.x + statusX, tableHeaderRect.y + 2f, statusW, 20f);
            Text.Anchor = TextAnchor.MiddleCenter;
            string colStatus = "RUML_TableColStatus".CanTranslate() ? (string)"RUML_TableColStatus".Translate() : "Состояние";
            Widgets.Label(h3, "<color=#C0C0C0><b>" + colStatus + "</b></color>");
            Text.Anchor = TextAnchor.UpperLeft;

            Rect h4 = new Rect(tableHeaderRect.x + actionX, tableHeaderRect.y + 2f, actionW, 20f);
            Text.Anchor = TextAnchor.MiddleCenter;
            string colAction = "RUML_TableColAction".CanTranslate() ? (string)"RUML_TableColAction".Translate() : "Действие";
            Widgets.Label(h4, "<color=#C0C0C0><b>" + colAction + "</b></color>");
            Text.Anchor = TextAnchor.UpperLeft;

            // Scrollable List (starts below table header)
            Rect listRect = new Rect(inRect.x, inRect.y + 122f, inRect.width, inRect.height - 164f);
            List<InstalledModItem> filtered = new List<InstalledModItem>();
            foreach (InstalledModItem m in allMods)
            {
                if (string.IsNullOrEmpty(modsSearchFilter) || m.ModFolder.IndexOf(modsSearchFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    filtered.Add(m);
                }
            }

            Rect viewRect = new Rect(0f, 0f, listRect.width - 24f, filtered.Count * 44f);
            Widgets.BeginScrollView(listRect, ref modsScrollPos, viewRect);

            float curY = 0f;
            foreach (InstalledModItem item in filtered)
            {
                if (curY + 44f < modsScrollPos.y - 10f || curY > modsScrollPos.y + listRect.height + 10f)
                {
                    curY += 44f;
                    continue;
                }

                string modFolder = item.ModFolder;
                bool isEnabled = Settings.IsModEnabled(modFolder);

                string activeAuthor = Settings.GetSelectedAuthor(modFolder);
                if (string.IsNullOrEmpty(activeAuthor) || !item.Authors.Contains(activeAuthor))
                {
                    activeAuthor = item.Authors.Count > 0 ? item.Authors[0] : "";
                    Settings.SetSelectedAuthor(modFolder, activeAuthor);
                }

                // Match cloud item for update status
                CloudTranslationItem cloudItem = null;
                for (int ci = 0; ci < RUMLCloudManager.Items.Count; ci++)
                {
                    CloudTranslationItem cit = RUMLCloudManager.Items[ci];
                    if (string.Equals(cit.ModFolder, modFolder, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(cit.AuthorFolder, activeAuthor, StringComparison.OrdinalIgnoreCase))
                    {
                        cloudItem = cit;
                        break;
                    }
                }
                bool hasUpdate = cloudItem != null && cloudItem.HasUpdate;

                Rect cardRect = new Rect(0f, curY, viewRect.width, 38f);
                Widgets.DrawBoxSolid(cardRect, RUMLUI.ColorCardBg);
                Widgets.DrawHighlightIfMouseover(cardRect);

                // Col 1: Fixed Checkbox (left) + ModName + [v1.X.X]
                Rect chkRect = new Rect(nameX, curY + 7f, 24f, 24f);
                bool newCheck = isEnabled;
                Widgets.Checkbox(chkRect.x, chkRect.y, ref newCheck);
                if (newCheck != isEnabled)
                {
                    Settings.SetModEnabled(modFolder, newCheck);
                    RUMLFolderManager.ApplyFilter(Content, Settings);
                    if (Settings.manualApplyMode)
                    {
                        RUMLFolderManager.hasPendingChanges = true;
                    }
                    else
                    {
                        RUMLFolderManager.ReloadLanguage();
                    }
                }

                string ver = (cloudItem != null && !string.IsNullOrEmpty(cloudItem.LocalVersion))
                    ? cloudItem.LocalVersion
                    : RUMLFolderManager.GetInstalledVersion(modFolder, activeAuthor);

                string modLabel = "<b>" + modFolder + "</b> <color=#80D0FF>[v" + (string.IsNullOrEmpty(ver) ? "1.0.0" : ver) + "]</color>";
                Rect nameRect = new Rect(nameX + 32f, curY + 6f, nameW - 32f, 26f);
                Widgets.Label(nameRect, modLabel);
                TooltipHandler.TipRegion(new Rect(nameX, curY + 4f, nameW, 30f), (isEnabled ? "Включено: нажмите на флажок, чтобы отключить этот перевод." : "Отключено: нажмите на флажок, чтобы включить этот перевод."));

                // Col 2: Fixed Author Selector Button or Single Author Badge
                Rect authorRect = new Rect(authorX, curY + 5f, authorW, 28f);
                if (item.Authors.Count > 1)
                {
                    string authBtnLabel = "<color=#40E0D0>" + activeAuthor + "</color> (" + item.Authors.Count + " авт.) ▼";
                    if (Widgets.ButtonText(authorRect, authBtnLabel))
                    {
                        List<FloatMenuOption> authOpts = new List<FloatMenuOption>();
                        for (int a = 0; a < item.Authors.Count; a++)
                        {
                            string aName = item.Authors[a];
                            string optLabel = aName + (string.Equals(aName, activeAuthor, StringComparison.OrdinalIgnoreCase) ? " [Активен]" : "");
                            authOpts.Add(new FloatMenuOption(optLabel, delegate()
                            {
                                Settings.SetSelectedAuthor(modFolder, aName);
                                RUMLFolderManager.ApplyFilter(Content, Settings);
                                if (Settings.manualApplyMode)
                                {
                                    RUMLFolderManager.hasPendingChanges = true;
                                }
                                else
                                {
                                    RUMLFolderManager.ReloadLanguage();
                                }
                            }));
                        }
                        Find.WindowStack.Add(new FloatMenu(authOpts));
                    }
                    TooltipHandler.TipRegion(authorRect, "Нажмите для переключения активного автора перевода на лету");
                }
                else if (item.Authors.Count == 1)
                {
                    string authorLabel = "<color=#40E0D0>" + item.Authors[0] + "</color>";
                    Widgets.Label(new Rect(authorRect.x, authorRect.y + 4f, authorRect.width, 24f), authorLabel);
                }

                // Col 3: Fixed Status / Update button
                Rect statusColRect = new Rect(statusX, curY + 5f, statusW, 28f);
                if (hasUpdate)
                {
                    Color prevUCol = GUI.color;
                    GUI.color = RUMLUI.ColorAccentGold;
                    string updLabel = "RUML_StatusUpdateAvailable".CanTranslate() ? (string)"RUML_StatusUpdateAvailable".Translate() : "Обновить!";
                    if (Widgets.ButtonText(statusColRect, updLabel))
                    {
                        RUMLCloudManager.DownloadAndInstallAsync(cloudItem, Content, Settings);
                    }
                    GUI.color = prevUCol;
                    TooltipHandler.TipRegion(statusColRect, "v" + (cloudItem.LocalVersion ?? "1.0") + " -> v" + cloudItem.Version);
                }
                else
                {
                    Text.Anchor = TextAnchor.MiddleCenter;
                    string upToDateText = "RUML_StatusUpToDate".CanTranslate() ? (string)"RUML_StatusUpToDate".Translate() : "✓ Актуально";
                    Widgets.Label(statusColRect, "<color=#50E050>" + upToDateText + "</color>");
                    Text.Anchor = TextAnchor.UpperLeft;
                }

                // Col 4: Fixed Delete button
                Rect delBtnRect = new Rect(actionX, curY + 5f, actionW, 28f);
                Color prevCol = GUI.color;
                GUI.color = RUMLUI.ColorDestructiveRed;
                string delLabel = "RUML_Uninstall".CanTranslate() ? (string)"RUML_Uninstall".Translate() : "Удалить";
                if (Widgets.ButtonText(delBtnRect, delLabel))
                {
                    RUMLFolderManager.DeleteAuthorTranslation(modFolder, activeAuthor, Content, Settings);
                }
                GUI.color = prevCol;
                TooltipHandler.TipRegion(delBtnRect, activeAuthor);

                curY += 44f;
            }

            Widgets.EndScrollView();

            // Bottom Controls: Unified Apply Bar
            RUMLUI.DrawBottomApplyBar(inRect, Content, Settings);
        }

        // =========================================================================
        // TAB 1: MODS AUDITOR & MISSING STRINGS DISCOVERY
        // =========================================================================
        private void DrawAuditTab(Rect inRect)
        {
            // Sub-Tab Navigation Header
            Rect subHeaderRect = new Rect(inRect.x, inRect.y, inRect.width, 30f);
            float subW = (inRect.width - 10f) / 2f;

            int selCount = Settings.auditSelectedPackageIds != null ? Settings.auditSelectedPackageIds.Count : 0;
            int repCount = lastAuditReports != null ? lastAuditReports.Count : 0;

            string sub0Label = "RUML_AuditSubTabSelection".CanTranslate() 
                ? (string)"RUML_AuditSubTabSelection".Translate(selCount) 
                : ("1. Выбор активных модов (" + selCount + " выбрано)");
            string sub1Label = "RUML_AuditSubTabResults".CanTranslate() 
                ? (string)"RUML_AuditSubTabResults".Translate(repCount) 
                : ("2. Результаты анализа (" + repCount + " отчётов)");

            if (DrawTabButton(new Rect(subHeaderRect.x, subHeaderRect.y, subW, 28f), sub0Label, auditSubTab == 0))
            {
                auditSubTab = 0;
            }
            if (DrawTabButton(new Rect(subHeaderRect.x + subW + 10f, subHeaderRect.y, subW, 28f), sub1Label, auditSubTab == 1))
            {
                auditSubTab = 1;
            }

            Rect bodyRect = new Rect(inRect.x, inRect.y + 36f, inRect.width, inRect.height - 36f);

            if (auditSubTab == 0)
            {
                DrawAuditSelectionSubTab(bodyRect);
            }
            else
            {
                DrawAuditResultsSubTab(bodyRect);
            }
        }

        private void DrawAuditSelectionSubTab(Rect inRect)
        {
            var running = LoadedModManager.RunningMods.ToList();
            if (running == null || running.Count == 0) return;

            // Row 1: Search and Clear
            Rect searchRect = new Rect(inRect.x, inRect.y, inRect.width, 26f);
            RUMLUI.DrawSearchBar(searchRect, ref auditSearchFilter);

            // Rebuild rows cache if null or running mods count changed
            if (cachedAuditSelectionRows == null || cachedAuditSelectionRows.Count != running.Count)
            {
                cachedAuditSelectionRows = new List<AuditSelectionModRow>(running.Count);
                cachedAuditCountAll = running.Count;
                cachedAuditCountNoTrans = 0;
                cachedAuditCountBuiltIn = 0;
                cachedAuditCountExternal = 0;
                cachedAuditCountRuml = 0;
                cachedAuditCountTransMods = 0;

                HashSet<string> rumlPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string p in FolderToPackageId.Values)
                {
                    if (!string.IsNullOrEmpty(p)) rumlPackageIds.Add(p);
                }
                if (RUMLCloudManager.Items != null)
                {
                    for (int ci = 0; ci < RUMLCloudManager.Items.Count; ci++)
                    {
                        var it = RUMLCloudManager.Items[ci];
                        if (it.IsInstalled && !string.IsNullOrEmpty(it.PackageId))
                        {
                            rumlPackageIds.Add(it.PackageId);
                        }
                    }
                }

                for (int i = 0; i < running.Count; i++)
                {
                    var m = running[i];
                    string pid = m.PackageIdPlayerFacing;
                    bool isRuml = rumlPackageIds.Contains(pid);
                    List<TargetModInfo> dummyTargets;
                    bool isTransMod = RUMLTranslationDetector.IsTranslationMod(m.PackageId, out dummyTargets) || RUMLTranslationDetector.IsTranslationMod(pid, out dummyTargets);
                    List<ActiveTranslationModInfo> dummyAct;
                    bool hasExternal = RUMLTranslationDetector.HasActiveTranslation(m.PackageId, out dummyAct) || RUMLTranslationDetector.HasActiveTranslation(pid, out dummyAct);
                    bool hasBuiltIn = RUMLTranslationDetector.HasBuiltInTranslation(m.PackageId) || RUMLTranslationDetector.HasBuiltInTranslation(pid);
                    bool isVanilla = IsVanillaOrDlc(pid);

                    if (isTransMod) cachedAuditCountTransMods++;
                    if (hasBuiltIn) cachedAuditCountBuiltIn++;
                    if (hasExternal) cachedAuditCountExternal++;
                    if (isRuml) cachedAuditCountRuml++;
                    if (!isVanilla && !isTransMod && !hasBuiltIn && !hasExternal && !isRuml) cachedAuditCountNoTrans++;

                    string tag = "";
                    if (isRuml)
                    {
                        tag = " <color=#FFD700>[RUML]</color>";
                    }
                    else if (isVanilla)
                    {
                        tag = string.Equals(pid, "ludeon.rimworld", StringComparison.OrdinalIgnoreCase)
                            ? " <color=#E0B020>[Core]</color>"
                            : " <color=#E0B020>[DLC]</color>";
                    }

                    string builtInBadge = RUMLTranslationDetector.GetBuiltInModBadge(m.PackageId);
                    string builtInTip = RUMLTranslationDetector.GetBuiltInModTooltip(m.PackageId);

                    string transBadge = "";
                    string transTip = null;
                    if (isTransMod)
                    {
                        transBadge = RUMLTranslationDetector.GetTranslationModSelfBadge(m.PackageId, m.Name);
                        transTip = RUMLTranslationDetector.GetTranslationModSelfTooltip(m.PackageId);
                    }
                    else if (hasExternal)
                    {
                        transBadge = RUMLTranslationDetector.GetTargetModBadge(m.PackageId);
                        transTip = RUMLTranslationDetector.GetTargetModTooltip(m.PackageId);
                    }

                    string dispName = GetModDisplayName(m);
                    string fullTip = dispName + "\n(" + pid + ")";
                    if (!string.IsNullOrEmpty(builtInTip)) fullTip += "\n\n" + builtInTip;
                    if (!string.IsNullOrEmpty(transTip)) fullTip += "\n\n" + transTip;
                    if (isRuml)
                    {
                        string rumlTip = "RUML_TipRumlActive".CanTranslate() ? (string)"RUML_TipRumlActive".Translate() : "Для этого мода активен перевод из системы RUML.";
                        fullTip += "\n\n" + rumlTip;
                    }

                    AuditSelectionModRow row = new AuditSelectionModRow
                    {
                        mod = m,
                        pid = pid,
                        displayName = dispName,
                        isRuml = isRuml,
                        isVanilla = isVanilla,
                        isTransMod = isTransMod,
                        hasExternal = hasExternal,
                        hasBuiltIn = hasBuiltIn,
                        tag = tag,
                        builtInBadge = builtInBadge,
                        transBadge = transBadge,
                        fullTip = fullTip
                    };
                    cachedAuditSelectionRows.Add(row);
                }
                cachedAuditFilteredRows = null;
            }

            int countAll = cachedAuditCountAll;
            int countNoTrans = cachedAuditCountNoTrans;
            int countBuiltIn = cachedAuditCountBuiltIn;
            int countExternal = cachedAuditCountExternal;
            int countRuml = cachedAuditCountRuml;
            int countTransMods = cachedAuditCountTransMods;

            // Row 2: Category Filter Bar
            string[] catLabels = new string[]
            {
                "RUML_FilterAll".CanTranslate() ? (string)"RUML_FilterAll".Translate(countAll) : ("Все (" + countAll + ")"),
                "RUML_FilterUntranslated".CanTranslate() ? (string)"RUML_FilterUntranslated".Translate(countNoTrans) : ("Без перевода (" + countNoTrans + ")"),
                "RUML_FilterBuiltIn".CanTranslate() ? (string)"RUML_FilterBuiltIn".Translate(countBuiltIn) : ("Встроенный (" + countBuiltIn + ")"),
                "RUML_FilterExternal".CanTranslate() ? (string)"RUML_FilterExternal".Translate(countExternal) : ("Внешний мод (" + countExternal + ")"),
                "RUML_FilterRUML".CanTranslate() ? (string)"RUML_FilterRUML".Translate(countRuml) : ("RUML (" + countRuml + ")"),
                "RUML_FilterPacks".CanTranslate() ? (string)"RUML_FilterPacks".Translate(countTransMods) : ("Пакеты (" + countTransMods + ")")
            };
            float catGap = 5f;
            float catBtnW = (inRect.width - catGap * 5f) / 6f;
            for (int c = 0; c < 6; c++)
            {
                Rect catRect = new Rect(inRect.x + c * (catBtnW + catGap), inRect.y + 30f, catBtnW, 26f);
                Color prevCol = GUI.color;
                if (auditCategoryFilter == c)
                {
                    GUI.color = RUMLUI.ColorActiveTab;
                }
                if (Widgets.ButtonText(catRect, catLabels[c]))
                {
                    auditCategoryFilter = c;
                }
                GUI.color = prevCol;
            }

            // Fast filtered list
            if (cachedAuditFilteredRows == null || lastAuditSearchFilter != auditSearchFilter || lastAuditCategoryFilter != auditCategoryFilter)
            {
                lastAuditSearchFilter = auditSearchFilter;
                lastAuditCategoryFilter = auditCategoryFilter;
                cachedAuditFilteredRows = new List<AuditSelectionModRow>();

                for (int i = 0; i < cachedAuditSelectionRows.Count; i++)
                {
                    AuditSelectionModRow r = cachedAuditSelectionRows[i];
                    if (auditCategoryFilter == 1 && (r.isVanilla || r.isTransMod || r.hasBuiltIn || r.hasExternal || r.isRuml)) continue;
                    if (auditCategoryFilter == 2 && !r.hasBuiltIn) continue;
                    if (auditCategoryFilter == 3 && !r.hasExternal) continue;
                    if (auditCategoryFilter == 4 && !r.isRuml) continue;
                    if (auditCategoryFilter == 5 && !r.isTransMod) continue;

                    if (!string.IsNullOrEmpty(auditSearchFilter) &&
                        r.displayName.IndexOf(auditSearchFilter, StringComparison.OrdinalIgnoreCase) < 0 &&
                        r.pid.IndexOf(auditSearchFilter, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }
                    cachedAuditFilteredRows.Add(r);
                }
            }

            // Row 3: Action Buttons (Batch selection)
            Rect btnRowRect = new Rect(inRect.x, inRect.y + 60f, inRect.width, 26f);
            float bGap = 6f;
            float bW = (inRect.width - bGap * 4f) / 5f;

            string selInFilterText = "RUML_SelectInFilter".CanTranslate() ? (string)"RUML_SelectInFilter".Translate() : "Выбрать в фильтре";
            if (Widgets.ButtonText(new Rect(btnRowRect.x, btnRowRect.y, bW, 26f), selInFilterText))
            {
                for (int i = 0; i < cachedAuditFilteredRows.Count; i++)
                {
                    Settings.SetAuditModSelected(cachedAuditFilteredRows[i].pid, true);
                }
            }
            string deselInFilterText = "RUML_DeselectInFilter".CanTranslate() ? (string)"RUML_DeselectInFilter".Translate() : "Снять в фильтре";
            if (Widgets.ButtonText(new Rect(btnRowRect.x + (bW + bGap), btnRowRect.y, bW, 26f), deselInFilterText))
            {
                for (int i = 0; i < cachedAuditFilteredRows.Count; i++)
                {
                    Settings.SetAuditModSelected(cachedAuditFilteredRows[i].pid, false);
                }
            }
            string selAllText = "RUML_SelectAll".CanTranslate() ? (string)"RUML_SelectAll".Translate() : "Выбрать ВСЕ";
            if (Widgets.ButtonText(new Rect(btnRowRect.x + (bW + bGap) * 2, btnRowRect.y, bW, 26f), selAllText))
            {
                List<string> all = new List<string>(cachedAuditSelectionRows.Count);
                for (int i = 0; i < cachedAuditSelectionRows.Count; i++) all.Add(cachedAuditSelectionRows[i].pid);
                Settings.SelectAuditMods(all);
            }
            string deselAllText = "RUML_DeselectAll".CanTranslate() ? (string)"RUML_DeselectAll".Translate() : "Снять ВСЕ";
            if (Widgets.ButtonText(new Rect(btnRowRect.x + (bW + bGap) * 3, btnRowRect.y, bW, 26f), deselAllText))
            {
                Settings.DeselectAllAuditMods();
            }
            string exVanillaText = "RUML_ExcludeVanilla".CanTranslate() ? (string)"RUML_ExcludeVanilla".Translate() : "Без Core/DLC";
            if (Widgets.ButtonText(new Rect(btnRowRect.x + (bW + bGap) * 4, btnRowRect.y, bW, 26f), exVanillaText))
            {
                List<string> nonVanilla = new List<string>();
                for (int i = 0; i < cachedAuditSelectionRows.Count; i++)
                {
                    if (!cachedAuditSelectionRows[i].isVanilla)
                    {
                        nonVanilla.Add(cachedAuditSelectionRows[i].pid);
                    }
                }
                Settings.SelectAuditMods(nonVanilla);
            }

            // Row 4: Status count
            Rect statusRect = new Rect(inRect.x, inRect.y + 90f, inRect.width, 20f);
            int selectedCount = Settings.auditSelectedPackageIds != null ? Settings.auditSelectedPackageIds.Count : 0;
            string statusSelectedText = "RUML_AuditStatusSelected".CanTranslate()
                ? (string)"RUML_AuditStatusSelected".Translate("<color=cyan>" + selectedCount + "</color>", "<color=white>" + running.Count + "</color>", "<color=#80D0FF>" + cachedAuditFilteredRows.Count + "</color>")
                : ("Выбрано для аудита: <color=cyan>" + selectedCount + "</color> из <color=white>" + running.Count + "</color> | Показано в фильтре: <color=#80D0FF>" + cachedAuditFilteredRows.Count + "</color>");
            Widgets.Label(statusRect, statusSelectedText);

            // Row 5: Scrollable Active Mods Checkbox List
            Rect listRect = new Rect(inRect.x, inRect.y + 114f, inRect.width, inRect.height - 164f);
            float rowHeight = 38f;
            float rowStep = 42f;
            Rect viewRect = new Rect(0f, 0f, listRect.width - 24f, cachedAuditFilteredRows.Count * rowStep);
            Widgets.BeginScrollView(listRect, ref auditModsScrollPos, viewRect);

            float curY = 0f;
            for (int i = 0; i < cachedAuditFilteredRows.Count; i++)
            {
                if (curY + rowStep < auditModsScrollPos.y - 10f || curY > auditModsScrollPos.y + listRect.height + 10f)
                {
                    curY += rowStep;
                    continue;
                }

                AuditSelectionModRow r = cachedAuditFilteredRows[i];
                string pid = r.pid;
                bool isSelected = Settings.IsAuditModSelected(pid);
                Rect rowRect = new Rect(0f, curY, viewRect.width, rowHeight);
                Widgets.DrawBoxSolid(rowRect, RUMLUI.ColorCardBg);
                Widgets.DrawHighlightIfMouseover(rowRect);

                float chkSize = 24f;
                float textX = rowRect.x + 8f;
                float textW = rowRect.width - chkSize - 20f;

                // Line 1: Mod Name + Badges
                Rect titleRect = new Rect(textX, curY + 2f, textW, 20f);
                string titleText = r.displayName + r.tag + r.builtInBadge + r.transBadge;
                bool prevWrap = Text.WordWrap;
                Text.WordWrap = false;
                Text.Font = GameFont.Small;
                Widgets.Label(titleRect, titleText);

                // Line 2: PackageId in tiny font
                Rect pidRect = new Rect(textX, curY + 20f, textW, 16f);
                Text.Font = GameFont.Tiny;
                Widgets.Label(pidRect, "<color=#888888>(" + pid + ")</color>");
                Text.Font = GameFont.Small;
                Text.WordWrap = prevWrap;

                // Checkbox on the right
                Rect checkR = new Rect(rowRect.width - chkSize - 8f, curY + 7f, chkSize, chkSize);
                bool newCheck = isSelected;
                Widgets.Checkbox(checkR.x, checkR.y, ref newCheck);

                // Clicking anywhere on the row card also toggles the checkbox
                Rect clickRowRect = new Rect(rowRect.x, rowRect.y, rowRect.width - chkSize - 12f, rowRect.height);
                if (Widgets.ButtonInvisible(clickRowRect))
                {
                    newCheck = !isSelected;
                }

                if (newCheck != isSelected)
                {
                    Settings.SetAuditModSelected(pid, newCheck);
                }

                if (!string.IsNullOrEmpty(r.fullTip))
                {
                    TooltipHandler.TipRegion(rowRect, r.fullTip);
                }

                curY += rowStep;
            }

            Widgets.EndScrollView();

            // Bottom Action Button: Run Audit on Selected
            Rect bottomRect = new Rect(inRect.x, inRect.yMax - 44f, inRect.width, 40f);
            Color oldCol = GUI.color;
            GUI.color = RUMLUI.ColorAccentGreen;
            string btnText = "RUML_RunAudit".CanTranslate()
                ? (string)"RUML_RunAudit".Translate(selectedCount)
                : ("Запустить аудит выбранных модов (" + selectedCount + ")");
            if (Widgets.ButtonText(bottomRect, btnText))
            {
                lastAuditReports = RUMLAuditor.RunAudit(Settings.auditSelectedPackageIds, FolderToPackageId);
                auditSubTab = 1;
            }
            GUI.color = oldCol;
        }

        private void DrawAuditResultsSubTab(Rect inRect)
        {
            if (lastAuditReports == null || lastAuditReports.Count == 0)
            {
                Rect msgRect = new Rect(inRect.x, inRect.y + 40f, inRect.width, 60f);
                string noRepText = "RUML_AuditNoReportsYet".CanTranslate()
                    ? (string)"RUML_AuditNoReportsYet".Translate()
                    : "Аудит ещё не запускался или среди выбранных модов ничего не найдено.\nПерейдите во вкладку 'Выбор активных модов', выберите нужные моды и нажмите зеленую кнопку.";
                Widgets.Label(msgRect, noRepText);

                Rect goBackRect = new Rect(inRect.x, inRect.y + 110f, 240f, 36f);
                string goBackText = "RUML_BackToMods".CanTranslate() ? (string)"RUML_BackToMods".Translate() : "Перейти к выбору модов";
                if (Widgets.ButtonText(goBackRect, goBackText))
                {
                    auditSubTab = 0;
                }
                return;
            }

            // Summary Header Row
            int totalMods = lastAuditReports.Count;
            int withMissing = 0;
            int fullTranslated = 0;
            int sumTotalItems = 0;
            int sumMissingItems = 0;
            int sumTotalDefs = 0;
            int sumTotalKeyed = 0;

            foreach (var r in lastAuditReports)
            {
                sumTotalItems += r.TotalItems;
                sumMissingItems += r.MissingItems;
                sumTotalDefs += r.TotalDefs;
                sumTotalKeyed += r.TotalKeyed;
                if (r.MissingItems > 0) withMissing++;
                else fullTranslated++;
            }
            float avgPercent = sumTotalItems > 0 ? ((float)(sumTotalItems - sumMissingItems) / sumTotalItems * 100f) : 100f;

            Rect sumRect = new Rect(inRect.x, inRect.y, inRect.width, 24f);
            string sumText = "RUML_AuditSummary".CanTranslate()
                ? (string)"RUML_AuditSummary".Translate(
                    "<b>" + totalMods + "</b>",
                    "<b>" + sumTotalItems + "</b>",
                    sumTotalDefs,
                    sumTotalKeyed,
                    "<color=#FF6B6B><b>" + sumMissingItems + "</b></color>",
                    "<b>" + avgPercent.ToString("F1") + "</b>")
                : ("Проверено: <b>" + totalMods + "</b> модов | Элементов: <b>" + sumTotalItems + "</b> (Дефов: " + sumTotalDefs + ", Keyed: " + sumTotalKeyed + ") | Не переведено: <color=#FF6B6B><b>" + sumMissingItems + "</b></color> | Покрытие: <b>" + avgPercent.ToString("F1") + "%</b>");
            Widgets.Label(sumRect, sumText);

            // Action Buttons Row
            Rect actRow = new Rect(inRect.x, inRect.y + 28f, inRect.width, 30f);
            float actW = (inRect.width - 30f) / 4f;

            string expRepText = "RUML_ExportReport".CanTranslate() ? (string)"RUML_ExportReport".Translate() : "Экспорт отчёта";
            if (Widgets.ButtonText(new Rect(actRow.x, actRow.y, actW, 30f), expRepText))
            {
                string f = RUMLAuditor.ExportReportToFile(lastAuditReports);
                Messages.Message("RUML: Отчёт успешно сохранён на Рабочий стол: " + f, MessageTypeDefOf.PositiveEvent, false);
            }
            string expTplText = "RUML_ExportTemplates".CanTranslate() ? (string)"RUML_ExportTemplates".Translate() : "Экспорт XML-шаблонов";
            if (Widgets.ButtonText(new Rect(actRow.x + actW + 10f, actRow.y, actW, 30f), expTplText))
            {
                string dir = RUMLAuditor.ExportMissingTemplates(lastAuditReports);
                if (!string.IsNullOrEmpty(dir))
                {
                    Messages.Message("RUML: Шаблоны XML сохранены: " + dir, MessageTypeDefOf.PositiveEvent, false);
                }
            }
            string repAuditText = "RUML_RepeatAudit".CanTranslate() ? (string)"RUML_RepeatAudit".Translate() : "Повторить аудит";
            if (Widgets.ButtonText(new Rect(actRow.x + (actW + 10f) * 2, actRow.y, actW, 30f), repAuditText))
            {
                lastAuditReports = RUMLAuditor.RunAudit(Settings.auditSelectedPackageIds, FolderToPackageId);
            }
            string backModsText = "RUML_BackToMods".CanTranslate() ? (string)"RUML_BackToMods".Translate() : "Выбор модов";
            if (Widgets.ButtonText(new Rect(actRow.x + (actW + 10f) * 3, actRow.y, actW, 30f), backModsText))
            {
                auditSubTab = 0;
            }

            // Filter & Search Row
            Rect filterRow = new Rect(inRect.x, inRect.y + 64f, inRect.width, 28f);
            float fBtnW = 120f;
            Rect srchR = new Rect(filterRow.x, filterRow.y, inRect.width - (fBtnW * 3 + 20f), 28f);
            RUMLUI.DrawSearchBar(srchR, ref auditResultSearchFilter);

            string mode0Text = "RUML_AuditResultAll".CanTranslate() ? (string)"RUML_AuditResultAll".Translate(totalMods) : ("Все (" + totalMods + ")");
            string mode1Text = "RUML_AuditResultMissing".CanTranslate() ? (string)"RUML_AuditResultMissing".Translate(withMissing) : ("С пропусками (" + withMissing + ")");
            string mode2Text = "RUML_AuditResultComplete".CanTranslate() ? (string)"RUML_AuditResultComplete".Translate(fullTranslated) : ("100% (" + fullTranslated + ")");

            if (DrawTabButton(new Rect(srchR.xMax + 10f, filterRow.y, fBtnW, 28f), mode0Text, auditResultMode == 0))
            {
                auditResultMode = 0;
            }
            if (DrawTabButton(new Rect(srchR.xMax + fBtnW + 15f, filterRow.y, fBtnW, 28f), mode1Text, auditResultMode == 1))
            {
                auditResultMode = 1;
            }
            if (DrawTabButton(new Rect(srchR.xMax + (fBtnW * 2) + 20f, filterRow.y, fBtnW, 28f), mode2Text, auditResultMode == 2))
            {
                auditResultMode = 2;
            }

            // Filtered results list
            List<ModAuditReport> filtered = new List<ModAuditReport>();
            foreach (var rep in lastAuditReports)
            {
                if (auditResultMode == 1 && rep.MissingItems == 0) continue;
                if (auditResultMode == 2 && rep.MissingItems > 0) continue;

                if (!string.IsNullOrEmpty(auditResultSearchFilter))
                {
                    if (rep.ModName.IndexOf(auditResultSearchFilter, StringComparison.OrdinalIgnoreCase) < 0 &&
                        rep.PackageId.IndexOf(auditResultSearchFilter, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }
                }
                filtered.Add(rep);
            }

            // Scrollable Results List
            Rect listRect = new Rect(inRect.x, inRect.y + 98f, inRect.width, inRect.height - 100f);

            float totalContentH = 0f;
            foreach (var rep in filtered)
            {
                bool isExpanded = (expandedAuditMod == rep.PackageId);
                if (isExpanded)
                {
                    List<string> tList = GetFilteredMissingList(rep, expandedAuditCategoryFilter);
                    totalContentH += 56f + 30f + Math.Min(tList.Count, 15) * 22f + 30f;
                }
                else
                {
                    totalContentH += 56f;
                }
            }

            Rect viewRect = new Rect(0f, 0f, listRect.width - 24f, Math.Max(totalContentH, listRect.height));
            Widgets.BeginScrollView(listRect, ref auditResultsScrollPos, viewRect);

            float y = 0f;
            foreach (var rep in filtered)
            {
                bool isExpanded = (expandedAuditMod == rep.PackageId);
                List<string> targetMissing = isExpanded ? GetFilteredMissingList(rep, expandedAuditCategoryFilter) : null;
                float cardH = isExpanded ? (56f + 30f + Math.Min(targetMissing.Count, 15) * 22f + 30f) : 52f;

                if (y + cardH < auditResultsScrollPos.y - 10f || y > auditResultsScrollPos.y + listRect.height + 10f)
                {
                    y += cardH + 4f;
                    continue;
                }

                Rect row = new Rect(0f, y, viewRect.width, cardH);
                Widgets.DrawBoxSolid(row, RUMLUI.ColorCardBgAlt);
                Widgets.DrawHighlightIfMouseover(row);

                // Top Line: Mod Name, PackageId, Tag
                string tag = rep.IsRUMLMod ? " <color=#FFD700>[RUML]</color>" : "";
                if (string.IsNullOrEmpty(tag) && IsVanillaOrDlc(rep.PackageId))
                {
                    tag = string.Equals(rep.PackageId, "ludeon.rimworld", StringComparison.OrdinalIgnoreCase)
                        ? " <color=#E0B020>[Core]</color>"
                        : " <color=#E0B020>[DLC]</color>";
                }
                string builtInBadge = RUMLTranslationDetector.GetBuiltInModBadge(rep.PackageId);
                string builtInTip = RUMLTranslationDetector.GetBuiltInModTooltip(rep.PackageId);
                string transBadge = "";
                string transTip = null;

                List<TargetModInfo> repTargetMods;
                if (RUMLTranslationDetector.IsTranslationMod(rep.PackageId, out repTargetMods))
                {
                    transBadge = RUMLTranslationDetector.GetTranslationModSelfBadge(rep.PackageId, rep.ModName);
                    transTip = RUMLTranslationDetector.GetTranslationModSelfTooltip(rep.PackageId);
                }
                else
                {
                    List<ActiveTranslationModInfo> repActiveTrans;
                    if (RUMLTranslationDetector.HasActiveTranslation(rep.PackageId, out repActiveTrans))
                    {
                        transBadge = RUMLTranslationDetector.GetTargetModBadge(rep.PackageId);
                        transTip = RUMLTranslationDetector.GetTargetModTooltip(rep.PackageId);
                    }
                }

                string title = "<b>" + rep.ModName + "</b>" + tag + builtInBadge + transBadge + " <color=grey>(" + rep.PackageId + ")</color>";
                Widgets.Label(new Rect(row.x + 8f, row.y + 4f, row.width - 150f, 22f), title);

                string resTip = "";
                if (!string.IsNullOrEmpty(builtInTip)) resTip += builtInTip;
                if (!string.IsNullOrEmpty(transTip)) resTip += (string.IsNullOrEmpty(resTip) ? "" : "\n\n") + transTip;
                if (rep.IsRUMLMod)
                {
                    string rumlTip = "RUML_TipRumlActive".CanTranslate() ? (string)"RUML_TipRumlActive".Translate() : "Для этого мода активен перевод из системы RUML.";
                    resTip += (string.IsNullOrEmpty(resTip) ? "" : "\n\n") + rumlTip;
                }
                if (!string.IsNullOrEmpty(resTip))
                {
                    TooltipHandler.TipRegion(row, resTip);
                }

                // Right percentage badge
                Color pCol = rep.Percent >= 99.9f ? new Color(0.2f, 0.9f, 0.3f) : (rep.Percent >= 50f ? new Color(1f, 0.8f, 0.2f) : new Color(1f, 0.3f, 0.3f));
                string pText = "<color=#" + ColorUtility.ToHtmlStringRGB(pCol) + "><b>" + rep.Percent.ToString("F1") + "%</b></color>";
                Widgets.Label(new Rect(row.width - 140f, row.y + 4f, 130f, 22f), pText);

                // Bottom Line: Counts and Status
                string transSub = "RUML_AuditRowTranslated".CanTranslate()
                    ? (string)"RUML_AuditRowTranslated".Translate(rep.TranslatedItems, rep.TotalItems, rep.TranslatedDefs, rep.TotalDefs, rep.TranslatedKeyed, rep.TotalKeyed)
                    : ("Переведено: " + rep.TranslatedItems + " / " + rep.TotalItems + " (Дефы: " + rep.TranslatedDefs + "/" + rep.TotalDefs + ", Keyed: " + rep.TranslatedKeyed + "/" + rep.TotalKeyed + ")  |  ");

                string missSub = rep.MissingItems > 0
                    ? ("<color=#FF7070>" + ("RUML_AuditRowMissing".CanTranslate() ? (string)"RUML_AuditRowMissing".Translate(rep.MissingItems) : ("Не переведено: " + rep.MissingItems + " элементов")) + "</color>")
                    : ("<color=#50E050>" + ("RUML_AuditRowComplete".CanTranslate() ? (string)"RUML_AuditRowComplete".Translate() : "100% Переведено (полное покрытие)") + "</color>");

                Widgets.Label(new Rect(row.x + 8f, row.y + 26f, row.width - 160f, 20f), transSub + missSub);

                // Expand Missing Details Button
                if (rep.MissingItems > 0)
                {
                    Rect expBtnRect = new Rect(row.width - 150f, row.y + 24f, 140f, 22f);
                    string expBtnLabel = isExpanded
                        ? ("RUML_HideDetails".CanTranslate() ? (string)"RUML_HideDetails".Translate() : "Скрыть детали ▲")
                        : ("RUML_ShowMissing".CanTranslate() ? (string)"RUML_ShowMissing".Translate(rep.MissingItems) : ("Пропуски (" + rep.MissingItems + ") ▼"));
                    if (Widgets.ButtonText(expBtnRect, expBtnLabel))
                    {
                        expandedAuditMod = isExpanded ? "" : rep.PackageId;
                        expandedAuditCategoryFilter = 0;
                    }
                }

                // Expanded missing lines preview & category filter
                if (isExpanded)
                {
                    Rect catRow = new Rect(row.x + 12f, row.y + 50f, row.width - 24f, 24f);
                    float catBtnW = 120f;
                    int defMissing = rep.MissingDefs;
                    int keyedMissing = rep.MissingKeyed;

                    string cat0Label = "RUML_FilterAll".CanTranslate() ? (string)"RUML_FilterAll".Translate(rep.MissingItems) : ("Все (" + rep.MissingItems + ")");
                    string cat1Label = "RUML_CategoryDefs".CanTranslate() ? (string)"RUML_CategoryDefs".Translate(defMissing) : ("Дефы (" + defMissing + ")");
                    string cat2Label = "RUML_CategoryKeyed".CanTranslate() ? (string)"RUML_CategoryKeyed".Translate(keyedMissing) : ("Keyed / Настройки (" + keyedMissing + ")");

                    if (DrawTabButton(new Rect(catRow.x, catRow.y, catBtnW, 24f), cat0Label, expandedAuditCategoryFilter == 0))
                    {
                        expandedAuditCategoryFilter = 0;
                    }
                    if (DrawTabButton(new Rect(catRow.x + catBtnW + 8f, catRow.y, catBtnW, 24f), cat1Label, expandedAuditCategoryFilter == 1))
                    {
                        expandedAuditCategoryFilter = 1;
                    }
                    if (DrawTabButton(new Rect(catRow.x + (catBtnW + 8f) * 2, catRow.y, catBtnW + 35f, 24f), cat2Label, expandedAuditCategoryFilter == 2))
                    {
                        expandedAuditCategoryFilter = 2;
                    }

                    Text.Font = GameFont.Tiny;
                    float lineY = catRow.y + 28f;
                    int showCount = Math.Min(targetMissing.Count, 15);
                    for (int i = 0; i < showCount; i++)
                    {
                        Rect lineR = new Rect(row.x + 16f, lineY, row.width - 32f, 20f);
                        Widgets.Label(lineR, "<color=#FFB0B0>• " + targetMissing[i] + "</color>");
                        lineY += 22f;
                    }
                    if (targetMissing.Count > showCount)
                    {
                        Rect moreR = new Rect(row.x + 16f, lineY, row.width - 32f, 20f);
                        string moreText = "RUML_AuditMoreLines".CanTranslate()
                            ? (string)"RUML_AuditMoreLines".Translate(targetMissing.Count - showCount)
                            : ("... и ещё " + (targetMissing.Count - showCount) + " непереведённых строк (полный список доступен в экспорте на Рабочий стол).");
                        Widgets.Label(moreR, "<color=grey>" + moreText + "</color>");
                    }
                    else if (targetMissing.Count == 0)
                    {
                        Rect emptyR = new Rect(row.x + 16f, lineY, row.width - 32f, 20f);
                        string emptyCatText = "RUML_AllStringsTranslatedInCat".CanTranslate()
                            ? (string)"RUML_AllStringsTranslatedInCat".Translate()
                            : "В данной категории все строки переведены.";
                        Widgets.Label(emptyR, "<color=#50E050>" + emptyCatText + "</color>");
                    }
                    Text.Font = GameFont.Small;
                }

                y += cardH + 4f;
            }

            Widgets.EndScrollView();
        }

        private List<string> GetFilteredMissingList(ModAuditReport rep, int categoryFilter)
        {
            if (rep == null) return new List<string>();
            if (categoryFilter == 1)
            {
                List<string> defs = new List<string>();
                defs.AddRange(rep.MissingDefLabels);
                defs.AddRange(rep.MissingDefDescriptions);
                defs.AddRange(rep.MissingDefOther);
                return defs;
            }
            if (categoryFilter == 2)
            {
                return rep.MissingKeyedList;
            }
            return rep.MissingList;
        }

        // =========================================================================
        // TAB 2: GITHUB CLOUD THIRD-PARTY TRANSLATIONS
        // =========================================================================
        private void DrawCloudTab(Rect inRect)
        {
            // Row 1: Catalog Sync & Management Actions
            float btnH = 28f;
            float b1W = 160f; // Обновить каталог
            float b2W = 210f; // Обновить все активные
            float b3W = 210f; // Открыть папку переводов

            Rect b1 = new Rect(inRect.x, inRect.y, b1W, btnH);
            string updCatText = "RUML_UpdateCatalog".CanTranslate() ? (string)"RUML_UpdateCatalog".Translate() : "Обновить каталог";
            if (Widgets.ButtonText(b1, updCatText))
            {
                RUMLCloudManager.FetchManifestAsync(Settings.cloudManifestUrl, Content);
            }
            TooltipHandler.TipRegion(b1, "Загрузить свежий список доступных переводов из GitHub-каталога.");

            Rect b4 = new Rect(b1.xMax + 10f, inRect.y, b2W, btnH);
            Color prevAllCol = GUI.color;
            GUI.color = RUMLUI.ColorButtonGreen;
            string updAllText = "RUML_UpdateAllActive".CanTranslate() ? (string)"RUML_UpdateAllActive".Translate() : "Обновить все активные";
            if (Widgets.ButtonText(b4, updAllText))
            {
                RUMLCloudManager.UpdateAllActiveAsync(Content, Settings);
            }
            GUI.color = prevAllCol;
            TooltipHandler.TipRegion(b4, "Автоматически скачать и обновить все активные переводы, для которых вышли новые версии.");

            Rect b2 = new Rect(b4.xMax + 10f, inRect.y, b3W, btnH);
            string openTransDir = "RUML_OpenTranslationsFolder".CanTranslate() ? (string)"RUML_OpenTranslationsFolder".Translate() : "Открыть папку переводов";
            if (Widgets.ButtonText(b2, openTransDir))
            {
                RUMLCloudManager.OpenTranslationsFolderInExplorer();
            }
            TooltipHandler.TipRegion(b2, RUMLFolderManager.GetExternalTranslationsDir());

            Rect bSettings = new Rect(inRect.xMax - 150f, inRect.y, 150f, btnH);
            string settingsBtnText = "RUML_CatalogSettings".CanTranslate() ? (string)"RUML_CatalogSettings".Translate() : "Настройки URL »";
            if (Widgets.ButtonText(bSettings, settingsBtnText))
            {
                currentMainTab = 3;
            }
            TooltipHandler.TipRegion(bSettings, "Перейти во вкладку настроек (выбор источника manifest.json, экспорт шаблона и др.)");

            // Row 2: Status Message & Progress
            Rect statusR = new Rect(inRect.x, inRect.y + 34f, inRect.width, 24f);
            RUMLUI.DrawStatusRibbon(statusR);

            // Row 3: Search filter, Status filter, Language filter & Author dropdown
            float statusBtnW = 160f;
            float langBtnW = 180f;
            float authBtnW = 160f;
            float totalFilterBtnsW = statusBtnW + langBtnW + authBtnW + 30f;
            Rect searchR = new Rect(inRect.x, inRect.y + 62f, inRect.width - totalFilterBtnsW, 28f);
            RUMLUI.DrawSearchBar(searchR, ref cloudSearchFilter);

            string effLang = RUMLFolderManager.GetTargetLanguageFolder();
            string effFriendly = RUMLFolderManager.GetLanguageFriendlyName(effLang);

            // Status Filter Button (Все / Установленные / Не установленные / С обновлениями)
            Rect statusBtnR = new Rect(searchR.xMax + 10f, inRect.y + 62f, statusBtnW, 28f);
            string statusBtnLabel;
            if (cloudStatusFilter == 1)
            {
                statusBtnLabel = "RUML_StatusFilterInstalled".CanTranslate() ? (string)"RUML_StatusFilterInstalled".Translate() : "Установленные";
            }
            else if (cloudStatusFilter == 2)
            {
                statusBtnLabel = "RUML_StatusFilterNotInstalled".CanTranslate() ? (string)"RUML_StatusFilterNotInstalled".Translate() : "Не установленные";
            }
            else if (cloudStatusFilter == 3)
            {
                statusBtnLabel = "RUML_StatusFilterUpdates".CanTranslate() ? (string)"RUML_StatusFilterUpdates".Translate() : "С обновлениями";
            }
            else
            {
                statusBtnLabel = "RUML_StatusFilterAll".CanTranslate() ? (string)"RUML_StatusFilterAll".Translate() : "Все переводы";
            }
            statusBtnLabel += " ▼";

            if (Widgets.ButtonText(statusBtnR, statusBtnLabel))
            {
                int cAll, cInst, cNotInst, cUpd;
                RUMLCloudManager.GetStatusCounts(cloudAuthorFilter, cloudSearchFilter, (cloudLanguageFilter == "auto") ? effLang : cloudLanguageFilter, out cAll, out cInst, out cNotInst, out cUpd);

                List<FloatMenuOption> sOpts = new List<FloatMenuOption>();
                string optAll = "RUML_FilterAllCount".CanTranslate() ? (string)"RUML_FilterAllCount".Translate(cAll) : ("Все (" + cAll + ")");
                sOpts.Add(new FloatMenuOption(optAll, delegate()
                {
                    cloudStatusFilter = 0;
                }));

                string optInst = "RUML_FilterInstalledCount".CanTranslate() ? (string)"RUML_FilterInstalledCount".Translate(cInst) : ("Установленные (" + cInst + ")");
                sOpts.Add(new FloatMenuOption(optInst, delegate()
                {
                    cloudStatusFilter = 1;
                }));

                string optNotInst = "RUML_FilterNotInstalledCount".CanTranslate() ? (string)"RUML_FilterNotInstalledCount".Translate(cNotInst) : ("Не установленные (" + cNotInst + ")");
                sOpts.Add(new FloatMenuOption(optNotInst, delegate()
                {
                    cloudStatusFilter = 2;
                }));

                string optUpd = "RUML_FilterUpdatesCount".CanTranslate() ? (string)"RUML_FilterUpdatesCount".Translate(cUpd) : ("Есть обновления (" + cUpd + ")");
                sOpts.Add(new FloatMenuOption(optUpd, delegate()
                {
                    cloudStatusFilter = 3;
                }));

                Find.WindowStack.Add(new FloatMenu(sOpts));
            }

            // Language Filter Button
            Rect langBtnR = new Rect(statusBtnR.xMax + 10f, inRect.y + 62f, langBtnW, 28f);
            string langBtnLabel;
            if (cloudLanguageFilter == "auto")
            {
                langBtnLabel = "RUML_LanguageAuto".CanTranslate() ? (string)"RUML_LanguageAuto".Translate(effFriendly) : ("Авто (" + effFriendly + ")");
            }
            else if (string.IsNullOrEmpty(cloudLanguageFilter) || cloudLanguageFilter == "all")
            {
                langBtnLabel = "RUML_LanguageAll".CanTranslate() ? (string)"RUML_LanguageAll".Translate() : "Все языки";
            }
            else
            {
                string curFriendly = RUMLFolderManager.GetLanguageFriendlyName(cloudLanguageFilter);
                langBtnLabel = "RUML_LangFilter".CanTranslate() ? (string)"RUML_LangFilter".Translate(curFriendly) : ("Язык: " + curFriendly);
            }
            langBtnLabel += " ▼";

            if (Widgets.ButtonText(langBtnR, langBtnLabel))
            {
                List<FloatMenuOption> langOpts = new List<FloatMenuOption>();
                string autoText = "RUML_LanguageAuto".CanTranslate() ? (string)"RUML_LanguageAuto".Translate(effFriendly) : ("Авто (" + effFriendly + ")");
                langOpts.Add(new FloatMenuOption(autoText, delegate()
                {
                    cloudLanguageFilter = "auto";
                }));
                string allText = "RUML_LanguageAll".CanTranslate() ? (string)"RUML_LanguageAll".Translate() : "Все языки";
                langOpts.Add(new FloatMenuOption(allText, delegate()
                {
                    cloudLanguageFilter = "all";
                }));

                List<string> availLangs = RUMLCloudManager.GetAvailableLanguages();
                for (int li = 0; li < availLangs.Count; li++)
                {
                    string lName = availLangs[li];
                    string dispName = RUMLFolderManager.GetLanguageFriendlyName(lName);
                    string optLabel = string.Equals(dispName, lName, StringComparison.OrdinalIgnoreCase) ? lName : (dispName + " (" + lName + ")");
                    langOpts.Add(new FloatMenuOption(optLabel, delegate()
                    {
                        cloudLanguageFilter = lName;
                    }));
                }
                Find.WindowStack.Add(new FloatMenu(langOpts));
            }

            // Author Filter Button
            Rect authorBtnR = new Rect(langBtnR.xMax + 10f, inRect.y + 62f, authBtnW, 28f);
            string authBtnLabel = string.IsNullOrEmpty(cloudAuthorFilter)
                ? ("RUML_AllAuthors".CanTranslate() ? (string)"RUML_AllAuthors".Translate(RUMLCloudManager.Items.Count) : "Все авторы ▼")
                : ("RUML_AuthorFilter".CanTranslate() ? (string)"RUML_AuthorFilter".Translate(cloudAuthorFilter) : ("Автор: " + cloudAuthorFilter + " ▼"));
            if (!authBtnLabel.EndsWith("▼")) authBtnLabel += " ▼";

            if (Widgets.ButtonText(authorBtnR, authBtnLabel))
            {
                List<FloatMenuOption> opts = new List<FloatMenuOption>();
                string allAuthText = "RUML_AllAuthors".CanTranslate() ? (string)"RUML_AllAuthors".Translate(RUMLCloudManager.Items.Count) : ("Все авторы (" + RUMLCloudManager.Items.Count + ")");
                opts.Add(new FloatMenuOption(allAuthText, delegate()
                {
                    cloudAuthorFilter = "";
                }));
                foreach (KeyValuePair<string, int> kv in RUMLCloudManager.GetAuthorCounts())
                {
                    string aName = kv.Key;
                    int aCount = kv.Value;
                    opts.Add(new FloatMenuOption(aName + " (" + aCount + ")", delegate()
                    {
                        cloudAuthorFilter = aName;
                    }));
                }
                Find.WindowStack.Add(new FloatMenu(opts));
            }

            // List of Grouped Cloud Translations
            Rect listRect = new Rect(inRect.x, inRect.y + 96f, inRect.width, inRect.height - 138f);
            string effectiveFilterLang = (cloudLanguageFilter == "auto") ? effLang : cloudLanguageFilter;
            int curCloudCount = RUMLCloudManager.Items != null ? RUMLCloudManager.Items.Count : 0;
            if (cachedCloudGroups == null ||
                lastCloudAuthorFilter != cloudAuthorFilter ||
                lastCloudSearchFilter != cloudSearchFilter ||
                lastCloudLangFilter != effectiveFilterLang ||
                lastCloudStatusFilter != cloudStatusFilter ||
                lastCloudItemsCount != curCloudCount)
            {
                lastCloudAuthorFilter = cloudAuthorFilter;
                lastCloudSearchFilter = cloudSearchFilter;
                lastCloudLangFilter = effectiveFilterLang;
                lastCloudStatusFilter = cloudStatusFilter;
                lastCloudItemsCount = curCloudCount;
                cachedCloudGroups = RUMLCloudManager.GetGroupedItems(cloudAuthorFilter, cloudSearchFilter, effectiveFilterLang, cloudStatusFilter);
            }

            Rect viewRect = new Rect(0f, 0f, listRect.width - 24f, Math.Max(cachedCloudGroups.Count * 84f, listRect.height));
            Widgets.BeginScrollView(listRect, ref cloudScrollPos, viewRect);

            float curY = 0f;
            for (int gi = 0; gi < cachedCloudGroups.Count; gi++)
            {
                if (curY + 84f < cloudScrollPos.y - 10f || curY > cloudScrollPos.y + listRect.height + 10f)
                {
                    curY += 84f;
                    continue;
                }

                var grp = cachedCloudGroups[gi];
                string modKey = string.IsNullOrEmpty(grp.PackageId) ? grp.ModName : grp.PackageId;
                int selIdx = 0;
                if (!selectedAuthorIndexByMod.TryGetValue(modKey, out selIdx))
                {
                    selIdx = 0;
                    for (int v = 0; v < grp.Versions.Count; v++)
                    {
                        if (grp.Versions[v].IsInstalled)
                        {
                            selIdx = v;
                            break;
                        }
                    }
                }
                if (selIdx < 0 || selIdx >= grp.Versions.Count) selIdx = 0;
                CloudTranslationItem item = grp.Versions[selIdx];

                Rect card = new Rect(0f, curY, viewRect.width, 78f);
                Widgets.DrawBoxSolid(card, RUMLUI.ColorCardBg);
                Widgets.DrawHighlightIfMouseover(card);

                // Line 1: Mod Name, Version, and Author Selector
                string upBadge = item.HasUpdate 
                    ? (" <color=#FFD700>" + ("RUML_UpdateAvailable".CanTranslate() ? (string)"RUML_UpdateAvailable".Translate() : "[Доступно обновление]") + "</color>") 
                    : "";
                string authorPrefix = "RUML_TableColAuthor".CanTranslate() ? (string)"RUML_TableColAuthor".Translate() : "Автор";

                if (grp.Versions.Count > 1)
                {
                    string prefix = "<b>" + item.ModName + "</b>" + upBadge + "  <color=grey>(v" + item.Version + ")</color>  " + authorPrefix + ": ";
                    float prefixW = Text.CalcSize(prefix.StripTags()).x;
                    Rect prefixR = new Rect(card.x + 8f, card.y + 5f, prefixW + 4f, 22f);
                    Widgets.Label(prefixR, prefix);

                    string selAuthText = "<color=#40E0D0>" + item.Author + "</color> (" + grp.Versions.Count + " авт.) ▼";
                    float rowAuthBtnW = Math.Max(Text.CalcSize(selAuthText.StripTags()).x + 20f, 120f);
                    Rect authBtnR = new Rect(prefixR.xMax + 4f, card.y + 3f, rowAuthBtnW, 24f);
                    if (Widgets.ButtonText(authBtnR, selAuthText))
                    {
                        List<FloatMenuOption> authOpts = new List<FloatMenuOption>();
                        for (int v = 0; v < grp.Versions.Count; v++)
                        {
                            int vIdx = v;
                            CloudTranslationItem vItem = grp.Versions[v];
                            string optLabel = vItem.Author + " (v" + vItem.Version + ")" + (vItem.IsInstalled ? " [Установлен]" : "");
                            authOpts.Add(new FloatMenuOption(optLabel, delegate()
                            {
                                selectedAuthorIndexByMod[modKey] = vIdx;
                            }));
                        }
                        Find.WindowStack.Add(new FloatMenu(authOpts));
                    }
                }
                else
                {
                    string fullTitle = "<b>" + item.ModName + "</b>" + upBadge + "  <color=grey>(v" + item.Version + ")</color>  " + authorPrefix + ": <color=#40E0D0>" + item.Author + "</color>";
                    Rect fullTitleR = new Rect(card.x + 8f, card.y + 5f, card.width - 230f, 22f);
                    Widgets.Label(fullTitleR, fullTitle);
                }

                // Line 2: Description
                string desc = "<color=#C0C0C0>" + item.Description + "</color>";
                Widgets.Label(new Rect(card.x + 8f, card.y + 28f, card.width - 230f, 22f), desc);

                // Line 3: Mod active status
                string modStatus = item.IsTargetModActive
                    ? ("<color=#50E050>" + ("RUML_ModActiveInGame".CanTranslate() ? (string)"RUML_ModActiveInGame".Translate(item.PackageId) : ("• Целевой мод активен в игре (" + item.PackageId + ")")) + "</color>")
                    : ("<color=#FFA040>" + ("RUML_ModInactiveInGame".CanTranslate() ? (string)"RUML_ModInactiveInGame".Translate(item.PackageId) : ("• Мод не обнаружен в списке активных модов (" + item.PackageId + ")")) + "</color>");

                List<ActiveTranslationModInfo> cloudTransList;
                if (RUMLTranslationDetector.HasActiveTranslation(item.PackageId, out cloudTransList))
                {
                    string alrTrans = "RUML_ModAlreadyTranslatedInGame".CanTranslate() ? (string)"RUML_ModAlreadyTranslatedInGame".Translate() : "В игре уже есть перевод";
                    modStatus += "  |<color=#E0A020>[" + alrTrans + "]</color>";
                    string cloudTransTip = RUMLTranslationDetector.GetTargetModTooltip(item.PackageId);
                    if (!string.IsNullOrEmpty(cloudTransTip))
                    {
                        TooltipHandler.TipRegion(card, cloudTransTip);
                    }
                }
                Widgets.Label(new Rect(card.x + 8f, card.y + 51f, card.width - 230f, 22f), modStatus);

                // Action Buttons (Right)
                if (item.IsInstalled)
                {
                    Rect uninstR = new Rect(card.width - 215f, card.y + 22f, 95f, 34f);
                    Color prevCol = GUI.color;
                    GUI.color = RUMLUI.ColorDestructiveRed;
                    string uninstText = "RUML_Uninstall".CanTranslate() ? (string)"RUML_Uninstall".Translate() : "Удалить";
                    if (Widgets.ButtonText(uninstR, uninstText))
                    {
                        RUMLCloudManager.Uninstall(item, Content, Settings);
                    }
                    GUI.color = prevCol;

                    Rect updateR = new Rect(card.width - 115f, card.y + 22f, 110f, 34f);
                    Color prevUpCol = GUI.color;
                    if (item.HasUpdate)
                    {
                        GUI.color = RUMLUI.ColorAccentGold;
                    }
                    string upLabel = item.HasUpdate
                        ? ("RUML_StatusUpdateAvailable".CanTranslate() ? (string)"RUML_StatusUpdateAvailable".Translate() : "Обновить!")
                        : ("RUML_Update".CanTranslate() ? (string)"RUML_Update".Translate() : "Обновить");
                    if (Widgets.ButtonText(updateR, upLabel))
                    {
                        RUMLCloudManager.DownloadAndInstallAsync(item, Content, Settings);
                    }
                    GUI.color = prevUpCol;
                }
                else
                {
                    Rect dlR = new Rect(card.width - 215f, card.y + 22f, 210f, 34f);
                    Color prevC = GUI.color;
                    GUI.color = RUMLUI.ColorButtonGreen;
                    string dlLabel = Settings.manualApplyMode
                        ? ("RUML_Download".CanTranslate() ? (string)"RUML_Download".Translate() : "Скачать")
                        : ("RUML_DownloadAndApply".CanTranslate() ? (string)"RUML_DownloadAndApply".Translate() : "Скачать и применить");
                    if (Widgets.ButtonText(dlR, dlLabel))
                    {
                        RUMLCloudManager.DownloadAndInstallAsync(item, Content, Settings);
                    }
                    GUI.color = prevC;
                }

                curY += 84f;
            }

            Widgets.EndScrollView();

            // Bottom Controls: Unified Apply Bar
            RUMLUI.DrawBottomApplyBar(inRect, Content, Settings);
        }

        // =========================================================================
        // TAB 3: MOD SETTINGS & CONFIGURATION
        // =========================================================================
        private void DrawSettingsTab(Rect inRect)
        {
            Rect listRect = new Rect(inRect.x, inRect.y, inRect.width, inRect.height);
            float curY = 10f;
            float contentW = inRect.width - 44f;
            float leftX = 10f;

            // Compute dynamic view height
            float viewHeight = 560f;
            Rect viewRect = new Rect(0f, 0f, listRect.width - 24f, viewHeight);

            Widgets.BeginScrollView(listRect, ref settingsScrollPos, viewRect);

            // =========================================================================
            // 1. TARGET LANGUAGE SETTINGS CARD
            // =========================================================================
            Rect cardLang = new Rect(leftX, curY, contentW, 125f);
            Widgets.DrawBoxSolid(cardLang, RUMLUI.ColorCardBg);
            Widgets.DrawHighlightIfMouseover(cardLang);

            string h1 = "RUML_SettingsLangHeader".CanTranslate() ? (string)"RUML_SettingsLangHeader".Translate() : "Параметры целевого языка";
            Widgets.Label(new Rect(cardLang.x + 10f, cardLang.y + 8f, cardLang.width - 20f, 24f), "<color=#80D0FF><b>" + h1 + "</b></color>");

            string d1 = "RUML_SettingsLangDesc".CanTranslate() ? (string)"RUML_SettingsLangDesc".Translate() : "Определяет, в какую языковую папку загружаются и монтируются переводы, а также какой язык анализируется Аудитором.";
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(cardLang.x + 10f, cardLang.y + 32f, cardLang.width - 20f, 22f), "<color=#A0A0A0>" + d1 + "</color>");
            Text.Font = GameFont.Small;

            string effLang = RUMLFolderManager.GetTargetLanguageFolder();
            string effFriendly = RUMLFolderManager.GetLanguageFriendlyName(effLang);
            string curLangLabel = "RUML_SettingsCurrentLang".CanTranslate() ? (string)"RUML_SettingsCurrentLang".Translate() : "Текущий целевой язык:";
            Widgets.Label(new Rect(cardLang.x + 10f, cardLang.y + 58f, 200f, 30f), curLangLabel);

            string tLangDisplay;
            if (Settings.targetLanguage == "auto" || string.IsNullOrEmpty(Settings.targetLanguage))
            {
                tLangDisplay = "RUML_LanguageAuto".CanTranslate() ? (string)"RUML_LanguageAuto".Translate(effFriendly) : ("Авто (" + effFriendly + ")");
            }
            else
            {
                string setFriendly = RUMLFolderManager.GetLanguageFriendlyName(Settings.targetLanguage);
                tLangDisplay = string.Equals(setFriendly, Settings.targetLanguage, StringComparison.OrdinalIgnoreCase) ? Settings.targetLanguage : (setFriendly + " (" + Settings.targetLanguage + ")");
            }
            string tLangBtnText = tLangDisplay + " ▼";

            Rect tLangBtnRect = new Rect(cardLang.x + 215f, cardLang.y + 54f, 280f, 30f);
            if (Widgets.ButtonText(tLangBtnRect, tLangBtnText))
            {
                List<FloatMenuOption> lOpts = new List<FloatMenuOption>();
                string autoOptText = "RUML_LanguageAuto".CanTranslate() ? (string)"RUML_LanguageAuto".Translate(effFriendly) : ("Авто (" + effFriendly + ")");
                lOpts.Add(new FloatMenuOption(autoOptText, delegate()
                {
                    Settings.targetLanguage = "auto";
                    RUMLTranslationDetector.ScanRunningMods();
                }));

                if (LanguageDatabase.AllLoadedLanguages != null)
                {
                    foreach (LoadedLanguage l in LanguageDatabase.AllLoadedLanguages)
                    {
                        if (l != null && !string.IsNullOrEmpty(l.folderName))
                        {
                            string fName = RUMLFolderManager.NormalizeLanguageName(l.folderName);
                            string disp = string.IsNullOrEmpty(l.FriendlyNameNative) ? fName : (l.FriendlyNameNative + " (" + fName + ")");
                            lOpts.Add(new FloatMenuOption(disp, delegate()
                            {
                                Settings.targetLanguage = fName;
                                RUMLTranslationDetector.ScanRunningMods();
                            }));
                        }
                    }
                }
                Find.WindowStack.Add(new FloatMenu(lOpts));
            }

            string activeDirText = "RUML_SettingsActiveGameFolder".CanTranslate() ? (string)"RUML_SettingsActiveGameFolder".Translate(effLang) : ("Активная языковая папка в игре: Languages/" + effLang);
            Widgets.Label(new Rect(cardLang.x + 10f, cardLang.y + 92f, cardLang.width - 20f, 24f), "<color=#50E050>" + activeDirText + "</color>");

            curY += 137f;

            // =========================================================================
            // 2. APPLICATION MODE CARD
            // =========================================================================
            Rect cardApply = new Rect(leftX, curY, contentW, 105f);
            Widgets.DrawBoxSolid(cardApply, RUMLUI.ColorCardBg);
            Widgets.DrawHighlightIfMouseover(cardApply);

            string h2 = "RUML_SettingsApplyHeader".CanTranslate() ? (string)"RUML_SettingsApplyHeader".Translate() : "Режим применения изменений";
            Widgets.Label(new Rect(cardApply.x + 10f, cardApply.y + 8f, cardApply.width - 20f, 24f), "<color=#80D0FF><b>" + h2 + "</b></color>");

            Rect chkBoxRect = new Rect(cardApply.x + 10f, cardApply.y + 36f, 24f, 24f);
            Widgets.Checkbox(chkBoxRect.x, chkBoxRect.y, ref Settings.manualApplyMode);

            string modeLabel = "RUML_ManualApplyMode".CanTranslate() ? (string)"RUML_ManualApplyMode".Translate() : "Режим «Применить по кнопке» (мгновенные действия без задержек и зависаний)";
            Rect modeLabelRect = new Rect(cardApply.x + 42f, cardApply.y + 36f, cardApply.width - 52f, 26f);
            Widgets.Label(modeLabelRect, modeLabel);

            string modeTip = "RUML_ManualApplyModeTooltip".CanTranslate() ? (string)"RUML_ManualApplyModeTooltip".Translate() : "В этом режиме скачивание, включение, отключение и удаление переводов выполняются мгновенно без повторной перезагрузки всей базы данных игры. Чтобы применить изменения в игре, нажмите кнопку применения внизу вкладки установленных переводов.";
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(cardApply.x + 42f, cardApply.y + 62f, cardApply.width - 52f, 36f), "<color=#A0A0A0>" + modeTip + "</color>");
            Text.Font = GameFont.Small;

            curY += 117f;

            // =========================================================================
            // 3. CLOUD CATALOG CONFIGURATION CARD
            // =========================================================================
            Rect cardCloud = new Rect(leftX, curY, contentW, 135f);
            Widgets.DrawBoxSolid(cardCloud, RUMLUI.ColorCardBg);
            Widgets.DrawHighlightIfMouseover(cardCloud);

            string h3 = "RUML_SettingsCloudHeader".CanTranslate() ? (string)"RUML_SettingsCloudHeader".Translate() : "Облачный каталог переводов";
            Widgets.Label(new Rect(cardCloud.x + 10f, cardCloud.y + 8f, cardCloud.width - 20f, 24f), "<color=#80D0FF><b>" + h3 + "</b></color>");

            string urlLabel = "RUML_SettingsManifestUrl".CanTranslate() ? (string)"RUML_SettingsManifestUrl".Translate() : "URL манифеста переводов (GitHub raw JSON):";
            Widgets.Label(new Rect(cardCloud.x + 10f, cardCloud.y + 34f, cardCloud.width - 20f, 22f), urlLabel);

            Rect urlFieldR = new Rect(cardCloud.x + 10f, cardCloud.y + 58f, cardCloud.width - 20f, 28f);
            Settings.cloudManifestUrl = Widgets.TextField(urlFieldR, Settings.cloudManifestUrl);

            // Row with 3 action buttons evenly spaced
            float btnSpacing = 10f;
            float btnW = (cardCloud.width - 20f - (btnSpacing * 2f)) / 3f;

            Rect resetUrlR = new Rect(cardCloud.x + 10f, cardCloud.y + 94f, btnW, 28f);
            string resetText = "RUML_SettingsResetUrl".CanTranslate() ? (string)"RUML_SettingsResetUrl".Translate() : "Сбросить URL";
            if (Widgets.ButtonText(resetUrlR, resetText))
            {
                Settings.cloudManifestUrl = RUMLSettings.DefaultManifestUrl;
            }
            TooltipHandler.TipRegion(resetUrlR, "Восстановить исходный официальный адрес каталога переводов: " + RUMLSettings.DefaultManifestUrl);

            Rect expMfR = new Rect(resetUrlR.xMax + btnSpacing, cardCloud.y + 94f, btnW, 28f);
            string expMfText = "RUML_ExportManifestTemplate".CanTranslate() ? (string)"RUML_ExportManifestTemplate".Translate() : "Экспорт manifest.json";
            if (Widgets.ButtonText(expMfR, expMfText))
            {
                string f = RUMLCloudManager.ExportSampleManifest();
                Messages.Message("RUML: Шаблон manifest.json сохранён на Рабочий стол: " + f, MessageTypeDefOf.PositiveEvent, false);
            }
            TooltipHandler.TipRegion(expMfR, "Создать пример файла manifest.json на рабочем столе для создания собственного репозитория переводов.");

            Rect openDirR = new Rect(expMfR.xMax + btnSpacing, cardCloud.y + 94f, btnW, 28f);
            string openTransDir = "RUML_OpenTranslationsFolder".CanTranslate() ? (string)"RUML_OpenTranslationsFolder".Translate() : "Открыть папку переводов";
            if (Widgets.ButtonText(openDirR, openTransDir))
            {
                RUMLCloudManager.OpenTranslationsFolderInExplorer();
            }
            TooltipHandler.TipRegion(openDirR, RUMLFolderManager.GetExternalTranslationsDir());

            curY += 147f;

            // =========================================================================
            // 4. UTILITIES & MAINTENANCE CARD
            // =========================================================================
            Rect cardUtils = new Rect(leftX, curY, contentW, 110f);
            Widgets.DrawBoxSolid(cardUtils, RUMLUI.ColorCardBg);
            Widgets.DrawHighlightIfMouseover(cardUtils);

            string h4 = "RUML_SettingsUtilsHeader".CanTranslate() ? (string)"RUML_SettingsUtilsHeader".Translate() : "Служебные действия";
            Widgets.Label(new Rect(cardUtils.x + 10f, cardUtils.y + 8f, cardUtils.width - 20f, 24f), "<color=#80D0FF><b>" + h4 + "</b></color>");

            Rect reloadBtnR = new Rect(cardUtils.x + 10f, cardUtils.y + 36f, 320f, 30f);
            Color prevRelCol = GUI.color;
            GUI.color = RUMLUI.ColorAccentGreen;
            string relText = "RUML_SettingsReloadLanguage".CanTranslate() ? (string)"RUML_SettingsReloadLanguage".Translate() : "Перезагрузить переводы в памяти игры сейчас";
            if (Widgets.ButtonText(reloadBtnR, relText))
            {
                RUMLFolderManager.ApplyFilter(Content, Settings);
                RUMLFolderManager.ReloadLanguage();
            }
            GUI.color = prevRelCol;

            Rect resetAuditR = new Rect(reloadBtnR.xMax + 10f, cardUtils.y + 36f, 280f, 30f);
            string resetAuditText = "RUML_SettingsResetAudit".CanTranslate() ? (string)"RUML_SettingsResetAudit".Translate() : "Сбросить выбор аудитора модов";
            if (Widgets.ButtonText(resetAuditR, resetAuditText))
            {
                InitDefaultAuditSelection();
                Messages.Message("RUML: Выбор аудитора сброшен к активным модам.", MessageTypeDefOf.PositiveEvent, false);
            }

            string aboutText = "RUML_SettingsAbout".CanTranslate() ? (string)"RUML_SettingsAbout".Translate() : "RUML (RimWorld Universal Mods Localization) | Автономный менеджер локализаций для RimWorld";
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(cardUtils.x + 10f, cardUtils.y + 76f, cardUtils.width - 20f, 24f), "<color=#707070>" + aboutText + "</color>");
            Text.Font = GameFont.Small;

            Widgets.EndScrollView();
        }
    }
}
