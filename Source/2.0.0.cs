using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using KSP.Localization;
using System.Reflection;

namespace KSP_EngineAnalyzer
{
    public enum SortMode { DeltaV, TWR, Isp, Value }
    public enum SizeFilter { All, Size0625, Size125, Size25, Size375 }
    public enum FuelType { All, LOXKerosene, LOXH2, LOXMethane, Hypergolic, SolidFuel, Monopropellant, Airbreathing, Xenon, Electric, Other }

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
        private bool filterDeprecated = true;
        private bool filterNonRO = true;

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

        public SizeFilter currentSizeFilter = SizeFilter.All;

        public FuelType currentFuelFilter = FuelType.All;

        private EnginePartGroup selectedEngine = null;
        private int selectedConfigIndex = 0;
        private bool showDetailPanel = false;
        private Rect detailWindowRect = new Rect(700, 150, 850, 950);

        // BetterSRB 药柱预设
        private List<BetterSRBGrain> _grainPresets = new List<BetterSRBGrain>();
        private int _selectedGrainIndex = 0;
        private Rect _cachedChartRect = Rect.zero;
        private Vector2 detailScrollPosition = Vector2.zero;

        private static Material _glMaterial = null;
        private static readonly string[] solidPropellants = new[] { "SolidFuel", "NGNC", "PBAN", "HTPB", "PSAM", "Polybutadiene" };

        private List<float> lockedWetMasses = new List<float>();
        private List<float> lockedDryMasses = new List<float>();
        private List<float> lockedVolumes = new List<float>();

        private List<EnginePartGroup> allGroupsCache = new List<EnginePartGroup>();
        private List<EnginePartGroup> filteredGroups = new List<EnginePartGroup>();

        private string L(string zh, string en) => isEnglish ? en : zh;

        private void Awake()
        {
            LoadSettings();
        }

