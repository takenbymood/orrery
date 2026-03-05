using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Represents a spherical planet with an icosahedral hexagonal grid tessellation.
/// This class handles the data and logic layer - grid generation, queries, and heightmaps.
/// Rendering is delegated to view classes like SphericalPlanetView.
/// </summary>
public partial class Planet : Node3D
{
    #region Export Properties
    [Export] public float planetRadius = 6371.0f; // Planet radius in km
    [Export] public int resolution = 4; // n value for hex resolution
    [Export] public bool useSnyder = false; // Toggle between projections
    [Export] public bool autoGenerate = true; // Generate on _Ready
    [Export] public bool showStatistics = true; // Print statistics to console
    [Export] public int majorPlateCount = 7; // Number of major tectonic plates
    [Export] public int minorPlateCount = 5; // Number of minor tectonic plates
    [Export] public int minorPlateMaxSize = 20; // Max hexes per minor plate
    [Export] public bool generatePlates = true; // Auto-generate tectonic plates
    [Export(PropertyHint.Range, "0.0,1.0,")] public float continentalPlateWeight = 0.35f; // Probability of major plate being continental (vs oceanic)
    #endregion

    #region Private Fields
    private HexGrid grid;
    private List<Hexagon> allCells = new List<Hexagon>();
    private Dictionary<string, Hexagon> cellsById = new Dictionary<string, Hexagon>();
    private int invalidCenterCount = 0;
    private int hexagonCount = 0;
    private int pentagonCount = 0;
    private Dictionary<string, int> hexToPlateId = new Dictionary<string, int>();
    private List<TectonicPlate> plates = new List<TectonicPlate>();
    private Dictionary<string, float> hexTectonicHeights = new Dictionary<string, float>();
    private Dictionary<int, List<Hexagon>> cellsByFace = new Dictionary<int, List<Hexagon>>();
    private Hexagon lastNearestCellCache = null;
    private Vector3 lastNearestDirectionCache = Vector3.Zero;
    #endregion

    #region Public Properties
    public HexGrid Grid => grid;
    public IReadOnlyList<Hexagon> AllCells => allCells;
    public int HexagonCount => hexagonCount;
    public int PentagonCount => pentagonCount;
    public float SeaLevel { get; private set; } = 0.5f;
    public IReadOnlyList<TectonicPlate> Plates => plates;
    public bool HasTectonicHeights => hexTectonicHeights.Count > 0;
    #endregion

    #region Godot Lifecycle
    public override void _Ready()
    {
        if (autoGenerate)
        {
            Generate();
        }
    }
    #endregion

    #region Public Methods
    /// <summary>
    /// Generates or regenerates the planet's hex grid
    /// </summary>
    public void Generate()
    {
        Clear();
        
        if (showStatistics)
            GD.Print("=== Generating Planet Hex Sphere ===");

        // Create projection and grid
        Projection projection = useSnyder ? new SnyderProjection() : (Projection)new GnomonicProjection();
        grid = new HexGrid(projection, radius: planetRadius);

        if (showStatistics)
        {
            GD.Print($"Planet radius: {planetRadius} km");
            GD.Print($"Using {(useSnyder ? "Snyder" : "Gnomonic")} projection");
            GD.Print($"Resolution n={resolution}");
            GD.Print($"Hex radius: {grid.ComputeRadiusForN(resolution):F2} km");
            GD.Print($"Hex height: {grid.ComputeHeightForN(resolution):F2} km");
        }

        // Generate all hexagons for all faces
        HashSet<string> processedHexes = new HashSet<string>();
        hexagonCount = 0;
        pentagonCount = 0;

        for (int face = 0; face < 20; face++)
        {
            for (int a = 0; a <= resolution + 1; a++)
            {
                for (int b = 0; b <= resolution + 1; b++)
                {
                    int c = 2 * (resolution + 1) - a - b;
                    
                    if (c >= 0 && c <= resolution + 1 && a + b + c == 2 * (resolution + 1))
                    {
                        Hexagon hex = new Hexagon(grid, face, (a, b, c), res: resolution + 1, solve_conflicts: true);
                        string hexId = hex.ToStrId();

                        if (processedHexes.Contains(hexId))
                            continue;

                        processedHexes.Add(hexId);
                        allCells.Add(hex);
                        cellsById[hexId] = hex;

                        // Add to face-based spatial partition
                        if (!cellsByFace.ContainsKey(face))
                            cellsByFace[face] = new List<Hexagon>();
                        cellsByFace[face].Add(hex);

                        bool isPentagon = IsPentagon(hex);

                        if (isPentagon)
                            pentagonCount++;
                        else
                            hexagonCount++;
                    }
                }
            }
        }

        if (showStatistics)
        {
            GD.Print($"Generated {allCells.Count} cells ({hexagonCount} hexagons, {pentagonCount} pentagons)");
            if (invalidCenterCount > 0)
                GD.PrintErr($"Skipped {invalidCenterCount} cells due to non-finite projection results.");

            PrintAreaStatistics();
        }

        // Generate tectonic plates if enabled
        if (generatePlates)
        {
            GenerateTectonicPlates();
            GenerateTectonicHeights();
        }

        // Calculate sea level from heightmap if available
        CalculateSeaLevel();
    }

