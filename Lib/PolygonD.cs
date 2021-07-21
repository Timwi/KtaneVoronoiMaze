using System;
using System.Collections.Generic;
using System.Linq;
using RT.Util.ExtensionMethods;

namespace RT.Util.Geometry
{
    /// <summary>This class encapsulates double-precision polygons.</summary>
    public sealed class PolygonD
    {
        private List<PointD> _vertices;

        /// <summary>Returns a list of vertices of the polygon.</summary>
        public List<PointD> Vertices { get { return _vertices; } }

        /// <summary>
        ///     Enumerates the edges of this polygon in vertex order. The enumerable is "live" and reflects any changes to
        ///     <see cref="Vertices"/> immediately.</summary>
        public IEnumerable<EdgeD> Edges => Vertices.ConsecutivePairs(closed: true).Select(pair => new EdgeD(pair.Item1, pair.Item2));

        /// <summary>
        ///     Initializes a polygon from a given list of vertices.</summary>
        /// <param name="vertices">
        ///     Vertices (corner points) to initialize polygon from.</param>
        public PolygonD(IEnumerable<PointD> vertices)
        {
            _vertices = new List<PointD>(vertices);
        }

        /// <summary>
        ///     Initializes a polygon from a given array of vertices.</summary>
        /// <param name="vertices">
        ///     Vertices (corner points) to initialize polygon from.</param>
        public PolygonD(params PointD[] vertices)
        {
            _vertices = new List<PointD>(vertices);
        }

        /// <summary>
        ///     Determines whether the current <see cref="PolygonD"/> contains the specified point. If the point lies exactly
        ///     on one of the polygon edges, it is considered to be contained in the polygon.</summary>
        /// <param name="point">
        ///     Point to check.</param>
        /// <returns>
        ///     True if the specified point lies inside the current <see cref="PolygonD"/>.</returns>
        public bool ContainsPoint(PointD point)
        {
            foreach (var edge in ToEdges())
                if (edge.ContainsPoint(point))
                    return true;
            bool c = false;
            PointD p = _vertices[_vertices.Count - 1];
            foreach (PointD q in _vertices)
            {
                if ((((q.Y <= point.Y) && (point.Y < p.Y)) ||
                     ((p.Y <= point.Y) && (point.Y < q.Y))) &&
                    (point.X < (p.X - q.X) * (point.Y - q.Y) / (p.Y - q.Y) + q.X))
                    c = !c;
                p = q;
            }
            return c;
        }

        /// <summary>
        ///     Determines the area of the current <see cref="PolygonD"/>.</summary>
        /// <returns>
        ///     The area of the current <see cref="PolygonD"/> in square units.</returns>
        public double Area()
        {
            double area = 0;
            PointD p = _vertices[_vertices.Count - 1];
            foreach (PointD q in _vertices)
            {
                area += q.Y * p.X - q.X * p.Y;
                p = q;
            }
            return area / 2;
        }

        /// <summary>Calculates the centroid of this polygon.</summary>
        public PointD Centroid()
        {
            // from http://stackoverflow.com/a/2792459/33080
            PointD centroid = new PointD(0, 0);
            double signedArea = 0.0;
            double x0; // Current vertex X
            double y0; // Current vertex Y
            double x1; // Next vertex X
            double y1; // Next vertex Y
            double a;  // Partial signed area

            // For all vertices except last
            int i = 0;
            for (; i < _vertices.Count - 1; ++i)
            {
                x0 = _vertices[i].X;
                y0 = _vertices[i].Y;
                x1 = _vertices[i + 1].X;
                y1 = _vertices[i + 1].Y;
                a = x0 * y1 - x1 * y0;
                signedArea += a;
                centroid.X += (x0 + x1) * a;
                centroid.Y += (y0 + y1) * a;
            }

            // Do last vertex
            x0 = _vertices[i].X;
            y0 = _vertices[i].Y;
            x1 = _vertices[0].X;
            y1 = _vertices[0].Y;
            a = x0 * y1 - x1 * y0;
            signedArea += a;
            centroid.X += (x0 + x1) * a;
            centroid.Y += (y0 + y1) * a;

            signedArea *= 0.5;
            centroid.X /= (6.0 * signedArea);
            centroid.Y /= (6.0 * signedArea);

            return centroid;
        }

