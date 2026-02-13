namespace Bridge.Data;

/// <summary>
/// Building size tier derived from Discord channel member count.
/// </summary>
public enum BuildingSize
{
    /// <summary>&lt;10 members → 15×15 footprint, 2 floors</summary>
    Small,
    /// <summary>10-30 members → 21×21 footprint, 3 floors (default)</summary>
    Medium,
    /// <summary>30+ members → 27×27 footprint, 4 floors</summary>
    Large
}
