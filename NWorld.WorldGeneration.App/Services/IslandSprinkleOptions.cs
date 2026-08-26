namespace NWorld.WorldGeneration.App.Services;

public readonly record struct IslandSprinkleOptions(
    int Count = 32,
    int Seed = 2468,
    int MinimumRadius = 3,
    int MaximumRadius = 12,
    int IrregularityPercent = 55);
