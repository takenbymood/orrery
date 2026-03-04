using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Spherical planet visualization layer. Renders a Planet's hex grid data
/// using polygon meshes or markers. Handles hover highlighting via mouse interaction.
/// </summary>
public partial class SphericalPlanetView : Node3D
{
    #region Export Properties
    [Export] public float visualRadius = 10.0f; // Visual radius in Godot units
    [Export] public bool hoverHighlightEnabled = true;
    [Export] public bool printHoverChanges = true;
    [Export] public bool showMarkers = false; // Show center markers instead of polygons
    [Export] public float markerRadius = 0.08f;
    [Export] public Color hexColor = new Color(0.2f, 0.45f, 0.95f);
    [Export] public Color pentagonColor = new Color(0.9f, 0.2f, 0.2f);
    [Export] public Color highlightColor = new Color(1.0f, 0.95f, 0.2f);
    [Export] public bool useHeightmapColoring = false; // Enable heightmap-based vertex colors
    [Export] public Color waterColor = new Color(0.1f, 0.3f, 0.7f); // Color for ocean (below sea level)
    [Export] public Color lowLandColor = new Color(0.2f, 0.6f, 0.2f); // Color for low land (at sea level)
    [Export] public Color highLandColor = new Color(0.9f, 0.9f, 0.9f); // Color for high mountains
    #endregion

    #region Private Fields
    private Planet planet;
    private MeshInstance3D planetMeshInstance; // Single mesh for entire planet
    private MeshInstance3D highlightMeshInstance; // Overlay mesh for highlighting
    private Dictionary<string, (Vector3 center, List<Vector3> vertices)> cellGeometry = new Dictionary<string, (Vector3, List<Vector3>)>();
    private string highlightedCellId = null;
    private string lastHoveredHexId = null;
    private int invalidCenterCount = 0;
    #endregion

    #region Public Properties
    public string HighlightedCellId => highlightedCellId;
    #endregion

    #region Godot Lifecycle
    public override void _Ready()
    {
        planet = GetNode<Planet>("Planet");
        if (planet != null && planet.AllCells.Count > 0)
        {
            RenderPlanet();
        }
    }

    public override void _Process(double delta)
    {
        if (!hoverHighlightEnabled || planet == null)
            return;

        UpdateHoveredHexFromMouse();
    }
    #endregion

    #region Public Methods - Rendering
    /// <summary>
    /// Renders all cells from the planet data
    /// </summary>
    public void RenderPlanet()
    {
        if (planet == null || planet.AllCells.Count == 0)
        {
            GD.PrintErr("Cannot render: Planet has no cells");
            return;
        }

        ClearRender();
        invalidCenterCount = 0;

        // Create a single surface tool for the entire planet
        var surfaceTool = new SurfaceTool();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

        foreach (Hexagon cell in planet.AllCells)
        {
            bool isPentagon = IsPentagon(cell);
            Color cellColor = isPentagon ? pentagonColor : hexColor;
            
            if (showMarkers)
                CreateCellMarkerGeometry(cell, cellColor, surfaceTool);
            else
                CreateCellPolygonGeometry(cell, cellColor, surfaceTool);
        }

        // Commit the combined mesh
        var planetMesh = surfaceTool.Commit();
        planetMeshInstance = new MeshInstance3D();
        planetMeshInstance.Mesh = planetMesh;
        
        var material = new StandardMaterial3D();
        material.AlbedoColor = useHeightmapColoring ? Colors.White : Colors.White;
        material.VertexColorUseAsAlbedo = true;
        material.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        planetMeshInstance.SetSurfaceOverrideMaterial(0, material);
        
        planet.AddChild(planetMeshInstance);

        if (invalidCenterCount > 0)
            GD.PrintErr($"Skipped {invalidCenterCount} cells during rendering due to non-finite projection results.");
    }

