namespace NWorld.WorldGeneration.App.Services;

public sealed record RiverGenerationOptions(
    int Count,
    int Seed,
    int WanderPercent,
    int WideningPercent,
    int SeaTerminationPercent);
