using Godot;
using System.Collections.Generic;

/// <summary>
/// Type of tectonic plate
/// </summary>
public enum PlateType
{
    Continental,  // Thick, buoyant crust - major plates only
    Oceanic,      // Thin, dense crust - major plates only
    Micro         // Small plate fragment - minor plates only
}

/// <summary>
/// Represents a tectonic plate on a planet
/// </summary>
public class TectonicPlate
{
    public int plateId;
    public bool isMajor; // True if major plate, false if minor
    public PlateType type;
    public List<string> hexIds = new List<string>(); // Hex IDs that belong to this plate
    
    // Drift direction in spherical coordinates (degrees)
    // Latitude: -90 to 90, Longitude: -180 to 180
    public double driftLatitude;  // North-south component of drift direction
    public double driftLongitude; // East-west component of drift direction
    public float driftSpeed; // Magnitude of drift (arbitrary units)
}
