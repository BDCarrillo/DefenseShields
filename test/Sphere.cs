﻿using System;
using static System.Math;

namespace GeometRi
{
    /// <summary>
    /// Sphere object defined by center point and radius.
    /// </summary>
    public class Sphere : IFiniteObject
    {

        private Point3d _point;
        private double _r;

        /// <summary>
        /// Initializes sphere using center point and radius.
        /// </summary>
        public Sphere(Point3d P, double R)
        {
            _point = P.Copy();
            _r = R;
        }

        /// <summary>
        /// Creates copy of the object
        /// </summary>
        public Sphere Copy()
        {
            return new Sphere(_point, _r);
        }

        #region "Properties"
        /// <summary>
        /// Center of the sphere
        /// </summary>
        public Point3d Center
        {
            get { return _point.Copy(); }
            set { _point = value.Copy(); }
        }

        /// <summary>
        /// X component of the spheres' center
        /// </summary>
        private double X
        {
            get { return _point.X; }
            set { _point.X = value; }
        }

        /// <summary>
        /// Y component of the spheres' center
        /// </summary>
        private double Y
        {
            get { return _point.Y; }
            set { _point.Y = value; }
        }

        /// <summary>
        /// Z component of the spheres' center
        /// </summary>
        private double Z
        {
            get { return _point.Z; }
            set { _point.Z = value; }
        }

        /// <summary>
        /// Radius of the sphere
        /// </summary>
        public double R
        {
            get { return _r; }
            set { _r = value; }
        }

        public double Area
        {
            get { return 4.0 * PI * Math.Pow(_r, 2); }
        }

        public double Volume
        {
            get { return 4.0 / 3.0 * PI * Math.Pow(_r, 3); }
        }
        #endregion

        #region "DistanceTo"
        public double DistanceTo(Point3d p)
        {
            double d = p.DistanceTo(this.Center);
            if (d > this.R)
            {
                return d - this.R;
            }
            else
            {
                return 0;
            }
        }

        public double DistanceTo(Line3d l)
        {
            double d = l.DistanceTo(this.Center);
            if (d > this.R)
            {
                return d - this.R;
            }
            else
            {
                return 0;
            }
        }

        public double DistanceTo(Ray3d r)
        {
            if (this.Center.ProjectionTo(r.ToLine).BelongsTo(r))
            {
                return this.DistanceTo(r.ToLine);
            }
            else
            {
                return this.DistanceTo(r.Point);
            }
        }

        public double DistanceTo(Segment3d s)
        {
            if (this.Center.ProjectionTo(s.ToLine).BelongsTo(s))
            {
                return this.DistanceTo(s.ToLine);
            }
            else
            {
                return Min(this.DistanceTo(s.P1), this.DistanceTo(s.P2));
            }
        }

        public double DistanceTo(Plane3d s)
        {
            double d = this.Center.DistanceTo(s);
            if (d > this.R)
            {
                return d - this.R;
            }
            else
            {
                return 0;
            }
        }
        #endregion

        #region "BoundingBox"
        /// <summary>
        /// Return minimum bounding box.
        /// </summary>
        public Box3d MinimumBoundingBox
        {
            get { return new Box3d(_point, 2.0 * _r, 2.0 * _r, 2.0 * _r); }
        }

        /// <summary>
        /// Return Axis Aligned Bounding Box (AABB) in given coordinate system.
        /// </summary>
        public Box3d BoundingBox(Coord3d coord = null)
        {
            coord = (coord == null) ? Coord3d.GlobalCS : coord;
            return new Box3d(_point, 2.0 * _r, 2.0 * _r, 2.0 * _r, new Rotation(coord));
        }

        /// <summary>
        /// Return bounding sphere.
        /// </summary>
        public Sphere BoundingSphere
        {
            get { return this; }

        }
        #endregion

        #region "Intersections"
        /// <summary>
        /// Get intersection of line with sphere.
        /// Returns 'null' (no intersection) or object of type 'Point3d' or 'Segment3d'.
        /// </summary>
        public object IntersectionWith(Line3d l)
        {

            double d = l.Direction.Normalized * (l.Point.ToVector - this.Center.ToVector);
            double det = Math.Pow(d, 2) - Math.Pow(((l.Point.ToVector - this.Center.ToVector).Norm), 2) + Math.Pow(_r, 2);

