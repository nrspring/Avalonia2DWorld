using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace NWorld.Map.Viewer;

public class TileMapControl : Control
{
    private sealed record HeatMapLegendItem(string Label, Color Color);

    public static readonly StyledProperty<ObservableCollection<MapTile>?> TilesProperty =
        AvaloniaProperty.Register<TileMapControl, ObservableCollection<MapTile>?>(nameof(Tiles));

    public static readonly StyledProperty<ObservableCollection<MapAdornment>?> AdornmentsProperty =
        AvaloniaProperty.Register<TileMapControl, ObservableCollection<MapAdornment>?>(nameof(Adornments));

    public static readonly StyledProperty<ObservableCollection<MapHighlight>?> HighlightsProperty =
        AvaloniaProperty.Register<TileMapControl, ObservableCollection<MapHighlight>?>(nameof(Highlights));

    public static readonly StyledProperty<double> ZoomLevelProperty =
        AvaloniaProperty.Register<TileMapControl, double>(nameof(ZoomLevel), 1.0);

    public static readonly StyledProperty<double> PanUpperRowProperty =
        AvaloniaProperty.Register<TileMapControl, double>(nameof(PanUpperRow), 0.0);

    public static readonly StyledProperty<double> PanLeftColumnProperty =
        AvaloniaProperty.Register<TileMapControl, double>(nameof(PanLeftColumn), 0.0);

    public static readonly StyledProperty<double> PanHeightRowsProperty =
        AvaloniaProperty.Register<TileMapControl, double>(nameof(PanHeightRows), 0.0);

    public static readonly StyledProperty<double> PanWidthColumnsProperty =
        AvaloniaProperty.Register<TileMapControl, double>(nameof(PanWidthColumns), 0.0);

    public static readonly StyledProperty<TileCoordinate?> SelectedTileProperty =
        AvaloniaProperty.Register<TileMapControl, TileCoordinate?>(nameof(SelectedTile));

    public static readonly StyledProperty<ICommand?> TileClickedCommandProperty =
        AvaloniaProperty.Register<TileMapControl, ICommand?>(nameof(TileClickedCommand));

    public static readonly StyledProperty<bool> IsPaintInteractionEnabledProperty =
        AvaloniaProperty.Register<TileMapControl, bool>(nameof(IsPaintInteractionEnabled));

    public static readonly StyledProperty<ICommand?> TilePaintStartedCommandProperty =
        AvaloniaProperty.Register<TileMapControl, ICommand?>(nameof(TilePaintStartedCommand));

    public static readonly StyledProperty<ICommand?> TilePaintedCommandProperty =
        AvaloniaProperty.Register<TileMapControl, ICommand?>(nameof(TilePaintedCommand));

    public static readonly StyledProperty<ICommand?> TilePaintCompletedCommandProperty =
        AvaloniaProperty.Register<TileMapControl, ICommand?>(nameof(TilePaintCompletedCommand));

    public static readonly StyledProperty<bool> FitMapOnTilesChangedProperty =
        AvaloniaProperty.Register<TileMapControl, bool>(nameof(FitMapOnTilesChanged));

    public static readonly StyledProperty<bool> ShowMiniMapProperty =
        AvaloniaProperty.Register<TileMapControl, bool>(nameof(ShowMiniMap), true);

    public static readonly StyledProperty<TileMapDisplayMode> DisplayModeProperty =
        AvaloniaProperty.Register<TileMapControl, TileMapDisplayMode>(nameof(DisplayMode), TileMapDisplayMode.Terrain);

    public static readonly DirectProperty<TileMapControl, int> MapWidthProperty =
        AvaloniaProperty.RegisterDirect<TileMapControl, int>(nameof(MapWidth), control => control.MapWidth);

    public static readonly DirectProperty<TileMapControl, int> MapHeightProperty =
        AvaloniaProperty.RegisterDirect<TileMapControl, int>(nameof(MapHeight), control => control.MapHeight);

    public static readonly DirectProperty<TileMapControl, TileCoordinate?> HoveredTileProperty =
        AvaloniaProperty.RegisterDirect<TileMapControl, TileCoordinate?>(
            nameof(HoveredTile),
            control => control.HoveredTile);

    private const double BaseTileSize = 32.0;
    private const double MinimumZoomLevel = 0.02;
    private const double MaximumZoomLevel = 2.0;
    private const double RasterTerrainMaximumTilePixels = 8.0;
    private const double GridLineMinimumTilePixels = 14.0;
    private const double ContourLineMinimumTilePixels = 18.0;
    private const double WaterAnimationIntervalMs = 90.0;
    private const double MiniMapMaximumWidth = 220.0;
    private const double MiniMapMaximumHeight = 160.0;
    private const double MiniMapMinimumWidth = 120.0;
    private const double MiniMapMargin = 18.0;
    private const double MiniMapPadding = 8.0;
    private const double HeatMapLegendMargin = 18.0;
    private const double HeatMapLegendPadding = 10.0;
    private const double HeatMapLegendWidth = 188.0;
    private const double HeatMapLegendSwatchSize = 12.0;
    private const double HeatMapLegendRowHeight = 18.0;
    private const int VectorTileBudget = 100_000;
    private const int ResourceSpriteCellSize = 64;

