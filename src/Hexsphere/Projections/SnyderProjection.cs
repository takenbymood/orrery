using Godot;
using static System.Math;

class SnyderProjection : Projection
{
    private double V;

    public SnyderProjection()
    {
        // v0 = a[0]
        double[] v0 = Icosahedron.a[0];

        // v1 = (a[0] + b[0]) * VtoC / (2 * phi)
        double[] v1_temp = new double[3];
        for (int i = 0; i < 3; i++)
        {
            v1_temp[i] = (Icosahedron.a[0][i] + Icosahedron.b[0][i]) * Icosahedron.VtoC / (2 * Icosahedron.phi);
        }

        // v2 = k[0]
        double[] v2 = Icosahedron.k[0];

        // V = determinant of [v0, v1, v2]
        V = MatrixDet3x3(v0, v1_temp, v2);
    }

    public override Vector2 Project(Vector3 X, int face)
    {
        // Get abc for face (3x3 array)
        double[][] abc = Icosahedron.abc[face];

        // dist_to_V = argsort(abc.dot(X))
        double[] distances = new double[3];
        for (int i = 0; i < 3; i++)
        {
            distances[i] = abc[i][0] * X.X + abc[i][1] * X.Y + abc[i][2] * X.Z;
        }
        int[] sortedIndices = ArgSort(distances);

        // Get vertices in sorted order (_,v1,v0) means skip first, take second and third
        double[] v0 = abc[sortedIndices[2]];
        double[] v1 = new double[3];
        for (int i = 0; i < 3; i++)
        {
            v1[i] = (abc[sortedIndices[2]][i] + abc[sortedIndices[1]][i]) * Icosahedron.VtoC / (2 * Icosahedron.phi);
        }
        double[] v2 = Icosahedron.k[face];

        // Check if X == v0
        Vector3 X_P;
        double tolerance = 1e-6;
        bool isV0 = Abs(X.X - v0[0]) < tolerance && Abs(X.Y - v0[1]) < tolerance && Abs(X.Z - v0[2]) < tolerance;

        if (isV0)
        {
            X_P = X;
        }
        else
        {
            double[] K;
            double[][] subface;

            int orientation = ((sortedIndices[1] - sortedIndices[2]) % 3 + 3) % 3;
            if (orientation == 1)
            {
                K = FindEABarycenter(X, v0, v1, v2);
                subface = BuildSubface(
                    Icosahedron.VtoC, v0,
                    Icosahedron.phi, v1,
                    Icosahedron.FtoC, v2
                );
            }
            else
            {
                K = FindEABarycenter(X, v0, v2, v1);
                subface = BuildSubface(
                    Icosahedron.VtoC, v0,
                    Icosahedron.FtoC, v2,
                    Icosahedron.phi, v1
                );
            }

            // X_P = subface.T.dot(K)
            double[] result = new double[3];
            for (int i = 0; i < 3; i++)
            {
                result[i] = subface[0][i] * K[0] + subface[1][i] * K[1] + subface[2][i] * K[2];
            }
            X_P = new Vector3((float)result[0], (float)result[1], (float)result[2]);
        }

        // Project to 2D using eB
        double[][] eB = Icosahedron.eB[face];
        double p0 = eB[0][0] * X_P.X + eB[0][1] * X_P.Y + eB[0][2] * X_P.Z;
        double p1 = eB[1][0] * X_P.X + eB[1][1] * X_P.Y + eB[1][2] * X_P.Z;

        return new Vector2((float)p0, (float)p1);
    }

