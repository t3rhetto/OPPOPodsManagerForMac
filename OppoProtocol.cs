using System;
using System.Collections.Generic;

namespace OppoPodsWPF;

public static class OppoProtocol
{
    public const byte Header = 0xAA;
    public const byte Seq = 0xF0;

    // Commands
    public const ushort CmdBattery = 0x0106;
    public const ushort CmdBatteryResp = 0x8106;
    public const ushort CmdAnc = 0x0404;
    public const ushort CmdQueryAnc = 0x010C;
    public const ushort CmdAncResp = 0x810C;
    public const ushort CmdActiveReport = 0x0204;
    public const ushort CmdSetFeature = 0x0403;
    public const ushort CmdSetEq = 0x0406;
    public const ushort CmdQueryEq = 0x010F;
    public const ushort CmdEqResp = 0x810F;
    public const ushort CmdEqNotify = 0x0504;
    public const ushort CmdSpatialAudio = 0x0422;   // Enco X3 空间音频三模式
    public const ushort CmdRegisterNotify = 0x0205;  // 订阅主动推送
    public const ushort CmdBatchQuery = 0x010D;       // 批量状态查询
    public const ushort CmdBatchQueryResp = 0x810D;   // 批量查询响应

    // Feature IDs
    public const byte FeatureSpatial = 0x1B;   // Free4/Air5 空间音效开关
    public const byte FeatureGameMain = 0x28;
    public const byte FeatureGameLL = 0x06;
    public const byte FeatureDualDevice = 0x11; // 双设备连接

    // ANC modes
    public static readonly byte[] AncOff         = { 0x01, 0x01, 0x01 };
    public static readonly byte[] AncSmart       = { 0x01, 0x01, 0x80 };
    public static readonly byte[] AncLight       = { 0x01, 0x01, 0x40 };
    public static readonly byte[] AncMedium      = { 0x01, 0x01, 0x20 };
    public static readonly byte[] AncDeep        = { 0x01, 0x01, 0x10 };
    public static readonly byte[] AncAdaptive    = { 0x01, 0x01, 0x00, 0x08 };
    public static readonly byte[] AncTransparency = { 0x01, 0x01, 0x04 };

    // ANC mode reverse lookup
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

    // Spatial audio modes (Enco X3, cmd 0x0422)
    public static readonly byte[] SpatialOff   = { 0x00 };
    public static readonly byte[] SpatialFixed = { 0x01 };
    public static readonly byte[] SpatialTrack = { 0x02 };

    // Build packet
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

    public static byte[] BuildFeaturePacket(byte feature, bool on) =>
        BuildPacket(CmdSetFeature, new[] { feature, (byte)(on ? 0x01 : 0x00) });

    // Pre-built packets
    public static readonly byte[] PktBattery = BuildPacket(CmdBattery);
    public static readonly byte[] PktQueryAnc = BuildPacket(CmdQueryAnc, new byte[] { 0x01, 0x01 });
    public static readonly byte[] PktQueryEq = BuildPacket(CmdQueryEq);

    public static readonly byte[] PktRegisterNotify = BuildPacket(CmdRegisterNotify,
        new byte[] { 0x01, 0x01, 0x02, 0x02 });  // 订阅电池+佩戴+ANC通知

    // 批量状态查询 (0x010D, 与 1812z 完全相同)
    public static readonly byte[] PktBatchQuery = BuildPacket(CmdBatchQuery,
        new byte[] { 0x0B, 0x05, 0x04, 0x0B, 0x11, 0x13, 0x18, 0x06, 0x1B, 0x1C, 0x27, 0x28 });

    // Air2 Pro 旧版 ANC 映射：NC ↔ Transparency 值交换
    public static string LegacyAncSwap(string mode) => mode switch
    {
        "Smart" or "Light" or "Medium" or "Deep" => "Transparency",
        "Transparency" => "Smart",
        _ => mode
    };

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

    public static byte[] PktSpatialAudio(string mode) => mode switch
    {
        "Fixed"  => BuildPacket(CmdSpatialAudio, SpatialFixed),
        "Track"  => BuildPacket(CmdSpatialAudio, SpatialTrack),
        _        => BuildPacket(CmdSpatialAudio, SpatialOff),
    };
}
