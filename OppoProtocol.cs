using System;
using System.Collections.Generic;

namespace OppoPodsWPF;

/// <summary>OPPO 私有 RFCOMM 协议定义。命令字和 feature ID 来自逆向工程，各型号通用。</summary>
public static class OppoProtocol
{
    /// <summary>OPPO SPP 服务 UUID，所有支持设备共用</summary>
    public static readonly Guid OppoSppUuid = new("0000079A-D102-11E1-9B23-00025B00A5A5");

    /// <summary>协议帧头</summary>
    public const byte Header = 0xAA;
    /// <summary>序列号</summary>
    public const byte Seq = 0xF0;

    // ========== 命令字 ==========
    public const ushort CmdBattery = 0x0106;         // 查询电量
    public const ushort CmdBatteryResp = 0x8106;     // 电量响应
    public const ushort CmdAnc = 0x0404;             // 设置降噪模式
    public const ushort CmdQueryAnc = 0x010C;        // 查询降噪状态
    public const ushort CmdAncResp = 0x810C;         // 降噪状态响应
    public const ushort CmdActiveReport = 0x0204;    // 主动上报（电量/佩戴）
    public const ushort CmdSetFeature = 0x0403;      // 设置功能开关
    public const ushort CmdSetEq = 0x0406;           // 设置 EQ 预设
    public const ushort CmdQueryEq = 0x010F;         // 查询当前 EQ
    public const ushort CmdEqResp = 0x810F;          // EQ 查询响应
    public const ushort CmdEqNotify = 0x0504;        // EQ 变更通知
    public const ushort CmdSpatialAudio = 0x0422;    // 空间音频三模式（Off/Fixed/Track）
    public const ushort CmdRegisterNotify = 0x0205;  // 订阅设备主动通知
    public const ushort CmdBatchQuery = 0x010D;      // 批量查询功能状态
    public const ushort CmdBatchQueryResp = 0x810D;  // 批量查询响应
    public const ushort CmdMultiConnectInfo = 0x0112;  // 查询多设备连接列表
    public const ushort CmdMultiConnectResp = 0x8112;  // 多设备列表响应
    public const ushort CmdOperateHandheld = 0x0429;   // 切换多设备中的活动设备

    // ========== 功能 feature ID ==========
    public const byte FeatureSpatial = 0x1B;     // 空间音效开关
    public const byte FeatureGameMain = 0x28;    // 游戏模式主开关
    public const byte FeatureGameLL = 0x06;      // 游戏模式低延迟（部分设备需要）
    public const byte FeatureDualDevice = 0x11;  // 双设备连接开关

    // ========== ANC 模式字节编码 ==========
    public static readonly byte[] AncOff         = { 0x01, 0x01, 0x01 };
    public static readonly byte[] AncSmart       = { 0x01, 0x01, 0x80 };
    public static readonly byte[] AncLight       = { 0x01, 0x01, 0x40 };
    public static readonly byte[] AncMedium      = { 0x01, 0x01, 0x20 };
    public static readonly byte[] AncDeep        = { 0x01, 0x01, 0x10 };
    public static readonly byte[] AncAdaptive    = { 0x01, 0x01, 0x00, 0x08 };
    public static readonly byte[] AncTransparency = { 0x01, 0x01, 0x04 };

    /// <summary>ANC 响应字节到模式名的反向查找。不同型号字节不同，动态映射见 DeviceCapabilities.AncModeMap</summary>
    public static readonly Dictionary<(byte, byte), string> AncValues = new()
    {
        [(8, 0)]   = "Off",
        [(2, 0)]   = "Smart",
        [(0x80, 0)] = "Smart",
        [(0x40, 0)] = "Light",
        [(0x20, 0)] = "Medium",
        [(0x10, 0)] = "Deep",
        [(0, 1)]   = "Transparency",
        [(0, 2)]   = "Transparency",
        [(4, 0)]   = "Transparency",
        [(0, 8)]   = "Adaptive",
    };

