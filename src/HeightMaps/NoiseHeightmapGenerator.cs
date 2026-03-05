using Godot;
using System;

/// <summary>
/// Enumeration of different heightmap generation modes
/// </summary>
public enum HeightmapMode
{
    PureNoise,      // Direct noise sampling without transformation
    PseudoTectonic, // Multi-scale noise for dramatic plate-like terrain
    Tectonic        // Realistic tectonic plate simulation (TBD)
}

/// <summary>
/// Generates spherical heightmap data using Godot's FastNoiseLite.
/// Supports various noise types and can be tuned for planetary terrain.
/// Configure noise settings directly on the FastNoiseLite resource.
/// </summary>
public partial class NoiseHeightmapGenerator : SphericalHeightmapGenerator
{
    [Export] public FastNoiseLite noise;
    [Export] public HeightmapMode Mode = HeightmapMode.PureNoise;
    [Export] public int tectonicMapResolution = 256; // Resolution of the pre-sampled tectonic heightmap (width per tile, default 256 = 768x384 total)
    [Export(PropertyHint.Range, "0.0,10.0,")] public float tectonicBlurRadius = 1.0f; // Gaussian blur radius for tectonic heightmap smoothing
    [Export(PropertyHint.Range, "0.0,1.0,")] public float lowFrequencyStrength = 0.05f; // Strength of low-frequency noise (large features)
    [Export(PropertyHint.Range, "0.01,2.0,")] public float lowFrequency = 0.3f; // Frequency scale for low-frequency noise
    [Export(PropertyHint.Range, "0.0,1.0,")] public float highFrequencyStrength = 0.03f; // Strength of high-frequency noise (detail)
    [Export(PropertyHint.Range, "0.5,10.0,")] public float highFrequency = 3.0f; // Frequency scale for high-frequency noise

    // Cache for tectonic mode
    private Planet cachedPlanet = null;
    private float[,] tectonicHeightMap = null; // 2D lat-lon heightmap (3x3 tiled for seamless wrapping)
    private int mapWidth = 0;
    private int mapHeight = 0;

    public override void _Ready()
    {
        base._Ready();

        // Create default noise if not assigned
        if (noise == null)
        {
            noise = new FastNoiseLite();
            noise.NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex;
            noise.Seed = 0;
            noise.Frequency = 1.0f;
            noise.FractalOctaves = 4;
        }
    }

    public override float GetRawHeightAtLatLon(double lat, double lon)
    {
        // Convert to 3D for seamless spherical sampling
        Vector3 pos = SphericalGeometry.LatLonToVector3(lat, lon);
        return GetRawHeightAtPosition(pos);
    }

    public override float GetRawHeightAtPosition(Vector3 position)
    {
        if (noise == null)
            return 0.0f;

        return Mode switch
        {
            HeightmapMode.PureNoise => GetRawHeightPureNoise(position),
            HeightmapMode.PseudoTectonic => GetRawHeightPseudoTectonic(position),
            HeightmapMode.Tectonic => GetRawHeightTectonic(position),
            _ => 0.0f
        };
    }

    /// <summary>
    /// Pure noise mode: direct sampling of noise without transformation
    /// </summary>
    private float GetRawHeightPureNoise(Vector3 position)
    {
        Vector3 normalized = position.Normalized();
        
        // Sample 3D noise on sphere surface for seamless, pole-distortion-free results
        float value = noise.GetNoise3D(normalized.X, normalized.Y, normalized.Z);
        return (value + 1.0f) * 0.5f; // Remap from [-1, 1] to [0, 1]
    }

    /// <summary>
    /// PseudoTectonic mode: multi-scale noise for dramatic plate-like terrain features
    /// Uses low-frequency base for continental structures, medium for ridges, high for detail
    /// </summary>
    private float GetRawHeightPseudoTectonic(Vector3 position)
    {
        Vector3 normalized = position.Normalized();
        
        // Low frequency: continental plates and major structures
        float lowFreq = noise.GetNoise3D(normalized.X * 0.2f, normalized.Y * 0.2f, normalized.Z * 0.2f);
        
        // Medium frequency: mountain ranges and ridge formations
        float medFreq = noise.GetNoise3D(normalized.X * 0.8f, normalized.Y * 0.8f, normalized.Z * 0.8f);
        
        // High frequency: surface detail and crust texture
        float highFreq = noise.GetNoise3D(normalized.X, normalized.Y, normalized.Z);
        
        // Combine scales: emphasize larger tectonic structures and ridge formations
        float combined = lowFreq * 0.5f + medFreq * 0.35f + highFreq * 0.15f;
        
        // Normalize to [0, 1]
        return (combined + 1.0f) * 0.5f;
    }