    /// <summary>
    /// Clears all rendered meshes
    /// </summary>
    public void ClearRender()
    {
        if (planetMeshInstance != null)
        {
            planetMeshInstance.QueueFree();
            planetMeshInstance = null;
        }
        
        if (highlightMeshInstance != null)
        {
            highlightMeshInstance.QueueFree();
            highlightMeshInstance = null;
        }
        
        cellGeometry.Clear();
        highlightedCellId = null;
        lastHoveredHexId = null;
    }

    /// <summary>
    /// Highlights a specific cell
    /// </summary>
    public bool HighlightCell(Hexagon hex)
    {
        if (hex == null)
            return false;

        return HighlightCellById(hex.ToStrId());
    }

    /// <summary>
    /// Highlights a cell by its string ID
    /// </summary>
    public bool HighlightCellById(string hexId)
    {
        if (string.IsNullOrEmpty(hexId) || !cellGeometry.ContainsKey(hexId))
            return false;

        if (highlightedCellId == hexId)
            return true;

        ClearHighlight();
        
        // Create highlight overlay mesh for this cell
        var (center, vertices) = cellGeometry[hexId];
        var surfaceTool = new SurfaceTool();
        surfaceTool.Begin(Mesh.PrimitiveType.Triangles);
        
        // Rebuild this hex's geometry with highlight color
        BuildHexGeometry(surfaceTool, center, vertices, highlightColor, highlightColor);
        
        var highlightMesh = surfaceTool.Commit();
        highlightMeshInstance = new MeshInstance3D();
        highlightMeshInstance.Mesh = highlightMesh;
        
        var material = new StandardMaterial3D();
        material.AlbedoColor = highlightColor;
        material.VertexColorUseAsAlbedo = false;
        material.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        material.AlbedoColor = new Color(highlightColor.R, highlightColor.G, highlightColor.B, 0.6f);
        highlightMeshInstance.SetSurfaceOverrideMaterial(0, material);
        
        planet.AddChild(highlightMeshInstance);
        highlightedCellId = hexId;
        return true;
    }

    /// <summary>
    /// Clears the current highlight
    /// </summary>
    public void ClearHighlight()
    {
        if (highlightMeshInstance != null)
        {
            highlightMeshInstance.QueueFree();
            highlightMeshInstance = null;
        }
        highlightedCellId = null;
    }
    #endregion

    #region Private Methods - Mouse Interaction
    private void UpdateHoveredHexFromMouse()
    {
        Camera3D camera = GetViewport().GetCamera3D();
        if (camera == null)
            return;

        Vector2 mousePos = GetViewport().GetMousePosition();
        Vector3 rayOrigin = camera.ProjectRayOrigin(mousePos);
        Vector3 rayDir = camera.ProjectRayNormal(mousePos).Normalized();

        if (!TryIntersectPlanetSphere(rayOrigin, rayDir, out Vector3 hitPoint))
        {
            if (lastHoveredHexId != null)
            {
                ClearHighlight();
                lastHoveredHexId = null;
            }
            return;
        }

        Vector3 localHit = planet.ToLocal(hitPoint).Normalized();
        Hexagon hoveredCell = planet.GetNearestCellAtLocalDirection(localHit);
        if (hoveredCell == null)
            return;

        string hoveredId = hoveredCell.ToStrId();
        if (hoveredId == lastHoveredHexId)
            return;

        if (HighlightCell(hoveredCell))
        {
            lastHoveredHexId = hoveredId;

            if (printHoverChanges)
            {
                var (lat, lon) = planet.CellToLatLon(hoveredCell);
                GD.Print($"Hover hex: {hoveredId} | lat={lat:F3}, lon={lon:F3}");
            }
        }
    }

