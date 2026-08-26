namespace NWorld.WorldGeneration.App.Services;

public readonly record struct HillGenerationOptions(
    int Count = 10,
    int Seed = 1337,
    int Strength = 18,
    int Width = 26,
    int PassFrequencyPercent = 55);
