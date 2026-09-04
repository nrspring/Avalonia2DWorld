using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using NWorld.Generation.TestApp.Persistence;
using NWorld.Generation.TestApp.ViewModels;
using NWorld.MapServices.Persistence;

namespace NWorld.Generation.TestApp;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// The file type worlds are saved as. Named for what it holds rather than for the app that
    /// wrote it, so the picker says something useful in the drop-down.
    /// </summary>
    private static FilePickerFileType MapFileType => new("NWorld map")
    {
        Patterns = [$"*.{MapArchive.Extension}"],
    };

    /// <summary>
    /// The file type what has been built is saved as. A separate type and not a second pattern
    /// on the one above, so the two pickers each offer the one kind of file they want and
    /// neither can be used to open the other by mistake.
    /// </summary>
    private static FilePickerFileType EnhancementFileType => new("NWorld enhancements")
    {
        Patterns = [$"*.{EnhancementFile.Extension}"],
    };

    /// <summary>
    /// Choosing a file is the window's job, not the view model's: pickers need a top level,
    /// and a view model that opened them would be one that could not run without a window.
    /// The view model is handed a stream and told what to call it.
    /// </summary>
    private async void OnLoadClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel model)
            return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open map",
            AllowMultiple = false,
            FileTypeFilter = [MapFileType],
        });

        if (files.Count == 0)
            return;

        await using var stream = await files[0].OpenReadAsync();
        model.Load(stream, files[0].Name);
    }

    /// <summary>
    /// Writes what has been built to a file of its own, beside the world rather than into it.
    /// </summary>
    private async void OnSaveEnhancementsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel model)
            return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save what is built",
            SuggestedFileName = model.SuggestedEnhancementName,
            DefaultExtension = EnhancementFile.Extension,
            FileTypeChoices = [EnhancementFileType],
            ShowOverwritePrompt = true,
        });

        if (file is null)
            return;

        // Truncated rather than opened for append. A save writes the whole layer, so a second
        // save of a map with less on it than the first has to leave a shorter file behind.
        await using var stream = await file.OpenWriteAsync();
        stream.SetLength(0);

        model.SaveEnhancements(stream);
    }

    /// <summary>
    /// Lays a saved file of enhancements over the open world, replacing whatever is built on it.
    /// </summary>
    private async void OnLoadEnhancementsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel model)
            return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load what is built",
            AllowMultiple = false,
            FileTypeFilter = [EnhancementFileType],
        });

        if (files.Count == 0)
            return;

        await using var stream = await files[0].OpenReadAsync();
        model.LoadEnhancements(stream, files[0].Name);
    }
}
