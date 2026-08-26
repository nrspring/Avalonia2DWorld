using System.Text.Json;
using System.Text.Json.Serialization;

namespace NWorld.WorldGeneration.App.Services;

public static class MapDocumentSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task SaveAsync(Stream stream, MapDocument document)
    {
        await JsonSerializer.SerializeAsync(stream, document, Options);
    }

    public static async Task<MapDocument?> LoadAsync(Stream stream)
    {
        return await JsonSerializer.DeserializeAsync<MapDocument>(stream, Options);
    }
}
