using Godot;
using System;

/// <summary>
/// Abstract base class for generating heightmap data in spherical coordinates.
/// Can be attached as a child to a Planet node to provide elevation data.
/// </summary>
public abstract partial class SphericalHeightmapGenerator : Node
{
    [Export] public float scale = 1.0f;
    [Export] public float offset = 0.0f;

    protected Planet planet;

    public override void _Ready()
    {
        // Try to get parent Planet
        if (GetParent() is Planet p)
        {
            planet = p;
        }
    }

    /// <summary>
    /// Gets the raw height value at a spherical position (lat, lon in degrees).
    /// Returns value typically in range [0, 1] before scale/offset applied.
    /// </summary>
    public abstract float GetRawHeightAtLatLon(double lat, double lon);

    /// <summary>
    /// Gets the raw height value at a 3D cartesian position (normalized).
    /// Returns value typically in range [0, 1] before scale/offset applied.
    /// </summary>
    public abstract float GetRawHeightAtPosition(Vector3 position);

    /// <summary>
    /// Gets the scaled and offset height at a spherical position.
    /// </summary>
    public float GetHeightAtLatLon(double lat, double lon)
    {
        return GetRawHeightAtLatLon(lat, lon) * scale + offset;
    }

    /// <summary>
    /// Gets the scaled and offset height at a 3D cartesian position.
    /// </summary>
    public float GetHeightAtPosition(Vector3 position)
    {
        return GetRawHeightAtPosition(position) * scale + offset;
    }

    /// <summary>
    /// Gets the height value for a specific hex cell.
    /// Default implementation uses the cell's center position.
    /// </summary>
    public virtual float GetHeightAtCell(Hexagon cell)
    {
        if (planet == null)
            return 0.0f;

        Vector3 pos = planet.CellToCartesian(cell);
        return GetHeightAtPosition(pos);
    }

    /// <summary>
    /// Gets the height value for a hex cell by its ID.
    /// </summary>
    public float GetHeightAtCellId(string cellId)
    {
        if (planet == null)
            return 0.0f;

        Hexagon cell = planet.GetCellById(cellId);
        if (cell == null)
            return 0.0f;

        return GetHeightAtCell(cell);
    }

    /// <summary>
    /// Fills an array with height values for an array of cells.
    /// Useful for batch processing.
    /// </summary>
    public void GetHeightsForCells(Hexagon[] cells, float[] heightsOut)
    {
        if (cells == null || heightsOut == null)
            return;

        int count = Mathf.Min(cells.Length, heightsOut.Length);
        for (int i = 0; i < count; i++)
        {
            heightsOut[i] = GetHeightAtCell(cells[i]);
        }
    }
}