    /// <summary>
    /// Clears all generated hex data
    /// </summary>
    public void Clear()
    {
        allCells.Clear();
        cellsById.Clear();
        hexToPlateId.Clear();
        plates.Clear();
        hexTectonicHeights.Clear();
        cellsByFace.Clear();
        lastNearestCellCache = null;
        invalidCenterCount = 0;
        hexagonCount = 0;
        pentagonCount = 0;
    }

    /// <summary>
    /// Gets a hex cell by its string ID
    /// </summary>
    public Hexagon GetCellById(string hexId)
    {
        return cellsById.TryGetValue(hexId, out var hex) ? hex : null;
    }

    /// <summary>
    /// Gets hex cell(s) at a given lat/lon position
    /// </summary>
    public Hexagon[] GetCellsAtLatLon(double lat, double lon)
    {
        if (grid == null)
            return new Hexagon[0];

        Hexagon nearest = GetNearestCellAtLatLon(lat, lon);
        return nearest != null ? new Hexagon[] { nearest } : new Hexagon[0];
    }

    /// <summary>
    /// Gets hex cell(s) at a given cartesian position
    /// </summary>
    public Hexagon[] GetCellsAtPosition(Vector3 position)
    {
        if (grid == null)
            return new Hexagon[0];

        Hexagon nearest = GetNearestCellAtLocalDirection(position.Normalized());
        return nearest != null ? new Hexagon[] { nearest } : new Hexagon[0];
    }

    public Hexagon GetNearestCellAtLatLon(double lat, double lon)
    {
        if (grid == null || allCells.Count == 0)
            return null;

        Vector3 dir = SphericalGeometry.LatLonToVector3(lat, lon).Normalized();
        return GetNearestCellAtLocalDirection(dir);
    }

    /// <summary>
    /// Gets the unique neighbors of a hex cell
    /// </summary>
    public Hexagon[] GetNeighbors(Hexagon hex)
    {
        return grid?.GetUniqueNeighbors(hex) ?? new Hexagon[0];
    }

    /// <summary>
    /// Converts a hex cell to lat/lon coordinates
    /// </summary>
    public (double lat, double lon) CellToLatLon(Hexagon hex)
    {
        return grid.HexToLatLon(hex);
    }

    /// <summary>
    /// Converts a hex cell to cartesian coordinates (on unit sphere)
    /// </summary>
    public Vector3 CellToCartesian(Hexagon hex)
    {
        return grid.HexToCartesian(hex);
    }

    /// <summary>
    /// Converts a hex cell to cartesian position (on unit sphere, unscaled)
    /// </summary>
    public Vector3 CellToNormalizedPosition(Hexagon hex)
    {
        return grid.projection.InvProject(hex.P, hex.face).Normalized();
    }

