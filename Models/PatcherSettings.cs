namespace RequiemGlamPatcher.Models;

public class PatcherSettings
{
    public string SkyrimDataPath { get; set; } = string.Empty;
    public string OutputPatchPath { get; set; } = string.Empty;
    public string PatchFileName { get; set; } = "GlamPatch.esp";
    public bool AutoDetectSkyrimPath { get; set; } = true;
}
