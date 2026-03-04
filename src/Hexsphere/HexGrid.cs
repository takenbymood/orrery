using Godot;
using static System.Math;
using System;

public class HexGrid
{
    public Projection projection;
    public double overlap = 0;
    public double margin = 0;
    public double radius; // Planet radius in km

    public HexGrid(Projection projection, double radius = 6371.0)
    {
        this.projection = projection;
        this.radius = radius;
    }

    public void SetOverlap(double overlap)
    {
        this.overlap = overlap;
        this.margin = 0.5 * overlap * Sqrt(5 * Sqrt(3) / (PI * radius * radius));
    }

    public int ComputeNForRadius(double r)
    {
        // Computes n (number of hexes per edge) for target hex radius
        double h_area = PI * r * r;
        double nb_h = (4 * PI * radius * radius) / h_area;
        return (int)Round(Sqrt(nb_h / 10) - 1);
    }

    public double ComputeRadiusForN(int n)
    {
        // Returns approximate radius of hexes at resolution n+1
        double nb_h = 20 * (n + 1) * (n + 1) / 2.0;
        double h_area = (4 * PI * radius * radius) / nb_h;
        double eq_r = Sqrt(h_area / PI);
        return eq_r;
    }

    public int ComputeNForHeight(double h)
    {
        // Computes n for target hex height
        double h_area = 2 * h * h * Sqrt(3);
        double nb_h = (4 * PI * radius * radius) / h_area;
        return (int)Round(Sqrt(nb_h / 10) - 1);
    }

    public double ComputeHeightForN(int n)
    {
        // Returns approximate height of hexes at resolution n+1
        double nb_h = 20 * (n + 1) * (n + 1) / 2.0;
        double h_area = (4 * PI * radius * radius) / nb_h;
        double height = Sqrt(h_area * Sqrt(3) / 6);
        return height;
    }

    public int ComputeNForSide(double s)
    {
        // Computes n for target hex side length
        double h_area = 3 * s * s * Sqrt(3) / 2;
        double nb_h = (4 * PI * radius * radius) / h_area;
        return (int)Round(Sqrt(nb_h / 10) - 1);
    }

    public double ComputeSideForN(int n)
    {
        // Returns approximate side length of hexes at resolution n+1
        double nb_h = 20 * (n + 1) * (n + 1) / 2.0;
        double h_area = (4 * PI * radius * radius) / nb_h;
        double side = Sqrt(2 * h_area * Sqrt(3) / 9);
        return side;
    }

    public (int face, (int x, int y, int z) pos) RectifyCoordinates(int face, (int x, int y, int z) pos, int n)
    {
        // Retrieves new face and pos if given pos is out of bounds
        int x = pos.x;
        int y = pos.y;
        int z = pos.z;

        while (true)
        {
            if (x > n + 1)
            {
                if (face % 10 < 5)
                {
                    (x, y, z) = ((n + 1) - z, 2 * (n + 1) - x, (n + 1) - y);
                }
                else
                {
                    (x, y, z) = (2 * (n + 1) - x, (n + 1) - y, (n + 1) - z);
                }
                face = Icosahedron.neighboring_face[face][0];
            }
            else if (y > n + 1)
            {
                if (face % 10 < 5)
                {
                    (x, y, z) = (2 * (n + 1) - y, (n + 1) - z, (n + 1) - x);
                }
                else
                {
                    (x, y, z) = ((n + 1) - x, 2 * (n + 1) - y, (n + 1) - z);
                }
                face = Icosahedron.neighboring_face[face][1];
            }
            else if (z > n + 1)
            {
                (x, y, z) = ((n + 1) - x, (n + 1) - y, 2 * (n + 1) - z);
                face = Icosahedron.neighboring_face[face][2];
            }
            else
            {
                break;
            }
        }

        return (face, (x, y, z));
    }

    public Hexagon[] LatLonToHex(double lat, double lon, int n)
    {
        // Returns hex(es) containing the point (lat, lon)
        Location location = new Location(this);
        location.RetrieveByProjection(lat, lon);
        return location.FindHex(n);
    }

    public Hexagon LatLonToNearestHex(double lat, double lon, int n, int searchK = 2)
    {
        // Returns the nearest hex center to the point (lat, lon)
        Vector3 target = SphericalGeometry.LatLonToVector3(lat, lon).Normalized();
        Hexagon[] candidates = LatLonToHex(lat, lon, n);
        return SelectNearestHexFromCandidates(target, candidates, searchK);
    }