    public Hexagon GetNearestCellAtLocalDirection(Vector3 localDirection)
    {
        if (allCells.Count == 0)
            return null;

        Vector3 dir = localDirection.Normalized();

        // Check cache first - if we're querying the same/very similar direction
        if (lastNearestCellCache != null && lastNearestDirectionCache.DistanceSquaredTo(dir) < 0.0001f)
        {
            return lastNearestCellCache;
        }

        // Just do a full search - it's simpler and more reliable
        // The cache will handle repeated queries efficiently
        float bestDot = float.MinValue;
        Hexagon bestCell = null;

        foreach (Hexagon cell in allCells)
        {
            Vector3 centerDir = CellToCartesian(cell).Normalized();
            float dot = centerDir.Dot(dir);
            if (dot > bestDot)
            {
                bestDot = dot;
                bestCell = cell;
            }
        }

        // Cache result
        lastNearestCellCache = bestCell;
        lastNearestDirectionCache = dir;

        return bestCell;
    }

    public Hexagon GetNearestCellAtWorldPoint(Vector3 worldPoint)
    {
        Vector3 localDir = ToLocal(worldPoint).Normalized();
        return GetNearestCellAtLocalDirection(localDir);
    }

    /// <summary>
    /// Gets the first SphericalHeightmapGenerator child node, if any.
    /// </summary>
    public SphericalHeightmapGenerator GetHeightmapGenerator()
    {
        foreach (Node child in GetChildren())
        {
            if (child is SphericalHeightmapGenerator generator)
                return generator;
        }
        return null;
    }

    /// <summary>
    /// Gets all SphericalHeightmapGenerator children.
    /// </summary>
    public SphericalHeightmapGenerator[] GetHeightmapGenerators()
    {
        var generators = new System.Collections.Generic.List<SphericalHeightmapGenerator>();
        foreach (Node child in GetChildren())
        {
            if (child is SphericalHeightmapGenerator generator)
                generators.Add(generator);
        }
        return generators.ToArray();
    }

    /// <summary>
    /// Gets the height at a specific cell using the first heightmap generator.
    /// Returns 0 if no generator is found.
    /// </summary>
    public float GetHeightAtCell(Hexagon hex)
    {
        var generator = GetHeightmapGenerator();
        return generator?.GetHeightAtCell(hex) ?? 0f;
    }

    /// <summary>
    /// Gets the height at a lat/lon coordinate using the first heightmap generator.
    /// Returns 0 if no generator is found.
    /// </summary>
    public float GetHeightAtLatLon(double latitude, double longitude)
    {
        var generator = GetHeightmapGenerator();
        return generator?.GetRawHeightAtLatLon(latitude, longitude) ?? 0f;
    }

    /// <summary>
    /// Gets the height at a world-space position using the first heightmap generator.
    /// Returns 0 if no generator is found.
    /// </summary>
    public float GetHeightAtPosition(Vector3 worldPosition)
    {
        var generator = GetHeightmapGenerator();
        return generator?.GetRawHeightAtPosition(worldPosition) ?? 0f;
    }

    /// <summary>
    /// Gets the plate ID for a given hex ID. Returns -1 if no plate found.
    /// </summary>
    public int GetPlateIdForHex(string hexId)
    {
        return hexToPlateId.TryGetValue(hexId, out int plateId) ? plateId : -1;
    }

    /// <summary>
    /// Gets the plate for a given hex ID. Returns null if no plate found.
    /// </summary>
    public TectonicPlate GetPlateForHex(string hexId)
    {
        int plateId = GetPlateIdForHex(hexId);
        if (plateId >= 0 && plateId < plates.Count)
            return plates[plateId];
        return null;
    }

    /// <summary>
    /// Gets the tectonic height for a given hex ID. Returns 0.5 if no height data available.
    /// </summary>
    public float GetTectonicHeightAtHex(string hexId)
    {
        return hexTectonicHeights.TryGetValue(hexId, out float height) ? height : 0.5f;
    }

    /// <summary>
    /// Gets the tectonic height for a given hex. Returns 0.5 if no height data available.
    /// </summary>
    public float GetTectonicHeightAtHex(Hexagon hex)
    {
        return GetTectonicHeightAtHex(hex.ToStrId());
    }
    #endregion