            if (det < -GeometRi3D.Tolerance)
            {
                return null;
            }
            else if (det < GeometRi3D.Tolerance)
            {
                return l.Point - d * l.Direction.Normalized.ToPoint;
            }
            else
            {
                Point3d p1 = l.Point + (-d + Sqrt(det)) * l.Direction.Normalized.ToPoint;
                Point3d p2 = l.Point + (-d - Sqrt(det)) * l.Direction.Normalized.ToPoint;
                return new Segment3d(p1, p2);
            }

        }

        /// <summary>
        /// Get intersection of plane with sphere.
        /// Returns 'null' (no intersection) or object of type 'Point3d' or 'Circle3d'.
        /// </summary>
        public object IntersectionWith(Plane3d s)
        {

            s.SetCoord(this.Center.Coord);
            double d1 = s.A * this.X + s.B * this.Y + s.C * this.Z + s.D;
            double d2 = Math.Pow(s.A, 2) + Math.Pow(s.B, 2) + Math.Pow(s.C, 2);
            double d = Abs(d1) / Sqrt(d2);

            if (d > this.R + GeometRi3D.Tolerance)
            {
                return null;
            }
            else
            {
                double Xc = this.X - s.A * d1 / d2;
                double Yc = this.Y - s.B * d1 / d2;
                double Zc = this.Z - s.C * d1 / d2;

                if (Abs(d - this.R) < GeometRi3D.Tolerance)
                {
                    return new Point3d(Xc, Yc, Zc, this.Center.Coord);
                }
                else
                {
                    double R = Sqrt(Math.Pow(this.R, 2) - Math.Pow(d, 2));
                    return new Circle3d(new Point3d(Xc, Yc, Zc, this.Center.Coord), R, s.Normal);
                }
            }
        }

        /// <summary>
        /// Get intersection of two spheres.
        /// Returns 'null' (no intersection) or object of type 'Point3d' or 'Circle3d'.
        /// </summary>
        public object IntersectionWith(Sphere s)
        {

            Point3d p = s.Center.ConvertTo(this.Center.Coord);
            double Dist = Sqrt(Math.Pow((this.X - p.X), 2) + Math.Pow((this.Y - p.Y), 2) + Math.Pow((this.Z - p.Z), 2));

            // Separated spheres
            if (Dist > this.R + s.R + GeometRi3D.Tolerance)
                return null;

            // One sphere inside the other
            if (Dist < Abs(this.R - s.R) - GeometRi3D.Tolerance)
                return null;

            // Intersection plane
            double A = 2 * (p.X - this.X);
            double B = 2 * (p.Y - this.Y);
            double C = 2 * (p.Z - this.Z);
            double D = Math.Pow(this.X, 2) - Math.Pow(p.X, 2) + Math.Pow(this.Y, 2) - Math.Pow(p.Y, 2) + Math.Pow(this.Z, 2) - Math.Pow(p.Z, 2) - Math.Pow(this.R, 2) + Math.Pow(s.R, 2);

            // Intersection center
            double t = (this.X * A + this.Y * B + this.Z * C + D) / (A * (this.X - p.X) + B * (this.Y - p.Y) + C * (this.Z - p.Z));
            double x = this.X + t * (p.X - this.X);
            double y = this.Y + t * (p.Y - this.Y);
            double z = this.Z + t * (p.Z - this.Z);

            // Outer tangency
            if (Abs(this.R + s.R - D) < GeometRi3D.Tolerance)
                return new Point3d(x, y, z, this.Center.Coord);

            // Inner tangency
            if (Abs(Abs(this.R - s.R) - D) < GeometRi3D.Tolerance)
                return new Point3d(x, y, z, this.Center.Coord);

            // Intersection
            double alpha = Acos((Math.Pow(this.R, 2) + Math.Pow(Dist, 2) - Math.Pow(s.R, 2)) / (2 * this.R * Dist));
            double R = this.R * Sin(alpha);
            Vector3d v = new Vector3d(this.Center, s.Center);

            return new Circle3d(new Point3d(x, y, z, this.Center.Coord), R, v);

        }
        #endregion

        /// <summary>
        /// Orthogonal projection of the sphere to the plane
        /// </summary>
        public Circle3d ProjectionTo(Plane3d s)
        {
            Point3d p = this.Center.ProjectionTo(s);
            return new Circle3d(p, this.R, s.Normal);
        }

