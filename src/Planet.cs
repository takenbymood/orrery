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
    #endregion

    #region Private Fields
    private HexGrid grid;
    private List<Hexagon> allCells = new List<Hexagon>();
    private Dictionary<string, Hexagon> cellsById = new Dictionary<string, Hexagon>();
    private int invalidCenterCount = 0;
    private int hexagonCount = 0;
    private int pentagonCount = 0;
    #endregion

    #region Public Properties
    public HexGrid Grid => grid;
    public IReadOnlyList<Hexagon> AllCells => allCells;
    public int HexagonCount => hexagonCount;
    public int PentagonCount => pentagonCount;
    public float SeaLevel { get; private set; } = 0.5f;
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

        // Sort heights and find the 60th percentile (lower 60% underwater)
        heights.Sort();
        int percentileIndex = (int)(heights.Count * 0.6);
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
