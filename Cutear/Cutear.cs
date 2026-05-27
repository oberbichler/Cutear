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

    public class Cutear
    {
        [ThreadStatic]
        private static List<Node>? _nodeCache;
        [ThreadStatic]
        private static int _nodeCacheIndex;

        [ThreadStatic]
        private static List<Node>? _queueCache;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Node CreateNode(int i, double x, double y)
        {
            _nodeCache ??= new List<Node>();

            if (_nodeCacheIndex < _nodeCache.Count)
            {
                var node = _nodeCache[_nodeCacheIndex];
                node.Reset(i, x, y);
                _nodeCacheIndex++;
                return node;
            }
            else
            {
                var node = new Node(i, x, y);
                _nodeCache.Add(node);
                _nodeCacheIndex++;
                return node;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ClearCacheReferences()
        {
            if (_nodeCache != null)
            {
                for (int i = 0; i < _nodeCacheIndex; i++)
                {
                    _nodeCache[i].ClearReferences();
                }
            }
            _nodeCacheIndex = 0;

            if (_queueCache != null)
            {
                _queueCache.Clear();
            }
        }

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

        public static List<int> Tessellate(ReadOnlySpan<double> data, ReadOnlySpan<int> holeIndices, int dim = 2)
        {
            var hasHoles = holeIndices.Length > 0;
            var outerLen = hasHoles ? holeIndices[0] * dim : data.Length;
            var outerNode = LinkedList(data, 0, outerLen, dim, true);
            
            int estimatedIndices = Math.Max(0, 3 * (data.Length / dim - 2));
            var triangles = new List<int>(estimatedIndices);

            if (outerNode == null || outerNode.next == outerNode)
            {
                return triangles;
            }

            try
            {
                double minX = default;
                double minY = default;
                double invSize = default;

                if (hasHoles)
                {
                    outerNode = EliminateHoles(data, holeIndices, outerNode, dim);
                }

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

                    invSize = Math.Max(maxX - minX, maxY - minY);
                    invSize = invSize != 0 ? 1.0 / invSize : 0;
                }

                EarcutLinked(outerNode, triangles, dim, minX, minY, invSize, 0);

                return triangles;
            }
            finally
            {
                ClearCacheReferences();
            }
        }

        private static Node? LinkedList(ReadOnlySpan<double> data, int start, int end, int dim, bool clockwise)
        {
            Node? last = null;

            if (clockwise == (SignedArea(data, start, end, dim) > 0))
            {
                for (int i = start; i < end; i += dim)
                {
                    last = InsertNode(i / dim, data[i], data[i + 1], last);
                }
            }
            else
            {
                for (int i = end - dim; i >= start; i -= dim)
                {
                    last = InsertNode(i / dim, data[i], data[i + 1], last);
                }
            }

            if (last != null && Equals(last, last.next!))
            {
                RemoveNode(last);
                last = last.next;
            }

            return last;
        }

        private static Node? FilterPoints(Node? start, Node? end = null)
        {
            if (start == null)
            {
                return start;
            }

            if (end == null)
            {
                end = start;
            }

            var p = start;
            bool again;
            int loopCount = 0;

            do
            {
                if (loopCount++ > 1000000)
                {
                    break;
                }
                again = false;

                if (!p.steiner && (Equals(p, p.next!) || Area(p.prev!, p, p.next!) == 0))
                {
                    RemoveNode(p);
                    p = end = p.prev!;
                    if (p == p.next)
                    {
                        break;
                    }

                    again = true;
                }
                else
                {
                    p = p.next!;
                }
            } while (again || p != end);

            return end;
        }

        private static void EarcutLinked(Node? ear, List<int> triangles, int dim, double minX, double minY, double invSize, int pass, int depth = 0)
        {
            if (ear == null || depth > 1000)
            {
                return;
            }

            if (pass == 0 && invSize != 0)
            {
                IndexCurve(ear, minX, minY, invSize);
            }

            var stop = ear;
            int loopCount = 0;

            while (ear.prev != ear.next)
            {
                if (loopCount++ > 1000000)
                {
                    break;
                }
                var prev = ear.prev!;
                var next = ear.next!;

                if (invSize != 0 ? IsEarHashed(ear, minX, minY, invSize) : IsEar(ear))
                {
                    triangles.Add(prev.i);
                    triangles.Add(ear.i);
                    triangles.Add(next.i);

                    RemoveNode(ear);

                    ear = next.next!;
                    stop = next.next;

                    continue;
                }

                ear = next;

                if (ear == stop)
                {
                    if (pass == 0)
                    {
                        EarcutLinked(FilterPoints(ear), triangles, dim, minX, minY, invSize, 1, depth + 1);
                    }
                    else if (pass == 1)
                    {
                        ear = CureLocalIntersections(FilterPoints(ear)!, triangles);
                        EarcutLinked(ear, triangles, dim, minX, minY, invSize, 2, depth + 1);
                    }
                    else if (pass == 2)
                    {
                        SplitEarcut(ear, triangles, dim, minX, minY, invSize, depth + 1);
                    }

                    break;
                }
            }
        }

        private static bool IsEar(Node ear)
        {
            var a = ear.prev!;
            var b = ear;
            var c = ear.next!;

            if (Area(a, b, c) >= 0)
            {
                return false;
            }

            var p = ear.next!.next!;

            while (p != ear.prev)
            {
                if (PointInTriangle(a.x, a.y, b.x, b.y, c.x, c.y, p.x, p.y) &&
                    Area(p.prev!, p, p.next!) >= 0)
                {
                    return false;
                }

                p = p.next!;
            }

            return true;
        }

        private static bool IsEarHashed(Node ear, double minX, double minY, double invSize)
        {
            var a = ear.prev!;
            var b = ear;
            var c = ear.next!;

            if (Area(a, b, c) >= 0)
            {
                return false;
            }

            var minTX = a.x < b.x ? (a.x < c.x ? a.x : c.x) : (b.x < c.x ? b.x : c.x);
            var minTY = a.y < b.y ? (a.y < c.y ? a.y : c.y) : (b.y < c.y ? b.y : c.y);
            var maxTX = a.x > b.x ? (a.x > c.x ? a.x : c.x) : (b.x > c.x ? b.x : c.x);
            var maxTY = a.y > b.y ? (a.y > c.y ? a.y : c.y) : (b.y > c.y ? b.y : c.y);

            var minZ = ZOrder(minTX, minTY, minX, minY, invSize);
            var maxZ = ZOrder(maxTX, maxTY, minX, minY, invSize);

            var p = ear.prevZ;
            var n = ear.nextZ;

            while (p != null && p.z >= minZ && n != null && n.z <= maxZ)
            {
                if (p != ear.prev && p != ear.next &&
                    PointInTriangle(a.x, a.y, b.x, b.y, c.x, c.y, p.x, p.y) &&
                    Area(p.prev!, p, p.next!) >= 0)
                {
                    return false;
                }

                p = p.prevZ;

                if (n != ear.prev && n != ear.next &&
                    PointInTriangle(a.x, a.y, b.x, b.y, c.x, c.y, n.x, n.y) &&
                    Area(n.prev!, n, n.next!) >= 0)
                {
                    return false;
                }

                n = n.nextZ;
            }

            while (p != null && p.z >= minZ)
            {
                if (p != ear.prev && p != ear.next &&
                    PointInTriangle(a.x, a.y, b.x, b.y, c.x, c.y, p.x, p.y) &&
                    Area(p.prev!, p, p.next!) >= 0)
                {
                    return false;
                }

                p = p.prevZ;
            }

            while (n != null && n.z <= maxZ)
            {
                if (n != ear.prev && n != ear.next &&
                    PointInTriangle(a.x, a.y, b.x, b.y, c.x, c.y, n.x, n.y) &&
                    Area(n.prev!, n, n.next!) >= 0)
                {
                    return false;
                }

                n = n.nextZ;
            }

            return true;
        }

        private static Node CureLocalIntersections(Node start, List<int> triangles)
        {
            var p = start;
            int loopCount = 0;
            do
            {
                if (loopCount++ > 1000000)
                {
                    break;
                }
                var a = p.prev!;
                var b = p.next!.next!;

                if (!Equals(a, b) && Intersects(a, p, p.next!, b) && LocallyInside(a, b) && LocallyInside(b, a))
                {
                    triangles.Add(a.i);
                    triangles.Add(p.i);
                    triangles.Add(b.i);

                    RemoveNode(p);
                    RemoveNode(p.next!);

                    p = start = b;
                }
                p = p.next!;
            } while (p != start);

            return FilterPoints(p)!;
        }

        private static void SplitEarcut(Node start, List<int> triangles, int dim, double minX, double minY, double invSize, int depth)
        {
            var a = start;
            int outerLoopCount = 0;
            do
            {
                if (outerLoopCount++ > 1000000)
                {
                    break;
                }
                var b = a.next!.next!;
                int innerLoopCount = 0;
                while (b != a.prev)
                {
                    if (innerLoopCount++ > 1000000)
                    {
                        break;
                    }
                    if (a.i != b.i && IsValidDiagonal(a, b))
                    {
                        var c = SplitPolygon(a, b);

                        a = FilterPoints(a, a.next)!;
                        c = FilterPoints(c, c.next)!;

                        EarcutLinked(a, triangles, dim, minX, minY, invSize, 0, depth + 1);
                        EarcutLinked(c, triangles, dim, minX, minY, invSize, 0, depth + 1);
                        return;
                    }
                    b = b.next!;
                }
                a = a.next!;
            } while (a != start);
        }

        private static Node EliminateHoles(ReadOnlySpan<double> data, ReadOnlySpan<int> holeIndices, Node outerNode, int dim)
        {
            _queueCache ??= new List<Node>();
            _queueCache.Clear();

            var len = holeIndices.Length;

            for (var i = 0; i < len; i++)
            {
                var start = holeIndices[i] * dim;
                var end = i < len - 1 ? holeIndices[i + 1] * dim : data.Length;
                var list = LinkedList(data, start, end, dim, false);
                if (list != null)
                {
                    if (list == list.next)
                    {
                        list.steiner = true;
                    }
                    _queueCache.Add(GetLeftmost(list));
                }
            }

            _queueCache.Sort(CompareXYSlope);

            for (var i = 0; i < _queueCache.Count; i++)
            {
                outerNode = EliminateHole(_queueCache[i], outerNode);
            }

            _queueCache.Clear();
            return outerNode;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CompareXYSlope(Node a, Node b)
        {
            int res = a.x.CompareTo(b.x);
            if (res == 0)
            {
                res = a.y.CompareTo(b.y);
                if (res == 0)
                {
                    double aSlope = (a.next!.y - a.y) / (a.next.x - a.x);
                    double bSlope = (b.next!.y - b.y) / (b.next.x - b.x);
                    res = aSlope.CompareTo(bSlope);
                }
            }
            return res;
        }

        private static Node EliminateHole(Node hole, Node outerNode)
        {
            var bridge = FindHoleBridge(hole, outerNode);
            if (bridge == null)
            {
                return outerNode;
            }

            var bridgeReverse = SplitPolygon(bridge, hole);

            FilterPoints(bridgeReverse, bridgeReverse.next);
            return FilterPoints(bridge, bridge.next)!;
        }

        private static Node? FindHoleBridge(Node hole, Node outerNode)
        {
            var p = outerNode;
            double hx = hole.x;
            double hy = hole.y;
            double qx = double.NegativeInfinity;
            Node? m = null;

            if (Equals(hole, p)) return p;
            int loopCount1 = 0;
            do
            {
                if (loopCount1++ > 1000000)
                {
                    break;
                }
                if (Equals(hole, p.next!)) return p.next;
                if (hy <= p.y && hy >= p.next!.y && p.next.y != p.y)
                {
                    double x = p.x + (hy - p.y) * (p.next.x - p.x) / (p.next.y - p.y);
                    if (x <= hx && x > qx)
                    {
                        qx = x;
                        m = p.x < p.next.x ? p : p.next;
                        if (x == hx) return m;
                    }
                }
                p = p.next!;
            } while (p != outerNode);

            if (m == null)
            {
                return null;
            }

            var stop = m;
            double mx = m.x;
            double my = m.y;
            double tanMin = double.PositiveInfinity;

            p = m;
            int loopCount2 = 0;

            do
            {
                if (loopCount2++ > 1000000)
                {
                    break;
                }
                if (hx >= p.x && p.x >= mx && hx != p.x &&
                    PointInTriangle(hy < my ? hx : qx, hy, mx, my, hy < my ? qx : hx, hy, p.x, p.y))
                {
                    double tan = Math.Abs(hy - p.y) / (hx - p.x);

                    if (LocallyInside(p, hole) &&
                        (tan < tanMin || (tan == tanMin && (p.x > m.x || (p.x == m.x && SectorContainsSector(m, p))))))
                    {
                        m = p;
                        tanMin = tan;
                    }
                }

                p = p.next!;
            } while (p != stop);

            return m;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool SectorContainsSector(Node m, Node p)
        {
            return Area(m.prev!, m, p.prev!) < 0 && Area(p.next!, m, m.next!) < 0;
        }

        private static void IndexCurve(Node start, double minX, double minY, double invSize)
        {
            var p = start;
            do
            {
                if (p.z == null)
                {
                    p.z = ZOrder(p.x, p.y, minX, minY, invSize);
                }

                p.prevZ = p.prev;
                p.nextZ = p.next;
                p = p.next!;
            } while (p != start);

            p.prevZ!.nextZ = null;
            p.prevZ = null;

            SortLinked(p);
        }

        private static Node SortLinked(Node list)
        {
            int numMerges;
            int inSize = 1;

            do
            {
                var p = list;
                Node? e;
                list = null!;
                Node? tail = null;
                numMerges = 0;

                while (p != null)
                {
                    numMerges++;
                    var q = p;
                    int pSize = 0;
                    for (int i = 0; i < inSize; i++)
                    {
                        pSize++;
                        q = q.nextZ;
                        if (q == null)
                        {
                            break;
                        }
                    }
                    int qSize = inSize;

                    while (pSize > 0 || (qSize > 0 && q != null))
                    {
                        if (pSize != 0 && (qSize == 0 || q == null || p.z <= q.z))
                        {
                            e = p;
                            p = p.nextZ;
                            pSize--;
                        }
                        else
                        {
                            e = q;
                            q = q!.nextZ;
                            qSize--;
                        }

                        if (tail != null)
                        {
                            tail.nextZ = e;
                        }
                        else
                        {
                            list = e!;
                        }

                        e!.prevZ = tail;
                        tail = e;
                    }

                    p = q;
                }

                tail!.nextZ = null;
                inSize *= 2;

            } while (numMerges > 1);

            return list;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ZOrder(double x, double y, double minX, double minY, double invSize)
        {
            int intX = (int)(32767 * (x - minX) * invSize);
            int intY = (int)(32767 * (y - minY) * invSize);

            intX = (intX | (intX << 8)) & 0x00FF00FF;
            intX = (intX | (intX << 4)) & 0x0F0F0F0F;
            intX = (intX | (intX << 2)) & 0x33333333;
            intX = (intX | (intX << 1)) & 0x55555555;

            intY = (intY | (intY << 8)) & 0x00FF00FF;
            intY = (intY | (intY << 4)) & 0x0F0F0F0F;
            intY = (intY | (intY << 2)) & 0x33333333;
            intY = (intY | (intY << 1)) & 0x55555555;

            return intX | (intY << 1);
        }

        private static Node GetLeftmost(Node start)
        {
            var p = start;
            var leftmost = start;
            do
            {
                if (p.x < leftmost.x || (p.x == leftmost.x && p.y < leftmost.y))
                {
                    leftmost = p;
                }

                p = p.next!;
            } while (p != start);

            return leftmost;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool PointInTriangle(double ax, double ay, double bx, double by, double cx, double cy, double px, double py)
        {
            return (cx - px) * (ay - py) - (ax - px) * (cy - py) >= 0 &&
                   (ax - px) * (by - py) - (bx - px) * (ay - py) >= 0 &&
                   (bx - px) * (cy - py) - (cx - px) * (by - py) >= 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsValidDiagonal(Node a, Node b)
        {
            return a.next!.i != b.i && a.prev!.i != b.i && !IntersectsPolygon(a, b) &&
                   ((LocallyInside(a, b) && LocallyInside(b, a) && MiddleInside(a, b) &&
                     (Area(a.prev!, a, b.prev!) != 0 || Area(a, b.prev!, b) != 0)) ||
                    (Equals(a, b) && Area(a.prev!, a, a.next!) > 0 && Area(b.prev!, b, b.next!) > 0));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double Area(Node p, Node q, Node r)
        {
            return (q.y - p.y) * (r.x - q.x) - (q.x - p.x) * (r.y - q.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Equals(Node p1, Node p2)
        {
            return p1.x == p2.x && p1.y == p2.y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Intersects(Node p1, Node q1, Node p2, Node q2)
        {
            if ((Equals(p1, q1) && Equals(p2, q2)) ||
                (Equals(p1, q2) && Equals(p2, q1)))
            {
                return true;
            }

            return Area(p1, q1, p2) > 0 != Area(p1, q1, q2) > 0 &&
                   Area(p2, q2, p1) > 0 != Area(p2, q2, q1) > 0;
        }

        private static bool IntersectsPolygon(Node a, Node b)
        {
            var p = a;
            do
            {
                if (p.i != a.i && p.next!.i != a.i && p.i != b.i && p.next.i != b.i &&
                    Intersects(p, p.next, a, b))
                {
                    return true;
                }

                p = p.next!;
            } while (p != a);

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool LocallyInside(Node a, Node b)
        {
            return Area(a.prev!, a, a.next!) < 0 ?
                Area(a, b, a.next!) >= 0 && Area(a, a.prev!, b) >= 0 :
                Area(a, b, a.prev!) < 0 || Area(a, a.next!, b) < 0;
        }

        private static bool MiddleInside(Node a, Node b)
        {
            var p = a;
            var inside = false;
            double px = (a.x + b.x) / 2;
            double py = (a.y + b.y) / 2;
            do
            {
                if (((p.y > py) != (p.next!.y > py)) && p.next.y != p.y &&
                    (px < (p.next.x - p.x) * (py - p.y) / (p.next.y - p.y) + p.x))
                {
                    inside = !inside;
                }

                p = p.next!;
            } while (p != a);

            return inside;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Node SplitPolygon(Node a, Node b)
        {
            var a2 = CreateNode(a.i, a.x, a.y);
            var b2 = CreateNode(b.i, b.x, b.y);
            var an = a.next!;
            var bp = b.prev!;

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Node InsertNode(int i, double x, double y, Node? last)
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
                last.next!.prev = p;
                last.next = p;
            }
            return p;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void RemoveNode(Node p)
        {
            p.next!.prev = p.prev;
            p.prev!.next = p.next;

            if (p.prevZ != null)
            {
                p.prevZ.nextZ = p.nextZ;
            }

            if (p.nextZ != null)
            {
                p.nextZ.prevZ = p.prevZ;
            }
        }

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

            return polygonArea == 0 && trianglesArea == 0 ? 0 :
                Math.Abs((trianglesArea - polygonArea) / polygonArea);
        }

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

        private sealed class Node
        {
            public int i;
            public double x;
            public double y;

            public int? z;

            public Node? prev;
            public Node? next;

            public Node? prevZ;
            public Node? nextZ;

            public bool steiner;

            public Node(int i, double x, double y)
            {
                this.i = i;
                this.x = x;
                this.y = y;
                this.z = null;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Reset(int i, double x, double y)
            {
                this.i = i;
                this.x = x;
                this.y = y;
                this.prev = null;
                this.next = null;
                this.z = null;
                this.prevZ = null;
                this.nextZ = null;
                this.steiner = false;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void ClearReferences()
            {
                this.prev = null;
                this.next = null;
                this.prevZ = null;
                this.nextZ = null;
            }
        }
    }
}
