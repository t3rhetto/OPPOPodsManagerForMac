using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace OppoPodsManager;

/// <summary>
/// 一个可选的 ANC 模式项（用于按 JSON 动态生成降噪 UI）。
/// 主模式可含若干子模式（如"降噪"下的智能/中度/超级/轻度）；无子模式则直接可发送。
/// </summary>
public sealed class AncOption
{
    public string Key { get; set; } = "";        // 内部键：Off/Transparency/Adaptive/NC/Smart/Light/Medium/Deep
    public string Label { get; set; } = "";       // UI 显示名
    public byte ProtocolIndex { get; set; }        // 发送时的位索引（有子模式时该值仅占位，实际发子模式）
    public bool Sendable { get; set; } = true;     // 该项本身能否直接发送（纯容器为 false）
    public List<AncOption> Children { get; set; } = new();
}

/// <summary>
/// 设备能力检测。从 DeviceModels.json 加载 whitelist 配置，按蓝牙名称匹配设备。
/// 支持能力、ANC 模式映射、EQ 名称均从 JSON 动态推导，添加新设备只需追加 whitelist 条目。
/// </summary>
public class DeviceCapabilities
{
    public string DeviceName { get; set; } = "";
    public string ModelName { get; set; } = "Unknown";
    public string ModelId { get; set; } = "";
    public int ProtocolType { get; set; } = 1;      // 0=旧版协议 1/2=新版；本程序命令层仅实现新版
    public bool SupportSpp { get; set; } = true;    // 是否支持经典 SPP（false 多为 BLE-only）
    public bool IsSupported { get; set; } = true;   // 名称匹配到 whitelist 且协议可用

    // ========== 功能标志 ==========
    public bool HasSpatialAudio { get; set; }      // cmd 0x0422 空间音频三模式（Off/Fixed/Track）
    public bool HasSpatialSound { get; set; }       // feature 0x1B 空间音效开关
    public bool HasDualDevice { get; set; }         // feature 0x11 双设备连接
    public bool HasAdaptiveAnc { get; set; }        // ANC 子模式（Smart/Light/Medium/Deep）
    public bool IsLegacyAnc { get; set; }           // noiseReductionMode 无子模式且 ANC On 在异常位置时启用值交换
    public bool HasGameMode { get; set; }           // 由 JSON gameSoundList 推导

    // ========== EQ 预设 ==========
    public Dictionary<string, byte> EqPresets { get; set; } = new();
    public Dictionary<byte, string> EqNames { get; set; } = new();

    /// <summary>protocolIndex → UI 模式名（按型号 noiseReductionMode 动态构建）</summary>
    public Dictionary<byte, string> AncIndexToName { get; set; } = new();

    /// <summary>UI 模式名 → protocolIndex（发送 ANC 时按型号取正确字节位）</summary>
    public Dictionary<string, byte> AncNameToIndex { get; set; } = new();

    /// <summary>按 JSON noiseReductionMode 构建的层级化 ANC 选项，用于动态生成 UI（主模式+子模式，已按 UI 顺序排序）</summary>
    public List<AncOption> AncOptions { get; set; } = new();

    // ========== 内嵌资源 ==========
    private static readonly List<JsonElement> _deviceModels = LoadDeviceModels();
    private static readonly Dictionary<string, string> _eqModeNames = LoadEqNameMap();

