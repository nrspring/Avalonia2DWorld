namespace NWorld.WorldGeneration.App.Services;

public sealed record ForestGenerationOptions(
    int Count,
    int Seed,
    int DensityPercent,
    int Width,
    int PassFrequencyPercent);
