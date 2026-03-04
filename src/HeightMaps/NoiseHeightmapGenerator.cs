using Godot;
using System;

/// <summary>
/// Generates spherical heightmap data using Godot's FastNoiseLite.
/// Supports various noise types and can be tuned for planetary terrain.
/// Configure noise settings directly on the FastNoiseLite resource.
/// </summary>
public partial class NoiseHeightmapGenerator : SphericalHeightmapGenerator
{
    [Export] public FastNoiseLite noise;

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

        Vector3 normalized = position.Normalized();
        
        // Sample 3D noise on sphere surface for seamless, pole-distortion-free results
        float value = noise.GetNoise3D(normalized.X, normalized.Y, normalized.Z);
        return (value + 1.0f) * 0.5f; // Remap from [-1, 1] to [0, 1]
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
