namespace OppoPodsManager;

/// <summary>
/// 音效增强互斥组选项（同一时刻只能生效一个）。
/// 具体哪些项参与互斥由型号 gameSoundMutexes 决定。
/// </summary>
public enum AudioEnhancement
{
    None,
    Eq,
    SpatialSound,
    GameSound,
}
