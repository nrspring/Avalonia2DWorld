namespace NWorld.WorldGeneration.App.Services;

public readonly record struct MountainGenerationOptions(
    int Count = 12,
    int Seed = 4242,
    int Strength = 18,
    int Radius = 18);
