using System;

namespace RevitGeoSuite.Core.Plateau.Tiles3D;

public readonly struct Matrix4x4d
{
    public readonly double M11, M12, M13, M14;
    public readonly double M21, M22, M23, M24;
    public readonly double M31, M32, M33, M34;
    public readonly double M41, M42, M43, M44;

    public Matrix4x4d(
        double m11, double m12, double m13, double m14,
        double m21, double m22, double m23, double m24,
        double m31, double m32, double m33, double m34,
        double m41, double m42, double m43, double m44)
    {
        M11 = m11; M12 = m12; M13 = m13; M14 = m14;
        M21 = m21; M22 = m22; M23 = m23; M24 = m24;
        M31 = m31; M32 = m32; M33 = m33; M34 = m34;
        M41 = m41; M42 = m42; M43 = m43; M44 = m44;
    }

    public static Matrix4x4d Identity { get; } = new Matrix4x4d(
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 1, 0,
        0, 0, 0, 1);

    public static Matrix4x4d FromColumnMajor(double[] values)
    {
        if (values is null || values.Length != 16)
        {
            throw new ArgumentException("Column-major matrix must have 16 entries.", nameof(values));
        }
        return new Matrix4x4d(
            values[0], values[4], values[8],  values[12],
            values[1], values[5], values[9],  values[13],
            values[2], values[6], values[10], values[14],
            values[3], values[7], values[11], values[15]);
    }

    public static Matrix4x4d Multiply(Matrix4x4d a, Matrix4x4d b)
    {
        return new Matrix4x4d(
            a.M11 * b.M11 + a.M12 * b.M21 + a.M13 * b.M31 + a.M14 * b.M41,
            a.M11 * b.M12 + a.M12 * b.M22 + a.M13 * b.M32 + a.M14 * b.M42,
            a.M11 * b.M13 + a.M12 * b.M23 + a.M13 * b.M33 + a.M14 * b.M43,
            a.M11 * b.M14 + a.M12 * b.M24 + a.M13 * b.M34 + a.M14 * b.M44,

            a.M21 * b.M11 + a.M22 * b.M21 + a.M23 * b.M31 + a.M24 * b.M41,
            a.M21 * b.M12 + a.M22 * b.M22 + a.M23 * b.M32 + a.M24 * b.M42,
            a.M21 * b.M13 + a.M22 * b.M23 + a.M23 * b.M33 + a.M24 * b.M43,
            a.M21 * b.M14 + a.M22 * b.M24 + a.M23 * b.M34 + a.M24 * b.M44,

            a.M31 * b.M11 + a.M32 * b.M21 + a.M33 * b.M31 + a.M34 * b.M41,
            a.M31 * b.M12 + a.M32 * b.M22 + a.M33 * b.M32 + a.M34 * b.M42,
            a.M31 * b.M13 + a.M32 * b.M23 + a.M33 * b.M33 + a.M34 * b.M43,
            a.M31 * b.M14 + a.M32 * b.M24 + a.M33 * b.M34 + a.M34 * b.M44,

            a.M41 * b.M11 + a.M42 * b.M21 + a.M43 * b.M31 + a.M44 * b.M41,
            a.M41 * b.M12 + a.M42 * b.M22 + a.M43 * b.M32 + a.M44 * b.M42,
            a.M41 * b.M13 + a.M42 * b.M23 + a.M43 * b.M33 + a.M44 * b.M43,
            a.M41 * b.M14 + a.M42 * b.M24 + a.M43 * b.M34 + a.M44 * b.M44);
    }

    public Vector3d TransformPoint(Vector3d v)
    {
        double x = M11 * v.X + M12 * v.Y + M13 * v.Z + M14;
        double y = M21 * v.X + M22 * v.Y + M23 * v.Z + M24;
        double z = M31 * v.X + M32 * v.Y + M33 * v.Z + M34;
        return new Vector3d(x, y, z);
    }
}

public readonly struct Vector3d
{
    public readonly double X;
    public readonly double Y;
    public readonly double Z;

    public Vector3d(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static Vector3d Zero => new Vector3d(0, 0, 0);

    public static Vector3d operator +(Vector3d a, Vector3d b) => new Vector3d(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Vector3d operator -(Vector3d a, Vector3d b) => new Vector3d(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static Vector3d operator *(Vector3d a, double s) => new Vector3d(a.X * s, a.Y * s, a.Z * s);

    public double LengthSquared => X * X + Y * Y + Z * Z;

    public double Length => Math.Sqrt(LengthSquared);
}
