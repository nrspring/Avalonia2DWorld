using Avalonia;
using Avalonia.Media;

namespace NWorld.Map.Viewer;

public readonly record struct TileCoordinate(int Row, int Column);

public enum TileTerrain
{
    DeepWater,
    Water,
    Land
}

public enum TileMapDisplayMode
{
    Terrain,
    HeatMap
}

public enum TileResource
{
    None,
    Forest,
    Iron,
    Stone,
    Coal,
    RareMetals
}

public enum MapAdornmentOrientation
{
    Horizontal,
    Vertical
}

public enum HighlightLayer
{
    BelowAdornments,
    AboveAdornments
}

public enum HighlightShape
{
    Fill,
    Outline,
    Ring,
    CornerBrackets,
    CrossHatch
}

public sealed record MapTile(
    int Row,
    int Column,
    TileTerrain Terrain,
    int Elevation,
    TileResource Resource = TileResource.None);

public sealed record MapAdornment(
    int Row,
    int Column,
    IImage? Sprite,
    int DisplayOrder,
    MapAdornmentOrientation Orientation = MapAdornmentOrientation.Horizontal,
    Rect? SourceRect = null,
    double Opacity = 1.0);

public sealed record MapHighlight(
    int Row,
    int Column,
    HighlightShape Shape,
    HighlightLayer Layer,
    int DisplayOrder,
    Color Fill,
    double FillOpacity = 0.25,
    Color? Stroke = null,
    double StrokeOpacity = 1.0,
    double StrokeThickness = 2.0);
