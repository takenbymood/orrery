
using Godot;
using static System.Math;
class GnomonicProjection : Projection
{

    public override Vector2 Project(Vector3 X, int face)
    {
        Vector3 x = X;
        double[] k = Icosahedron.k[face];
        double[][] eB = Icosahedron.eB[face];

        // P = eB.dot(X) - matrix-vector multiplication (2x3 . 3D = 2D)
        double p0 = eB[0][0] * x.X + eB[0][1] * x.Y + eB[0][2] * x.Z;
        double p1 = eB[1][0] * x.X + eB[1][1] * x.Y + eB[1][2] * x.Z;

        // k.dot(X)
        double k_dot_x = k[0] * x.X + k[1] * x.Y + k[2] * x.Z;

        // P = FtoC * P / k.dot(X)
        p0 = Icosahedron.FtoC * p0 / k_dot_x;
        p1 = Icosahedron.FtoC * p1 / k_dot_x;

        return new Vector2((float)p0, (float)p1);
    }

    public override Vector3 InvProject(Vector2 P, int face)
    {
        Vector2 p = P;
        double[] k = Icosahedron.k[face];
        double[][] eB = Icosahedron.eB[face];

        // X = eB.T.dot(P) + FtoC * k
        // eB.T is 3x2, P is 2D, so result is 3D
        double[] x = new double[3];
        for (int i = 0; i < 3; i++)
        {
            x[i] = eB[0][i] * p.X + eB[1][i] * p.Y + Icosahedron.FtoC * k[i];
        }

        // Normalize X
        double norm = System.Math.Sqrt(x[0] * x[0] + x[1] * x[1] + x[2] * x[2]);
        for (int i = 0; i < 3; i++)
        {
            x[i] /= norm;
        }

        return new Vector3((float)x[0], (float)x[1], (float)x[2]);
    }
}