    #region Private Methods - Tectonic Plates
    /// <summary>
    /// Generates tectonic plates using round-robin flood-fill growth.
    /// Each plate takes turns growing one step until the planet is covered.
    /// </summary>
    private void GenerateTectonicPlates()
    {
        hexToPlateId.Clear();
        plates.Clear();

        if (allCells.Count == 0)
            return;

        var random = new Random();
        var unassignedHexIds = new HashSet<string>(cellsById.Keys);
        var plateFrontiers = new List<Queue<string>>(); // Frontier for each plate

        // Create major plates with seed hexes
        for (int i = 0; i < majorPlateCount && unassignedHexIds.Count > 0; i++)
        {
            int randomIdx = random.Next(unassignedHexIds.Count);
            string seedHexId = unassignedHexIds.ElementAt(randomIdx);
            
            var plate = new TectonicPlate 
            { 
                plateId = i, 
                isMajor = true,
                type = random.NextDouble() < continentalPlateWeight ? PlateType.Continental : PlateType.Oceanic,
                driftLatitude = (random.NextDouble() * 180.0) - 90.0,  // -90 to 90
                driftLongitude = (random.NextDouble() * 360.0) - 180.0, // -180 to 180
                driftSpeed = (float)(random.NextDouble() * 2.0 + 0.5) // 0.5 to 2.5
            };
            plates.Add(plate);
            
            hexToPlateId[seedHexId] = i;
            plate.hexIds.Add(seedHexId);
            unassignedHexIds.Remove(seedHexId);
            
            var frontier = new Queue<string>();
            frontier.Enqueue(seedHexId);
            plateFrontiers.Add(frontier);
        }

        // Create minor plates with seed hexes
        for (int i = 0; i < minorPlateCount && unassignedHexIds.Count > 0; i++)
        {
            int randomIdx = random.Next(unassignedHexIds.Count);
            string seedHexId = unassignedHexIds.ElementAt(randomIdx);
            
            // Minor plates have 50% chance to be Continental/Oceanic, 50% chance to be Micro
            PlateType minorPlateType;
            if (random.NextDouble() < 0.5)
            {
                // Use same continental weight as major plates
                minorPlateType = random.NextDouble() < continentalPlateWeight ? PlateType.Continental : PlateType.Oceanic;
            }
            else
            {
                minorPlateType = PlateType.Micro;
            }
            
            var plate = new TectonicPlate 
            { 
                plateId = majorPlateCount + i, 
                isMajor = false,
                type = minorPlateType,
                driftLatitude = (random.NextDouble() * 180.0) - 90.0,
                driftLongitude = (random.NextDouble() * 360.0) - 180.0,
                driftSpeed = (float)(random.NextDouble() * 1.5 + 0.3) // 0.3 to 1.8 (slower than major)
            };
            plates.Add(plate);
            
            hexToPlateId[seedHexId] = majorPlateCount + i;
            plate.hexIds.Add(seedHexId);
            unassignedHexIds.Remove(seedHexId);
            
            var frontier = new Queue<string>();
            frontier.Enqueue(seedHexId);
            plateFrontiers.Add(frontier);
        }

        // Round-robin growth: each plate expands one step per round
        while (unassignedHexIds.Count > 0)
        {
            bool anyExpansion = false;

            for (int plateIdx = 0; plateIdx < plateFrontiers.Count; plateIdx++)
            {
                var frontier = plateFrontiers[plateIdx];
                var plate = plates[plateIdx];
                
                if (frontier.Count == 0)
                    continue;

                // Skip if this is a minor plate that has reached max size
                if (!plate.isMajor && plate.hexIds.Count >= minorPlateMaxSize)
                    continue;

                // Expand one step for this plate
                string currentHexId = frontier.Dequeue();
                Hexagon currentHex = cellsById[currentHexId];
                var neighbors = GetNeighbors(currentHex);

                foreach (Hexagon neighbor in neighbors)
                {
                    // Stop if minor plate would exceed max size
                    if (!plate.isMajor && plate.hexIds.Count >= minorPlateMaxSize)
                        break;

                    string neighborId = neighbor.ToStrId();

                    if (!unassignedHexIds.Contains(neighborId))
                        continue; // Already assigned to a plate

                    // Assign to this plate
                    hexToPlateId[neighborId] = plate.plateId;
                    plate.hexIds.Add(neighborId);
                    unassignedHexIds.Remove(neighborId);
                    frontier.Enqueue(neighborId);
                    anyExpansion = true;
                }
            }

            // If no plate expanded, break to avoid infinite loop
            if (!anyExpansion)
                break;
        }

        // Assign remaining hexes to nearest plate
        foreach (string hexId in unassignedHexIds.ToList())
        {
            AssignToNearestPlate(hexId);
        }

        if (showStatistics)
        {
            GD.Print($"Generated {plates.Count} tectonic plates ({majorPlateCount} major, {minorPlateCount} minor)");
            for (int i = 0; i < plates.Count; i++)
            {
                var plate = plates[i];
                GD.Print($"  Plate {i}: {plate.hexIds.Count} cells, type={plate.type}, drift=({plate.driftLatitude:F1}°, {plate.driftLongitude:F1}°), speed={plate.driftSpeed:F2}");
            }
        }
    }