    /// <summary>
    /// Tectonic mode: applies small noise variation to tectonic plate heights
    /// Uses pre-sampled 2D lat-lon heightmap for fast, smooth lookups
    /// </summary>
    private float GetRawHeightTectonic(Vector3 position)
    {
        // Get the parent Planet node
        Planet planet = GetParent() as Planet;
        if (planet == null || !planet.HasTectonicHeights)
        {
            // Fallback to pure noise if no tectonic data available
            return GetRawHeightPureNoise(position);
        }

        // Build heightmap if needed (first time or planet changed)
        if (cachedPlanet != planet || tectonicHeightMap == null)
        {
            BuildTectonicHeightMap(planet);
            cachedPlanet = planet;
        }

        // Convert position to lat/lon
        Vector3 normalized = position.Normalized();
        var (lat, lon) = SphericalGeometry.Vector3ToLatLon(normalized);
        
        // Sample the tectonic heightmap using bilinear interpolation
        float tectonicHeight = SampleHeightMap(lat, lon);

        // Apply multi-scale noise variation
        float totalNoise = 0.0f;
        
        // Low frequency noise (large-scale features)
        if (lowFrequencyStrength > 0.0f)
        {
            Vector3 lowFreqPos = normalized * lowFrequency;
            float lowFreqNoise = noise.GetNoise3D(lowFreqPos.X, lowFreqPos.Y, lowFreqPos.Z);
            totalNoise += lowFreqNoise * lowFrequencyStrength;
        }
        
        // High frequency noise (fine detail)
        if (highFrequencyStrength > 0.0f)
        {
            Vector3 highFreqPos = normalized * highFrequency;
            float highFreqNoise = noise.GetNoise3D(highFreqPos.X, highFreqPos.Y, highFreqPos.Z);
            totalNoise += highFreqNoise * highFrequencyStrength;
        }

        // Combine tectonic base with noise variation
        float finalHeight = tectonicHeight + totalNoise;

        // Clamp to valid range [0, 1]
        return Mathf.Clamp(finalHeight, 0.0f, 1.0f);
    }

    /// <summary>
    /// Samples the tectonic heightmap with bilinear interpolation
    /// </summary>
    private float SampleHeightMap(double lat, double lon)
    {
        if (tectonicHeightMap == null || mapWidth == 0 || mapHeight == 0)
            return 0.5f;

        // Normalize lat/lon to [0, 1] range
        // lat: -90 to 90 -> 0 to 1
        // lon: -180 to 180 -> 0 to 1
        float u = (float)((lon + 180.0) / 360.0);
        float v = (float)((lat + 90.0) / 180.0);

        // Map to center tile of 3x3 grid (use tile [1,1])
        // This ensures we can wrap seamlessly
        u = (u + 1.0f) / 3.0f; // Offset to middle tile
        v = (v + 1.0f) / 3.0f;

        // Convert to pixel coordinates
        float x = u * (mapWidth - 1);
        float y = v * (mapHeight - 1);

        // Get integer coordinates for bilinear sampling
        int x0 = Mathf.FloorToInt(x);
        int y0 = Mathf.FloorToInt(y);
        int x1 = x0 + 1;
        int y1 = y0 + 1;

        // Clamp to map bounds
        x0 = Mathf.Clamp(x0, 0, mapWidth - 1);
        x1 = Mathf.Clamp(x1, 0, mapWidth - 1);
        y0 = Mathf.Clamp(y0, 0, mapHeight - 1);
        y1 = Mathf.Clamp(y1, 0, mapHeight - 1);

        // Get fractional parts for interpolation
        float fx = x - x0;
        float fy = y - y0;

        // Bilinear interpolation
        float h00 = tectonicHeightMap[y0, x0];
        float h10 = tectonicHeightMap[y0, x1];
        float h01 = tectonicHeightMap[y1, x0];
        float h11 = tectonicHeightMap[y1, x1];

        float h0 = Mathf.Lerp(h00, h10, fx);
        float h1 = Mathf.Lerp(h01, h11, fx);
        
        return Mathf.Lerp(h0, h1, fy);
    }

