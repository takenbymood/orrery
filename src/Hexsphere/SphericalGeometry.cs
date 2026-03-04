using static System.Math;
using Godot;

class SphericalGeometry
{

    // Golden Number
    public static double phi = (1 + System.Math.Sqrt(5)) / 2.0;


    public static (double,double,double) LatLonToXYZ(double lat, double lon)
    {
        lat *= System.Math.PI / 180.0;
        lon *= System.Math.PI / 180.0;

        
        double X = System.Math.Cos(lat) * System.Math.Cos(lon);
        double Y = System.Math.Cos(lat) * System.Math.Sin(lon);
        double Z = System.Math.Sin(lat);
        (double x,double y,double z) XYZ = (X,Y,Z);
        return XYZ;
    }

    public static (double,double,double) LatLonToXYZ((double,double) latlon)
    {
        double lat = latlon.Item1;
        double lon = latlon.Item2;
        lat *= System.Math.PI / 180.0;
        lon *= System.Math.PI / 180.0;

        double X = System.Math.Cos(lat) * System.Math.Cos(lon);
        double Y = System.Math.Cos(lat) * System.Math.Sin(lon);
        double Z = System.Math.Sin(lat);
        (double x,double y,double z) XYZ = (X,Y,Z);
        return XYZ;
    }


    public static (double,double) XYToLatlon((double,double) X){
        (double,double,double) PX = (X.Item1, X.Item2, 0);

        double lat = System.Math.Atan2(0.0, System.Math.Sqrt(PX.Item1 * PX.Item1 + PX.Item2 * PX.Item2));
        double lon = 0.0;

        if (lat != System.Math.PI / 2 && lat != -System.Math.PI / 2){
            lon = System.Math.Atan2(PX.Item2, PX.Item1);
        }
        lat *= 180 / System.Math.PI;
        lon *= 180 / System.Math.PI;

        return (lat, lon);
    }

    public static (double,double) XYToLatlon(double X, double Y){
        (double,double,double) PX = (X, Y, 0);

        double lat = System.Math.Atan2(0.0, System.Math.Sqrt(PX.Item1 * PX.Item1 + PX.Item2 * PX.Item2));
        double lon = 0.0;

        if (lat != System.Math.PI / 2 && lat != -System.Math.PI / 2){
            lon = System.Math.Atan2(PX.Item2, PX.Item1);
        }
        lat *= 180 / System.Math.PI;
        lon *= 180 / System.Math.PI;

        return (lat, lon);
    }

    public static double ComputeLatLonNormalisedDist((double, double) LL1, (double, double) LL2)
    {
        (double,double,double) X1 = LatLonToXYZ(LL1);
        (double,double,double) X2 = LatLonToXYZ(LL2);
        return System.Math.Acos(X1.Item1 * X2.Item1 + X1.Item2 * X2.Item2 + X1.Item3 * X2.Item3);
    }

    public static double ComputeLatLonDist((double, double) LL1, (double, double) LL2, double R = 6371.0)
    {
        double d = ComputeLatLonNormalisedDist(LL1, LL2);
        return R * d;
    }

    // Vector3 overloads
    public static Vector3 LatLonToVector3(double lat, double lon)
    {
        var xyz = LatLonToXYZ(lat, lon);
        return new Vector3((float)xyz.Item1, (float)xyz.Item2, (float)xyz.Item3);
    }

    public static Vector3 LatLonToVector3((double, double) latlon)
    {
        var xyz = LatLonToXYZ(latlon);
        return new Vector3((float)xyz.Item1, (float)xyz.Item2, (float)xyz.Item3);
    }

    public static (double, double) Vector3ToLatLon(Vector3 v)
    {
        Vector3 n = v.Normalized();
        double lat = System.Math.Asin(System.Math.Clamp(n.Z, -1.0f, 1.0f)) * 180.0 / System.Math.PI;
        double lon = System.Math.Atan2(n.Y, n.X) * 180.0 / System.Math.PI;
        return (lat, lon);
    }

    public static double ComputeVector3NormalisedDist(Vector3 v1, Vector3 v2)
    {
        double dotProduct = v1.X * v2.X + v1.Y * v2.Y + v1.Z * v2.Z;
        return System.Math.Acos(Clamp(dotProduct, -1.0, 1.0));
    }

    public static double ComputeVector3Dist(Vector3 v1, Vector3 v2, double R = 6371.0)
    {
        double d = ComputeVector3NormalisedDist(v1, v2);
        return R * d;
    }

}