    private void AssignToNearestPlate(string hexHexId)
    {
        if (hexToPlateId.ContainsKey(hexHexId))
            return; // Already assigned

        Hexagon hex = cellsById[hexHexId];
        var neighbors = GetNeighbors(hex);

        foreach (Hexagon neighbor in neighbors)
        {
            string neighborId = neighbor.ToStrId();
            if (hexToPlateId.TryGetValue(neighborId, out int plateId))
            {
                hexToPlateId[hexHexId] = plateId;
                plates[plateId].hexIds.Add(hexHexId);
                return;
            }
        }
    }

    /// <summary>
    /// Generates tectonic-based heights for all hexes.
    /// Base heights depend on plate type, with adjustments at plate boundaries.
    /// </summary>
    private void GenerateTectonicHeights()
    {
        hexTectonicHeights.Clear();

        if (plates.Count == 0 || allCells.Count == 0)
            return;

        // Step 1: Set base heights for all hexes based on plate type
        foreach (var hex in allCells)
        {
            string hexId = hex.ToStrId();
            var plate = GetPlateForHex(hexId);
            
            if (plate == null)
            {
                hexTectonicHeights[hexId] = 0.5f; // Default for unassigned
                continue;
            }

            // Base heights: Continental=0.6, Oceanic=0.4, Micro=0.5
            float baseHeight = plate.type switch
            {
                PlateType.Continental => 0.65f,
                PlateType.Oceanic => 0.35f,
                PlateType.Micro => 0.5f,
                _ => 0.5f
            };

            hexTectonicHeights[hexId] = baseHeight;
        }

        // Step 2: Adjust heights at plate boundaries
        foreach (var hex in allCells)
        {
            string hexId = hex.ToStrId();
            var plate = GetPlateForHex(hexId);
            
            if (plate == null)
                continue;

            var neighbors = GetNeighbors(hex);
            Vector3 hexPos = CellToCartesian(hex);

            foreach (var neighbor in neighbors)
            {
                string neighborId = neighbor.ToStrId();
                var neighborPlate = GetPlateForHex(neighborId);

                // Skip if same plate or neighbor has no plate
                if (neighborPlate == null || neighborPlate.plateId == plate.plateId)
                    continue;

                // This is a boundary hex - calculate interaction type
                Vector3 neighborPos = CellToCartesian(neighbor);
                float heightAdjustment = CalculateBoundaryHeightAdjustment(
                    hex, hexPos, plate, 
                    neighbor, neighborPos, neighborPlate
                );

                // Apply adjustment (take maximum if multiple boundaries)
                float currentHeight = hexTectonicHeights[hexId];
                hexTectonicHeights[hexId] = Mathf.Max(currentHeight, currentHeight + heightAdjustment);
            }
        }

        // Clamp all heights to valid range
        foreach (var hexId in hexTectonicHeights.Keys.ToList())
        {
            hexTectonicHeights[hexId] = Mathf.Clamp(hexTectonicHeights[hexId], 0.0f, 1.0f);
        }

        if (showStatistics)
        {
            var heights = hexTectonicHeights.Values.ToList();
            float minHeight = heights.Min();
            float maxHeight = heights.Max();
            float avgHeight = heights.Average();
            GD.Print($"Tectonic heights generated: min={minHeight:F3}, max={maxHeight:F3}, avg={avgHeight:F3}");
        }
    }

