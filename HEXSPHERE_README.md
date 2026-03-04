# Hex Sphere Visualization

## How to Use

1. **Run the project** - The hex sphere will be generated automatically when you run `game.tscn`

2. **Camera Controls:**
   - **Right Mouse Button + Drag** - Rotate camera around sphere
   - **Mouse Wheel** - Zoom in/out
   - **Spacebar (hold)** - Auto-rotate the view

## Scene Parameters

Select the `HexSphere` node in the editor to adjust:

- **resolution** (default: 4) - Hex grid resolution (higher = more hexes)
- **sphereRadius** (default: 10) - Visual size of the sphere in Godot units
- **useSnyder** (default: false) - Toggle between Gnomonic and Snyder projections
- **showWireframe** (default: true) - Show hex edges
- **hexColor** - Color of hex faces
- **wireframeColor** - Color of hex edges

## What You Should See

A sphere covered with hexagonal tiles! The hexagons should:
- Cover the entire sphere with minimal distortion
- Have clear visible edges (if wireframe is enabled)
- Be evenly distributed across all 20 icosahedron faces

## Troubleshooting

If you see errors in the console:
1. Check that all C# files are compiled (rebuild the project)
2. Verify the scene file references are correct
3. Try lowering the resolution value if it's too high

## Testing Different Resolutions

Common resolution values:
- `n = 2` - Very coarse (about 120 hexes total)
- `n = 4` - Good for testing (about 480 hexes)
- `n = 8` - Medium detail (about 1920 hexes)
- `n = 16` - High detail (about 7680 hexes)

For Earth-scale projects, compute `n` using:
```csharp
int n = grid.ComputeNForRadius(100.0); // 100 km hex radius
```
