using CommunityToolkit.Mvvm.ComponentModel;

namespace NWorld.Generation.App.ViewModels;

/// <summary>
/// Base for every view model in the app. <see cref="ObservableObject"/> supplies the
/// INotifyPropertyChanged plumbing the [ObservableProperty] generator writes against.
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
}
