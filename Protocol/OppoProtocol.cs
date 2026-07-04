using System;
using System.Collections.Generic;

namespace OppoPodsManager;

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
    public const ushort CmdActiveReport = 0x0204;    // 主动通知事件（payload[0]=子类型，官方 NotificationCommandManager.b 分发）
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
    public const ushort CmdQueryProductId = 0x0103;    // 查询远程 Product ID（官方设备识别主键）
    public const ushort CmdProductIdResp = 0x8103;     // Product ID 响应

    // ========== 官方完整命令目录（0x100-0x134 查询 / 0x400-0x43B 设置）==========
    // 来源：APK PollCommandManager 发送方法 + D() 巨型 switch (0x8100-0x8134 响应)。
    // 命令层用 melody 命令号，经 SPP 0xAA 帧承载。响应 = 命令 | 0x8000。
    // 标注 [已验证] = 实机日志确认收发；其余为官方目录，供后续按能力启用。

    // ----- 设备信息查询（空载荷）-----
    public const ushort CmdQueryCapability   = 0x0100;  // requestCapability 基础能力
    public const ushort CmdQueryMtu          = 0x0101;  // 远程 MTU
    public const ushort CmdQueryVendorId     = 0x0102;  // 远程 Vendor ID
    public const ushort CmdQueryVersion      = 0x0105;  // 远程固件版本
    public const ushort CmdQueryUpgradeCap   = 0x0107;  // 升级能力
    public const ushort CmdQueryFunctionKey  = 0x0108;  // 按键功能
    public const ushort CmdQueryEarStatus    = 0x0109;  // 耳机状态
    public const ushort CmdQueryColorId      = 0x010B;  // 耳机颜色 ID
    public const ushort CmdQueryFeatureState = 0x010D;  // 功能开关状态（= CmdBatchQuery）
    public const ushort CmdQueryCodecType    = 0x0114;  // 编解码器类型
    public const ushort CmdQueryHearing      = 0x0115;  // 听力增强数据
    public const ushort CmdQueryCapBitmap    = 0x011C;  // 能力位图（TriangleInfo/GetCapability）
    public const ushort CmdQueryMultiInfo    = 0x011D;  // 多连接信息（另一路）
    public const ushort CmdQueryEarScan      = 0x011E;  // 耳道扫描数据
    public const ushort CmdQueryEarTone      = 0x0121;  // 耳音调数据
    public const ushort CmdQueryEqAll        = 0x0122;  // 全部 EQ 数据
    public const ushort CmdQueryCodecList    = 0x0123;  // 编解码器列表
    public const ushort CmdQueryBassEngine   = 0x0124;  // 低音引擎（降噪新格式）
    public const ushort CmdQueryAccountKey   = 0x0125;  // AccountKey
    public const ushort CmdQuerySpineCalib   = 0x0129;  // 脊柱校准状态
    public const ushort CmdQueryColorIdAlt   = 0x012A;  // 耳机颜色 ID（备用）
    public const ushort CmdQueryGameSound    = 0x012B;  // 游戏音效信息
    public const ushort CmdSetCurrentNoise   = 0x012E;  // 设置当前降噪（PCM 写路径）
    public const ushort CmdSetBuildModel     = 0x041F;  // 设置机型 Build.MODEL

    // ----- 高级设置（0x400-0x43B，SetCommandManager 统一处理响应）-----
    public const ushort CmdSetGlobalSwitch   = 0x0400;  // 全局开关
    public const ushort CmdSetChargeStatus   = 0x0403;  // = CmdSetFeature（功能开关）
    public const ushort CmdSetNoiseMode      = 0x0404;  // = CmdAnc（设置降噪）
    public const ushort CmdSetEqPreset       = 0x0406;  // = CmdSetEq
    public const ushort CmdSetCodec          = 0x040E;  // 设置编解码器
    public const ushort CmdSetEqDetail       = 0x0418;  // 设置详细 EQ（自定义频段）
    public const ushort CmdSetSpatialAudioV2 = 0x0417;  // 空间音频（旧路）
    public const ushort CmdSetGameModeV2     = 0x0412;  // 游戏模式（新版）
    public const ushort CmdSetFeatureSwitch  = 0x0423;  // 功能开关设置 [value][enable]

    // ----- 其它操作 -----
    public const ushort CmdFindDevice        = 0x0435;  // 查找设备（旧 0x35 系）
    public const ushort CmdSpatialAudioResp  = 0x8422;  // 空间音频响应

    // 已验证收发（实机日志确认）：0x0103/0x0106/0x010C/0x010F/0x010D/0x0112/0x0204/0x0205/
    //   0x0404/0x0406/0x0403/0x0422/0x0429 及其 0x8xxx 响应、0x8200-0x8205、0x0500-0x05FF。

    // ========== 通知注册响应族（官方 NotificationCommandManager.c 分发，耳机→手机）==========
    public const ushort CmdNotifyCapabilityResp = 0x8200;  // 通知能力响应（保存耳机支持的事件集）
    public const ushort CmdRegisterNotifyResp   = 0x8201;  // 单条注册通知响应（含 status+event）
    public const ushort CmdRegisterNotifyEvent  = 0x8202;  // 注册后携带的事件（内部再走子类型分发）
    public const ushort CmdCancelNotifyResp     = 0x8203;  // 取消注册通知响应
    public const ushort CmdRegisterMultiResp    = 0x8205;  // 批量注册通知响应 = 初始化握手完成 ACK

    // ========== 0x0204 通知事件子类型（payload[0]，官方语义）==========
    public const byte EvtBattery       = 0x01;  // 电池信息 List<BatteryInfo>
    public const byte EvtEarBudsStatus = 0x02;  // 佩戴/入耳状态 List<StatusInfo>
    public const byte EvtNoiseMode     = 0x03;  // 降噪模式变更（次字节区分旧/新/智能）
    public const byte EvtCompactness   = 0x04;  // 贴合度检测
    public const byte EvtGameMode      = 0x05;  // 游戏模式开关
    public const byte EvtMultiConnect  = 0x06;  // 多设备连接状态
    public const byte EvtHearingDetect = 0x08;  // 听力检测状态
    public const byte EvtCodecType     = 0x09;  // 编解码器类型
    public const byte EvtZenMode       = 0x0A;  // 禅模式开关
    public const byte EvtPersonalNoise = 0x0B;  // 个性化降噪结果
    public const byte EvtTriangle      = 0x0D;  // 空间音频三角信息
    public const byte EvtEarScan       = 0x0E;  // 耳道扫描结果
    public const byte EvtGaming        = 0x0F;  // 多连接游戏/手持/点击等级公共事件
    public const byte EvtOneshot       = 0x10;  // Oneshot 状态
    public const byte EvtToneChange    = 0x11;  // 耳音调变更

    /// <summary>0x0204 子类型 → 可读名称（用于日志）。</summary>
    public static string ActiveReportName(int subType) => subType switch
    {
        EvtBattery       => "电池",
        EvtEarBudsStatus => "佩戴状态",
        EvtNoiseMode     => "降噪变更",
        EvtCompactness   => "贴合检测",
        EvtGameMode      => "游戏模式",
        EvtMultiConnect  => "多连接状态",
        EvtHearingDetect => "听力检测",
        EvtCodecType     => "编解码",
        EvtZenMode       => "禅模式",
        EvtPersonalNoise => "个性化降噪",
        EvtTriangle      => "空间音频三角",
        EvtEarScan       => "耳道扫描",
        EvtGaming        => "游戏/手持公共事件",
        EvtOneshot       => "Oneshot",
        EvtToneChange    => "耳音调",
        _                => "未知(0x" + subType.ToString("X2") + ")"
    };

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

    /// <summary>ANC 响应字节到模式名的反向查找（静态回退表）。按型号的动态映射见 DeviceCapabilities.AncIndexToName</summary>
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
        pkt[5] = (byte)((cmd / 256) & 0xFF);
        pkt[6] = Seq;
        pkt[7] = (byte)(payload.Length & 0xFF);
        pkt[8] = (byte)((payload.Length / 256) & 0xFF);
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

    /// <summary>单独订阅佩戴通知（部分设备需要单独注册，使用 action=0x02）</summary>
    public static readonly byte[] PktRegisterWear = BuildPacket(CmdRegisterNotify,
        new byte[] { 0x02, 0x02 });

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

    /// <summary>按 protocolIndex 生成 ANC 设置帧（新版协议：位图 type=1，bit=protocolIndex）。</summary>
    public static byte[] PktAncByIndex(byte protocolIndex)
    {
        // payload = [flag 0x01][type 0x01=位图][bitmap...]，第 protocolIndex 位置 1
        int byteCount = (protocolIndex / 8) + 1;
        var payload = new byte[2 + byteCount];
        payload[0] = 0x01;
        payload[1] = 0x01;
        // bit = 2^(protocolIndex % 8)，避免使用位移运算符
        int bitPos = protocolIndex % 8;
        int bitVal = 1;
        for (int k = 0; k < bitPos; k++) bitVal *= 2;
        payload[2 + (protocolIndex / 8)] = (byte)bitVal;
        return BuildPacket(CmdAnc, payload);
    }

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

    // ===== 载荷构造（供传输层 Send(cmd, payload)；帧封装交给 IFrameCodec）=====
    public static readonly byte[] PayEmpty = { };
    public static readonly byte[] PayQueryAnc = { 0x01, 0x01 };
    public static readonly byte[] PayRegisterNotify = { 0x01, 0x01, 0x02, 0x02 };
    public static readonly byte[] PayRegisterWear = { 0x02, 0x02 };
    public static readonly byte[] PayBatchQuery = { 0x0B, 0x05, 0x04, 0x0B, 0x11, 0x13, 0x18, 0x06, 0x1B, 0x1C, 0x27, 0x28 };

    public static byte[] FeaturePayload(byte feature, bool on)
    {
        return new byte[] { feature, (byte)(on ? 0x01 : 0x00) };
    }

    public static byte[] AncPayloadByIndex(byte protocolIndex)
    {
        int byteCount = (protocolIndex / 8) + 1;
        var payload = new byte[2 + byteCount];
        payload[0] = 0x01;
        payload[1] = 0x01;
        int bitPos = protocolIndex % 8;
        int bitVal = 1;
        for (int k = 0; k < bitPos; k++) bitVal *= 2;
        payload[2 + (protocolIndex / 8)] = (byte)bitVal;
        return payload;
    }

    public static byte[] AncPayloadByName(string mode)
    {
        switch (mode)
        {
            case "Off": return AncOff;
            case "Smart": return AncSmart;
            case "Light": return AncLight;
            case "Medium": return AncMedium;
            case "Deep": return AncDeep;
            case "Adaptive": return AncAdaptive;
            case "Transparency": return AncTransparency;
            default: return AncOff;
        }
    }

    public static byte[] SpatialPayload(string mode)
    {
        switch (mode)
        {
            case "Fixed": return SpatialFixed;
            case "Track": return SpatialTrack;
            default: return SpatialOff;
        }
    }

    public static byte[] OperateHandheldPayload(string targetAddress, bool connect)
    {
        var parts = targetAddress.Split(':');
        var payload = new byte[1 + parts.Length];
        payload[0] = (byte)(connect ? 0x01 : 0x00);
        for (int i = 0; i < parts.Length; i++)
            payload[1 + i] = Convert.ToByte(parts[i], 16);
        return payload;
    }

    /// <summary>设置编解码器（cmd 0x040E）：[codec(1)]。</summary>
    public static byte[] CodecPayload(byte codec) => new[] { codec };

    /// <summary>功能开关设置（cmd 0x0423）：[value(1)][enable(1)]。</summary>
    public static byte[] FeatureSwitchPayload(byte value, bool enable) =>
        new[] { value, (byte)(enable ? 0x01 : 0x00) };

    /// <summary>查找设备（cmd 0x0435）：[action(1)] 0=停止 1=开始。</summary>
    public static byte[] FindDevicePayload(bool start) => new[] { (byte)(start ? 0x01 : 0x00) };

    /// <summary>设置机型（cmd 0x041F）：[len(1)][Build.Model UTF-8]。</summary>
    public static byte[] BuildModelPayload(string model)
    {
        var b = System.Text.Encoding.UTF8.GetBytes(model ?? "");
        var payload = new byte[1 + b.Length];
        payload[0] = (byte)(b.Length & 0xFF);
        Buffer.BlockCopy(b, 0, payload, 1, b.Length);
        return payload;
    }

    /// <summary>
    /// 解析远程固件版本响应（0x8105）：[status(1)][n(1)][UTF-8 CSV]。
    /// 官方 CommandUtil.k 按逗号分割、每 3 字段为一条 VersionInfo；这里返回原始版本串。
    /// </summary>
    public static string? ParseVersion(byte[] payload)
    {
        if (payload == null || payload.Length < 3 || payload[0] != 0) return null;
        try
        {
            var s = System.Text.Encoding.UTF8.GetString(payload, 2, payload.Length - 2)
                        .TrimEnd('\0').Trim();
            return string.IsNullOrEmpty(s) ? null : s;
        }
        catch { return null; }
    }

    /// <summary>解析编解码器类型响应（0x8114）：[status(1)][n(1)][id,val]×n，返回首个 codec id。</summary>
    public static int ParseCodecType(byte[] payload)
    {
        if (payload == null || payload.Length < 3 || payload[0] != 0) return -1;
        int n = payload[1];
        if (n <= 0 || payload.Length < 2 + 2) return -1;
        return payload[3];   // [id][val] 首对的 val 即当前 codec
    }

    /// <summary>
    /// 解析 0x8103 响应载荷，返回 6 位大写十六进制 productId（匹配 JSON id），失败返回 null。
    /// 格式：[status(1)][productId(3 字节, 小端)]，status!=0 或长度!=4 视为无效。
    /// </summary>
    public static string? ParseProductId(byte[] payload)
    {
        if (payload == null || payload.Length != 4) return null;
        if (payload[0] != 0) return null;  // status 非 0 = 失败
        int id = payload[1] + payload[2] * 256 + payload[3] * 65536;
        return id.ToString("X6");
    }
}