    // ========== 空间音频模式（cmd 0x0422）==========
    public static readonly byte[] SpatialOff   = { 0x00 };
    public static readonly byte[] SpatialFixed = { 0x01 };
    public static readonly byte[] SpatialTrack = { 0x02 };

    /// <summary>构造 OPPO 协议帧</summary>
    public static byte[] BuildPacket(ushort cmd, byte[]? payload = null)
    {
        payload ??= Array.Empty<byte>();
        int totalLen = 7 + payload.Length;
        var pkt = new byte[2 + totalLen];
        pkt[0] = Header;
        pkt[1] = (byte)totalLen;
        pkt[2] = 0x00;
        pkt[3] = 0x00;
        pkt[4] = (byte)(cmd & 0xFF);
        pkt[5] = (byte)(cmd >> 8);
        pkt[6] = Seq;
        pkt[7] = (byte)(payload.Length & 0xFF);
        pkt[8] = (byte)(payload.Length >> 8);
        Buffer.BlockCopy(payload, 0, pkt, 9, payload.Length);
        return pkt;
    }

    /// <summary>构造功能开关帧（cmd 0x0403 + feature + on/off）</summary>
    public static byte[] BuildFeaturePacket(byte feature, bool on) =>
        BuildPacket(CmdSetFeature, new[] { feature, (byte)(on ? 0x01 : 0x00) });

    // ========== 预构建的常用包 ==========
    public static readonly byte[] PktBattery = BuildPacket(CmdBattery);
    public static readonly byte[] PktQueryAnc = BuildPacket(CmdQueryAnc, new byte[] { 0x01, 0x01 });
    public static readonly byte[] PktQueryEq = BuildPacket(CmdQueryEq);

    /// <summary>订阅主动通知：电池 + 佩戴 + ANC</summary>
    public static readonly byte[] PktRegisterNotify = BuildPacket(CmdRegisterNotify,
        new byte[] { 0x01, 0x01, 0x02, 0x02 });

    /// <summary>批量查询包，查询 12 个功能 feature 的状态</summary>
    public static readonly byte[] PktBatchQuery = BuildPacket(CmdBatchQuery,
        new byte[] { 0x0B, 0x05, 0x04, 0x0B, 0x11, 0x13, 0x18, 0x06, 0x1B, 0x1C, 0x27, 0x28 });

    /// <summary>查询多设备连接列表</summary>
    public static readonly byte[] PktMultiConnectInfo = BuildPacket(CmdMultiConnectInfo);

    /// <summary>设备名称匹配的品牌关键词</summary>
    public static readonly string[] SupportedBrands = { "OPPO", "OnePlus", "realme" };

    /// <summary>旧版 ANC 值交换：部分无子模式的设备 NC ↔ Transparency 对调</summary>
    public static string LegacyAncSwap(string mode) => mode switch
    {
        "Smart" or "Light" or "Medium" or "Deep" => "Transparency",
        "Transparency" => "Smart",
        _ => mode
    };

    /// <summary>根据 ANC 模式名生成设置帧</summary>
    public static byte[] PktAncMode(string mode) => mode switch
    {
        "Off"           => BuildPacket(CmdAnc, AncOff),
        "Smart"         => BuildPacket(CmdAnc, AncSmart),
        "Light"         => BuildPacket(CmdAnc, AncLight),
        "Medium"        => BuildPacket(CmdAnc, AncMedium),
        "Deep"          => BuildPacket(CmdAnc, AncDeep),
        "Adaptive"      => BuildPacket(CmdAnc, AncAdaptive),
        "Transparency"  => BuildPacket(CmdAnc, AncTransparency),
        _ => PktBattery
    };

    /// <summary>根据空间音频模式名生成设置帧</summary>
    public static byte[] PktSpatialAudio(string mode) => mode switch
    {
        "Fixed"  => BuildPacket(CmdSpatialAudio, SpatialFixed),
        "Track"  => BuildPacket(CmdSpatialAudio, SpatialTrack),
        _        => BuildPacket(CmdSpatialAudio, SpatialOff),
    };
}
