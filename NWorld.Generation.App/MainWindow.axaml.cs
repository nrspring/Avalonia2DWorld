using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using NWorld.Generation.App.Persistence;
using NWorld.Generation.App.ViewModels;

namespace NWorld.Generation.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// The file type these are saved as. Named for what it holds rather than for the app, so
    /// the picker says something useful in the drop-down.
    /// </summary>
    private static FilePickerFileType MapFileType => new("NWorld map")
    {
        Patterns = [$"*.{MapFile.Extension}"],
    };

    /// <summary>
    /// Choosing a file is the window's job, not the view model's: pickers need a top level,
    /// and a view model that opened them would be one that could not run without a window.
    /// The view model is handed a stream and told what to call it.
    /// </summary>
    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel model || model.Tiles is null)
            return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save map",
            // Stamped, and sortable: saves pile up fast while a world is being tuned, and a
            // name that sorts by when it was made is the cheapest way to tell the fifth from
            // the sixth. Seconds included because two saves a minute apart is not the pace
            // anyone works at here.
            SuggestedFileName = $"world-{DateTime.Now.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture)}",
            DefaultExtension = MapFile.Extension,
            FileTypeChoices = [MapFileType],
        });

        if (file is null)
            return;

        await using var stream = await file.OpenWriteAsync();
        model.Save(stream, file.Name);
    }

    /// <inheritdoc cref="OnSaveClick"/>
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
