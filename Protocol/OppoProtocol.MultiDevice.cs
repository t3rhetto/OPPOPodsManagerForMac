using System;

namespace OppoPodsManager;

/// <summary>多设备管理与操作常量、载荷构造（cmd 0x0429 全系列）。</summary>
public static partial class OppoProtocol
{
    /// <summary>多设备操作类型（cmd 0x0429 payload[0]）。</summary>
    public const byte MultiOpConnect = 0x01;
    public const byte MultiOpDisconnect = 0x02;
    public const byte MultiOpSetPriority = 0x03;
    public const byte MultiOpUnpair = 0x04;

    /// <summary>
    /// 多设备操作载荷（cmd 0x0429）：
    ///   operateType 1/2/3：[type(1)][addr(6)]
    ///   operateType 4：[type(1)][enable(1)][addr(6)]
    /// </summary>
    public static byte[] MultiConnectOpPayload(byte operateType, string targetAddress, bool clearAddress = false)
    {
        var mac = ParseMac(targetAddress);
        if (operateType == MultiOpUnpair)
        {
            var payload = new byte[8];
            payload[0] = operateType;
            payload[1] = (byte)(clearAddress ? 0x00 : 0x01);
            if (!clearAddress) Buffer.BlockCopy(mac, 0, payload, 2, 6);
            return payload;
        }
        else
        {
            var payload = new byte[7];
            payload[0] = operateType;
            Buffer.BlockCopy(mac, 0, payload, 1, 6);
            return payload;
        }
    }

    /// <summary>兼容旧签名：connect=true→连接，false→断开。</summary>
    public static byte[] OperateHandheldPayload(string targetAddress, bool connect) =>
        MultiConnectOpPayload(connect ? MultiOpConnect : MultiOpDisconnect, targetAddress);

    /// <summary>把 "AA:BB:CC:DD:EE:FF" 解析为 6 字节 MAC。</summary>
    private static byte[] ParseMac(string address)
    {
        var mac = new byte[6];
        if (string.IsNullOrEmpty(address)) return mac;
        var parts = address.Split(':');
        for (int i = 0; i < 6 && i < parts.Length; i++)
        {
            try { mac[i] = Convert.ToByte(parts[i], 16); }
            catch { mac[i] = 0; }
        }
        return mac;
    }
}
