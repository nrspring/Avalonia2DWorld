namespace NWorld.WorldGeneration.App.Services;

public sealed record LakeGenerationOptions(
    int Count,
    int Seed,
    int MinimumRadius,
    int MaximumRadius,
    int IrregularityPercent);
