using Godot;
using static System.Math;
using System;

public class Hexagon
{
    private static readonly string[] face_char = new string[]
    {
        "A", "B", "C", "D", "E", "F", "G", "H", "I", "J",
        "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T"
    };

    public HexGrid grid;
    public int face;
    public (int x, int y, int z) pos;
    public int n; // Resolution - 1
    public bool vertex_conflicts;
    public bool edge_conflicts;

    private Vector2? _P = null;

    public Vector2 P
    {
        get
        {
            if (_P == null)
            {
                // P = 2 * sqrt(3) * Bis.dot(pos) / (3 * (n + 1))
                double x = pos.x;
                double y = pos.y;
                double z = pos.z;

                double p0 = Icosahedron.Bis[0][0] * x + Icosahedron.Bis[0][1] * y + Icosahedron.Bis[0][2] * z;
                double p1 = Icosahedron.Bis[1][0] * x + Icosahedron.Bis[1][1] * y + Icosahedron.Bis[1][2] * z;

                double factor = 2 * Sqrt(3) / (3 * (n + 1));
                _P = new Vector2((float)(p0 * factor), (float)(p1 * factor));
            }
            return _P.Value;
        }
    }

    public Hexagon(HexGrid grid, int face, (int x, int y, int z) pos, int? res = null, bool solve_conflicts = false)
    {
        this.grid = grid;

        if (res == null)
        {
            res = (pos.x + pos.y + pos.z) / 2;
        }

        this.n = res.Value - 1;

        int a = pos.x;
        int b = pos.y;
        int c = pos.z;

        this.vertex_conflicts = (a == 0) || (b == 0) || (c == 0);
        this.edge_conflicts = ((a == n + 1) || (b == n + 1) || (c == n + 1)) && !vertex_conflicts;

        if (solve_conflicts && (vertex_conflicts || edge_conflicts))
        {
            (face, pos) = ResolveConflicts(face, pos);
        }

        this.face = face;
        this.pos = pos;
    }

    public Hexagon(HexGrid grid, string str_id, int? res = null, bool solve_conflicts = false)
    {
        this.grid = grid;
        (int face, (int, int, int) pos) = FromStrId(str_id);

        if (res == null)
        {
            res = (pos.Item1 + pos.Item2 + pos.Item3) / 2;
        }

        this.n = res.Value - 1;

        int a = pos.Item1;
        int b = pos.Item2;
        int c = pos.Item3;

        this.vertex_conflicts = (a == 0) || (b == 0) || (c == 0);
        this.edge_conflicts = ((a == n + 1) || (b == n + 1) || (c == n + 1)) && !vertex_conflicts;

        if (solve_conflicts && (vertex_conflicts || edge_conflicts))
        {
            (face, pos) = ResolveConflicts(face, pos);
        }

        this.face = face;
        this.pos = pos;
    }

    public static (int face, (int x, int y, int z) pos) FromStrId(string str_id)
    {
        int face = Array.IndexOf(face_char, str_id.Substring(0, 1));
        string[] raw_pos = str_id.Substring(1).Split('-');
        return (face, (int.Parse(raw_pos[0]), int.Parse(raw_pos[1]), int.Parse(raw_pos[2])));
    }

    public string ToStrId()
    {
        return $"{face_char[face]}{pos.x:D5}-{pos.y:D5}-{pos.z:D5}";
    }

    public override string ToString()
    {
        return $"{face} ({pos.x}, {pos.y}, {pos.z}) / n = {n}";
    }

    private (int face, (int x, int y, int z) pos) ResolveConflicts(int face, (int x, int y, int z) pos)
    {
        int a = pos.x;
        int b = pos.y;
        int c = pos.z;

        if (vertex_conflicts)
        {
            if (a == 0 && face % 10 < 5)
                return (face, (a, b, c));
            else if (a == 0)
                return (((face - 4) % 5 + 5) % 5 + 10 * (face / 10), (a, b, c));
            else if (b == 0 && face % 10 < 5)
                return (((face + 1) % 5 + 5) % 5 + 10 * (face / 10), (b, a, c));
            else if (b == 0)
                return (face - 5, (b, a, c));
            else if (c == 0 && face % 10 < 5)
                return (10 * (face / 10), (a, b, c));
            else if (c == 0)
                return (((2 - face) % 5 + 5) % 5 + 10 * (1 - face / 10), (c, a, b));
        }

        if (edge_conflicts)
        {
            if (c == n + 1 && face % 10 < 5)
                return (face, (a, b, c));
            else if (c == n + 1)
                return (face - 5, (n + 1 - a, n + 1 - b, c));
            else if (a == n + 1 && (face / 10 == 0 || face % 10 < 5))
                return (face, (a, b, c));
            else if (a == n + 1)
                return (((2 - face) % 5 + 5) % 5 + 5, (a, n + 1 - b, n + 1 - c));
            else if (b == n + 1 && face % 10 < 5)
                return (((face - 1) % 5 + 5) % 5 + 10 * (face / 10), (b, n + 1 - c, n + 1 - a));
            else if (b == n + 1 && face / 10 == 1)
                return (((1 - face) % 5 + 5) % 5 + 5, (n + 1 - a, b, n + 1 - c));
            else if (b == n + 1)
                return (face, (a, b, c));
        }

        return (face, pos);
    }

    public Hexagon ComputeNeighbor((int dx, int dy, int dz) dP)
    {
        int a = pos.x + dP.dx;
        int b = pos.y + dP.dy;
        int c = pos.z + dP.dz;

        (int new_face, (int, int, int) new_pos) = grid.RectifyCoordinates(face, (a, b, c), n);

        return new Hexagon(grid, new_face, new_pos, res: n + 1, solve_conflicts: true);
    }

    public Hexagon[] KRing(int k)
    {
        System.Collections.Generic.List<Hexagon> res = new System.Collections.Generic.List<Hexagon>();

        for (int x = -k; x <= k; x++)
        {
            for (int y = -k; y <= k; y++)
            {
                for (int z = -k; z <= k; z++)
                {
                    if (x + y + z == 0)
                    {
                        res.Add(ComputeNeighbor((x, y, z)));
                    }
                }
            }
        }

        return res.ToArray();
    }

    public double ComputeRadius()
    {
        return grid.ComputeRadiusForN(n);
    }

    public double ComputeHeight()
    {
        return grid.ComputeHeightForN(n);
    }

    public double ComputeSide()
    {
        return grid.ComputeSideForN(n);
    }
}