    public (double lat, double lon) HexToLatLon(Hexagon hexagon)
    {
        // Returns (lat, lon) coordinates of hexagon center
        Vector3 X = projection.InvProject(hexagon.P, hexagon.face);
        return SphericalGeometry.Vector3ToLatLon(X);
    }

    public Hexagon[] CartesianToHex(Vector3 position, int n)
    {
        // Returns hex(es) containing the given 3D cartesian position (normalized)
        Location location = new Location(this);
        location.RetrieveByProjection(position.Normalized());
        return location.FindHex(n);
    }

    public Hexagon CartesianToNearestHex(Vector3 position, int n, int searchK = 2)
    {
        // Returns the nearest hex center to the given 3D cartesian position
        Vector3 target = position.Normalized();
        Hexagon[] candidates = CartesianToHex(target, n);
        return SelectNearestHexFromCandidates(target, candidates, searchK);
    }

    public Vector3 HexToCartesian(Hexagon hexagon)
    {
        // Returns normalized 3D cartesian position of hexagon center
        return projection.InvProject(hexagon.P, hexagon.face).Normalized();
    }

    public Hexagon GetHexById(string hexId, int? n = null)
    {
        // Returns hexagon by its string ID
        return new Hexagon(this, hexId, res: n, solve_conflicts: true);
    }

    public Hexagon[] GetNeighbors(Hexagon hex)
    {
        // Returns the 6 direct neighbors of a hexagon
        (int dx, int dy, int dz)[] directions = new (int, int, int)[]
        {
            (1, -1, 0),
            (1, 0, -1),
            (0, 1, -1),
            (-1, 1, 0),
            (-1, 0, 1),
            (0, -1, 1)
        };

        Hexagon[] neighbors = new Hexagon[6];
        for (int i = 0; i < 6; i++)
        {
            neighbors[i] = hex.ComputeNeighbor(directions[i]);
        }
        return neighbors;
    }

    public Hexagon[] GetUniqueNeighbors(Hexagon hex)
    {
        // Returns unique neighbors (5 for pentagons, 6 for hexagons)
        var neighbors = GetNeighbors(hex);
        var uniqueSet = new System.Collections.Generic.HashSet<string>();
        var result = new System.Collections.Generic.List<Hexagon>();

        foreach (var neighbor in neighbors)
        {
            string id = neighbor.ToStrId();
            if (!uniqueSet.Contains(id))
            {
                uniqueSet.Add(id);
                result.Add(neighbor);
            }
        }

        return result.ToArray();
    }

    public Hexagon[] GetNeighborsByLatLon(double lat, double lon, int n)
    {
        // Returns neighbors of hex at given lat/lon position
        var hexes = LatLonToHex(lat, lon, n);
        if (hexes.Length > 0)
            return GetUniqueNeighbors(hexes[0]);
        return new Hexagon[0];
    }

    public Hexagon[] GetNeighborsByCartesian(Vector3 position, int n)
    {
        // Returns neighbors of hex at given cartesian position
        var hexes = CartesianToHex(position, n);
        if (hexes.Length > 0)
            return GetUniqueNeighbors(hexes[0]);
        return new Hexagon[0];
    }

    public Hexagon[] GetNeighborsByHexId(string hexId, int? n = null)
    {
        // Returns neighbors of hex by its string ID
        var hex = GetHexById(hexId, n);
        return GetUniqueNeighbors(hex);
    }

    private Hexagon SelectNearestHexFromCandidates(Vector3 target, Hexagon[] candidates, int searchK)
    {
        if (candidates == null || candidates.Length == 0)
            return null;

        Hexagon seed = candidates[0];
        float bestSeedDot = float.MinValue;
        foreach (Hexagon candidate in candidates)
        {
            Vector3 center = HexToCartesian(candidate).Normalized();
            float d = center.Dot(target);
            if (d > bestSeedDot)
            {
                bestSeedDot = d;
                seed = candidate;
            }
        }

        var unique = new System.Collections.Generic.Dictionary<string, Hexagon>();
        unique[seed.ToStrId()] = seed;

        int kMax = System.Math.Max(0, searchK);
        for (int k = 1; k <= kMax; k++)
        {
            foreach (Hexagon neighbor in seed.KRing(k))
            {
                string id = neighbor.ToStrId();
                if (!unique.ContainsKey(id))
                    unique[id] = neighbor;
            }
        }

        Hexagon best = seed;
        float bestDot = float.MinValue;
        foreach (Hexagon hex in unique.Values)
        {
            Vector3 center = HexToCartesian(hex).Normalized();
            float d = center.Dot(target);
            if (d > bestDot)
            {
                bestDot = d;
                best = hex;
            }
        }

        return best;
    }
}