    private bool TryIntersectPlanetSphere(Vector3 rayOrigin, Vector3 rayDir, out Vector3 hitPoint)
    {
        Vector3 center = planet.GlobalTransform.Origin;
        Vector3 scale = planet.GlobalTransform.Basis.Scale;
        float sphereRadius = visualRadius * (Mathf.Abs(scale.X) + Mathf.Abs(scale.Y) + Mathf.Abs(scale.Z)) / 3.0f;

        Vector3 oc = rayOrigin - center;
        float a = rayDir.Dot(rayDir);
        float b = 2.0f * oc.Dot(rayDir);
        float c = oc.Dot(oc) - sphereRadius * sphereRadius;

        float discriminant = b * b - 4.0f * a * c;
        if (discriminant < 0)
        {
            hitPoint = Vector3.Zero;
            return false;
        }

        float sqrtD = Mathf.Sqrt(discriminant);
        float t0 = (-b - sqrtD) / (2.0f * a);
        float t1 = (-b + sqrtD) / (2.0f * a);

        float t = t0 >= 0 ? t0 : t1;
        if (t < 0)
        {
            hitPoint = Vector3.Zero;
            return false;
        }

        hitPoint = rayOrigin + rayDir * t;
        return true;
    }
    #endregion

    #region Private Methods - Rendering
    private Color GetHeightColor(Vector3 position)
    {
        if (!useHeightmapColoring)
            return Colors.White; // Return neutral color if heightmap coloring disabled

        Vector3 normalized = position.Normalized();
        var (lat, lon) = SphericalGeometry.Vector3ToLatLon(normalized);
        float height = planet.GetHeightAtLatLon(lat, lon);
        return HeightToColor(height, planet.SeaLevel);
    }

    private void AddTriangleToMesh(SurfaceTool toolSurface, Vector3 p1, Color c1, Vector3 p2, Color c2, Vector3 p3, Color c3)
    {
        Vector3 normal = (p1 + p2 + p3).Normalized();

        toolSurface.SetNormal(normal);
        toolSurface.SetColor(c1);
        toolSurface.AddVertex(p1);

        toolSurface.SetNormal(normal);
        toolSurface.SetColor(c2);
        toolSurface.AddVertex(p2);

        toolSurface.SetNormal(normal);
        toolSurface.SetColor(c3);
        toolSurface.AddVertex(p3);
    }

    private void CreateCellMarkerGeometry(Hexagon hex, Color color, SurfaceTool surfaceTool)
    {
        Vector3 center = planet.Grid.projection.InvProject(hex.P, hex.face).Normalized() * visualRadius;
        string hexId = hex.ToStrId();

        if (!IsFinite(center))
        {
            invalidCenterCount++;
            return;
        }

        // Sample heightmap at marker position if enabled
        Color markerColor = color;
        if (useHeightmapColoring)
        {
            Vector3 centerNormalized = center.Normalized();
            var (lat, lon) = SphericalGeometry.Vector3ToLatLon(centerNormalized);
            float height = planet.GetHeightAtLatLon(lat, lon);
            markerColor = HeightToColor(height, planet.SeaLevel);
        }

        // Store geometry (for markers, just store the center as a single point)
        cellGeometry[hexId] = (center, new List<Vector3> { center });

        // For markers, we'll create actual sphere meshes as before
        var markerMesh = new SphereMesh();
        markerMesh.Radius = markerRadius;
        markerMesh.Height = markerRadius * 2.0f;
        markerMesh.RadialSegments = 10;
        markerMesh.Rings = 6;

        var meshInstance = new MeshInstance3D();
        meshInstance.Mesh = markerMesh;
        meshInstance.Position = center;

        var material = new StandardMaterial3D();
        material.AlbedoColor = markerColor;
        material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        meshInstance.SetSurfaceOverrideMaterial(0, material);

        planet.AddChild(meshInstance);
    }