    public override Vector3 InvProject(Vector2 P, int face)
    {
        // Huge step back: robust numeric inverse using forward Snyder + Newton updates on the sphere.
        // Seed with gnomonic inverse on the same face.
        double[] k = Icosahedron.k[face];
        double[][] eB = Icosahedron.eB[face];

        double sx = eB[0][0] * P.X + eB[1][0] * P.Y + Icosahedron.FtoC * k[0];
        double sy = eB[0][1] * P.X + eB[1][1] * P.Y + Icosahedron.FtoC * k[1];
        double sz = eB[0][2] * P.X + eB[1][2] * P.Y + Icosahedron.FtoC * k[2];

        Vector3 x = new Vector3((float)sx, (float)sy, (float)sz).Normalized();
        if (!IsFinite(x.X) || !IsFinite(x.Y) || !IsFinite(x.Z))
            x = new Vector3((float)k[0], (float)k[1], (float)k[2]).Normalized();

        const int maxIter = 20;
        const double eps = 1e-5;
        const double tol = 1e-7;

        for (int iter = 0; iter < maxIter; iter++)
        {
            Vector2 p = Project(x, face);
            Vector2 err = p - P;
            double errNorm = Sqrt(err.X * err.X + err.Y * err.Y);
            if (!IsFinite(errNorm))
                break;
            if (errNorm < tol)
                return x.Normalized();

            Vector3 u = x.Cross(Vector3.Up);
            if (u.LengthSquared() < 1e-10f)
                u = x.Cross(Vector3.Right);
            u = u.Normalized();
            Vector3 v = x.Cross(u).Normalized();

            Vector3 xu = (x + (float)eps * u).Normalized();
            Vector3 xv = (x + (float)eps * v).Normalized();

            Vector2 pu = Project(xu, face);
            Vector2 pv = Project(xv, face);

            double j00 = (pu.X - p.X) / eps;
            double j10 = (pu.Y - p.Y) / eps;
            double j01 = (pv.X - p.X) / eps;
            double j11 = (pv.Y - p.Y) / eps;

            double det = j00 * j11 - j01 * j10;
            if (Abs(det) < 1e-14 || !IsFinite(det))
                break;

            double rhs0 = -err.X;
            double rhs1 = -err.Y;

            double du = (rhs0 * j11 - rhs1 * j01) / det;
            double dv = (j00 * rhs1 - j10 * rhs0) / det;
            if (!IsFinite(du) || !IsFinite(dv))
                break;

            // Damped step to improve stability near edges.
            const double damping = 0.7;
            x = (x + (float)(damping * du) * u + (float)(damping * dv) * v).Normalized();
            if (!IsFinite(x.X) || !IsFinite(x.Y) || !IsFinite(x.Z))
                break;
        }

        return x.Normalized();
    }

    private double[] FindEABarycenter(Vector3 X, double[] v0, double[] v1, double[] v2)
    {
        // d = V * X - det([X, v1, v2]) * v0
        double[] X_arr = new double[] { X.X, X.Y, X.Z };
        double det_Xv1v2 = MatrixDet3x3(X_arr, v1, v2);

        double[] d = new double[3];
        for (int i = 0; i < 3; i++)
        {
            d[i] = V * X_arr[i] - det_Xv1v2 * v0[i];
        }

        // Normalize d
        double d_norm = Sqrt(d[0] * d[0] + d[1] * d[1] + d[2] * d[2]);
        if (d_norm < 1e-12)
            return new double[] { 1, 0, 0 };

        for (int i = 0; i < 3; i++)
        {
            d[i] /= d_norm;
        }

        // h = sqrt((1 - v0.dot(X)) / (1 - v0.dot(d)))
        double v0_dot_X = DotProduct(v0, X_arr);
        double v0_dot_d = DotProduct(v0, d);
        double denom = 1 - v0_dot_d;
        if (Abs(denom) < 1e-12)
            return new double[] { 1, 0, 0 };

        double h = Sqrt(Max(0.0, (1 - v0_dot_X) / denom));

        // A calculation
        double det_v0v1d = MatrixDet3x3(v0, v1, d);
        double v0_dot_v1 = DotProduct(v0, v1);
        double v1_dot_d = DotProduct(v1, d);
        double A = 2 * Atan2(
            det_v0v1d,
            1 + v0_dot_v1 + v1_dot_d + v0_dot_d
        );

        double A2 = PI / 30;

        double[] K = new double[3];
        K[2] = h * A / A2;
        K[1] = h - K[2];
        K[0] = 1 - h;

        if (!IsFinite3(K))
            return new double[] { 1, 0, 0 };

        return K;
    }

    private double[] Slerp(double[] u, double[] v, double t)
    {
        double ang_dist = Acos(Clamp(DotProduct(u, v), -1.0, 1.0));
        double sin_ang = Sin(ang_dist);

        if (Abs(sin_ang) < 1e-12)
        {
            double[] linear = new double[3];
            for (int i = 0; i < 3; i++)
            {
                linear[i] = (1 - t) * u[i] + t * v[i];
            }
            double norm = Sqrt(linear[0] * linear[0] + linear[1] * linear[1] + linear[2] * linear[2]);
            if (norm > 0)
            {
                for (int i = 0; i < 3; i++)
                    linear[i] /= norm;
            }
            return linear;
        }

        double[] result = new double[3];
        for (int i = 0; i < 3; i++)
        {
            result[i] = Sin((1 - t) * ang_dist) * u[i] / sin_ang + Sin(t * ang_dist) * v[i] / sin_ang;
        }

        return result;
    }

    private bool IsFinite(double value)
    {
        return !(double.IsNaN(value) || double.IsInfinity(value));
    }

    private bool IsFinite3(double[] v)
    {
        return IsFinite(v[0]) && IsFinite(v[1]) && IsFinite(v[2]);
    }

    private Vector3 Normalize3(double[] v)
    {
        double norm = Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
        if (norm < 1e-12 || !IsFinite(norm))
            return Vector3.Up;
        return new Vector3((float)(v[0] / norm), (float)(v[1] / norm), (float)(v[2] / norm));
    }

