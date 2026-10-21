using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Buffers;

namespace Cutear
{
    /// <summary>
    /// Defines a contract for a point, enabling zero-overhead generic execution.
    /// </summary>
    public interface IPoint
    {
        /// <summary>The X coordinate.</summary>
        double X { get; }

        /// <summary>The Y coordinate.</summary>
        double Y { get; }

        /// <summary>The Z coordinate.</summary>
        double Z { get; }
    }

    /// <summary>
    /// Represents a point with double-precision coordinates.
    /// </summary>
    public readonly struct Point : IPoint
    {
        /// <summary>The X coordinate.</summary>
        public double X { get; }

        /// <summary>The Y coordinate.</summary>
        public double Y { get; }

        /// <summary>The Z coordinate.</summary>
        public double Z { get; }

        /// <summary>Creates a new point with the specified 2D coordinates.</summary>
        public Point(double x, double y) => (X, Y, Z) = (x, y, 0);

        /// <summary>Creates a new point with the specified 3D coordinates.</summary>
        public Point(double x, double y, double z) => (X, Y, Z) = (x, y, z);

        /// <inheritdoc />
        public override string ToString() => $"({X}, {Y}, {Z})";
    }

    /// <summary>
    /// Defines a contract for a polygon, enabling zero-overhead generic execution over custom geometry containers.
    /// Rings are accessed as ReadOnlySpans to prevent array copying and heap allocations.
    /// </summary>
    /// <typeparam name="TPoint">The type of points, which must implement IPoint.</typeparam>
    public interface IPolygon<TPoint> where TPoint : struct, IPoint
    {
        /// <summary>
        /// Gets the number of rings in the polygon (including holes).
        /// </summary>
        int RingCount { get; }

        /// <summary>
        /// Gets a specific ring (outer boundary at index 0, followed by holes).
        /// </summary>
        /// <param name="index">The index of the ring.</param>
        /// <returns>A ReadOnlySpan containing the ring's points.</returns>
        ReadOnlySpan<TPoint> GetRing(int index);
    }

    /// <summary>
    /// Represents a generic polygon, composed of one or more linear rings of custom point types.
    /// The first ring represents the outer boundary, and subsequent rings represent holes.
    /// </summary>
    /// <typeparam name="TPoint">The type of point, which must implement IPoint.</typeparam>
    public readonly struct Polygon<TPoint> : IPolygon<TPoint> where TPoint : struct, IPoint
    {
        /// <summary>The rings of the polygon (outer boundary at index 0, followed by holes).</summary>
        public TPoint[][] Rings { get; }

        /// <inheritdoc />
        public int RingCount => Rings?.Length ?? 0;

        /// <inheritdoc />
        public ReadOnlySpan<TPoint> GetRing(int index) => Rings[index];

        /// <summary>Creates a new polygon from the specified rings.</summary>
        public Polygon(TPoint[][] rings) => Rings = rings ?? throw new ArgumentNullException(nameof(rings));
    }

    /// <summary>
    /// Represents a standard polygon, composed of one or more rings of the standard Point type.
    /// </summary>
    public readonly struct Polygon : IPolygon<Point>
    {
        /// <summary>The rings of the polygon (outer boundary at index 0, followed by holes).</summary>
        public Point[][] Rings { get; }

        /// <inheritdoc />
        public int RingCount => Rings?.Length ?? 0;

        /// <inheritdoc />
        public ReadOnlySpan<Point> GetRing(int index) => Rings[index];

        /// <summary>Creates a new polygon from the specified rings.</summary>
        public Polygon(Point[][] rings) => Rings = rings ?? throw new ArgumentNullException(nameof(rings));

        /// <summary>Creates a new polygon from GeoJSON-style double coordinates.</summary>
        public Polygon(double[][][] coordinates)
        {
            if (coordinates == null) throw new ArgumentNullException(nameof(coordinates));

            Rings = new Point[coordinates.Length][];
            for (int i = 0; i < coordinates.Length; i++)
            {
                var ringCoords = coordinates[i];
                var ring = new Point[ringCoords.Length];
                for (int j = 0; j < ringCoords.Length; j++)
                {
                    var pt = ringCoords[j];
                    if (pt.Length >= 3)
                    {
                        ring[j] = new Point(pt[0], pt[1], pt[2]);
                    }
                    else
                    {
                        ring[j] = new Point(pt[0], pt[1]);
                    }
                }
                Rings[i] = ring;
            }
        }

        /// <summary>
        /// Implicitly converts a non-generic Polygon to a generic Polygon&lt;Point&gt;.
        /// </summary>
        public static implicit operator Polygon<Point>(Polygon p) => new Polygon<Point>(p.Rings);
    }

    /// <summary>
    /// C# port of the Mapbox Earcut polygon triangulation library (v3.2.4).
    /// </summary>
    public class Cutear
    {
        [ThreadStatic]
        private static Triangulator? _triangulator;

        private static Triangulator Instance => _triangulator ??= new Triangulator();

        /// <summary>
        /// Triangulates a polygon given as a generic <see cref="IPolygon{TPoint}"/>.
        /// </summary>
        /// <param name="polygon">The polygon (outer ring at index 0, followed by holes).</param>
        /// <param name="dim">The number of coordinates per vertex (2 or 3).</param>
        /// <returns>Triangles as triplets of vertex indices.</returns>
        public static List<int> Tessellate<TPoint>(IPolygon<TPoint> polygon, int dim = 2) where TPoint : struct, IPoint
        {
            if (polygon == null) throw new ArgumentNullException(nameof(polygon));
            if (dim < 2 || dim > 3) throw new ArgumentOutOfRangeException(nameof(dim), "Dimension must be 2 or 3.");

            int ringCount = polygon.RingCount;
            if (ringCount == 0)
            {
                return new List<int>();
            }

            int totalVertices = 0;
            for (int i = 0; i < ringCount; i++)
            {
                totalVertices += polygon.GetRing(i).Length;
            }

            if (totalVertices == 0)
            {
                return new List<int>();
            }

            double[] rentData = ArrayPool<double>.Shared.Rent(totalVertices * dim);
            int[] rentHoles = ArrayPool<int>.Shared.Rent(Math.Max(0, ringCount - 1));

            try
            {
                int dataIndex = 0;
                int holeIndex = 0;
                int currentVertexOffset = 0;

                for (int i = 0; i < ringCount; i++)
                {
                    var ring = polygon.GetRing(i);
                    if (ring.Length == 0) continue;

                    if (i > 0)
                    {
                        rentHoles[holeIndex++] = currentVertexOffset;
                    }

                    for (int j = 0; j < ring.Length; j++)
                    {
                        var pt = ring[j];
                        rentData[dataIndex++] = pt.X;
                        rentData[dataIndex++] = pt.Y;
                        if (dim == 3)
                        {
                            rentData[dataIndex++] = pt.Z;
                        }
                    }

                    currentVertexOffset += ring.Length;
                }

                return Tessellate(
                    new ReadOnlySpan<double>(rentData, 0, dataIndex),
                    new ReadOnlySpan<int>(rentHoles, 0, holeIndex),
                    dim: dim);
            }
            finally
            {
                ArrayPool<double>.Shared.Return(rentData);
                ArrayPool<int>.Shared.Return(rentHoles);
            }
        }