    private static readonly Color DeepWaterColor = Color.Parse("#153755");
    private static readonly Color DeepWaterShelfEdgeColor = Color.Parse("#214D68");
    private static readonly Color ShallowWaterColor = Color.Parse("#2F6F8E");
    private static readonly Color ShallowWaterDeepEdgeColor = Color.Parse("#2A6382");
    private static readonly Color ShallowWaterCoastColor = Color.Parse("#3B7D99");
    private static readonly Color WaterAnimationTintColor = Color.Parse("#6EA6B8");
    private static readonly Color ShoreColor = Color.Parse("#B9BA78");
    private static readonly Color LowLandColor = Color.Parse("#476F3B");
    private static readonly Color HillLowColor = Color.Parse("#668643");
    private static readonly Color HillHighColor = Color.Parse("#B2A65E");
    private static readonly Color HighLandColor = Color.Parse("#7FA15A");
    private static readonly Color HeatMapDeepWaterColor = Color.Parse("#000000");
    private static readonly Color HeatMapShallowWaterColor = Color.Parse("#151515");
    private static readonly Color HeatMapLandColor = Color.Parse("#0E4A22");
    private static readonly Color HeatMapHillColor = Color.Parse("#123A1E");
    private static readonly Color HeatMapMountainColor = Color.Parse("#4A0B0B");
    private static readonly Color HeatMapForestColor = Color.Parse("#39FF14");
    private static readonly Color HeatMapIronColor = Color.Parse("#FF7A1A");
    private static readonly Color HeatMapStoneColor = Color.Parse("#CBD6E2");
    private static readonly Color HeatMapCoalColor = Color.Parse("#5D61B3");
    private static readonly Color HeatMapRareMetalsColor = Color.Parse("#E441FF");
    private static readonly Color MissingTileColor = Color.Parse("#101820");
    private static readonly IBrush MissingTileBrush = new SolidColorBrush(MissingTileColor);
    private static readonly IPen GridLinePen = new Pen(new SolidColorBrush(Color.FromArgb(56, 12, 18, 24)), 1);
    private static readonly IBrush MiniMapBackgroundBrush = new SolidColorBrush(Color.FromArgb(226, 18, 24, 32));
    private static readonly IBrush MiniMapViewportFillBrush = new SolidColorBrush(Color.FromArgb(34, 220, 236, 255));
    private static readonly IPen MiniMapBorderPen = new Pen(new SolidColorBrush(Color.FromArgb(180, 92, 115, 138)), 1);
    private static readonly IPen MiniMapViewportPen = new Pen(new SolidColorBrush(Color.FromArgb(230, 221, 238, 255)), 1.5);
    private static readonly IBrush HeatMapLegendBackgroundBrush = new SolidColorBrush(Color.FromArgb(226, 18, 24, 32));
    private static readonly IBrush HeatMapLegendTextBrush = new SolidColorBrush(Color.Parse("#EAF0F6"));
    private static readonly IBrush HeatMapLegendSecondaryTextBrush = new SolidColorBrush(Color.Parse("#AAB8C7"));
    private static readonly IPen HeatMapLegendBorderPen = new Pen(new SolidColorBrush(Color.FromArgb(180, 92, 115, 138)), 1);
    private static readonly IPen HeatMapLegendSwatchBorderPen = new Pen(new SolidColorBrush(Color.FromArgb(140, 232, 239, 247)), 1);
    private static readonly Typeface HeatMapLegendTitleTypeface =
        new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold, FontStretch.Normal);
    private static readonly Typeface HeatMapLegendTypeface =
        new(FontFamily.Default, FontStyle.Normal, FontWeight.Normal, FontStretch.Normal);
    private static readonly Color ContourLineColor = Color.Parse("#26321F");
    private static readonly Color ReliefHighlightColor = Color.Parse("#F4E9A8");
    private static readonly Color ReliefShadowColor = Color.Parse("#1F2B1D");
    private static readonly Color[] LandColors = BuildLandColors();
    private static readonly IReadOnlyDictionary<TileResource, IBrush> ResourceBrushes =
        new Dictionary<TileResource, IBrush>
        {
            [TileResource.Forest] = new SolidColorBrush(Color.Parse("#1F5D35")),
            [TileResource.Iron] = new SolidColorBrush(Color.Parse("#A8AEB6")),
            [TileResource.Stone] = new SolidColorBrush(Color.Parse("#77808B")),
            [TileResource.Coal] = new SolidColorBrush(Color.Parse("#15191F")),
            [TileResource.RareMetals] = new SolidColorBrush(Color.Parse("#A678E2"))
        };
    private static readonly Bitmap? ResourceSpriteSheet = LoadResourceSpriteSheet();
    private static readonly IReadOnlyDictionary<TileResource, Rect> ResourceSpriteSourceRects =
        new Dictionary<TileResource, Rect>
        {
            [TileResource.Forest] = new Rect(0, 0, ResourceSpriteCellSize, ResourceSpriteCellSize),
            [TileResource.Iron] = new Rect(ResourceSpriteCellSize, 0, ResourceSpriteCellSize, ResourceSpriteCellSize),
            [TileResource.Stone] = new Rect(ResourceSpriteCellSize * 2, 0, ResourceSpriteCellSize, ResourceSpriteCellSize),
            [TileResource.Coal] = new Rect(ResourceSpriteCellSize * 3, 0, ResourceSpriteCellSize, ResourceSpriteCellSize),
            [TileResource.RareMetals] = new Rect(ResourceSpriteCellSize * 4, 0, ResourceSpriteCellSize, ResourceSpriteCellSize)
        };
    private static readonly IReadOnlyList<HeatMapLegendItem> HeatMapLegendItems =
    [
        new("Deep water", HeatMapDeepWaterColor),
        new("Shallow water", HeatMapShallowWaterColor),
        new("Land elevation 0-1", HeatMapLandColor),
        new("Hills elevation 2-10", HeatMapHillColor),
        new("Mountains elevation 11+", HeatMapMountainColor),
        new("Forest resource", HeatMapForestColor),
        new("Iron resource", HeatMapIronColor),
        new("Stone resource", HeatMapStoneColor),
        new("Coal resource", HeatMapCoalColor),
        new("Rare metals resource", HeatMapRareMetalsColor)
    ];

    private readonly Dictionary<TileCoordinate, MapTile> _tilesByCoordinate = [];
    private readonly Dictionary<TileCoordinate, List<MapAdornment>> _adornmentsByCoordinate = [];
    private readonly Dictionary<TileCoordinate, List<MapHighlight>> _highlightsByCoordinate = [];

    private bool _indexesDirty = true;
    private bool _terrainBitmapDirty = true;
    private bool _fitMapToViewportPending;
    private bool _fitMapToViewportScheduled;
    private bool _isPointerDown;
    private bool _isDragging;
    private bool _isPanning;
    private bool _isPainting;
    private bool _isMiniMapDragging;
    private Point _lastPointerPosition;
    private Point _pointerDownPosition;
    private TileCoordinate? _lastPaintedTile;
    private int _mapWidth;
    private int _mapHeight;
    private TileCoordinate? _hoveredTile;
    private WriteableBitmap? _terrainBitmap;
    private WriteableBitmap? _waterAnimationOverlayBitmap;
    private DispatcherTimer? _waterAnimationTimer;
    private double _waterAnimationPhase;

    public TileMapControl()
    {
        Tiles = [];
        Adornments = [];
        Highlights = [];
        Focusable = true;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        StartWaterAnimation();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StopWaterAnimation();
        base.OnDetachedFromVisualTree(e);
    }

    public ObservableCollection<MapTile>? Tiles
    {
        get => GetValue(TilesProperty);
        set => SetValue(TilesProperty, value);
    }

    public ObservableCollection<MapAdornment>? Adornments
    {
        get => GetValue(AdornmentsProperty);
        set => SetValue(AdornmentsProperty, value);
    }

    public ObservableCollection<MapHighlight>? Highlights
    {
        get => GetValue(HighlightsProperty);
        set => SetValue(HighlightsProperty, value);
    }

    public double ZoomLevel
    {
        get => GetValue(ZoomLevelProperty);
        set => SetValue(ZoomLevelProperty, Math.Clamp(value, MinimumAllowedZoomLevel, MaximumZoomLevel));
    }

    public double PanUpperRow
    {
        get => GetValue(PanUpperRowProperty);
        set => SetValue(PanUpperRowProperty, value);
    }

    public double PanLeftColumn
    {
        get => GetValue(PanLeftColumnProperty);
        set => SetValue(PanLeftColumnProperty, value);
    }

    public double PanHeightRows
    {
        get => GetValue(PanHeightRowsProperty);
        set => SetValue(PanHeightRowsProperty, value);
    }

    public double PanWidthColumns
    {
        get => GetValue(PanWidthColumnsProperty);
        set => SetValue(PanWidthColumnsProperty, value);
    }

    public TileCoordinate? SelectedTile
    {
        get => GetValue(SelectedTileProperty);
        set => SetValue(SelectedTileProperty, value);
    }

    public ICommand? TileClickedCommand
    {
        get => GetValue(TileClickedCommandProperty);
        set => SetValue(TileClickedCommandProperty, value);
    }

    public bool IsPaintInteractionEnabled
    {
        get => GetValue(IsPaintInteractionEnabledProperty);
        set => SetValue(IsPaintInteractionEnabledProperty, value);
    }

    public ICommand? TilePaintStartedCommand
    {
        get => GetValue(TilePaintStartedCommandProperty);
        set => SetValue(TilePaintStartedCommandProperty, value);
    }

    public ICommand? TilePaintedCommand
    {
        get => GetValue(TilePaintedCommandProperty);
        set => SetValue(TilePaintedCommandProperty, value);
    }

    public ICommand? TilePaintCompletedCommand
    {
        get => GetValue(TilePaintCompletedCommandProperty);
        set => SetValue(TilePaintCompletedCommandProperty, value);
    }

    public bool FitMapOnTilesChanged
    {
        get => GetValue(FitMapOnTilesChangedProperty);
        set => SetValue(FitMapOnTilesChangedProperty, value);
    }

    public bool ShowMiniMap
    {
        get => GetValue(ShowMiniMapProperty);
        set => SetValue(ShowMiniMapProperty, value);
    }

    public TileMapDisplayMode DisplayMode
    {
        get => GetValue(DisplayModeProperty);
        set => SetValue(DisplayModeProperty, value);
    }

    public int MapWidth
    {
        get => _mapWidth;
        private set => SetAndRaise(MapWidthProperty, ref _mapWidth, value);
    }

    public int MapHeight
    {
        get => _mapHeight;
        private set => SetAndRaise(MapHeightProperty, ref _mapHeight, value);
    }

    public TileCoordinate? HoveredTile
    {
        get => _hoveredTile;
        private set => SetAndRaise(HoveredTileProperty, ref _hoveredTile, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        EnsureIndexes();

        if (Bounds.Width <= 0 || Bounds.Height <= 0 || EffectivePanWidthColumns <= 0 || EffectivePanHeightRows <= 0)
        {
            return;
        }

        var firstRow = Math.Max(0, (int)Math.Floor(PanUpperRow));
        var firstColumn = Math.Max(0, (int)Math.Floor(PanLeftColumn));
        var lastRow = Math.Min(MapHeight - 1, (int)Math.Ceiling(PanUpperRow + EffectivePanHeightRows));
        var lastColumn = Math.Min(MapWidth - 1, (int)Math.Ceiling(PanLeftColumn + EffectivePanWidthColumns));

        if (lastRow < firstRow || lastColumn < firstColumn)
        {
            return;
        }

        if (ShouldDrawRasterTerrain(firstRow, firstColumn, lastRow, lastColumn))
        {
            DrawRasterTerrain(context, out var rasterSource, out var rasterDestination);
            if (DisplayMode == TileMapDisplayMode.Terrain)
            {
                DrawRasterWaterAnimation(context, rasterSource, rasterDestination);
            }

            DrawDynamicLayers(context, firstRow, firstColumn, lastRow, lastColumn);
            DrawMiniMap(context);
            DrawHeatMapLegend(context);
            return;
        }

        for (var row = firstRow; row <= lastRow; row++)
        {
            for (var column = firstColumn; column <= lastColumn; column++)
            {
                var coordinate = new TileCoordinate(row, column);
                var destination = GetTileRect(row, column);

                if (_tilesByCoordinate.TryGetValue(coordinate, out var tile))
                {
                    DrawTile(context, tile, destination);
                }
                else
                {
                    context.DrawRectangle(MissingTileBrush, null, destination);
                }

                DrawHighlights(context, coordinate, destination, HighlightLayer.BelowAdornments);
                DrawAdornments(context, coordinate, destination);
                DrawHighlights(context, coordinate, destination, HighlightLayer.AboveAdornments);
            }
        }

        DrawMiniMap(context);
        DrawHeatMapLegend(context);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed && !point.Properties.IsRightButtonPressed)
        {
            return;
        }

        Focus();

        if (point.Properties.IsLeftButtonPressed && TryBeginMiniMapDrag(point.Position))
        {
            if (_isMiniMapDragging)
            {
                e.Pointer.Capture(this);
            }

            e.Handled = true;
            return;
        }

        if (point.Properties.IsLeftButtonPressed && TryBeginPaint(point.Position, e.Pointer))
        {
            e.Handled = true;
            return;
        }

        _isPointerDown = true;
        _isDragging = false;
        _isPanning = point.Properties.IsRightButtonPressed;
        _pointerDownPosition = point.Position;
        _lastPointerPosition = point.Position;
        e.Pointer.Capture(this);
        e.Handled = _isPanning;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var point = e.GetCurrentPoint(this);
        if (_isMiniMapDragging)
        {
            if (!point.Properties.IsLeftButtonPressed)
            {
                _isMiniMapDragging = false;
                e.Pointer.Capture(null);
                e.Handled = true;
                return;
            }

            UpdateMiniMapPan(point.Position);
            e.Handled = true;
            return;
        }

        UpdateHoveredTile(point.Position);

        if (_isPainting)
        {
            if (!point.Properties.IsLeftButtonPressed)
            {
                CompletePaint(e.Pointer);
                e.Handled = true;
                return;
            }

            PaintTileAt(point.Position);
            e.Handled = true;
            return;
        }

        if (!_isPointerDown)
        {
            return;
        }

        var delta = point.Position - _lastPointerPosition;
        if (_isPanning && !point.Properties.IsRightButtonPressed)
        {
            _isPointerDown = false;
            _isPanning = false;
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        if (!_isDragging && GetDistance(point.Position, _pointerDownPosition) > 3)
        {
            _isDragging = true;
        }

        if (_isPanning && _isDragging)
        {
            PanLeftColumn -= delta.X / TilePixelWidth;
            PanUpperRow -= delta.Y / TilePixelHeight;
            ClampPan();
            InvalidateVisual();
        }

        _lastPointerPosition = point.Position;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_isPainting)
        {
            CompletePaint(e.Pointer);
            e.Handled = true;
            return;
        }

        if (_isMiniMapDragging)
        {
            e.Pointer.Capture(null);
            _isMiniMapDragging = false;
            e.Handled = true;
            return;
        }

        if (!_isPointerDown)
        {
            return;
        }

        e.Pointer.Capture(null);
        _isPointerDown = false;
        var wasPanning = _isPanning;
        _isPanning = false;

        if (!wasPanning && !_isDragging && TryGetTileCoordinate(e.GetPosition(this), out var coordinate))
        {
            var command = TileClickedCommand;
            if (command?.CanExecute(coordinate) == true)
            {
                command.Execute(coordinate);
            }
        }

        _isDragging = false;
        e.Handled = wasPanning;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        HoveredTile = null;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (e.Delta.Y == 0)
        {
            return;
        }

        var pointerPosition = e.GetPosition(this);
        var worldColumn = PanLeftColumn + pointerPosition.X / TilePixelWidth;
        var worldRow = PanUpperRow + pointerPosition.Y / TilePixelHeight;
        var zoomFactor = e.Delta.Y > 0 ? 1.1 : 1.0 / 1.1;

        ZoomLevel *= zoomFactor;
        if (Math.Abs(ZoomLevel - MinimumAllowedZoomLevel) < 0.0001)
        {
            PanLeftColumn = 0;
            PanUpperRow = 0;
        }

        UpdatePanSizeFromZoom();

        PanLeftColumn = worldColumn - pointerPosition.X / TilePixelWidth;
        PanUpperRow = worldRow - pointerPosition.Y / TilePixelHeight;
        ClampPan();
        UpdateHoveredTile(pointerPosition);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_fitMapToViewportPending && MapWidth > 0 && MapHeight > 0)
        {
            ScheduleFitMapToViewport();
        }
        else
        {
            var zoom = Math.Clamp(ZoomLevel, MinimumAllowedZoomLevel, MaximumZoomLevel);
            if (Math.Abs(ZoomLevel - zoom) > double.Epsilon)
            {
                ZoomLevel = zoom;
                return;
            }

            UpdatePanSizeFromZoom();
            ClampPan();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TilesProperty)
        {
            Unsubscribe(change.GetOldValue<ObservableCollection<MapTile>?>());
            Subscribe(change.GetNewValue<ObservableCollection<MapTile>?>());
            MarkIndexesDirty(FitMapOnTilesChanged);
        }
        else if (change.Property == AdornmentsProperty)
        {
            Unsubscribe(change.GetOldValue<ObservableCollection<MapAdornment>?>());
            Subscribe(change.GetNewValue<ObservableCollection<MapAdornment>?>());
            MarkIndexesDirty(false);
        }
        else if (change.Property == HighlightsProperty)
        {
            Unsubscribe(change.GetOldValue<ObservableCollection<MapHighlight>?>());
            Subscribe(change.GetNewValue<ObservableCollection<MapHighlight>?>());
            MarkIndexesDirty(false);
        }
        else if (change.Property == ZoomLevelProperty)
        {
            var zoom = Math.Clamp(ZoomLevel, MinimumAllowedZoomLevel, MaximumZoomLevel);
            if (Math.Abs(ZoomLevel - zoom) > double.Epsilon)
            {
                ZoomLevel = zoom;
            }

            UpdatePanSizeFromZoom();
            ClampPan();
            InvalidateVisual();
        }
        else if (change.Property == PanUpperRowProperty ||
                 change.Property == PanLeftColumnProperty ||
                 change.Property == PanHeightRowsProperty ||
                 change.Property == PanWidthColumnsProperty ||
                 change.Property == SelectedTileProperty ||
                 change.Property == ShowMiniMapProperty)
        {
            InvalidateVisual();
        }
        else if (change.Property == DisplayModeProperty)
        {
            _terrainBitmapDirty = true;
            _waterAnimationOverlayBitmap = null;
            InvalidateVisual();
        }
    }

    private double EffectivePanWidthColumns => PanWidthColumns > 0 ? PanWidthColumns : Bounds.Width / ScaledBaseTileSize;

    private double EffectivePanHeightRows => PanHeightRows > 0 ? PanHeightRows : Bounds.Height / ScaledBaseTileSize;

    private double ScaledBaseTileSize => BaseTileSize * Math.Clamp(ZoomLevel, MinimumZoomLevel, MaximumZoomLevel);

    private double TilePixelWidth => Bounds.Width / Math.Max(EffectivePanWidthColumns, 0.0001);

    private double TilePixelHeight => Bounds.Height / Math.Max(EffectivePanHeightRows, 0.0001);

    private double MinimumAllowedZoomLevel => Math.Max(MinimumZoomLevel, GetFitZoomLevel());

    private void Subscribe(INotifyCollectionChanged? collection)
    {
        if (collection is not null)
        {
            collection.CollectionChanged += OnCollectionChanged;
        }
    }

    private void Unsubscribe(INotifyCollectionChanged? collection)
    {
        if (collection is not null)
        {
            collection.CollectionChanged -= OnCollectionChanged;
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var shouldFitMap = sender == Tiles &&
                           FitMapOnTilesChanged &&
                           e.Action is not (NotifyCollectionChangedAction.Replace or NotifyCollectionChangedAction.Move);
        MarkIndexesDirty(shouldFitMap);
    }

    private void MarkIndexesDirty(bool requestFitMap)
    {
        _indexesDirty = true;
        _terrainBitmapDirty = true;
        _waterAnimationOverlayBitmap = null;
        _fitMapToViewportPending |= requestFitMap;
        InvalidateVisual();
    }

    private void EnsureIndexes()
    {
        if (!_indexesDirty)
        {
            return;
        }

        _tilesByCoordinate.Clear();
        _adornmentsByCoordinate.Clear();
        _highlightsByCoordinate.Clear();

        var mapWidth = 0;
        var mapHeight = 0;

        if (Tiles is not null)
        {
            foreach (var tile in Tiles)
            {
                if (tile.Row < 0 || tile.Column < 0)
                {
                    continue;
                }

                _tilesByCoordinate[new TileCoordinate(tile.Row, tile.Column)] = tile;
                mapHeight = Math.Max(mapHeight, tile.Row + 1);
                mapWidth = Math.Max(mapWidth, tile.Column + 1);
            }
        }

        if (Adornments is not null)
        {
            foreach (var adornment in Adornments)
            {
                if (adornment.Row < 0 || adornment.Column < 0)
                {
                    continue;
                }

                AddGrouped(_adornmentsByCoordinate, new TileCoordinate(adornment.Row, adornment.Column), adornment);
            }

            foreach (var adornments in _adornmentsByCoordinate.Values)
            {
                adornments.Sort(static (left, right) => left.DisplayOrder.CompareTo(right.DisplayOrder));
            }
        }

        if (Highlights is not null)
        {
            foreach (var highlight in Highlights)
            {
                if (highlight.Row < 0 || highlight.Column < 0)
                {
                    continue;
                }

                AddGrouped(_highlightsByCoordinate, new TileCoordinate(highlight.Row, highlight.Column), highlight);
            }

            foreach (var highlights in _highlightsByCoordinate.Values)
            {
                highlights.Sort(static (left, right) => left.DisplayOrder.CompareTo(right.DisplayOrder));
            }
        }

        MapWidth = mapWidth;
        MapHeight = mapHeight;
        _terrainBitmapDirty = true;
        _waterAnimationOverlayBitmap = null;

        if (_fitMapToViewportPending && MapWidth > 0 && MapHeight > 0 && Bounds.Width > 0 && Bounds.Height > 0)
        {
            ScheduleFitMapToViewport();
        }

        ClampPan();
        _indexesDirty = false;
    }

    private bool ShouldDrawRasterTerrain(int firstRow, int firstColumn, int lastRow, int lastColumn)
    {
        var visibleTileCount = (lastRow - firstRow + 1) * (lastColumn - firstColumn + 1);
        return _tilesByCoordinate.Count > 0 &&
               (TilePixelWidth <= RasterTerrainMaximumTilePixels ||
                TilePixelHeight <= RasterTerrainMaximumTilePixels ||
                visibleTileCount > VectorTileBudget);
    }

    private void DrawRasterTerrain(DrawingContext context, out Rect source, out Rect destination)
    {
        source = default;
        destination = default;
        EnsureTerrainBitmap();
        if (_terrainBitmap is null)
        {
            return;
        }

        var visibleLeft = Math.Max(0, PanLeftColumn);
        var visibleTop = Math.Max(0, PanUpperRow);
        var visibleRight = Math.Min(MapWidth, PanLeftColumn + EffectivePanWidthColumns);
        var visibleBottom = Math.Min(MapHeight, PanUpperRow + EffectivePanHeightRows);

        if (visibleRight <= visibleLeft || visibleBottom <= visibleTop)
        {
            return;
        }

        source = new Rect(
            visibleLeft,
            visibleTop,
            visibleRight - visibleLeft,
            visibleBottom - visibleTop);
        destination = new Rect(
            (visibleLeft - PanLeftColumn) * TilePixelWidth,
            (visibleTop - PanUpperRow) * TilePixelHeight,
            (visibleRight - visibleLeft) * TilePixelWidth,
            (visibleBottom - visibleTop) * TilePixelHeight);
        context.DrawImage(_terrainBitmap, source, destination);
    }

    private void DrawRasterWaterAnimation(DrawingContext context, Rect source, Rect destination)
    {
        if (source.Width <= 0 || source.Height <= 0 || destination.Width <= 0 || destination.Height <= 0)
        {
            return;
        }

        EnsureWaterAnimationOverlayBitmap();
        if (_waterAnimationOverlayBitmap is null)
        {
            return;
        }

        using (context.PushOpacity(GetWaterAnimationOverlayOpacity()))
        {
            context.DrawImage(_waterAnimationOverlayBitmap, source, destination);
        }
    }

    private void DrawDynamicLayers(DrawingContext context, int firstRow, int firstColumn, int lastRow, int lastColumn)
    {
        if (_adornmentsByCoordinate.Count == 0 && _highlightsByCoordinate.Count == 0)
        {
            return;
        }

        var drawn = new HashSet<TileCoordinate>();
        foreach (var coordinate in _highlightsByCoordinate.Keys)
        {
            if (drawn.Add(coordinate))
            {
                DrawDynamicLayerTile(context, coordinate, firstRow, firstColumn, lastRow, lastColumn);
            }
        }

        foreach (var coordinate in _adornmentsByCoordinate.Keys)
        {
            if (drawn.Add(coordinate))
            {
                DrawDynamicLayerTile(context, coordinate, firstRow, firstColumn, lastRow, lastColumn);
            }
        }
    }

    private void DrawDynamicLayerTile(
        DrawingContext context,
        TileCoordinate coordinate,
        int firstRow,
        int firstColumn,
        int lastRow,
        int lastColumn)
    {
        if (coordinate.Row < firstRow ||
            coordinate.Row > lastRow ||
            coordinate.Column < firstColumn ||
            coordinate.Column > lastColumn)
        {
            return;
        }

        var destination = GetTileRect(coordinate.Row, coordinate.Column);
        DrawHighlights(context, coordinate, destination, HighlightLayer.BelowAdornments);
        DrawAdornments(context, coordinate, destination);
        DrawHighlights(context, coordinate, destination, HighlightLayer.AboveAdornments);
    }

    private void EnsureTerrainBitmap()
    {
        if (!_terrainBitmapDirty && _terrainBitmap is not null)
        {
            return;
        }

        if (MapWidth <= 0 || MapHeight <= 0)
        {
            _terrainBitmap = null;
            _terrainBitmapDirty = false;
            return;
        }

        var pixelSize = new PixelSize(MapWidth, MapHeight);
        var bitmap = new WriteableBitmap(
            pixelSize,
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);

        using (var framebuffer = bitmap.Lock())
        {
            var pixels = new byte[framebuffer.RowBytes * MapHeight];
            for (var row = 0; row < MapHeight; row++)
            {
                var rowOffset = row * framebuffer.RowBytes;
                for (var column = 0; column < MapWidth; column++)
                {
                    var color = GetRenderedTerrainColor(new TileCoordinate(row, column));
                    var offset = rowOffset + column * 4;
                    pixels[offset] = color.B;
                    pixels[offset + 1] = color.G;
                    pixels[offset + 2] = color.R;
                    pixels[offset + 3] = color.A;
                }
            }

            Marshal.Copy(pixels, 0, framebuffer.Address, pixels.Length);
        }

        _terrainBitmap = bitmap;
        _terrainBitmapDirty = false;
    }

    private void EnsureWaterAnimationOverlayBitmap()
    {
        if (_waterAnimationOverlayBitmap is not null)
        {
            return;
        }

        if (MapWidth <= 0 || MapHeight <= 0)
        {
            return;
        }

        var pixelSize = new PixelSize(MapWidth, MapHeight);
        var bitmap = new WriteableBitmap(
            pixelSize,
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        using (var framebuffer = bitmap.Lock())
        {
            var pixels = new byte[framebuffer.RowBytes * MapHeight];
            for (var row = 0; row < MapHeight; row++)
            {
                var rowOffset = row * framebuffer.RowBytes;
                for (var column = 0; column < MapWidth; column++)
                {
                    var coordinate = new TileCoordinate(row, column);
                    if (!_tilesByCoordinate.TryGetValue(coordinate, out var tile) ||
                        tile.Terrain is not (TileTerrain.Water or TileTerrain.DeepWater))
                    {
                        continue;
                    }

                    var alpha = (byte)(tile.Terrain == TileTerrain.Water ? 92 : 58);
                    var offset = rowOffset + column * 4;
                    pixels[offset] = Premultiply(WaterAnimationTintColor.B, alpha);
                    pixels[offset + 1] = Premultiply(WaterAnimationTintColor.G, alpha);
                    pixels[offset + 2] = Premultiply(WaterAnimationTintColor.R, alpha);
                    pixels[offset + 3] = alpha;
                }
            }

            Marshal.Copy(pixels, 0, framebuffer.Address, pixels.Length);
        }

        _waterAnimationOverlayBitmap = bitmap;
    }

    private Color GetRenderedTerrainColor(TileCoordinate coordinate)
    {
        if (!_tilesByCoordinate.TryGetValue(coordinate, out var tile))
        {
            return MissingTileColor;
        }

        if (DisplayMode == TileMapDisplayMode.HeatMap)
        {
            return GetHeatMapTerrainColor(tile);
        }

        var baseColor = tile.Terrain switch
        {
            TileTerrain.DeepWater => GetDeepWaterColor(coordinate),
            TileTerrain.Water => GetWaterColor(coordinate),
            TileTerrain.Land => GetLandColor(coordinate, tile.Elevation),
            _ => MissingTileColor
        };

        var jitter = GetCoordinateJitter(coordinate.Row, coordinate.Column);
        if (tile.Terrain is TileTerrain.Water or TileTerrain.DeepWater)
        {
            jitter += GetWaterAnimationBrightness(coordinate);
        }

        return AdjustBrightness(baseColor, jitter);
    }

    private static Color GetHeatMapTerrainColor(MapTile tile)
    {
        if (tile.Resource != TileResource.None)
        {
            return tile.Resource switch
            {
                TileResource.Forest => HeatMapForestColor,
                TileResource.Iron => HeatMapIronColor,
                TileResource.Stone => HeatMapStoneColor,
                TileResource.Coal => HeatMapCoalColor,
                TileResource.RareMetals => HeatMapRareMetalsColor,
                _ => MissingTileColor
            };
        }

        return tile.Terrain switch
        {
            TileTerrain.DeepWater => HeatMapDeepWaterColor,
            TileTerrain.Water => HeatMapShallowWaterColor,
            TileTerrain.Land when tile.Elevation >= 11 => HeatMapMountainColor,
            TileTerrain.Land when tile.Elevation >= 2 => HeatMapHillColor,
            TileTerrain.Land => HeatMapLandColor,
            _ => MissingTileColor
        };
    }

    private void DrawMiniMap(DrawingContext context)
    {
        if (!ShowMiniMap || MapWidth <= 0 || MapHeight <= 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        EnsureTerrainBitmap();
        if (_terrainBitmap is null)
        {
            return;
        }

        var outerRect = GetMiniMapOuterRect();
        if (outerRect.Width <= 0 || outerRect.Height <= 0)
        {
            return;
        }

        var mapRect = GetMiniMapMapRect(outerRect);
        context.DrawRectangle(MiniMapBackgroundBrush, MiniMapBorderPen, outerRect, 8, 8);
        context.DrawImage(_terrainBitmap, new Rect(0, 0, MapWidth, MapHeight), mapRect);
        context.DrawRectangle(null, MiniMapBorderPen, mapRect);

        var viewportRect = GetMiniMapViewportRect(mapRect);
        if (viewportRect.Width > 0 && viewportRect.Height > 0)
        {
            context.DrawRectangle(MiniMapViewportFillBrush, MiniMapViewportPen, viewportRect);
        }
    }

    private void DrawHeatMapLegend(DrawingContext context)
    {
        if (DisplayMode != TileMapDisplayMode.HeatMap || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var legendHeight = HeatMapLegendPadding * 2 + 22.0 + HeatMapLegendItems.Count * HeatMapLegendRowHeight;
        if (Bounds.Width < HeatMapLegendWidth + HeatMapLegendMargin * 2 ||
            Bounds.Height < legendHeight + HeatMapLegendMargin * 2)
        {
            return;
        }

        var legendRect = new Rect(
            Bounds.Width - HeatMapLegendWidth - HeatMapLegendMargin,
            HeatMapLegendMargin,
            HeatMapLegendWidth,
            legendHeight);

        context.DrawRectangle(HeatMapLegendBackgroundBrush, HeatMapLegendBorderPen, legendRect, 8, 8);

        var title = CreateLegendText("Heat Map", 13.0, HeatMapLegendTextBrush, HeatMapLegendTitleTypeface);
        var originX = legendRect.X + HeatMapLegendPadding;
        var y = legendRect.Y + HeatMapLegendPadding;
        context.DrawText(title, new Point(originX, y));
        y += 24.0;

        foreach (var item in HeatMapLegendItems)
        {
            var swatchRect = new Rect(
                originX,
                y + (HeatMapLegendRowHeight - HeatMapLegendSwatchSize) / 2,
                HeatMapLegendSwatchSize,
                HeatMapLegendSwatchSize);
            context.DrawRectangle(new SolidColorBrush(item.Color), HeatMapLegendSwatchBorderPen, swatchRect, 2, 2);

            var text = CreateLegendText(item.Label, 11.0, HeatMapLegendSecondaryTextBrush, HeatMapLegendTypeface);
            context.DrawText(text, new Point(originX + HeatMapLegendSwatchSize + 8.0, y + 1.0));
            y += HeatMapLegendRowHeight;
        }
    }

    private static FormattedText CreateLegendText(string text, double fontSize, IBrush brush, Typeface typeface)
    {
        return new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            brush);
    }

    private Rect GetMiniMapOuterRect()
    {
        if (Bounds.Width <= MiniMapMargin * 2 || Bounds.Height <= MiniMapMargin * 2)
        {
            return default;
        }

        var availableWidth = Math.Max(0, Bounds.Width - MiniMapMargin * 2);
        var availableHeight = Math.Max(0, Bounds.Height - MiniMapMargin * 2);
        var width = Math.Min(MiniMapMaximumWidth, availableWidth);
        var height = Math.Min(MiniMapMaximumHeight, availableHeight);

        if (width < MiniMapMinimumWidth || height < MiniMapMinimumWidth * 0.55)
        {
            return default;
        }

        return new Rect(
            Bounds.Width - width - MiniMapMargin,
            Bounds.Height - height - MiniMapMargin,
            width,
            height);
    }

    private Rect GetMiniMapMapRect(Rect outerRect)
    {
        var available = outerRect.Deflate(MiniMapPadding);
        if (MapWidth <= 0 || MapHeight <= 0 || available.Width <= 0 || available.Height <= 0)
        {
            return default;
        }

        var mapAspect = MapWidth / (double)MapHeight;
        var availableAspect = available.Width / available.Height;
        double width;
        double height;

        if (mapAspect >= availableAspect)
        {
            width = available.Width;
            height = width / mapAspect;
        }
        else
        {
            height = available.Height;
            width = height * mapAspect;
        }

        return new Rect(
            available.X + (available.Width - width) / 2,
            available.Y + (available.Height - height) / 2,
            width,
            height);
    }

    private Rect GetMiniMapViewportRect(Rect mapRect)
    {
        if (MapWidth <= 0 || MapHeight <= 0 || mapRect.Width <= 0 || mapRect.Height <= 0)
        {
            return default;
        }

        var left = mapRect.Left + Math.Clamp(PanLeftColumn / MapWidth, 0, 1) * mapRect.Width;
        var top = mapRect.Top + Math.Clamp(PanUpperRow / MapHeight, 0, 1) * mapRect.Height;
        var width = Math.Clamp(EffectivePanWidthColumns / MapWidth, 0, 1) * mapRect.Width;
        var height = Math.Clamp(EffectivePanHeightRows / MapHeight, 0, 1) * mapRect.Height;

        return new Rect(left, top, width, height).Intersect(mapRect);
    }

    private bool TryBeginMiniMapDrag(Point position)
    {
        if (!ShowMiniMap || MapWidth <= 0 || MapHeight <= 0)
        {
            return false;
        }

        var outerRect = GetMiniMapOuterRect();
        if (!outerRect.Contains(position))
        {
            return false;
        }

        var mapRect = GetMiniMapMapRect(outerRect);
        if (!mapRect.Contains(position))
        {
            return true;
        }

        _isMiniMapDragging = true;
        UpdateMiniMapPan(position);
        return true;
    }

    private void UpdateMiniMapPan(Point position)
    {
        var mapRect = GetMiniMapMapRect(GetMiniMapOuterRect());
        if (mapRect.Width <= 0 || mapRect.Height <= 0)
        {
            return;
        }

        var centerColumn = Math.Clamp((position.X - mapRect.X) / mapRect.Width, 0, 1) * MapWidth;
        var centerRow = Math.Clamp((position.Y - mapRect.Y) / mapRect.Height, 0, 1) * MapHeight;
        PanLeftColumn = centerColumn - EffectivePanWidthColumns / 2.0;
        PanUpperRow = centerRow - EffectivePanHeightRows / 2.0;
        ClampPan();
        HoveredTile = null;
        InvalidateVisual();
    }

    private static void AddGrouped<T>(
        IDictionary<TileCoordinate, List<T>> groups,
        TileCoordinate coordinate,
        T value)
    {
        if (!groups.TryGetValue(coordinate, out var values))
        {
            values = [];
            groups[coordinate] = values;
        }

        values.Add(value);
    }

    private void UpdatePanSizeFromZoom()
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        PanWidthColumns = Bounds.Width / ScaledBaseTileSize;
        PanHeightRows = Bounds.Height / ScaledBaseTileSize;
    }

    private void FitMapToViewport()
    {
        if (MapWidth <= 0 || MapHeight <= 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        ZoomLevel = Math.Clamp(GetFitZoomLevel(), MinimumZoomLevel, MaximumZoomLevel);
        UpdatePanSizeFromZoom();
        PanLeftColumn = 0;
        PanUpperRow = 0;
    }

    private double GetFitZoomLevel()
    {
        if (MapWidth <= 0 || MapHeight <= 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return MinimumZoomLevel;
        }

        var horizontalZoom = Bounds.Width / (MapWidth * BaseTileSize);
        var verticalZoom = Bounds.Height / (MapHeight * BaseTileSize);
        return Math.Min(horizontalZoom, verticalZoom);
    }

    private void ScheduleFitMapToViewport()
    {
        if (_fitMapToViewportScheduled)
        {
            return;
        }

        _fitMapToViewportScheduled = true;
        Dispatcher.UIThread.Post(() =>
        {
            _fitMapToViewportScheduled = false;
            if (!_fitMapToViewportPending)
            {
                return;
            }

            FitMapToViewport();
            _fitMapToViewportPending = false;
            ClampPan();
            InvalidateVisual();
        }, DispatcherPriority.Render);
    }

    private void StartWaterAnimation()
    {
        _waterAnimationTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(WaterAnimationIntervalMs)
        };
        _waterAnimationTimer.Tick -= OnWaterAnimationTick;
        _waterAnimationTimer.Tick += OnWaterAnimationTick;
        _waterAnimationTimer.Start();
    }

    private void StopWaterAnimation()
    {
        if (_waterAnimationTimer is null)
        {
            return;
        }

        _waterAnimationTimer.Stop();
        _waterAnimationTimer.Tick -= OnWaterAnimationTick;
    }

    private void OnWaterAnimationTick(object? sender, EventArgs e)
    {
        if (_tilesByCoordinate.Count == 0)
        {
            return;
        }

        _waterAnimationPhase = (_waterAnimationPhase + 0.34) % (Math.PI * 2.0);
        InvalidateVisual();
    }

    private void ClampPan()
    {
        if (MapWidth > 0 && EffectivePanWidthColumns > 0)
        {
            PanLeftColumn = Math.Clamp(PanLeftColumn, 0, Math.Max(0, MapWidth - EffectivePanWidthColumns));
        }

        if (MapHeight > 0 && EffectivePanHeightRows > 0)
        {
            PanUpperRow = Math.Clamp(PanUpperRow, 0, Math.Max(0, MapHeight - EffectivePanHeightRows));
        }
    }

    private Rect GetTileRect(int row, int column)
    {
        return new Rect(
            (column - PanLeftColumn) * TilePixelWidth,
            (row - PanUpperRow) * TilePixelHeight,
            TilePixelWidth,
            TilePixelHeight);
    }

    private void UpdateHoveredTile(Point position)
    {
        HoveredTile = TryGetTileCoordinate(position, out var coordinate) ? coordinate : null;
    }

    private bool TryBeginPaint(Point position, IPointer pointer)
    {
        if (!IsPaintInteractionEnabled || !TryGetTileCoordinate(position, out var coordinate))
        {
            return false;
        }

        _isPainting = true;
        _isDragging = false;
        _isPointerDown = false;
        _lastPaintedTile = null;
        ExecuteCommand(TilePaintStartedCommand, coordinate);
        PaintTile(coordinate);
        pointer.Capture(this);
        return true;
    }

    private void PaintTileAt(Point position)
    {
        if (TryGetTileCoordinate(position, out var coordinate))
        {
            PaintTile(coordinate);
        }
    }

    private void PaintTile(TileCoordinate coordinate)
    {
        if (_lastPaintedTile == coordinate)
        {
            return;
        }

        ExecuteCommand(TilePaintedCommand, coordinate);
        _lastPaintedTile = coordinate;
    }

    private void CompletePaint(IPointer pointer)
    {
        pointer.Capture(null);
        _isPainting = false;
        _lastPaintedTile = null;
        ExecuteCommand(TilePaintCompletedCommand, null);
    }

    private static void ExecuteCommand(ICommand? command, object? parameter)
    {
        if (command?.CanExecute(parameter) == true)
        {
            command.Execute(parameter);
        }
    }

    private bool TryGetTileCoordinate(Point position, out TileCoordinate coordinate)
    {
        EnsureIndexes();

        var column = (int)Math.Floor(PanLeftColumn + position.X / TilePixelWidth);
        var row = (int)Math.Floor(PanUpperRow + position.Y / TilePixelHeight);
        coordinate = new TileCoordinate(row, column);

        return row >= 0 &&
               column >= 0 &&
               row < MapHeight &&
               column < MapWidth &&
               _tilesByCoordinate.ContainsKey(coordinate);
    }

    private void DrawTile(DrawingContext context, MapTile tile, Rect destination)
    {
        var coordinate = new TileCoordinate(tile.Row, tile.Column);
        var brush = new SolidColorBrush(GetRenderedTerrainColor(coordinate));
        var pen = Math.Min(destination.Width, destination.Height) >= GridLineMinimumTilePixels ? GridLinePen : null;
        context.DrawRectangle(brush, pen, destination);
        if (DisplayMode == TileMapDisplayMode.Terrain)
        {
            DrawHillRelief(context, tile, destination);
            DrawHillContours(context, tile, destination);
            DrawResource(context, tile.Resource, destination);
        }
    }

    private static double GetDistance(Point first, Point second)
    {
        var x = first.X - second.X;
        var y = first.Y - second.Y;
        return Math.Sqrt(x * x + y * y);
    }

    private Color GetWaterColor(TileCoordinate coordinate)
    {
        var landNeighbors = CountNeighborTerrain(coordinate, TileTerrain.Land);
        if (landNeighbors > 0)
        {
            return Lerp(ShallowWaterColor, ShallowWaterCoastColor, Math.Min(1.0, landNeighbors / 4.0));
        }

        var deepNeighbors = CountNeighborTerrain(coordinate, TileTerrain.DeepWater);
        return deepNeighbors > 0
            ? Lerp(ShallowWaterColor, ShallowWaterDeepEdgeColor, Math.Min(0.75, deepNeighbors * 0.12))
            : ShallowWaterColor;
    }

    private Color GetDeepWaterColor(TileCoordinate coordinate)
    {
        var shallowNeighbors = CountNeighborTerrain(coordinate, TileTerrain.Water);
        return shallowNeighbors > 0
            ? Lerp(DeepWaterColor, DeepWaterShelfEdgeColor, Math.Min(0.75, shallowNeighbors * 0.12))
            : DeepWaterColor;
    }

    private int CountNeighborTerrain(TileCoordinate coordinate, TileTerrain terrain)
    {
        var count = 0;
        for (var rowOffset = -1; rowOffset <= 1; rowOffset++)
        {
            for (var columnOffset = -1; columnOffset <= 1; columnOffset++)
            {
                if (rowOffset == 0 && columnOffset == 0)
                {
                    continue;
                }

                var neighbor = new TileCoordinate(coordinate.Row + rowOffset, coordinate.Column + columnOffset);
                if (_tilesByCoordinate.TryGetValue(neighbor, out var tile) && tile.Terrain == terrain)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private Color GetLandColor(TileCoordinate coordinate, int elevation)
    {
        var color = LandColors[Math.Clamp(elevation, 0, 25)];
        color = ApplyLandRelief(coordinate, color);
        var waterNeighbors = CountNeighborTerrain(coordinate, TileTerrain.Water) +
                             CountNeighborTerrain(coordinate, TileTerrain.DeepWater);

        return waterNeighbors > 0
            ? Lerp(color, ShoreColor, Math.Min(0.55, waterNeighbors * 0.12))
            : color;
    }

    private Color ApplyLandRelief(TileCoordinate coordinate, Color color)
    {
        if (!_tilesByCoordinate.TryGetValue(coordinate, out var tile) || tile.Terrain != TileTerrain.Land)
        {
            return color;
        }

        var northWestAverage = GetLandElevationOrSelf(coordinate.Row - 1, coordinate.Column - 1, tile.Elevation) +
                               GetLandElevationOrSelf(coordinate.Row - 1, coordinate.Column, tile.Elevation) +
                               GetLandElevationOrSelf(coordinate.Row, coordinate.Column - 1, tile.Elevation);
        var southEastAverage = GetLandElevationOrSelf(coordinate.Row + 1, coordinate.Column + 1, tile.Elevation) +
                               GetLandElevationOrSelf(coordinate.Row + 1, coordinate.Column, tile.Elevation) +
                               GetLandElevationOrSelf(coordinate.Row, coordinate.Column + 1, tile.Elevation);
        var directionalRelief = (southEastAverage - northWestAverage) / 3.0;
        var localRelief = Math.Max(0, tile.Elevation - GetAverageCardinalLandElevation(coordinate, tile.Elevation));
        var hillLift = tile.Elevation >= 2 ? (tile.Elevation - 1) * 1.4 : 0;

        return AdjustBrightness(color, directionalRelief * 5.0 + localRelief * 4.0 + hillLift);
    }

    private int GetLandElevationOrSelf(int row, int column, int fallbackElevation)
    {
        return _tilesByCoordinate.TryGetValue(new TileCoordinate(row, column), out var tile) &&
               tile.Terrain == TileTerrain.Land
            ? tile.Elevation
            : fallbackElevation;
    }

    private double GetAverageCardinalLandElevation(TileCoordinate coordinate, int fallbackElevation)
    {
        var total = 0;
        var count = 0;
        Add(coordinate.Row - 1, coordinate.Column);
        Add(coordinate.Row + 1, coordinate.Column);
        Add(coordinate.Row, coordinate.Column - 1);
        Add(coordinate.Row, coordinate.Column + 1);
        return count == 0 ? fallbackElevation : total / (double)count;

        void Add(int row, int column)
        {
            if (_tilesByCoordinate.TryGetValue(new TileCoordinate(row, column), out var tile) &&
                tile.Terrain == TileTerrain.Land)
            {
                total += tile.Elevation;
                count++;
            }
        }
    }

    private void DrawHillRelief(DrawingContext context, MapTile tile, Rect destination)
    {
        if (tile.Terrain != TileTerrain.Land ||
            tile.Elevation < 2 ||
            Math.Min(destination.Width, destination.Height) < ContourLineMinimumTilePixels)
        {
            return;
        }

        var northWestAverage = GetLandElevationOrSelf(tile.Row - 1, tile.Column - 1, tile.Elevation) +
                               GetLandElevationOrSelf(tile.Row - 1, tile.Column, tile.Elevation) +
                               GetLandElevationOrSelf(tile.Row, tile.Column - 1, tile.Elevation);
        var southEastAverage = GetLandElevationOrSelf(tile.Row + 1, tile.Column + 1, tile.Elevation) +
                               GetLandElevationOrSelf(tile.Row + 1, tile.Column, tile.Elevation) +
                               GetLandElevationOrSelf(tile.Row, tile.Column + 1, tile.Elevation);
        var slope = Math.Clamp(Math.Abs(southEastAverage - northWestAverage) / 18.0, 0.10, 0.42);
        var inset = Math.Max(1.0, Math.Min(destination.Width, destination.Height) * 0.08);
        var highlightPen = new Pen(new SolidColorBrush(WithOpacity(ReliefHighlightColor, slope * 0.45)), inset);
        var shadowPen = new Pen(new SolidColorBrush(WithOpacity(ReliefShadowColor, slope * 0.50)), inset);
        var rect = destination.Deflate(inset / 2);

        context.DrawLine(highlightPen, rect.BottomLeft, rect.TopLeft);
        context.DrawLine(highlightPen, rect.TopLeft, rect.TopRight);
        context.DrawLine(shadowPen, rect.TopRight, rect.BottomRight);
        context.DrawLine(shadowPen, rect.BottomRight, rect.BottomLeft);
    }

    private void DrawHillContours(DrawingContext context, MapTile tile, Rect destination)
    {
        if (tile.Terrain != TileTerrain.Land ||
            tile.Elevation < 2 ||
            Math.Min(destination.Width, destination.Height) < ContourLineMinimumTilePixels)
        {
            return;
        }

        var contourLevel = GetContourLevel(tile.Elevation);
        if (contourLevel < 2)
        {
            return;
        }

        var opacity = Math.Clamp((Math.Min(destination.Width, destination.Height) - ContourLineMinimumTilePixels) / 28.0, 0.18, 0.48);
        var pen = new Pen(new SolidColorBrush(WithOpacity(ContourLineColor, opacity)), 1.25);

        DrawContourEdgeIfNeeded(context, pen, tile.Row - 1, tile.Column, contourLevel, destination.TopLeft, destination.TopRight);
        DrawContourEdgeIfNeeded(context, pen, tile.Row + 1, tile.Column, contourLevel, destination.BottomLeft, destination.BottomRight);
        DrawContourEdgeIfNeeded(context, pen, tile.Row, tile.Column - 1, contourLevel, destination.TopLeft, destination.BottomLeft);
        DrawContourEdgeIfNeeded(context, pen, tile.Row, tile.Column + 1, contourLevel, destination.TopRight, destination.BottomRight);
    }

    private void DrawContourEdgeIfNeeded(
        DrawingContext context,
        IPen pen,
        int neighborRow,
        int neighborColumn,
        int contourLevel,
        Point start,
        Point end)
    {
        var neighborLevel = _tilesByCoordinate.TryGetValue(new TileCoordinate(neighborRow, neighborColumn), out var neighbor) &&
                            neighbor.Terrain == TileTerrain.Land
            ? GetContourLevel(neighbor.Elevation)
            : 0;

        if (neighborLevel != contourLevel)
        {
            context.DrawLine(pen, start, end);
        }
    }

    private static int GetContourLevel(int elevation)
    {
        return elevation < 2 ? 0 : Math.Min(10, elevation / 2 * 2);
    }

    private static void DrawResource(DrawingContext context, TileResource resource, Rect destination)
    {
        if (resource == TileResource.None)
        {
            return;
        }

        if (TryDrawResourceSprite(context, resource, destination))
        {
            return;
        }

        if (!ResourceBrushes.TryGetValue(resource, out var brush))
        {
            return;
        }

        var size = Math.Min(destination.Width, destination.Height) * 0.42;
        var resourceRect = new Rect(
            destination.Center.X - size / 2,
            destination.Center.Y - size / 2,
            size,
            size);

        switch (resource)
        {
            case TileResource.Forest:
                context.DrawEllipse(brush, null, destination.Center, size / 2, size / 2);
                break;
            case TileResource.Stone:
            case TileResource.RareMetals:
                using (context.PushTransform(Matrix.CreateRotation(Math.PI / 4, destination.Center)))
                {
                    context.DrawRectangle(brush, null, resourceRect);
                }

                break;
            default:
                context.DrawRectangle(brush, null, resourceRect);
                break;
        }
    }

    private static bool TryDrawResourceSprite(DrawingContext context, TileResource resource, Rect destination)
    {
        if (ResourceSpriteSheet is null || !ResourceSpriteSourceRects.TryGetValue(resource, out var sourceRect))
        {
            return false;
        }

        var inset = Math.Min(destination.Width, destination.Height) * 0.02;
        context.DrawImage(ResourceSpriteSheet, sourceRect, destination.Deflate(inset));
        return true;
    }

    private static Bitmap? LoadResourceSpriteSheet()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://NWorld.Map.Viewer/Assets/Sprites/resource-sprites.png"));
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    private void DrawAdornments(DrawingContext context, TileCoordinate coordinate, Rect destination)
    {
        if (!_adornmentsByCoordinate.TryGetValue(coordinate, out var adornments))
        {
            return;
        }

        foreach (var adornment in adornments)
        {
            if (adornment.Sprite is null || adornment.Opacity <= 0)
            {
                continue;
            }

            using (context.PushOpacity(Math.Clamp(adornment.Opacity, 0, 1)))
            {
                if (adornment.Orientation == MapAdornmentOrientation.Vertical)
                {
                    using (context.PushTransform(Matrix.CreateRotation(Math.PI / 2, destination.Center)))
                    {
                        DrawAdornmentSprite(context, adornment, destination);
                    }
                }
                else
                {
                    DrawAdornmentSprite(context, adornment, destination);
                }
            }
        }
    }

    private static void DrawAdornmentSprite(DrawingContext context, MapAdornment adornment, Rect destination)
    {
        if (adornment.Sprite is null)
        {
            return;
        }

        if (adornment.SourceRect is { } sourceRect)
        {
            context.DrawImage(adornment.Sprite, sourceRect, destination);
        }
        else
        {
            context.DrawImage(adornment.Sprite, destination);
        }
    }

    private void DrawHighlights(
        DrawingContext context,
        TileCoordinate coordinate,
        Rect destination,
        HighlightLayer layer)
    {
        if (!_highlightsByCoordinate.TryGetValue(coordinate, out var highlights))
        {
            return;
        }

        foreach (var highlight in highlights)
        {
            if (highlight.Layer == layer)
            {
                DrawHighlight(context, highlight, destination);
            }
        }
    }

    private static void DrawHighlight(DrawingContext context, MapHighlight highlight, Rect destination)
    {
        var fill = CreateBrush(highlight.Fill, highlight.FillOpacity);
        var stroke = highlight.Stroke ?? highlight.Fill;
        var pen = CreatePen(stroke, highlight.StrokeOpacity, highlight.StrokeThickness);

        switch (highlight.Shape)
        {
            case HighlightShape.Fill:
                context.DrawRectangle(fill, null, destination);
                break;
            case HighlightShape.Outline:
                context.DrawRectangle(null, pen, destination.Deflate(highlight.StrokeThickness / 2));
                break;
            case HighlightShape.Ring:
                context.DrawEllipse(null, pen, destination.Center, destination.Width * 0.38, destination.Height * 0.38);
                break;
            case HighlightShape.CornerBrackets:
                DrawCornerBrackets(context, pen, destination);
                break;
            case HighlightShape.CrossHatch:
                context.DrawRectangle(fill, null, destination);
                DrawCrossHatch(context, pen, destination);
                break;
        }
    }

    private static IBrush? CreateBrush(Color color, double opacity)
    {
        return opacity <= 0 ? null : new SolidColorBrush(WithOpacity(color, opacity));
    }

    private static IPen? CreatePen(Color color, double opacity, double thickness)
    {
        return opacity <= 0 || thickness <= 0 ? null : new Pen(new SolidColorBrush(WithOpacity(color, opacity)), thickness);
    }

    private static Color WithOpacity(Color color, double opacity)
    {
        return Color.FromArgb((byte)(Math.Clamp(opacity, 0, 1) * 255), color.R, color.G, color.B);
    }

    private static void DrawCornerBrackets(DrawingContext context, IPen? pen, Rect destination)
    {
        if (pen is null)
        {
            return;
        }

        var length = Math.Min(destination.Width, destination.Height) * 0.28;
        var rect = destination.Deflate(pen.Thickness / 2);

        context.DrawLine(pen, rect.TopLeft, rect.TopLeft + new Vector(length, 0));
        context.DrawLine(pen, rect.TopLeft, rect.TopLeft + new Vector(0, length));
        context.DrawLine(pen, rect.TopRight, rect.TopRight + new Vector(-length, 0));
        context.DrawLine(pen, rect.TopRight, rect.TopRight + new Vector(0, length));
        context.DrawLine(pen, rect.BottomLeft, rect.BottomLeft + new Vector(length, 0));
        context.DrawLine(pen, rect.BottomLeft, rect.BottomLeft + new Vector(0, -length));
        context.DrawLine(pen, rect.BottomRight, rect.BottomRight + new Vector(-length, 0));
        context.DrawLine(pen, rect.BottomRight, rect.BottomRight + new Vector(0, -length));
    }

    private static void DrawCrossHatch(DrawingContext context, IPen? pen, Rect destination)
    {
        if (pen is null)
        {
            return;
        }

        var step = Math.Max(6, Math.Min(destination.Width, destination.Height) / 4);
        for (var offset = -destination.Height; offset < destination.Width; offset += step)
        {
            var start = new Point(destination.Left + offset, destination.Bottom);
            var end = new Point(destination.Left + offset + destination.Height, destination.Top);
            context.DrawLine(pen, start, end);
        }
    }

    private static IBrush[] BuildLandBrushes()
    {
        var brushes = new IBrush[LandColors.Length];

        for (var elevation = 0; elevation < brushes.Length; elevation++)
        {
            brushes[elevation] = new SolidColorBrush(LandColors[elevation]);
        }

        return brushes;
    }

    private static Color[] BuildLandColors()
    {
        var colors = new Color[26];

        for (var elevation = 0; elevation < colors.Length; elevation++)
        {
            colors[elevation] = elevation switch
            {
                <= 1 => LowLandColor,
                <= 10 => Lerp(HillLowColor, HillHighColor, (elevation - 2) / 8.0),
                _ => Lerp(HillHighColor, HighLandColor, (elevation - 10) / 15.0)
            };
        }

        return colors;
    }

    private static Color Lerp(Color start, Color end, double amount)
    {
        var t = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            (byte)(start.R + (end.R - start.R) * t),
            (byte)(start.G + (end.G - start.G) * t),
            (byte)(start.B + (end.B - start.B) * t));
    }

    private static Color AdjustBrightness(Color color, double amount)
    {
        return Color.FromRgb(
            ClampColor(color.R + amount),
            ClampColor(color.G + amount),
            ClampColor(color.B + amount));
    }

    private double GetWaterAnimationBrightness(TileCoordinate coordinate)
    {
        var wave = Math.Sin(_waterAnimationPhase + coordinate.Row * 0.13 + coordinate.Column * 0.09);
        return wave * 9.0;
    }

    private double GetWaterAnimationOverlayOpacity()
    {
        return 0.32 + (Math.Sin(_waterAnimationPhase) + 1.0) * 0.16;
    }

    private static byte ClampColor(double value)
    {
        return (byte)Math.Clamp((int)Math.Round(value), 0, 255);
    }

    private static byte Premultiply(byte value, byte alpha)
    {
        return (byte)(value * alpha / 255);
    }

    private static double GetCoordinateJitter(int row, int column)
    {
        unchecked
        {
            var hash = (uint)(row * 73856093) ^ (uint)(column * 19349663);
            hash ^= hash >> 13;
            hash *= 1274126177u;
            hash ^= hash >> 16;
            return ((hash & 0xFF) / 255.0 - 0.5) * 14.0;
        }
    }
}
