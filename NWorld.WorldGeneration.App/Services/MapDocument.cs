using NWorld.Map.Viewer;

namespace NWorld.WorldGeneration.App.Services;

public sealed class MapDocument
{
    public int Version { get; set; } = 1;

    public DateTimeOffset SavedAtUtc { get; set; }

    public MapDocumentSettings Settings { get; set; } = new();

    public MapDocumentMap Map { get; set; } = new();
}

public sealed class MapDocumentSettings
{
    public string MapWidthText { get; set; } = "1500";

    public string MapHeightText { get; set; } = "1000";

    public string NoiseSeedText { get; set; } = "1337";

    public NoiseBlendMode NoiseBlendMode { get; set; } = NoiseBlendMode.Fractal;

    public string NoiseFrequencyText { get; set; } = "2.35";

    public string NoiseOctavesText { get; set; } = "6";

    public string NoisePersistenceText { get; set; } = "0.42";

    public string NoiseLacunarityText { get; set; } = "2.15";

    public string NoiseExponentText { get; set; } = "1.35";

    public string NoiseWaterLevelText { get; set; } = "34";

    public string NoiseMinimumWaterConnectionWidthText { get; set; } = "0";

    public string NoiseHeightBiasText { get; set; } = "0.00";

    public bool NoiseIslandFalloffEnabled { get; set; }

    public string NoiseIslandStrengthText { get; set; } = "0.00";

    public string NoiseFalloffExponentText { get; set; } = "1.65";

    public string IslandCountText { get; set; } = "32";

    public string IslandSeedText { get; set; } = "2468";

    public string IslandMinimumRadiusText { get; set; } = "3";

    public string IslandMaximumRadiusText { get; set; } = "12";

    public string IslandIrregularityText { get; set; } = "55";

    public string LakeCountText { get; set; } = "12";

    public string LakeSeedText { get; set; } = "9753";

    public string LakeMinimumRadiusText { get; set; } = "4";

    public string LakeMaximumRadiusText { get; set; } = "16";

    public string LakeIrregularityText { get; set; } = "60";

    public string RiverCountText { get; set; } = "8";

    public string RiverSeedText { get; set; } = "8642";

    public string RiverWanderText { get; set; } = "55";

    public string RiverWideningText { get; set; } = "18";

    public string RiverSeaTerminationText { get; set; } = "70";

    public string ForestCountText { get; set; } = "18";

    public string ForestSeedText { get; set; } = "11235";

    public string ForestDensityText { get; set; } = "68";

    public string ForestWidthText { get; set; } = "18";

    public string ForestPassFrequencyText { get; set; } = "55";

    public string IronSeedText { get; set; } = "5150";

    public string IronCountText { get; set; } = "8";

    public string IronDensityText { get; set; } = "62";

    public string IronWidthText { get; set; } = "8";

    public string StoneSeedText { get; set; } = "5151";

    public string StoneCountText { get; set; } = "10";

    public string StoneDensityText { get; set; } = "66";

    public string StoneWidthText { get; set; } = "9";

    public string CoalSeedText { get; set; } = "5152";

    public string CoalCountText { get; set; } = "7";

    public string CoalDensityText { get; set; } = "58";

    public string CoalWidthText { get; set; } = "8";

    public string RareMetalsSeedText { get; set; } = "5153";

    public string RareMetalsCountText { get; set; } = "4";

    public string RareMetalsDensityText { get; set; } = "52";

    public string RareMetalsWidthText { get; set; } = "6";

    public string ShallowWaterBufferText { get; set; } = "20";

    public string HillRangeCountText { get; set; } = "10";

    public string HillSeedText { get; set; } = "1337";

    public string HillStrengthText { get; set; } = "18";

    public string HillWidthText { get; set; } = "26";

    public string HillPassFrequencyText { get; set; } = "55";

    public string MountainPeakCountText { get; set; } = "12";

    public string MountainSeedText { get; set; } = "4242";

    public string MountainStrengthText { get; set; } = "18";

    public string MountainRadiusText { get; set; } = "18";

    public TileMapDisplayMode MapDisplayMode { get; set; } = TileMapDisplayMode.Terrain;
}

public sealed class MapDocumentMap
{
    public int Width { get; set; }

    public int Height { get; set; }

    public List<MapDocumentTile> Tiles { get; set; } = [];
}

public sealed class MapDocumentTile
{
    public int Row { get; set; }

    public int Column { get; set; }

    public TileTerrain Terrain { get; set; }

    public int Elevation { get; set; }

    public TileResource Resource { get; set; }
}