    private void CreateCellPolygonGeometry(Hexagon hex, Color color, SurfaceTool surfaceTool)
    {
        (int dx, int dy, int dz)[] directions = new (int, int, int)[]
        {
            (1, -1, 0), (1, 0, -1), (0, 1, -1),
            (-1, 1, 0), (-1, 0, 1), (0, -1, 1)
        };

        Vector3 center = planet.Grid.projection.InvProject(hex.P, hex.face).Normalized() * visualRadius;
        string hexId = hex.ToStrId();
        if (!IsFinite(center))
        {
            invalidCenterCount++;
            return;
        }

        // Get unique neighbors in circular order
        List<(string id, Vector3 pos)> uniqueNeighbors = new List<(string, Vector3)>();
        HashSet<string> seenIds = new HashSet<string>();
        
        for (int i = 0; i < directions.Length; i++)
        {
            var d = directions[i];
            Hexagon neighbor = hex.ComputeNeighbor(d);
            string neighborId = neighbor.ToStrId();
            Vector3 neighborCenter = planet.Grid.projection.InvProject(neighbor.P, neighbor.face).Normalized() * visualRadius;
            
            if (IsFinite(neighborCenter) && !seenIds.Contains(neighborId))
            {
                uniqueNeighbors.Add((neighborId, neighborCenter));
                seenIds.Add(neighborId);
            }
        }

        // Compute boundary vertices where three cells meet
        List<Vector3> vertices = new List<Vector3>();
        for (int i = 0; i < uniqueNeighbors.Count; i++)
        {
            int nextIdx = (i + 1) % uniqueNeighbors.Count;
            
            Vector3 n1 = uniqueNeighbors[i].pos;
            Vector3 n2 = uniqueNeighbors[nextIdx].pos;
            
            Vector3 vertex = ((center + n1 + n2) / 3.0f).Normalized() * visualRadius;
            if (IsFinite(vertex))
                vertices.Add(vertex);
        }

        if (vertices.Count < 3)
        {
            invalidCenterCount++;
            return;
        }

        // Store geometry for highlighting
        cellGeometry[hexId] = (center, new List<Vector3>(vertices));

        // Sample heightmap at vertices if enabled
        Color centerColor = color;
        Dictionary<Vector3, Color> vertexColors = new Dictionary<Vector3, Color>();
        
        if (useHeightmapColoring)
        {
            // Sample height at center by converting to lat/lon first
            Vector3 centerNormalized = center.Normalized();
            var (centerLat, centerLon) = SphericalGeometry.Vector3ToLatLon(centerNormalized);
            float centerHeight = planet.GetHeightAtLatLon(centerLat, centerLon);
            centerColor = HeightToColor(centerHeight, planet.SeaLevel);
            
            // Sample height at each vertex by converting to lat/lon
            foreach (Vector3 vertex in vertices)
            {
                Vector3 vertexNormalized = vertex.Normalized();
                var (vertexLat, vertexLon) = SphericalGeometry.Vector3ToLatLon(vertexNormalized);
                float vertexHeight = planet.GetHeightAtLatLon(vertexLat, vertexLon);
                vertexColors[vertex] = HeightToColor(vertexHeight, planet.SeaLevel);
            }
        }
        else
        {
            // Use uniform color
            foreach (Vector3 vertex in vertices)
            {
                vertexColors[vertex] = color;
            }
        }

        // Add triangles to the shared surfaceTool with subdivision per wedge
        // Pentagons: 5 vertices × 4 triangles = 20 triangles
        // Hexagons: 6 vertices × 4 triangles = 24 triangles
        for (int i = 0; i < vertices.Count; i++)
        {
            Vector3 v1 = vertices[i];
            Vector3 v2 = vertices[(i + 1) % vertices.Count];

            // Compute subdivision points for this wedge
            Vector3 edgeMid = ((v1 + v2) / 2.0f).Normalized() * visualRadius;
            Vector3 innerMid1 = ((center + v1) / 2.0f).Normalized() * visualRadius;
            Vector3 innerMid2 = ((center + v2) / 2.0f).Normalized() * visualRadius;

            // Sample colors at subdivision points
            Color edgeMidColor = GetHeightColor(edgeMid);
            Color innerMid1Color = GetHeightColor(innerMid1);
            Color innerMid2Color = GetHeightColor(innerMid2);

            // Triangle 1: Center, innerMid1, innerMid2
            AddTriangleToMesh(surfaceTool, center, centerColor, innerMid1, innerMid1Color, innerMid2, innerMid2Color);

            // Triangle 2: innerMid1, v1, edgeMid
            AddTriangleToMesh(surfaceTool, innerMid1, innerMid1Color, v1, vertexColors[v1], edgeMid, edgeMidColor);

            // Triangle 3: innerMid2, edgeMid, v2
            AddTriangleToMesh(surfaceTool, innerMid2, innerMid2Color, edgeMid, edgeMidColor, v2, vertexColors[v2]);

            // Triangle 4: innerMid1, edgeMid, innerMid2
            AddTriangleToMesh(surfaceTool, innerMid1, innerMid1Color, edgeMid, edgeMidColor, innerMid2, innerMid2Color);
        }
    }