        /// <summary>
        /// Triangulates a polygon given as a flat array of vertex coordinates.
        /// </summary>
        /// <param name="data">Flat array of vertex coordinates.</param>
        /// <param name="holeIndices">Indices (in vertices, not coordinates) where each hole ring starts.</param>
        /// <param name="dim">The number of coordinates per vertex in <paramref name="data"/>.</param>
        /// <returns>Triangles as triplets of vertex indices into <paramref name="data"/>.</returns>
        public static List<int> Tessellate(ReadOnlySpan<double> data, ReadOnlySpan<int> holeIndices, int dim = 2)
        {
            int estimatedIndices = Math.Max(0, 3 * (data.Length / dim - 2));
            var triangles = new List<int>(estimatedIndices);
            Instance.Earcut(data, holeIndices, dim, triangles);
            return triangles;
        }

        /// <summary>
        /// Refines a triangulation toward the constrained Delaunay triangulation by legalizing every
        /// interior edge in place with Lawson flips — maximizing the minimum angle and removing most
        /// slivers. The number of triangles and the polygon boundary are preserved.
        /// </summary>
        /// <param name="triangles">Triangle indices, as returned by <see cref="Tessellate(ReadOnlySpan{double}, ReadOnlySpan{int}, int)"/>; mutated in place.</param>
        /// <param name="coords">The flat vertex coordinates passed to the triangulation.</param>
        /// <param name="dim">The number of coordinates per vertex in <paramref name="coords"/>.</param>
        public static void Refine(Span<int> triangles, ReadOnlySpan<double> coords, int dim = 2)
        {
            Instance.Refine(triangles, coords, dim);
        }

        /// <inheritdoc cref="Refine(Span{int}, ReadOnlySpan{double}, int)"/>
        public static void Refine(List<int> triangles, ReadOnlySpan<double> coords, int dim = 2)
        {
            if (triangles == null) throw new ArgumentNullException(nameof(triangles));
#if NET5_0_OR_GREATER
            Instance.Refine(CollectionsMarshal.AsSpan(triangles), coords, dim);
#else
            int n = triangles.Count;
            int[] rent = ArrayPool<int>.Shared.Rent(n);
            try
            {
                triangles.CopyTo(rent);
                Instance.Refine(new Span<int>(rent, 0, n), coords, dim);
                for (int i = 0; i < n; i++) triangles[i] = rent[i];
            }
            finally
            {
                ArrayPool<int>.Shared.Return(rent);
            }
#endif
        }

        /// <summary>
        /// Returns the relative difference between the polygon area and the area of its triangulation —
        /// a value near 0 means a correct triangulation.
        /// </summary>
        public static double Deviation(ReadOnlySpan<double> data, ReadOnlySpan<int> holeIndices, ReadOnlySpan<int> triangles, int dim = 2)
        {
            var hasHoles = holeIndices.Length > 0;
            var outerLen = hasHoles ? holeIndices[0] * dim : data.Length;

            var polygonArea = Math.Abs(SignedArea(data, 0, outerLen, dim));
            if (hasHoles)
            {
                var len = holeIndices.Length;

                for (var i = 0; i < len; i++)
                {
                    var start = holeIndices[i] * dim;
                    var end = i < len - 1 ? holeIndices[i + 1] * dim : data.Length;
                    polygonArea -= Math.Abs(SignedArea(data, start, end, dim));
                }
            }

            var trianglesArea = default(double);
            for (var i = 0; i < triangles.Length; i += 3)
            {
                var a = triangles[i] * dim;
                var b = triangles[i + 1] * dim;
                var c = triangles[i + 2] * dim;
                trianglesArea += Math.Abs(
                    (data[a] - data[c]) * (data[b + 1] - data[a + 1]) -
                    (data[a] - data[b]) * (data[c + 1] - data[a + 1]));
            }

            // with no triangles, a polygon area within shoelace roundoff of zero (which scales with the
            // squared coordinate magnitude) means the input was degenerate rather than mistriangulated
            if (trianglesArea == 0)
            {
                double max = 0;
                for (var i = 0; i < data.Length; i += dim)
                {
                    max = Math.Max(max, Math.Max(Math.Abs(data[i]), Math.Abs(data[i + 1])));
                }
                return Math.Abs(polygonArea) <= data.Length * max * max * MachineEpsilon ? 0 : 1;
            }

            return Math.Abs((trianglesArea - polygonArea) / polygonArea);
        }

        /// <summary>
        /// Turns a polygon in multi-dimensional array form (e.g. as in GeoJSON) into the flat form Earcut accepts.
        /// </summary>
        public static (double[] vertices, int[] holes, int dimensions) Flatten(double[][][] data)
        {
            if (data == null || data.Length == 0 || data[0].Length == 0)
            {
                return (Array.Empty<double>(), Array.Empty<int>(), 0);
            }

            var vertices = new List<double>();
            var holes = new List<int>();
            int dimensions = data[0][0].Length;
            int holeIndex = 0;
            int prevLen = 0;

            foreach (var ring in data)
            {
                foreach (var p in ring)
                {
                    for (int d = 0; d < dimensions; d++)
                    {
                        vertices.Add(p[d]);
                    }
                }
                if (prevLen > 0)
                {
                    holeIndex += prevLen;
                    holes.Add(holeIndex);
                }
                prevLen = ring.Length;
            }

            return (vertices.ToArray(), holes.ToArray(), dimensions);
        }

        // Number.EPSILON in JavaScript (not double.Epsilon, which is the smallest subnormal)
        private const double MachineEpsilon = 2.220446049250313e-16;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double SignedArea(ReadOnlySpan<double> data, int start, int end, int dim)
        {
            double sum = 0;
            for (int i = start, j = end - dim; i < end; i += dim)
            {
                sum += (data[j] - data[i]) * (data[i + 1] + data[j + 1]);
                j = i;
            }
            return sum;
        }

        // all mutable state of a triangulation (node pool, hole-bridge index, sort and refine scratch)
        // lives in one per-thread instance, so repeated calls allocate nothing beyond the result
        private sealed class Triangulator
        {
            private Node[] _nodes = new Node[64];
            private int _nodeCount;

            // set by FilterPoints whenever it removes at least one node; read by EarcutLinked's stall
            // handler to decide whether another clip pass is worth attempting before the costlier stages
            private bool _filteredOut;

            // true only while EliminateHoles merges holes, so RemoveNode keeps the block index live (GrowBlock)
            private bool _indexActive;

            private Node[] _queue = new Node[16];
            private Node[] _queueBuf = new Node[16];