    /// <summary>
    /// Calculates height adjustment at a plate boundary based on plate types and convergence.
    /// </summary>
    private float CalculateBoundaryHeightAdjustment(
        Hexagon hex, Vector3 hexPos, TectonicPlate plate,
        Hexagon neighbor, Vector3 neighborPos, TectonicPlate neighborPlate)
    {
        // Calculate boundary normal (pointing from hex toward neighbor)
        Vector3 boundaryNormal = (neighborPos - hexPos).Normalized();

        // Convert plate drift directions to 3D vectors
        Vector3 plateDrift = SphericalGeometry.LatLonToVector3(plate.driftLatitude, plate.driftLongitude).Normalized();
        Vector3 neighborDrift = SphericalGeometry.LatLonToVector3(neighborPlate.driftLatitude, neighborPlate.driftLongitude).Normalized();

        // Calculate relative motion along boundary normal
        // Positive = convergent (plates moving toward each other)
        // Negative = divergent (plates moving apart)
        float plateTowardBoundary = plateDrift.Dot(boundaryNormal) * plate.driftSpeed;
        float neighborTowardBoundary = neighborDrift.Dot(-boundaryNormal) * neighborPlate.driftSpeed;
        float convergence = plateTowardBoundary + neighborTowardBoundary;

        // Determine interaction type and calculate height adjustment
        if (convergence > 0.6f) // Convergent boundary
        {
            if (plate.type == PlateType.Continental && neighborPlate.type == PlateType.Continental)
            {
                // Continental-Continental: Major mountain building
                return 0.25f; // Can reach up to 0.95-1.0
            }
            else if (plate.type == PlateType.Continental && neighborPlate.type == PlateType.Oceanic)
            {
                // Continental side: Volcanic arc mountains
                return 0.05f; // Continental rises to ~0.65 (volcanic arc)
            }
            else if (plate.type == PlateType.Oceanic && neighborPlate.type == PlateType.Continental)
            {
                // Oceanic side: Subduction, oceanic plate goes down
                return -0.15f; // Oceanic drops to ~0.25 (trench)
            }
            else if (plate.type == PlateType.Oceanic && neighborPlate.type == PlateType.Oceanic)
            {
                // Oceanic-Oceanic: Island arcs
                return 0.15f; // Forms islands at ~0.55
            }
        }
        else if (convergence < -0.6f) // Divergent boundary
        {
            if (plate.type == PlateType.Oceanic && neighborPlate.type == PlateType.Oceanic)
            {
                // Mid-ocean ridge: Slight elevation
                return 0.05f; // Rises slightly to ~0.45
            }
            else if (plate.type == PlateType.Continental || neighborPlate.type == PlateType.Continental)
            {
                // Continental rift: Lowering
                return -0.05f; // Drops to ~0.55 (rift valley)
            }
        }

        // Transform boundary or minimal interaction
        return 0.0f;
    }
    #endregion

    #region Private Methods - Cell Type Detection
    private bool IsPentagon(Hexagon hex)
    {
        (int dx, int dy, int dz)[] directions = new (int, int, int)[]
        {
            (1, -1, 0), (1, 0, -1), (0, 1, -1),
            (-1, 1, 0), (-1, 0, 1), (0, -1, 1)
        };

        HashSet<string> uniqueNeighbors = new HashSet<string>();
        foreach (var d in directions)
        {
            Hexagon neighbor = hex.ComputeNeighbor(d);
            uniqueNeighbors.Add(neighbor.ToStrId());
        }

        return uniqueNeighbors.Count == 5;
    }
    #endregion

    #region Private Methods - Statistics
    private void CalculateSeaLevel()
    {
        var generator = GetHeightmapGenerator();
        if (generator == null || allCells.Count == 0)
        {
            SeaLevel = 0.5f; // Default to middle if no heightmap
            return;
        }

        // Sample heights from all cells
        var heights = new List<float>();
        foreach (Hexagon cell in allCells)
        {
            float height = generator.GetHeightAtCell(cell);
            heights.Add(height);
        }

        if (heights.Count == 0)
        {
            SeaLevel = 0.5f;
            return;
        }

        // Sort heights and find the 60th percentile (lower 55% underwater)
        heights.Sort();
        int percentileIndex = (int)(heights.Count * 0.55);
        percentileIndex = Mathf.Clamp(percentileIndex, 0, heights.Count - 1);
        
        SeaLevel = heights[percentileIndex];

        if (showStatistics)
            GD.Print($"Sea level calculated: {SeaLevel:F4} (40% above water, 60% below)");
    }

