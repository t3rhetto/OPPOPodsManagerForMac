using System.Collections.Generic;

namespace OppoPodsWPF;

public class PodState
{
    public Dictionary<string, (int Level, bool Charging)?> Battery { get; } = new();
    public string AncMode { get; set; } = "?";
    public string EqPreset { get; set; } = "?";
    public string WearingL { get; set; } = "";
    public string WearingR { get; set; } = "";
    public bool Connected { get; set; }
    public bool SpatialSound { get; set; }
    public string SpatialMode { get; set; } = "Off";
    public bool GameMode { get; set; }
    public bool DualDevice { get; set; }
}