    /// <summary>
    /// Pre-builds a 2D lat-lon heightmap from tectonic hex data
    /// Creates a 3x3 tiled map for seamless wrapping
    /// </summary>
    private void BuildTectonicHeightMap(Planet planet)
    {
        GD.Print("Building tectonic heightmap...");
        var timer = System.Diagnostics.Stopwatch.StartNew();

        // Pre-cache all hex directions and heights for fast lookup
        var hexCache = new System.Collections.Generic.List<(Vector3 dir, float height)>();
        foreach (var hex in planet.AllCells)
        {
            Vector3 dir = planet.CellToCartesian(hex).Normalized();
            float height = planet.GetTectonicHeightAtHex(hex);
            hexCache.Add((dir, height));
        }
        GD.Print($"  Cached {hexCache.Count} hex positions");

        // Create 3x3 tiled map (width x height)
        // Height is half of width for equirectangular projection
        mapWidth = tectonicMapResolution * 3;
        mapHeight = (tectonicMapResolution / 2) * 3;
        
        tectonicHeightMap = new float[mapHeight, mapWidth];

        // Build a spatial acceleration grid (coarse grid)
        // Divide sphere into 20x10 lat/lon buckets
        int gridLat = 10;
        int gridLon = 20;
        var spatialGrid = new System.Collections.Generic.List<(Vector3 dir, float height)>[gridLat, gridLon];
        
        for (int i = 0; i < gridLat; i++)
        {
            for (int j = 0; j < gridLon; j++)
            {
                spatialGrid[i, j] = new System.Collections.Generic.List<(Vector3 dir, float height)>();
            }
        }

        // Assign each hex to grid cells
        foreach (var (dir, height) in hexCache)
        {
            var (lat, lon) = SphericalGeometry.Vector3ToLatLon(dir);
            int latIdx = Mathf.Clamp((int)((lat + 90.0) / 180.0 * gridLat), 0, gridLat - 1);
            int lonIdx = Mathf.Clamp((int)((lon + 180.0) / 360.0 * gridLon), 0, gridLon - 1);
            spatialGrid[latIdx, lonIdx].Add((dir, height));
        }
        
        GD.Print($"  Built spatial grid ({gridLat}x{gridLon})");

        int progressStep = Mathf.Max(1, mapHeight / 10);
        int totalPixels = mapWidth * mapHeight;
        int processedPixels = 0;
        
        // Sample every pixel in the 3x3 grid
        for (int y = 0; y < mapHeight; y++)
        {
            for (int x = 0; x < mapWidth; x++)
            {
                // Convert pixel to lat/lon in the 3x3 grid space
                float u = x / (float)(mapWidth - 1);
                float v = y / (float)(mapHeight - 1);

                // Convert to lat/lon (-90 to 90, -180 to 180)
                float tileU = (u * 3.0f) % 1.0f;
                float tileV = (v * 3.0f) % 1.0f;

                double lon = (tileU * 360.0) - 180.0;
                double lat = (tileV * 180.0) - 90.0;

                // Convert to 3D direction
                Vector3 dir = SphericalGeometry.LatLonToVector3(lat, lon).Normalized();

                // Find the grid cell for this direction
                int latIdx = Mathf.Clamp((int)((lat + 90.0) / 180.0 * gridLat), 0, gridLat - 1);
                int lonIdx = Mathf.Clamp((int)((lon + 180.0) / 360.0 * gridLon), 0, gridLon - 1);

                // Search only in this cell and neighbors (3x3 = 9 cells max)
                float height = 0.5f;
                float bestDot = float.MinValue;
                
                for (int di = -1; di <= 1; di++)
                {
                    for (int dj = -1; dj <= 1; dj++)
                    {
                        int ni = latIdx + di;
                        int nj = (lonIdx + dj + gridLon) % gridLon; // Wrap longitude
                        
                        if (ni >= 0 && ni < gridLat)
                        {
                            foreach (var (hexDir, hexHeight) in spatialGrid[ni, nj])
                            {
                                float dot = dir.Dot(hexDir);
                                if (dot > bestDot)
                                {
                                    bestDot = dot;
                                    height = hexHeight;
                                }
                            }
                        }
                    }
                }

                tectonicHeightMap[y, x] = height;
                processedPixels++;
            }

            // Progress indicator
            if (y % progressStep == 0)
            {
                GD.Print($"  Progress: {(y * 100 / mapHeight)}%");
            }
        }

        // Apply gaussian blur if blur radius > 0
        if (tectonicBlurRadius > 0.0f)
        {
            GD.Print($"  Applying gaussian blur (radius={tectonicBlurRadius:F1})...");
            ApplyGaussianBlur(tectonicBlurRadius);
        }

        timer.Stop();
        GD.Print($"Tectonic heightmap built: {mapWidth}x{mapHeight} ({processedPixels:N0} pixels) in {timer.ElapsedMilliseconds}ms");
    }