    /// <summary>
    /// Builds hex geometry with 24-triangle subdivision for highlighting
    /// </summary>
    private void BuildHexGeometry(SurfaceTool surfaceTool, Vector3 center, List<Vector3> vertices, Color centerColor, Color edgeColor)
    {
        // Same subdivision logic as CreateCellPolygonGeometry but with uniform colors
        for (int i = 0; i < vertices.Count; i++)
        {
            Vector3 v1 = vertices[i];
            Vector3 v2 = vertices[(i + 1) % vertices.Count];

            // Compute subdivision points for this wedge
            Vector3 edgeMid = ((v1 + v2) / 2.0f).Normalized() * visualRadius;
            Vector3 innerMid1 = ((center + v1) / 2.0f).Normalized() * visualRadius;
            Vector3 innerMid2 = ((center + v2) / 2.0f).Normalized() * visualRadius;

            // Use uniform colors for highlighting
            // Triangle 1: Center, innerMid1, innerMid2
            AddTriangleToMesh(surfaceTool, center, centerColor, innerMid1, centerColor, innerMid2, centerColor);

            // Triangle 2: innerMid1, v1, edgeMid
            AddTriangleToMesh(surfaceTool, innerMid1, centerColor, v1, edgeColor, edgeMid, edgeColor);

            // Triangle 3: innerMid2, edgeMid, v2
            AddTriangleToMesh(surfaceTool, innerMid2, centerColor, edgeMid, edgeColor, v2, edgeColor);

            // Triangle 4: innerMid1, edgeMid, innerMid2
            AddTriangleToMesh(surfaceTool, innerMid1, centerColor, edgeMid, edgeColor, innerMid2, centerColor);
        }
    }

    private bool IsPentagon(Hexagon hex)
    {
        (int dx, int dy, int dz)[] directions = new (int, int, int)[]
        {
            (1, -1, 0), (1, 0, -1), (0, 1, -1),
            (-1, 1, 0), (-1, 0, 1), (0, -1, 1)
        };

        HashSet<string> seen = new HashSet<string> { hex.ToStrId() };

        foreach (var d in directions)
        {
            Hexagon neighbor = hex.ComputeNeighbor(d);
            string neighborId = neighbor.ToStrId();
            if (seen.Contains(neighborId))
                return true;
            seen.Add(neighborId);
        }

        return false;
    }

    private bool IsFinite(Vector3 v)
    {
        return !(float.IsNaN(v.X) || float.IsInfinity(v.X)
              || float.IsNaN(v.Y) || float.IsInfinity(v.Y)
              || float.IsNaN(v.Z) || float.IsInfinity(v.Z));
    }

    /// <summary>
    /// Converts a height value (0-1) to a color based on sea level.
    /// Below sea level: flat water color. Above sea level: smooth gradient from lowLand to highLand.
    /// </summary>
    private Color HeightToColor(float height, float seaLevel)
    {
        height = Mathf.Clamp(height, 0.0f, 1.0f);
        seaLevel = Mathf.Clamp(seaLevel, 0.0f, 1.0f);
        
        if (height < seaLevel)
        {
            // Below sea level: uniform water color
            return waterColor;
        }
        else
        {
            // Above sea level: smooth gradient from low land to high land
            // Map [seaLevel, 1.0] to [0, 1] for interpolation
            float landRange = 1.0f - seaLevel;
            if (landRange <= 0.001f)
                return lowLandColor; // Avoid division by zero
            
            float t = (height - seaLevel) / landRange;
            return lowLandColor.Lerp(highLandColor, t);
        }
    }
    #endregion
}
