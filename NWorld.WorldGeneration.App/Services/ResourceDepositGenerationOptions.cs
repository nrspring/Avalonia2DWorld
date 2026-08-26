using NWorld.Map.Viewer;

namespace NWorld.WorldGeneration.App.Services;

public sealed record ResourceDepositGenerationOptions(
    TileResource Resource,
    int Seed,
    int Count,
    int DensityPercent,
    int Width);