    private double MatrixDet3x3(double[] row1, double[] row2, double[] row3)
    {
        return row1[0] * (row2[1] * row3[2] - row2[2] * row3[1])
             - row1[1] * (row2[0] * row3[2] - row2[2] * row3[0])
             + row1[2] * (row2[0] * row3[1] - row2[1] * row3[0]);
    }

    private int[] ArgSort(double[] arr)
    {
        int[] indices = new int[arr.Length];
        for (int i = 0; i < arr.Length; i++) indices[i] = i;

        System.Array.Sort(indices, (i, j) => arr[i].CompareTo(arr[j]));

        return indices;
    }

    private double[] Solve3x3(double[][] A, double[] b)
    {
        // Create augmented matrix
        double[][] matrix = new double[3][];
        for (int i = 0; i < 3; i++)
        {
            matrix[i] = new double[4];
            for (int j = 0; j < 3; j++)
            {
                matrix[i][j] = A[i][j];
            }
            matrix[i][3] = b[i];
        }

        // Gaussian elimination with partial pivoting
        for (int i = 0; i < 3; i++)
        {
            // Find pivot
            int pivot = i;
            for (int j = i + 1; j < 3; j++)
            {
                if (Abs(matrix[j][i]) > Abs(matrix[pivot][i]))
                    pivot = j;
            }

            // Swap rows
            double[] temp = matrix[i];
            matrix[i] = matrix[pivot];
            matrix[pivot] = temp;

            // Eliminate
            for (int j = i + 1; j < 3; j++)
            {
                double factor = matrix[j][i] / matrix[i][i];
                for (int k = i; k < 4; k++)
                {
                    matrix[j][k] -= factor * matrix[i][k];
                }
            }
        }

        // Back substitution
        double[] x = new double[3];
        for (int i = 2; i >= 0; i--)
        {
            x[i] = matrix[i][3];
            for (int j = i + 1; j < 3; j++)
            {
                x[i] -= matrix[i][j] * x[j];
            }
            x[i] /= matrix[i][i];
        }

        return x;
    }

    private double DotProduct(double[] u, double[] v)
    {
        return u[0] * v[0] + u[1] * v[1] + u[2] * v[2];
    }

    private double[][] BuildSubface(double s0, double[] v0, double s1, double[] v1, double s2, double[] v2)
    {
        return new double[][]
        {
            new double[] { s0 * v0[0], s0 * v0[1], s0 * v0[2] },
            new double[] { s1 * v1[0], s1 * v1[1], s1 * v1[2] },
            new double[] { s2 * v2[0], s2 * v2[1], s2 * v2[2] }
        };
    }

    private double[][] Transpose3x3(double[][] m)
    {
        return new double[][]
        {
            new double[] { m[0][0], m[1][0], m[2][0] },
            new double[] { m[0][1], m[1][1], m[2][1] },
            new double[] { m[0][2], m[1][2], m[2][2] }
        };
    }

    private Vector3 RefineInverse(Vector2 targetP, int face, Vector3 initial)
    {
        Vector3 x = initial.Normalized();
        if (!IsFinite(x.X) || !IsFinite(x.Y) || !IsFinite(x.Z))
            x = Vector3.Up;

        const int maxIter = 8;
        const double eps = 1e-5;
        const double tol = 1e-6;

        for (int iter = 0; iter < maxIter; iter++)
        {
            Vector2 p = Project(x, face);
            Vector2 err = p - targetP;
            double errNorm = Sqrt(err.X * err.X + err.Y * err.Y);
            if (errNorm < tol)
                break;

            Vector3 u = x.Cross(Vector3.Up);
            if (u.LengthSquared() < 1e-10f)
                u = x.Cross(Vector3.Right);
            u = u.Normalized();
            Vector3 v = x.Cross(u).Normalized();

            Vector3 xu = (x + (float)eps * u).Normalized();
            Vector3 xv = (x + (float)eps * v).Normalized();

            Vector2 pu = Project(xu, face);
            Vector2 pv = Project(xv, face);

            double j00 = (pu.X - p.X) / eps;
            double j10 = (pu.Y - p.Y) / eps;
            double j01 = (pv.X - p.X) / eps;
            double j11 = (pv.Y - p.Y) / eps;

            double det = j00 * j11 - j01 * j10;
            if (Abs(det) < 1e-14 || !IsFinite(det))
                break;

            double rhs0 = -err.X;
            double rhs1 = -err.Y;

            double du = (rhs0 * j11 - rhs1 * j01) / det;
            double dv = (j00 * rhs1 - j10 * rhs0) / det;

            if (!IsFinite(du) || !IsFinite(dv))
                break;

            x = (x + (float)du * u + (float)dv * v).Normalized();
            if (!IsFinite(x.X) || !IsFinite(x.Y) || !IsFinite(x.Z))
                return initial.Normalized();
        }

        return x;
    }
}
