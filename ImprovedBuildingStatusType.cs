using Game.Prefabs;

namespace IBLIV
{
    // This mod's improved building status types.
    // Start at an arbitrary large number to avoid overlap with the game's BuildingStatusType and
    // hopefully avoid conflicts with any other mod's building status types.
    public enum ImprovedBuildingStatusType
    {
        None = 987650,

        LevelResidential,
        LevelCommercial,
        LevelIndustrial,
        LevelOffice,

        // This mod uses the game's infomode prefabs for signature buildings.
        // So also use the game's building status types for signature buildings.
        SignatureResidential = BuildingStatusType.SignatureResidential,
        SignatureCommercial  = BuildingStatusType.SignatureCommercial,
        SignatureIndustrial  = BuildingStatusType.SignatureIndustrial,
        SignatureOffice      = BuildingStatusType.SignatureOffice,
    }
}
