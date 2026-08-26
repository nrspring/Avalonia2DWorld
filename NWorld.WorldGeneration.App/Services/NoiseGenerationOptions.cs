namespace NWorld.WorldGeneration.App.Services;

public sealed class NoiseGenerationOptions
{
    public int Seed { get; init; }

    public NoiseBlendMode BlendMode { get; init; } = NoiseBlendMode.Fractal;

    public float Frequency { get; init; } = 2.35f;

    public int Octaves { get; init; } = 6;

    public float Persistence { get; init; } = 0.42f;

    public float Lacunarity { get; init; } = 2.15f;

    public float Exponent { get; init; } = 1.35f;

    public float WaterLevelPercent { get; init; } = 34f;

    public int MinimumWaterConnectionWidth { get; init; }

    public float HeightBias { get; init; }

    public bool UseIslandFalloff { get; init; }

    public float IslandStrength { get; init; }

    public float FalloffExponent { get; init; } = 1.65f;
}
