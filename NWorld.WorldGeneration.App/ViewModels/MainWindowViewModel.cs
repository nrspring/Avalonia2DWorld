using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using NWorld.Map.Viewer;
using NWorld.WorldGeneration.App.Infrastructure;
using NWorld.WorldGeneration.App.Services;

namespace NWorld.WorldGeneration.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private const int MaximumUndoHistory = 10;

    private readonly IMapDocumentDialogService? _mapDocumentDialogs;
    private readonly List<MapDocument> _undoHistory = [];
    private readonly List<AsyncRelayCommand> _executionCommands = [];
    private readonly RelayCommand _undoCommand;
    private MapDocument? _lastCommittedDocument;

    private string _mapWidthText = "1500";
    private string _mapHeightText = "1000";
    private string _noiseSeedText = "1337";
    private NoiseBlendMode _noiseBlendMode = NoiseBlendMode.Fractal;
    private string _noiseFrequencyText = "2.35";
    private string _noiseOctavesText = "6";
    private string _noisePersistenceText = "0.42";
    private string _noiseLacunarityText = "2.15";
    private string _noiseExponentText = "1.35";
    private string _noiseWaterLevelText = "34";
    private string _noiseMinimumWaterConnectionWidthText = "0";
    private string _noiseHeightBiasText = "0.00";
    private bool _noiseIslandFalloffEnabled;
    private string _noiseIslandStrengthText = "0.00";
    private string _noiseFalloffExponentText = "1.65";
    private string _islandCountText = "32";
    private string _islandSeedText = "2468";
    private string _islandMinimumRadiusText = "3";
    private string _islandMaximumRadiusText = "12";
    private string _islandIrregularityText = "55";
    private string _lakeCountText = "12";
    private string _lakeSeedText = "9753";
    private string _lakeMinimumRadiusText = "4";
    private string _lakeMaximumRadiusText = "16";
    private string _lakeIrregularityText = "60";
    private string _riverCountText = "8";
    private string _riverSeedText = "8642";
    private string _riverWanderText = "55";
    private string _riverWideningText = "18";
    private string _riverSeaTerminationText = "70";
    private string _forestCountText = "18";
    private string _forestSeedText = "11235";
    private string _forestDensityText = "68";
    private string _forestWidthText = "18";
    private string _forestPassFrequencyText = "55";
    private string _ironSeedText = "5150";
    private string _ironCountText = "8";
    private string _ironDensityText = "62";
    private string _ironWidthText = "8";
    private string _stoneSeedText = "5151";
    private string _stoneCountText = "10";
    private string _stoneDensityText = "66";
    private string _stoneWidthText = "9";
    private string _coalSeedText = "5152";
    private string _coalCountText = "7";
    private string _coalDensityText = "58";
    private string _coalWidthText = "8";
    private string _rareMetalsSeedText = "5153";
    private string _rareMetalsCountText = "4";
    private string _rareMetalsDensityText = "52";
    private string _rareMetalsWidthText = "6";
    private string _shallowWaterBufferText = "20";
    private string _hillRangeCountText = "10";
    private string _hillSeedText = "1337";
    private string _hillStrengthText = "18";
    private string _hillWidthText = "26";
    private string _hillPassFrequencyText = "55";
    private string _mountainPeakCountText = "12";
    private string _mountainSeedText = "4242";
    private string _mountainStrengthText = "18";
    private string _mountainRadiusText = "18";
    private string _generationStatus = "No map generated.";
    private bool _isExecutionRunning;
    private bool _isGenerateMapRunning;
    private bool _isSprinkleIslandsRunning;
    private bool _isGenerateLakesRunning;
    private bool _isGenerateRiversRunning;
    private bool _isGenerateForestsRunning;
    private bool _isGenerateIronDepositsRunning;
    private bool _isGenerateStoneDepositsRunning;
    private bool _isGenerateCoalDepositsRunning;
    private bool _isGenerateRareMetalsDepositsRunning;
    private bool _isApplyShallowWaterRunning;
    private bool _isGenerateHillsRunning;
    private bool _isGenerateMountainsRunning;
    private bool _isSaveMapRunning;
    private bool _isLoadMapRunning;
    private double _zoomLevel = 1.0;
    private double _panUpperRow;
    private double _panLeftColumn;
    private double _panHeightRows;
    private double _panWidthColumns;
    private bool _showMiniMap = true;
    private TileMapDisplayMode _mapDisplayMode = TileMapDisplayMode.Terrain;
    private bool _isPaintModeEnabled;
    private bool _paintSessionActive;
    private bool _paintSessionChanged;
    private MapPaintTool _paintTool = MapPaintTool.ShallowWater;
    private readonly HashSet<TileCoordinate> _paintedTilesThisSession = [];
    private TileCoordinate? _hoveredTile;
    private ObservableCollection<MapTile> _tiles = [];
    private ObservableCollection<MapAdornment> _adornments = [];
    private ObservableCollection<MapHighlight> _highlights = [];

    public MainWindowViewModel(IMapDocumentDialogService? mapDocumentDialogs = null)
    {
        _mapDocumentDialogs = mapDocumentDialogs;
        GenerateMapCommand = CreateExecutionCommand(() => RunExecutionAsync(value => IsGenerateMapRunning = value, GenerateMapAsync));
        RandomizeSeedCommand = new RelayCommand(RandomizeSeed);
        SprinkleIslandsCommand = CreateExecutionCommand(() => RunExecutionAsync(value => IsSprinkleIslandsRunning = value, SprinkleIslandsAsync));
        RandomizeIslandSeedCommand = new RelayCommand(RandomizeIslandSeed);
        GenerateLakesCommand = CreateExecutionCommand(() => RunExecutionAsync(value => IsGenerateLakesRunning = value, GenerateLakesAsync));
        RandomizeLakeSeedCommand = new RelayCommand(RandomizeLakeSeed);
        GenerateRiversCommand = CreateExecutionCommand(() => RunExecutionAsync(value => IsGenerateRiversRunning = value, GenerateRiversAsync));
        RandomizeRiverSeedCommand = new RelayCommand(RandomizeRiverSeed);
        GenerateForestsCommand = CreateExecutionCommand(() => RunExecutionAsync(value => IsGenerateForestsRunning = value, GenerateForestsAsync));
        RandomizeForestSeedCommand = new RelayCommand(RandomizeForestSeed);
        GenerateIronDepositsCommand = CreateExecutionCommand(() => RunExecutionAsync(value => IsGenerateIronDepositsRunning = value, () => GenerateResourceDepositsAsync(TileResource.Iron)));
        RandomizeIronSeedCommand = new RelayCommand(RandomizeIronSeed);
        GenerateStoneDepositsCommand = CreateExecutionCommand(() => RunExecutionAsync(value => IsGenerateStoneDepositsRunning = value, () => GenerateResourceDepositsAsync(TileResource.Stone)));
        RandomizeStoneSeedCommand = new RelayCommand(RandomizeStoneSeed);
        GenerateCoalDepositsCommand = CreateExecutionCommand(() => RunExecutionAsync(value => IsGenerateCoalDepositsRunning = value, () => GenerateResourceDepositsAsync(TileResource.Coal)));
        RandomizeCoalSeedCommand = new RelayCommand(RandomizeCoalSeed);
        GenerateRareMetalsDepositsCommand = CreateExecutionCommand(() => RunExecutionAsync(value => IsGenerateRareMetalsDepositsRunning = value, () => GenerateResourceDepositsAsync(TileResource.RareMetals)));
        RandomizeRareMetalsSeedCommand = new RelayCommand(RandomizeRareMetalsSeed);
        ApplyShallowWaterBufferCommand = CreateExecutionCommand(() => RunExecutionAsync(value => IsApplyShallowWaterRunning = value, ApplyShallowWaterBufferAsync));
        GenerateHillsCommand = CreateExecutionCommand(() => RunExecutionAsync(value => IsGenerateHillsRunning = value, GenerateHillsAsync));
        RandomizeHillSeedCommand = new RelayCommand(RandomizeHillSeed);
        GenerateMountainsCommand = CreateExecutionCommand(() => RunExecutionAsync(value => IsGenerateMountainsRunning = value, GenerateMountainsAsync));
        RandomizeMountainSeedCommand = new RelayCommand(RandomizeMountainSeed);
        BeginPaintCommand = new RelayCommand(BeginPaint);
        PaintTileCommand = new RelayCommand<TileCoordinate>(PaintTile);
        CompletePaintCommand = new RelayCommand(CompletePaint);
        SaveMapCommand = CreateExecutionCommand(() => RunExecutionAsync(value => IsSaveMapRunning = value, SaveMapAsync));
        LoadMapCommand = CreateExecutionCommand(() => RunExecutionAsync(value => IsLoadMapRunning = value, LoadMapAsync));
        _undoCommand = new RelayCommand(Undo, () => CanUndo);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<MapTile> Tiles
    {
        get => _tiles;
        private set => SetProperty(ref _tiles, value);
    }

    public ObservableCollection<MapAdornment> Adornments
    {
        get => _adornments;
        private set => SetProperty(ref _adornments, value);
    }

    public ObservableCollection<MapHighlight> Highlights
    {
        get => _highlights;
        private set => SetProperty(ref _highlights, value);
    }

    public IReadOnlyList<NoiseBlendMode> NoiseBlendModes { get; } = Enum.GetValues<NoiseBlendMode>();

    public IReadOnlyList<TileMapDisplayMode> MapDisplayModes { get; } = Enum.GetValues<TileMapDisplayMode>();

    public IReadOnlyList<MapPaintTool> PaintTools { get; } = Enum.GetValues<MapPaintTool>();

    public ICommand GenerateMapCommand { get; }

    public ICommand RandomizeSeedCommand { get; }

    public ICommand SprinkleIslandsCommand { get; }

    public ICommand RandomizeIslandSeedCommand { get; }

    public ICommand GenerateLakesCommand { get; }

    public ICommand RandomizeLakeSeedCommand { get; }

    public ICommand GenerateRiversCommand { get; }

    public ICommand RandomizeRiverSeedCommand { get; }

    public ICommand GenerateForestsCommand { get; }

    public ICommand RandomizeForestSeedCommand { get; }

    public ICommand GenerateIronDepositsCommand { get; }

    public ICommand RandomizeIronSeedCommand { get; }

    public ICommand GenerateStoneDepositsCommand { get; }

    public ICommand RandomizeStoneSeedCommand { get; }

    public ICommand GenerateCoalDepositsCommand { get; }

    public ICommand RandomizeCoalSeedCommand { get; }

    public ICommand GenerateRareMetalsDepositsCommand { get; }

    public ICommand RandomizeRareMetalsSeedCommand { get; }

    public ICommand ApplyShallowWaterBufferCommand { get; }

    public ICommand GenerateHillsCommand { get; }

    public ICommand RandomizeHillSeedCommand { get; }

    public ICommand GenerateMountainsCommand { get; }

    public ICommand RandomizeMountainSeedCommand { get; }

    public ICommand BeginPaintCommand { get; }

    public ICommand PaintTileCommand { get; }

    public ICommand CompletePaintCommand { get; }

    public ICommand SaveMapCommand { get; }

    public ICommand LoadMapCommand { get; }

    public ICommand UndoCommand => _undoCommand;

    public string MapWidthText
    {
        get => _mapWidthText;
        set
        {
            if (SetProperty(ref _mapWidthText, value))
            {
                OnPropertyChanged(nameof(MapMenuSummary));
            }
        }
    }

    public string MapHeightText
    {
        get => _mapHeightText;
        set
        {
            if (SetProperty(ref _mapHeightText, value))
            {
                OnPropertyChanged(nameof(MapMenuSummary));
            }
        }
    }

    public string NoiseSeedText
    {
        get => _noiseSeedText;
        set
        {
            if (SetProperty(ref _noiseSeedText, value))
            {
                OnPropertyChanged(nameof(MapMenuSummary));
            }
        }
    }

    public NoiseBlendMode NoiseBlendMode
    {
        get => _noiseBlendMode;
        set
        {
            if (SetProperty(ref _noiseBlendMode, value))
            {
                OnPropertyChanged(nameof(MapMenuSummary));
            }
        }
    }

    public string NoiseFrequencyText
    {
        get => _noiseFrequencyText;
        set => SetNoiseTextProperty(ref _noiseFrequencyText, value);
    }

    public string NoiseOctavesText
    {
        get => _noiseOctavesText;
        set => SetNoiseTextProperty(ref _noiseOctavesText, value);
    }

    public string NoisePersistenceText
    {
        get => _noisePersistenceText;
        set => SetNoiseTextProperty(ref _noisePersistenceText, value);
    }

    public string NoiseLacunarityText
    {
        get => _noiseLacunarityText;
        set => SetNoiseTextProperty(ref _noiseLacunarityText, value);
    }

    public string NoiseExponentText
    {
        get => _noiseExponentText;
        set => SetNoiseTextProperty(ref _noiseExponentText, value);
    }

    public string NoiseWaterLevelText
    {
        get => _noiseWaterLevelText;
        set => SetNoiseTextProperty(ref _noiseWaterLevelText, value);
    }

    public string NoiseMinimumWaterConnectionWidthText
    {
        get => _noiseMinimumWaterConnectionWidthText;
        set => SetNoiseTextProperty(ref _noiseMinimumWaterConnectionWidthText, value);
    }

    public string NoiseHeightBiasText
    {
        get => _noiseHeightBiasText;
        set => SetNoiseTextProperty(ref _noiseHeightBiasText, value);
    }

    public bool NoiseIslandFalloffEnabled
    {
        get => _noiseIslandFalloffEnabled;
        set => SetProperty(ref _noiseIslandFalloffEnabled, value);
    }

    public string NoiseIslandStrengthText
    {
        get => _noiseIslandStrengthText;
        set => SetNoiseTextProperty(ref _noiseIslandStrengthText, value);
    }

    public string NoiseFalloffExponentText
    {
        get => _noiseFalloffExponentText;
        set => SetNoiseTextProperty(ref _noiseFalloffExponentText, value);
    }

    public string IslandCountText
    {
        get => _islandCountText;
        set => SetIslandTextProperty(ref _islandCountText, value);
    }

    public string IslandSeedText
    {
        get => _islandSeedText;
        set => SetIslandTextProperty(ref _islandSeedText, value);
    }

    public string IslandMinimumRadiusText
    {
        get => _islandMinimumRadiusText;
        set => SetIslandTextProperty(ref _islandMinimumRadiusText, value);
    }

    public string IslandMaximumRadiusText
    {
        get => _islandMaximumRadiusText;
        set => SetIslandTextProperty(ref _islandMaximumRadiusText, value);
    }

    public string IslandIrregularityText
    {
        get => _islandIrregularityText;
        set => SetIslandTextProperty(ref _islandIrregularityText, value);
    }

    public string LakeCountText
    {
        get => _lakeCountText;
        set => SetLakeTextProperty(ref _lakeCountText, value);
    }

    public string LakeSeedText
    {
        get => _lakeSeedText;
        set => SetLakeTextProperty(ref _lakeSeedText, value);
    }

    public string LakeMinimumRadiusText
    {
        get => _lakeMinimumRadiusText;
        set => SetLakeTextProperty(ref _lakeMinimumRadiusText, value);
    }

    public string LakeMaximumRadiusText
    {
        get => _lakeMaximumRadiusText;
        set => SetLakeTextProperty(ref _lakeMaximumRadiusText, value);
    }

    public string LakeIrregularityText
    {
        get => _lakeIrregularityText;
        set => SetLakeTextProperty(ref _lakeIrregularityText, value);
    }

    public string RiverCountText
    {
        get => _riverCountText;
        set => SetRiverTextProperty(ref _riverCountText, value);
    }

    public string RiverSeedText
    {
        get => _riverSeedText;
        set => SetRiverTextProperty(ref _riverSeedText, value);
    }

    public string RiverWanderText
    {
        get => _riverWanderText;
        set => SetRiverTextProperty(ref _riverWanderText, value);
    }

    public string RiverWideningText
    {
        get => _riverWideningText;
        set => SetRiverTextProperty(ref _riverWideningText, value);
    }

    public string RiverSeaTerminationText
    {
        get => _riverSeaTerminationText;
        set => SetRiverTextProperty(ref _riverSeaTerminationText, value);
    }

    public string ForestCountText
    {
        get => _forestCountText;
        set => SetResourceTextProperty(ref _forestCountText, value);
    }

    public string ForestSeedText
    {
        get => _forestSeedText;
        set => SetResourceTextProperty(ref _forestSeedText, value);
    }

    public string ForestDensityText
    {
        get => _forestDensityText;
        set => SetResourceTextProperty(ref _forestDensityText, value);
    }

    public string ForestWidthText
    {
        get => _forestWidthText;
        set => SetResourceTextProperty(ref _forestWidthText, value);
    }

    public string ForestPassFrequencyText
    {
        get => _forestPassFrequencyText;
        set => SetResourceTextProperty(ref _forestPassFrequencyText, value);
    }

    public string IronSeedText
    {
        get => _ironSeedText;
        set => SetResourceTextProperty(ref _ironSeedText, value);
    }

    public string IronCountText
    {
        get => _ironCountText;
        set => SetResourceTextProperty(ref _ironCountText, value);
    }

    public string IronDensityText
    {
        get => _ironDensityText;
        set => SetResourceTextProperty(ref _ironDensityText, value);
    }

    public string IronWidthText
    {
        get => _ironWidthText;
        set => SetResourceTextProperty(ref _ironWidthText, value);
    }

    public string StoneSeedText
    {
        get => _stoneSeedText;
        set => SetResourceTextProperty(ref _stoneSeedText, value);
    }

    public string StoneCountText
    {
        get => _stoneCountText;
        set => SetResourceTextProperty(ref _stoneCountText, value);
    }

    public string StoneDensityText
    {
        get => _stoneDensityText;
        set => SetResourceTextProperty(ref _stoneDensityText, value);
    }

    public string StoneWidthText
    {
        get => _stoneWidthText;
        set => SetResourceTextProperty(ref _stoneWidthText, value);
    }

    public string CoalSeedText
    {
        get => _coalSeedText;
        set => SetResourceTextProperty(ref _coalSeedText, value);
    }

    public string CoalCountText
    {
        get => _coalCountText;
        set => SetResourceTextProperty(ref _coalCountText, value);
    }

    public string CoalDensityText
    {
        get => _coalDensityText;
        set => SetResourceTextProperty(ref _coalDensityText, value);
    }

    public string CoalWidthText
    {
        get => _coalWidthText;
        set => SetResourceTextProperty(ref _coalWidthText, value);
    }

    public string RareMetalsSeedText
    {
        get => _rareMetalsSeedText;
        set => SetResourceTextProperty(ref _rareMetalsSeedText, value);
    }

    public string RareMetalsCountText
    {
        get => _rareMetalsCountText;
        set => SetResourceTextProperty(ref _rareMetalsCountText, value);
    }

    public string RareMetalsDensityText
    {
        get => _rareMetalsDensityText;
        set => SetResourceTextProperty(ref _rareMetalsDensityText, value);
    }

    public string RareMetalsWidthText
    {
        get => _rareMetalsWidthText;
        set => SetResourceTextProperty(ref _rareMetalsWidthText, value);
    }

    public string ShallowWaterBufferText
    {
        get => _shallowWaterBufferText;
        set
        {
            if (SetProperty(ref _shallowWaterBufferText, value))
            {
                OnPropertyChanged(nameof(WaterMenuSummary));
            }
        }
    }

    public string HillRangeCountText
    {
        get => _hillRangeCountText;
        set => SetHillTextProperty(ref _hillRangeCountText, value);
    }

    public string HillSeedText
    {
        get => _hillSeedText;
        set => SetHillTextProperty(ref _hillSeedText, value);
    }

    public string HillStrengthText
    {
        get => _hillStrengthText;
        set => SetHillTextProperty(ref _hillStrengthText, value);
    }

    public string HillWidthText
    {
        get => _hillWidthText;
        set => SetHillTextProperty(ref _hillWidthText, value);
    }

    public string HillPassFrequencyText
    {
        get => _hillPassFrequencyText;
        set => SetHillTextProperty(ref _hillPassFrequencyText, value);
    }

    public string MountainPeakCountText
    {
        get => _mountainPeakCountText;
        set => SetHillTextProperty(ref _mountainPeakCountText, value);
    }

    public string MountainSeedText
    {
        get => _mountainSeedText;
        set => SetHillTextProperty(ref _mountainSeedText, value);
    }

    public string MountainStrengthText
    {
        get => _mountainStrengthText;
        set => SetHillTextProperty(ref _mountainStrengthText, value);
    }

    public string MountainRadiusText
    {
        get => _mountainRadiusText;
        set => SetHillTextProperty(ref _mountainRadiusText, value);
    }

    public string GenerationStatus
    {
        get => _generationStatus;
        private set => SetProperty(ref _generationStatus, value);
    }

    public bool IsGenerateMapRunning
    {
        get => _isGenerateMapRunning;
        private set => SetProperty(ref _isGenerateMapRunning, value);
    }

    public bool IsSprinkleIslandsRunning
    {
        get => _isSprinkleIslandsRunning;
        private set => SetProperty(ref _isSprinkleIslandsRunning, value);
    }

    public bool IsGenerateLakesRunning
    {
        get => _isGenerateLakesRunning;
        private set => SetProperty(ref _isGenerateLakesRunning, value);
    }

    public bool IsGenerateRiversRunning
    {
        get => _isGenerateRiversRunning;
        private set => SetProperty(ref _isGenerateRiversRunning, value);
    }

    public bool IsGenerateForestsRunning
    {
        get => _isGenerateForestsRunning;
        private set => SetProperty(ref _isGenerateForestsRunning, value);
    }

    public bool IsGenerateIronDepositsRunning
    {
        get => _isGenerateIronDepositsRunning;
        private set => SetProperty(ref _isGenerateIronDepositsRunning, value);
    }

    public bool IsGenerateStoneDepositsRunning
    {
        get => _isGenerateStoneDepositsRunning;
        private set => SetProperty(ref _isGenerateStoneDepositsRunning, value);
    }

    public bool IsGenerateCoalDepositsRunning
    {
        get => _isGenerateCoalDepositsRunning;
        private set => SetProperty(ref _isGenerateCoalDepositsRunning, value);
    }

    public bool IsGenerateRareMetalsDepositsRunning
    {
        get => _isGenerateRareMetalsDepositsRunning;
        private set => SetProperty(ref _isGenerateRareMetalsDepositsRunning, value);
    }

    public bool IsApplyShallowWaterRunning
    {
        get => _isApplyShallowWaterRunning;
        private set => SetProperty(ref _isApplyShallowWaterRunning, value);
    }

    public bool IsGenerateHillsRunning
    {
        get => _isGenerateHillsRunning;
        private set => SetProperty(ref _isGenerateHillsRunning, value);
    }

    public bool IsGenerateMountainsRunning
    {
        get => _isGenerateMountainsRunning;
        private set => SetProperty(ref _isGenerateMountainsRunning, value);
    }

    public bool IsSaveMapRunning
    {
        get => _isSaveMapRunning;
        private set => SetProperty(ref _isSaveMapRunning, value);
    }

    public bool IsLoadMapRunning
    {
        get => _isLoadMapRunning;
        private set => SetProperty(ref _isLoadMapRunning, value);
    }

    private bool IsExecutionRunning
    {
        get => _isExecutionRunning;
        set
        {
            if (SetProperty(ref _isExecutionRunning, value))
            {
                NotifyExecutionCommandStatesChanged();
            }
        }
    }

    public string MapMenuSummary => Tiles.Count == 0
        ? $"{MapWidthText} x {MapHeightText} pending"
        : $"{MapWidth} x {MapHeight} • Islands {IslandCountText} • Water {NoiseWaterLevelText}%";

    public string WaterMenuSummary => Tiles.Count == 0 ? "No map" : $"Shallow {ShallowWaterBufferText} tiles";

    public string LakesMenuSummary => Tiles.Count == 0 ? "No map" : $"Lakes {LakeCountText} • {LakeMinimumRadiusText}-{LakeMaximumRadiusText}";

    public string RiversMenuSummary => Tiles.Count == 0 ? "No map" : $"Rivers {RiverCountText} • Sea {RiverSeaTerminationText}%";

    public string ResourcesMenuSummary => Tiles.Count == 0
        ? "No map"
        : $"Trees {ForestCountText} • Ore {IronCountText}/{StoneCountText}/{CoalCountText}/{RareMetalsCountText}";

    public string HillsMenuSummary => Tiles.Count == 0 ? "No map" : $"Hills {HillRangeCountText} • Mountains {MountainPeakCountText}";

    public string ViewMenuSummary => $"{MapDisplayMode} • Minimap {(ShowMiniMap ? "On" : "Off")}";

    public string PaintMenuSummary => IsPaintModeEnabled ? $"{PaintTool} brush active" : "Paint off";

    public bool IsIslandPaintOptionsVisible => IsPaintModeEnabled && PaintTool == MapPaintTool.Islands;

    public bool IsLakePaintOptionsVisible => IsPaintModeEnabled && PaintTool == MapPaintTool.Lakes;

    public bool CanUndo => _undoHistory.Count > 0;

    public int MapWidth { get; private set; }

    public int MapHeight { get; private set; }

    public double ZoomLevel
    {
        get => _zoomLevel;
        set => SetProperty(ref _zoomLevel, value);
    }

    public double PanUpperRow
    {
        get => _panUpperRow;
        set => SetProperty(ref _panUpperRow, value);
    }

    public double PanLeftColumn
    {
        get => _panLeftColumn;
        set => SetProperty(ref _panLeftColumn, value);
    }

    public double PanHeightRows
    {
        get => _panHeightRows;
        set => SetProperty(ref _panHeightRows, value);
    }

    public double PanWidthColumns
    {
        get => _panWidthColumns;
        set => SetProperty(ref _panWidthColumns, value);
    }

    public bool ShowMiniMap
    {
        get => _showMiniMap;
        set
        {
            if (SetProperty(ref _showMiniMap, value))
            {
                OnPropertyChanged(nameof(ViewMenuSummary));
            }
        }
    }

    public TileMapDisplayMode MapDisplayMode
    {
        get => _mapDisplayMode;
        set
        {
            if (SetProperty(ref _mapDisplayMode, value))
            {
                OnPropertyChanged(nameof(ViewMenuSummary));
            }
        }
    }

    public bool IsPaintModeEnabled
    {
        get => _isPaintModeEnabled;
        set
        {
            if (SetProperty(ref _isPaintModeEnabled, value))
            {
                OnPropertyChanged(nameof(PaintMenuSummary));
                OnPropertyChanged(nameof(IsIslandPaintOptionsVisible));
                OnPropertyChanged(nameof(IsLakePaintOptionsVisible));
            }
        }
    }

    public MapPaintTool PaintTool
    {
        get => _paintTool;
        set
        {
            if (SetProperty(ref _paintTool, value))
            {
                OnPropertyChanged(nameof(PaintMenuSummary));
                OnPropertyChanged(nameof(IsIslandPaintOptionsVisible));
                OnPropertyChanged(nameof(IsLakePaintOptionsVisible));
            }
        }
    }

    public TileCoordinate? HoveredTile
    {
        get => _hoveredTile;
        set => SetProperty(ref _hoveredTile, value);
    }

    private async Task GenerateMapAsync()
    {
        if (!TryReadMapSize(out var width, out var height) ||
            !TryBuildNoiseOptions(out var options))
        {
            return;
        }

        var tiles = await Task.Run(() => BaseLandGenerator.Generate(width, height, options));

        PushUndoSnapshot();
        Tiles = new ObservableCollection<MapTile>(tiles);
        Adornments = [];
        Highlights = [];
        MapWidth = width;
        MapHeight = height;
        PanUpperRow = 0;
        PanLeftColumn = 0;
        GenerationStatus = $"Generated {width} x {height} continent map with seed {options.Seed}.";
        OnPropertyChanged(nameof(MapWidth));
        OnPropertyChanged(nameof(MapHeight));
        OnPropertyChanged(nameof(MapMenuSummary));
        OnPropertyChanged(nameof(WaterMenuSummary));
        OnPropertyChanged(nameof(LakesMenuSummary));
        OnPropertyChanged(nameof(RiversMenuSummary));
        OnPropertyChanged(nameof(ResourcesMenuSummary));
        OnPropertyChanged(nameof(HillsMenuSummary));
        CommitCurrentMapState();
    }

    private void RandomizeSeed()
    {
        NoiseSeedText = Random.Shared.Next(1, int.MaxValue).ToString(CultureInfo.InvariantCulture);
    }

    private async Task SaveMapAsync()
    {
        if (_mapDocumentDialogs is null)
        {
            GenerationStatus = "Save is not available in this host.";
            return;
        }

        if (Tiles.Count == 0 || MapWidth <= 0 || MapHeight <= 0)
        {
            GenerationStatus = "Generate or load a map before saving.";
            return;
        }

        var suggestedFileName = $"nworld-map-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.nworldmap";

        try
        {
            await using var file = await _mapDocumentDialogs.OpenSaveFileAsync(suggestedFileName);
            if (file is null)
            {
                return;
            }

            await MapDocumentSerializer.SaveAsync(file.Stream, CreateMapDocument());
            GenerationStatus = $"Saved map to {file.Name}.";
        }
        catch (Exception ex)
        {
            GenerationStatus = $"Save failed: {ex.Message}";
        }
    }

    private async Task LoadMapAsync()
    {
        if (_mapDocumentDialogs is null)
        {
            GenerationStatus = "Load is not available in this host.";
            return;
        }

        try
        {
            await using var file = await _mapDocumentDialogs.OpenLoadFileAsync();
            if (file is null)
            {
                return;
            }

            var document = await MapDocumentSerializer.LoadAsync(file.Stream);
            if (document?.Map?.Tiles is null)
            {
                GenerationStatus = "Load failed: the selected file is not a valid map document.";
                return;
            }

            PushUndoSnapshot();
            ApplyMapDocument(document);
            CommitCurrentMapState();
            GenerationStatus = $"Loaded map from {file.Name}.";
        }
        catch (Exception ex)
        {
            GenerationStatus = $"Load failed: {ex.Message}";
        }
    }

    private void RandomizeIslandSeed()
    {
        IslandSeedText = Random.Shared.Next(1, int.MaxValue).ToString(CultureInfo.InvariantCulture);
        GenerationStatus = "Island seed randomized.";
    }

    private void RandomizeLakeSeed()
    {
        LakeSeedText = Random.Shared.Next(1, int.MaxValue).ToString(CultureInfo.InvariantCulture);
        GenerationStatus = "Lake seed randomized.";
    }

    private void RandomizeRiverSeed()
    {
        RiverSeedText = Random.Shared.Next(1, int.MaxValue).ToString(CultureInfo.InvariantCulture);
        GenerationStatus = "River seed randomized.";
    }

    private void RandomizeForestSeed()
    {
        ForestSeedText = Random.Shared.Next(1, int.MaxValue).ToString(CultureInfo.InvariantCulture);
        GenerationStatus = "Forest seed randomized.";
    }

    private void RandomizeIronSeed()
    {
        IronSeedText = Random.Shared.Next(1, int.MaxValue).ToString(CultureInfo.InvariantCulture);
        GenerationStatus = "Iron seed randomized.";
    }

    private void RandomizeStoneSeed()
    {
        StoneSeedText = Random.Shared.Next(1, int.MaxValue).ToString(CultureInfo.InvariantCulture);
        GenerationStatus = "Stone seed randomized.";
    }

    private void RandomizeCoalSeed()
    {
        CoalSeedText = Random.Shared.Next(1, int.MaxValue).ToString(CultureInfo.InvariantCulture);
        GenerationStatus = "Coal seed randomized.";
    }

    private void RandomizeRareMetalsSeed()
    {
        RareMetalsSeedText = Random.Shared.Next(1, int.MaxValue).ToString(CultureInfo.InvariantCulture);
        GenerationStatus = "Rare metals seed randomized.";
    }

    private async Task SprinkleIslandsAsync()
    {
        if (Tiles.Count == 0 || MapWidth <= 0 || MapHeight <= 0)
        {
            GenerationStatus = "Generate continents before sprinkling islands.";
            return;
        }

        if (!TryBuildIslandOptions(out var options))
        {
            return;
        }

        if (options.Count == 0)
        {
            GenerationStatus = "Island count is 0, so no islands were added.";
            return;
        }

        var sourceTiles = Tiles.ToList();
        var mapWidth = MapWidth;
        var mapHeight = MapHeight;
        var result = await Task.Run(() => IslandSprinkler.Sprinkle(sourceTiles, mapWidth, mapHeight, options));
        if (result.PlacedIslandCount == 0 || result.AddedLandTileCount == 0)
        {
            GenerationStatus = "No islands were placed. Try fewer islands, smaller radii, or a different seed.";
            return;
        }

        PushUndoSnapshot();
        Tiles = new ObservableCollection<MapTile>(result.Tiles);
        GenerationStatus = $"Sprinkled {result.PlacedIslandCount} islands with seed {options.Seed}, adding {result.AddedLandTileCount} land tiles.";
        OnPropertyChanged(nameof(MapMenuSummary));
        OnPropertyChanged(nameof(WaterMenuSummary));
        OnPropertyChanged(nameof(LakesMenuSummary));
        OnPropertyChanged(nameof(RiversMenuSummary));
        OnPropertyChanged(nameof(ResourcesMenuSummary));
        OnPropertyChanged(nameof(HillsMenuSummary));
        CommitCurrentMapState();
    }

    private async Task GenerateLakesAsync()
    {
        if (Tiles.Count == 0 || MapWidth <= 0 || MapHeight <= 0)
        {
            GenerationStatus = "Generate a map before placing lakes.";
            return;
        }

        if (!TryBuildLakeOptions(out var options))
        {
            return;
        }

        if (options.Count == 0)
        {
            GenerationStatus = "Lake count is 0, so no lakes were placed.";
            return;
        }

        var sourceTiles = Tiles.ToList();
        var mapWidth = MapWidth;
        var mapHeight = MapHeight;
        var result = await Task.Run(() => LakeGenerator.Generate(sourceTiles, mapWidth, mapHeight, options));
        if (result.PlacedLakeCount == 0 || result.AddedWaterTileCount == 0)
        {
            GenerationStatus = "No lakes were placed. Try fewer lakes, smaller radii, or more inland land away from mountains.";
            return;
        }

        PushUndoSnapshot();
        Tiles = new ObservableCollection<MapTile>(result.Tiles);
        GenerationStatus = $"Placed {result.PlacedLakeCount} lakes with seed {options.Seed}, converting {result.AddedWaterTileCount} land tiles. Lowest lake elevation is {result.MinimumLakeElevation}.";
        OnPropertyChanged(nameof(WaterMenuSummary));
        OnPropertyChanged(nameof(LakesMenuSummary));
        OnPropertyChanged(nameof(RiversMenuSummary));
        OnPropertyChanged(nameof(ResourcesMenuSummary));
        OnPropertyChanged(nameof(HillsMenuSummary));
        CommitCurrentMapState();
    }

    private async Task GenerateRiversAsync()
    {
        if (Tiles.Count == 0 || MapWidth <= 0 || MapHeight <= 0)
        {
            GenerationStatus = "Generate a map and hills before creating rivers.";
            return;
        }

        if (!Tiles.Any(tile => tile.Terrain == TileTerrain.Land && tile.Elevation is >= 2 and < 11))
        {
            GenerationStatus = "Create hills before creating rivers. Rivers start in hill areas.";
            return;
        }

        if (!TryBuildRiverOptions(out var options))
        {
            return;
        }

        if (options.Count == 0)
        {
            GenerationStatus = "River count is 0, so no rivers were created.";
            return;
        }

        var sourceTiles = Tiles.ToList();
        var mapWidth = MapWidth;
        var mapHeight = MapHeight;
        var result = await Task.Run(() => RiverGenerator.Generate(sourceTiles, mapWidth, mapHeight, options));
        if (result.PlacedRiverCount == 0 || result.AddedWaterTileCount == 0)
        {
            GenerationStatus = "No rivers were created. Try adding more hills, reducing mountains, or increasing wander.";
            return;
        }

        PushUndoSnapshot();
        Tiles = new ObservableCollection<MapTile>(result.Tiles);
        GenerationStatus = $"Created {result.PlacedRiverCount} rivers with seed {options.Seed}, converting {result.AddedWaterTileCount} tiles to water.";
        OnPropertyChanged(nameof(WaterMenuSummary));
        OnPropertyChanged(nameof(LakesMenuSummary));
        OnPropertyChanged(nameof(RiversMenuSummary));
        OnPropertyChanged(nameof(ResourcesMenuSummary));
        OnPropertyChanged(nameof(HillsMenuSummary));
        CommitCurrentMapState();
    }

    private async Task GenerateForestsAsync()
    {
        if (Tiles.Count == 0 || MapWidth <= 0 || MapHeight <= 0)
        {
            GenerationStatus = "Generate a map before placing forest resources.";
            return;
        }

        if (!TryBuildForestOptions(out var options))
        {
            return;
        }

        if (options.Count == 0)
        {
            GenerationStatus = "Forest count is 0, so no forests were placed.";
            return;
        }

        var sourceTiles = Tiles.ToList();
        var mapWidth = MapWidth;
        var mapHeight = MapHeight;
        var result = await Task.Run(() => ForestGenerator.Generate(sourceTiles, mapWidth, mapHeight, options));
        if (result.PlannedForestCount == 0 || result.ForestTileCount == 0)
        {
            GenerationStatus = "No forests were placed. Try more land or hills, fewer mountains, or wider forests.";
            return;
        }

        PushUndoSnapshot();
        Tiles = new ObservableCollection<MapTile>(result.Tiles);
        GenerationStatus = $"Placed {result.PlannedForestCount} forest regions with seed {options.Seed}, adding {result.ForestTileCount} tree tiles.";
        OnPropertyChanged(nameof(ResourcesMenuSummary));
        CommitCurrentMapState();
    }

    private async Task GenerateResourceDepositsAsync(TileResource resource)
    {
        if (Tiles.Count == 0 || MapWidth <= 0 || MapHeight <= 0)
        {
            GenerationStatus = $"Generate a map before placing {GetResourceDisplayName(resource).ToLowerInvariant()} deposits.";
            return;
        }

        if (!TryBuildResourceDepositOptions(resource, out var options))
        {
            return;
        }

        if (options.Count == 0)
        {
            GenerationStatus = $"{GetResourceDisplayName(resource)} count is 0, so no deposits were placed.";
            return;
        }

        var sourceTiles = Tiles.ToList();
        var mapWidth = MapWidth;
        var mapHeight = MapHeight;
        var result = await Task.Run(() => ResourceDepositGenerator.Generate(sourceTiles, mapWidth, mapHeight, options));
        var placedCount = GetPlacedResourceCount(result, resource);
        if (placedCount == 0)
        {
            GenerationStatus = $"No {GetResourceDisplayName(resource).ToLowerInvariant()} deposits were placed. Try more eligible land or fewer existing resources.";
            return;
        }

        PushUndoSnapshot();
        Tiles = new ObservableCollection<MapTile>(result.Tiles);
        GenerationStatus = $"Placed {placedCount} {GetResourceDisplayName(resource).ToLowerInvariant()} deposit tiles with seed {options.Seed}.";
        OnPropertyChanged(nameof(ResourcesMenuSummary));
        CommitCurrentMapState();
    }

    private void RandomizeHillSeed()
    {
        HillSeedText = Random.Shared.Next(1, int.MaxValue).ToString(CultureInfo.InvariantCulture);
        GenerationStatus = "Hill seed randomized.";
    }

    private async Task GenerateHillsAsync()
    {
        if (Tiles.Count == 0 || MapWidth <= 0 || MapHeight <= 0)
        {
            GenerationStatus = "Generate a map before raising hill ranges.";
            return;
        }

        if (!TryBuildHillOptions(out var options))
        {
            return;
        }

        if (options.Count == 0)
        {
            GenerationStatus = "Hill range count is 0, so no elevation changes were made.";
            return;
        }

        var sourceTiles = Tiles.ToList();
        var mapWidth = MapWidth;
        var mapHeight = MapHeight;
        var result = await Task.Run(() => HillGenerator.Generate(sourceTiles, mapWidth, mapHeight, options));
        if (result.PlannedRangeCount == 0)
        {
            GenerationStatus = "Could not find enough land room for hill ranges. Try defining more land first.";
            return;
        }

        if (result.RaisedTileCount == 0)
        {
            GenerationStatus = "Hill ranges were planned, but no land tiles were raised. Try a wider or stronger range setting.";
            return;
        }

        PushUndoSnapshot();
        Tiles = new ObservableCollection<MapTile>(result.Tiles);
        GenerationStatus = $"Raised {result.PlannedRangeCount} hill ranges with seed {options.Seed}, changing {result.RaisedTileCount} land tiles. Peak elevation is {result.PeakElevation}.";
        OnPropertyChanged(nameof(HillsMenuSummary));
        CommitCurrentMapState();
    }

    private void RandomizeMountainSeed()
    {
        MountainSeedText = Random.Shared.Next(1, int.MaxValue).ToString(CultureInfo.InvariantCulture);
        GenerationStatus = "Mountain seed randomized.";
    }

    private async Task GenerateMountainsAsync()
    {
        if (Tiles.Count == 0 || MapWidth <= 0 || MapHeight <= 0)
        {
            GenerationStatus = "Generate a map and hills before creating mountains.";
            return;
        }

        if (!Tiles.Any(tile => tile.Terrain == TileTerrain.Land && tile.Elevation is >= 2 and < 11))
        {
            GenerationStatus = "Create hills before creating mountains. Mountains can only be placed in hill areas.";
            return;
        }

        if (!TryBuildMountainOptions(out var options))
        {
            return;
        }

        if (options.Count == 0)
        {
            GenerationStatus = "Mountain range count is 0, so no elevation changes were made.";
            return;
        }

        var sourceTiles = Tiles.ToList();
        var mapWidth = MapWidth;
        var mapHeight = MapHeight;
        var result = await Task.Run(() => MountainGenerator.Generate(sourceTiles, mapWidth, mapHeight, options));
        if (result.PlannedRangeCount == 0)
        {
            GenerationStatus = "Could not place mountain ranges. Create more hill area or lower the mountain range count.";
            return;
        }

        if (result.RaisedTileCount == 0)
        {
            GenerationStatus = "Mountain ranges were planned, but no hill tiles were raised. Try stronger or wider mountains.";
            return;
        }

        PushUndoSnapshot();
        Tiles = new ObservableCollection<MapTile>(result.Tiles);
        GenerationStatus = $"Created {result.PlannedRangeCount} mountain ranges with seed {options.Seed}, raising {result.RaisedTileCount} hill tiles. Peak elevation is {result.PeakElevation}.";
        OnPropertyChanged(nameof(HillsMenuSummary));
        CommitCurrentMapState();
    }

    private async Task ApplyShallowWaterBufferAsync()
    {
        if (Tiles.Count == 0 || MapWidth <= 0 || MapHeight <= 0)
        {
            GenerationStatus = "Generate a map before applying shallow water.";
            return;
        }

        if (!TryParseInt(ShallowWaterBufferText, "shallow water buffer", 0, Math.Max(MapWidth, MapHeight), out var buffer))
        {
            return;
        }

        var sourceTiles = Tiles.ToList();
        var mapWidth = MapWidth;
        var mapHeight = MapHeight;
        var updated = await Task.Run(() => ApplyShallowWaterBufferCore(sourceTiles, mapWidth, mapHeight, buffer));

        PushUndoSnapshot();
        Tiles = new ObservableCollection<MapTile>(updated);
        GenerationStatus = $"Applied shallow water within {buffer} tiles of land.";
        OnPropertyChanged(nameof(WaterMenuSummary));
        CommitCurrentMapState();
    }

    private static List<MapTile> ApplyShallowWaterBufferCore(IReadOnlyList<MapTile> sourceTiles, int mapWidth, int mapHeight, int buffer)
    {
        var tileGrid = new MapTile?[mapHeight * mapWidth];
        foreach (var tile in sourceTiles)
        {
            if ((uint)tile.Row >= (uint)mapHeight || (uint)tile.Column >= (uint)mapWidth)
            {
                continue;
            }

            tileGrid[GetIndex(tile.Row, tile.Column, mapWidth)] = tile;
        }

        var distances = Enumerable.Repeat(-1, mapHeight * mapWidth).ToArray();
        var queue = new Queue<int>();

        for (var row = 0; row < mapHeight; row++)
        {
            for (var column = 0; column < mapWidth; column++)
            {
                var index = GetIndex(row, column, mapWidth);
                if (tileGrid[index] is { Terrain: TileTerrain.Land })
                {
                    distances[index] = 0;
                    queue.Enqueue(index);
                }
            }
        }

        while (queue.Count > 0)
        {
            var index = queue.Dequeue();
            var distance = distances[index];
            if (distance >= buffer)
            {
                continue;
            }

            var row = index / mapWidth;
            var column = index % mapWidth;
            EnqueueWater(row - 1, column - 1, distance + 1);
            EnqueueWater(row - 1, column, distance + 1);
            EnqueueWater(row - 1, column + 1, distance + 1);
            EnqueueWater(row, column - 1, distance + 1);
            EnqueueWater(row, column + 1, distance + 1);
            EnqueueWater(row + 1, column - 1, distance + 1);
            EnqueueWater(row + 1, column, distance + 1);
            EnqueueWater(row + 1, column + 1, distance + 1);
        }

        var updated = new List<MapTile>(sourceTiles.Count);
        foreach (var tile in sourceTiles)
        {
            if (tile.Terrain == TileTerrain.Land)
            {
                updated.Add(tile);
                continue;
            }

            var distance = distances[GetIndex(tile.Row, tile.Column, mapWidth)];
            var terrain = distance > 0 && distance <= buffer
                ? TileTerrain.Water
                : TileTerrain.DeepWater;
            updated.Add(tile with { Terrain = terrain, Elevation = 0 });
        }

        return updated;

        void EnqueueWater(int row, int column, int distance)
        {
            if ((uint)row >= (uint)mapHeight || (uint)column >= (uint)mapWidth)
            {
                return;
            }

            var neighborIndex = GetIndex(row, column, mapWidth);
            if (distances[neighborIndex] >= 0 ||
                tileGrid[neighborIndex] is not { Terrain: TileTerrain.Water or TileTerrain.DeepWater })
            {
                return;
            }

            distances[neighborIndex] = distance;
            queue.Enqueue(neighborIndex);
        }
    }

    private int GetIndex(int row, int column)
    {
        return (row * MapWidth) + column;
    }

    private static int GetIndex(int row, int column, int mapWidth)
    {
        return (row * mapWidth) + column;
    }

    private void BeginPaint()
    {
        _paintSessionActive = Tiles.Count > 0 && MapWidth > 0 && MapHeight > 0;
        _paintSessionChanged = false;
        _paintedTilesThisSession.Clear();

        if (!_paintSessionActive)
        {
            GenerationStatus = "Generate or load a map before painting.";
        }
    }

    private void PaintTile(TileCoordinate coordinate)
    {
        if (!_paintSessionActive)
        {
            return;
        }

        if (PaintTool is MapPaintTool.Islands or MapPaintTool.Lakes && _paintSessionChanged)
        {
            return;
        }

        if (!_paintedTilesThisSession.Add(coordinate))
        {
            return;
        }

        if (PaintTool == MapPaintTool.Islands)
        {
            PaintIsland(coordinate);
            return;
        }

        if (PaintTool == MapPaintTool.Lakes)
        {
            PaintLake(coordinate);
            return;
        }

        if (!TryGetTileIndex(coordinate, out var index))
        {
            return;
        }

        var tile = Tiles[index];
        var updated = PaintTool switch
        {
            MapPaintTool.ShallowWater => tile with
            {
                Terrain = TileTerrain.Water,
                Elevation = 0,
                Resource = TileResource.None
            },
            MapPaintTool.River => tile with
            {
                Terrain = TileTerrain.Water,
                Elevation = Math.Max(1, tile.Elevation),
                Resource = TileResource.None
            },
            MapPaintTool.Hill => tile with
            {
                Terrain = TileTerrain.Land,
                Elevation = 2,
                Resource = tile.Terrain == TileTerrain.Land ? tile.Resource : TileResource.None
            },
            MapPaintTool.RemoveResource => tile with
            {
                Resource = TileResource.None
            },
            _ => tile
        };

        if (updated == tile)
        {
            return;
        }

        if (!_paintSessionChanged)
        {
            PushUndoSnapshot();
            _paintSessionChanged = true;
        }

        Tiles[index] = updated;
        GenerationStatus = $"Painting {PaintTool}.";
    }

    private void PaintIsland(TileCoordinate coordinate)
    {
        if (!TryBuildPaintIslandOptions(
                out var seed,
                out var minimumRadius,
                out var maximumRadius,
                out var irregularityPercent))
        {
            return;
        }

        var result = IslandSprinkler.PlantIsland(
            Tiles,
            MapWidth,
            MapHeight,
            coordinate.Row,
            coordinate.Column,
            minimumRadius,
            maximumRadius,
            irregularityPercent,
            seed);
        if (result.AddedLandTileCount == 0)
        {
            GenerationStatus = "No island land was added at that point.";
            return;
        }

        PushUndoSnapshot();
        _paintSessionChanged = true;
        ApplyPaintResultTiles(result.Tiles);
        GenerationStatus = $"Painted island at row {coordinate.Row}, column {coordinate.Column}, adding {result.AddedLandTileCount} land tiles.";
    }

    private void PaintLake(TileCoordinate coordinate)
    {
        if (!TryBuildPaintLakeOptions(
                out var seed,
                out var minimumRadius,
                out var maximumRadius,
                out var irregularityPercent))
        {
            return;
        }

        var result = LakeGenerator.PlantLake(
            Tiles,
            MapWidth,
            MapHeight,
            coordinate.Row,
            coordinate.Column,
            minimumRadius,
            maximumRadius,
            irregularityPercent,
            seed);
        if (result.AddedWaterTileCount == 0)
        {
            GenerationStatus = "No lake was added there. Lakes need inland land or hills and cannot touch sea water or mountains.";
            return;
        }

        PushUndoSnapshot();
        _paintSessionChanged = true;
        ApplyPaintResultTiles(result.Tiles);
        GenerationStatus = $"Painted lake at row {coordinate.Row}, column {coordinate.Column}, converting {result.AddedWaterTileCount} land tiles.";
    }

    private void ApplyPaintResultTiles(IReadOnlyList<MapTile> updatedTiles)
    {
        var count = Math.Min(Tiles.Count, updatedTiles.Count);
        for (var index = 0; index < count; index++)
        {
            if (Tiles[index] != updatedTiles[index])
            {
                Tiles[index] = updatedTiles[index];
            }
        }
    }

    private void CompletePaint()
    {
        if (!_paintSessionActive)
        {
            return;
        }

        _paintSessionActive = false;
        _paintedTilesThisSession.Clear();

        if (!_paintSessionChanged)
        {
            return;
        }

        CommitCurrentMapState();
        OnPropertyChanged(nameof(MapMenuSummary));
        OnPropertyChanged(nameof(WaterMenuSummary));
        OnPropertyChanged(nameof(LakesMenuSummary));
        OnPropertyChanged(nameof(RiversMenuSummary));
        OnPropertyChanged(nameof(HillsMenuSummary));
        GenerationStatus = $"Painted {PaintTool}.";
    }

    private bool TryGetTileIndex(TileCoordinate coordinate, out int index)
    {
        index = -1;
        if ((uint)coordinate.Row >= (uint)MapHeight || (uint)coordinate.Column >= (uint)MapWidth)
        {
            return false;
        }

        var expectedIndex = GetIndex(coordinate.Row, coordinate.Column);
        if ((uint)expectedIndex < (uint)Tiles.Count &&
            Tiles[expectedIndex].Row == coordinate.Row &&
            Tiles[expectedIndex].Column == coordinate.Column)
        {
            index = expectedIndex;
            return true;
        }

        for (var tileIndex = 0; tileIndex < Tiles.Count; tileIndex++)
        {
            if (Tiles[tileIndex].Row == coordinate.Row && Tiles[tileIndex].Column == coordinate.Column)
            {
                index = tileIndex;
                return true;
            }
        }

        return false;
    }

    private void PushUndoSnapshot()
    {
        _undoHistory.Add(_lastCommittedDocument ?? CreateMapDocument());
        if (_undoHistory.Count > MaximumUndoHistory)
        {
            _undoHistory.RemoveAt(0);
        }

        NotifyUndoStateChanged();
    }

    private void Undo()
    {
        if (_undoHistory.Count == 0)
        {
            GenerationStatus = "Nothing to undo.";
            return;
        }

        var index = _undoHistory.Count - 1;
        var document = _undoHistory[index];
        _undoHistory.RemoveAt(index);
        ApplyMapData(document.Map);
        CommitCurrentMapState();
        GenerationStatus = _undoHistory.Count == 0
            ? "Undid last map change."
            : $"Undid last map change. {_undoHistory.Count} undo steps remain.";
        NotifyUndoStateChanged();
    }

    private void CommitCurrentMapState()
    {
        _lastCommittedDocument = CreateMapDocument();
    }

    private void NotifyUndoStateChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        _undoCommand.RaiseCanExecuteChanged();
    }

    private AsyncRelayCommand CreateExecutionCommand(Func<Task> execute)
    {
        var command = new AsyncRelayCommand(execute, () => !IsExecutionRunning);
        _executionCommands.Add(command);
        return command;
    }

    private async Task RunExecutionAsync(Action<bool> setIsRunning, Func<Task> execute)
    {
        IsExecutionRunning = true;
        setIsRunning(true);

        try
        {
            await Task.Yield();
            await execute();
        }
        finally
        {
            setIsRunning(false);
            IsExecutionRunning = false;
        }
    }

    private void NotifyExecutionCommandStatesChanged()
    {
        foreach (var command in _executionCommands)
        {
            command.RaiseCanExecuteChanged();
        }
    }

    private MapDocument CreateMapDocument()
    {
        return new MapDocument
        {
            SavedAtUtc = DateTimeOffset.UtcNow,
            Settings = new MapDocumentSettings
            {
                MapWidthText = MapWidthText,
                MapHeightText = MapHeightText,
                NoiseSeedText = NoiseSeedText,
                NoiseBlendMode = NoiseBlendMode,
                NoiseFrequencyText = NoiseFrequencyText,
                NoiseOctavesText = NoiseOctavesText,
                NoisePersistenceText = NoisePersistenceText,
                NoiseLacunarityText = NoiseLacunarityText,
                NoiseExponentText = NoiseExponentText,
                NoiseWaterLevelText = NoiseWaterLevelText,
                NoiseMinimumWaterConnectionWidthText = NoiseMinimumWaterConnectionWidthText,
                NoiseHeightBiasText = NoiseHeightBiasText,
                NoiseIslandFalloffEnabled = NoiseIslandFalloffEnabled,
                NoiseIslandStrengthText = NoiseIslandStrengthText,
                NoiseFalloffExponentText = NoiseFalloffExponentText,
                IslandCountText = IslandCountText,
                IslandSeedText = IslandSeedText,
                IslandMinimumRadiusText = IslandMinimumRadiusText,
                IslandMaximumRadiusText = IslandMaximumRadiusText,
                IslandIrregularityText = IslandIrregularityText,
                LakeCountText = LakeCountText,
                LakeSeedText = LakeSeedText,
                LakeMinimumRadiusText = LakeMinimumRadiusText,
                LakeMaximumRadiusText = LakeMaximumRadiusText,
                LakeIrregularityText = LakeIrregularityText,
                RiverCountText = RiverCountText,
                RiverSeedText = RiverSeedText,
                RiverWanderText = RiverWanderText,
                RiverWideningText = RiverWideningText,
                RiverSeaTerminationText = RiverSeaTerminationText,
                ForestCountText = ForestCountText,
                ForestSeedText = ForestSeedText,
                ForestDensityText = ForestDensityText,
                ForestWidthText = ForestWidthText,
                ForestPassFrequencyText = ForestPassFrequencyText,
                IronSeedText = IronSeedText,
                IronCountText = IronCountText,
                IronDensityText = IronDensityText,
                IronWidthText = IronWidthText,
                StoneSeedText = StoneSeedText,
                StoneCountText = StoneCountText,
                StoneDensityText = StoneDensityText,
                StoneWidthText = StoneWidthText,
                CoalSeedText = CoalSeedText,
                CoalCountText = CoalCountText,
                CoalDensityText = CoalDensityText,
                CoalWidthText = CoalWidthText,
                RareMetalsSeedText = RareMetalsSeedText,
                RareMetalsCountText = RareMetalsCountText,
                RareMetalsDensityText = RareMetalsDensityText,
                RareMetalsWidthText = RareMetalsWidthText,
                ShallowWaterBufferText = ShallowWaterBufferText,
                HillRangeCountText = HillRangeCountText,
                HillSeedText = HillSeedText,
                HillStrengthText = HillStrengthText,
                HillWidthText = HillWidthText,
                HillPassFrequencyText = HillPassFrequencyText,
                MountainPeakCountText = MountainPeakCountText,
                MountainSeedText = MountainSeedText,
                MountainStrengthText = MountainStrengthText,
                MountainRadiusText = MountainRadiusText,
                MapDisplayMode = MapDisplayMode
            },
            Map = new MapDocumentMap
            {
                Width = MapWidth,
                Height = MapHeight,
                Tiles = Tiles
                    .Select(tile => new MapDocumentTile
                    {
                        Row = tile.Row,
                        Column = tile.Column,
                        Terrain = tile.Terrain,
                        Elevation = tile.Elevation,
                        Resource = tile.Resource
                    })
                    .ToList()
            }
        };
    }

    private void ApplyMapDocument(MapDocument document)
    {
        ApplyDocumentSettings(document.Settings);
        ApplyMapData(document.Map);
    }

    private void ApplyMapData(MapDocumentMap map)
    {
        var width = map.Width;
        var height = map.Height;
        if (width <= 0 || height <= 0)
        {
            width = map.Tiles.Count == 0 ? 0 : map.Tiles.Max(tile => tile.Column) + 1;
            height = map.Tiles.Count == 0 ? 0 : map.Tiles.Max(tile => tile.Row) + 1;
        }

        MapWidth = width;
        MapHeight = height;
        Tiles = new ObservableCollection<MapTile>(
            map.Tiles
                .Where(tile => tile.Row >= 0 && tile.Column >= 0)
                .Select(tile => new MapTile(tile.Row, tile.Column, tile.Terrain, tile.Elevation, tile.Resource)));
        Adornments = [];
        Highlights = [];
        PanUpperRow = 0;
        PanLeftColumn = 0;

        OnPropertyChanged(nameof(MapWidth));
        OnPropertyChanged(nameof(MapHeight));
        OnPropertyChanged(nameof(MapMenuSummary));
        OnPropertyChanged(nameof(WaterMenuSummary));
        OnPropertyChanged(nameof(LakesMenuSummary));
        OnPropertyChanged(nameof(RiversMenuSummary));
        OnPropertyChanged(nameof(ResourcesMenuSummary));
        OnPropertyChanged(nameof(HillsMenuSummary));
    }

    private void ApplyDocumentSettings(MapDocumentSettings settings)
    {
        MapWidthText = settings.MapWidthText;
        MapHeightText = settings.MapHeightText;
        NoiseSeedText = settings.NoiseSeedText;
        NoiseBlendMode = settings.NoiseBlendMode;
        NoiseFrequencyText = settings.NoiseFrequencyText;
        NoiseOctavesText = settings.NoiseOctavesText;
        NoisePersistenceText = settings.NoisePersistenceText;
        NoiseLacunarityText = settings.NoiseLacunarityText;
        NoiseExponentText = settings.NoiseExponentText;
        NoiseWaterLevelText = settings.NoiseWaterLevelText;
        NoiseMinimumWaterConnectionWidthText = settings.NoiseMinimumWaterConnectionWidthText;
        NoiseHeightBiasText = settings.NoiseHeightBiasText;
        NoiseIslandFalloffEnabled = settings.NoiseIslandFalloffEnabled;
        NoiseIslandStrengthText = settings.NoiseIslandStrengthText;
        NoiseFalloffExponentText = settings.NoiseFalloffExponentText;
        IslandCountText = settings.IslandCountText;
        IslandSeedText = settings.IslandSeedText;
        IslandMinimumRadiusText = settings.IslandMinimumRadiusText;
        IslandMaximumRadiusText = settings.IslandMaximumRadiusText;
        IslandIrregularityText = settings.IslandIrregularityText;
        LakeCountText = settings.LakeCountText;
        LakeSeedText = settings.LakeSeedText;
        LakeMinimumRadiusText = settings.LakeMinimumRadiusText;
        LakeMaximumRadiusText = settings.LakeMaximumRadiusText;
        LakeIrregularityText = settings.LakeIrregularityText;
        RiverCountText = settings.RiverCountText;
        RiverSeedText = settings.RiverSeedText;
        RiverWanderText = settings.RiverWanderText;
        RiverWideningText = settings.RiverWideningText;
        RiverSeaTerminationText = settings.RiverSeaTerminationText;
        ForestCountText = settings.ForestCountText;
        ForestSeedText = settings.ForestSeedText;
        ForestDensityText = settings.ForestDensityText;
        ForestWidthText = settings.ForestWidthText;
        ForestPassFrequencyText = settings.ForestPassFrequencyText;
        IronSeedText = settings.IronSeedText;
        IronCountText = settings.IronCountText;
        IronDensityText = settings.IronDensityText;
        IronWidthText = settings.IronWidthText;
        StoneSeedText = settings.StoneSeedText;
        StoneCountText = settings.StoneCountText;
        StoneDensityText = settings.StoneDensityText;
        StoneWidthText = settings.StoneWidthText;
        CoalSeedText = settings.CoalSeedText;
        CoalCountText = settings.CoalCountText;
        CoalDensityText = settings.CoalDensityText;
        CoalWidthText = settings.CoalWidthText;
        RareMetalsSeedText = settings.RareMetalsSeedText;
        RareMetalsCountText = settings.RareMetalsCountText;
        RareMetalsDensityText = settings.RareMetalsDensityText;
        RareMetalsWidthText = settings.RareMetalsWidthText;
        ShallowWaterBufferText = settings.ShallowWaterBufferText;
        HillRangeCountText = settings.HillRangeCountText;
        HillSeedText = settings.HillSeedText;
        HillStrengthText = settings.HillStrengthText;
        HillWidthText = settings.HillWidthText;
        HillPassFrequencyText = settings.HillPassFrequencyText;
        MountainPeakCountText = settings.MountainPeakCountText;
        MountainSeedText = settings.MountainSeedText;
        MountainStrengthText = settings.MountainStrengthText;
        MountainRadiusText = settings.MountainRadiusText;
        MapDisplayMode = settings.MapDisplayMode;
    }

    private bool TryReadMapSize(out int width, out int height)
    {
        width = 0;
        height = 0;

        if (!TryParseInt(MapWidthText, "width", 1, 20_000, out width))
        {
            return false;
        }

        if (!TryParseInt(MapHeightText, "height", 1, 20_000, out height))
        {
            return false;
        }

        return true;
    }

    private bool TryBuildNoiseOptions(out NoiseGenerationOptions options)
    {
        options = new NoiseGenerationOptions();

        if (!TryParseInt(NoiseSeedText, "seed", 0, int.MaxValue, out var seed) ||
            !TryParseFloat(NoiseFrequencyText, "frequency", 0.05f, 64f, out var frequency) ||
            !TryParseInt(NoiseOctavesText, "octaves", 1, 12, out var octaves) ||
            !TryParseFloat(NoisePersistenceText, "persistence", 0.05f, 1.25f, out var persistence) ||
            !TryParseFloat(NoiseLacunarityText, "lacunarity", 1.1f, 5f, out var lacunarity) ||
            !TryParseFloat(NoiseExponentText, "exponent", 0.05f, 4f, out var exponent) ||
            !TryParseFloat(NoiseWaterLevelText, "water level", 0f, 98f, out var waterLevel) ||
            !TryParseInt(NoiseMinimumWaterConnectionWidthText, "cut width", 0, 32, out var minimumWaterConnectionWidth) ||
            !TryParseFloat(NoiseHeightBiasText, "height bias", -0.75f, 0.75f, out var heightBias) ||
            !TryParseFloat(NoiseIslandStrengthText, "island strength", 0f, 1f, out var islandStrength) ||
            !TryParseFloat(NoiseFalloffExponentText, "falloff exponent", 0.1f, 6f, out var falloffExponent))
        {
            return false;
        }

        options = new NoiseGenerationOptions
        {
            Seed = seed,
            BlendMode = NoiseBlendMode,
            Frequency = frequency,
            Octaves = octaves,
            Persistence = persistence,
            Lacunarity = lacunarity,
            Exponent = exponent,
            WaterLevelPercent = waterLevel,
            MinimumWaterConnectionWidth = minimumWaterConnectionWidth,
            HeightBias = heightBias,
            UseIslandFalloff = NoiseIslandFalloffEnabled,
            IslandStrength = islandStrength,
            FalloffExponent = falloffExponent
        };

        return true;
    }

    private bool TryBuildHillOptions(out HillGenerationOptions options)
    {
        options = new HillGenerationOptions();

        if (!TryParseInt(HillRangeCountText, "hill range count", 0, 48, out var count) ||
            !TryParseInt(HillSeedText, "hill seed", 0, int.MaxValue, out var seed) ||
            !TryParseInt(HillStrengthText, "hill strength", 1, 48, out var strength) ||
            !TryParseInt(HillWidthText, "hill width", 4, 120, out var width) ||
            !TryParseInt(HillPassFrequencyText, "hill pass frequency", 0, 100, out var passFrequencyPercent))
        {
            return false;
        }

        options = new HillGenerationOptions(
            count,
            seed,
            strength,
            width,
            passFrequencyPercent);
        return true;
    }

    private bool TryBuildMountainOptions(out MountainGenerationOptions options)
    {
        options = new MountainGenerationOptions();

        if (!TryParseInt(MountainPeakCountText, "mountain range count", 0, 64, out var count) ||
            !TryParseInt(MountainSeedText, "mountain seed", 0, int.MaxValue, out var seed) ||
            !TryParseInt(MountainStrengthText, "mountain strength", 1, 48, out var strength) ||
            !TryParseInt(MountainRadiusText, "mountain width", 2, 80, out var radius))
        {
            return false;
        }

        options = new MountainGenerationOptions(
            count,
            seed,
            strength,
            radius);
        return true;
    }

    private bool TryBuildLakeOptions(out LakeGenerationOptions options)
    {
        options = new LakeGenerationOptions(0, 0, 0, 0, 0);

        if (!TryParseInt(LakeCountText, "lake count", 0, 200, out var count) ||
            !TryParseInt(LakeSeedText, "lake seed", 0, int.MaxValue, out var seed) ||
            !TryParseInt(LakeMinimumRadiusText, "minimum lake radius", 1, 80, out var minimumRadius) ||
            !TryParseInt(LakeMaximumRadiusText, "maximum lake radius", 1, 120, out var maximumRadius) ||
            !TryParseInt(LakeIrregularityText, "lake irregularity", 0, 100, out var irregularityPercent))
        {
            return false;
        }

        if (maximumRadius < minimumRadius)
        {
            GenerationStatus = "maximum lake radius must be greater than or equal to minimum lake radius.";
            return false;
        }

        options = new LakeGenerationOptions(
            count,
            seed,
            minimumRadius,
            maximumRadius,
            irregularityPercent);
        return true;
    }

    private bool TryBuildRiverOptions(out RiverGenerationOptions options)
    {
        options = new RiverGenerationOptions(0, 0, 0, 0, 0);

        if (!TryParseInt(RiverCountText, "river count", 0, 120, out var count) ||
            !TryParseInt(RiverSeedText, "river seed", 0, int.MaxValue, out var seed) ||
            !TryParseInt(RiverWanderText, "river wander", 0, 100, out var wanderPercent) ||
            !TryParseInt(RiverWideningText, "river widening", 0, 100, out var wideningPercent) ||
            !TryParseInt(RiverSeaTerminationText, "river sea termination", 0, 100, out var seaTerminationPercent))
        {
            return false;
        }

        options = new RiverGenerationOptions(
            count,
            seed,
            wanderPercent,
            wideningPercent,
            seaTerminationPercent);
        return true;
    }

    private bool TryBuildForestOptions(out ForestGenerationOptions options)
    {
        options = new ForestGenerationOptions(0, 0, 0, 0, 0);

        if (!TryParseInt(ForestCountText, "forest count", 0, 240, out var count) ||
            !TryParseInt(ForestSeedText, "forest seed", 0, int.MaxValue, out var seed) ||
            !TryParseInt(ForestDensityText, "forest density", 1, 100, out var densityPercent) ||
            !TryParseInt(ForestWidthText, "forest width", 2, 100, out var width) ||
            !TryParseInt(ForestPassFrequencyText, "forest path frequency", 0, 100, out var passFrequencyPercent))
        {
            return false;
        }

        options = new ForestGenerationOptions(
            count,
            seed,
            densityPercent,
            width,
            passFrequencyPercent);
        return true;
    }

    private bool TryBuildResourceDepositOptions(TileResource resource, out ResourceDepositGenerationOptions options)
    {
        options = new ResourceDepositGenerationOptions(resource, 0, 0, 0, 0);

        var seedText = GetResourceSeedText(resource);
        var countText = GetResourceCountText(resource);
        var densityText = GetResourceDensityText(resource);
        var widthText = GetResourceWidthText(resource);
        var label = GetResourceDisplayName(resource).ToLowerInvariant();
        if (!TryParseInt(seedText, $"{label} seed", 0, int.MaxValue, out var seed) ||
            !TryParseInt(countText, $"{label} count", 0, 240, out var count) ||
            !TryParseInt(densityText, $"{label} density", 1, 100, out var densityPercent) ||
            !TryParseInt(widthText, $"{label} width", 2, 100, out var width))
        {
            return false;
        }

        options = new ResourceDepositGenerationOptions(
            resource,
            seed,
            count,
            densityPercent,
            width);
        return true;
    }

    private string GetResourceSeedText(TileResource resource)
    {
        return resource switch
        {
            TileResource.Iron => IronSeedText,
            TileResource.Stone => StoneSeedText,
            TileResource.Coal => CoalSeedText,
            TileResource.RareMetals => RareMetalsSeedText,
            _ => "0"
        };
    }

    private string GetResourceCountText(TileResource resource)
    {
        return resource switch
        {
            TileResource.Iron => IronCountText,
            TileResource.Stone => StoneCountText,
            TileResource.Coal => CoalCountText,
            TileResource.RareMetals => RareMetalsCountText,
            _ => "0"
        };
    }

    private string GetResourceDensityText(TileResource resource)
    {
        return resource switch
        {
            TileResource.Iron => IronDensityText,
            TileResource.Stone => StoneDensityText,
            TileResource.Coal => CoalDensityText,
            TileResource.RareMetals => RareMetalsDensityText,
            _ => "0"
        };
    }

    private string GetResourceWidthText(TileResource resource)
    {
        return resource switch
        {
            TileResource.Iron => IronWidthText,
            TileResource.Stone => StoneWidthText,
            TileResource.Coal => CoalWidthText,
            TileResource.RareMetals => RareMetalsWidthText,
            _ => "0"
        };
    }

    private static int GetPlacedResourceCount(ResourceDepositGenerationResult result, TileResource resource)
    {
        return resource switch
        {
            TileResource.Iron => result.IronTileCount,
            TileResource.Stone => result.StoneTileCount,
            TileResource.Coal => result.CoalTileCount,
            TileResource.RareMetals => result.RareMetalsTileCount,
            _ => 0
        };
    }

    private static string GetResourceDisplayName(TileResource resource)
    {
        return resource switch
        {
            TileResource.Iron => "Iron",
            TileResource.Stone => "Stone",
            TileResource.Coal => "Coal",
            TileResource.RareMetals => "Rare metals",
            _ => "Resource"
        };
    }

    private bool TryBuildIslandOptions(out IslandSprinkleOptions options)
    {
        options = new IslandSprinkleOptions();

        if (!TryParseInt(IslandCountText, "island count", 0, 300, out var count) ||
            !TryParseInt(IslandSeedText, "island seed", 0, int.MaxValue, out var seed) ||
            !TryParseInt(IslandMinimumRadiusText, "minimum island radius", 1, 80, out var minimumRadius) ||
            !TryParseInt(IslandMaximumRadiusText, "maximum island radius", 1, 120, out var maximumRadius) ||
            !TryParseInt(IslandIrregularityText, "island irregularity", 0, 100, out var irregularityPercent))
        {
            return false;
        }

        if (maximumRadius < minimumRadius)
        {
            GenerationStatus = "maximum island radius must be greater than or equal to minimum island radius.";
            return false;
        }

        options = new IslandSprinkleOptions(
            count,
            seed,
            minimumRadius,
            maximumRadius,
            irregularityPercent);
        return true;
    }

    private bool TryBuildPaintIslandOptions(
        out int seed,
        out int minimumRadius,
        out int maximumRadius,
        out int irregularityPercent)
    {
        seed = 0;
        minimumRadius = 0;
        maximumRadius = 0;
        irregularityPercent = 0;

        if (!TryParseInt(IslandSeedText, "island seed", 0, int.MaxValue, out seed) ||
            !TryParseInt(IslandMinimumRadiusText, "minimum island radius", 1, 80, out minimumRadius) ||
            !TryParseInt(IslandMaximumRadiusText, "maximum island radius", 1, 120, out maximumRadius) ||
            !TryParseInt(IslandIrregularityText, "island irregularity", 0, 100, out irregularityPercent))
        {
            return false;
        }

        if (maximumRadius < minimumRadius)
        {
            GenerationStatus = "maximum island radius must be greater than or equal to minimum island radius.";
            return false;
        }

        return true;
    }

    private bool TryBuildPaintLakeOptions(
        out int seed,
        out int minimumRadius,
        out int maximumRadius,
        out int irregularityPercent)
    {
        seed = 0;
        minimumRadius = 0;
        maximumRadius = 0;
        irregularityPercent = 0;

        if (!TryParseInt(LakeSeedText, "lake seed", 0, int.MaxValue, out seed) ||
            !TryParseInt(LakeMinimumRadiusText, "minimum lake radius", 1, 80, out minimumRadius) ||
            !TryParseInt(LakeMaximumRadiusText, "maximum lake radius", 1, 120, out maximumRadius) ||
            !TryParseInt(LakeIrregularityText, "lake irregularity", 0, 100, out irregularityPercent))
        {
            return false;
        }

        if (maximumRadius < minimumRadius)
        {
            GenerationStatus = "maximum lake radius must be greater than or equal to minimum lake radius.";
            return false;
        }

        return true;
    }

    private bool TryParseInt(string text, string label, int minimum, int maximum, out int value)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ||
            value < minimum ||
            value > maximum)
        {
            GenerationStatus = $"{label} must be a whole number between {minimum} and {maximum}.";
            return false;
        }

        return true;
    }

    private bool TryParseFloat(string text, string label, float minimum, float maximum, out float value)
    {
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
            value < minimum ||
            value > maximum)
        {
            GenerationStatus = $"{label} must be a number between {minimum.ToString(CultureInfo.InvariantCulture)} and {maximum.ToString(CultureInfo.InvariantCulture)}.";
            return false;
        }

        return true;
    }

    private void SetNoiseTextProperty(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, value, propertyName))
        {
            OnPropertyChanged(nameof(MapMenuSummary));
        }
    }

    private void SetIslandTextProperty(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, value, propertyName))
        {
            OnPropertyChanged(nameof(MapMenuSummary));
        }
    }

    private void SetLakeTextProperty(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, value, propertyName))
        {
            OnPropertyChanged(nameof(LakesMenuSummary));
        }
    }

    private void SetRiverTextProperty(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, value, propertyName))
        {
            OnPropertyChanged(nameof(RiversMenuSummary));
        }
    }

    private void SetResourceTextProperty(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, value, propertyName))
        {
            OnPropertyChanged(nameof(ResourcesMenuSummary));
        }
    }

    private void SetHillTextProperty(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, value, propertyName))
        {
            OnPropertyChanged(nameof(HillsMenuSummary));
        }
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
