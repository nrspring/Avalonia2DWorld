using Avalonia.Media;

namespace Avalonia2DWorld.Generation.App.ViewModels;

/// <summary>
/// One line of a key under the picture choice: a colour and what it means.
/// <para>
/// A view model type rather than the renderer's own <c>KeyEntry</c>, because the two speak
/// different languages -- the renderer decides in <c>SKColor</c>, which is what it draws with,
/// and a swatch on a panel is an Avalonia brush. This is the one place the translation
/// happens, and the colours themselves still come from the renderer.
/// </para>
/// </summary>
/// <param name="Name">What the colour means.</param>
/// <param name="Swatch">The colour, ready to hang on a Border.</param>
public sealed record KeySwatch(string Name, IBrush Swatch);
