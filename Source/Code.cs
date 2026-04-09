using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using KSP.Localization;

namespace KSP_EngineAnalyzer
{
    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public class EngineAnalyzerWindow : MonoBehaviour
    {
        private Rect windowRect = new Rect(100, 100, 850, 950);
        private Vector2 scrollPosition = Vector2.zero;
        private bool isVisible = false;

        private float lockedPayloadMass = 0f;
        private float currentStageDryMass = 1.0f;
        private float targetVolumeKL = 4.5f;
        private string targetVolumeInput = "4.50";
        private int engineCount = 1;
        private int lastPartCount = 0;

        private float ispLimit = 4000f;
        private float twrFilterLimit = 20.1f;
        private float twrMinLimit = 0.0f;
        private const float SCIFI_THRESHOLD = 20000f;
        private bool isSciFiMode = false;
        private string searchFilter = "";

        private bool showRockets = true;
        private bool showJets = true;
        private bool showSRB = true;
        private bool sortByValue = false;

        private bool isSmartMode = false;
        private bool isVacMode = true;
        private string targetDVInput = "3000";
        private float targetDV = 3000f;
        private string targetTWRInput = "1.2";
        private float targetTWR = 1.2f;

        private List<float> lockedWetMasses = new List<float>();
        private List<float> lockedDryMasses = new List<float>();
        private List<float> lockedVolumes = new List<float>();

        private List<EngineEntry> allEnginesCache = new List<EngineEntry>();
        private List<EngineEntry> filteredResults = new List<EngineEntry>();

        private void Update()
        {
            if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.E))
            {
                isVisible = !isVisible;
                if (isVisible) { CheckShipReset(); SyncCurrentStage(); RefreshData(); }
            }
        }

        private void CheckShipReset()
        {
            int currentParts = EditorLogic.fetch?.ship?.Parts?.Count ?? 0;
            if (currentParts == 0 && lastPartCount > 0)
            {
                lockedPayloadMass = 0f;
                lockedWetMasses.Clear();
                lockedDryMasses.Clear();
                lockedVolumes.Clear();
            }
            lastPartCount = currentParts;
        }

        private float GetShipDryMass()
        {
            if (EditorLogic.fetch?.ship == null) return 0f;
            float total = 0f;
            foreach (Part p in EditorLogic.fetch.ship.Parts) total += p.mass;
            return total;
        }

        private float GetShipTotalMass()
        {
            if (EditorLogic.fetch?.ship == null) return 0f;
            float total = 0f;
            foreach (Part p in EditorLogic.fetch.ship.Parts)
            {
                total += p.mass + p.GetResourceMass();
            }
            return total;
        }

        private float GetShipTotalVolumeKL()
        {
            if (EditorLogic.fetch?.ship == null) return 0f;
            float totalLiters = 0f;
            foreach (Part p in EditorLogic.fetch.ship.Parts)
            {
                foreach (PartModule pm in p.Modules)
                {
                    if (pm.moduleName == "ModuleFuelTanks" || pm.moduleName == "ModuleTankService")
                    {
                        var field = pm.Fields.Cast<BaseField>().FirstOrDefault(f => f.name.ToLower().Contains("volume"));
                        if (field != null && float.TryParse(field.GetValue(pm).ToString(), out float v))
                            totalLiters += v;
                    }
                }
            }
            return totalLiters / 1000f;
        }

        private void SyncCurrentStage()
        {
            float totalWet = GetShipTotalMass();
            float totalDry = GetShipDryMass();
            float totalVol = GetShipTotalVolumeKL();

            float prevWet = lockedWetMasses.Count > 0 ? lockedWetMasses.Max() : 0f;
            float prevDry = lockedDryMasses.Count > 0 ? lockedDryMasses.Max() : 0f;
            float prevVol = lockedVolumes.Count > 0 ? lockedVolumes.Max() : 0f;

            currentStageDryMass = Math.Max(totalDry - prevDry, 0.001f);
            targetVolumeKL = Math.Max(totalVol - prevVol, 0f);
            targetVolumeInput = targetVolumeKL.ToString("F2");
            lockedPayloadMass = prevWet;
        }

        private void RefreshData()
        {
            allEnginesCache.Clear();
            foreach (AvailablePart ap in PartLoader.LoadedPartsList)
            {
                var engineMod = ap.partPrefab?.FindModuleImplementing<ModuleEngines>();
                if (engineMod == null) continue;

                bool isJet = engineMod.propellants.Any(p => p.name == "IntakeAir");
                bool isSRB = engineMod.throttleLocked || engineMod.propellants.Any(p => p.name == "SolidFuel");

                float mixDensity = 0.005f;
                List<string> fuels = new List<string>();
                float totalRatio = engineMod.propellants.Sum(p => p.ratio);
                if (totalRatio > 0)
                {
                    mixDensity = 0f;
                    foreach (var p in engineMod.propellants)
                    {
                        var def = PartResourceLibrary.Instance.GetDefinition(p.name);
                        mixDensity += (p.ratio / totalRatio) * (def?.density ?? 0.005f);
                        fuels.Add(def?.displayName ?? p.name);
                    }
                }

                float ispVac = engineMod.atmosphereCurve.Evaluate(0f);
                float ispASL = engineMod.atmosphereCurve.Evaluate(1f);
                float activeIsp = isVacMode ? ispVac : ispASL;
                float activeThrust = (isVacMode ? engineMod.maxThrust : engineMod.maxThrust * (ispASL / Math.Max(1f, ispVac))) * engineCount;
                float totalDryMass = lockedPayloadMass + currentStageDryMass + (ap.partPrefab.mass * engineCount);

                string burnTime = "无限", ignitions = "无限次点火", ullage = "<color=lime>免沉底</color>";
                foreach (PartModule pm in ap.partPrefab.Modules)
                {
                    string mn = pm.moduleName;
                    if (mn.Contains("ModuleEngineConfigs") || mn.Contains("ModuleRealEngine") || mn.Contains("ModuleEnginesRF"))
                    {
                        var fields = pm.Fields.Cast<BaseField>().ToList();
                        var fIgnition = fields.FirstOrDefault(f => f.name.ToLower().Contains("ignition"));
                        var fUllage = fields.FirstOrDefault(f => f.name.ToLower().Contains("ullage"));
                        var fBurn = fields.FirstOrDefault(f => f.name.ToLower().Contains("burn"));

                        if (fIgnition != null)
                        {
                            string igRaw = fIgnition.GetValue(pm).ToString();
                            if (int.TryParse(igRaw, out int igCount))
                            {
                                if (igCount > 0 && igCount < 100)
                                    ignitions = igCount + " 次点火";
                                else
                                    ignitions = "无限次点火";
                            }
                            else if (igRaw.ToLower().Contains("inf")) ignitions = "无限次点火";
                        }
                        if (fUllage != null && fUllage.GetValue(pm).ToString().ToLower() == "true") ullage = "<color=red>需沉底</color>";
                        if (fBurn != null && float.TryParse(fBurn.GetValue(pm).ToString(), out float bt) && bt > 0) burnTime = bt + "s";
                    }
                }

                float dV = 0f, twr = 0f, calcVol = 0f, calcWet = 0f;
                bool meetsSmart = true;

                if (isSRB)
                {
                    float srbFuelMass = ap.partPrefab.Resources.Where(r => r.resourceName == "SolidFuel").Sum(r => (float)(r.maxAmount * r.info.density));
                    calcWet = totalDryMass + (srbFuelMass * engineCount);
                    calcVol = (srbFuelMass * engineCount / mixDensity) / 1000f;
                    if (totalDryMass > 0) dV = activeIsp * 9.80665f * Mathf.Log(calcWet / totalDryMass);
                    twr = activeThrust / (calcWet * 9.80665f);
                }
                else if (isSmartMode)
                {
                    float R = Mathf.Exp(targetDV / (activeIsp * 9.80665f));
                    calcWet = totalDryMass * R;
                    calcVol = ((calcWet - totalDryMass) / mixDensity) / 1000f;
                    twr = activeThrust / (calcWet * 9.80665f);
                    if (twr < targetTWR) meetsSmart = false;
                    dV = targetDV;
                }
                else
                {
                    float fuelMass = (targetVolumeKL * 1000f) * mixDensity;
                    calcWet = totalDryMass + fuelMass;
                    calcVol = targetVolumeKL;
                    if (totalDryMass > 0) dV = activeIsp * 9.80665f * Mathf.Log(calcWet / totalDryMass);
                    twr = activeThrust / (calcWet * 9.80665f);
                }

                allEnginesCache.Add(new EngineEntry
                {
                    part = ap,
                    title = Localizer.Format(ap.title),
                    deltaV = dV,
                    twr = twr,
                    ispVac = ispVac,
                    ispASL = ispASL,
                    price = ap.cost * engineCount,
                    fuelInfo = string.Join("/", fuels),
                    burnTimeDisplay = burnTime,
                    ignitionsDisplay = ignitions,
                    ullageInfo = ullage,
                    displayWetMass = calcWet,
                    displayVolume = calcVol,
                    isJet = isJet,
                    isSRB = isSRB,
                    meetsSmartCriteria = meetsSmart,
                    modSource = ap.partUrl.Split('/')[0]
                });
            }
            ApplyFilters();
        }

        private void ApplyFilters()
        {
            isSciFiMode = ispLimit >= SCIFI_THRESHOLD;
            var query = allEnginesCache.Where(e => {
                bool typeMatch = (showRockets && !e.isJet && !e.isSRB) || (showJets && e.isJet) || (showSRB && e.isSRB);
                if (!typeMatch) return false;
                bool ispMatch = isSciFiMode || (isVacMode ? e.ispVac : e.ispASL) <= ispLimit;
                bool twrMatch = (twrFilterLimit >= 20.1f || e.twr <= twrFilterLimit) && e.twr >= twrMinLimit;
                bool smartMatch = !isSmartMode || e.meetsSmartCriteria;
                bool searchMatch = string.IsNullOrEmpty(searchFilter) || e.title.IndexOf(searchFilter, StringComparison.OrdinalIgnoreCase) >= 0;
                return ispMatch && twrMatch && smartMatch && searchMatch;
            });

            if (isSmartMode)
                filteredResults = (sortByValue ? query.OrderBy(e => e.price) : query.OrderBy(e => e.displayVolume)).Take(40).ToList();
            else
                filteredResults = (sortByValue ? query.OrderByDescending(e => (e.deltaV / Math.Max(1f, e.price))) : query.OrderByDescending(e => e.deltaV)).Take(40).ToList();
        }

        private void OnGUI()
        {
            if (!isVisible) return;
            GUI.skin = HighLogic.Skin;
            windowRect = GUILayout.Window(888, windowRect, DrawWindow, "引擎全效分析器 v0.9.6");
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(isVacMode ? "🌌 真空模式" : "🌍 海平面模式")) { isVacMode = !isVacMode; RefreshData(); }
            if (GUILayout.Button(isSmartMode ? "🟢 逆向规划" : "⚪ 常规分析")) { isSmartMode = !isSmartMode; RefreshData(); }
            GUILayout.EndHorizontal();

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"锁定载荷: <color=yellow>{lockedPayloadMass:F2}t</color>", GUILayout.Width(120));
            if (GUILayout.Button("<color=lime>完成并锁定当前级</color>"))
            {
                lockedWetMasses.Add(GetShipTotalMass());
                lockedDryMasses.Add(GetShipDryMass());
                lockedVolumes.Add(GetShipTotalVolumeKL());
                SyncCurrentStage();
                RefreshData();
            }
            if (GUILayout.Button("<color=orange>重置整个程序</color>"))
            {
                lockedWetMasses.Clear();
                lockedDryMasses.Clear();
                lockedVolumes.Clear();
                lockedPayloadMass = 0f;
                currentStageDryMass = 0.001f;
                targetVolumeKL = 0f;
                targetVolumeInput = "0.00";
                lastPartCount = 0;
                RefreshData();
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"当前级增重: {currentStageDryMass:F2}t", GUILayout.Width(120));
            if (GUILayout.Button("同步VAB数据")) { SyncCurrentStage(); RefreshData(); }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("储箱容积(kL):", GUILayout.Width(90));
            targetVolumeInput = GUILayout.TextField(targetVolumeInput, GUILayout.Width(50));
            if (float.TryParse(targetVolumeInput, out float vRes))
            {
                if (Math.Abs(vRes - targetVolumeKL) > 0.001f)
                {
                    targetVolumeKL = vRes;
                    RefreshData();
                }
            }
            GUILayout.Space(20);
            GUILayout.Label($"集群: {engineCount}", GUILayout.Width(50));
            int newCount = (int)GUILayout.HorizontalSlider(engineCount, 1, 12);
            if (newCount != engineCount) { engineCount = newCount; RefreshData(); }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            bool r = GUILayout.Toggle(showRockets, " 火箭", GUILayout.Width(60));
            GUILayout.Space(40);
            bool j = GUILayout.Toggle(showJets, " 喷气", GUILayout.Width(60));
            GUILayout.Space(40);
            bool s = GUILayout.Toggle(showSRB, " 固推", GUILayout.Width(60));
            GUILayout.Space(60);
            bool sbv = GUILayout.Toggle(sortByValue, " 性价比排序", GUILayout.Width(100));

            if (r != showRockets || j != showJets || s != showSRB || sbv != sortByValue)
            {
                showRockets = r; showJets = j; showSRB = s; sortByValue = sbv; ApplyFilters();
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            string ispDisplay = ispLimit >= SCIFI_THRESHOLD ? "<color=magenta>科幻模式</color>" : $"{ispLimit:F0}s";
            GUILayout.Label($"比冲限制: {ispDisplay}", GUILayout.Width(110));
            float nIsp = GUILayout.HorizontalSlider(ispLimit, 100f, 20001f);
            if (Math.Abs(nIsp - ispLimit) > 1f) { ispLimit = nIsp; ApplyFilters(); }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            string twrVal = twrFilterLimit >= 20.1f ? "无上限" : twrFilterLimit.ToString("F1");
            GUILayout.Label($"TWR上限: {twrVal}", GUILayout.Width(110));
            float nt = GUILayout.HorizontalSlider(twrFilterLimit, 0.1f, 20.1f);
            if (Math.Abs(nt - twrFilterLimit) > 0.01f) { twrFilterLimit = nt; ApplyFilters(); }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"TWR下限: {twrMinLimit:F1}", GUILayout.Width(110));
            float ntm = GUILayout.HorizontalSlider(twrMinLimit, 0.0f, 10.0f);
            if (Math.Abs(ntm - twrMinLimit) > 0.01f) { twrMinLimit = ntm; ApplyFilters(); }
            GUILayout.EndHorizontal();

            if (isSmartMode)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("目标 Δv:", GUILayout.Width(60));
                targetDVInput = GUILayout.TextField(targetDVInput, GUILayout.Width(50));
                if (float.TryParse(targetDVInput, out float d)) targetDV = d;
                GUILayout.Label("最低TWR:", GUILayout.Width(60));
                targetTWRInput = GUILayout.TextField(targetTWRInput, GUILayout.Width(40));
                if (float.TryParse(targetTWRInput, out float t)) targetTWR = t;
                GUILayout.EndHorizontal();
            }

            searchFilter = GUILayout.TextField(searchFilter, GUI.skin.FindStyle("TextField"));
            GUILayout.EndVertical();

            scrollPosition = GUILayout.BeginScrollView(scrollPosition);
            foreach (var e in filteredResults)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.BeginHorizontal();
                string typeLabel = e.isJet ? "<color=orange>[喷气]</color> " : (e.isSRB ? "<color=red>[固推]</color> " : "<color=cyan>[火箭]</color> ");
                GUILayout.Label($"{typeLabel}<b>{e.title}</b>" + (engineCount > 1 ? $" x{engineCount}" : ""));
                if (GUILayout.Button("选取", GUILayout.Width(45))) EditorLogic.fetch.SpawnPart(e.part);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label($"Δv: <color=lime>{e.deltaV:F0}</color>", GUILayout.Width(80));
                GUILayout.Label($"TWR: {e.twr:F2}", GUILayout.Width(70));
                GUILayout.Label($"Isp: {e.ispVac:F0}", GUILayout.Width(70));
                GUILayout.Label(isSmartMode ? $"容积: {e.displayVolume:F1}kL" : $"总重: {e.displayWetMass:F1}t");
                GUILayout.Label($"$: <color=yellow>{e.price:F0}</color>");
                GUILayout.EndHorizontal();

                string meta = $"燃料: {e.fuelInfo} | {e.ullageInfo} | <color=orange>{e.ignitionsDisplay}</color> | 燃烧: {e.burnTimeDisplay}";
                GUILayout.Label($"<size=10><color=#999999>{meta}</color></size>");
                GUILayout.EndVertical();
            }
            GUILayout.EndScrollView();

            if (GUILayout.Button("关闭窗口", GUILayout.Height(25))) isVisible = false;
            GUILayout.EndVertical();
            GUI.DragWindow();
        }
    }

    public class EngineEntry
    {
        public AvailablePart part;
        public string title, burnTimeDisplay, modSource, fuelInfo, ullageInfo, ignitionsDisplay;
        public float deltaV, twr, ispVac, ispASL, price, displayWetMass, displayVolume;
        public bool isJet, isSRB, meetsSmartCriteria;
    }
}