            public void Earcut(ReadOnlySpan<double> data, ReadOnlySpan<int> holeIndices, int dim, List<int> triangles)
            {
                try
                {
                    var hasHoles = holeIndices.Length > 0;
                    var outerLen = hasHoles ? holeIndices[0] * dim : data.Length;

                    var outerNode = LinkedList(data, 0, outerLen, dim, true);

                    if (outerNode == null || outerNode.next == outerNode.prev) return;

                    double minX = 0, minY = 0, invSize = 0;

                    if (hasHoles) outerNode = EliminateHoles(data, holeIndices, outerNode, dim);

                    // if the shape is not too simple, we'll use z-order curve hash later; calculate polygon bbox
                    if (data.Length > 80 * dim)
                    {
                        minX = data[0];
                        minY = data[1];
                        double maxX = minX;
                        double maxY = minY;

                        for (int i = dim; i < outerLen; i += dim)
                        {
                            double x = data[i];
                            double y = data[i + 1];
                            if (x < minX) minX = x;
                            if (y < minY) minY = y;
                            if (x > maxX) maxX = x;
                            if (y > maxY) maxY = y;
                        }

                        // minX, minY and invSize are later used to transform coords into integers for z-order calculation
                        invSize = Math.Max(maxX - minX, maxY - minY);
                        invSize = invSize != 0 ? 32767 / invSize : 0;
                    }

                    EarcutLinked(outerNode, triangles, minX, minY, invSize);
                }
                finally
                {
                    Reset();
                }
            }

