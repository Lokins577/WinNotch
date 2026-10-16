namespace WinNotch.Features;

public enum FeatureGroup
{
    Island,
    Sound,
    Monitor,
    Windows,
    Input,
    ClipboardAndFiles,
    EnergyAndDisplay,
    Tools,
    Platform
}

public enum FeatureMaturity
{
    Available,
    Experimental,
    Planned,
    Unsupported
}

public sealed record FeatureDescriptor(
    FeatureId Id,
    string DisplayName,
    FeatureGroup Group,
    string Description,
    bool EnabledByDefault,
    FeatureMaturity Maturity = FeatureMaturity.Available,
    IReadOnlyList<FeatureId>? Dependencies = null);
