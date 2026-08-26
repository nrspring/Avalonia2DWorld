using Avalonia.Controls;
using Avalonia.Platform.Storage;
using NWorld.WorldGeneration.App.Infrastructure;
using NWorld.WorldGeneration.App.ViewModels;

namespace NWorld.WorldGeneration.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel(new MapDocumentDialogService(this));
    }

    private sealed class MapDocumentDialogService(Window owner) : IMapDocumentDialogService
    {
        private static readonly FilePickerFileType MapFileType = new("NWorld Map")
        {
            Patterns = ["*.nworldmap", "*.json"],
            MimeTypes = ["application/json"]
        };

        public async Task<MapDocumentFile?> OpenSaveFileAsync(string suggestedFileName)
        {
            var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Map",
                SuggestedFileName = suggestedFileName,
                DefaultExtension = "nworldmap",
                FileTypeChoices = [MapFileType]
            });

            if (file is null)
            {
                return null;
            }

            return new MapDocumentFile(await file.OpenWriteAsync(), file.Name);
        }

        public async Task<MapDocumentFile?> OpenLoadFileAsync()
        {
            var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Load Map",
                AllowMultiple = false,
                FileTypeFilter = [MapFileType]
            });

            var file = files.Count > 0 ? files[0] : null;
            if (file is null)
            {
                return null;
            }

            return new MapDocumentFile(await file.OpenReadAsync(), file.Name);
        }
    }
}
