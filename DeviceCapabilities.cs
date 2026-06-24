using System.Collections.Generic;
using System.Linq;

namespace OppoPodsWPF;

/// <summary>
/// 设备能力检测：根据蓝牙名称匹配型号，决定 UI 展示哪些功能开关
/// 逻辑移植自 <see href="https://github.com/1812z/OppoPods"/> DeviceCapabilities.kt
/// </summary>
public class DeviceCapabilities
{
    public string DeviceName { get; init; } = "";
    public string ModelName { get; set; } = "Unknown";

    // 功能标志
    public bool HasSpatialAudio { get; init; }      // X3: cmd 0x0422 空间音频三模式(固定/追踪/关闭)
    public bool HasSpatialSound { get; init; }       // Free4/Air5: feature 0x1B 空间音效开关
    public bool HasDualDevice { get; init; }         // feature 0x11 双设备连接
    public bool HasAdaptiveAnc { get; init; }        // Free4: 自适应降噪
    public bool IsLegacyAnc { get; init; }           // Air2 Pro: ANC模式值交换(兼容)
    public bool HasGameMode { get; init; } = true;   // 所有设备都支持游戏模式

    // 各型号 EQ 预设不同
    public Dictionary<string, byte> EqPresets { get; set; } = new();
    public Dictionary<byte, string> EqNames { get; set; } = new();

    // ============================================================
    // 硬编码型号表（移植自 1812z）
    // ============================================================
    private static readonly HashSet<string> AdaptiveAncModels = new() { "encofree4" };
    private static readonly HashSet<string> SpatialAudioModels = new() { "encox3" };
    private static readonly HashSet<string> SpatialSoundModels = new() { "encofree4", "encoair5" };
    private static readonly HashSet<string> LegacyAncModels   = new() { "encoair2pro" };
    private static readonly HashSet<string> DualDeviceModels  = new() { "encofree4", "encox3" };

    // X3 大师调音（5种，与欢律一致）
    private static readonly Dictionary<string, byte> X3Eq = new()
    {
        ["至臻原音"] = 0,
        ["高清解析"] = 1,
        ["纯享人声"] = 2,
        ["澎湃低音"] = 3,
        ["丹拿特调"] = 7,
    };

    // Free4 大师调音（实测）
    private static readonly Dictionary<string, byte> Free4Eq = new()
    {
        ["至臻原音"] = 0,
        ["纯享人声"] = 1,
        ["澎湃低音"] = 2,
        ["丹拿特调"] = 3,
        ["活力动感"] = 7,
    };

    // ============================================================
    // 检测入口
    // ============================================================
    public static DeviceCapabilities Detect(string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
            return Default();

        var norm = Normalize(deviceName);
        return DetectByNorm(norm, deviceName);
    }

    /// <summary>手动覆盖型号（跳过名称检测）</summary>
    public static DeviceCapabilities ForceModel(string modelName) =>
        DetectByModelName(modelName);

    private static DeviceCapabilities DetectByModelName(string modelName) => modelName switch
    {
        "OPPO Enco Free4"    => DetectByNorm("encofree4", "OPPO Enco Free4"),
        "OPPO Enco X3"       => DetectByNorm("encox3", "OPPO Enco X3"),
        "OPPO Enco Air5"     => DetectByNorm("encoair5", "OPPO Enco Air5"),
        "OPPO Enco Air2 Pro" => DetectByNorm("encoair2pro", "OPPO Enco Air2 Pro"),
        _                    => Default()
    };

    private static DeviceCapabilities DetectByNorm(string norm, string originalName)
    {
        var caps = new DeviceCapabilities
        {
            DeviceName = originalName,
            HasSpatialAudio  = MatchAny(norm, SpatialAudioModels),
            HasSpatialSound  = MatchAny(norm, SpatialSoundModels),
            HasDualDevice    = MatchAny(norm, DualDeviceModels),
            HasAdaptiveAnc   = MatchAny(norm, AdaptiveAncModels),
            IsLegacyAnc      = MatchAny(norm, LegacyAncModels),
        };

        // EQ 预设：X3 5种，Free4 4种，其他通用
        if (Match(norm, "encox3"))
        {
            caps.EqPresets = X3Eq;
            caps.ModelName = "OPPO Enco X3";
        }
        else if (Match(norm, "encofree4"))
        {
            caps.EqPresets = Free4Eq;
            caps.ModelName = "OPPO Enco Free4";
        }
        else if (Match(norm, "encoair5"))
        {
            caps.EqPresets = Free4Eq;   // Air5 与 Free4 一致
            caps.ModelName = "OPPO Enco Air5";
        }
        else if (Match(norm, "encoair2pro"))
        {
            caps.EqPresets = Free4Eq;
            caps.ModelName = "OPPO Enco Air2 Pro";
        }
        else
        {
            caps.EqPresets = Free4Eq;   // 默认回退
            caps.ModelName = originalName;
        }

        // 反向索引
        foreach (var (k, v) in caps.EqPresets)
            caps.EqNames[v] = k;

        return caps;
    }

    private static DeviceCapabilities Default() =>
        new() { EqPresets = Free4Eq, ModelName = "Unknown" };

    // ============================================================
    // 名称匹配工具
    // ============================================================
    private static string Normalize(string name) =>
        new string(name.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static bool Match(string normName, string normModel) =>
        normName.Contains(normModel) || normModel.Contains(normName);

    private static bool MatchAny(string normName, HashSet<string> models) =>
        models.Any(m => Match(normName, m));
}