        /// <summary>
        ///     Determines whether this polygon is convex or concave. Throws if all vertices lie on a straight line, or if
        ///     there are 2 or fewer vertices.</summary>
        public bool IsConvex()
        {
            if (_vertices.Count <= 2)
                throw new InvalidOperationException();
            bool? crossPositive = null;
            for (int i = 0; i < _vertices.Count; i++)
            {
                var pt0 = i == 0 ? _vertices[_vertices.Count - 1] : _vertices[i - 1];
                var pt1 = _vertices[i];
                var pt2 = i == _vertices.Count - 1 ? _vertices[0] : _vertices[i + 1];
                double crossZ = (pt1 - pt0).CrossZ(pt2 - pt1);
                if (crossZ != 0)
                {
                    if (crossPositive == null)
                        crossPositive = crossZ > 0;
                    else if (crossPositive != crossZ > 0)
                        return false;
                }
            }
            if (crossPositive == null)
                throw new InvalidOperationException("All polygon points lie on a straight line.");
            return true;
        }

        /// <summary>Returns an array containing all the edges of this polygon.</summary>
        public IEnumerable<EdgeD> ToEdges()
        {
            int i;
            for (i = 0; i < _vertices.Count - 1; i++)
                yield return new EdgeD(_vertices[i], _vertices[i + 1]);
            yield return new EdgeD(_vertices[i], _vertices[0]);
        }

        // signed distance from point to polygon outline (negative if point is outside)

        /// <summary>
        ///     Returns the signed distance from <paramref name="p"/> to the outline of the polygon. The result is negative if
        ///     the point is outside of the polygon.</summary>
        /// <param name="p">
        ///     The point to calculate the distance from.</param>
        public double DistanceFromPoint(PointD p)
        {
            double getSegDistSq(double px, double py, PointD a, PointD b)
            {
                var x = a.X;
                var y = a.Y;
                var dx = b.X - x;
                var dy = b.Y - y;

                if (dx != 0 || dy != 0)
                {
                    var t = ((px - x) * dx + (py - y) * dy) / (dx * dx + dy * dy);

                    if (t > 1)
                    {
                        x = b.X;
                        y = b.Y;

                    }
                    else if (t > 0)
                    {
                        x += dx * t;
                        y += dy * t;
                    }
                }

                dx = px - x;
                dy = py - y;

                return dx * dx + dy * dy;
            }

            var inside = false;
            var minDistSq = double.PositiveInfinity;

            for (int i = 0, len = Vertices.Count, j = len - 1; i < len; j = i++)
            {
                var a = Vertices[i];
                var b = Vertices[j];

                if ((a.Y > p.Y != b.Y > p.Y) && (p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X))
                    inside = !inside;

                minDistSq = Math.Min(minDistSq, getSegDistSq(p.X, p.Y, a, b));
            }

            return minDistSq == 0 ? 0 : (inside ? 1 : -1) * Math.Sqrt(minDistSq);
        }

        struct Cell
        {
            public Cell(PointD p, double h, PolygonD polygon)
            {
                this.p = p; // cell center 
                this.h = h; // half the cell size
                d = polygon.DistanceFromPoint(p); // distance from cell center to polygon
                max = d + this.h * Math.Sqrt(2); // max distance to polygon within a cell
            }

            public PointD p { get; private set; }
            public double h { get; private set; }
            public double d { get; private set; }
            public double max { get; private set; }
        }

        public PointD GetLabelPoint(double precision = 1)
        {
            // find the bounding box of the polygon
            var minX = Vertices.Min(v => v.X);
            var maxX = Vertices.Max(v => v.X);
            var minY = Vertices.Min(v => v.Y);
            var maxY = Vertices.Max(v => v.Y);
            var width = maxX - minX;
            var height = maxY - minY;
            var cellSize = Math.Min(width, height);
            var h = cellSize / 2;

            if (cellSize == 0)
                return new PointD(minX, minY);

            // a priority queue of cells in order of their "potential" (max distance to polygon)
            var cellQueue = new PriorityQueue<Cell, double>(largestFirst: true);

            void enqueueCell(Cell c) { cellQueue.Add(c, c.max); }

            // cover polygon with initial cells
            for (var x = minX; x < maxX; x += cellSize)
                for (var y = minY; y < maxY; y += cellSize)
                    enqueueCell(new Cell(new PointD(x + h, y + h), h, this));

            // guess bounding box center
            var bestCell = new Cell(new PointD(minX + width / 2, minY + height / 2), 0, this);

            while (cellQueue.Count > 0)
            {
                // pick the most promising cell from the queue
                cellQueue.Extract(out var cell, out var weight);

                // update the best cell if we found a better one
                if (cell.d > bestCell.d)
                    bestCell = cell;

                // do not drill down further if there's no chance of a better solution
                if (cell.max - bestCell.d <= precision)
                    continue;

                // split the cell into four cells
                h = cell.h / 2;
                enqueueCell(new Cell(cell.p + new PointD(-h, -h), h, this));
                enqueueCell(new Cell(cell.p + new PointD(h, -h), h, this));
                enqueueCell(new Cell(cell.p + new PointD(-h, h), h, this));
                enqueueCell(new Cell(cell.p + new PointD(h, h), h, this));
            }

            return bestCell.p;
        }
    }
}
