namespace NWorld.Map.Models
{
    /// <summary>
    /// One tile's worth of arguments to a component draw: where it goes and what it was given.
    /// </summary>
    /// <param name="X">Map coordinate, not a pixel offset.</param>
    /// <param name="Y">Map coordinate, not a pixel offset.</param>
    /// <param name="Params">
    /// The component's own arguments, from <see cref="MapRenderComponent.Params"/>. Usually empty.
    /// </param>
    public readonly record struct TilePlacement(int X, int Y, string[] Params);
}