    /// <summary>从嵌入资源加载设备 whitelist，按名称长度降序排列</summary>
    private static List<JsonElement> LoadDeviceModels()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("OppoPodsManager.DeviceModels.json");
            if (stream == null) return new List<JsonElement>();
            using var reader = new StreamReader(stream);
            var doc = JsonDocument.Parse(reader.ReadToEnd());
            var list = doc.RootElement.EnumerateArray().ToList();
            list.Sort((a, b) =>
            {
                var na = a.GetProperty("name").GetString() ?? "";
                var nb = b.GetProperty("name").GetString() ?? "";
                return nb.Length.CompareTo(na.Length);
            });
            return list;
        }
        catch { return new List<JsonElement>(); }
    }

    /// <summary>从嵌入资源加载 EQ modeType → 名称映射</summary>
    private static Dictionary<string, string> LoadEqNameMap()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("OppoPodsManager.EqModeNames.json");
            if (stream == null) return new Dictionary<string, string>();
            using var reader = new StreamReader(stream);
            var doc = JsonDocument.Parse(reader.ReadToEnd());
            var map = new Dictionary<string, string>();
            foreach (var kv in doc.RootElement.GetProperty("mapping").EnumerateObject())
                map[kv.Name] = kv.Value.GetString() ?? "";
            return map;
        }
        catch { return new Dictionary<string, string>(); }
    }

    /// <summary>根据蓝牙名称自动检测设备能力</summary>
    public static DeviceCapabilities Detect(string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName)) return Default();
        var norm = Normalize(deviceName);

        // 第1轮：规范化后完全相等
        foreach (var entry in _deviceModels)
        {
            var nm = Normalize(entry.GetProperty("name").GetString() ?? "");
            if (nm.Length > 0 && nm == norm)
                return FromJson(entry, deviceName);
        }

        // 第2轮：设备名包含型号名（型号名需 >=5 字符，避免 "air" 之类误配）
        foreach (var entry in _deviceModels)
        {
            var nm = Normalize(entry.GetProperty("name").GetString() ?? "");
            if (nm.Length >= 5 && norm.Contains(nm))
                return FromJson(entry, deviceName);
        }

        // 第3轮：型号名包含设备名（设备名需 >=5 字符）
        foreach (var entry in _deviceModels)
        {
            var nm = Normalize(entry.GetProperty("name").GetString() ?? "");
            if (norm.Length >= 5 && nm.Contains(norm))
                return FromJson(entry, deviceName);
        }

        // 未匹配：标记为未完整适配
        return new DeviceCapabilities { DeviceName = deviceName, ModelName = deviceName, IsSupported = false };
    }

    /// <summary>按完整型号名手动覆盖检测</summary>
    public static DeviceCapabilities ForceModel(string modelName)
    {
        foreach (var entry in _deviceModels)
        {
            var entryName = entry.GetProperty("name").GetString() ?? "";
            if (string.Equals(modelName, entryName, StringComparison.OrdinalIgnoreCase))
                return FromJson(entry, modelName);
        }
        return Default();
    }


    /// <summary>按 productId（官方主键，如 "06F010"）精确识别设备。找不到返回 null。</summary>
    public static DeviceCapabilities? DetectById(string productId, string? deviceName = null)
    {
        if (string.IsNullOrWhiteSpace(productId)) return null;
        foreach (var entry in _deviceModels)
        {
            var entryId = entry.GetProperty("id").GetString() ?? "";
            if (string.Equals(entryId, productId, StringComparison.OrdinalIgnoreCase))
                return FromJson(entry, deviceName ?? entry.GetProperty("name").GetString() ?? productId);
        }
        return null;
    }

    /// <summary>获取所有已知设备型号名称列表</summary>
    public static List<string> GetModelNames()
    {
        var names = new List<string>();
        foreach (var entry in _deviceModels)
        {
            var name = entry.GetProperty("name").GetString();
            if (!string.IsNullOrEmpty(name))
                names.Add(name);
        }
        return names;
    }

    /// <summary>从 whitelist JSON 条目解析设备能力</summary>
    private static DeviceCapabilities FromJson(JsonElement entry, string deviceName)
    {
        var name = entry.GetProperty("name").GetString() ?? deviceName;
        var id = entry.GetProperty("id").GetString() ?? "";
        var caps = new DeviceCapabilities { DeviceName = deviceName, ModelName = name, ModelId = id };

        if (entry.TryGetProperty("protocolType", out var pt) && pt.ValueKind == JsonValueKind.Number)
            caps.ProtocolType = pt.GetInt32();
        if (entry.TryGetProperty("supportSpp", out var sp) && (sp.ValueKind == JsonValueKind.True || sp.ValueKind == JsonValueKind.False))
            caps.SupportSpp = sp.GetBoolean();
        // 命令层仅实现新版协议(1/2)且需支持 SPP，否则标记为未完整适配
        caps.IsSupported = caps.SupportSpp && caps.ProtocolType != 0;

        if (!entry.TryGetProperty("function", out var func)) return caps;

        // spatialTypes 存在 → 空间音效；长度 ≥3 → 空间音频三模式
        caps.HasSpatialSound = func.TryGetProperty("spatialTypes", out var st);
        if (st.ValueKind == JsonValueKind.Array)
        {
            int n = 0;
            foreach (var _ in st.EnumerateArray()) { n++; if (n >= 3) break; }
            if (n >= 3) caps.HasSpatialAudio = true;
        }

        // multiDevicesConnect ≥ 1 → 双设备连接
        if (func.TryGetProperty("multiDevicesConnect", out var mdc))
            caps.HasDualDevice = mdc.GetInt32() >= 1;

        // gameSoundList 非空 → 游戏模式（官方需 cap(0x423) + 该列表）
        if (func.TryGetProperty("gameSoundList", out var gsl) && gsl.ValueKind == JsonValueKind.Array)
        {
            foreach (var _ in gsl.EnumerateArray()) { caps.HasGameMode = true; break; }
        }

        // noiseReductionMode 有 childrenMode → 自适应降噪子模式
        if (func.TryGetProperty("noiseReductionMode", out var nrm))
        {
            foreach (var mode in nrm.EnumerateArray())
                if (mode.TryGetProperty("childrenMode", out _))
                    caps.HasAdaptiveAnc = true;

            BuildAncOptions(nrm, caps);

            // 无 childrenMode 且 modeType 5 在 protocolIndex 0 → 旧版 ANC 值交换
            bool hasChildren = false;
            foreach (var mode in nrm.EnumerateArray())
                if (mode.TryGetProperty("childrenMode", out _)) hasChildren = true;
            if (!hasChildren)
            {
                foreach (var mode in nrm.EnumerateArray())
                    if (mode.TryGetProperty("modeType", out var lt) && lt.GetInt32() == 5 &&
                        mode.TryGetProperty("protocolIndex", out var lp) && lp.GetInt32() == 0)
                    { caps.IsLegacyAnc = true; break; }
            }
        }

        // equalizerMode[].modeType → EqModeNames.json 查找显示名称
        var eqMap = new Dictionary<byte, string>();
        if (func.TryGetProperty("equalizerMode", out var eqModes))
        {
            foreach (var mode in eqModes.EnumerateArray())
            {
                if (!mode.TryGetProperty("protocolIndex", out var pi)) continue;
                byte idx = pi.GetByte();
                string displayName = idx < 10 ? $"模式{idx}" : $"M{idx}";
                if (mode.TryGetProperty("modeType", out var mt))
                    if (_eqModeNames.TryGetValue(mt.GetInt32().ToString(), out var n))
                        displayName = n;
                if (!eqMap.ContainsKey(idx)) eqMap[idx] = displayName;
            }
        }
        if (eqMap.Count == 0) eqMap[0] = "默认";
        ApplyEqNames(caps, eqMap);
        return caps;
    }

    private static DeviceCapabilities Default() => new() { ModelName = "Unknown" };

    /// <summary>写入 EQ preset 双向索引</summary>
    private static void ApplyEqNames(DeviceCapabilities caps, Dictionary<byte, string> names)
    {
        caps.EqPresets = new Dictionary<string, byte>();
        caps.EqNames = new Dictionary<byte, string>(names);
        foreach (var (k, v) in names) caps.EqPresets[v] = k;
    }

    // modeType 语义（官方 APK Lpa/k.b() + Lu9/a 权威映射）：
    //   1=关闭 2=通透 3=轻度 4=深度 5=降噪(容器) 6=自适应 7=智能 8=中度 10=自适应
    private static string ModeKey(int type) => type switch
    {
        1 => "Off",
        2 => "Transparency",
        3 => "Light",
        4 => "Deep",        // APK: melody_common_depth_noise_reduction_title = 深度降噪
        5 => "NC",          // 降噪容器（含子档位）或扁平"强降噪"
        6 => "Adaptive",
        7 => "Smart",       // APK: intelligent_title_v2 = 智能切换
        8 => "Medium",      // APK: melody_common_middle_noise_reduction_title = 中度降噪
        10 => "Adaptive",   // APK Lu9/a: modeType 10 → 自适应（Free4 系扁平主模式）
        _ => "Mode" + type
    };

    /// <summary>模式键 → UI 中文显示名</summary>
    private static string AncLabel(string key) => key switch
    {
        "Off" => "关闭",
        "Transparency" => "通透",
        "Adaptive" => "自适应",
        "NC" => "降噪",
        "Smart" => "智能",
        "Light" => "轻度",
        "Medium" => "中度",
        "Deep" => "深度",
        _ => key
    };

    /// <summary>UI 排序权重：降噪 → 通透 → 自适应 → 其它 → 关闭（关闭永远最后）</summary>
    private static int MainRank(string key) => key switch
    {
        "NC" => 0, "Smart" => 0,
        "Transparency" => 1,
        "Adaptive" => 2,
        "Off" => 99,
        _ => 50
    };

    /// <summary>
    /// 从 JSON noiseReductionMode 构建：
    ///   1) 层级化 AncOptions（主模式 + 子模式），供 UI 动态生成；
    ///   2) 扁平 AncIndexToName / AncNameToIndex，供 ParseAnc / SendAnc 使用。
    /// 完全按型号 JSON 推导，型号没有的模式不会出现。
    /// </summary>
    private static void BuildAncOptions(JsonElement nrm, DeviceCapabilities caps)
    {
        var idxToName = caps.AncIndexToName;
        var nameToIdx = caps.AncNameToIndex;
        var options = new List<AncOption>();

        foreach (var entry in nrm.EnumerateArray())
        {
            if (!entry.TryGetProperty("modeType", out var mt)) continue;
            int type = mt.GetInt32();
            string key = ModeKey(type);
            byte ownIdx = entry.TryGetProperty("protocolIndex", out var pi) ? pi.GetByte() : (byte)0;

            var hasChildren = entry.TryGetProperty("childrenMode", out var children)
                              && children.ValueKind == JsonValueKind.Array;

            // 收集子模式
            var childOpts = new List<AncOption>();
            if (hasChildren)
            {
                foreach (var child in children.EnumerateArray())
                {
                    if (!child.TryGetProperty("protocolIndex", out var cpi)) continue;
                    int ctype = child.TryGetProperty("modeType", out var cmt) ? cmt.GetInt32() : type;
                    byte cidx = cpi.GetByte();
                    string ckey = ModeKey(ctype);
                    RegisterFlat(idxToName, nameToIdx, cidx, ckey);
                    childOpts.Add(new AncOption { Key = ckey, Label = AncLabel(ckey), ProtocolIndex = cidx, Sendable = true });
                }
            }

            // 单一自引用子模式（如 Off/Transparency 各含一个同类型子项）视为叶子：直接用子项索引发送
            if (childOpts.Count == 1 && childOpts[0].Key == key)
            {
                var only = childOpts[0];
                RegisterFlat(idxToName, nameToIdx, only.ProtocolIndex, key);
                options.Add(new AncOption { Key = key, Label = AncLabel(key), ProtocolIndex = only.ProtocolIndex, Sendable = true });
                continue;
            }

            if (childOpts.Count > 0)
            {
                // 真正的容器（多个子模式）：主项不可直接发送，点主项时发第一个子项
                options.Add(new AncOption { Key = key, Label = AncLabel(key), ProtocolIndex = ownIdx, Sendable = false, Children = childOpts });
            }
            else
            {
                // 扁平叶子项
                RegisterFlat(idxToName, nameToIdx, ownIdx, key);
                options.Add(new AncOption { Key = key, Label = AncLabel(key), ProtocolIndex = ownIdx, Sendable = true });
            }
        }

        // 主项排序 + 子项各自排序
        options.Sort((a, b) => MainRank(a.Key).CompareTo(MainRank(b.Key)));
        foreach (var o in options)
            o.Children.Sort((a, b) => MainRank(a.Key).CompareTo(MainRank(b.Key)));
        caps.AncOptions = options;
    }

    private static void RegisterFlat(Dictionary<byte, string> idxToName, Dictionary<string, byte> nameToIdx, byte idx, string name)
    {
        idxToName[idx] = name;
        // 名称→索引只记第一次，避免容器/子模式重名覆盖
        if (!nameToIdx.ContainsKey(name)) nameToIdx[name] = idx;
    }

    /// <summary>移除非字母数字字符并转小写，用于模糊匹配</summary>
    private static string Normalize(string name) =>
        new string(name.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

}