        private void LoadSettings()
        {
            isEnglish = PlayerPrefs.GetInt("EA_isEnglish", 0) == 1;
            isCompactMode = PlayerPrefs.GetInt("EA_isCompactMode", 0) == 1;
            onlyResearched = PlayerPrefs.GetInt("EA_onlyResearched", 0) == 1;
            filterDeprecated = PlayerPrefs.GetInt("EA_filterDeprecated", 1) == 1;
            filterNonRO = PlayerPrefs.GetInt("EA_filterNonRO", 1) == 1;
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
            PlayerPrefs.SetInt("EA_filterDeprecated", filterDeprecated ? 1 : 0);
            PlayerPrefs.SetInt("EA_filterNonRO", filterNonRO ? 1 : 0);
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

        private float GetPropellantMaxAmount(Propellant prop)
        {
            try
            {
                var field = prop.GetType().GetField("maxAmount");
                if (field != null) return (float)field.GetValue(prop);
                
                var property = prop.GetType().GetProperty("maxAmount");
                if (property != null) return (float)property.GetValue(prop);
            }
            catch { }
            
            return 0f;
        }

        private FuelType DetectFuelType(AvailablePart ap)
        {
            var engineMod = FindEngineModule(ap);
            
            if (engineMod != null)
            {
                var propellantNames = engineMod.propellants.Select(p => p.name.ToLower()).ToList();
                
                if (propellantNames.Contains("solidfuel")) return FuelType.SolidFuel;
                if (propellantNames.Contains("intakeair")) return FuelType.Airbreathing;
                if (propellantNames.Contains("xenongas")) return FuelType.Xenon;
                if (propellantNames.Contains("electriccharge")) return FuelType.Electric;
                if (propellantNames.Contains("monopropellant")) return FuelType.Monopropellant;
                
                if (propellantNames.Contains("liquidfuel") && propellantNames.Contains("oxidizer"))
                    return FuelType.LOXKerosene;
                if (propellantNames.Contains("liquidhydrogen") && propellantNames.Contains("oxidizer"))
                    return FuelType.LOXH2;
                if (propellantNames.Contains("methane") && propellantNames.Contains("oxidizer"))
                    return FuelType.LOXMethane;
                
                bool hasFuel = propellantNames.Any(p => p.Contains("hydrazine") || p.Contains("aerozine") || 
                                                      p.Contains("mmh") || p.Contains("udmh") || p.Contains("dmh"));
                bool hasOxidizer = propellantNames.Any(p => p.Contains("n2o4") || propellantNames.Contains("oxidizer"));
                if (hasFuel && hasOxidizer) return FuelType.Hypergolic;
            }
            
            var cfgNode = ap.partConfig?.GetNodes("MODULE").FirstOrDefault(n => 
                (n.GetValue("name")?.Contains("EnginesRF") ?? false) || 
                (n.GetValue("name")?.Contains("RealEngine") ?? false));
            
            if (cfgNode != null)
            {
                var propellantNodes = cfgNode.GetNodes("PROPELLANT");
                if (propellantNodes.Length > 0)
                {
                    List<string> propellants = new List<string>();
                    foreach (var pNode in propellantNodes)
                    {
                        string name = pNode.GetValue("name");
                        if (!string.IsNullOrEmpty(name)) propellants.Add(name.ToLower());
                    }
                    
                    if (propellants.Contains("solidfuel")) return FuelType.SolidFuel;
                    if (propellants.Contains("monopropellant")) return FuelType.Monopropellant;
                    if (propellants.Contains("liquidhydrogen")) return FuelType.LOXH2;
                    if (propellants.Contains("methane")) return FuelType.LOXMethane;
                    if (propellants.Contains("liquidfuel")) return FuelType.LOXKerosene;
                    if (propellants.Any(p => p.Contains("hydrazine") || p.Contains("aerozine") || p.Contains("mmh")))
                        return FuelType.Hypergolic;
                }
            }
            
            return FuelType.Other;
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

        private AnimationCurve ParseThrustCurveFromConfigNode(ConfigNode curveNode)
        {
            if (curveNode == null) return null;

            var keyValues = curveNode.GetValues("key");
            if (keyValues == null || keyValues.Length == 0) return null;

            var keys = new List<Keyframe>();
            foreach (var kv in keyValues)
            {
                var parts = kv.Split(new char[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                if (!float.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float time)) continue;
                if (!float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float value)) continue;

                Keyframe kf;
                if (parts.Length >= 4 &&
                    float.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float inTan) &&
                    float.TryParse(parts[3], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float outTan))
                {
                    kf = new Keyframe(time, value, inTan, outTan);
                }
                else
                {
                    kf = new Keyframe(time, value);
                }
                keys.Add(kf);
            }

            if (keys.Count == 0) return null;

            var curve = new AnimationCurve(keys.ToArray());
            var fixedKeys = curve.keys;
            for (int i = 0; i < fixedKeys.Length; i++)
            {
                var orig = keys[i];
                fixedKeys[i].inTangent  = orig.inTangent;
                fixedKeys[i].outTangent = orig.outTangent;
                fixedKeys[i].tangentMode = 0;
            }
            for (int i = 0; i < fixedKeys.Length; i++)
                curve.MoveKey(i, fixedKeys[i]);
            return curve;
        }

        /// <summary>
        /// 尝试从 BetterSRB（或其他使用 GRAIN 节点的模组）读取运行时推力曲线。
        /// </summary>
        private AnimationCurve TryGetBetterSRBCurve(AvailablePart ap)
        {
            if (ap?.partPrefab == null) return null;

            string[] preferredNames = new[] { "thrustcurve", "thrust_curve", "grainthrustcurve", "srbcurve", "motorCurve", "thrustProfile" };

            AnimationCurve fallback = null;

            foreach (PartModule pm in ap.partPrefab.Modules)
            {
                string moduleName = pm.GetType().Name.ToLower();
                bool isSRBRelated = moduleName.Contains("srb") || moduleName.Contains("grain") ||
                                    moduleName.Contains("solid") || moduleName.Contains("better") ||
                                    moduleName.Contains("motor");
                if (!isSRBRelated) continue;

                var type = pm.GetType();
                var bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

                foreach (var field in type.GetFields(bindingFlags))
                {
                    if (field.FieldType != typeof(AnimationCurve)) continue;
                    try
                    {
                        var val = field.GetValue(pm) as AnimationCurve;
                        if (val == null || val.keys.Length < 2) continue;

                        string nameLower = field.Name.ToLower();
                        foreach (var preferred in preferredNames)
                        {
                            if (nameLower.Contains(preferred))
                                return val;
                        }
                        if (fallback == null) fallback = val;
                    }
                    catch { }
                }

                foreach (var prop in type.GetProperties(bindingFlags))
                {
                    if (prop.PropertyType != typeof(AnimationCurve)) continue;
                    try
                    {
                        var val = prop.GetValue(pm) as AnimationCurve;
                        if (val == null || val.keys.Length < 2) continue;

                        string nameLower = prop.Name.ToLower();
                        foreach (var preferred in preferredNames)
                        {
                            if (nameLower.Contains(preferred))
                                return val;
                        }
                        if (fallback == null) fallback = val;
                    }
                    catch { }
                }

                try
                {
                    ConfigNode node = new ConfigNode();
                    pm.Save(node);

                    foreach (string nodeName in new[] { "thrustCurve", "ThrustCurve", "grainThrustCurve", "motorCurve" })
                    {
                        var curveNode = node.GetNode(nodeName);
                        if (curveNode != null)
                        {
                            var parsed = ParseThrustCurveFromConfigNode(curveNode);
                            if (parsed != null && parsed.keys.Length >= 2)
                                return parsed;
                        }
                    }
                }
                catch { }
            }

            return fallback;
        }

        /// <summary>
        /// 尝试为特定型号配置读取 BetterSRB 的运行时推力曲线。
        /// </summary>
        private AnimationCurve TryGetBetterSRBCurveForConfig(AvailablePart ap, string configName)
        {
            if (ap?.partPrefab == null) return null;

            var bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            foreach (PartModule pm in ap.partPrefab.Modules)
            {
                string moduleName = pm.GetType().Name;
                if (!moduleName.Contains("EngineConfigs") && !moduleName.Contains("BetterSRB") &&
                    !moduleName.Contains("SRB") && !moduleName.Contains("Grain")) continue;

                try
                {
                    var type = pm.GetType();

                    foreach (var field in type.GetFields(bindingFlags))
                    {
                        if (!field.Name.ToLower().Contains("config")) continue;
                        var listVal = field.GetValue(pm);
                        if (listVal == null) continue;

                        var enumerable = listVal as System.Collections.IEnumerable;
                        if (enumerable == null) continue;

                        foreach (var item in enumerable)
                        {
                            if (item == null) continue;
                            var itemType = item.GetType();

                            string itemName = null;
                            var nameField = itemType.GetField("name", bindingFlags) ?? itemType.GetField("configName", bindingFlags);
                            var nameProp = itemType.GetProperty("name", bindingFlags) ?? itemType.GetProperty("configName", bindingFlags);
                            if (nameField != null) itemName = nameField.GetValue(item)?.ToString();
                            else if (nameProp != null) itemName = nameProp.GetValue(item)?.ToString();

                            if (itemName != null && !itemName.Equals(configName, StringComparison.OrdinalIgnoreCase)) continue;

                            foreach (var f in itemType.GetFields(bindingFlags))
                            {
                                if (f.FieldType != typeof(AnimationCurve)) continue;
                                try
                                {
                                    var ac = f.GetValue(item) as AnimationCurve;
                                    if (ac != null && ac.keys.Length >= 2) return ac;
                                }
                                catch { }
                            }

                            foreach (var f in itemType.GetFields(bindingFlags))
                            {
                                if (f.FieldType != typeof(ConfigNode)) continue;
                                try
                                {
                                    var cn = f.GetValue(item) as ConfigNode;
                                    if (cn == null) continue;
                                    foreach (string nodeName in new[] { "thrustCurve", "ThrustCurve", "grainThrustCurve" })
                                    {
                                        var curveNode = cn.GetNode(nodeName);
                                        if (curveNode != null)
                                        {
                                            var parsed = ParseThrustCurveFromConfigNode(curveNode);
                                            if (parsed != null && parsed.keys.Length >= 2) return parsed;
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                    }

                    try
                    {
                        ConfigNode savedNode = new ConfigNode();
                        pm.Save(savedNode);

                        foreach (ConfigNode cfgNode in savedNode.GetNodes("CONFIG"))
                        {
                            string nodeCfgName = cfgNode.GetValue("name");
                            if (nodeCfgName == null || !nodeCfgName.Equals(configName, StringComparison.OrdinalIgnoreCase)) continue;

                            foreach (string curveName in new[] { "thrustCurve", "ThrustCurve", "grainThrustCurve", "motorCurve" })
                            {
                                var curveNode = cfgNode.GetNode(curveName);
                                if (curveNode != null)
                                {
                                    var parsed = ParseThrustCurveFromConfigNode(curveNode);
                                    if (parsed != null && parsed.keys.Length >= 2) return parsed;
                                }
                            }
                        }
                    }
                    catch { }
                }
                catch { }
            }

            return null;
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

        private ModuleEngines FindEngineModule(AvailablePart ap)
        {
            if (ap?.partPrefab == null) return null;
            var mod = ap.partPrefab.FindModuleImplementing<ModuleEngines>();
            if (mod != null) return mod;
            foreach (PartModule pm in ap.partPrefab.Modules)
            {
                if (pm.moduleName.StartsWith("ModuleEngines"))
                    return pm as ModuleEngines;
            }
            return null;
        }

        private void RefreshData()
        {
            allGroupsCache.Clear();
            foreach (AvailablePart ap in PartLoader.LoadedPartsList)
            {
                var engineMod = FindEngineModule(ap);
                if (engineMod == null) continue;

                if (onlyResearched && !ResearchAndDevelopment.PartModelPurchased(ap)) continue;

                bool isJet = engineMod.propellants.Any(p => p.name == "IntakeAir") || engineMod.propellants.Any(p => p.name.ToLower().Contains("intake"));
                bool isSRB = false;

                if (ap.title != null && (ap.title.ToLower().Contains("srb") || ap.title.ToLower().Contains("solid")))
                {
                    isSRB = true;
                }
                else
                {
                    foreach (var prop in engineMod.propellants)
                    {
                        string pn = prop.name ?? "";
                        if (solidPropellants.Any(f => pn == f || pn.ToLower().Contains(f.ToLower())))
                        {
                            isSRB = true;
                            break;
                        }
                    }
                }

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

                AnimationCurve thrustCurve = null;
                float totalImpulse = 0f;
                float avgThrust = baseMaxThrust;

                if (isSRB)
                {
                    if (engineMod.useThrustCurve && engineMod.thrustCurve != null)
                        thrustCurve = FloatCurveToAnimationCurve(engineMod.thrustCurve);

                    if (thrustCurve == null)
                    {
                        try
                        {
                            foreach (var moduleNode in ap.partConfig?.GetNodes("MODULE") ?? new ConfigNode[0])
                            {
                                var tcNode = moduleNode.GetNode("thrustCurve") ?? moduleNode.GetNode("ThrustCurve");
                                if (tcNode == null) continue;
                                thrustCurve = ParseThrustCurveFromConfigNode(tcNode);
                                if (thrustCurve != null) break;
                            }
                        }
                        catch { }
                    }

                    if (thrustCurve == null)
                        thrustCurve = TryGetBetterSRBCurve(ap);
                }

                if (thrustCurve == null || thrustCurve.keys.Length == 0)
                    thrustCurve = new AnimationCurve(new Keyframe(0, 1f), new Keyframe(1, 1f));
                
                if (isSRB)
                {
                    float burnTime = 10f;
                    
                    float solidFuelMass = 0f;
                    foreach (PartResource res in ap.partPrefab.Resources)
                    {
                        if (res == null || res.resourceName == null) continue;
                        string rn = res.resourceName;
                        if (solidPropellants.Any(f => rn == f || rn.ToLower().Contains(f.ToLower())))
                        {
                            var resDef = PartResourceLibrary.Instance.GetDefinition(rn);
                            if (resDef != null)
                            {
                                solidFuelMass += (float)(res.maxAmount * resDef.density);
                            }
                        }
                    }

                    if (solidFuelMass <= 0)
                    {
                        foreach (var prop in engineMod.propellants)
                        {
                            if (prop == null || prop.name == null) continue;
                            string pn = prop.name;
                            if (solidPropellants.Any(f => pn == f || pn.ToLower().Contains(f.ToLower())))
                            {
                                var resDef = PartResourceLibrary.Instance.GetDefinition(pn);
                                if (resDef != null)
                                {
                                    float maxAmount = GetFieldValue(prop, "maxAmount");
                                    if (maxAmount > 0)
                                    {
                                        solidFuelMass += maxAmount * resDef.density;
                                    }
                                }
                            }
                        }
                    }
                    
                    if (baseMaxThrust > 0 && baseIspVac > 0 && solidFuelMass > 0)
                    {
                        burnTime = solidFuelMass / (baseMaxThrust / (baseIspVac * 9.80665f));
                    }
                    
                    float step = 0.01f;
                    float avgThrustRatio = 0f;
                    int sampleCount = 0;
                    for (float t = 0; t <= 1; t += step)
                    {
                        float thrustVal = thrustCurve != null ? thrustCurve.Evaluate(1f - t) : 1f;
                        if (float.IsNaN(thrustVal) || float.IsInfinity(thrustVal)) thrustVal = 1f;
                        thrustVal = Math.Max(0f, Math.Min(10f, thrustVal));
                        avgThrustRatio += thrustVal;
                        totalImpulse += thrustVal * baseMaxThrust * step * burnTime;
                        sampleCount++;
                    }
                    if (sampleCount > 0)
                    {
                        avgThrust = (avgThrustRatio / sampleCount) * baseMaxThrust;
                    }
                    else
                    {
                        avgThrust = baseMaxThrust;
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
                    sizeFilter = GetEngineSizeFilter(ap),
                    fuelType = DetectFuelType(ap),
                    baseThrust = baseMaxThrust,
                    baseIspVac = baseIspVac,
                    baseIspASL = baseIspASL,
                    manufacturer = ap.manufacturer,
                    thrustCurve = thrustCurve,
                    totalImpulse = totalImpulse,
                    avgThrust = avgThrust
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
                        
                        var atmEngine = FindEngineModule(ap);
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
                        
                        AnimationCurve cfgThrustCurve = null;
                        float cfgTotalImpulse = 0f;
                        float cfgAvgThrust = cfgT;
                        float cfgBurnTime = 10f;
                        
                        if (isSRB)
                        {
                            var cfgTCNode = cfg.GetNode("thrustCurve");
                            if (cfgTCNode == null)
                            {
                                foreach (var subNode in cfg.GetNodes())
                                {
                                    if (subNode.name.ToLower().Contains("thrust") && subNode.name.ToLower().Contains("curve"))
                                    {
                                        cfgTCNode = subNode;
                                        break;
                                    }
                                }
                            }
                            
                            cfgThrustCurve = ParseThrustCurveFromConfigNode(cfgTCNode);
                            if (cfgThrustCurve == null) cfgThrustCurve = TryGetBetterSRBCurveForConfig(ap, cfgName);
                            if (cfgThrustCurve == null) cfgThrustCurve = thrustCurve;
                            if (cfgThrustCurve == null) cfgThrustCurve = new AnimationCurve(new Keyframe(0, 1f), new Keyframe(1, 1f));
                            
                            float solidFuelMass = 0f;
                            if (cfg.HasNode("RESOURCE"))
                            {
                                foreach (var resNode in cfg.GetNodes("RESOURCE"))
                                {
                                    string resName = resNode.GetValue("name") ?? "";
                                    if (solidPropellants.Any(f => resName == f || resName.ToLower().Contains(f.ToLower())))
                                    {
                                        float maxAmount = 0f;
                                        if (resNode.HasValue("maxAmount")) float.TryParse(resNode.GetValue("maxAmount"), out maxAmount);
                                        var resDef = PartResourceLibrary.Instance.GetDefinition(resName);
                                        if (resDef != null && maxAmount > 0)
                                        {
                                            solidFuelMass += maxAmount * resDef.density;
                                        }
                                    }
                                }
                            }
                            
                            if (solidFuelMass <= 0)
                            {
                                foreach (PartResource res in ap.partPrefab.Resources)
                                {
                                    if (res == null || res.resourceName == null) continue;
                                    string rn = res.resourceName;
                                    if (solidPropellants.Any(f => rn == f || rn.ToLower().Contains(f.ToLower())))
                                     {
                                         var resDef = PartResourceLibrary.Instance.GetDefinition(rn);
                                        if (resDef != null)
                                        {
                                            solidFuelMass += (float)(res.maxAmount * resDef.density);
                                        }
                                    }
                                }
                            }
                            
                            float fuelFlow = (cfgT > 0 && cfgIV > 0) ? cfgT / (cfgIV * 9.80665f) : engineMod.maxFuelFlow;
                            float explicitFuelFlow = 0f;
                            if (cfg.HasValue("maxFuelFlow") && float.TryParse(cfg.GetValue("maxFuelFlow"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out explicitFuelFlow) && explicitFuelFlow > 0)
                            {
                                fuelFlow = explicitFuelFlow;
                            }
                            
                            if (fuelFlow > 0 && solidFuelMass > 0)
                            {
                                cfgBurnTime = solidFuelMass / fuelFlow;
                            }
                            
                            float step = 0.01f;
                            float avgThrustRatio = 0f;
                            int sampleCount = 0;
                            for (float t = 0; t <= 1; t += step)
                            {
                                float thrustVal = cfgThrustCurve.Evaluate(1f - t);
                                if (float.IsNaN(thrustVal) || float.IsInfinity(thrustVal)) thrustVal = 1f;
                                thrustVal = Math.Max(0f, Math.Min(10f, thrustVal));
                                avgThrustRatio += thrustVal;
                                cfgTotalImpulse += thrustVal * cfgT * step * cfgBurnTime;
                                sampleCount++;
                            }
                            if (sampleCount > 0)
                            {
                                cfgAvgThrust = (avgThrustRatio / sampleCount) * cfgT;
                            }
                            else
                            {
                                cfgAvgThrust = cfgT;
                            }
                        }
                        
                        var config = CalculateConfig(cfgName, cfgT, cfgIV, cfgIA, baseMixDensity, baseFuels, baseUllage, baseIgnitions, baseBurnTime, ap, engineCount, isHP, isSRB ? cfgAvgThrust : 0f);
                        config.thrustCurve = cfgThrustCurve;
                        config.totalImpulse = cfgTotalImpulse;
                        config.avgThrust = cfgAvgThrust;
                        config.burnTime = cfgBurnTime;
                        group.configs.Add(config);
                    }
                }
                else 
                {
                    float useAvgThrust = isSRB && avgThrust > 0f ? avgThrust : 0f;

                    if (isSRB)
                    {
                        var grains = LoadBetterSRBGrains(ap);
                        if (grains.Count > 0)
                        {
                            foreach (var grain in grains)
                            {
                                float gMaxThrust = baseMaxThrust * grain.thrustMultiplier;
                                float gAvgThrust = avgThrust * grain.thrustMultiplier;
                                var gConfig = CalculateConfig(grain.displayName, gMaxThrust, baseIspVac, baseIspASL, baseMixDensity, baseFuels, baseUllage, baseIgnitions, baseBurnTime, ap, engineCount, false, gAvgThrust);
                                gConfig.thrustCurve = grain.curve ?? thrustCurve;
                                gConfig.totalImpulse = totalImpulse * grain.thrustMultiplier;
                                gConfig.avgThrust = gAvgThrust;

                                float solidFuelMass = 0f;
                                foreach (PartResource res in ap.partPrefab.Resources)
                                {
                                    if (res != null && res.resourceName != null &&
                                        (res.resourceName == "SolidFuel" || res.resourceName.ToLower().Contains("solid")))
                                    {
                                        var resDef = PartResourceLibrary.Instance.GetDefinition(res.resourceName);
                                        if (resDef != null)
                                        {
                                            solidFuelMass = (float)(res.maxAmount * resDef.density);
                                            break;
                                        }
                                    }
                                }
                                gConfig.burnTime = (gMaxThrust > 0 && baseIspVac > 0 && solidFuelMass > 0)
                                    ? solidFuelMass / (gMaxThrust / (baseIspVac * 9.80665f))
                                    : 10f;
                                group.configs.Add(gConfig);
                            }
                        }
                        else
                        {
                            var config = CalculateConfig(L("标准型号", "Standard"), baseMaxThrust, baseIspVac, baseIspASL, baseMixDensity, baseFuels, baseUllage, baseIgnitions, baseBurnTime, ap, engineCount, false, useAvgThrust);
                            config.thrustCurve = thrustCurve;
                            config.totalImpulse = totalImpulse;
                            config.avgThrust = avgThrust;

                            float solidFuelMass = 0f;
                            foreach (PartResource res in ap.partPrefab.Resources)
                            {
                                if (res != null && res.resourceName != null &&
                                    (res.resourceName == "SolidFuel" || res.resourceName.ToLower().Contains("solid")))
                                {
                                    var resDef = PartResourceLibrary.Instance.GetDefinition(res.resourceName);
                                    if (resDef != null)
                                    {
                                        solidFuelMass = (float)(res.maxAmount * resDef.density);
                                        break;
                                    }
                                }
                            }
                            config.burnTime = (baseMaxThrust > 0 && baseIspVac > 0 && solidFuelMass > 0)
                                ? solidFuelMass / (baseMaxThrust / (baseIspVac * 9.80665f))
                                : 10f;
                            group.configs.Add(config);
                        }
                    }
                    else
                    {
                        var config = CalculateConfig(L("标准型号", "Standard"), baseMaxThrust, baseIspVac, baseIspASL, baseMixDensity, baseFuels, baseUllage, baseIgnitions, baseBurnTime, ap, engineCount, false);
                        group.configs.Add(config);
                    }
                }
                allGroupsCache.Add(group);
            }
            ApplyFilters();
        }

        private EngineConfig CalculateConfig(string name, float T, float IV, float IA, float dens, List<string> f, string ull, string ign, string bt, AvailablePart ap, int cnt, bool isHP, float avgThrust = 0f)
        {
            const float g0 = 9.80665f;
            
            float effectiveT = avgThrust > 0f ? avgThrust : T;
            float isp, thrust;
            if (isVacMode)
            {
                isp = IV;
                thrust = effectiveT * cnt;
            }
            else
            {
                isp = IA;
                var mod = FindEngineModule(ap);
                if (mod != null)
                {
                    float curveT0 = mod.thrustCurve.Evaluate(0f);
                    thrust = effectiveT * Math.Max(curveT0, 0.01f) * cnt;
                }
                else
                {
                    thrust = effectiveT * (IA / Math.Max(IV, 0.01f)) * cnt;
                }
            }
            
            float dry = vesselMassAtSync + (ap.partPrefab.mass * cnt);
            float dV = 0, twr = 0, vol = 0, wet = 0;
            bool meet = true;

            var engineModule = FindEngineModule(ap);
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
            if (allGroupsCache.Count == 0)
            {
                RefreshData();
                if (allGroupsCache.Count == 0) return;
            }
            
            isSciFiMode = ispLimit >= SCIFI_THRESHOLD;
            filteredGroups.Clear();
            foreach (var g in allGroupsCache)
            {
                if (!((showRockets && !g.isJet && !g.isSRB) || (showJets && g.isJet) || (showSRB && g.isSRB))) continue;
                if (!string.IsNullOrEmpty(searchFilter) && g.partTitle.IndexOf(searchFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (currentSizeFilter != SizeFilter.All && g.sizeFilter != currentSizeFilter) continue;
                if (filterDeprecated && g.partTitle.ToUpper().Contains("DEPRECATED")) continue;
                if (filterNonRO) { string tu = g.partTitle.ToUpper(); if (tu.Contains("NON RO") || tu.Contains("NONRO")) continue; }
                // Fuel filter commented out: if (currentFuelFilter != FuelType.All && g.fuelType != currentFuelFilter) continue;

                var fg = new EnginePartGroup { part = g.part, partTitle = g.partTitle, isJet = g.isJet, isSRB = g.isSRB, configs = new List<EngineConfig>(), isHidden = g.isHidden, engineSize = g.engineSize, fuelType = g.fuelType, baseThrust = g.baseThrust, baseIspVac = g.baseIspVac, baseIspASL = g.baseIspASL, manufacturer = g.manufacturer, thrustCurve = g.thrustCurve, totalImpulse = g.totalImpulse, avgThrust = g.avgThrust };
                foreach (var c in g.configs)
                {
                    if ((isSciFiMode || (isVacMode ? c.ispVac : c.ispASL) <= ispLimit) && (twrFilterLimit >= 20.1f || c.twr <= twrFilterLimit + 0.01f) && c.twr >= twrMinLimit - 0.01f && (!isSmartMode || c.meetsSmartCriteria))
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
            windowRect.height = 680f;
            windowRect = GUILayout.Window(888, windowRect, DrawWindow, L("引擎全效分析器 v1.0.0", "Engine Analyzer v1.0.0"));
            
            if (showDetailPanel && selectedEngine != null)
            {
                float targetWidth = isCompactMode ? 450 : 850;
                detailWindowRect = new Rect(detailWindowRect.x, detailWindowRect.y, targetWidth, isCompactMode ? 400 : 600);
                detailWindowRect = GUI.Window(12345, detailWindowRect, DrawDetailPanel, L("引擎详情", "Engine Details"));
            }
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

            bool fd = filterDeprecated;
            bool fn = filterNonRO;
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
                fd = GUILayout.Toggle(filterDeprecated, L(" 无废弃", " No Dep"), GUILayout.Width(75));
                fn = GUILayout.Toggle(filterNonRO, L(" 纯RO", " Only RO"), GUILayout.Width(75));
                GUILayout.EndHorizontal();

                // Fuel filter commented out due to instability
            // GUILayout.BeginHorizontal();
            // GUILayout.Label(L("燃料:", "Fuel:"), GUILayout.Width(40));
            // ... fuel filter buttons ...
            // GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                fd = GUILayout.Toggle(filterDeprecated, L(" 无废弃", " No Dep"), GUILayout.Width(75));
                fn = GUILayout.Toggle(filterNonRO, L(" 纯RO", " Only RO"), GUILayout.Width(75));
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();           // ← 闭合过滤器框(GUI.skin.box)，排序/滑条/搜索在外

            GUILayout.BeginHorizontal();
            GUILayout.Label(L("排序:", "Sort:"), GUILayout.Width(50));
            if (GUILayout.Button(currentSortMode == SortMode.DeltaV ? L("<color=lime>Δv</color>", "<color=lime>Δv</color>") : L("Δv", "Δv"), GUILayout.Width(50))) { currentSortMode = SortMode.DeltaV; ApplyFilters(); }
            if (GUILayout.Button(currentSortMode == SortMode.TWR ? L("<color=lime>TWR</color>", "<color=lime>TWR</color>") : L("TWR", "TWR"), GUILayout.Width(50))) { currentSortMode = SortMode.TWR; ApplyFilters(); }
            if (GUILayout.Button(currentSortMode == SortMode.Isp ? L("<color=lime>Isp</color>", "<color=lime>Isp</color>") : L("Isp", "Isp"), GUILayout.Width(50))) { currentSortMode = SortMode.Isp; ApplyFilters(); }
            if (GUILayout.Button(currentSortMode == SortMode.Value ? L("<color=lime>性价比</color>", "<color=lime>Value</color>") : L("性价比", "Value"), GUILayout.Width(60))) { currentSortMode = SortMode.Value; ApplyFilters(); }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (r != showRockets || j != showJets || s != showSRB || or != onlyResearched) { showRockets = r; showJets = j; showSRB = s; onlyResearched = or; RefreshData(); }
            if (fd != filterDeprecated || fn != filterNonRO) { filterDeprecated = fd; filterNonRO = fn; ApplyFilters(); }

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

            scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.Width(windowRect.width - 20), GUILayout.Height(isCompactMode ? 320f : 380f));
            foreach (var group in filteredGroups)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.BeginHorizontal();
                string typeLabel = group.isJet ? L("<color=orange>[喷气]</color> ", "<color=orange>[Jet]</color> ") : (group.isSRB ? L("<color=red>[固推]</color> ", "<color=red>[SRB]</color> ") : L("<color=cyan>[火箭]</color> ", "<color=cyan>[Rocket]</color> "));
                string hiddenLabel = group.isHidden ? L(" <color=red>[隐藏]</color>", " <color=red>[Hidden]</color>") : "";
                string sizeLabel = $" ({group.engineSize})";
                GUILayout.Label($"<size=15>{typeLabel}<b>{group.partTitle}</b>{hiddenLabel}{sizeLabel}{(engineCount > 1 ? $" x{engineCount}" : "")}</size>");
                if (GUILayout.Button(L("详情", "Detail"), GUILayout.Width(60))) { selectedEngine = group; showDetailPanel = true; _grainPresets.Clear(); _selectedGrainIndex = 0; _cachedChartRect = Rect.zero; }
                GUILayout.EndHorizontal();

                for (int i = 0; i < group.configs.Count; i++)
                {
                    var cfg = group.configs[i];
                    GUILayout.BeginVertical(GUI.skin.box);
                    GUILayout.BeginHorizontal();

                    string hpDisplay = cfg.needsHighPressure ? L("<color=red>[需高压罐]</color> ", "<color=red>[High Pressure]</color> ") : "";

                    if (isCompactMode)
                    {
                        string impStr = cfg.totalImpulse > 0 ? $" Imp:{cfg.totalImpulse:F0}kN·s" : "";
                        GUILayout.Label($"{hpDisplay}<size=12><color=#E0E0E0>{cfg.configName}</color></size>", GUILayout.Width(180));
                        GUILayout.FlexibleSpace();
                        GUILayout.Label($"Δv:{cfg.deltaV:F0} TWR:{cfg.twr:F2}{impStr}");
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
                        if (cfg.totalImpulse > 0)
                            GUILayout.Label($"Imp: <color=orange>{cfg.totalImpulse:F0}</color>", GUILayout.Width(80));
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
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(L("关闭窗口", "Close Window"), GUILayout.Height(25))) isVisible = false;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        private void DrawDetailPanel(int windowID)
        {
            float panelWidth = isCompactMode ? 450 : 850;
            detailWindowRect.width = panelWidth;
            detailWindowRect.height = isCompactMode ? 550f : 650f;

            GUILayout.BeginVertical(GUILayout.Width(panelWidth));
            GUILayout.Label($"<size=16><b>{selectedEngine.partTitle}</b></size>", GUILayout.Height(35));

            float maxHeight = isCompactMode ? 340 : 500;
            detailScrollPosition = GUILayout.BeginScrollView(detailScrollPosition, GUILayout.Width(panelWidth - 10), GUILayout.MaxHeight(maxHeight));

            // 配置/药柱选择器
            bool hasConfigs = selectedEngine.configs.Count > 1;
            bool hasGrains = false;
            if (selectedEngine.isSRB)
            {
                if (_grainPresets.Count == 0)
                    _grainPresets = LoadBetterSRBGrains(selectedEngine.part);
                hasGrains = _grainPresets.Count > 0;
            }

            if (hasGrains)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("<b>" + L("推力变体", "Thrust Variants") + "</b>");
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(L("🔄 读取当前药柱曲线", "🔄 Read Current Grain"), GUILayout.Width(160)))
                {
                    var eng = FindEngineModule(selectedEngine.part);
                    if (eng != null && eng.useThrustCurve && eng.thrustCurve != null)
                    {
                        var fresh = FloatCurveToAnimationCurve(eng.thrustCurve);
                        if (fresh != null && fresh.keys.Length >= 2)
                        {
                            if (_selectedGrainIndex < _grainPresets.Count)
                                _grainPresets[_selectedGrainIndex] = new BetterSRBGrain
                                {
                                    displayName = _grainPresets[_selectedGrainIndex].displayName,
                                    curve = fresh,
                                    thrustMultiplier = _grainPresets[_selectedGrainIndex].thrustMultiplier
                                };
                            _cachedChartRect = Rect.zero;
                        }
                    }
                }
                GUILayout.EndHorizontal();

                bool allSameCurve = _grainPresets.Count > 1 &&
                    _grainPresets.All(g => ReferenceEquals(g.curve, _grainPresets[0].curve));
                if (allSameCurve)
                    GUILayout.Label(
                        L("<size=11><color=#aaaaaa>※ 在右键菜单选好药柱后点上方按钮逐个记录曲线</color></size>",
                          "<size=11><color=#aaaaaa>※ Select each grain in PAW, then click above to record its curve</color></size>"));

                var grainNames = _grainPresets.Select(g => g.displayName.Replace("\n", "").Replace("\r", "")).ToArray();
                int cols = Mathf.Min(grainNames.Length, 4);
                int newGrain = GUILayout.SelectionGrid(_selectedGrainIndex, grainNames, cols);
                if (newGrain != _selectedGrainIndex)
                {
                    _selectedGrainIndex = newGrain;
                    selectedConfigIndex = Mathf.Min(newGrain, selectedEngine.configs.Count - 1);
                    _cachedChartRect = Rect.zero;
                }
                GUILayout.Space(5);
            }
            else if (hasConfigs)
            {
                GUILayout.Label("<b>" + L("配置:", "Configuration:") + "</b>");
                var configNames = selectedEngine.configs.Select(c => c.configName).ToArray();
                int cols = Mathf.Min(configNames.Length, 4);
                int newIndex = GUILayout.SelectionGrid(selectedConfigIndex, configNames, cols);
                if (newIndex != selectedConfigIndex)
                {
                    selectedConfigIndex = newIndex;
                    _cachedChartRect = Rect.zero;
                }
                GUILayout.Space(5);
            }
            else
            {
                selectedConfigIndex = 0;
            }

            var selectedConfig = selectedEngine.configs[Mathf.Clamp(selectedConfigIndex, 0, selectedEngine.configs.Count - 1)];

            GUILayout.Label(L("制造商:", "Manufacturer:") + " " + (string.IsNullOrEmpty(selectedEngine.manufacturer) ? L("未知", "Unknown") : selectedEngine.manufacturer));
            GUILayout.Label(L("尺寸:", "Size:") + " " + selectedEngine.engineSize);
            GUILayout.Label(L("类型:", "Type:") + " " + (selectedEngine.isJet ? L("喷气发动机", "Jet Engine") : (selectedEngine.isSRB ? L("固体助推器", "Solid Rocket Booster") : L("液体火箭发动机", "Liquid Rocket Engine"))));
            GUILayout.Label(L("燃料类型:", "Fuel Type:") + " " + GetFuelTypeName(selectedEngine.fuelType));

            GUILayout.Space(8);

            if (selectedEngine.isSRB)
            {
                float baseThrust   = selectedEngine.baseThrust;
                float grainThrust  = (_grainPresets.Count > 0 && _selectedGrainIndex < _grainPresets.Count)
                    ? baseThrust * _grainPresets[_selectedGrainIndex].thrustMultiplier
                    : selectedConfig.avgThrust > 0f ? selectedConfig.avgThrust : baseThrust;
                float displayThrust = grainThrust;

                float solidFuelMass = 0f;
                foreach (PartResource res in selectedEngine.part.partPrefab.Resources)
                {
                    if (res == null || res.resourceName == null) continue;
                    string rn = res.resourceName;
                    if (solidPropellants.Any(f => rn == f || rn.ToLower().Contains(f.ToLower())))
                    {
                        var resDef = PartResourceLibrary.Instance.GetDefinition(rn);
                        if (resDef != null)
                            solidFuelMass += (float)(res.maxAmount * resDef.density);
                    }
                }

                float burnTime = selectedConfig.burnTime;
                if (grainThrust > 0 && selectedEngine.baseIspVac > 0 && solidFuelMass > 0
                    && (burnTime <= 0 || burnTime > 200))
                {
                    burnTime = solidFuelMass / (grainThrust / (selectedEngine.baseIspVac * 9.80665f));
                }
                if (burnTime <= 0) burnTime = 150f;
                float displayIspVac = selectedEngine.baseIspVac;
                float displayIspASL = selectedEngine.baseIspASL;
                float twrThrust = selectedConfig.avgThrust > 0f ? selectedConfig.avgThrust : baseThrust;
                float twr = twrThrust / (Math.Max(selectedEngine.part.partPrefab.mass, 0.001f) * 9.81f);

                float displayMass = selectedEngine.part.partPrefab.mass;
                try
                {
                    float resourceMass = 0f;
                    foreach (PartResource res in selectedEngine.part.partPrefab.Resources)
                    {
                        if (res != null && res.maxAmount > 0)
                        {
                            var resDef = PartResourceLibrary.Instance.GetDefinition(res.resourceName);
                            if (resDef != null) resourceMass += (float)(res.maxAmount * resDef.density);
                        }
                    }
                    displayMass += resourceMass;
                }
                catch { }

                AnimationCurve displayCurve;
                if (_grainPresets.Count > 0 && _selectedGrainIndex < _grainPresets.Count)
                    displayCurve = _grainPresets[_selectedGrainIndex].curve;
                else
                    displayCurve = selectedConfig.thrustCurve;

                if (displayCurve == null || displayCurve.keys.Length < 2)
                    displayCurve = new AnimationCurve(new Keyframe(0, 1f), new Keyframe(1, 1f));

                float curveTimeMax = 1f;
                if (displayCurve.keys.Length >= 2)
                {
                    float firstKey = displayCurve.keys[0].time;
                    float lastKey = displayCurve.keys[displayCurve.length - 1].time;
                    curveTimeMax = Mathf.Max(Mathf.Max(firstKey, lastKey), 1f);
                }

                float displayTotalImpulse = selectedConfig.totalImpulse;
                float displayAvgThrust = selectedConfig.avgThrust > 0f ? selectedConfig.avgThrust : displayThrust;
                bool grainChanged = _grainPresets.Count > 0
                    && _selectedGrainIndex < _grainPresets.Count
                    && !ReferenceEquals(_grainPresets[_selectedGrainIndex].curve, selectedConfig.thrustCurve);
                if (grainChanged)
                {
                    float curveValMax = 0.001f;
                    for (float t = 0; t <= 1f; t += 0.01f)
                    {
                        float v = displayCurve.Evaluate((1f - t) * curveTimeMax);
                        if (!float.IsNaN(v) && !float.IsInfinity(v) && v > curveValMax) curveValMax = v;
                    }
                    float recalcImpulse = 0f, recalcRatio = 0f;
                    int recalcCount = 0;
                    for (float t = 0; t <= 1f; t += 0.01f)
                    {
                        float tv = displayCurve.Evaluate((1f - t) * curveTimeMax);
                        if (float.IsNaN(tv) || float.IsInfinity(tv)) tv = 1f;
                        tv = Math.Max(0f, tv);
                        float tvNorm = tv / curveValMax;
                        recalcRatio += tvNorm;
                        recalcImpulse += tvNorm * displayThrust * 0.01f * burnTime;
                        recalcCount++;
                    }
                    float recalcAvgRatio = recalcCount > 0 ? recalcRatio / recalcCount : 1f;
                    displayTotalImpulse = recalcImpulse;
                    displayAvgThrust = recalcAvgRatio > 0.01f ? recalcImpulse / burnTime : displayThrust;
                }

                if (isCompactMode)
                {
                    GUILayout.BeginVertical(GUI.skin.box);
                    GUILayout.Label("<b>" + L("性能参数", "Performance") + "</b>");
                    GUILayout.Label(L("铭牌推力:", "Rated Thrust:") + $" <color=lime>{baseThrust:F0} kN</color>");
                    if (_grainPresets.Count > 0 && Math.Abs(grainThrust - baseThrust) > 1f)
                        GUILayout.Label(L("药柱推力:", "Grain Thrust:") + $" <color=#aaffaa>{grainThrust:F0} kN</color>");
                    GUILayout.Label(L("真空比冲:", "Vacuum Isp:") + $" <color=cyan>{displayIspVac:F0} s</color>");
                    GUILayout.Label(L("海平面比冲:", "Sea Level Isp:") + $" <color=orange>{displayIspASL:F0} s</color>");
                    GUILayout.Label(L("推重比(自身):", "TWR(Self):") + $" <color=yellow>{twr:F2}</color>");
                    GUILayout.Label(L("价格:", "Price:") + $" <color=yellow>{selectedEngine.part.cost:F0}</color>");
                    GUILayout.Label(L("质量:", "Mass:") + $" {displayMass:F3} t");
                    GUILayout.EndVertical();
                    GUILayout.Space(5);
                    GUILayout.BeginVertical(GUI.skin.box);
                    GUILayout.Label("<b>" + L("推力曲线", "Thrust Curve") + "</b>");
                    GUILayout.Label(L("总冲量:", "Total Impulse:") + $" <color=lime>{displayTotalImpulse:F1} kN·s</color>");
                    GUILayout.Label(L("平均推力:", "Avg Thrust:") + $" <color=orange>{displayAvgThrust:F0} kN</color>");
                    GUILayout.Label(L("燃烧时间:", "Burn Time:") + $" <color=cyan>{burnTime:F1} s</color>");
                    GUILayout.EndVertical();
                }
                else
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(220));
                    GUILayout.Label("<b>" + L("性能参数", "Performance") + "</b>");
                    GUILayout.Label(L("铭牌推力:", "Rated Thrust:") + $" <color=lime>{baseThrust:F0} kN</color>");
                    if (_grainPresets.Count > 0 && Math.Abs(grainThrust - baseThrust) > 1f)
                        GUILayout.Label(L("药柱推力:", "Grain Thrust:") + $" <color=#aaffaa>{grainThrust:F0} kN</color>");
                    GUILayout.Label(L("真空比冲:", "Vacuum Isp:") + $" <color=cyan>{displayIspVac:F0} s</color>");
                    GUILayout.Label(L("海平面比冲:", "Sea Level Isp:") + $" <color=orange>{displayIspASL:F0} s</color>");
                    GUILayout.Label(L("推重比(自身):", "TWR(Self):") + $" <color=yellow>{twr:F2}</color>");
                    GUILayout.Label(L("价格:", "Price:") + $" <color=yellow>{selectedEngine.part.cost:F0}</color>");
                    GUILayout.Label(L("质量:", "Mass:") + $" {displayMass:F3} t");
                    GUILayout.EndVertical();
                    GUILayout.Space(10);
                    GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(panelWidth - 250));
                    GUILayout.Label("<b>" + L("推力曲线", "Thrust Curve") + "</b>");
                    GUILayout.Label(L("总冲量:", "Total Impulse:") + $" <color=lime>{displayTotalImpulse:F1} kN·s</color>");
                    GUILayout.Label(L("平均推力:", "Avg Thrust:") + $" <color=orange>{displayAvgThrust:F0} kN</color>");
                    GUILayout.Label(L("燃烧时间:", "Burn Time:") + $" <color=cyan>{burnTime:F1} s</color>");
                    GUILayout.EndVertical();
                    GUILayout.EndHorizontal();
                }

                GUILayout.Space(8);

                float yLabelWidth = 65f;
                float chartWidth = isCompactMode ? panelWidth - 60 : panelWidth - yLabelWidth - 50f;
                float chartHeight = 150f;
                int yDivisions = 5;
                int xDivisions = 6;

                float curvePeak = 1f;
                if (displayCurve != null)
                {
                    for (float t = 0; t <= 1f; t += 0.01f)
                    {
                        float v = displayCurve.Evaluate((1f - t) * curveTimeMax);
                        if (!float.IsNaN(v) && !float.IsInfinity(v) && v > curvePeak) curvePeak = v;
                    }
                }
                float chartMaxKN = displayThrust * Math.Max(curvePeak, 0.001f);

                GUILayout.BeginHorizontal();
                GUILayout.BeginVertical(GUILayout.Width(yLabelWidth));
                float labelSlotH = chartHeight / (yDivisions - 1);
                for (int i = 0; i < yDivisions; i++)
                {
                    float tv = chartMaxKN * ((float)(yDivisions - 1 - i) / (yDivisions - 1));
                    GUILayout.Label($"<size=11><color=#cccccc>{tv:F0} kN</color></size>",
                        GUILayout.Width(yLabelWidth), GUILayout.Height(labelSlotH));
                }
                GUILayout.EndVertical();

                GUILayout.BeginVertical(GUILayout.Width(chartWidth));

                Rect reservedRect = GUILayoutUtility.GetRect(chartWidth, chartHeight);
                if (Event.current.type == EventType.Repaint)
                {
                    _cachedChartRect = reservedRect;
                    DrawThrustCurve(_cachedChartRect, displayCurve, chartMaxKN, burnTime, curveTimeMax, detailWindowRect);
                }
                else if (_cachedChartRect != Rect.zero)
                {
                    DrawThrustCurve(_cachedChartRect, displayCurve, chartMaxKN, burnTime, curveTimeMax, detailWindowRect);
                }

                GUILayout.BeginHorizontal(GUILayout.Width(chartWidth));
                float slotW = chartWidth / xDivisions;
                for (int i = 0; i < xDivisions; i++)
                {
                    float timeVal = burnTime * ((float)i / xDivisions);
                    float labelW = (i == xDivisions - 1) ? slotW - 25f : slotW;
                    GUILayout.Label($"<size=11><color=#cccccc>{timeVal:F1}</color></size>",
                        GUILayout.Width(labelW), GUILayout.Height(20));
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal(GUILayout.Width(chartWidth));
                GUILayout.FlexibleSpace();
                GUILayout.Label($"<size=12><color=#aaaaaa>{L("时间(秒)", "Time (s)")}</color></size>");
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label("<b>" + L("性能参数", "Performance") + "</b>");

                float displayThrust = selectedEngine.baseThrust;
                float displayIspVac = selectedEngine.baseIspVac;
                float displayIspASL = selectedEngine.baseIspASL;

                GUILayout.Label(L("基础推力:", "Base Thrust:") + $" <color=lime>{displayThrust:F0} kN</color>");
                GUILayout.Label(L("真空比冲:", "Vacuum Isp:") + $" <color=cyan>{displayIspVac:F0} s</color>");
                GUILayout.Label(L("海平面比冲:", "Sea Level Isp:") + $" <color=orange>{displayIspASL:F0} s</color>");

                float twr = displayThrust / (selectedEngine.part.partPrefab.mass * 9.81f);
                GUILayout.Label(L("推重比(自身):", "TWR(Self):") + $" <color=yellow>{twr:F2}</color>");
                GUILayout.Label(L("价格:", "Price:") + $" <color=yellow>{selectedEngine.part.cost:F0}</color>");

                float displayMass = selectedEngine.part.partPrefab.mass;
                GUILayout.Label(L("质量:", "Mass:") + $" {displayMass:F3} t");

                if (selectedEngine.isJet)
                {
                    GUILayout.Space(10);
                    GUILayout.Label("<b>" + L("喷气发动机特性", "Jet Engine Characteristics") + "</b>");
                    GUILayout.Label(L("推力特性:", "Thrust Characteristic:") + " <color=yellow>" + L("随速度/高度变化", "Varies with speed/altitude") + "</color>");
                    GUILayout.Label(L("工作范围:", "Operating Range:") + " <color=cyan>" + L("大气层内", "Within atmosphere") + "</color>");
                }
                else if (selectedEngine.fuelType == FuelType.Xenon || selectedEngine.fuelType == FuelType.Electric)
                {
                    GUILayout.Space(10);
                    GUILayout.Label("<b>" + L("电推进特性", "Electric Propulsion") + "</b>");
                    GUILayout.Label(L("推力类型:", "Thrust Type:") + " <color=yellow>" + L("持续低推力", "Continuous low thrust") + "</color>");
                }
                else
                {
                    GUILayout.Space(10);
                    GUILayout.Label("<b>" + L("推力特性", "Thrust Characteristics") + "</b>");
                    GUILayout.Label(L("推力类型:", "Thrust Type:") + " <color=green>" + L("可节流稳定推力", "Throttleable stable thrust") + "</color>");
                }
            }

            GUILayout.Space(10);
            GUILayout.Label("<b>" + L("配置列表", "Configurations") + "</b>");
            foreach (var cfg in selectedEngine.configs.Take(3))
            {
                GUILayout.Label($"  {cfg.configName}: Δv={cfg.deltaV:F0}m/s, TWR={cfg.twr:F2}, Isp={cfg.ispVac:F0}s");
            }
            if (selectedEngine.configs.Count > 3)
            {
                GUILayout.Label($"  ... ({selectedEngine.configs.Count - 3} " + L("更多配置", "more configs)"));
            }

            GUILayout.EndScrollView();

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(L("生成引擎", "Spawn"), GUILayout.Width(100), GUILayout.Height(25)))
            {
                EditorLogic.fetch.SpawnPart(selectedEngine.part);
            }
            if (GUILayout.Button(L("关闭", "Close"), GUILayout.Width(100), GUILayout.Height(25)))
            {
                showDetailPanel = false;
                selectedEngine = null;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        /// <summary>
        /// 读取 BetterSRB 的药柱变体列表。
        /// </summary>
        private List<BetterSRBGrain> LoadBetterSRBGrains(AvailablePart ap)
        {
            var result = new List<BetterSRBGrain>();
            if (ap?.partConfig == null || ap.partPrefab == null) return result;

            try
            {
                bool hasBetterSRBTag = ap.partConfig.HasValue("BetterSRBsTag");

                ConfigNode engModule = null;
                bool hasMaxThrust01 = false;
                foreach (var mn in ap.partConfig.GetNodes("MODULE"))
                {
                    string mname = mn.GetValue("name") ?? "";
                    if (!mname.StartsWith("ModuleEngine")) continue;
                    if (mn.HasValue("maxThrust01")) { hasMaxThrust01 = true; engModule = mn; break; }
                    if (engModule == null) engModule = mn;
                }

                if (!hasBetterSRBTag && !hasMaxThrust01) return result;
                if (engModule == null) return result;

                float baseThrust = 0f;
                if (engModule.HasValue("maxThrust"))
                    float.TryParse(engModule.GetValue("maxThrust"),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out baseThrust);

                AnimationCurve fallbackCurve = null;
                {
                    var eng = ap.partPrefab.FindModuleImplementing<ModuleEngines>();
                    if (eng != null && eng.useThrustCurve && eng.thrustCurve != null)
                        fallbackCurve = FloatCurveToAnimationCurve(eng.thrustCurve);
                }
                if (fallbackCurve == null)
                {
                    var tcNode = engModule.GetNode("thrustCurve");
                    if (tcNode != null) fallbackCurve = ParseThrustCurveFromConfigNode(tcNode);
                }
                if (fallbackCurve == null)
                    fallbackCurve = new AnimationCurve(new Keyframe(0, 1f), new Keyframe(1, 1f));

                List<(string title, AnimationCurve curve, float mult, int priority)> b9Grains = null;
                foreach (var modNode in ap.partConfig.GetNodes("MODULE"))
                {
                    if (modNode.GetValue("name") != "ModuleB9PartSwitch") continue;
                    string mid = modNode.GetValue("moduleID") ?? "";
                    if (!mid.Contains("solid") && !mid.Contains("thrust") && !mid.Contains("grain")) continue;

                    b9Grains = new List<(string, AnimationCurve, float, int)>();
                    foreach (var subtype in modNode.GetNodes("SUBTYPE"))
                    {
                        string rawTitle = subtype.GetValue("title") ?? subtype.GetValue("name") ?? "Unknown";
                        string title = rawTitle.Replace("\n", "").Replace("\r", "").Replace("<br>", " ").Replace("<br/>", " ");
                        int.TryParse(subtype.GetValue("defaultSubtypePriority") ?? "0", out int priority);

                        AnimationCurve gc = null;
                        float mult = 1f;
                        var dataNode = subtype.GetNode("MODULE")?.GetNode("DATA");
                        if (dataNode != null)
                        {
                            var tcNode = dataNode.GetNode("thrustCurve");
                            if (tcNode != null) gc = ParseThrustCurveFromConfigNode(tcNode);

                            string maxThrustRef = dataNode.GetValue("maxThrust") ?? "";
                            if (!string.IsNullOrEmpty(maxThrustRef))
                            {
                                int idx0 = maxThrustRef.LastIndexOf('/');
                                int idx1 = maxThrustRef.LastIndexOf('$');
                                float vt = 0f;
                                if (idx0 >= 0 && idx1 > idx0)
                                {
                                    string fieldName = maxThrustRef.Substring(idx0 + 1, idx1 - idx0 - 1);
                                    if (engModule.HasValue(fieldName))
                                        float.TryParse(engModule.GetValue(fieldName),
                                            System.Globalization.NumberStyles.Float,
                                            System.Globalization.CultureInfo.InvariantCulture, out vt);
                                }
                                else
                                {
                                    float.TryParse(maxThrustRef,
                                        System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture, out vt);
                                }
                                if (vt > 0 && baseThrust > 0) mult = vt / baseThrust;
                            }
                        }
                        b9Grains.Add((title, gc, mult, priority));
                    }
                    if (b9Grains.Count >= 2) break;
                    b9Grains = null;
                }

                if (b9Grains != null && b9Grains.Count >= 2)
                {
                    foreach (var (title, gc, mult, _) in b9Grains.OrderByDescending(g => g.priority))
                    {
                        result.Add(new BetterSRBGrain
                        {
                            displayName = title,
                            curve = gc ?? fallbackCurve,
                            thrustMultiplier = mult > 0 ? mult : 1f
                        });
                    }
                    return result;
                }

                var grainCurves = new List<(string name, AnimationCurve curve, float thrustMult)>();
                var bFlags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

                foreach (PartModule pm in ap.partPrefab.Modules)
                {
                    if (pm == null) continue;
                    var pmType = pm.GetType();
                    string pmFullName = pmType.FullName ?? "";
                    if (pmFullName.StartsWith("UnityEngine") || pmFullName.StartsWith("KSPAssets")) continue;

                    foreach (var field in pmType.GetFields(bFlags))
                    {
                        if (!typeof(System.Collections.IList).IsAssignableFrom(field.FieldType)) continue;
                        System.Collections.IList list;
                        try { list = field.GetValue(pm) as System.Collections.IList; } catch { continue; }
                        if (list == null || list.Count < 2) continue;

                        var first = list[0];
                        if (first == null) continue;
                        var itemType = first.GetType();
                        var itemFields = itemType.GetFields(bFlags);

                        System.Reflection.FieldInfo nameField = null, curveField = null;
                        System.Reflection.FieldInfo floatCurveField = null;
                        System.Reflection.FieldInfo thrustField = null;
                        foreach (var f in itemFields)
                        {
                            string fn = f.Name.ToLower();
                            if (f.FieldType == typeof(string) && (fn.Contains("name") || fn.Contains("display") || fn.Contains("label")))
                                nameField = nameField ?? f;
                            if (f.FieldType == typeof(AnimationCurve))
                                curveField = curveField ?? f;
                            if (f.FieldType == typeof(FloatCurve))
                                floatCurveField = floatCurveField ?? f;
                            if (f.FieldType == typeof(float) && (fn.Contains("thrust") || fn.Contains("mult") || fn.Contains("ratio")))
                                thrustField = thrustField ?? f;
                        }

                        if (nameField == null || (curveField == null && floatCurveField == null)) continue;

                        grainCurves.Clear();
                        bool readOK = true;
                        foreach (var item in list)
                        {
                            try
                            {
                                string gname = nameField.GetValue(item) as string ?? $"Grain {grainCurves.Count + 1}";
                                AnimationCurve gc = null;
                                if (curveField != null) gc = curveField.GetValue(item) as AnimationCurve;
                                if ((gc == null || gc.keys.Length < 2) && floatCurveField != null)
                                    gc = FloatCurveToAnimationCurve(floatCurveField.GetValue(item) as FloatCurve);
                                float tm = thrustField != null ? (float)thrustField.GetValue(item) : 1f;
                                grainCurves.Add((gname, gc, tm > 0 ? tm : 1f));
                            }
                            catch { readOK = false; break; }
                        }
                        if (readOK && grainCurves.Count >= 2)
                        {
                            foreach (var (gname, gc, tm) in grainCurves)
                            {
                                result.Add(new BetterSRBGrain
                                {
                                    displayName = gname,
                                    curve = gc ?? fallbackCurve,
                                    thrustMultiplier = tm
                                });
                            }
                            return result;
                        }
                    }
                }

                if (hasMaxThrust01 && result.Count == 0)
                {
                    if (baseThrust > 0)
                        result.Add(new BetterSRBGrain
                        {
                            displayName = L($"默认 ({baseThrust:F0} kN)", $"Default ({baseThrust:F0} kN)"),
                            curve = fallbackCurve, thrustMultiplier = 1f
                        });
                    for (int i = 1; i <= 20; i++)
                    {
                        string key = $"maxThrust{i:D2}";
                        if (!engModule.HasValue(key)) break;
                        float.TryParse(engModule.GetValue(key),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float vt);
                        if (vt <= 0) continue;
                        result.Add(new BetterSRBGrain
                        {
                            displayName = L($"变体{i} ({vt:F0} kN)", $"Variant {i} ({vt:F0} kN)"),
                            curve = fallbackCurve,
                            thrustMultiplier = baseThrust > 0 ? vt / baseThrust : 1f
                        });
                    }
                    if (result.Count <= 1) result.Clear();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"EngineAnalyzer: LoadBetterSRBGrains error: {ex.Message}\n{ex.StackTrace}");
            }

            return result;
        }
        /// <summary>判断曲线是否为平直线（值始终为常数）</summary>
        private bool IsFlat(AnimationCurve c)
        {
            if (c == null || c.keys.Length < 2) return true;
            float first = c.keys[0].value;
            for (int i = 1; i < c.keys.Length; i++)
                if (Math.Abs(c.keys[i].value - first) > 0.01f) return false;
            return true;
        }

        /// <summary>
        /// 返回标准 BetterSRB 默认药柱曲线（渐增-持平-渐减）。
        /// 来源：BetterSRBs 的 KSP_stock.cfg / ReStockPlus.cfg 中所有零件共用的曲线。
        /// key = 0 0.1 0 35 / key = 0.03 0.801 0.76 0.76 / key = 0.61 1.2418 0.76 -0.62 / key = 1 1 -0.62 0
        /// </summary>
        private AnimationCurve MakeDefaultBetterSRBCurve()
        {
            var keys = new Keyframe[]
            {
                new Keyframe(0f,    0.1f,    0f,     35f),
                new Keyframe(0.03f, 0.801f,  0.76f,  0.76f),
                new Keyframe(0.61f, 1.2418f, 0.76f, -0.62f),
                new Keyframe(1f,    1f,     -0.62f,  0f)
            };
            var c = new AnimationCurve(keys);
            var fixedKeys = c.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                fixedKeys[i].inTangent  = keys[i].inTangent;
                fixedKeys[i].outTangent = keys[i].outTangent;
                fixedKeys[i].tangentMode = 0;
            }
            for (int i = 0; i < fixedKeys.Length; i++) c.MoveKey(i, fixedKeys[i]);
            return c;
        }

        private AnimationCurve FloatCurveToAnimationCurve(FloatCurve floatCurve)
        {
            if (floatCurve == null) return null;
            try
            {
                var type = floatCurve.GetType();
                var bindingFlags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

                foreach (string name in new[] { "fCurve", "curve", "_curve", "animCurve", "animationCurve" })
                {
                    var field = type.GetField(name, bindingFlags);
                    if (field != null && field.FieldType == typeof(AnimationCurve))
                    {
                        var val = field.GetValue(floatCurve) as AnimationCurve;
                        if (val != null && val.keys.Length >= 2) return val;
                    }
                    var prop = type.GetProperty(name, bindingFlags);
                    if (prop != null && prop.PropertyType == typeof(AnimationCurve))
                    {
                        var val = prop.GetValue(floatCurve) as AnimationCurve;
                        if (val != null && val.keys.Length >= 2) return val;
                    }
                }

                foreach (var field in type.GetFields(bindingFlags))
                {
                    if (field.FieldType != typeof(AnimationCurve)) continue;
                    var val = field.GetValue(floatCurve) as AnimationCurve;
                    if (val != null && val.keys.Length >= 2) return val;
                }
                foreach (var prop in type.GetProperties(bindingFlags))
                {
                    if (prop.PropertyType != typeof(AnimationCurve)) continue;
                    var val = prop.GetValue(floatCurve) as AnimationCurve;
                    if (val != null && val.keys.Length >= 2) return val;
                }

                ConfigNode node = new ConfigNode("FloatCurve");
                floatCurve.Save(node);
                var parsed = ParseThrustCurveFromConfigNode(node);
                if (parsed != null && parsed.keys.Length >= 2) return parsed;
            }
            catch { }
            return null;
        }

        private float GetFieldValue(object obj, string fieldName)
        {
            try
            {
                var field = obj.GetType().GetField(fieldName);
                if (field != null) return (float)field.GetValue(obj);
                
                var property = obj.GetType().GetProperty(fieldName);
                if (property != null) return (float)property.GetValue(obj);
            }
            catch { }
            
            return 0f;
        }

        private float GetBurnTime(EnginePartGroup engine)
        {
            float burnTime = 0f;
            
            try
            {
                var engineMod = FindEngineModule(engine.part);
                if (engineMod != null && engineMod.maxFuelFlow > 0)
                {
                    float solidFuelMass = 0f;
                    
                    foreach (var prop in engineMod.propellants)
                    {
                        if (prop != null && prop.name != null &&
                            (prop.name == "SolidFuel" || prop.name.ToLower().Contains("solid")))
                        {
                            float maxAmount = GetFieldValue(prop, "maxAmount");
                            
                            if (maxAmount <= 0)
                            {
                                foreach (PartResource res in engine.part.partPrefab.Resources)
                                {
                                    if (res != null && res.resourceName != null && 
                                        res.resourceName.Equals(prop.name, StringComparison.OrdinalIgnoreCase))
                                    {
                                        maxAmount = (float)res.maxAmount;
                                        break;
                                    }
                                }
                            }
                            
                            if (maxAmount > 0)
                            {
                                var resDef = PartResourceLibrary.Instance.GetDefinition(prop.name);
                                if (resDef != null)
                                {
                                    solidFuelMass = maxAmount * resDef.density;
                                    break;
                                }
                            }
                        }
                    }
                    
                    if (solidFuelMass > 0)
                    {
                        burnTime = solidFuelMass / engineMod.maxFuelFlow;
                    }
                }
            }
            catch (Exception ex) 
            {
                Debug.LogError($"EngineAnalyzer: GetBurnTime error: {ex.Message}");
            }
            
            return burnTime > 0 ? burnTime : 150f;
        }

        private void DrawThrustCurve(Rect rect, AnimationCurve curve, float maxThrust, float burnTime = 150f, float curveTimeMax = 1f, Rect winRect = default(Rect))
        {
            if (Event.current.type != EventType.Repaint) return;
            if (curve == null || curve.keys.Length < 2) return;
            if (rect.width < 2 || rect.height < 2) return;

            if (_glMaterial == null)
                _glMaterial = new Material(Shader.Find("Hidden/Internal-Colored"));

            int resolution = 100;

            float curveMax = 0.001f;
            for (int i = 0; i <= resolution; i++)
            {
                float v = curve.Evaluate((1f - (float)i / resolution) * curveTimeMax);
                if (!float.IsNaN(v) && !float.IsInfinity(v) && v > curveMax) curveMax = v;
            }

            float ox = winRect.x + rect.x;
            float oy = winRect.y + rect.y;
            float w = rect.width, h = rect.height;
            float sh = Screen.height;

            GL.PushMatrix();
            _glMaterial.SetPass(0);
            GL.LoadPixelMatrix();

            GL.Begin(GL.TRIANGLE_STRIP);
            GL.Color(new Color(0.15f, 0.55f, 0.15f, 0.25f));
            for (int i = 0; i <= resolution; i++)
            {
                float t = (float)i / resolution;
                float cv = curve.Evaluate((1f - t) * curveTimeMax);
                if (float.IsNaN(cv) || float.IsInfinity(cv)) cv = 0f;
                float nv = Mathf.Clamp01(cv / curveMax);
                float sx = ox + w * t;
                float syCurve = oy + h * (1f - nv);
                float syBase = oy + h;
                if (float.IsNaN(sx) || float.IsNaN(syCurve)) continue;
                GL.Vertex3(sx, sh - syCurve, 0f);
                GL.Vertex3(sx, sh - syBase, 0f);
            }
            GL.End();

            GL.Begin(GL.LINES);
            GL.Color(new Color(0.2f, 0.2f, 0.2f, 0.25f));
            int gxDivs = 5;
            for (int i = 0; i <= gxDivs; i++)
            {
                float sx = ox + w * ((float)i / gxDivs);
                GL.Vertex3(sx, sh - oy, 0f);
                GL.Vertex3(sx, sh - (oy + h), 0f);
            }
            int gyDivs = 4;
            for (int i = 0; i <= gyDivs; i++)
            {
                float sy = oy + h * ((float)i / gyDivs);
                GL.Vertex3(ox, sh - sy, 0f);
                GL.Vertex3(ox + w, sh - sy, 0f);
            }
            GL.End();

            GL.Begin(GL.LINES);
            GL.Color(new Color(0.55f, 0.12f, 0.12f, 0.7f));
            float baseY = sh - (oy + h);
            GL.Vertex3(ox, baseY, 0f);
            GL.Vertex3(ox + w, baseY, 0f);
            GL.End();

            GL.Begin(GL.LINE_STRIP);
            GL.Color(new Color(0.35f, 0.85f, 0.35f, 1f));
            for (int i = 0; i <= resolution; i++)
            {
                float t = (float)i / resolution;
                float cv = curve.Evaluate((1f - t) * curveTimeMax);
                if (float.IsNaN(cv) || float.IsInfinity(cv)) cv = 0f;
                float nv = Mathf.Clamp01(cv / curveMax);
                float sx = ox + w * t;
                float sy = oy + h * (1f - nv);
                if (float.IsNaN(sx) || float.IsNaN(sy)) continue;
                GL.Vertex3(sx, sh - sy, 0f);
            }
            GL.End();

            GL.PopMatrix();
        }

        private string GetFuelTypeName(FuelType type)
        {
            switch (type)
            {
                case FuelType.LOXKerosene: return L("液氧煤油", "LOX/Kerosene");
                case FuelType.LOXH2: return L("液氧液氢", "LOX/LH2");
                case FuelType.LOXMethane: return L("液氧甲烷", "LOX/Methane");
                case FuelType.Hypergolic: return L("自燃推进剂", "Hypergolic");
                case FuelType.SolidFuel: return L("固体燃料", "Solid Fuel");
                case FuelType.Monopropellant: return L("单组元", "Monopropellant");
                case FuelType.Airbreathing: return L("吸气式", "Airbreathing");
                case FuelType.Xenon: return L("氙气", "Xenon");
                case FuelType.Electric: return L("电力", "Electric");
                case FuelType.Other: return L("其他", "Other");
                default: return L("全部", "All");
            }
        }

        public void OnHotLoad(MonoBehaviour old)
        {
            EngineAnalyzerWindow oldWindow = old as EngineAnalyzerWindow;
            if (oldWindow != null)
            {
                this.isVisible = oldWindow.isVisible;
                this.windowRect = oldWindow.windowRect;
                this.scrollPosition = oldWindow.scrollPosition;
                this.isVacMode = oldWindow.isVacMode;
                this.isSmartMode = oldWindow.isSmartMode;
                this.isCompactMode = oldWindow.isCompactMode;
                this.targetVolumeKL = oldWindow.targetVolumeKL;
                this.targetVolumeInput = oldWindow.targetVolumeInput;
                this.engineCount = oldWindow.engineCount;
                this.ispLimit = oldWindow.ispLimit;
                this.twrFilterLimit = oldWindow.twrFilterLimit;
                this.twrMinLimit = oldWindow.twrMinLimit;
                this.searchFilter = oldWindow.searchFilter;
                this.currentSortMode = oldWindow.currentSortMode;
                this.currentSizeFilter = oldWindow.currentSizeFilter;
                this.showRockets = oldWindow.showRockets;
                this.showJets = oldWindow.showJets;
                this.showSRB = oldWindow.showSRB;
                this.onlyResearched = oldWindow.onlyResearched;
                this.filterDeprecated = oldWindow.filterDeprecated;
                this.filterNonRO = oldWindow.filterNonRO;
                this.isEnglish = oldWindow.isEnglish;
                
                RefreshData();
            }
        }

        public void OnHotUnload(MonoBehaviour newBehaviour)
        {
        }

        static void OnHotLoad(System.Reflection.Assembly old, System.Reflection.Assembly @new)
        {
        }

        static void OnHotUnload(System.Reflection.Assembly old, System.Reflection.Assembly @new)
        {
        }
    }

    // ── BetterSRB 药柱数据 ──
    public class BetterSRBGrain
    {
        public string displayName;
        public AnimationCurve curve;
        public float thrustMultiplier = 1f;
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
        public FuelType fuelType;
        public float baseThrust;
        public float baseIspVac;
        public float baseIspASL;
        public string manufacturer;
        public AnimationCurve thrustCurve;
        public float totalImpulse;
        public float avgThrust;
    }
    public class EngineConfig
    {
        public string configName, burnTimeDisplay, fuelInfo, ullageInfo, ignitionsDisplay;
        public float deltaV, twr, ispVac, ispASL, price, displayWetMass, displayVolume;
        public bool meetsSmartCriteria, needsHighPressure;
        public AnimationCurve thrustCurve;
        public float totalImpulse;
        public float avgThrust;
        public float burnTime;
    }
}