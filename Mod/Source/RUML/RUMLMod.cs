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
                TooltipHandler.TipRegion(clearRect, "Очистить строку поиска");
            }
        }

        public static void DrawStatusRibbon(Rect rect)
        {
            string sText;
            if (RUMLCloudManager.IsBusy)
            {
                sText = "<color=yellow>Загрузка: " + RUMLCloudManager.StatusMessage + " (" + RUMLCloudManager.DownloadPercent.ToString("F0") + "%)</color>";
            }
            else if (RUMLFolderManager.hasPendingChanges)
            {
                sText = "<color=#FFD700>• Есть неприменённые изменения! Нажмите «Применить настройки» внизу окна.</color>";
            }
            else
            {
                sText = "<color=#80D0FF>" + RUMLCloudManager.StatusMessage + "</color>";
            }
            Widgets.Label(rect, sText);
        }

        public static void DrawBottomApplyBar(Rect inRect, ModContentPack content, RUMLSettings settings)
        {
            Rect chkBoxRect = new Rect(inRect.x + 4f, inRect.yMax - 68f, 24f, 24f);
            Widgets.Checkbox(chkBoxRect.x, chkBoxRect.y, ref settings.manualApplyMode);

            Rect modeLabelRect = new Rect(inRect.x + 34f, inRect.yMax - 68f, inRect.width - 40f, 26f);
            Widgets.Label(modeLabelRect, "Режим «Применить по кнопке» (мгновенные действия без задержек и зависаний)");
            TooltipHandler.TipRegion(new Rect(inRect.x, inRect.yMax - 68f, inRect.width, 26f), "В этом режиме скачивание, включение, отключение и удаление переводов выполняются мгновенно без повторной перезагрузки всей базы данных игры. Чтобы применить изменения в игре, нажмите зелёную кнопку ниже.");

            Rect bottomRect = new Rect(inRect.x, inRect.yMax - 38f, inRect.width, 36f);
            Color prevApplyCol = GUI.color;
            if (RUMLFolderManager.hasPendingChanges)
            {
                GUI.color = ColorAccentGreen;
            }
            string btnText = RUMLFolderManager.hasPendingChanges
                ? "★ Применить настройки и перезагрузить переводы в памяти игры (есть изменения!)"
                : "Применить настройки и перезагрузить переводы в памяти игры";
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

        private string modsSearchFilter = "";
        private string auditSearchFilter = "";
        private string auditResultSearchFilter = "";
        private string cloudSearchFilter = "";
        private string cloudAuthorFilter = "";
        private Dictionary<string, int> selectedAuthorIndexByMod = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private List<ModAuditReport> lastAuditReports = null;
        private int currentMainTab = 0; // 0: Built-in Mods, 1: Auditor, 2: GitHub Cloud
        private int auditSubTab = 0;    // 0: Select Active Mods, 1: Audit Results
        private int auditResultMode = 0; // 0: All, 1: With Missing, 2: 100% Translated
        private string expandedAuditMod = "";
        private int expandedAuditCategoryFilter = 0; // 0: All, 1: Defs, 2: Keyed / Settings
        private bool autoCheckUpdatesTriggered = false;

        public static readonly List<string> KnownMods = new List<string>
        {
            "AlphaAnimals", "AlphaArmoury", "AlphaBiomes", "AlphaGenes", "AlphaGenesIntegrated",
            "AlphaMechs", "AlphaMemes", "AlphaRandom", "ArchotechExpanded", "CatsBootsAndGloves",
            "EPOE_ModularCompat", "EPOE_Royalty", "GeneExtractorTiers", "InfoCardPlus", "KabouterXenotype",
            "MoreGroupedBuildings", "PlasmaShieldImplant", "Psycasts2", "RebuildDoorsCorners",
            "ReelsStorage", "RegrowthAspen", "RespliceCore", "SbzFridge", "SbzGravshipStorage",
            "SimpleSidearms", "TooManyMods", "VOE_Factory", "VanometricGenerator"
        };

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
            { "KabouterXenotype", "sovereign.chipchipchip" },
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
            foreach (ModContentPack m in LoadedModManager.RunningMods)
            {
                if (!IsVanillaOrDlc(m.PackageIdPlayerFacing))
                {
                    pids.Add(m.PackageIdPlayerFacing);
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

            // Main Navigation Header (3 Tabs)
            int installedCount = RUMLFolderManager.GetAllInstalledMods().Count;
            int cloudCount = RUMLCloudManager.Items != null ? RUMLCloudManager.Items.Count : 0;

            string tab0Title = "1. Установленные (" + installedCount + ")";
            string tab1Title = "2. Аудитор модов";
            string tab2Title = cloudCount > 0 ? ("3. Каталог переводов (" + cloudCount + ")") : "3. Каталог переводов";

            Rect tabRect = new Rect(inRect.x, inRect.y, inRect.width, 32f);
            float tabWidth = (inRect.width - 20f) / 3f;

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

            Rect contentRect = new Rect(inRect.x, inRect.y + 40f, inRect.width, inRect.height - 40f);

            if (currentMainTab == 0)
            {
                DrawModsTab(contentRect);
            }
            else if (currentMainTab == 1)
            {
                DrawAuditTab(contentRect);
            }
            else
            {
                DrawCloudTab(contentRect);
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
                Widgets.Label(infoRect, "Локализации ещё не установлены в RUML.\nRUML работает как автономное ядро. Вы можете скачать нужные переводы во вкладке «Сторонние переводы (GitHub)».");

                Rect goBtn = new Rect(inRect.x + 10f, infoRect.yMax + 10f, 320f, 34f);
                if (Widgets.ButtonText(goBtn, "Перейти в каталог переводов (GitHub)"))
                {
                    currentMainTab = 2;
                }

                Rect emptyDirRect = new Rect(inRect.x + 10f, goBtn.yMax + 14f, 240f, 32f);
                if (Widgets.ButtonText(emptyDirRect, "Открыть папку переводов"))
                {
                    RUMLCloudManager.OpenTranslationsFolderInExplorer();
                }
                TooltipHandler.TipRegion(emptyDirRect, RUMLFolderManager.GetExternalTranslationsDir());
                return;
            }

            // Top Controls: Row 1 - Search & Batch Toggle
            Rect topRect = new Rect(inRect.x, inRect.y, inRect.width, 30f);
            float btnW = 115f;

            Rect searchRect = new Rect(topRect.x, topRect.y, inRect.width - (btnW * 2 + 20f), 30f);
            RUMLUI.DrawSearchBar(searchRect, ref modsSearchFilter);

            if (Widgets.ButtonText(new Rect(searchRect.xMax + 10f, topRect.y, btnW, 30f), "Включить все"))
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
            if (Widgets.ButtonText(new Rect(searchRect.xMax + btnW + 20f, topRect.y, btnW, 30f), "Отключить все"))
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

            if (Widgets.ButtonText(new Rect(actRow.x, actRow.y, actW1, 30f), "Проверить обновления"))
            {
                RUMLCloudManager.CheckUpdatesAsync(Settings.cloudManifestUrl, Content);
            }

            Color prevBtnCol = GUI.color;
            GUI.color = RUMLUI.ColorButtonGreen;
            if (Widgets.ButtonText(new Rect(actRow.x + actW1 + 10f, actRow.y, actW2, 30f), "Обновить актуальные"))
            {
                RUMLCloudManager.UpdateOutdatedOnlyAsync(Content, Settings);
            }
            GUI.color = prevBtnCol;
            TooltipHandler.TipRegion(new Rect(actRow.x + actW1 + 10f, actRow.y, actW2, 30f), "Скачать обновления ТОЛЬКО для тех активных переводов, для которых вышли новые версии (без повторной загрузки уже актуальных).");

            if (Widgets.ButtonText(new Rect(actRow.x + actW1 + actW2 + 20f, actRow.y, actW3, 30f), "Переустановить все"))
            {
                RUMLCloudManager.UpdateAllActiveAsync(Content, Settings);
            }
            TooltipHandler.TipRegion(new Rect(actRow.x + actW1 + actW2 + 20f, actRow.y, actW3, 30f), "Принудительно заново перекачать и установить все активные переводы.");

            Rect openDirRect = new Rect(actRow.x + actW1 + actW2 + actW3 + 30f, actRow.y, actW4, 30f);
            if (Widgets.ButtonText(openDirRect, "Открыть папку переводов"))
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
            Widgets.Label(h1, "<color=#C0C0C0><b>Мод / Локализация</b></color>");

            Rect h2 = new Rect(tableHeaderRect.x + authorX, tableHeaderRect.y + 2f, authorW, 20f);
            Widgets.Label(h2, "<color=#C0C0C0><b>Автор перевода</b></color>");

            Rect h3 = new Rect(tableHeaderRect.x + statusX, tableHeaderRect.y + 2f, statusW, 20f);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(h3, "<color=#C0C0C0><b>Состояние</b></color>");
            Text.Anchor = TextAnchor.UpperLeft;

            Rect h4 = new Rect(tableHeaderRect.x + actionX, tableHeaderRect.y + 2f, actionW, 20f);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(h4, "<color=#C0C0C0><b>Действие</b></color>");
            Text.Anchor = TextAnchor.UpperLeft;

            // Scrollable List (starts below table header)
            Rect listRect = new Rect(inRect.x, inRect.y + 122f, inRect.width, inRect.height - 198f);
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

                string targetPid = null;
                FolderToPackageId.TryGetValue(modFolder, out targetPid);
                string standaloneBadge = "";
                string transName;
                if (!string.IsNullOrEmpty(targetPid) && RUMLTranslationDetector.HasTranslationMod(targetPid, out transName))
                {
                    standaloneBadge = " <color=#E0A020>[Сторонний мод]</color>";
                }

                string modLabel = "<b>" + modFolder + "</b> <color=#80D0FF>[v" + (string.IsNullOrEmpty(ver) ? "1.0.0" : ver) + "]</color>" + standaloneBadge;
                Rect nameRect = new Rect(nameX + 32f, curY + 6f, nameW - 32f, 26f);
                Widgets.Label(nameRect, modLabel);

                string tip = (isEnabled ? "Включено: нажмите на флажок, чтобы отключить этот перевод." : "Отключено: нажмите на флажок, чтобы включить этот перевод.");
                string tName;
                if (!string.IsNullOrEmpty(targetPid) && RUMLTranslationDetector.HasTranslationMod(targetPid, out tName))
                {
                    tip += "\n\n" + RUMLTranslationDetector.GetTranslationTooltip(targetPid);
                }
                TooltipHandler.TipRegion(new Rect(nameX, curY + 4f, nameW, 30f), tip);

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
                    if (Widgets.ButtonText(statusColRect, "Обновить!"))
                    {
                        RUMLCloudManager.DownloadAndInstallAsync(cloudItem, Content, Settings);
                    }
                    GUI.color = prevUCol;
                    TooltipHandler.TipRegion(statusColRect, "Доступно обновление: v" + (cloudItem.LocalVersion ?? "1.0") + " -> v" + cloudItem.Version);
                }
                else
                {
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(statusColRect, "<color=#50E050>✓ Актуально</color>");
                    Text.Anchor = TextAnchor.UpperLeft;
                }

                // Col 4: Fixed Delete button
                Rect delBtnRect = new Rect(actionX, curY + 5f, actionW, 28f);
                Color prevCol = GUI.color;
                GUI.color = RUMLUI.ColorDestructiveRed;
                if (Widgets.ButtonText(delBtnRect, "Удалить"))
                {
                    RUMLFolderManager.DeleteAuthorTranslation(modFolder, activeAuthor, Content, Settings);
                }
                GUI.color = prevCol;
                TooltipHandler.TipRegion(delBtnRect, "Удалить установленный перевод от автора " + activeAuthor);

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

            string sub0Label = "1. Выбор активных модов (" + selCount + " выбрано)";
            string sub1Label = "2. Результаты анализа (" + repCount + " отчётов)";

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

            // Row 1: Search and Clear
            Rect searchRect = new Rect(inRect.x, inRect.y, inRect.width, 28f);
            RUMLUI.DrawSearchBar(searchRect, ref auditSearchFilter);

            // Row 2: Batch Selection Buttons
            Rect btnRowRect = new Rect(inRect.x, inRect.y + 34f, inRect.width, 28f);
            float bW = (inRect.width - 40f) / 5f;

            if (Widgets.ButtonText(new Rect(btnRowRect.x, btnRowRect.y, bW, 28f), "Выбрать все"))
            {
                List<string> all = new List<string>();
                foreach (var m in running) all.Add(m.PackageIdPlayerFacing);
                Settings.SelectAuditMods(all);
            }
            if (Widgets.ButtonText(new Rect(btnRowRect.x + bW + 10f, btnRowRect.y, bW, 28f), "Снять все"))
            {
                Settings.DeselectAllAuditMods();
            }
            if (Widgets.ButtonText(new Rect(btnRowRect.x + (bW + 10f) * 2, btnRowRect.y, bW, 28f), "Только RUML"))
            {
                List<string> ruml = new List<string>();
                foreach (var m in running)
                {
                    if (FolderToPackageId.Values.Any(p => string.Equals(p, m.PackageIdPlayerFacing, StringComparison.OrdinalIgnoreCase)))
                    {
                        ruml.Add(m.PackageIdPlayerFacing);
                    }
                }
                Settings.SelectAuditMods(ruml);
            }
            if (Widgets.ButtonText(new Rect(btnRowRect.x + (bW + 10f) * 3, btnRowRect.y, bW, 28f), "С переводом"))
            {
                var targets = RUMLTranslationDetector.GetAllTargetPackageIds();
                List<string> withTrans = new List<string>();
                foreach (var m in running)
                {
                    if (targets.Contains(m.PackageId) || targets.Contains(m.PackageIdPlayerFacing))
                    {
                        withTrans.Add(m.PackageIdPlayerFacing);
                    }
                }
                Settings.SelectAuditMods(withTrans);
            }
            if (Widgets.ButtonText(new Rect(btnRowRect.x + (bW + 10f) * 4, btnRowRect.y, bW, 28f), "Без Core/DLC"))
            {
                List<string> nonVanilla = new List<string>();
                foreach (var m in running)
                {
                    if (!IsVanillaOrDlc(m.PackageIdPlayerFacing))
                    {
                        nonVanilla.Add(m.PackageIdPlayerFacing);
                    }
                }
                Settings.SelectAuditMods(nonVanilla);
            }

            // Row 3: Status count
            Rect statusRect = new Rect(inRect.x, inRect.y + 68f, inRect.width, 22f);
            int selectedCount = Settings.auditSelectedPackageIds != null ? Settings.auditSelectedPackageIds.Count : 0;
            int withTransCount = RUMLTranslationDetector.TotalTargetsCount;
            Widgets.Label(statusRect, "Выбрано: <color=cyan>" + selectedCount + "</color> из <color=white>" + running.Count + "</color> активных | Со сторонними модами-переводами: <color=#78D070>" + withTransCount + "</color>");

            // Row 4: Scrollable Active Mods Checkbox List
            Rect listRect = new Rect(inRect.x, inRect.y + 94f, inRect.width, inRect.height - 146f);
            List<ModContentPack> filteredMods = new List<ModContentPack>();
            foreach (var m in running)
            {
                string disp = GetModDisplayName(m);
                if (string.IsNullOrEmpty(auditSearchFilter) ||
                    disp.IndexOf(auditSearchFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    m.PackageIdPlayerFacing.IndexOf(auditSearchFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    filteredMods.Add(m);
                }
            }

            Rect viewRect = new Rect(0f, 0f, listRect.width - 24f, filteredMods.Count * 36f);
            Widgets.BeginScrollView(listRect, ref auditModsScrollPos, viewRect);

            float curY = 0f;
            foreach (var mod in filteredMods)
            {
                string pid = mod.PackageIdPlayerFacing;
                bool isSelected = Settings.IsAuditModSelected(pid);
                Rect rowRect = new Rect(0f, curY, viewRect.width, 32f);
                Widgets.DrawBoxSolid(rowRect, RUMLUI.ColorCardBg);
                Widgets.DrawHighlightIfMouseover(rowRect);

                bool isRuml = FolderToPackageId.Values.Any(p => string.Equals(p, pid, StringComparison.OrdinalIgnoreCase));
                bool isDlc = IsVanillaOrDlc(pid);

                string tag = "";
                if (isRuml)
                {
                    tag = " <color=#40E0D0>[RUML]</color>";
                }
                else if (isDlc)
                {
                    tag = string.Equals(pid, "ludeon.rimworld", StringComparison.OrdinalIgnoreCase)
                        ? " <color=#E0B020>[Core]</color>"
                        : " <color=#E0B020>[DLC]</color>";
                }

                string transBadge = RUMLTranslationDetector.GetTranslationBadge(mod.PackageId) ?? "";
                if (string.IsNullOrEmpty(transBadge) && !string.Equals(mod.PackageId, pid, StringComparison.OrdinalIgnoreCase))
                {
                    transBadge = RUMLTranslationDetector.GetTranslationBadge(pid) ?? "";
                }

                string labelText = "  " + GetModDisplayName(mod) + tag + transBadge + " <color=grey>(" + pid + ")</color>";

                bool newCheck = isSelected;
                Rect checkR = new Rect(rowRect.x + 4f, curY + 1f, rowRect.width - 8f, 30f);
                Widgets.CheckboxLabeled(checkR, labelText, ref newCheck);
                if (newCheck != isSelected)
                {
                    Settings.SetAuditModSelected(pid, newCheck);
                }

                string transTip = RUMLTranslationDetector.GetTranslationTooltip(mod.PackageId) ?? RUMLTranslationDetector.GetTranslationTooltip(pid);
                if (!string.IsNullOrEmpty(transTip))
                {
                    TooltipHandler.TipRegion(rowRect, transTip);
                }

                curY += 36f;
            }

            Widgets.EndScrollView();

            // Bottom Action Button: Run Audit on Selected
            Rect bottomRect = new Rect(inRect.x, inRect.yMax - 44f, inRect.width, 40f);
            Color oldCol = GUI.color;
            GUI.color = RUMLUI.ColorAccentGreen;
            string btnText = "Запустить аудит выбранных модов (" + selectedCount + ")";
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
                Widgets.Label(msgRect, "Аудит ещё не запускался или среди выбранных модов ничего не найдено.\nПерейдите во вкладку 'Выбор активных модов', выберите нужные моды и нажмите зеленую кнопку.");

                Rect goBackRect = new Rect(inRect.x, inRect.y + 110f, 240f, 36f);
                if (Widgets.ButtonText(goBackRect, "Перейти к выбору модов"))
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
            string sumText = "Проверено: <b>" + totalMods + "</b> модов | Элементов: <b>" + sumTotalItems + "</b> (Дефов: " + sumTotalDefs + ", Keyed: " + sumTotalKeyed + ") | Не переведено: <color=#FF6B6B><b>" + sumMissingItems + "</b></color> | Покрытие: <b>" + avgPercent.ToString("F1") + "%</b>";
            Widgets.Label(sumRect, sumText);

            // Action Buttons Row
            Rect actRow = new Rect(inRect.x, inRect.y + 28f, inRect.width, 30f);
            float actW = (inRect.width - 30f) / 4f;

            if (Widgets.ButtonText(new Rect(actRow.x, actRow.y, actW, 30f), "Экспорт отчёта"))
            {
                string f = RUMLAuditor.ExportReportToFile(lastAuditReports);
                Messages.Message("RUML: Отчёт успешно сохранён на Рабочий стол: " + f, MessageTypeDefOf.PositiveEvent, false);
            }
            if (Widgets.ButtonText(new Rect(actRow.x + actW + 10f, actRow.y, actW, 30f), "Экспорт XML-шаблонов"))
            {
                string dir = RUMLAuditor.ExportMissingTemplates(lastAuditReports);
                if (!string.IsNullOrEmpty(dir))
                {
                    Messages.Message("RUML: Шаблоны XML сохранены: " + dir, MessageTypeDefOf.PositiveEvent, false);
                }
            }
            if (Widgets.ButtonText(new Rect(actRow.x + (actW + 10f) * 2, actRow.y, actW, 30f), "Повторить аудит"))
            {
                lastAuditReports = RUMLAuditor.RunAudit(Settings.auditSelectedPackageIds, FolderToPackageId);
            }
            if (Widgets.ButtonText(new Rect(actRow.x + (actW + 10f) * 3, actRow.y, actW, 30f), "Выбор модов"))
            {
                auditSubTab = 0;
            }

            // Filter & Search Row
            Rect filterRow = new Rect(inRect.x, inRect.y + 64f, inRect.width, 28f);
            float fBtnW = 120f;
            Rect srchR = new Rect(filterRow.x, filterRow.y, inRect.width - (fBtnW * 3 + 20f), 28f);
            RUMLUI.DrawSearchBar(srchR, ref auditResultSearchFilter);

            if (DrawTabButton(new Rect(srchR.xMax + 10f, filterRow.y, fBtnW, 28f), "Все (" + totalMods + ")", auditResultMode == 0))
            {
                auditResultMode = 0;
            }
            if (DrawTabButton(new Rect(srchR.xMax + fBtnW + 15f, filterRow.y, fBtnW, 28f), "С пропусками (" + withMissing + ")", auditResultMode == 1))
            {
                auditResultMode = 1;
            }
            if (DrawTabButton(new Rect(srchR.xMax + (fBtnW * 2) + 20f, filterRow.y, fBtnW, 28f), "100% (" + fullTranslated + ")", auditResultMode == 2))
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

                Rect row = new Rect(0f, y, viewRect.width, cardH);
                Widgets.DrawBoxSolid(row, RUMLUI.ColorCardBgAlt);
                Widgets.DrawHighlightIfMouseover(row);

                // Top Line: Mod Name, PackageId, Tag
                string tag = rep.IsRUMLMod ? " <color=#40E0D0>[RUML]</color>" : "";
                if (string.IsNullOrEmpty(tag) && IsVanillaOrDlc(rep.PackageId))
                {
                    tag = string.Equals(rep.PackageId, "ludeon.rimworld", StringComparison.OrdinalIgnoreCase)
                        ? " <color=#E0B020>[Core]</color>"
                        : " <color=#E0B020>[DLC]</color>";
                }
                string transBadge = RUMLTranslationDetector.GetTranslationBadge(rep.PackageId) ?? "";
                string title = "<b>" + rep.ModName + "</b>" + tag + transBadge + " <color=grey>(" + rep.PackageId + ")</color>";
                Widgets.Label(new Rect(row.x + 8f, row.y + 4f, row.width - 150f, 22f), title);

                string transTip = RUMLTranslationDetector.GetTranslationTooltip(rep.PackageId);
                if (!string.IsNullOrEmpty(transTip))
                {
                    TooltipHandler.TipRegion(row, transTip);
                }

                // Right percentage badge
                Color pCol = rep.Percent >= 99.9f ? new Color(0.2f, 0.9f, 0.3f) : (rep.Percent >= 50f ? new Color(1f, 0.8f, 0.2f) : new Color(1f, 0.3f, 0.3f));
                string pText = "<color=#" + ColorUtility.ToHtmlStringRGB(pCol) + "><b>" + rep.Percent.ToString("F1") + "%</b></color>";
                Widgets.Label(new Rect(row.width - 140f, row.y + 4f, 130f, 22f), pText);

                // Bottom Line: Counts and Status
                string sub = "Переведено: " + rep.TranslatedItems + " / " + rep.TotalItems +
                             " (Дефы: " + rep.TranslatedDefs + "/" + rep.TotalDefs + ", Keyed: " + rep.TranslatedKeyed + "/" + rep.TotalKeyed + ")  |  " +
                             (rep.MissingItems > 0
                                 ? "<color=#FF7070>Не переведено: " + rep.MissingItems + " элементов</color>"
                                 : "<color=#50E050>100% Переведено (полное покрытие)</color>");
                Widgets.Label(new Rect(row.x + 8f, row.y + 26f, row.width - 160f, 20f), sub);

                // Expand Missing Details Button
                if (rep.MissingItems > 0)
                {
                    Rect expBtnRect = new Rect(row.width - 150f, row.y + 24f, 140f, 22f);
                    string expBtnLabel = isExpanded ? "Скрыть детали ▲" : ("Пропуски (" + rep.MissingItems + ") ▼");
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

                    if (DrawTabButton(new Rect(catRow.x, catRow.y, catBtnW, 24f), "Все (" + rep.MissingItems + ")", expandedAuditCategoryFilter == 0))
                    {
                        expandedAuditCategoryFilter = 0;
                    }
                    if (DrawTabButton(new Rect(catRow.x + catBtnW + 8f, catRow.y, catBtnW, 24f), "Дефы (" + defMissing + ")", expandedAuditCategoryFilter == 1))
                    {
                        expandedAuditCategoryFilter = 1;
                    }
                    if (DrawTabButton(new Rect(catRow.x + (catBtnW + 8f) * 2, catRow.y, catBtnW + 35f, 24f), "Keyed / Настройки (" + keyedMissing + ")", expandedAuditCategoryFilter == 2))
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
                        Widgets.Label(moreR, "<color=grey>... и ещё " + (targetMissing.Count - showCount) + " непереведённых строк (полный список доступен в экспорте на Рабочий стол).</color>");
                    }
                    else if (targetMissing.Count == 0)
                    {
                        Rect emptyR = new Rect(row.x + 16f, lineY, row.width - 32f, 20f);
                        Widgets.Label(emptyR, "<color=#50E050>В данной категории все строки переведены.</color>");
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
            // Row 1: Manifest URL & Sync Buttons
            Rect urlRow = new Rect(inRect.x, inRect.y, inRect.width, 28f);
            float btnW1 = 125f; // Обновить каталог
            float btnW4 = 145f; // Обновить все
            float btnW2 = 175f; // Открыть папку переводов
            float btnW3 = 125f; // Шаблон manifest
            float totalBtnsW = btnW1 + btnW4 + btnW2 + btnW3 + 40f;

            Rect urlLabelR = new Rect(urlRow.x, urlRow.y, 110f, 28f);
            Widgets.Label(urlLabelR, "GitHub Каталог:");

            Rect urlFieldR = new Rect(urlLabelR.xMax + 5f, urlRow.y, inRect.width - (urlLabelR.width + totalBtnsW + 15f), 28f);
            Settings.cloudManifestUrl = Widgets.TextField(urlFieldR, Settings.cloudManifestUrl);

            Rect b1 = new Rect(urlFieldR.xMax + 10f, urlRow.y, btnW1, 28f);
            if (Widgets.ButtonText(b1, "Обновить каталог"))
            {
                RUMLCloudManager.FetchManifestAsync(Settings.cloudManifestUrl, Content);
            }

            Rect b4 = new Rect(b1.xMax + 10f, urlRow.y, btnW4, 28f);
            Color prevAllCol = GUI.color;
            GUI.color = RUMLUI.ColorButtonGreen;
            if (Widgets.ButtonText(b4, "Обновить все"))
            {
                RUMLCloudManager.UpdateAllActiveAsync(Content, Settings);
            }
            GUI.color = prevAllCol;
            TooltipHandler.TipRegion(b4, "Автоматически скачать и обновить все активные переводы, для которых вышли новые версии");

            Rect b2 = new Rect(b4.xMax + 10f, urlRow.y, btnW2, 28f);
            if (Widgets.ButtonText(b2, "Открыть папку переводов"))
            {
                RUMLCloudManager.OpenTranslationsFolderInExplorer();
            }
            TooltipHandler.TipRegion(b2, RUMLFolderManager.GetExternalTranslationsDir());

            Rect b3 = new Rect(b2.xMax + 10f, urlRow.y, btnW3, 28f);
            if (Widgets.ButtonText(b3, "Шаблон manifest"))
            {
                string f = RUMLCloudManager.ExportSampleManifest();
                Messages.Message("RUML: Шаблон manifest.json сохранён на Рабочий стол: " + f, MessageTypeDefOf.PositiveEvent, false);
            }

            // Row 2: Status Message & Progress
            Rect statusR = new Rect(inRect.x, inRect.y + 34f, inRect.width, 24f);
            RUMLUI.DrawStatusRibbon(statusR);

            // Row 3: Search filter & Author dropdown
            Rect searchR = new Rect(inRect.x, inRect.y + 62f, inRect.width - 210f, 28f);
            RUMLUI.DrawSearchBar(searchR, ref cloudSearchFilter);

            Rect authorBtnR = new Rect(searchR.xMax + 10f, inRect.y + 62f, 200f, 28f);
            string authBtnLabel = string.IsNullOrEmpty(cloudAuthorFilter) ? "Все авторы ▼" : ("Автор: " + cloudAuthorFilter + " ▼");
            if (Widgets.ButtonText(authorBtnR, authBtnLabel))
            {
                List<FloatMenuOption> opts = new List<FloatMenuOption>();
                opts.Add(new FloatMenuOption("Все авторы (" + RUMLCloudManager.Items.Count + ")", delegate()
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
            Rect listRect = new Rect(inRect.x, inRect.y + 96f, inRect.width, inRect.height - 172f);
            List<CloudModGroup> groups = RUMLCloudManager.GetGroupedItems(cloudAuthorFilter, cloudSearchFilter);

            Rect viewRect = new Rect(0f, 0f, listRect.width - 24f, Math.Max(groups.Count * 84f, listRect.height));
            Widgets.BeginScrollView(listRect, ref cloudScrollPos, viewRect);

            float curY = 0f;
            foreach (var grp in groups)
            {
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
                string upBadge = item.HasUpdate ? " <color=#FFD700>[Доступно обновление]</color>" : "";
                string titlePrefix = "<b>" + item.ModName + "</b>" + upBadge + "  <color=grey>(v" + item.Version + ")</color>  Автор: ";
                float prefixW = Text.CalcSize(titlePrefix).x;
                Rect prefixR = new Rect(card.x + 8f, card.y + 5f, prefixW, 22f);
                Widgets.Label(prefixR, titlePrefix);

                if (grp.Versions.Count > 1)
                {
                    string selAuthText = "<color=#40E0D0>" + item.Author + "</color> (" + grp.Versions.Count + " авт.) ▼";
                    float authBtnW = Math.Max(Text.CalcSize(selAuthText).x + 16f, 130f);
                    Rect authBtnR = new Rect(prefixR.xMax + 2f, card.y + 3f, authBtnW, 24f);
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
                    string authorLabel = "<color=#40E0D0>" + item.Author + "</color>";
                    Rect authorLabelR = new Rect(prefixR.xMax + 2f, card.y + 5f, card.width - (prefixR.width + 230f), 22f);
                    Widgets.Label(authorLabelR, authorLabel);
                }

                // Line 2: Description
                string desc = "<color=#C0C0C0>" + item.Description + "</color>";
                Widgets.Label(new Rect(card.x + 8f, card.y + 28f, card.width - 230f, 22f), desc);

                // Line 3: Mod active status
                string modStatus = item.IsTargetModActive
                    ? "<color=#50E050>• Целевой мод активен в игре (" + item.PackageId + ")</color>"
                    : "<color=#FFA040>• Мод не обнаружен в списке активных модов (" + item.PackageId + ")</color>";

                string cloudTransBadge = RUMLTranslationDetector.GetTranslationBadge(item.PackageId, 28);
                if (!string.IsNullOrEmpty(cloudTransBadge))
                {
                    modStatus += "  |" + cloudTransBadge;
                }
                Widgets.Label(new Rect(card.x + 8f, card.y + 51f, card.width - 230f, 22f), modStatus);

                string cloudTransTip = RUMLTranslationDetector.GetTranslationTooltip(item.PackageId);
                if (!string.IsNullOrEmpty(cloudTransTip))
                {
                    TooltipHandler.TipRegion(card, cloudTransTip);
                }

                // Action Buttons (Right)
                if (item.IsInstalled)
                {
                    Rect uninstR = new Rect(card.width - 210f, card.y + 22f, 95f, 34f);
                    Color prevCol = GUI.color;
                    GUI.color = RUMLUI.ColorDestructiveRed;
                    if (Widgets.ButtonText(uninstR, "Удалить"))
                    {
                        RUMLCloudManager.Uninstall(item, Content, Settings);
                    }
                    GUI.color = prevCol;

                    Rect updateR = new Rect(card.width - 110f, card.y + 22f, 105f, 34f);
                    Color prevUpCol = GUI.color;
                    if (item.HasUpdate)
                    {
                        GUI.color = RUMLUI.ColorAccentGold;
                    }
                    string upLabel = item.HasUpdate ? "Обновить!" : "Обновить";
                    if (Widgets.ButtonText(updateR, upLabel))
                    {
                        RUMLCloudManager.DownloadAndInstallAsync(item, Content, Settings);
                    }
                    GUI.color = prevUpCol;
                }
                else
                {
                    Rect dlR = new Rect(card.width - 210f, card.y + 22f, 205f, 34f);
                    Color prevC = GUI.color;
                    GUI.color = RUMLUI.ColorButtonGreen;
                    string dlLabel = Settings.manualApplyMode ? "Скачать" : "Скачать и применить";
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
    }
}
