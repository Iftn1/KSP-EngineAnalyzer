using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using KSP.Localization;
using System.Reflection;

namespace KSP_EngineAnalyzer
{
    public enum SortMode { DeltaV, TWR, Isp, Value }
    public enum SizeFilter { All, Size0625, Size125, Size25, Size375, Size01875, Size5, Surface }
    public enum FuelType { All, LOXKerosene, LOXH2, LOXMethane, Hypergolic, SolidFuel, Monopropellant, Airbreathing, Xenon, Electric, Other }

    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public class EngineAnalyzerWindow : MonoBehaviour
    {
        private Rect windowRect = new Rect(100, 100, 850, 680);
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

        private string lockID = "EngineAnalyzerLock";
        private bool isCompactMode = false;

        public SizeFilter currentSizeFilter = SizeFilter.All;

        public FuelType currentFuelFilter = FuelType.All;

        private EnginePartGroup selectedEngine = null;
        private int selectedConfigIndex = 0;
        private bool showDetailPanel = false;
        private bool _detailPanelJustOpened = false;
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

        private string L(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return tag;
            if (tag.StartsWith("#"))
            {
                string result = KSP.Localization.Localizer.GetStringByTag(tag);
                if (!string.IsNullOrEmpty(result) && result != tag)
                    return result;
            }
            return tag;
        }

        private string L(string zh, string en)
        {
            if (zh.StartsWith("#"))
            {
                string result = KSP.Localization.Localizer.GetStringByTag(zh);
                if (!string.IsNullOrEmpty(result) && result != zh)
                    return result;
            }
            return zh;
        }

        private float AutoToggleWidth(string text)
        {
            return GUI.skin.toggle.CalcSize(new GUIContent(text)).x + 32f;
        }

        private float AutoToggleWidthWide(string text)
        {
            return GUI.skin.toggle.CalcSize(new GUIContent(text)).x * 3f;
        }

        private float AutoButtonWidth(string text)
        {
            return GUI.skin.button.CalcSize(new GUIContent(text)).x + 20f;
        }

        private void Awake()
        {
            LoadSettings();
            if (windowRect.width < 10) { windowRect.width = 850f; windowRect.height = 680f; }
            windowRect.width = isCompactMode ? 450f : 850f;
        }

        private void LoadSettings()
        {
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
            engineCount = PlayerPrefs.GetInt("EA_engineCount", 1);
        }

        private void SaveSettings()
        {
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
            PlayerPrefs.SetInt("EA_engineCount", engineCount);
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
            string titleLower = ap.title != null ? ap.title.ToLower() : "";
            string nameLower = ap.name != null ? ap.name.ToLower() : "";

            if (IsSurfaceAttached(ap))
                return 0f;

            string profiles = ap.bulkheadProfiles != null
                ? ap.bulkheadProfiles.ToLower() : "";

            if (profiles.Contains("size1p5") || profiles.Contains("size1.5"))
                return 1.875f;
            if (profiles.Contains("size0")) return 0.625f;
            if (profiles.Contains("size1")) return 1.25f;
            if (profiles.Contains("size2")) return 2.5f;
            if (profiles.Contains("size3")) return 3.75f;
            if (profiles.Contains("size4")) return 5.0f;

            if (titleLower.Contains("1.875m") || nameLower.Contains("1875"))
                return 1.875f;

            int maxNodeSize = -1;
            if (ap.partPrefab != null && ap.partPrefab.attachNodes != null && ap.partPrefab.attachNodes.Count > 0)
            {
                foreach (var node in ap.partPrefab.attachNodes)
                {
                    try
                    {
                        if (node.nodeType == AttachNode.NodeType.Stack && node.size > maxNodeSize)
                            maxNodeSize = node.size;
                    }
                    catch { if (node.size > maxNodeSize) maxNodeSize = node.size; }
                }
            }

            if (maxNodeSize >= 4 || titleLower.Contains("5m") || nameLower.Contains("5m"))
                return 5.0f;
            if (maxNodeSize == 3 || titleLower.Contains("3.75m") || nameLower.Contains("375"))
                return 3.75f;
            if (maxNodeSize == 2 || titleLower.Contains("2.5m") || nameLower.Contains("25"))
                return 2.5f;
            if (maxNodeSize == 1 || titleLower.Contains("1.25m") || nameLower.Contains("125"))
                return 1.25f;
            if (maxNodeSize == 0 || titleLower.Contains("0.625m") || nameLower.Contains("0625"))
                return 0.625f;

            return 1.25f;
        }

        private bool IsSurfaceAttached(AvailablePart ap)
        {
            if (ap?.partPrefab == null) return false;

            bool hasStackNode = false;
            bool hasSurfaceNode = false;

            if (ap.partPrefab.attachNodes != null && ap.partPrefab.attachNodes.Count > 0)
            {
                foreach (var node in ap.partPrefab.attachNodes)
                {
                    try
                    {
                        if (node.nodeType == AttachNode.NodeType.Surface)
                            hasSurfaceNode = true;
                        else if (node.nodeType == AttachNode.NodeType.Stack && node.size >= 0)
                            hasStackNode = true;
                    }
                    catch
                    {
                        string nid = node.id != null ? node.id.ToLower() : "";
                        if (nid.Contains("attach") || nid.Contains("surface") || nid.Contains("srf"))
                            hasSurfaceNode = true;
                        else
                            hasStackNode = true;
                    }
                }
            }

            if (hasSurfaceNode && !hasStackNode) return true;

            string titleLower = ap.title != null ? ap.title.ToLower() : "";
            string nameLower = ap.name != null ? ap.name.ToLower() : "";
            if (titleLower.Contains("radial") || titleLower.Contains("srf") ||
                nameLower.Contains("radial") || nameLower.Contains("srf"))
                return true;

            return false;
        }

        private string GetEngineSize(AvailablePart ap)
        {
            if (IsSurfaceAttached(ap)) return "SRF";
            float size = GetPartSize(ap);
            if (Math.Abs(size - 0.625f) < 0.1f) return "0.625m";
            if (Math.Abs(size - 1.25f) < 0.1f) return "1.25m";
            if (Math.Abs(size - 1.875f) < 0.1f) return "1.875m";
            if (Math.Abs(size - 2.5f) < 0.1f) return "2.5m";
            if (Math.Abs(size - 3.75f) < 0.1f) return "3.75m";
            if (Math.Abs(size - 5.0f) < 0.1f) return "5m";
            return size.ToString("F3") + "m";
        }

        private SizeFilter GetEngineSizeFilter(AvailablePart ap)
        {
            if (IsSurfaceAttached(ap)) return SizeFilter.Surface;
            float size = GetPartSize(ap);
            if (Math.Abs(size - 0.625f) < 0.1f) return SizeFilter.Size0625;
            if (Math.Abs(size - 1.25f) < 0.1f) return SizeFilter.Size125;
            if (Math.Abs(size - 1.875f) < 0.1f) return SizeFilter.Size01875;
            if (Math.Abs(size - 2.5f) < 0.1f) return SizeFilter.Size25;
            if (Math.Abs(size - 3.75f) < 0.1f) return SizeFilter.Size375;
            if (Math.Abs(size - 5.0f) < 0.1f) return SizeFilter.Size5;
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

            var rawKeys = new List<Keyframe>();
            var rawHasTan = new List<bool>();
            foreach (var kv in keyValues)
            {
                var parts = kv.Split(new char[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                if (!float.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float time)) continue;
                if (!float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float value)) continue;

                Keyframe kf;
                bool hasTan = false;
                if (parts.Length >= 4 &&
                    float.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float inTan) &&
                    float.TryParse(parts[3], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float outTan))
                {
                    kf = new Keyframe(time, value, inTan, outTan);
                    hasTan = true;
                }
                else
                {
                    kf = new Keyframe(time, value);
                }
                rawKeys.Add(kf);
                rawHasTan.Add(hasTan);
            }

            if (rawKeys.Count == 0) return null;

            var sorted = rawKeys.Select((k, i) => new { kf = k, idx = i })
                                .OrderBy(x => x.kf.time)
                                .ToList();

            var keys = sorted.Select(x => x.kf).ToArray();
            var hasTanFlags = sorted.Select(x => rawHasTan[x.idx]).ToArray();

            var curve = new AnimationCurve(keys);
            var fixedKeys = curve.keys;
            for (int i = 0; i < fixedKeys.Length; i++)
            {
                fixedKeys[i].inTangent = keys[i].inTangent;
                fixedKeys[i].outTangent = keys[i].outTangent;
                fixedKeys[i].tangentMode = hasTanFlags[i] ? 1 : 0;
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
                if (IsSolidRocketPart(p)) continue;
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

        private bool IsSolidRocketPart(Part p)
        {
            if (p?.partInfo?.partPrefab == null) return false;
            var eng = p.partInfo.partPrefab.FindModuleImplementing<ModuleEngines>();
            if (eng == null) return false;
            foreach (var prop in eng.propellants)
            {
                string pn = prop.name;
                if (solidPropellants.Any(s => pn == s || pn.ToLower().Contains(s.ToLower())))
                    return true;
            }
            return false;
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

                if (!isSRB)
                {
                    foreach (var moduleNode in ap.partConfig?.GetNodes("MODULE") ?? new ConfigNode[0])
                    {
                        string mn = moduleNode.GetValue("name");
                        if (mn == null || (!mn.Contains("EngineConfigs") && !mn.Contains("RealEngine") && !mn.Contains("EnginesRF"))) continue;
                        foreach (ConfigNode cfg in moduleNode.GetNodes("CONFIG"))
                        {
                            foreach (ConfigNode propNode in cfg.GetNodes("PROPELLANT"))
                            {
                                string pn = propNode.GetValue("name") ?? "";
                                if (solidPropellants.Any(f => pn == f || pn.ToLower().Contains(f.ToLower())))
                                {
                                    isSRB = true;
                                    goto endSolidCheck;
                                }
                            }
                        }
                    }
                endSolidCheck:;
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
                        baseFuels.Add(def?.displayName ?? p.name);
                    }
                }

                float baseIspVac = engineMod.atmosphereCurve.Evaluate(0f);
                float baseIspASL = engineMod.atmosphereCurve.Evaluate(1f);
                float baseMaxThrust = engineMod.maxThrust;

                string baseBurnTime = L("#engineAnalyzer_Inf"), baseIgnitions = L("#engineAnalyzer_InfIgnitions"), baseUllage = L("#engineAnalyzer_NoUllage");
                foreach (PartModule pm in ap.partPrefab.Modules)
                {
                    if (pm.moduleName.Contains("ModuleEngineConfigs") || pm.moduleName.Contains("ModuleRealEngine") || pm.moduleName.Contains("ModuleEnginesRF"))
                    {
                        var fields = pm.Fields.Cast<BaseField>().ToList();
                        var fIgn = fields.FirstOrDefault(f => f.name.ToLower().Contains("ignition"));
                        var fUll = fields.FirstOrDefault(f => f.name.ToLower().Contains("ullage"));
                        var fBrn = fields.FirstOrDefault(f => f.name.ToLower().Contains("burn"));
                        if (fIgn != null && int.TryParse(fIgn.GetValue(pm).ToString(), out int c) && c >= 0 && c < 100) baseIgnitions = c + L("#engineAnalyzer_Ignitions");
                        if (fUll != null && fUll.GetValue(pm).ToString().ToLower() == "true") baseUllage = L("#engineAnalyzer_NeedUllage");
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
                    isSurface = IsSurfaceAttached(ap),
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
                ConfigNode mecNode = ap.partConfig?.GetNodes("MODULE").FirstOrDefault(n => { string mn = n.GetValue("name"); return mn != null && (mn.Contains("EngineConfigs") || mn.Contains("RealEngine") || mn.Contains("EnginesRF")); });

                if (isSRB)
                {
                    var testCfgNodes = mecNode?.GetNodes("CONFIG");
                    Debug.Log($"[EngineAnalyzer] SRB PART={ap.title} mecNode={mecNode != null} mecNode.HasNode(CONFIG)={testCfgNodes?.Length ?? -1}");
                    if (testCfgNodes != null)
                    {
                        for (int di = 0; di < testCfgNodes.Length; di++)
                        {
                            var cn = testCfgNodes[di];
                            Debug.Log($"[EngineAnalyzer]   CONFIG[{di}] name={cn.GetValue("name")} hasThrustCurve={cn.HasNode("thrustCurve")} hasAtmCurve={cn.HasNode("atmosphereCurve")}");
                        }
                    }
                }

                ConfigNode[] allConfigs = null;
                if (mecNode != null && mecNode.HasNode("CONFIG"))
                {
                    allConfigs = mecNode.GetNodes("CONFIG");
                }
                else if (isSRB)
                {
                    PartModule mecPartModule = null;
                    foreach (PartModule pm in ap.partPrefab.Modules)
                    {
                        if (pm.moduleName == "ModuleEngineConfigs") { mecPartModule = pm; break; }
                    }

                    if (mecPartModule != null)
                    {
                        var mecType = mecPartModule.GetType();
                        var configsField = mecType.GetField("configs", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                        if (configsField != null)
                        {
                            var rawConfigs = configsField.GetValue(mecPartModule);
                            if (rawConfigs is ConfigNode[] cfgArray && cfgArray.Length > 0)
                            {
                                allConfigs = cfgArray;
                                Debug.Log($"[EngineAnalyzer] PART={ap.title} got {allConfigs.Length} CONFIGs via reflection (configs array)");
                            }
                            else if (rawConfigs is System.Collections.Generic.List<ConfigNode> cfgList && cfgList.Count > 0)
                            {
                                allConfigs = cfgList.ToArray();
                                Debug.Log($"[EngineAnalyzer] PART={ap.title} got {allConfigs.Length} CONFIGs via reflection (configs list)");
                            }
                        }

                        if (allConfigs == null)
                        {
                            foreach (var f in mecType.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                            {
                                bool isConfigArray = f.FieldType == typeof(ConfigNode[]);
                                bool isConfigList = f.FieldType == typeof(System.Collections.Generic.List<ConfigNode>);
                                if ((isConfigArray || isConfigList) && f.Name.ToLower().Contains("config"))
                                {
                                    if (isConfigArray)
                                    {
                                        var arr = f.GetValue(mecPartModule) as ConfigNode[];
                                        if (arr != null && arr.Length > 0) { allConfigs = arr; }
                                    }
                                    else
                                    {
                                        var list = f.GetValue(mecPartModule) as System.Collections.Generic.List<ConfigNode>;
                                        if (list != null && list.Count > 0) { allConfigs = list.ToArray(); }
                                    }
                                    if (allConfigs != null)
                                    {
                                        Debug.Log($"[EngineAnalyzer] PART={ap.title} got {allConfigs.Length} CONFIGs via reflection (field={f.Name})");
                                        break;
                                    }
                                }
                            }
                        }

                        if (allConfigs == null)
                        {
                            Debug.Log($"[EngineAnalyzer] PART={ap.title} ModuleEngineConfigs found but no ConfigNode[] field detected");
                            var mecType2 = mecPartModule.GetType();
                            foreach (var f in mecType2.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                            {
                                Debug.Log($"[EngineAnalyzer]   field: {f.Name} = {f.FieldType}");
                            }
                        }
                    }
                    else
                    {
                        Debug.Log($"[EngineAnalyzer] PART={ap.title} no ModuleEngineConfigs in partPrefab.Modules");
                    }
                }

                if (allConfigs != null && allConfigs.Length > 0)
                {
                    if (mecNode != null && !mecNode.HasNode("CONFIG"))
                        Debug.Log($"[EngineAnalyzer] PART={ap.title} CONFIG from GameDB: {allConfigs.Length} configs");
                    foreach (ConfigNode cfg in allConfigs)
                    {
                        string cfgName = cfg.GetValue("name") ?? L("#engineAnalyzer_UnknownModel");
                        Debug.Log($"[EngineAnalyzer] Processing CONFIG cfgName={cfgName}");

                        bool isHP = false;
                        if (cfg.HasValue("type") && (cfg.GetValue("type").Contains("TankService") || cfg.GetValue("type").Contains("Pressure-fed"))) isHP = true;
                        if (cfg.HasValue("pressureFed") && cfg.GetValue("pressureFed").ToLower() == "true") isHP = true;
                        if (cfgName.IndexOf("Pressure-fed", StringComparison.OrdinalIgnoreCase) >= 0 || cfgName.IndexOf("Pressure Fed", StringComparison.OrdinalIgnoreCase) >= 0) isHP = true;

                        float cfgT = baseMaxThrust; if (cfg.HasValue("maxThrust")) float.TryParse(cfg.GetValue("maxThrust"), out cfgT);
                        float solidFuelMass = 0f;

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
                            ConfigNode targetCurveNode = null;
                            ConfigNode resolvedEngineCfg = null;

                            targetCurveNode = cfg.GetNode("thrustCurve");

                            if (targetCurveNode == null)
                            {
                                foreach (ConfigNode subNode in cfg.GetNodes())
                                {
                                    if (subNode.name.ToUpper().Contains("THRUST") && subNode.name.ToUpper().Contains("CURVE"))
                                    {
                                        targetCurveNode = subNode;
                                        break;
                                    }
                                }
                            }

                            if (targetCurveNode == null)
                            {
                                int totalParts = 0; int matchedParts = 0;
                                foreach (UrlDir.UrlConfig urlCfg in GameDatabase.Instance.GetConfigs("PART"))
                                {
                                    totalParts++;
                                    if (urlCfg.config.HasValue("engineType") && urlCfg.config.GetValue("engineType") == cfgName)
                                    {
                                        matchedParts++;
                                        ConfigNode mec = urlCfg.config.GetNode("MODULE", "name", "ModuleEngineConfigs");
                                        if (mec != null)
                                        {
                                            ConfigNode subCfg = mec.GetNode("CONFIG", "name", cfgName);
                                            if (subCfg != null)
                                            {
                                                resolvedEngineCfg = subCfg;
                                                targetCurveNode = subCfg.GetNode("thrustCurve");
                                                break;
                                            }
                                        }
                                    }
                                }
                                Debug.Log($"[EngineAnalyzer] GameDB scan cfgName={cfgName}: scanned={totalParts} matched={matchedParts} found={targetCurveNode != null}");
                            }

                            if (resolvedEngineCfg != null)
                            {
                                if (resolvedEngineCfg.HasValue("maxThrust")) float.TryParse(resolvedEngineCfg.GetValue("maxThrust"), out cfgT);

                                ConfigNode resolvedCurve = resolvedEngineCfg.GetNode("atmosphereCurve");
                                if (resolvedCurve != null)
                                {
                                    foreach (string v in resolvedCurve.GetValues("key"))
                                    {
                                        string[] s = v.Split(new char[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                                        if (s.Length >= 2 && float.TryParse(s[0], out float k) && float.TryParse(s[1], out float val))
                                        {
                                            if (k < 0.1f) cfgIV = val; if (Math.Abs(k - 1f) < 0.1f) cfgIA = val;
                                        }
                                    }
                                }
                            }

                            if (targetCurveNode != null)
                            {
                                cfgThrustCurve = ParseThrustCurveFromConfigNode(targetCurveNode);
                            }
                            else
                            {
                                cfgThrustCurve = TryGetBetterSRBCurveForConfig(ap, cfgName);
                            }

                            if (cfgThrustCurve == null) cfgThrustCurve = thrustCurve;
                            if (cfgThrustCurve == null) cfgThrustCurve = new AnimationCurve(new Keyframe(0, 1f), new Keyframe(1, 1f));

                            Debug.Log($"[EngineAnalyzer] cfg={cfgName} hasCurve={cfgThrustCurve != null} keys={cfgThrustCurve.keys.Length} curve[0]=({cfgThrustCurve.keys[0].time},{cfgThrustCurve.keys[0].value}) resolvedEngineCfg={resolvedEngineCfg != null}");

                            ConfigNode resCfg = resolvedEngineCfg ?? cfg;
                            solidFuelMass = 0f;
                            if (resCfg.HasNode("RESOURCE"))
                            {
                                foreach (var resNode in resCfg.GetNodes("RESOURCE"))
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

                            if (solidFuelMass <= 0)
                            {
                                foreach (var modNode in ap.partConfig?.GetNodes("MODULE") ?? new ConfigNode[0])
                                {
                                    string mn = modNode.GetValue("name");
                                    if (mn != "ModuleFuelTanks" && mn != "ModuleTankService") continue;
                                    float tankVol = 0f;
                                    if (modNode.HasValue("volume")) float.TryParse(modNode.GetValue("volume"), out tankVol);
                                    foreach (ConfigNode tankNode in modNode.GetNodes("TANK"))
                                    {
                                        string tn = tankNode.GetValue("name") ?? "";
                                        if (!solidPropellants.Any(f => tn == f || tn.ToLower().Contains(f.ToLower()))) continue;
                                        float tankAmt = 0f;
                                        if (tankNode.HasValue("amount")) float.TryParse(tankNode.GetValue("amount"), out tankAmt);
                                        if (tankAmt <= 0 && tankVol > 0) tankAmt = tankVol;
                                        var def = PartResourceLibrary.Instance.GetDefinition(tn);
                                        if (def != null) solidFuelMass += tankAmt * def.density;
                                    }
                                }
                            }

                            float fuelFlow = (cfgT > 0 && cfgIV > 0) ? cfgT / (cfgIV * 9.80665f) : engineMod.maxFuelFlow;
                            float explicitFuelFlow = 0f;
                            ConfigNode fuelCfg = resolvedEngineCfg ?? cfg;
                            if (fuelCfg.HasValue("maxFuelFlow") && float.TryParse(fuelCfg.GetValue("maxFuelFlow"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out explicitFuelFlow) && explicitFuelFlow > 0)
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

                        string configBurnTimeStr = isSRB ? cfgBurnTime.ToString("F0") + "s" : baseBurnTime;
                        var config = CalculateConfig(cfgName, cfgT, cfgIV, cfgIA, baseMixDensity, baseFuels, baseUllage, baseIgnitions, configBurnTimeStr, ap, engineCount, isHP, isSRB ? cfgAvgThrust : 0f, isSRB ? solidFuelMass : -1f);
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
                            var config = CalculateConfig(L("#engineAnalyzer_Standard"), baseMaxThrust, baseIspVac, baseIspASL, baseMixDensity, baseFuels, baseUllage, baseIgnitions, baseBurnTime, ap, engineCount, false, useAvgThrust);
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
                        var config = CalculateConfig(L("#engineAnalyzer_Standard"), baseMaxThrust, baseIspVac, baseIspASL, baseMixDensity, baseFuels, baseUllage, baseIgnitions, baseBurnTime, ap, engineCount, false);
                         group.configs.Add(config);
                    }
                }
                allGroupsCache.Add(group);
            }
            ApplyFilters();
        }

        private EngineConfig CalculateConfig(string name, float T, float IV, float IA, float dens, List<string> f, string ull, string ign, string bt, AvailablePart ap, int cnt, bool isHP, float avgThrust = 0f, float preFuelMass = -1f)
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
                thrust = effectiveT * (IA / Math.Max(IV, 0.01f)) * cnt;
            }

            float dry = vesselMassAtSync + (ap.partPrefab.mass * cnt);
            float dV = 0, twr = 0, vol = 0, wet = 0;
            bool meet = true;

            var engineModule = FindEngineModule(ap);
            if (preFuelMass >= 0f)
            {
                float fMass = preFuelMass;
                wet = dry + (fMass * cnt);
                vol = (fMass * cnt / dens) / 1000f;
                if (dry > 0) dV = isp * g0 * Mathf.Log(wet / dry);
                twr = thrust / (wet * g0);
            }
            else if (f.Any(fuel => solidPropellants.Any(sp => fuel == sp || fuel.ToLower().Contains(sp.ToLower()))) || (engineModule != null && engineModule.throttleLocked))
            {
                float fMass = 0f;
                foreach (PartResource res in ap.partPrefab.Resources)
                {
                    if (res == null || res.resourceName == null) continue;
                    string rn = res.resourceName;
                    if (solidPropellants.Any(sp => rn == sp || rn.ToLower().Contains(sp.ToLower())))
                    {
                        var resDef = PartResourceLibrary.Instance.GetDefinition(rn);
                        if (resDef != null)
                            fMass += (float)(res.maxAmount * resDef.density);
                    }
                }
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

            return new EngineConfig { configName = name, deltaV = dV, twr = twr, ispVac = IV, ispASL = IA, price = ap.cost * cnt, displayWetMass = wet, displayVolume = vol, fuelInfo = string.Join("/", f), ullageInfo = ull, ignitionsDisplay = ign, burnTimeDisplay = bt, meetsSmartCriteria = meet, needsHighPressure = isHP, maxThrust = T * cnt };
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

                var fg = new EnginePartGroup { part = g.part, partTitle = g.partTitle, isJet = g.isJet, isSRB = g.isSRB, isSurface = g.isSurface, configs = new List<EngineConfig>(), isHidden = g.isHidden, engineSize = g.engineSize, sizeFilter = g.sizeFilter, fuelType = g.fuelType, baseThrust = g.baseThrust, baseIspVac = g.baseIspVac, baseIspASL = g.baseIspASL, manufacturer = g.manufacturer, thrustCurve = g.thrustCurve, totalImpulse = g.totalImpulse, avgThrust = g.avgThrust };
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
            windowRect = GUILayout.Window(888, windowRect, DrawWindow, L("#engineAnalyzer_Title"));

            if (showDetailPanel && selectedEngine != null)
            {
                if (_detailPanelJustOpened)
                {
                    detailWindowRect.width = isCompactMode ? 450f : 850f;
                    detailWindowRect.height = isCompactMode ? 800f : 950f;
                    detailWindowRect.x = windowRect.x + windowRect.width + 20f;
                    detailWindowRect.y = windowRect.y;
                    _detailPanelJustOpened = false;
                }

                detailWindowRect = GUILayout.Window(12345, detailWindowRect, DrawDetailPanel, L("#engineAnalyzer_EngineDetails"));
            }
        }

        private void DrawWindow(int id)
        {
            float panelWidth = isCompactMode ? 450f : 850f;

            GUILayout.BeginVertical(GUILayout.Width(panelWidth));

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(isVacMode ? L("#engineAnalyzer_VacuumMode") : L("#engineAnalyzer_SeaLevelMode"))) { isVacMode = !isVacMode; RefreshData(); }
            if (GUILayout.Button(isSmartMode ? L("#engineAnalyzer_ReverseMode") : L("#engineAnalyzer_NormalMode"))) { isSmartMode = !isSmartMode; RefreshData(); }
            if (GUILayout.Button(isCompactMode ? L("#engineAnalyzer_CompactMode") : L("#engineAnalyzer_NormalLayout"))) { isCompactMode = !isCompactMode; windowRect.width = isCompactMode ? 450f : 850f; windowRect.height = 680f; if (showDetailPanel) { detailWindowRect.width = isCompactMode ? 450f : 850f; detailWindowRect.height = isCompactMode ? 800f : 950f; } }
            GUILayout.EndHorizontal();

            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label($"{L("#engineAnalyzer_SyncMass")}: <color=yellow>{vesselMassAtSync:F2}t</color>", GUILayout.Width(130));
            if (GUILayout.Button(L("#engineAnalyzer_LockCurrentStage")))
            {
                lockedWetMasses.Add(GetShipTotalMass());
                lockedDryMasses.Add(GetShipDryMass());
                lockedVolumes.Add(GetShipTotalVolumeKL());
                SyncCurrentStage();
                RefreshData();
            }
            if (GUILayout.Button(L("#engineAnalyzer_ResetAll"))) { lockedWetMasses.Clear(); lockedDryMasses.Clear(); lockedVolumes.Clear(); vesselMassAtSync = 0; SyncCurrentStage(); RefreshData(); }
            GUILayout.EndHorizontal();

            if (lockedWetMasses.Count > 0)
            {
                showStageHistory = GUILayout.Toggle(showStageHistory, $" {L("#engineAnalyzer_History")} ({lockedWetMasses.Count})");
                if (showStageHistory)
                {
                    for (int i = 0; i < lockedWetMasses.Count; i++)
                    {
                        GUILayout.Label($"  S{i + 1}: {L("#engineAnalyzer_Wet")} {lockedWetMasses[i]:F2}t | {L("#engineAnalyzer_Volume")} {lockedVolumes[i]:F1}kL", GUI.skin.GetStyle("CaptionLabel"));
                    }
                }
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(L("#engineAnalyzer_SyncVAB"))) { SyncCurrentStage(); RefreshData(); }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(L("#engineAnalyzer_VolKL"), GUILayout.Width(60));
            targetVolumeInput = GUILayout.TextField(targetVolumeInput, GUILayout.Width(50));
            if (float.TryParse(targetVolumeInput, out float v) && Math.Abs(v - targetVolumeKL) > 0.01f) { targetVolumeKL = v; RefreshData(); }
            GUILayout.Space(20);
            GUILayout.Label($"{L("#engineAnalyzer_Cluster")}: {engineCount}", GUILayout.Width(50));
            int nc = Mathf.RoundToInt(GUILayout.HorizontalSlider(engineCount, 1, 12)); if (nc != engineCount) { engineCount = nc; RefreshData(); }
            GUILayout.EndHorizontal();

            bool fd = filterDeprecated;
            bool fn = filterNonRO;

            GUILayout.BeginHorizontal();
            string rocketLabel = L("#engineAnalyzer_Rocket");
            string jetLabel = L("#engineAnalyzer_Jet");
            string srbLabel = L("#engineAnalyzer_SRB");
            string researchedLabel = L("#engineAnalyzer_Researched");
            GUIContent rocketContent = new GUIContent(rocketLabel, L("#engineAnalyzer_RocketTooltip"));
            GUIContent jetContent = new GUIContent(jetLabel, L("#engineAnalyzer_JetTooltip"));
            GUIContent srbContent = new GUIContent(srbLabel, L("#engineAnalyzer_SRBTooltip"));
            GUIContent researchedContent = new GUIContent(researchedLabel, L("#engineAnalyzer_ResearchedTooltip"));
            bool r = GUILayout.Toggle(showRockets, rocketContent, GUILayout.Width(AutoToggleWidth(rocketLabel)));
            GUILayout.Space(10);
            bool j = GUILayout.Toggle(showJets, jetContent, GUILayout.Width(AutoToggleWidth(jetLabel)));
            GUILayout.Space(10);
            bool s = GUILayout.Toggle(showSRB, srbContent, GUILayout.Width(AutoToggleWidth(srbLabel)));
            GUILayout.Space(10);
            bool or = GUILayout.Toggle(onlyResearched, researchedContent, GUILayout.Width(AutoToggleWidthWide(researchedLabel)));
            GUILayout.Space(20f);
            string noDepLabelRow = L("#engineAnalyzer_NoDep");
            string onlyROLabelRow = L("#engineAnalyzer_OnlyRO");
            GUIContent noDepContent = new GUIContent(noDepLabelRow, L("#engineAnalyzer_NoDepTooltip"));
            GUIContent onlyRoContent = new GUIContent(onlyROLabelRow, L("#engineAnalyzer_OnlyRoTooltip"));
            fd = GUILayout.Toggle(filterDeprecated, noDepContent, GUILayout.Width(AutoToggleWidthWide(noDepLabelRow)));
            GUILayout.Space(5f);
            fn = GUILayout.Toggle(filterNonRO, onlyRoContent, GUILayout.Width(AutoToggleWidthWide(onlyROLabelRow)));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (!isCompactMode)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(L("#engineAnalyzer_Size"), GUILayout.Width(40));
                string sizeAll = L("#engineAnalyzer_All");
                string sizeSRF = L("#engineAnalyzer_SRF");
                string size0625 = L("#engineAnalyzer_0625");
                string size125 = L("#engineAnalyzer_125");
                string size1875 = L("#engineAnalyzer_1875");
                string size25 = L("#engineAnalyzer_25");
                string size375 = L("#engineAnalyzer_375");
                string size5 = L("#engineAnalyzer_5m");
                if (GUILayout.Button(currentSizeFilter == SizeFilter.All ? L("#engineAnalyzer_AllHighlighted") : sizeAll, GUILayout.Width(AutoButtonWidth(sizeAll)))) { currentSizeFilter = SizeFilter.All; ApplyFilters(); }
                if (GUILayout.Button(currentSizeFilter == SizeFilter.Surface ? L("#engineAnalyzer_SRFHighlighted") : sizeSRF, GUILayout.Width(AutoButtonWidth(sizeSRF)))) { currentSizeFilter = SizeFilter.Surface; ApplyFilters(); }
                if (GUILayout.Button(currentSizeFilter == SizeFilter.Size0625 ? L("#engineAnalyzer_0625Highlighted") : size0625, GUILayout.Width(AutoButtonWidth(size0625)))) { currentSizeFilter = SizeFilter.Size0625; ApplyFilters(); }
                if (GUILayout.Button(currentSizeFilter == SizeFilter.Size125 ? L("#engineAnalyzer_125Highlighted") : size125, GUILayout.Width(AutoButtonWidth(size125)))) { currentSizeFilter = SizeFilter.Size125; ApplyFilters(); }
                if (GUILayout.Button(currentSizeFilter == SizeFilter.Size01875 ? L("#engineAnalyzer_1875Highlighted") : size1875, GUILayout.Width(AutoButtonWidth(size1875)))) { currentSizeFilter = SizeFilter.Size01875; ApplyFilters(); }
                if (GUILayout.Button(currentSizeFilter == SizeFilter.Size25 ? L("#engineAnalyzer_25Highlighted") : size25, GUILayout.Width(AutoButtonWidth(size25)))) { currentSizeFilter = SizeFilter.Size25; ApplyFilters(); }
                if (GUILayout.Button(currentSizeFilter == SizeFilter.Size375 ? L("#engineAnalyzer_375Highlighted") : size375, GUILayout.Width(AutoButtonWidth(size375)))) { currentSizeFilter = SizeFilter.Size375; ApplyFilters(); }
                if (GUILayout.Button(currentSizeFilter == SizeFilter.Size5 ? L("#engineAnalyzer_5mHighlighted") : size5, GUILayout.Width(AutoButtonWidth(size5)))) { currentSizeFilter = SizeFilter.Size5; ApplyFilters(); }
                GUILayout.EndHorizontal();

                // Fuel filter commented out due to instability
                // GUILayout.BeginHorizontal();
                // GUILayout.Label(L("燃料:", "Fuel:"), GUILayout.Width(40));
                // ... fuel filter buttons ...
                // GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label(L("#engineAnalyzer_Sort"), GUILayout.Width(50));
            if (GUILayout.Button(currentSortMode == SortMode.DeltaV ? L("#engineAnalyzer_DeltaVHighlighted") : L("#engineAnalyzer_DeltaV"), GUILayout.Width(50))) { currentSortMode = SortMode.DeltaV; ApplyFilters(); }
            if (GUILayout.Button(currentSortMode == SortMode.TWR ? L("#engineAnalyzer_TWRHighlighted") : L("#engineAnalyzer_TWR"), GUILayout.Width(50))) { currentSortMode = SortMode.TWR; ApplyFilters(); }
            if (GUILayout.Button(currentSortMode == SortMode.Isp ? L("#engineAnalyzer_IspHighlighted") : L("#engineAnalyzer_Isp"), GUILayout.Width(50))) { currentSortMode = SortMode.Isp; ApplyFilters(); }
            if (GUILayout.Button(currentSortMode == SortMode.Value ? L("#engineAnalyzer_ValueHighlighted") : L("#engineAnalyzer_Value"), GUILayout.Width(60))) { currentSortMode = SortMode.Value; ApplyFilters(); }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (r != showRockets || j != showJets || s != showSRB || or != onlyResearched) { showRockets = r; showJets = j; showSRB = s; onlyResearched = or; RefreshData(); }
            if (fd != filterDeprecated || fn != filterNonRO) { filterDeprecated = fd; filterNonRO = fn; ApplyFilters(); }

            GUILayout.BeginHorizontal();
            string ispDisplay = ispLimit >= SCIFI_THRESHOLD ? L("#engineAnalyzer_SciFi") : $"{ispLimit:F0}s";
            GUILayout.Label($"{L("#engineAnalyzer_IspLimit")}: {ispDisplay}", GUILayout.Width(110));
            float newIsp = GUILayout.HorizontalSlider(ispLimit, 100f, 20001f);
            if (Math.Abs(newIsp - ispLimit) > 0.5f) { ispLimit = newIsp; ApplyFilters(); }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            string twrVal = twrFilterLimit >= 20.1f ? L("#engineAnalyzer_None") : twrFilterLimit.ToString("F1");
            GUILayout.Label($"{L("#engineAnalyzer_MaxTWR")}: {twrVal}", GUILayout.Width(110));
            float newTwrLimit = GUILayout.HorizontalSlider(twrFilterLimit, 0.1f, 20.1f);
            if (Math.Abs(newTwrLimit - twrFilterLimit) > 0.05f) { twrFilterLimit = newTwrLimit; ApplyFilters(); }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"{L("#engineAnalyzer_MinTWR")}: {twrMinLimit:F1}", GUILayout.Width(110));
            float newTwrMin = GUILayout.HorizontalSlider(twrMinLimit, 0.0f, 10.0f);
            if (Math.Abs(newTwrMin - twrMinLimit) > 0.05f) { twrMinLimit = newTwrMin; ApplyFilters(); }
            GUILayout.EndHorizontal();

            if (isSmartMode)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(L("#engineAnalyzer_TargetDV"), GUILayout.Width(80));
                targetDVInput = GUILayout.TextField(targetDVInput, GUILayout.Width(80));
                if (float.TryParse(targetDVInput, out float d)) targetDV = d;
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label(L("#engineAnalyzer_Search"), GUILayout.Width(50));
            string newFilter = GUILayout.TextField(searchFilter, isCompactMode ? GUILayout.Width(150) : GUILayout.Width(200));
            if (newFilter != searchFilter) { searchFilter = newFilter; ApplyFilters(); }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical(); // 闭合过滤器框

            scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.Width(panelWidth - 20), GUILayout.Height(360f));
            foreach (var group in filteredGroups)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.BeginHorizontal();
                string typeLabel = group.isJet ? L("#engineAnalyzer_JetTag") : (group.isSRB ? L("#engineAnalyzer_SRBTag") : L("#engineAnalyzer_RocketTag"));
                string hiddenLabel = group.isHidden ? L("#engineAnalyzer_HiddenTag") : "";
                string sizeLabel = $" ({group.engineSize})";
                GUILayout.Label($"<size=15>{typeLabel}<b>{group.partTitle}</b>{hiddenLabel}{sizeLabel}{(engineCount > 1 ? $" x{engineCount}" : "")}</size>");
                if (GUILayout.Button(L("#engineAnalyzer_Detail"), GUILayout.Width(60))) { selectedEngine = group; showDetailPanel = true; _detailPanelJustOpened = true; _grainPresets.Clear(); _selectedGrainIndex = 0; _cachedChartRect = Rect.zero; }
                GUILayout.EndHorizontal();

                for (int i = 0; i < group.configs.Count; i++)
                {
                    var cfg = group.configs[i];
                    GUILayout.BeginVertical(GUI.skin.box);
                    GUILayout.BeginHorizontal();

                    string hpDisplay = cfg.needsHighPressure ? L("#engineAnalyzer_HighPressure") : "";

                    if (isCompactMode)
                    {
                        string impStr = cfg.totalImpulse > 0 ? $" Imp:{cfg.totalImpulse:F0}kN·s" : "";
                        GUILayout.Label($"{hpDisplay}<size=12><color=#E0E0E0>{cfg.configName}</color></size>", GUILayout.Width(160));
                        GUILayout.FlexibleSpace();
                        GUILayout.Label($"Δv:{cfg.deltaV:F0} TWR:{cfg.twr:F2} Isp:{cfg.ispVac:F0}{impStr}");
                        if (GUILayout.Button(L("#engineAnalyzer_Sel"), GUILayout.Width(35))) SpawnAndConfigure(group.part, cfg.configName);
                    }
                    else
                    {
                        GUILayout.Label($"{hpDisplay}<size=13><color=#E0E0E0>▶ {L("#engineAnalyzer_Config")}: {cfg.configName}</color></size>");
                        if (GUILayout.Button(L("#engineAnalyzer_Select"), GUILayout.Width(60))) SpawnAndConfigure(group.part, cfg.configName);
                    }
                    GUILayout.EndHorizontal();

                    GUILayout.Label($"<size=10><color=#999999>{L("#engineAnalyzer_Fuel")}: {cfg.fuelInfo} | {cfg.ullageInfo} | <color=orange>{cfg.ignitionsDisplay}</color> | {L("#engineAnalyzer_Burn")}: {cfg.burnTimeDisplay}</color></size>");

                    if (!isCompactMode)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label($"Δv: <color=lime>{cfg.deltaV:F0}</color>", GUILayout.Width(80));
                        GUILayout.Label($"TWR: {cfg.twr:F2}", GUILayout.Width(80));
                        GUILayout.Label($"Isp: {cfg.ispVac:F0}", GUILayout.Width(85));
                        if (cfg.totalImpulse > 0)
                            GUILayout.Label($"Imp: <color=orange>{cfg.totalImpulse:F0}</color>", GUILayout.Width(85));
                        GUILayout.Space(15);
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
            if (GUILayout.Button(L("#engineAnalyzer_CloseWindow"), GUILayout.Height(25))) isVisible = false;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            if (Event.current.type == EventType.Repaint && !string.IsNullOrEmpty(GUI.tooltip))
            {
                Vector2 mousePos = Event.current.mousePosition;
                GUIContent tooltipContent = new GUIContent(GUI.tooltip);
                Vector2 tooltipSize = GUI.skin.box.CalcSize(tooltipContent);
                Rect tooltipRect = new Rect(mousePos.x + 15f, mousePos.y + 15f, tooltipSize.x + 10f, tooltipSize.y + 5f);
                if (tooltipRect.xMax > windowRect.width) tooltipRect.x = windowRect.width - tooltipRect.width - 5f;
                GUI.backgroundColor = new Color(0f, 0f, 0f, 0.85f);
                GUI.Box(tooltipRect, GUI.tooltip);
                GUI.backgroundColor = Color.white;
            }
            GUI.DragWindow();
        }

        private void DrawDetailPanel(int windowID)
        {
            if (selectedEngine == null || selectedEngine.configs.Count == 0)
            {
                GUILayout.Label("No engine data");
                if (GUILayout.Button("Close")) { showDetailPanel = false; selectedEngine = null; }
                GUI.DragWindow();
                return;
            }

            float panelWidth = isCompactMode ? 450f : 850f;
            GUILayout.BeginVertical(GUILayout.Width(panelWidth));

            GUILayout.Label($"<size=16><b>{selectedEngine.partTitle}</b></size>");
            GUILayout.Space(5f);

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
                GUILayout.Label("<b>" + L("#engineAnalyzer_ThrustVariants") + "</b>");
                var grainNames = _grainPresets.Select(g => g.displayName.Replace("\n", "").Replace("\r", "")).ToArray();
                selectedConfigIndex = GUILayout.SelectionGrid(selectedConfigIndex, grainNames, isCompactMode ? 2 : 4);
                if (selectedConfigIndex >= selectedEngine.configs.Count)
                    selectedConfigIndex = Mathf.Min(selectedConfigIndex, selectedEngine.configs.Count - 1);
            }
            else if (hasConfigs)
            {
                GUILayout.Label("<b>" + L("#engineAnalyzer_Configuration") + "</b>");
                var configNames = selectedEngine.configs.Select(c => c.configName).ToArray();
                selectedConfigIndex = GUILayout.SelectionGrid(selectedConfigIndex, configNames, isCompactMode ? 2 : 4);
            }
            else
            {
                selectedConfigIndex = 0;
            }

            if (selectedConfigIndex < 0 || selectedConfigIndex >= selectedEngine.configs.Count)
                selectedConfigIndex = 0;

            var cfg = selectedEngine.configs[selectedConfigIndex];

            GUILayout.Space(5f);

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label(L("#engineAnalyzer_Manufacturer") + " " + (string.IsNullOrEmpty(selectedEngine.manufacturer) ? L("#engineAnalyzer_Unknown") : selectedEngine.manufacturer));
            GUILayout.Label(L("#engineAnalyzer_Size") + " " + selectedEngine.engineSize);
            GUILayout.Label(L("#engineAnalyzer_Type") + " " + (selectedEngine.isJet ? L("#engineAnalyzer_JetEngine") : (selectedEngine.isSRB ? L("#engineAnalyzer_SolidBooster") : L("#engineAnalyzer_LiquidRocket"))));
            GUILayout.Label(L("#engineAnalyzer_FuelType") + " " + (string.IsNullOrEmpty(cfg.fuelInfo) ? GetFuelTypeName(selectedEngine.fuelType) : cfg.fuelInfo));
            GUILayout.EndVertical();

            GUILayout.Space(8f);

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label($"<b>{L("#engineAnalyzer_Specs")}</b>");
            GUILayout.Label($"{L("#engineAnalyzer_Thrust")}: {cfg.maxThrust:F1} kN");
            GUILayout.Label($"Isp (Vac): {cfg.ispVac:F0}s | Isp (ASL): {cfg.ispASL:F0}s");
            GUILayout.Label($"TWR: {cfg.twr:F2} | {L("#engineAnalyzer_Price")}: {cfg.price:F0}");
            if (selectedEngine.isSRB)
            {
                GUILayout.Label($"{L("#engineAnalyzer_TotalImp")}: {cfg.totalImpulse:F0} kN·s | {L("#engineAnalyzer_AvgThrust")}: {cfg.avgThrust:F1} kN");
                GUILayout.Label($"{L("#engineAnalyzer_BurnTimeStr")}: {cfg.burnTime:F1} s");
            }
            GUILayout.EndVertical();

            if (selectedEngine.isSRB)
            {
                GUILayout.Space(8f);

                float yLabelWidth = 65f;
                float chartWidth = isCompactMode ? 370f : 740f;
                float chartHeight = 140f;

                GUILayout.BeginHorizontal();
                GUILayout.BeginVertical(GUILayout.Width(yLabelWidth));
                GUILayout.Label($"{cfg.maxThrust:F0} kN", GUILayout.Height(28f));
                GUILayout.Label($"{(cfg.maxThrust * 0.75f):F0} kN", GUILayout.Height(28f));
                GUILayout.Label($"{(cfg.maxThrust * 0.5f):F0} kN", GUILayout.Height(28f));
                GUILayout.Label($"{(cfg.maxThrust * 0.25f):F0} kN", GUILayout.Height(28f));
                GUILayout.Label("0 kN", GUILayout.Height(20f));
                GUILayout.EndVertical();

                GUILayout.BeginVertical(GUILayout.Width(chartWidth));
                Rect reservedRect = GUILayoutUtility.GetRect(chartWidth, chartHeight);
                if (Event.current.type == EventType.Repaint)
                    _cachedChartRect = reservedRect;
                if (_cachedChartRect != Rect.zero && cfg.thrustCurve != null)
                    DrawThrustCurve(_cachedChartRect, cfg.thrustCurve, cfg.maxThrust, detailWindowRect, cfg.burnTime);

                GUILayout.BeginHorizontal(GUILayout.Width(chartWidth));
                float slotW = chartWidth / 5f;
                for (int i = 0; i <= 5; i++)
                {
                    float timeVal = (cfg.burnTime / 5f) * i;
                    GUILayout.Label($"<size=11>{timeVal:F1}s</size>", GUILayout.Width((i == 5) ? slotW - 25f : slotW));
                }
                GUILayout.EndHorizontal();

                GUILayout.Label($"<size=12><color=gray>== {L("#engineAnalyzer_Timeline")} ==</color></size>", GUILayout.Width(chartWidth));
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(10f);

            GUILayout.Label($"<b>{L("#engineAnalyzer_ConfigList")}</b>");
            foreach (var c in selectedEngine.configs.Take(4))
            {
                GUILayout.Label($"  {c.configName}: Δv={c.deltaV:F0} | TWR={c.twr:F2} | Isp={c.ispVac:F0}s");
            }

            GUILayout.FlexibleSpace();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(L("#engineAnalyzer_Spawn"), GUILayout.Width(110f), GUILayout.Height(25f)))
            {
                SpawnAndConfigure(selectedEngine.part, cfg.configName);
            }
            GUILayout.Space(15f);
            if (GUILayout.Button(L("#engineAnalyzer_Close"), GUILayout.Width(100f), GUILayout.Height(25f)))
            {
                showDetailPanel = false;
                selectedEngine = null;
            }
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
                            curve = fallbackCurve,
                            thrustMultiplier = 1f
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
                fixedKeys[i].inTangent = keys[i].inTangent;
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

        private void DrawThrustCurve(Rect rect, AnimationCurve curve, float maxThrust, Rect currentDetailRect, float burnTime = 150f, float curveTimeMax = 1f)
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

            float ox = detailWindowRect.x + rect.x;
            float oy = detailWindowRect.y + rect.y;
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
                case FuelType.LOXKerosene: return L("#engineAnalyzer_fuelLOXKero");
                case FuelType.LOXH2: return L("#engineAnalyzer_fuelLOXLH2");
                case FuelType.LOXMethane: return L("#engineAnalyzer_fuelLOXMethane");
                case FuelType.Hypergolic: return L("#engineAnalyzer_fuelHypergolic");
                case FuelType.SolidFuel: return L("#engineAnalyzer_fuelSolid");
                case FuelType.Monopropellant: return L("#engineAnalyzer_fuelMono");
                case FuelType.Airbreathing: return L("#engineAnalyzer_fuelAirbreathing");
                case FuelType.Xenon: return L("#engineAnalyzer_fuelXenon");
                case FuelType.Electric: return L("#engineAnalyzer_fuelElectric");
                case FuelType.Other: return L("#engineAnalyzer_fuelOther");
                default: return L("#engineAnalyzer_All");
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
        public AvailablePart part; public string partTitle; public bool isJet, isSRB, isSurface;
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
        public float maxThrust;
        public float burnTime;
    }
}
