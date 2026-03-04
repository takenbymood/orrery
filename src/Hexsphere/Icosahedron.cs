using System.Numerics;
using static System.Math;

static class Icosahedron
{

    // Golden Number
    public static double phi = (1 + System.Math.Sqrt(5)) / 2.0;

    // Face-to-center distance
    public static double FtoC = System.Math.Sqrt(phi * phi - 1.0 / 3.0);

    // Vertex-to-center distance
    public static double VtoC = System.Math.Sqrt(1 + phi * phi);

    // Normal vector of all face triangles
    public static double[][] k = InitializeNormals();

    private static double[][] InitializeNormals()
    {
        double scale = Sqrt(3) / (3 * (1 + phi));
        return new double[][]
        {
            new double[] { (2 * phi + 1) * scale, phi * scale, 0 },
            new double[] { (1 + phi) * scale, (1 + phi) * scale, (1 + phi) * scale },
            new double[] { phi * scale, 0, (2 * phi + 1) * scale },
            new double[] { (1 + phi) * scale, -(1 + phi) * scale, (1 + phi) * scale },
            new double[] { (2 * phi + 1) * scale, -phi * scale, 0 },
            new double[] { (1 + phi) * scale, (1 + phi) * scale, -(1 + phi) * scale },
            new double[] { 0, (2 * phi + 1) * scale, phi * scale },
            new double[] { -phi * scale, 0, (2 * phi + 1) * scale },
            new double[] { 0, -(2 * phi + 1) * scale, phi * scale },
            new double[] { (1 + phi) * scale, -(1 + phi) * scale, -(1 + phi) * scale },
            new double[] { -(2 * phi + 1) * scale, phi * scale, 0 },
            new double[] { -(1 + phi) * scale, (1 + phi) * scale, -(1 + phi) * scale },
            new double[] { -phi * scale, 0, -(2 * phi + 1) * scale },
            new double[] { -(1 + phi) * scale, -(1 + phi) * scale, -(1 + phi) * scale },
            new double[] { -(2 * phi + 1) * scale, -phi * scale, 0 },
            new double[] { -(1 + phi) * scale, (1 + phi) * scale, (1 + phi) * scale },
            new double[] { 0, (2 * phi + 1) * scale, -phi * scale },
            new double[] { phi * scale, 0, -(2 * phi + 1) * scale },
            new double[] { 0, -(2 * phi + 1) * scale, -phi * scale },
            new double[] { -(1 + phi) * scale, -(1 + phi) * scale, (1 + phi) * scale },
        };
    }

    // Directing summit of all face triangles
    public static double[][] a = new double[][]
    {
        new double[] { phi, 0, -1 },
        new double[] { 1, phi, 0 },
        new double[] { 0, 1, phi },
        new double[] { 0, -1, phi },
        new double[] { 1, -phi, 0 },
        new double[] { 1, phi, 0 },
        new double[] { 0, 1, phi },
        new double[] { 0, -1, phi },
        new double[] { 1, -phi, 0 },
        new double[] { phi, 0, -1 },
        new double[] { -phi, 0, 1 },
        new double[] { -1, phi, 0 },
        new double[] { 0, 1, -phi },
        new double[] { 0, -1, -phi },
        new double[] { -1, -phi, 0 },
        new double[] { -1, phi, 0 },
        new double[] { 0, 1, -phi },
        new double[] { 0, -1, -phi },
        new double[] { -1, -phi, 0 },
        new double[] { -phi, 0, 1 },
    };

    // Directing edge of all face triangles
    public static double[][] e1 = InitializeE1();

    private static double[][] InitializeE1()
    {
        double[][] temp = new double[][]
        {
            new double[] { 1 - phi, phi, 1 },
            new double[] { -1, 1 - phi, phi },
            new double[] { 0, -2, 0 },
            new double[] { 1, 1 - phi, -phi },
            new double[] { phi - 1, phi, -1 },
            new double[] { 1 - phi, phi, 1 },
            new double[] { -1, 1 - phi, phi },
            new double[] { 0, -2, 0 },
            new double[] { 1, 1 - phi, -phi },
            new double[] { phi - 1, phi, -1 },
            new double[] { phi - 1, phi, -1 },
            new double[] { 1, 1 - phi, -phi },
            new double[] { 0, -2, 0 },
            new double[] { -1, 1 - phi, phi },
            new double[] { 1 - phi, phi, 1 },
            new double[] { phi - 1, phi, -1 },
            new double[] { 1, 1 - phi, -phi },
            new double[] { 0, -2, 0 },
            new double[] { -1, 1 - phi, phi },
            new double[] { 1 - phi, phi, 1 },
        };

        // Multiply ranges [5:10] and [15:20] by -1
        for (int i = 5; i < 10; i++)
            for (int j = 0; j < 3; j++)
                temp[i][j] *= -1;
        for (int i = 15; i < 20; i++)
            for (int j = 0; j < 3; j++)
                temp[i][j] *= -1;

        return temp;
    }

