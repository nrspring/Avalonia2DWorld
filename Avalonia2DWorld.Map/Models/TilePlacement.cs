using System.Collections.Generic;

namespace Avalonia2DWorld.Map.Models
{
    /// <summary>
    /// One tile's worth of arguments to a component draw: where it goes and what it was given.
    /// </summary>
    /// <param name="X">Map coordinate, not a pixel offset.</param>
    /// <param name="Y">Map coordinate, not a pixel offset.</param>
    /// <param name="Params">
    /// The component's own arguments by name, from <see cref="MapRenderComponent.Params"/>.
    /// Usually empty -- <see cref="MapRenderComponent.None"/> rather than null, so a render
    /// function never has to check.
    /// </param>
    public readonly record struct TilePlacement(
        int X, int Y, IReadOnlyDictionary<string, string> Params);
}