        /// <summary>
        /// Orthogonal projection of the sphere to the line
        /// </summary>
        public Segment3d ProjectionTo(Line3d l)
        {
            Point3d p = this.Center.ProjectionTo(l);
            return new Segment3d(p.Translate(this.R * l.Direction.Normalized), p.Translate(-this.R * l.Direction.Normalized));
        }

        #region "TranslateRotateReflect"
        /// <summary>
        /// Translate sphere by a vector
        /// </summary>
        public Sphere Translate(Vector3d v)
        {
            return new Sphere(this.Center.Translate(v), this.R);
        }

        /// <summary>
        /// Rotate sphere by a given rotation matrix
        /// </summary>
        [System.Obsolete("use Rotation object and specify rotation center: this.Rotate(Rotation r, Point3d p)")]
        public Sphere Rotate(Matrix3d m)
        {
            return new Sphere(this.Center.Rotate(m), this.R);
        }

        /// <summary>
        /// Rotate sphere by a given rotation matrix around point 'p' as a rotation center
        /// </summary>
        [System.Obsolete("use Rotation object: this.Rotate(Rotation r, Point3d p)")]
        public Sphere Rotate(Matrix3d m, Point3d p)
        {
            return new Sphere(this.Center.Rotate(m, p), this.R);
        }

        /// <summary>
        /// Rotate sphere around point 'p' as a rotation center
        /// </summary>
        public Sphere Rotate(Rotation r, Point3d p)
        {
            return new Sphere(this.Center.Rotate(r, p), this.R);
        }

        /// <summary>
        /// Reflect sphere in given point
        /// </summary>
        public Sphere ReflectIn(Point3d p)
        {
            return new Sphere(this.Center.ReflectIn(p), this.R);
        }

        /// <summary>
        /// Reflect sphere in given line
        /// </summary>
        public Sphere ReflectIn(Line3d l)
        {
            return new Sphere(this.Center.ReflectIn(l), this.R);
        }

        /// <summary>
        /// Reflect sphere in given plane
        /// </summary>
        public Sphere ReflectIn(Plane3d s)
        {
            return new Sphere(this.Center.ReflectIn(s), this.R);
        }
        #endregion

        /// <summary>
        /// Determines whether two objects are equal.
        /// </summary>
        public override bool Equals(object obj)
        {
            if (obj == null || (!object.ReferenceEquals(this.GetType(), obj.GetType())))
            {
                return false;
            }
            Sphere s = (Sphere)obj;
            if (GeometRi3D.UseAbsoluteTolerance)
            {
                return s.Center == this.Center && Abs(s.R - this.R) <= GeometRi3D.Tolerance;
            }
            else
            {
                return this.Center.DistanceTo(s.Center) <= GeometRi3D.Tolerance * this.R &&
                       Abs(s.R - this.R) <= GeometRi3D.Tolerance * this.R;
            }

        }

        /// <summary>
        /// Returns the hashcode for the object.
        /// </summary>
        public override int GetHashCode()
        {
            return GeometRi3D.HashFunction(_point.GetHashCode(), _r.GetHashCode());
        }

        /// <summary>
        /// String representation of an object in global coordinate system.
        /// </summary>
        public override String ToString()
        {
            return ToString(Coord3d.GlobalCS);
        }

        /// <summary>
        /// String representation of an object in reference coordinate system.
        /// </summary>
        public String ToString(Coord3d coord)
        {
            string nl = System.Environment.NewLine;

            if (coord == null) { coord = Coord3d.GlobalCS; }
            Point3d p = _point.ConvertTo(coord);

            string str = string.Format("Sphere: ") + nl;
            str += string.Format("  Center -> ({0,10:g5}, {1,10:g5}, {2,10:g5})", p.X, p.Y, p.Z) + nl;
            str += string.Format("  Radius -> {0,10:g5}", _r);
            return str;
        }

        // Operators overloads
        //-----------------------------------------------------------------

        public static bool operator ==(Sphere s1, Sphere s2)
        {
            return s1.Equals(s2);
        }
        public static bool operator !=(Sphere s1, Sphere s2)
        {
            return !s1.Equals(s2);
        }

    }
}