    private static double[] CrossProduct(double[] u, double[] v)
    {
        return new double[]
        {
            u[1] * v[2] - u[2] * v[1],
            u[2] * v[0] - u[0] * v[2],
            u[0] * v[1] - u[1] * v[0]
        };
    }

    // Intermediate unnormalized b and e2/c for calculation
    private static double[][] InitializeUnnormalizedB()
    {
        double[][] result = new double[20][];
        for (int i = 0; i < 20; i++)
        {
            result[i] = new double[3];
            for (int j = 0; j < 3; j++)
                result[i][j] = a[i][j] + e1[i][j];
        }
        return result;
    }

    private static double[][] InitializeE2()
    {
        // Create e1/2 for cross product calculation
        double[][] e1_half = new double[20][];
        for (int i = 0; i < 20; i++)
        {
            e1_half[i] = new double[3];
            for (int j = 0; j < 3; j++)
                e1_half[i][j] = e1[i][j] / 2;
        }

        // Calculate e2 = cross(k, e1/2)
        double[][] result = new double[20][];
        for (int i = 0; i < 20; i++)
            result[i] = CrossProduct(k[i], e1_half[i]);

        return result;
    }

    // b = a + e1 (normalized)
    public static double[][] b = InitializeB();

    private static double[][] InitializeB()
    {
        double[][] unnormB = InitializeUnnormalizedB();
        double[][] result = new double[20][];
        for (int i = 0; i < 20; i++)
        {
            result[i] = new double[3];
            for (int j = 0; j < 3; j++)
                result[i][j] = unnormB[i][j] / VtoC;
        }
        return result;
    }

    // e2 = cross(k, e1/2)
    public static double[][] e2 = InitializeE2();

    // eB = [stack(e1/2, e2) for each face]
    public static double[][][] eB = InitializeEB();

    private static double[][][] InitializeEB()
    {
        double[][][] result = new double[20][][];
        for (int f = 0; f < 20; f++)
        {
            result[f] = new double[2][];
            result[f][0] = new double[3];
            result[f][1] = (double[])e2[f].Clone();
            for (int j = 0; j < 3; j++)
                result[f][0][j] = e1[f][j] / 2;
        }
        return result;
    }

    // c = 0.5 * (a + b_unnormalized) + sqrt(3) * e2, normalized by VtoC
    public static double[][] c = InitializeC();

    private static double[][] InitializeC()
    {
        double[][] unnormB = InitializeUnnormalizedB();
        double[][] result = new double[20][];
        double sqrt3 = Sqrt(3);
        for (int i = 0; i < 20; i++)
        {
            result[i] = new double[3];
            for (int j = 0; j < 3; j++)
            {
                result[i][j] = (0.5 * (a[i][j] + unnormB[i][j]) + sqrt3 * e2[i][j]) / VtoC;
            }
        }
        return result;
    }

    // abc = [stack(a, b, c) for each face] - initialized in static constructor
    public static double[][][] abc;

    private static void NormalizeA()
    {
        for (int i = 0; i < 20; i++)
            for (int j = 0; j < 3; j++)
                a[i][j] /= VtoC;
    }

    static Icosahedron()
    {
        NormalizeA();
        abc = InitializeABC();
    }

    private static double[][][] InitializeABC()
    {
        double[][][] result = new double[20][][];
        for (int f = 0; f < 20; f++)
        {
            result[f] = new double[3][];
            result[f][0] = (double[])a[f].Clone();
            result[f][1] = (double[])b[f].Clone();
            result[f][2] = (double[])c[f].Clone();
        }
        return result;
    }

    // Oriented edges of the face triangle in the face coordinate system
    public static double[][] Tr = new double[][]
    {
        new double[] { -1.0 / 2, -1.0 / 2, 1 },
        new double[] { Sqrt(3) / 2, -Sqrt(3) / 2, 0 },
    };

    // Bisectors of the face triangle in the face coordinate system
    public static double[][] Bis = new double[][]
    {
        new double[] { Sqrt(3) / 2, -Sqrt(3) / 2, 0 },
        new double[] { 1.0 / 2, 1.0 / 2, -1 },
    };

    // Neighboring faces
    public static int[][] neighboring_face = new int[][]
    {
        new int[] { 1, 4, 5 },
        new int[] { 2, 0, 6 },
        new int[] { 3, 1, 7 },
        new int[] { 4, 2, 8 },
        new int[] { 0, 3, 9 },
        new int[] { 17, 16, 0 },
        new int[] { 16, 15, 1 },
        new int[] { 15, 19, 2 },
        new int[] { 19, 18, 3 },
        new int[] { 18, 17, 4 },
        new int[] { 11, 14, 15 },
        new int[] { 12, 10, 16 },
        new int[] { 13, 11, 17 },
        new int[] { 14, 12, 18 },
        new int[] { 10, 13, 19 },
        new int[] { 7, 6, 10 },
        new int[] { 6, 5, 11 },
        new int[] { 5, 9, 12 },
        new int[] { 9, 8, 13 },
        new int[] { 8, 7, 14 },
    };

    public static double[] GetNormal(int face)
    {
        return k[face];
    }

}