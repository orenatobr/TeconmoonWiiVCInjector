namespace WiiVCInjector;

/// <summary>User choices for one build (mirrors the WinForms UI state).</summary>
public sealed class BuildOptions
{
    /// <summary>"wii", "gcn", "dol" or "wiiware".</summary>
    public string System { get; set; } = "wii";
    /// <summary>Game ISO/WBFS, GameCube ISO or DOL file (unused for wiiware).</summary>
    public string GamePath { get; set; } = "";
    public string OutputDir { get; set; } = "";
    public string Title1 { get; set; } = "";
    public string? Title2 { get; set; }
    public string IconPath { get; set; } = "";
    public string BannerPath { get; set; } = "";
    public string? DrcPath { get; set; }
    public string? LogoPath { get; set; }
    public string CommonKey { get; set; } = "";
    public string TitleKey { get; set; } = "";
    /// <summary>"1" GamePad off, "65537" GamePad use.</summary>
    public string DrcUse { get; set; } = "65537";
    /// <summary>nfs2iso2nfs patch flags: "", "-horizontal", "-wiimote", "-instantcc" or "-nocc".</summary>
    public string NfsPatch { get; set; } = "";
    public bool LrPatch { get; set; }
    public bool Passthrough { get; set; } = true;
    public bool Wiimmfi { get; set; }
    public bool ForceCC { get; set; }
    public bool DisableTrimming { get; set; }
    public bool Force43 { get; set; }
    public bool ForceInterlaced { get; set; }
    /// <summary>4 character WiiWare/VC title code.</summary>
    public string WiiwareTitle { get; set; } = "";
    public string? BootSoundPath { get; set; }
    public bool BootLoop { get; set; }
    public string? Game2Path { get; set; }
    public string? CustomDolPath { get; set; }
    public bool DisableNintendontAutoboot { get; set; }
    public bool C2wPatch { get; set; }
    /// <summary>Wii U ancast key (hex) for the C2W patch.</summary>
    public string AncastKey { get; set; } = "";
    public bool VideoModeChange { get; set; }
    public VideoTarget VideoTarget { get; set; } = VideoTarget.Ntsc;

    /// <summary>Shallow copy, enough because every member is a value or an immutable string.</summary>
    public BuildOptions Clone() => (BuildOptions)MemberwiseClone();
}