    private void PrintAreaStatistics()
    {
        var (mean, std, count) = ComputeHexAreaStats();
        if (count > 0)
        {
            GD.Print($"Hex area stats ({count} cells): mean={mean:F4} km^2, std={std:F4} km^2");
            double relStdPercent = mean > 0 ? (std / mean) * 100.0 : 0.0;
            GD.Print($"Hex area relative std: {relStdPercent:F3}%");
        }
        else
        {
            GD.PrintErr("Hex area stats unavailable (no valid hexes for area estimation).");
        }
    }

    private (double mean, double std, int count) ComputeHexAreaStats()
    {
        List<double> areas = new List<double>();

        foreach (Hexagon cell in allCells)
        {
            if (IsPentagon(cell))
                continue;

            double area = EstimateCellAreaKm2(cell);
            if (area > 0 && !double.IsNaN(area) && !double.IsInfinity(area))
                areas.Add(area);
        }

        if (areas.Count == 0)
            return (0, 0, 0);

        double mean = areas.Average();
        double variance = areas.Sum(a => Math.Pow(a - mean, 2)) / areas.Count;
        double std = Math.Sqrt(variance);

        return (mean, std, areas.Count);
    }

    private double EstimateCellAreaKm2(Hexagon cell)
    {
        (int dx, int dy, int dz)[] directions = new (int, int, int)[]
        {
            (1, -1, 0), (1, 0, -1), (0, 1, -1),
            (-1, 1, 0), (-1, 0, 1), (0, -1, 1)
        };

        Vector3 center = grid.projection.InvProject(cell.P, cell.face).Normalized();
        if (!IsFinite(center))
            return -1;

        List<Vector3> boundary = new List<Vector3>();

        foreach (var d in directions)
        {
            Hexagon neighbor = cell.ComputeNeighbor(d);
            Vector3 neighborCenter = grid.projection.InvProject(neighbor.P, neighbor.face).Normalized();
            if (!IsFinite(neighborCenter))
                continue;

            Vector3 midpoint = (center + neighborCenter).Normalized();
            if (IsFinite(midpoint))
                boundary.Add(midpoint);
        }

        if (boundary.Count < 3)
            return -1;

        Vector3 axis1 = center.Cross(Vector3.Up);
        if (axis1.LengthSquared() < 1e-8f)
            axis1 = center.Cross(Vector3.Right);
        axis1 = axis1.Normalized();
        Vector3 axis2 = center.Cross(axis1).Normalized();

        var ordered = boundary.OrderBy(v =>
        {
            Vector3 tangent = (v - center * v.Dot(center)).Normalized();
            float x = tangent.Dot(axis1);
            float y = tangent.Dot(axis2);
            return MathF.Atan2(y, x);
        }).ToList();

        double areaUnitSphere = SphericalPolygonAreaUnit(ordered);
        return areaUnitSphere * grid.radius * grid.radius;
    }

    private double SphericalPolygonAreaUnit(List<Vector3> vertices)
    {
        if (vertices.Count < 3)
            return 0;

        double area = 0;
        Vector3 v0 = vertices[0].Normalized();

        for (int i = 1; i < vertices.Count - 1; i++)
        {
            Vector3 v1 = vertices[i].Normalized();
            Vector3 v2 = vertices[i + 1].Normalized();
            area += SphericalTriangleAreaUnit(v0, v1, v2);
        }

        return area;
    }

    private double SphericalTriangleAreaUnit(Vector3 a, Vector3 b, Vector3 c)
    {
        double det = a.Dot(b.Cross(c));
        double denom = 1.0 + a.Dot(b) + b.Dot(c) + c.Dot(a);
        return 2.0 * Math.Atan2(Math.Abs(det), Math.Abs(denom));
    }
    #endregion

    #region Private Methods - Utilities
    private bool IsFinite(Vector3 v)
    {
        return !(float.IsNaN(v.X) || float.IsInfinity(v.X)
              || float.IsNaN(v.Y) || float.IsInfinity(v.Y)
              || float.IsNaN(v.Z) || float.IsInfinity(v.Z));
    }
    #endregion
}