            private void Reset()
            {
                for (int i = 0; i < _nodeCount; i++)
                {
                    _nodes[i].ClearReferences();
                }
                _nodeCount = 0;
                _indexActive = false;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private Node CreateNode(int i, double x, double y)
            {
                if (_nodeCount == _nodes.Length)
                {
                    Array.Resize(ref _nodes, _nodes.Length * 2);
                }

                var node = _nodes[_nodeCount];
                if (node == null)
                {
                    node = new Node();
                    _nodes[_nodeCount] = node;
                }
                _nodeCount++;

                node.Reset(i, x, y);
                return node;
            }

            // create a circular doubly linked list from polygon points in the specified winding order
            private Node? LinkedList(ReadOnlySpan<double> data, int start, int end, int dim, bool clockwise)
            {
                Node? last = null;

                if (clockwise == (SignedArea(data, start, end, dim) > 0))
                {
                    for (int i = start; i < end; i += dim) last = InsertNode(i / dim, data[i], data[i + 1], last);
                }
                else
                {
                    for (int i = end - dim; i >= start; i -= dim) last = InsertNode(i / dim, data[i], data[i + 1], last);
                }

                if (last != null && Equals(last, last.next))
                {
                    RemoveNode(last);
                    last = last.next;
                }

                return last;
            }

            // Remove collinear or coincident points; removability depends only on a node's immediate
            // neighbors, so we sweep forward and re-check the predecessor after each removal. With no `end`
            // we sweep the whole ring, lapping until nothing is removable (the fixpoint the clipper needs).
            // With an explicit `end` we heal only the dirty window around a bridge/diagonal cut, stopping at
            // `end` rather than lapping — O(window) instead of O(ring).
            private Node FilterPoints(Node start, Node? end = null)
            {
                if (end == null) end = start;
                bool full = end == start;

                var p = start;
                bool again;
                do
                {
                    again = false;
                    if (p != p.next && !p.steiner &&
                        (Equals(p, p.next) || Area(p.prev, p, p.next) == 0))
                    {
                        if (full || p == end) end = p.prev; // pull the stop bound back past the removal
                        _filteredOut = true;
                        RemoveNode(p);
                        p = p.prev;         // re-check the predecessor
                        again = true;
                    }
                    else if (full || p != end)
                    {
                        p = p.next;
                        again = !full;      // local heal: keep looping until the sweep reaches end
                    }
                } while (again || p != end);

                return end;
            }

            // main ear slicing loop which triangulates a polygon (given as a linked list)
            private void EarcutLinked(Node ear, List<int> triangles, double minX, double minY, double invSize)
            {
                // interlink polygon nodes in z-order
                if (invSize != 0) IndexCurve(ear, minX, minY, invSize);

                var stop = ear;
                bool cured = false;

                // iterate through ears, slicing them one by one
                while (ear.prev != ear.next)
                {
                    var prev = ear.prev;
                    var next = ear.next;

                    if (Area(prev, ear, next) < 0 && (invSize != 0 ? IsEarHashed(ear, minX, minY, invSize) : IsEar(ear)))
                    {
                        // cut off the triangle
                        triangles.Add(prev.i);
                        triangles.Add(ear.i);
                        triangles.Add(next.i);

                        RemoveNode(ear);
                        ear = next;
                        stop = next;
                        continue;
                    }

                    ear = next;

                    // if we looped through the whole remaining polygon and can't find any more ears
                    if (ear == stop)
                    {
                        // try filtering collinear/coincident points and slicing again — repeat as long as
                        // filtering actually removes nodes, since each removal can expose new ears
                        _filteredOut = false;
                        ear = FilterPoints(ear);
                        if (_filteredOut) { stop = ear; continue; }

                        // filtering is exhausted: cure small local self-intersections once, then retry
                        if (!cured)
                        {
                            ear = CureLocalIntersections(ear, triangles);
                            stop = ear;
                            cured = true;
                            continue;
                        }

                        // as a last resort, try splitting the remaining polygon into two
                        SplitEarcut(ear, triangles, minX, minY, invSize);
                        break;
                    }
                }
            }

            // check whether a polygon node forms a valid ear with adjacent nodes
            private static bool IsEar(Node ear)
            {
                // reflex check (Area(a, b, c) >= 0) is hoisted into the EarcutLinked caller
                Node a = ear.prev, b = ear, c = ear.next;
                double ax = a.x, bx = b.x, cx = c.x, ay = a.y, by = b.y, cy = c.y;
                double x0 = Math.Min(ax, Math.Min(bx, cx)); // triangle bbox
                double y0 = Math.Min(ay, Math.Min(by, cy));
                double x1 = Math.Max(ax, Math.Max(bx, cx));
                double y1 = Math.Max(ay, Math.Max(by, cy));

                // make sure we don't have other points inside the potential ear
                var p = c.next;
                while (p != a)
                {
                    if (p.x >= x0 && p.x <= x1 && p.y >= y0 && p.y <= y1 && !(ax == p.x && ay == p.y) &&
                        PointInTriangle(ax, ay, bx, by, cx, cy, p.x, p.y) && Area(p.prev, p, p.next) >= 0) return false;
                    p = p.next;
                }
                return true;
            }

            private static bool IsEarHashed(Node ear, double minX, double minY, double invSize)
            {
                // reflex check is hoisted into the EarcutLinked caller (see IsEar)
                Node a = ear.prev, b = ear, c = ear.next;
                double ax = a.x, bx = b.x, cx = c.x, ay = a.y, by = b.y, cy = c.y;
                double x0 = Math.Min(ax, Math.Min(bx, cx)); // triangle bbox
                double y0 = Math.Min(ay, Math.Min(by, cy));
                double x1 = Math.Max(ax, Math.Max(bx, cx));
                double y1 = Math.Max(ay, Math.Max(by, cy));
                int minZ = ZOrder(x0, y0, minX, minY, invSize); // z-order range for the current triangle bbox
                int maxZ = ZOrder(x1, y1, minX, minY, invSize);

                var p = ear.prevZ;
                while (p != null && p.z >= minZ) // look for points inside the triangle in decreasing z-order
                {
                    if (p.x >= x0 && p.x <= x1 && p.y >= y0 && p.y <= y1 && p != c && !(ax == p.x && ay == p.y) &&
                        PointInTriangle(ax, ay, bx, by, cx, cy, p.x, p.y) && Area(p.prev, p, p.next) >= 0) return false;
                    p = p.prevZ;
                }

                var n = ear.nextZ;
                while (n != null && n.z <= maxZ) // look for points in increasing z-order
                {
                    if (n.x >= x0 && n.x <= x1 && n.y >= y0 && n.y <= y1 && n != c && !(ax == n.x && ay == n.y) &&
                        PointInTriangle(ax, ay, bx, by, cx, cy, n.x, n.y) && Area(n.prev, n, n.next) >= 0) return false;
                    n = n.nextZ;
                }

                return true;
            }

            // go through all polygon nodes and cure small local self-intersections
            private Node CureLocalIntersections(Node start, List<int> triangles)
            {
                var p = start;
                bool cured = false;
                do
                {
                    var a = p.prev;
                    var b = p.next.next;

                    if (Intersects(a, p, p.next, b, false) && LocallyInside(a, b) && LocallyInside(b, a))
                    {
                        triangles.Add(a.i);
                        triangles.Add(p.i);
                        triangles.Add(b.i);

                        // remove two nodes involved
                        RemoveNode(p);
                        RemoveNode(p.next);

                        p = start = b;
                        cured = true;
                    }
                    p = p.next;
                } while (p != start);

                return cured ? FilterPoints(p) : p;
            }

            // try splitting polygon into two and triangulate them independently
            private void SplitEarcut(Node start, List<int> triangles, double minX, double minY, double invSize)
            {
                // look for a valid diagonal that divides the polygon into two
                var a = start;
                do
                {
                    var b = a.next.next;
                    while (b != a.prev)
                    {
                        if (a.i != b.i && IsValidDiagonal(a, b))
                        {
                            // split the polygon in two by the diagonal
                            var c = SplitPolygon(a, b);

                            // filter colinear points around the cuts
                            a = FilterPoints(a, a.next);
                            c = FilterPoints(c, c.next);

                            // run earcut on each half
                            EarcutLinked(a, triangles, minX, minY, invSize);
                            EarcutLinked(c, triangles, minX, minY, invSize);
                            return;
                        }
                        b = b.next;
                    }
                    a = a.next;
                } while (a != start);
            }

            // link every hole into the outer loop, producing a single-ring polygon without holes
            private Node EliminateHoles(ReadOnlySpan<double> data, ReadOnlySpan<int> holeIndices, Node outerNode, int dim)
            {
                int len = holeIndices.Length;
                if (_queue.Length < len)
                {
                    _queue = new Node[len];
                    _queueBuf = new Node[len];
                }
                int count = 0;

                for (int i = 0; i < len; i++)
                {
                    int start = holeIndices[i] * dim;
                    int end = i < len - 1 ? holeIndices[i + 1] * dim : data.Length;
                    var list = LinkedList(data, start, end, dim, false);
                    if (list == null) continue;
                    if (list == list.next) list.steiner = true;
                    _queue[count++] = GetLeftmost(list);
                }

                SortQueue(count);

                // block-bbox index for FindHoleBridge, grown append-only as holes merge (see notes
                // above BuildBlockIndex). Seed it with the outer ring, then append each merged hole.
                BuildBlockIndex(data.Length / dim, len);
                IndexSegment(outerNode, outerNode);

                // process holes from left to right; _indexActive lets RemoveNode keep block bboxes live as
                // FilterPoints heals edges during merges (see GrowBlock)
                _indexActive = true;
                for (int i = 0; i < count; i++)
                {
                    outerNode = EliminateHole(_queue[i], outerNode);
                }
                _indexActive = false;

                Array.Clear(_queue, 0, count);

                // collapse collinear/coincident points across the whole merged ring once before clipping
                return FilterPoints(outerNode);
            }

            // stable merge sort of the hole queue (matching JavaScript's stable Array.prototype.sort)
            private void SortQueue(int n)
            {
                if (n <= 32)
                {
                    for (int i = 1; i < n; i++)
                    {
                        var node = _queue[i];
                        int j = i - 1;
                        while (j >= 0 && CompareXYSlope(_queue[j], node) > 0) { _queue[j + 1] = _queue[j]; j--; }
                        _queue[j + 1] = node;
                    }
                    return;
                }

                Node[] src = _queue, dst = _queueBuf;
                for (int width = 1; width < n; width *= 2)
                {
                    for (int lo = 0; lo < n; lo += 2 * width)
                    {
                        int mid = Math.Min(lo + width, n), hi = Math.Min(lo + 2 * width, n);
                        int i = lo, j = mid, k = lo;
                        while (i < mid && j < hi) dst[k++] = CompareXYSlope(src[j], src[i]) < 0 ? src[j++] : src[i++];
                        while (i < mid) dst[k++] = src[i++];
                        while (j < hi) dst[k++] = src[j++];
                    }
                    var tmp = src; src = dst; dst = tmp;
                }
                if (src != _queue) Array.Copy(src, _queue, n);
                Array.Clear(_queueBuf, 0, n);
            }

            // mirrors the JavaScript comparator `a.x - b.x || a.y - b.y || slopeA - slopeB`,
            // where a NaN term is falsy and falls through (and a final NaN counts as equal)
            private static int CompareXYSlope(Node a, Node b)
            {
                // when the left-most point of 2 holes meet at a vertex, sort the holes counterclockwise so that when we find
                // the bridge to the outer shell is always the point that they meet at.
                double d = a.x - b.x;
                if (d != 0 && !double.IsNaN(d)) return d < 0 ? -1 : 1;
                d = a.y - b.y;
                if (d != 0 && !double.IsNaN(d)) return d < 0 ? -1 : 1;
                d = (a.next.y - a.y) / (a.next.x - a.x) -
                    (b.next.y - b.y) / (b.next.x - b.x);
                return d < 0 ? -1 : d > 0 ? 1 : 0;
            }

            // find a bridge between vertices that connects hole with an outer ring and link it
            private Node EliminateHole(Node hole, Node outerNode)
            {
                var bridge = FindHoleBridge(hole, outerNode);
                if (bridge == null)
                {
                    return outerNode;
                }

                var bridgeReverse = SplitPolygon(bridge, hole);

                // index the merged-in segment before filtering: in ring order the splice runs
                // bridge -> hole -> bridgeReverse -> bridge2 -> (bridge's old next), covering the
                // hole's edges and both new slit edges. FilterPoints below only drops collinear /
                // coincident points, so these bboxes stay valid (conservative) supersets.
                var bridge2 = bridgeReverse.next;
                IndexSegment(bridge, bridge2.next);

                // heal collinear/coincident points around the two new slit edges
                FilterPoints(bridgeReverse, bridgeReverse.next);
                return FilterPoints(bridge, bridge.next);
            }

            // Block-bbox index for FindHoleBridge (mapbox/earcut#183): one [minX,minY,maxX,maxY] bbox per K
            // consecutive ring edges, in a flat array, so the leftward-ray scan can skip whole blocks in
            // O(1) instead of walking the entire merged ring. Grown append-only — the outer ring seeds
            // it, then each merged hole appends a segment (head node, stop node, K-blocks over
            // head..stop); independent segments, not a ring tiling, since splices land mid-ring.
            // Buffers are sized once from the input upper bound and reused across calls.
            //
            // FilterPoints only drops collinear/coincident points, so a stale bbox stays a conservative
            // superset of its live edges (never a false skip); the scan skips dead nodes (p.prev.next !=
            // p) and lazily advances a dead stop. Blocks are scanned in append (not ring) order, so the
            // chosen bridge can differ from the un-indexed code — a different but equally valid result.
            private const int K = 16; // edges per block

            private double[] _blockBBox = new double[0]; // [minX,minY,maxX,maxY] per block
            private Node[] _blockHead = new Node[0];     // first node of each block's segment
            private Node[] _blockStop = new Node[0];     // node just past each block's segment (exclusive walk bound)
            private int _numBlocks;

            private void BuildBlockIndex(int maxNodes, int numHoles)
            {
                // upper bound: every input node indexed once, +2 bridge nodes per hole, plus a partial
                // trailing block per appended segment (outer ring + one per hole)
                int maxBlocks = (maxNodes + 2 * numHoles + K - 1) / K + numHoles + 2;
                if (_blockHead.Length < maxBlocks) GrowBlockIndex(maxBlocks);
                _numBlocks = 0;
            }

            private void GrowBlockIndex(int maxBlocks)
            {
                Array.Resize(ref _blockBBox, maxBlocks * 4);
                Array.Resize(ref _blockHead, maxBlocks);
                Array.Resize(ref _blockStop, maxBlocks);
            }

            // index the ring run head..stop (exclusive) as ceil(len / K) blocks; head == stop means
            // the whole ring. each block's bbox covers both endpoints of every edge it owns.
            private void IndexSegment(Node head, Node stop)
            {
                var p = head;
                do
                {
                    int b = _numBlocks++;
                    if (b == _blockHead.Length) GrowBlockIndex(b * 2);
                    _blockHead[b] = p;
                    double minX = p.x, minY = p.y, maxX = p.x, maxY = p.y;
                    int k = 0;
                    do
                    {
                        var c = p.next; // edge p->c; bbox must bound both endpoints
                        p.z = b; // reuse z as the owning block during EliminateHoles (see GrowBlock)
                        if (c.x < minX) minX = c.x; if (c.x > maxX) maxX = c.x;
                        if (c.y < minY) minY = c.y; if (c.y > maxY) maxY = c.y;
                        p = c;
                    } while (++k < K && p != stop);
                    _blockStop[b] = p;
                    int g = b * 4;
                    _blockBBox[g] = minX; _blockBBox[g + 1] = minY; _blockBBox[g + 2] = maxX; _blockBBox[g + 3] = maxY;
                } while (p != stop);
            }

            // when FilterPoints heals an edge head->tail (removing the collinear node between them), the
            // healed edge can extend past head's frozen block bbox if its old far endpoint lived in another
            // block; grow head's block bbox to cover tail so the leftward-ray prune can't false-skip it.
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void GrowBlock(Node head, Node tail)
            {
                int g = head.z * 4;
                if (tail.x < _blockBBox[g]) _blockBBox[g] = tail.x;
                if (tail.y < _blockBBox[g + 1]) _blockBBox[g + 1] = tail.y;
                if (tail.x > _blockBBox[g + 2]) _blockBBox[g + 2] = tail.x;
                if (tail.y > _blockBBox[g + 3]) _blockBBox[g + 3] = tail.y;
            }

            private Node LiveBlockStop(int b)
            {
                var stop = _blockStop[b];
                while (stop.prev.next != stop) stop = stop.next;
                _blockStop[b] = stop;
                return stop;
            }

            // the block's head node can be removed by FilterPoints during merges; advance it to the next
            // live node so the walk doesn't start on (and immediately terminate at) a dead node. For the
            // single full-ring seed block (head == stop) the same forward advance keeps them equal, so the
            // do-while still laps the whole ring instead of collapsing to an empty walk.
            private Node LiveBlockHead(int b)
            {
                var head = _blockHead[b];
                while (head.prev.next != head) head = head.next;
                _blockHead[b] = head;
                return head;
            }

            // David Eberly's algorithm for finding a bridge between hole and outer polygon
            private Node? FindHoleBridge(Node hole, Node outerNode)
            {
                var p = outerNode;
                double hx = hole.x;
                double hy = hole.y;
                double qx = double.NegativeInfinity;
                Node? m = null;
                var bbox = _blockBBox;

                // find a segment intersected by a ray from the hole's leftmost point to the left;
                // segment's endpoint with lesser x will be potential connection point
                // unless they intersect at a vertex, then choose the vertex
                if (Equals(hole, p)) return p;

                // scan blocks; skip any whose bbox can't hold a crossing that beats qx and lies left
                // of hx (the prune Morton order can't express — explicit per-axis [minY,maxY]/[minX,maxX])
                for (int b = 0, g = 0; b < _numBlocks; b++, g += 4)
                {
                    if (hy < bbox[g + 1] || hy > bbox[g + 3] || bbox[g] > hx || bbox[g + 2] <= qx) continue;

                    // ensure the walk's exclusive bound is live so we don't overrun into other blocks
                    var stop = LiveBlockStop(b);

                    p = LiveBlockHead(b);
                    do
                    {
                        if (p.prev.next == p) // skip nodes removed by FilterPoints (stale in the index)
                        {
                            if (Equals(hole, p.next)) return p.next;
                            else if (hy <= p.y && hy >= p.next.y && p.next.y != p.y)
                            {
                                double x = p.x + (hy - p.y) * (p.next.x - p.x) / (p.next.y - p.y);
                                if (x <= hx && x > qx)
                                {
                                    qx = x;
                                    m = p.x < p.next.x ? p : p.next;
                                    if (x == hx) return m; // hole touches outer segment; pick leftmost endpoint
                                }
                            }
                        }
                        p = p.next;
                    } while (p != stop);
                }

                if (m == null) return null;

                // look for points inside the triangle of hole point, segment intersection and endpoint;
                // if there are no points found, we have a valid connection;
                // otherwise choose the point of the minimum angle with the ray as connection point

                double mx = m.x;
                double my = m.y;
                double tminY = Math.Min(hy, my); // the triangle's y span; x span is [mx, hx]
                double tmaxY = Math.Max(hy, my);
                double tanMin = double.PositiveInfinity;

                // scan the same blocks; skip any whose bbox can't overlap the triangle's [mx,hx]×[tminY,tmaxY] box
                for (int b = 0, g = 0; b < _numBlocks; b++, g += 4)
                {
                    if (bbox[g + 2] < mx || bbox[g] > hx || bbox[g + 3] < tminY || bbox[g + 1] > tmaxY) continue;

                    var stop = LiveBlockStop(b);

                    p = LiveBlockHead(b);
                    do
                    {
                        if (p.prev.next == p && hx >= p.x && p.x >= mx && hx != p.x && // skip dead nodes
                            PointInTriangle(hy < my ? hx : qx, hy, mx, my, hy < my ? qx : hx, hy, p.x, p.y))
                        {
                            double tan = Math.Abs(hy - p.y) / (hx - p.x); // tangential

                            // if hole point sits on p's horizontal edge (T-junction touch): the bridge runs
                            // along that edge — LocallyInside rejects it as collinear, but it's valid
                            if ((LocallyInside(p, hole) || (p.y == hy && p.next.y == hy && p.next.x > hx)) &&
                                (tan < tanMin || (tan == tanMin && (p.x > m.x || (p.x == m.x && SectorContainsSector(m, p))))))
                            {
                                m = p;
                                tanMin = tan;
                            }
                        }

                        p = p.next;
                    } while (p != stop);
                }

                return m;
            }

            // whether sector in vertex m contains sector in vertex p in the same coordinates
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static bool SectorContainsSector(Node m, Node p)
            {
                return Area(m.prev, m, p.prev) < 0 && Area(p.next, m, m.next) < 0;
            }

            // scratch buffers reused across calls and grown on demand: two node-ref arrays that
            // ping-pong during the radix passes, plus parallel z-value arrays so the passes read
            // z from contiguous memory instead of dereferencing each node. 256-entry histogram for
            // 8-bit digits; the small histogram keeps per-call setup cheap (most rings are short)
            private Node[] _sortArr = new Node[64];
            private Node[] _sortBuf = new Node[0];
            private uint[] _zArr = new uint[0];
            private uint[] _zBuf = new uint[0];
            private readonly uint[] _counts = new uint[256];

            // interlink polygon nodes in z-order: collect into an array, sort by z, relink
            private void IndexCurve(Node start, double minX, double minY, double invSize)
            {
                var p = start;
                int n = 0;
                do
                {
                    // always (re)compute: z may still hold a block index left over from EliminateHoles
                    p.z = ZOrder(p.x, p.y, minX, minY, invSize);
                    if (n == _sortArr.Length) Array.Resize(ref _sortArr, n * 2);
                    _sortArr[n++] = p;
                    p = p.next;
                } while (p != start);

                SortNodes(n);

                Node? prev = null;
                for (int i = 0; i < n; i++)
                {
                    var node = _sortArr[i];
                    node.prevZ = prev;
                    if (prev != null) prev.nextZ = node;
                    prev = node;
                }
                prev!.nextZ = null;

                // drop the node refs but keep the capacity
                Array.Clear(_sortArr, 0, n);
                if (_sortBuf.Length >= n) Array.Clear(_sortBuf, 0, n);
            }

            // sort the first n nodes of _sortArr by z, in place: insertion sort for small n (cheaper
            // than histogram setup), else LSD radix in four 8-bit passes (covering z's 30 bits)
            private void SortNodes(int n)
            {
                var sortArr = _sortArr;
                if (n <= 32)
                {
                    for (int i = 1; i < n; i++)
                    {
                        var node = sortArr[i];
                        int z = node.z;
                        int j = i - 1;
                        while (j >= 0 && sortArr[j].z > z) { sortArr[j + 1] = sortArr[j]; j--; }
                        sortArr[j + 1] = node;
                    }
                    return;
                }

                if (_zArr.Length < n)
                {
                    _zArr = new uint[n];
                    _zBuf = new uint[n];
                    _sortBuf = new Node[n];
                }
                for (int i = 0; i < n; i++) _zArr[i] = (uint)sortArr[i].z;

                // even pass count lands the sorted result back in _sortArr
                RadixPass(n, sortArr, _zArr, _sortBuf, _zBuf, 0);
                RadixPass(n, _sortBuf, _zBuf, sortArr, _zArr, 8);
                RadixPass(n, sortArr, _zArr, _sortBuf, _zBuf, 16);
                RadixPass(n, _sortBuf, _zBuf, sortArr, _zArr, 24);
            }

            // one LSD radix pass: stably scatter the first n nodes (and their z) from src to dst,
            // bucketed by the 8-bit digit of z at the given bit shift
            private void RadixPass(int n, Node[] src, uint[] srcZ, Node[] dst, uint[] dstZ, int shift)
            {
                var counts = _counts;
                Array.Clear(counts, 0, counts.Length);
                for (int i = 0; i < n; i++) counts[(srcZ[i] >> shift) & 0xff]++;
                // turn per-bucket counts into start offsets (prefix sum)
                uint sum = 0;
                for (int b = 0; b < 256; b++) { uint c = counts[b]; counts[b] = sum; sum += c; }
                for (int i = 0; i < n; i++)
                {
                    uint z = srcZ[i];
                    uint pos = counts[(z >> shift) & 0xff]++;
                    dst[pos] = src[i];
                    dstZ[pos] = z;
                }
            }

            // z-order of a point given coords and inverse of the longer side of data bbox
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static int ZOrder(double x, double y, double minX, double minY, double invSize)
            {
                // coords are transformed into non-negative 15-bit integer range
                int ix = (int)((x - minX) * invSize);
                int iy = (int)((y - minY) * invSize);

                ix = (ix | (ix << 8)) & 0x00FF00FF;
                ix = (ix | (ix << 4)) & 0x0F0F0F0F;
                ix = (ix | (ix << 2)) & 0x33333333;
                ix = (ix | (ix << 1)) & 0x55555555;

                iy = (iy | (iy << 8)) & 0x00FF00FF;
                iy = (iy | (iy << 4)) & 0x0F0F0F0F;
                iy = (iy | (iy << 2)) & 0x33333333;
                iy = (iy | (iy << 1)) & 0x55555555;

                return ix | (iy << 1);
            }

            // find the leftmost node of a polygon ring
            private static Node GetLeftmost(Node start)
            {
                var p = start;
                var leftmost = start;
                do
                {
                    if (p.x < leftmost.x || (p.x == leftmost.x && p.y < leftmost.y)) leftmost = p;
                    p = p.next;
                } while (p != start);

                return leftmost;
            }

            // check if a point lies within a convex triangle
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static bool PointInTriangle(double ax, double ay, double bx, double by, double cx, double cy, double px, double py)
            {
                return (cx - px) * (ay - py) >= (ax - px) * (cy - py) &&
                       (ax - px) * (by - py) >= (bx - px) * (ay - py) &&
                       (bx - px) * (cy - py) >= (cx - px) * (by - py);
            }

            // check if a diagonal between two polygon nodes is valid (lies in polygon interior)
            private static bool IsValidDiagonal(Node a, Node b)
            {
                bool zeroLength = Equals(a, b) && Area(a.prev, a, a.next) > 0 && Area(b.prev, b, b.next) > 0; // degenerate case
                return a.next.i != b.i && (zeroLength || LocallyInside(a, b) && LocallyInside(b, a) && // locally visible
                    (Area(a.prev, a, b.prev) != 0 || Area(a, b.prev, b) != 0)) && // no opposite-facing sectors
                    !IntersectsPolygon(a, b) && (zeroLength || MiddleInside(a, b)); // doesn't intersect other edges, diagonal inside polygon
            }

            // signed area of a triangle
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static double Area(Node p, Node q, Node r)
            {
                return (q.y - p.y) * (r.x - q.x) - (q.x - p.x) * (r.y - q.y);
            }

            // check if two points are equal
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static bool Equals(Node p1, Node p2)
            {
                return p1.x == p2.x && p1.y == p2.y;
            }

            // check if two segments intersect; by default includes collinear boundary touches
            private static bool Intersects(Node p1, Node q1, Node p2, Node q2, bool includeBoundary = true)
            {
                double o1 = Area(p1, q1, p2);
                double o2 = Area(p1, q1, q2);
                double o3 = Area(p2, q2, p1);
                double o4 = Area(p2, q2, q1);

                if (((o1 > 0 && o2 < 0) || (o1 < 0 && o2 > 0)) && ((o3 > 0 && o4 < 0) || (o3 < 0 && o4 > 0))) return true;

                if (!includeBoundary) return false;

                if (o1 == 0 && OnSegment(p1, p2, q1)) return true; // p1, q1 and p2 are collinear and p2 lies on p1q1
                if (o2 == 0 && OnSegment(p1, q2, q1)) return true; // p1, q1 and q2 are collinear and q2 lies on p1q1
                if (o3 == 0 && OnSegment(p2, p1, q2)) return true; // p2, q2 and p1 are collinear and p1 lies on p2q2
                if (o4 == 0 && OnSegment(p2, q1, q2)) return true; // p2, q2 and q1 are collinear and q1 lies on p2q2

                return false;
            }

            // for collinear points p, q, r, check if point q lies on segment pr
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static bool OnSegment(Node p, Node q, Node r)
            {
                return q.x <= Math.Max(p.x, r.x) && q.x >= Math.Min(p.x, r.x) && q.y <= Math.Max(p.y, r.y) && q.y >= Math.Min(p.y, r.y);
            }

            // check if a polygon diagonal intersects any polygon segments
            private static bool IntersectsPolygon(Node a, Node b)
            {
                // diagonal bbox; an edge whose bbox can't overlap it can't intersect it, so
                // skip the orientation test for those (the common case — the diagonal is short)
                double minX = Math.Min(a.x, b.x);
                double maxX = Math.Max(a.x, b.x);
                double minY = Math.Min(a.y, b.y);
                double maxY = Math.Max(a.y, b.y);

                var p = a;
                do
                {
                    var n = p.next;
                    if ((p.x > maxX && n.x > maxX) || (p.x < minX && n.x < minX) ||
                        (p.y > maxY && n.y > maxY) || (p.y < minY && n.y < minY))
                    {
                        p = n;
                        continue;
                    }
                    if (p.i != a.i && n.i != a.i && p.i != b.i && n.i != b.i &&
                        Intersects(p, n, a, b)) return true;
                    p = n;
                } while (p != a);

                return false;
            }

            // check if a polygon diagonal is locally inside the polygon
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static bool LocallyInside(Node a, Node b)
            {
                return Area(a.prev, a, a.next) < 0 ?
                    Area(a, b, a.next) >= 0 && Area(a, a.prev, b) >= 0 :
                    Area(a, b, a.prev) < 0 || Area(a, a.next, b) < 0;
            }

            // check if the middle point of a polygon diagonal is inside the polygon
            private static bool MiddleInside(Node a, Node b)
            {
                var p = a;
                bool inside = false;
                double px = (a.x + b.x) / 2;
                double py = (a.y + b.y) / 2;
                do
                {
                    var n = p.next;
                    if (((p.y > py) != (n.y > py)) && (px < (n.x - p.x) * (py - p.y) / (n.y - p.y) + p.x))
                        inside = !inside;
                    p = n;
                } while (p != a);

                return inside;
            }

            // link two polygon vertices with a bridge; if the vertices belong to the same ring, it splits polygon into two;
            // if one belongs to the outer ring and another to a hole, it merges it into a single ring
            private Node SplitPolygon(Node a, Node b)
            {
                var a2 = CreateNode(a.i, a.x, a.y);
                var b2 = CreateNode(b.i, b.x, b.y);
                var an = a.next;
                var bp = b.prev;

                a.next = b;
                b.prev = a;

                a2.next = an;
                an.prev = a2;

                b2.next = a2;
                a2.prev = b2;

                bp.next = b2;
                b2.prev = bp;

                return b2;
            }

            // create a node and optionally link it with previous one (in a circular doubly linked list)
            private Node InsertNode(int i, double x, double y, Node? last)
            {
                var p = CreateNode(i, x, y);

                if (last == null)
                {
                    p.prev = p;
                    p.next = p;
                }
                else
                {
                    p.next = last.next;
                    p.prev = last;
                    last.next.prev = p;
                    last.next = p;
                }
                return p;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void RemoveNode(Node p)
            {
                p.next.prev = p.prev;
                p.prev.next = p.next;

                if (p.prevZ != null) p.prevZ.nextZ = p.nextZ;
                if (p.nextZ != null) p.nextZ.prevZ = p.prevZ;

                // keep the hole-bridge index's block bboxes covering the healed prev->next edge
                if (_indexActive) GrowBlock(p.prev, p.next);
            }

            // Reusable scratch for Refine():
            //   _he        = twin half-edge of each edge, or -1 on the polygon boundary
            //   _hTable    = open-addressing hash, slot -> half-edge index, valid iff _hStamp[slot] == _gen
            //   _edgeStamp = pending-in-stack flag, cleared when the edge is popped
            private int[] _edgeStack = new int[0];
            private int[] _he = new int[0];
            private int[] _hTable = new int[0];
            private uint[] _hStamp = new uint[0];
            private byte[] _edgeStamp = new byte[0];
            private int _hMask;
            private uint _gen;

            // Refine a triangulation toward the constrained Delaunay triangulation by legalizing every
            // interior edge in place with Lawson flips. Adapted from delaunator's edge legalization.
            // Uses non-robust predicates: float input is fine, and the worst case is a not-quite-Delaunay
            // edge, never an invalid mesh.
            public void Refine(Span<int> t, ReadOnlySpan<double> coords, int dim)
            {
                int n = t.Length;
                if (n < 6) return;
                EnsureScratch(n);

                // bumping the generation logically empties the hash (no clearing)
                if (++_gen == 0)
                {
                    Array.Clear(_hStamp, 0, _hStamp.Length);
                    _gen = 1;
                }

                var he = _he;
                var hTable = _hTable;
                var hStamp = _hStamp;
                var edgeStamp = _edgeStamp;
                var edgeStack = _edgeStack;
                int hMask = _hMask;
                uint gen = _gen;

                he.AsSpan(0, n).Fill(-1);

                // Build half-edge twins with an undirected-edge hash; consumed slots mark linked pairs. As each
                // pair is linked we seed the stack with one representative (s, the earlier-inserted edge) — this
                // fuses the initial "push every interior edge" pass into the build, saving a full O(n) scan.
                // edgeStamp is all-zero here (balanced push/pop leaves it clean) and each pair links once, so
                // the seed write needs no dedup guard.
                int i = 0;
                for (int e = 0; e < n; e++)
                {
                    int a = t[e], b = t[NextHE(e)];
                    int lo = a < b ? a : b, hi = a < b ? b : a;
                    int h = (unchecked(lo * (int)0x9e3779b1) ^ unchecked(hi * (int)0x85ebca6b)) & hMask;
                    while (hStamp[h] == gen)
                    {
                        int s = hTable[h];
                        // s == -1 marks a consumed slot (a pair already linked) — skip past it
                        if (s != -1)
                        {
                            int sa = t[s], sb = t[NextHE(s)];
                            if ((sa == lo && sb == hi) || (sa == hi && sb == lo))
                            {
                                he[e] = s; he[s] = e; hTable[h] = -1; // link, then consume the slot
                                edgeStamp[s] = 1; edgeStack[i++] = s; // seed the interior edge for the cascade
                                break;
                            }
                        }
                        h = (h + 1) & hMask;
                    }
                    if (hStamp[h] != gen) { hTable[h] = e; hStamp[h] = gen; } // first occurrence: insert
                }

                while (i > 0)
                {
                    int a = edgeStack[--i];
                    edgeStamp[a] = 0;
                    int b = he[a];
                    if (b == -1) continue;

                    int a0 = a - a % 3;
                    int b0 = b - b % 3;
                    int ar = a0 + (a + 2) % 3;
                    int al = a0 + (a + 1) % 3;
                    int bl = b0 + (b + 2) % 3;
                    int br = b0 + (b + 1) % 3;
                    int p0 = t[ar], pr = t[a], pl = t[al], p1 = t[bl];

                    double x0 = coords[p0 * dim], y0 = coords[p0 * dim + 1];
                    double xr = coords[pr * dim], yr = coords[pr * dim + 1];
                    double xl = coords[pl * dim], yl = coords[pl * dim + 1];
                    double x1 = coords[p1 * dim], y1 = coords[p1 * dim + 1];

                    // Test inCircle first: most interior edges are already Delaunay (inCircle true → no flip),
                    // so this short-circuits before the two convexity orients on the common path. The quad must
                    // also be convex (both new triangles CCW) — flipping a reflex quad would push a triangle
                    // outside the polygon. Boundary/hole edges need no guard — they self-protect via he == -1.
                    if (!InCircle(x0, y0, xr, yr, xl, yl, x1, y1) &&
                        Orient(x0, y0, xr, yr, x1, y1) > 0 && Orient(x0, y0, x1, y1, xl, yl) > 0)
                    {
                        t[a] = p1; t[b] = p0;
                        int hbl = he[bl], har = he[ar];
                        he[a] = hbl; if (hbl != -1) he[hbl] = a;
                        he[b] = har; if (har != -1) he[har] = b;
                        he[ar] = bl; he[bl] = ar;

                        // re-check the quad's four outer edges; skip boundary edges (he == -1) and any
                        // already queued (edgeStamp), which also keeps the stack bounded by n.
                        if (hbl    != -1 && edgeStamp[a]  == 0) { edgeStamp[a]  = 1; edgeStack[i++] = a; }
                        if (har    != -1 && edgeStamp[b]  == 0) { edgeStamp[b]  = 1; edgeStack[i++] = b; }
                        if (he[al] != -1 && edgeStamp[al] == 0) { edgeStamp[al] = 1; edgeStack[i++] = al; }
                        if (he[br] != -1 && edgeStamp[br] == 0) { edgeStamp[br] = 1; edgeStack[i++] = br; }
                    }
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static double Orient(double ax, double ay, double bx, double by, double cx, double cy)
            {
                return (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
            }

            // Whether p is inside or exactly on the circumcircle of triangle (a, b, c). Sign is negated vs the
            // usual predicate to match earcut's CCW winding — the standard sign would build the anti-Delaunay
            // mesh. Cocircular quads are legal ties, so Refine only flips when this returns false.
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static bool InCircle(double ax, double ay, double bx, double by, double cx, double cy, double px, double py)
            {
                double dx = ax - px, dy = ay - py, ex = bx - px, ey = by - py, fx = cx - px, fy = cy - py;
                double ap = dx * dx + dy * dy, bp = ex * ex + ey * ey, cp = fx * fx + fy * fy;
                // A near-cocircular quad is a legal Delaunay tie, but roundoff can flag both an edge and its
                // flip as illegal, cascading into an endless flip loop (mapbox/earcut#205) — so treat a
                // determinant within a small margin of zero as a tie. The determinant's worst-case roundoff
                // error is provably below 9e-16·(ap + bp + cp)² (Shewchuk-style bound), so the margin
                // guarantees every executed flip is illegal in exact arithmetic, and Lawson flipping always terminates.
                double s = ap + bp + cp;
                return dx * (ey * cp - bp * fy) - dy * (ex * cp - bp * fx) + ap * (ex * fy - ey * fx) <= 1e-13 * s * s;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static int NextHE(int e) // next half-edge within the same triangle
            {
                return e - e % 3 + (e + 1) % 3;
            }

            private void EnsureScratch(int n)
            {
                // edgeStack holds at most one entry per half-edge (edgeStamp dedups), so n is a safe cap —
                // sizing it up front lets the cascade push without a bounds/grow check.
                if (_edgeStack.Length < n) _edgeStack = new int[n];
                if (_he.Length < n) _he = new int[n];
                if (_edgeStamp.Length < n) _edgeStamp = new byte[n];
                int size = 1;
                while (size < n * 4) size <<= 1; // power-of-two table, load factor <= 0.25
                if (_hTable.Length < size)
                {
                    _hTable = new int[size];
                    _hStamp = new uint[size];
                    _gen = 0;
                }
                _hMask = size - 1;
            }
        }

        private sealed class Node
        {
            public int i;      // vertex index in coordinates array
            public double x;   // vertex coordinates
            public double y;

            public int z;      // z-order curve value; doubles as owning block in the hole-bridge index during EliminateHoles

            public Node prev = null!; // previous and next vertex nodes in a polygon ring
            public Node next = null!;

            public Node? prevZ; // previous and next nodes in z-order
            public Node? nextZ;

            public bool steiner; // single-vertex hole to preserve through FilterPoints

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Reset(int i, double x, double y)
            {
                this.i = i;
                this.x = x;
                this.y = y;
                this.z = 0;
                this.prevZ = null;
                this.nextZ = null;
                this.steiner = false;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void ClearReferences()
            {
                this.prev = null!;
                this.next = null!;
                this.prevZ = null;
                this.nextZ = null;
            }
        }
    }
}
