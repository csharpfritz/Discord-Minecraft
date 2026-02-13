namespace Bridge.Data;

public static class WorldConstants
{
    public const int VillageSpacing = 175;
    public const int GridColumns = 10;
    public const int BuildingFootprint = 21; // Medium default; Small=15, Large=27
    public const int BuildingFloors = 3; // Medium default; Small=2, Large=4
    public const int FloorHeight = 5;
    public const int VillagePlazaRadius = 60;
    public const int MaxBuildingsPerVillage = 16;
    public const int BaseY = -60; // Superflat world surface level (bedrock at -64, dirt -63 to -61, grass -60)
    public const int CrossroadsPlazaRadius = 30; // 61×61 plaza = radius 30
    public const int CrossroadsStationSlots = 16;
    public const int CrossroadsStationRadius = 35;
    public const int VillagePlazaInnerRadius = 15; // 31×31 plaza = radius 15 from center
    public const int VillageStationOffset = 17; // PlazaInnerRadius + 2 block walkway gap
}
