using Godot;
using static System.Math;
using System;

class Location
{
    public Vector3 X;
    public (double lat, double lon) latlon;
    public HexGrid grid;
    public int face;
    public Vector2 P;

    public Location(HexGrid grid)
    {
        this.grid = grid;
    }

    public void RetrieveByProjection(double lat, double lon)
    {
        this.latlon = (lat, lon);
        this.X = SphericalGeometry.LatLonToVector3(lat, lon);

        // Find which face to project onto (highest dot product with k)
        double max_dot = double.MinValue;
        int best_face = 0;

        for (int i = 0; i < 20; i++)
        {
            double[] k = Icosahedron.k[i];
            double dot = k[0] * X.X + k[1] * X.Y + k[2] * X.Z;
            if (dot > max_dot)
            {
                max_dot = dot;
                best_face = i;
            }
        }

        this.face = best_face;
        this.P = grid.projection.Project(X, face);
    }

    public void RetrieveByProjection(Vector3 X)
    {
        this.X = X;
        this.latlon = SphericalGeometry.Vector3ToLatLon(X);

        // Find which face to project onto
        double max_dot = double.MinValue;
        int best_face = 0;

        for (int i = 0; i < 20; i++)
        {
            double[] k = Icosahedron.k[i];
            double dot = k[0] * X.X + k[1] * X.Y + k[2] * X.Z;
            if (dot > max_dot)
            {
                max_dot = dot;
                best_face = i;
            }
        }

        this.face = best_face;
        this.P = grid.projection.Project(X, face);
    }

    public (int face, (int x, int y, int z) pos) FindPosFromPTrB(int face, (double x, double y, double z) P_TrB, int n)
    {
        // Finds hex pos (a, b, c) from triangular barycentric coordinates
        int N = 2 * n + 1;

        double x = P_TrB.x;
        double y = P_TrB.y;
        double z = P_TrB.z;

        int u = (int)(x * (N + 1) / 2);
        int v = (int)(y * (N + 1) / 2);
        int w = (int)(z * (N + 1) / 2);

        int a = (2 + (N - v) + w) / 3;
        int b = (2 + (N - w) + u) / 3;
        int c = N + 1 - (a + b);

        if (a < 0 || b < 0 || c < 0 || a > n + 1 || b > n + 1 || c > n + 1)
        {
            return grid.RectifyCoordinates(face, (a, b, c), n);
        }

        return (face, (a, b, c));
    }

    public Hexagon[] FindHex(int n)
    {
        // Finds which hexagon(s) the point P belongs to
        double margin = grid.margin;

        // Convert P to triangular barycentric coordinates: Tr.T.dot(P)
        double x = Icosahedron.Tr[0][0] * P.X + Icosahedron.Tr[0][1] * P.Y;
        double y = Icosahedron.Tr[1][0] * P.X + Icosahedron.Tr[1][1] * P.Y;
        double z = Icosahedron.Tr[0][2] * P.X + Icosahedron.Tr[1][2] * P.Y;

        x += 1;
        y += 1;
        z += 1;

        System.Collections.Generic.HashSet<(int face, (int, int, int) pos)> res_set = 
            new System.Collections.Generic.HashSet<(int, (int, int, int))>();

        if (margin > 0)
        {
            res_set.Add(FindPosFromPTrB(face, (x, y, z), n));

            double[] offsets = new double[] { -margin, margin };
            foreach (double j in offsets)
            {
                res_set.Add(FindPosFromPTrB(face, (x + j, y - 0.5 * j, z - 0.5 * j), n));
                res_set.Add(FindPosFromPTrB(face, (x - 0.5 * j, y + j, z - 0.5 * j), n));
                res_set.Add(FindPosFromPTrB(face, (x - 0.5 * j, y - 0.5 * j, z + j), n));
            }
        }
        else
        {
            res_set.Add(FindPosFromPTrB(face, (x, y, z), n));
        }

        System.Collections.Generic.List<Hexagon> result = new System.Collections.Generic.List<Hexagon>();
        foreach (var (f, pos) in res_set)
        {
            result.Add(new Hexagon(grid, f, pos, res: n + 1, solve_conflicts: true));
        }

        return result.ToArray();
    }
}
