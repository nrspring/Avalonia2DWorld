namespace NWorld.WorldGeneration.App.Infrastructure;

public interface IMapDocumentDialogService
{
    Task<MapDocumentFile?> OpenSaveFileAsync(string suggestedFileName);

    Task<MapDocumentFile?> OpenLoadFileAsync();
}

public sealed record MapDocumentFile(Stream Stream, string Name) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        await Stream.DisposeAsync();
    }
}
