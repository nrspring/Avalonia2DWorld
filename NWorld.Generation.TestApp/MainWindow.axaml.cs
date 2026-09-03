using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using NWorld.Generation.TestApp.Persistence;
using NWorld.Generation.TestApp.ViewModels;

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
}