    /// <summary>
    /// Applies a gaussian blur to the tectonic heightmap
    /// </summary>
    private void ApplyGaussianBlur(float radius)
    {
        if (tectonicHeightMap == null || radius <= 0)
            return;

        // Create temporary buffer for blur result
        float[,] blurred = new float[mapHeight, mapWidth];

        // Calculate gaussian kernel size (3 sigma covers 99.7% of distribution)
        int kernelSize = Mathf.CeilToInt(radius * 3);
        if (kernelSize < 1) kernelSize = 1;

        // Pre-calculate gaussian weights
        float sigma = radius;
        float twoSigmaSquared = 2.0f * sigma * sigma;
        float normalizationFactor = 1.0f / (Mathf.Sqrt(Mathf.Pi * twoSigmaSquared));

        // Apply separable gaussian blur (horizontal then vertical for efficiency)
        // Horizontal pass
        float[,] temp = new float[mapHeight, mapWidth];
        for (int y = 0; y < mapHeight; y++)
        {
            for (int x = 0; x < mapWidth; x++)
            {
                float sum = 0.0f;
                float weightSum = 0.0f;

                for (int i = -kernelSize; i <= kernelSize; i++)
                {
                    int sampleX = x + i;
                    
                    // Wrap horizontally (seamless longitude)
                    if (sampleX < 0) sampleX += mapWidth;
                    if (sampleX >= mapWidth) sampleX -= mapWidth;

                    float distance = i;
                    float weight = normalizationFactor * Mathf.Exp(-(distance * distance) / twoSigmaSquared);
                    
                    sum += tectonicHeightMap[y, sampleX] * weight;
                    weightSum += weight;
                }

                temp[y, x] = sum / weightSum;
            }
        }

        // Vertical pass
        for (int y = 0; y < mapHeight; y++)
        {
            for (int x = 0; x < mapWidth; x++)
            {
                float sum = 0.0f;
                float weightSum = 0.0f;

                for (int i = -kernelSize; i <= kernelSize; i++)
                {
                    int sampleY = y + i;
                    
                    // Clamp vertically (poles don't wrap)
                    sampleY = Mathf.Clamp(sampleY, 0, mapHeight - 1);

                    float distance = i;
                    float weight = normalizationFactor * Mathf.Exp(-(distance * distance) / twoSigmaSquared);
                    
                    sum += temp[sampleY, x] * weight;
                    weightSum += weight;
                }

                blurred[y, x] = sum / weightSum;
            }
        }

        // Copy blurred result back to heightmap
        tectonicHeightMap = blurred;
    }

    /// <summary>
    /// Sets a new random seed for the noise generator.
    /// </summary>
    public void Randomize()
    {
        if (noise != null)
        {
            noise.Seed = (int)(GD.Randi() % int.MaxValue);
        }
    }

    /// <summary>
    /// Sets the noise type.
    /// </summary>
    public void SetNoiseType(FastNoiseLite.NoiseTypeEnum noiseType)
    {
        if (noise != null)
        {
            noise.NoiseType = noiseType;
        }
    }

    /// <summary>
    /// Sets the fractal type for noise generation.
    /// </summary>
    public void SetFractalType(FastNoiseLite.FractalTypeEnum fractalType)
    {
        if (noise != null)
        {
            noise.FractalType = fractalType;
        }
    }
}
