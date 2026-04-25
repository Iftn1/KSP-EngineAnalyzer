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
        private float vesselMassAtSync = 0f;
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
        private bool showStageHistory = false;
        private bool onlyResearched = false;

        public enum SortMode { DeltaV, TWR, Isp, Value }
        public SortMode currentSortMode = SortMode.DeltaV;

        private bool isSmartMode = false;
        private bool isVacMode = true;
        private string targetDVInput = "3000";
        private float targetDV = 3000f;
        private string targetTWRInput = "1.2";
        private float targetTWR = 1.2f;

        private bool isEnglish = false;
        private string lockID = "EngineAnalyzerLock";
        private bool isCompactMode = false;

        public enum SizeFilter { All, Size0625, Size125, Size25, Size375 }
        public SizeFilter currentSizeFilter = SizeFilter.All;

        private List<float> lockedWetMasses = new List<float>();
        private List<float> lockedDryMasses = new List<float>();
        private List<float> lockedVolumes = new List<float>();

        private List<EnginePartGroup> allGroupsCache = new List<EnginePartGroup>();
        private List<EnginePartGroup> filteredGroups = new List<EnginePartGroup>();

        private string L(string zh, string en) => isEnglish ? en : zh;

        private void Awake() { LoadSettings(); }

        private void LoadSettings()
        {
            isEnglish = PlayerPrefs.GetInt("EA_isEnglish", 0) == 1;
            isCompactMode = PlayerPrefs.GetInt("EA_isCompactMode", 0) == 1;
            onlyResearched = PlayerPrefs.GetInt("EA_onlyResearched", 0) == 1;
            currentSortMode = (SortMode)PlayerPrefs.GetInt("EA_sortMode", (int)SortMode.DeltaV);
            currentSizeFilter = (SizeFilter)PlayerPrefs.GetInt("EA_sizeFilter", (int)SizeFilter.All);
            isVacMode = PlayerPrefs.GetInt("EA_isVacMode", 1) == 1;
            isSmartMode = PlayerPrefs.GetInt("EA_isSmartMode", 0) == 1;
            showRockets = PlayerPrefs.GetInt("EA_showRockets", 1) == 1;
            showJets = PlayerPrefs.GetInt("EA_showJets", 1) == 1;
            showSRB = PlayerPrefs.GetInt("EA_showSRB", 1) == 1;
            ispLimit = PlayerPrefs.GetFloat("EA_ispLimit", 4000f);
            twrFilterLimit = PlayerPrefs.GetFloat("EA_twrFilterLimit", 20.1f);
            twrMinLimit = PlayerPrefs.GetFloat("EA_twrMinLimit", 0f);
        }

        private void SaveSettings()
        {
            PlayerPrefs.SetInt("EA_isEnglish", isEnglish ? 1 : 0);
            PlayerPrefs.SetInt("EA_isCompactMode", isCompactMode ? 1 : 0);
            PlayerPrefs.SetInt("EA_onlyResearched", onlyResearched ? 1 : 0);
            PlayerPrefs.SetInt("EA_sortMode", (int)currentSortMode);
            PlayerPrefs.SetInt("EA_sizeFilter", (int)currentSizeFilter);
            PlayerPrefs.SetInt("EA_isVacMode", isVacMode ? 1 : 0);
            PlayerPrefs.SetInt("EA_isSmartMode", isSmartMode ? 1 : 0);
            PlayerPrefs.SetInt("EA_showRockets", showRockets ? 1 : 0);
            PlayerPrefs.SetInt("EA_showJets", showJets ? 1 : 0);
            PlayerPrefs.SetInt("EA_showSRB", showSRB ? 1 : 0);
            PlayerPrefs.SetFloat("EA_ispLimit", ispLimit);
            PlayerPrefs.SetFloat("EA_twrFilterLimit", twrFilterLimit);
            PlayerPrefs.SetFloat("EA_twrMinLimit", twrMinLimit);
            PlayerPrefs.Save();
        }

        private bool IsPartVisible(AvailablePart ap)
        {
            if (!ResearchAndDevelopment.PartModelPurchased(ap)) return false;
            if (ap.TechRequired != null)
            {
                try
                {
                    var techState = ResearchAndDevelopment.GetTechnologyState(ap.TechRequired);
                    if (techState != null)
                    {
                        var stateField = techState.GetType().GetField("state");
                        if (stateField != null)
                        {
                            var stateValue = stateField.GetValue(techState);
                            if (!stateValue.ToString().Equals("Available", StringComparison.OrdinalIgnoreCase)) return false;
                        }
                    }
                }
                catch { }
            }
            return true;
        }

        private float GetPartSize(AvailablePart ap)
        {
            if (ap.partPrefab != null)
            {
                var field = ap.partPrefab.GetType().GetField("size", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (field != null) return (float)field.GetValue(ap.partPrefab);
            }
            if (ap.partConfig != null && ap.partConfig.HasValue("size"))
            {
                if (float.TryParse(ap.partConfig.GetValue("size"), out float s)) return s;
            }
            return 1.25f;
        }

        private string GetEngineSize(AvailablePart ap)
        {
            float size = GetPartSize(ap);
            if (Math.Abs(size - 0.625f) < 0.1f) return "0.625m";
            if (Math.Abs(size - 1.25f) < 0.1f) return "1.25m";
            if (Math.Abs(size - 2.5f) < 0.1f) return "2.5m";
            if (Math.Abs(size - 3.75f) < 0.1f) return "3.75m";
            return size.ToString("F3") + "m";
        }

        private SizeFilter GetEngineSizeFilter(AvailablePart ap)
        {
            float size = GetPartSize(ap);
            if (Math.Abs(size - 0.625f) < 0.1f) return SizeFilter.Size0625;
            if (Math.Abs(size - 1.25f) < 0.1f) return SizeFilter.Size125;
            if (Math.Abs(size - 2.5f) < 0.1f) return SizeFilter.Size25;
            if (Math.Abs(size - 3.75f) < 0.1f) return SizeFilter.Size375;
            return SizeFilter.All;
        }

        private void Update()
        {
            if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.E))
            {
                isVisible = !isVisible;
                if (isVisible) { CheckShipReset(); SyncCurrentStage(); RefreshData(); }
                else { InputLockManager.RemoveControlLock(lockID); }
            }
        }

        private void OnDestroy()
        {
            InputLockManager.RemoveControlLock(lockID);
            SaveSettings();
        }

        private void SpawnAndConfigure(AvailablePart ap, string configName)
        {
            EditorLogic.fetch.SpawnPart(ap);
            Part p = EditorLogic.SelectedPart;
            if (p == null || configName == "标准型号" || configName == "Standard") return;

            foreach (PartModule pm in p.Modules)
            {
                if (pm.moduleName == "ModuleEngineConfigs" || pm.moduleName == "ModuleEnginesRF" || pm.moduleName == "ModuleRealEngine")
                {
                    var method = pm.GetType().GetMethod("SetConfiguration", new Type[] { typeof(string) });
                    if (method != null) { method.Invoke(pm, new object[] { configName }); }
                    else
                    {
                        var field = pm.Fields.Cast<BaseField>().FirstOrDefault(f => f.name == "configuration");
                        if (field != null) field.SetValue(configName, pm);
                    }
                    break;
                }
            }
        }

        private void CheckShipReset()
        {
            int currentParts = EditorLogic.fetch?.ship?.Parts?.Count ?? 0;
            if (currentParts == 0 && lastPartCount > 0)
            {
                lockedPayloadMass = 0f;
                vesselMassAtSync = 0f;
                lockedWetMasses.Clear();
                lockedDryMasses.Clear();
                lockedVolumes.Clear();
            }
            lastPartCount = currentParts;
        }

        private float GetShipDryMass() => EditorLogic.fetch?.ship?.Parts.Sum(p => p.mass) ?? 0f;
        private float GetShipTotalMass() => EditorLogic.fetch?.ship?.Parts.Sum(p => p.mass + p.GetResourceMass()) ?? 0f;

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
            float totalVol = GetShipTotalVolumeKL();
            vesselMassAtSync = totalWet;
            float prevWet = lockedWetMasses.Count > 0 ? lockedWetMasses.Max() : 0f;
            float prevVol = lockedVolumes.Count > 0 ? lockedVolumes.Max() : 0f;
            targetVolumeKL = Math.Max(totalVol - prevVol, 0f);
            targetVolumeInput = targetVolumeKL.ToString("F2");
            lockedPayloadMass = prevWet;
        }

        private void RefreshData()
        {
            allGroupsCache.Clear();
            foreach (AvailablePart ap in PartLoader.LoadedPartsList)
            {
                var engineMod = ap.partPrefab?.FindModuleImplementing<ModuleEngines>();
                if (engineMod == null) continue;

                if (onlyResearched && !ResearchAndDevelopment.PartModelPurchased(ap)) continue;

                bool isJet = engineMod.propellants.Any(p => p.name == "IntakeAir");
                bool isSRB = engineMod.throttleLocked || engineMod.propellants.Any(p => p.name == "SolidFuel");

                float baseMixDensity = 0.005f;
                List<string> baseFuels = new List<string>();
                float totalRatio = engineMod.propellants.Sum(p => p.ratio);
                if (totalRatio > 0)
                {
                    baseMixDensity = 0f;
                    foreach (var p in engineMod.propellants)
                    {
                        var def = PartResourceLibrary.Instance.GetDefinition(p.name);
                        baseMixDensity += (p.ratio / totalRatio) * (def?.density ?? 0.005f);
                        baseFuels.Add(isEnglish ? (def?.name ?? p.name) : (def?.displayName ?? p.name));
                    }
                }

                float baseIspVac = engineMod.atmosphereCurve.Evaluate(0f);
                float baseIspASL = engineMod.atmosphereCurve.Evaluate(1f);
                float baseMaxThrust = engineMod.maxThrust;

                string baseBurnTime = L("无限", "Inf"), baseIgnitions = L("无限次点火", "Inf Ignitions"), baseUllage = L("<color=lime>免沉底</color>", "<color=lime>No Ullage</color>");
                foreach (PartModule pm in ap.partPrefab.Modules)
                {
                    if (pm.moduleName.Contains("ModuleEngineConfigs") || pm.moduleName.Contains("ModuleRealEngine") || pm.moduleName.Contains("ModuleEnginesRF"))
                    {
                        var fields = pm.Fields.Cast<BaseField>().ToList();
                        var fIgn = fields.FirstOrDefault(f => f.name.ToLower().Contains("ignition"));
                        var fUll = fields.FirstOrDefault(f => f.name.ToLower().Contains("ullage"));
                        var fBrn = fields.FirstOrDefault(f => f.name.ToLower().Contains("burn"));
                        if (fIgn != null && int.TryParse(fIgn.GetValue(pm).ToString(), out int c) && c < 100) baseIgnitions = c + L(" 次点火", " Ignitions");
                        if (fUll != null && fUll.GetValue(pm).ToString().ToLower() == "true") baseUllage = L("<color=red>需沉底</color>", "<color=red>Need Ullage</color>");
                        if (fBrn != null && float.TryParse(fBrn.GetValue(pm).ToString(), out float bt) && bt > 0) baseBurnTime = bt + "s";
                    }
                }

                EnginePartGroup group = new EnginePartGroup 
                { 
                    part = ap, 
                    partTitle = Localizer.Format(ap.title), 
                    isJet = isJet, 
                    isSRB = isSRB,
                    isHidden = !IsPartVisible(ap),
                    engineSize = GetEngineSize(ap),
                    sizeFilter = GetEngineSizeFilter(ap)
                };
                ConfigNode mecNode = ap.partConfig?.GetNodes("MODULE").FirstOrDefault(n => n.GetValue("name").Contains("EngineConfigs") || n.GetValue("name").Contains("RealEngine") || n.GetValue("name").Contains("EnginesRF"));

                if (mecNode != null && mecNode.HasNode("CONFIG"))
                {
                    foreach (ConfigNode cfg in mecNode.GetNodes("CONFIG"))
                    {
                        string cfgName = cfg.GetValue("name") ?? L("未知型号", "Unknown");

                        bool isHP = false;
                        if (cfg.HasValue("type") && (cfg.GetValue("type").Contains("TankService") || cfg.GetValue("type").Contains("Pressure-fed"))) isHP = true;
                        if (cfg.HasValue("pressureFed") && cfg.GetValue("pressureFed").ToLower() == "true") isHP = true;
                        if (cfgName.IndexOf("Pressure-fed", StringComparison.OrdinalIgnoreCase) >= 0 || cfgName.IndexOf("Pressure Fed", StringComparison.OrdinalIgnoreCase) >= 0) isHP = true;

                        float cfgT = baseMaxThrust; if (cfg.HasValue("maxThrust")) float.TryParse(cfg.GetValue("maxThrust"), out cfgT);
                        
                        var atmEngine = ap.partPrefab.FindModuleImplementing<ModuleEngines>();
                        float cfgIV = atmEngine != null ? atmEngine.atmosphereCurve.Evaluate(0f) : baseIspVac;
                        float cfgIA = atmEngine != null ? atmEngine.atmosphereCurve.Evaluate(1f) : baseIspASL;
                        
                        ConfigNode curve = cfg.GetNode("atmosphereCurve");
                        if (curve != null)
                        {
                            foreach (string v in curve.GetValues("key"))
                            {
                                string[] s = v.Split(new char[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                                if (s.Length >= 2 && float.TryParse(s[0], out float k) && float.TryParse(s[1], out float val))
                                {
                                    if (k < 0.1f) cfgIV = val; if (Math.Abs(k - 1f) < 0.1f) cfgIA = val;
                                }
                            }
                        }
                        group.configs.Add(CalculateConfig(cfgName, cfgT, cfgIV, cfgIA, baseMixDensity, baseFuels, baseUllage, baseIgnitions, baseBurnTime, ap, engineCount, isHP));
                    }
                }
                else group.configs.Add(CalculateConfig(L("标准型号", "Standard"), baseMaxThrust, baseIspVac, baseIspASL, baseMixDensity, baseFuels, baseUllage, baseIgnitions, baseBurnTime, ap, engineCount, false));
                allGroupsCache.Add(group);
            }
            ApplyFilters();
        }

        private EngineConfig CalculateConfig(string name, float T, float IV, float IA, float dens, List<string> f, string ull, string ign, string bt, AvailablePart ap, int cnt, bool isHP)
        {
            const float g0 = 9.80665f;
            
            float isp, thrust;
            if (isVacMode)
            {
                isp = IV;
                thrust = T * cnt;
            }
            else
            {
                isp = IA;
                var mod = ap.partPrefab.FindModuleImplementing<ModuleEngines>();
                if (mod != null)
                {
                    thrust = T * mod.thrustCurve.Evaluate(1f) * cnt;
                }
                else
                {
                    thrust = T * (IA / IV) * cnt;
                }
            }
            
            float dry = vesselMassAtSync + (ap.partPrefab.mass * cnt);
            float dV = 0, twr = 0, vol = 0, wet = 0;
            bool meet = true;

            var engineModule = ap.partPrefab.FindModuleImplementing<ModuleEngines>();
            if (f.Contains("SolidFuel") || (engineModule != null && engineModule.throttleLocked))
            {
                float fMass = ap.partPrefab.Resources.Where(r => r.resourceName == "SolidFuel").Sum(r => (float)(r.maxAmount * r.info.density));
                wet = dry + (fMass * cnt); 
                vol = (fMass * cnt / dens) / 1000f;
                if (dry > 0) dV = isp * g0 * Mathf.Log(wet / dry);
                twr = thrust / (wet * g0);
            }
            else if (isSmartMode)
            {
                float R = Mathf.Exp(targetDV / (isp * g0)); 
                wet = dry * R; 
                vol = ((wet - dry) / dens) / 1000f;
                twr = thrust / (wet * g0); 
                if (twr < targetTWR) meet = false; 
                dV = targetDV;
            }
            else
            {
                float fMass = (targetVolumeKL * 1000f) * dens; 
                wet = dry + fMass; 
                vol = targetVolumeKL;
                if (dry > 0) dV = isp * g0 * Mathf.Log(wet / dry);
                twr = thrust / (wet * g0);
            }

            return new EngineConfig { configName = name, deltaV = dV, twr = twr, ispVac = IV, ispASL = IA, price = ap.cost * cnt, displayWetMass = wet, displayVolume = vol, fuelInfo = string.Join("/", f), ullageInfo = ull, ignitionsDisplay = ign, burnTimeDisplay = bt, meetsSmartCriteria = meet, needsHighPressure = isHP };
        }

        private void ApplyFilters()
        {
            isSciFiMode = ispLimit >= SCIFI_THRESHOLD;
            filteredGroups.Clear();
            foreach (var g in allGroupsCache)
            {
                if (!((showRockets && !g.isJet && !g.isSRB) || (showJets && g.isJet) || (showSRB && g.isSRB))) continue;
                if (!string.IsNullOrEmpty(searchFilter) && g.partTitle.IndexOf(searchFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (currentSizeFilter != SizeFilter.All && g.sizeFilter != currentSizeFilter) continue;

                var fg = new EnginePartGroup { part = g.part, partTitle = g.partTitle, isJet = g.isJet, isSRB = g.isSRB, configs = new List<EngineConfig>(), isHidden = g.isHidden, engineSize = g.engineSize };
                foreach (var c in g.configs)
                {
                    if ((isSciFiMode || (isVacMode ? c.ispVac : c.ispASL) <= ispLimit) && (twrFilterLimit >= 20.1f || c.twr <= twrFilterLimit) && c.twr >= twrMinLimit && (!isSmartMode || c.meetsSmartCriteria))
                        fg.configs.Add(c);
                }
                if (fg.configs.Count > 0) filteredGroups.Add(fg);
            }
            filteredGroups = filteredGroups.OrderByDescending(GetGroupSortKey).Take(40).ToList();
        }

        private float GetGroupSortKey(EnginePartGroup g)
        {
            switch (currentSortMode)
            {
                case SortMode.DeltaV:
                    return g.MaxDeltaV;
                case SortMode.TWR:
                    return g.configs.Count > 0 ? g.configs.Max(c => c.twr) : 0;
                case SortMode.Isp:
                    return g.configs.Count > 0 ? (isVacMode ? g.configs.Max(c => c.ispVac) : g.configs.Max(c => c.ispASL)) : 0;
                case SortMode.Value:
                    return g.MaxDVPerCost;
                default:
                    return g.MaxDeltaV;
            }
        }

        private void OnGUI()
        {
            if (!isVisible) return;

            Vector2 mousePos = Event.current.mousePosition;
            if (windowRect.Contains(mousePos)) { InputLockManager.SetControlLock(ControlTypes.EDITOR_LOCK, lockID); }
            else { InputLockManager.RemoveControlLock(lockID); }

            GUI.skin = HighLogic.Skin;
            windowRect.width = isCompactMode ? 450 : 850;
            windowRect = GUILayout.Window(888, windowRect, DrawWindow, L("引擎全效分析器 v1.0.0", "Engine Analyzer v1.0.0"));
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(isVacMode ? L("🌌 真空模式", "🌌 Vacuum") : L("🌍 海平面模式", "🌍 Sea Level"))) { isVacMode = !isVacMode; RefreshData(); }
            if (GUILayout.Button(isSmartMode ? L("🟢 逆向规划", "🟢 Reverse") : L("⚪ 常规分析", "⚪ Normal"))) { isSmartMode = !isSmartMode; RefreshData(); }
            if (GUILayout.Button(isCompactMode ? L("📦 紧凑模式", "📦 Compact") : L("📋 标准模式", "📋 Normal"))) { isCompactMode = !isCompactMode; }
            GUILayout.EndHorizontal();

            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label($"{L("同步全重", "Sync Mass")}: <color=yellow>{vesselMassAtSync:F2}t</color>", GUILayout.Width(130));
            if (GUILayout.Button(L("<color=lime>完成并锁定当前级</color>", "<color=lime>Lock Current Stage</color>")))
            {
                lockedWetMasses.Add(GetShipTotalMass());
                lockedDryMasses.Add(GetShipDryMass());
                lockedVolumes.Add(GetShipTotalVolumeKL());
                SyncCurrentStage();
                RefreshData();
            }
            if (GUILayout.Button(L("<color=orange>清空重置</color>", "<color=orange>Reset All</color>"))) { lockedWetMasses.Clear(); lockedDryMasses.Clear(); lockedVolumes.Clear(); vesselMassAtSync = 0; SyncCurrentStage(); RefreshData(); }
            GUILayout.EndHorizontal();

            if (lockedWetMasses.Count > 0)
            {
                showStageHistory = GUILayout.Toggle(showStageHistory, $" {L("查看历史分级", "History")} ({lockedWetMasses.Count})");
                if (showStageHistory)
                {
                    for (int i = 0; i < lockedWetMasses.Count; i++)
                    {
                        GUILayout.Label($"  S{i + 1}: {L("湿重", "Wet")} {lockedWetMasses[i]:F2}t | {L("容积", "Vol")} {lockedVolumes[i]:F1}kL", GUI.skin.GetStyle("CaptionLabel"));
                    }
                }
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(L("同步VAB数据", "Sync VAB Data"))) { SyncCurrentStage(); RefreshData(); }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(L("容积(kL):", "Vol(kL):"), GUILayout.Width(60));
            targetVolumeInput = GUILayout.TextField(targetVolumeInput, GUILayout.Width(50));
            if (float.TryParse(targetVolumeInput, out float v) && Math.Abs(v - targetVolumeKL) > 0.01f) { targetVolumeKL = v; RefreshData(); }
            GUILayout.Space(20);
            GUILayout.Label($"{L("集群", "Cluster")}: {engineCount}", GUILayout.Width(50));
            int nc = (int)GUILayout.HorizontalSlider(engineCount, 1, 12); if (nc != engineCount) { engineCount = nc; RefreshData(); }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            bool r = GUILayout.Toggle(showRockets, L(" 火箭", " Rocket"), GUILayout.Width(60));
            GUILayout.Space(10);
            bool j = GUILayout.Toggle(showJets, L(" 喷气", " Jet"), GUILayout.Width(60));
            GUILayout.Space(10);
            bool s = GUILayout.Toggle(showSRB, L(" 固推", " SRB"), GUILayout.Width(60));
            GUILayout.Space(10);
            bool or = GUILayout.Toggle(onlyResearched, L(" 只显示已研究", " Researched Only"), GUILayout.Width(120));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("ZH", GUILayout.Width(35))) { isEnglish = false; RefreshData(); }
            if (GUILayout.Button("EN", GUILayout.Width(35))) { isEnglish = true; RefreshData(); }
            GUILayout.EndHorizontal();

            if (!isCompactMode)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(L("尺寸:", "Size:"), GUILayout.Width(40));
                if (GUILayout.Button(currentSizeFilter == SizeFilter.All ? L("<color=lime>全部</color>", "<color=lime>All</color>") : L("全部", "All"), GUILayout.Width(50))) { currentSizeFilter = SizeFilter.All; ApplyFilters(); }
                if (GUILayout.Button(currentSizeFilter == SizeFilter.Size0625 ? L("<color=lime>0.625m</color>", "<color=lime>0.625m</color>") : L("0.625m", "0.625m"), GUILayout.Width(60))) { currentSizeFilter = SizeFilter.Size0625; ApplyFilters(); }
                if (GUILayout.Button(currentSizeFilter == SizeFilter.Size125 ? L("<color=lime>1.25m</color>", "<color=lime>1.25m</color>") : L("1.25m", "1.25m"), GUILayout.Width(60))) { currentSizeFilter = SizeFilter.Size125; ApplyFilters(); }
                if (GUILayout.Button(currentSizeFilter == SizeFilter.Size25 ? L("<color=lime>2.5m</color>", "<color=lime>2.5m</color>") : L("2.5m", "2.5m"), GUILayout.Width(55))) { currentSizeFilter = SizeFilter.Size25; ApplyFilters(); }
                if (GUILayout.Button(currentSizeFilter == SizeFilter.Size375 ? L("<color=lime>3.75m</color>", "<color=lime>3.75m</color>") : L("3.75m", "3.75m"), GUILayout.Width(60))) { currentSizeFilter = SizeFilter.Size375; ApplyFilters(); }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label(L("排序:", "Sort:"), GUILayout.Width(50));
            if (GUILayout.Button(currentSortMode == SortMode.DeltaV ? L("<color=lime>Δv</color>", "<color=lime>Δv</color>") : L("Δv", "Δv"), GUILayout.Width(50))) { currentSortMode = SortMode.DeltaV; ApplyFilters(); }
            if (GUILayout.Button(currentSortMode == SortMode.TWR ? L("<color=lime>TWR</color>", "<color=lime>TWR</color>") : L("TWR", "TWR"), GUILayout.Width(50))) { currentSortMode = SortMode.TWR; ApplyFilters(); }
            if (GUILayout.Button(currentSortMode == SortMode.Isp ? L("<color=lime>Isp</color>", "<color=lime>Isp</color>") : L("Isp", "Isp"), GUILayout.Width(50))) { currentSortMode = SortMode.Isp; ApplyFilters(); }
            if (GUILayout.Button(currentSortMode == SortMode.Value ? L("<color=lime>性价比</color>", "<color=lime>Value</color>") : L("性价比", "Value"), GUILayout.Width(60))) { currentSortMode = SortMode.Value; ApplyFilters(); }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (r != showRockets || j != showJets || s != showSRB || or != onlyResearched) { showRockets = r; showJets = j; showSRB = s; onlyResearched = or; RefreshData(); }

            GUILayout.BeginHorizontal();
            string ispDisplay = ispLimit >= SCIFI_THRESHOLD ? L("<color=magenta>科幻模式</color>", "<color=magenta>Sci-Fi</color>") : $"{ispLimit:F0}s";
            GUILayout.Label($"{L("比冲限制", "Isp Limit")}: {ispDisplay}", GUILayout.Width(110));
            float nIsp = GUILayout.HorizontalSlider(ispLimit, 100f, 20001f);
            if (Math.Abs(nIsp - ispLimit) > 1f) { ispLimit = nIsp; ApplyFilters(); }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            string twrVal = twrFilterLimit >= 20.1f ? L("无上限", "None") : twrFilterLimit.ToString("F1");
            GUILayout.Label($"{L("TWR上限", "Max TWR")}: {twrVal}", GUILayout.Width(110));
            float nt = GUILayout.HorizontalSlider(twrFilterLimit, 0.1f, 20.1f);
            if (Math.Abs(nt - twrFilterLimit) > 0.01f) { twrFilterLimit = nt; ApplyFilters(); }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"{L("TWR下限", "Min TWR")}: {twrMinLimit:F1}", GUILayout.Width(110));
            float ntm = GUILayout.HorizontalSlider(twrMinLimit, 0.0f, 10.0f);
            if (Math.Abs(ntm - twrMinLimit) > 0.01f) { twrMinLimit = ntm; ApplyFilters(); }
            GUILayout.EndHorizontal();

            if (isSmartMode)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(L("目标 Δv:", "Target Δv:"), GUILayout.Width(60));
                targetDVInput = GUILayout.TextField(targetDVInput, GUILayout.Width(50));
                if (float.TryParse(targetDVInput, out float d)) targetDV = d;
                GUILayout.Label(L("最低TWR:", "Min TWR:"), GUILayout.Width(60));
                targetTWRInput = GUILayout.TextField(targetTWRInput, GUILayout.Width(40));
                if (float.TryParse(targetTWRInput, out float t)) targetTWR = t;
                GUILayout.EndHorizontal();
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label(L("搜索:", "Search:"), GUILayout.Width(50));
            string newFilter = GUILayout.TextField(searchFilter, isCompactMode ? GUILayout.Width(150) : GUILayout.Width(200));
            if (newFilter != searchFilter) { searchFilter = newFilter; ApplyFilters(); }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();

            scrollPosition = GUILayout.BeginScrollView(scrollPosition);
            foreach (var group in filteredGroups)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                string typeLabel = group.isJet ? L("<color=orange>[喷气]</color> ", "<color=orange>[Jet]</color> ") : (group.isSRB ? L("<color=red>[固推]</color> ", "<color=red>[SRB]</color> ") : L("<color=cyan>[火箭]</color> ", "<color=cyan>[Rocket]</color> "));
                string hiddenLabel = group.isHidden ? L(" <color=red>[隐藏]</color>", " <color=red>[Hidden]</color>") : "";
                string sizeLabel = $" ({group.engineSize})";
                GUILayout.Label($"<size=15>{typeLabel}<b>{group.partTitle}</b>{hiddenLabel}{sizeLabel}{(engineCount > 1 ? $" x{engineCount}" : "")}</size>");

                for (int i = 0; i < group.configs.Count; i++)
                {
                    var cfg = group.configs[i];
                    GUILayout.BeginVertical(GUI.skin.box);
                    GUILayout.BeginHorizontal();

                    string hpDisplay = cfg.needsHighPressure ? L("<color=red>[需高压罐]</color> ", "<color=red>[High Pressure]</color> ") : "";

                    if (isCompactMode)
                    {
                        GUILayout.Label($"{hpDisplay}<size=12><color=#E0E0E0>{cfg.configName}</color></size>", GUILayout.Width(180));
                        GUILayout.FlexibleSpace();
                        GUILayout.Label($"Δv:{cfg.deltaV:F0} TWR:{cfg.twr:F2}");
                        if (GUILayout.Button(L("选", "Sel"), GUILayout.Width(35))) SpawnAndConfigure(group.part, cfg.configName);
                    }
                    else
                    {
                        GUILayout.Label($"{hpDisplay}<size=13><color=#E0E0E0>▶ {L("型号", "Config")}: {cfg.configName}</color></size>");
                        if (GUILayout.Button(L("选取", "Select"), GUILayout.Width(60))) SpawnAndConfigure(group.part, cfg.configName);
                    }
                    GUILayout.EndHorizontal();

                    GUILayout.Label($"<size=10><color=#999999>{L("燃料", "Fuel")}: {cfg.fuelInfo} | {cfg.ullageInfo} | <color=orange>{cfg.ignitionsDisplay}</color> | {L("燃时", "Burn")}: {cfg.burnTimeDisplay}</color></size>");

                    if (!isCompactMode)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label($"Δv: <color=lime>{cfg.deltaV:F0}</color>", GUILayout.Width(80));
                        GUILayout.Label($"TWR: {cfg.twr:F2}", GUILayout.Width(70));
                        GUILayout.Label($"Isp: {cfg.ispVac:F0}", GUILayout.Width(70));
                        GUILayout.Label(isSmartMode ? $"{cfg.displayVolume:F1}kL" : $"{cfg.displayWetMass:F1}t");
                        GUILayout.Label($"$: <color=yellow>{cfg.price:F0}</color>");
                        GUILayout.EndHorizontal();
                    }
                    GUILayout.EndVertical();
                    if (i < group.configs.Count - 1) GUILayout.Space(isCompactMode ? 3 : 5);
                }
                GUILayout.EndVertical();
                GUILayout.Space(isCompactMode ? 5 : 10);
            }
            GUILayout.EndScrollView();
            if (GUILayout.Button(L("关闭窗口", "Close Window"), GUILayout.Height(25))) isVisible = false;
            GUILayout.EndVertical();
            GUI.DragWindow();
        }
    }

    public class EnginePartGroup
    {
        public AvailablePart part; public string partTitle; public bool isJet, isSRB;
        public List<EngineConfig> configs = new List<EngineConfig>();
        public float MinPrice => configs.Count > 0 ? configs.Min(c => c.price) : 0;
        public float MinVolume => configs.Count > 0 ? configs.Min(c => c.displayVolume) : 0;
        public float MaxDeltaV => configs.Count > 0 ? configs.Max(c => c.deltaV) : 0;
        public float MaxDVPerCost => configs.Count > 0 ? configs.Max(c => c.deltaV / Math.Max(1f, c.price)) : 0;
        public bool isHidden;
        public string engineSize;
        public SizeFilter sizeFilter;
    }
    public class EngineConfig
    {
        public string configName, burnTimeDisplay, fuelInfo, ullageInfo, ignitionsDisplay;
        public float deltaV, twr, ispVac, ispASL, price, displayWetMass, displayVolume;
        public bool meetsSmartCriteria, needsHighPressure;
    }
}
