using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;

namespace Cutear.Tests;

[TestFixture]
public class EarcutTests
{
    private static TestCaseData LoadTestCaseData(string filename, int expectedTriangles, double expectedDeviation = 0, double? expectedDeviationWithRotation = null)
    {
        return new TestCaseData(filename, expectedTriangles, expectedDeviation, expectedDeviationWithRotation ?? expectedDeviation).SetName(filename);
    }

    // mirrors test/expected.json of mapbox/earcut v3.2.4
    private static IEnumerable<TestCaseData> TestCases()
    {
        yield return LoadTestCaseData("earcut", 548);
        yield return LoadTestCaseData("building", 13);
        yield return LoadTestCaseData("dude", 106, 2e-15);
        yield return LoadTestCaseData("water", 2482, 0.0009);
        yield return LoadTestCaseData("water2", 1211);
        yield return LoadTestCaseData("water3", 197);
        yield return LoadTestCaseData("water3b", 25);
        yield return LoadTestCaseData("water4", 705);
        yield return LoadTestCaseData("water-huge", 5174, 0.0018, 0.005);
        yield return LoadTestCaseData("water-huge2", 4484, 0.003, 0.03);
        yield return LoadTestCaseData("water-huge3", 15470);
        yield return LoadTestCaseData("self-tangent-1", 23);
        yield return LoadTestCaseData("self-tangent-2", 25);
        yield return LoadTestCaseData("self-tangent-3", 174);
        yield return LoadTestCaseData("self-tangent-4", 3082);
        yield return LoadTestCaseData("degenerate", 0);
        yield return LoadTestCaseData("bad-hole", 42, 0.019, 0.03);
        yield return LoadTestCaseData("empty-square", 0);
        yield return LoadTestCaseData("issue16", 12, 4e-16, 1e-15);
        yield return LoadTestCaseData("issue17", 11, 2e-16);
        yield return LoadTestCaseData("steiner", 9);
        yield return LoadTestCaseData("issue29", 40, 1e-15);
        yield return LoadTestCaseData("issue34", 138);
        yield return LoadTestCaseData("issue35", 844);
        yield return LoadTestCaseData("self-touching", 124, 1e-13);
        yield return LoadTestCaseData("outside-ring", 64);
        yield return LoadTestCaseData("simplified-us-border", 120);
        yield return LoadTestCaseData("touching-holes", 57);
        yield return LoadTestCaseData("touching-holes2", 10);
        yield return LoadTestCaseData("touching-holes3", 82);
        yield return LoadTestCaseData("touching-holes4", 55);
        yield return LoadTestCaseData("touching-holes5", 133);
        yield return LoadTestCaseData("touching-holes6", 3098);
        yield return LoadTestCaseData("hole-touching-outer", 77);
        yield return LoadTestCaseData("hilbert", 1024);
        yield return LoadTestCaseData("issue45", 10);
        yield return LoadTestCaseData("eberly-3", 73);
        yield return LoadTestCaseData("eberly-6", 1428, 2e-15, 3e-14);
        yield return LoadTestCaseData("issue52", 108);
        yield return LoadTestCaseData("shared-points", 4);
        yield return LoadTestCaseData("bad-diagonals", 7);
        yield return LoadTestCaseData("issue83", 0);
        yield return LoadTestCaseData("issue107", 0);
        yield return LoadTestCaseData("issue111", 18);
        yield return LoadTestCaseData("boxy", 58);
        yield return LoadTestCaseData("collinear-diagonal", 14);
        yield return LoadTestCaseData("issue119", 18);
        yield return LoadTestCaseData("hourglass", 2);
        yield return LoadTestCaseData("touching2", 8);
        yield return LoadTestCaseData("touching3", 15);
        yield return LoadTestCaseData("touching4", 19);
        yield return LoadTestCaseData("rain", 2681);
        yield return LoadTestCaseData("issue131", 12);
        yield return LoadTestCaseData("infinite-loop-jhl", 0);
        yield return LoadTestCaseData("filtered-bridge-jhl", 25);
        yield return LoadTestCaseData("issue149", 2);
        yield return LoadTestCaseData("issue142", 4, 0.13);
        yield return LoadTestCaseData("issue186", 41);
        yield return LoadTestCaseData("issue147", 30);
    }

    private static double[][][] LoadFixture(string filename)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", $"{filename}.json");
        return JsonSerializer.Deserialize<double[][][]>(File.ReadAllText(path))
            ?? throw new InvalidOperationException($"Failed to deserialize fixture '{filename}'");
    }

    private static double[][][] Rotate(double[][][] rings, int rotation)
    {
        var theta = rotation * Math.PI / 180.0;
        double xx = Math.Round(Math.Cos(theta)), xy = Math.Round(-Math.Sin(theta));
        double yx = Math.Round(Math.Sin(theta)), yy = Math.Round(Math.Cos(theta));
        return rings
            .Select(ring => ring.Select(p => new[] { xx * p[0] + xy * p[1], yx * p[0] + yy * p[1] }).ToArray())
            .ToArray();
    }

    [TestCaseSource(nameof(TestCases))]
    public void AreaTest(string filename, int expectedTriangles, double expectedDeviation, double expectedDeviationWithRotation)
    {
        var polylines = LoadFixture(filename);

        foreach (var rotation in new[] { 0, 90, 180, 270 })
        {
            var (vertices, holes, dimensions) = Cutear.Flatten(rotation == 0 ? polylines : Rotate(polylines, rotation));

            var triangles = Cutear.Tessellate(vertices, holes, dimensions);

            if (rotation == 0)
            {
                Assert.That(triangles.Count / 3, Is.EqualTo(expectedTriangles), $"Rotation {rotation}: triangle count");
            }

            if (expectedTriangles > 0)
            {
                var actualDeviation = Cutear.Deviation(vertices, holes, triangles.ToArray(), dimensions);
                var allowedDeviation = rotation != 0 ? expectedDeviationWithRotation : expectedDeviation;
                Assert.That(actualDeviation, Is.LessThanOrEqualTo(allowedDeviation), $"Rotation {rotation}: deviation");
            }
        }
    }

    [Test]
    public void GenericPolygonTest()
    {
        // Create a square polygon with a hole
        var outerRing = new[]
        {
            new Point(0, 0),
            new Point(100, 0),
            new Point(100, 100),
            new Point(0, 100)
        };
        var holeRing = new[]
        {
            new Point(20, 20),
            new Point(80, 20),
            new Point(80, 80),
            new Point(20, 80)
        };

        var rings = new[] { outerRing, holeRing };
        var polygon = new Polygon(rings);

        var triangles = Cutear.Tessellate(polygon);

        Assert.That(triangles, Is.EqualTo(new[] { 0, 4, 7, 5, 4, 0, 5, 0, 1, 5, 1, 2, 3, 0, 7, 3, 7, 6, 6, 5, 2, 6, 2, 3 }));
    }

    [Test]
    public void Generic3DPolygonTest()
    {
        // Create a 3D polygon
        var outerRing = new[]
        {
            new Point(10, 0, 1.5),
            new Point(0, 50, 2.5),
            new Point(60, 60, 3.5),
            new Point(70, 10, 4.5)
        };

        var polygon = new Polygon(new[] { outerRing });

        var triangles = Cutear.Tessellate(polygon, dim: 3);

        Assert.That(triangles, Is.EqualTo(new[] { 1, 0, 3, 1, 3, 2 }));
    }

    [Test]
    public void Indices2D()
    {
        var indices = Cutear.Tessellate(new double[] { 10, 0, 0, 50, 60, 60, 70, 10 }, Array.Empty<int>());
        Assert.That(indices, Is.EqualTo(new[] { 1, 0, 3, 1, 3, 2 }));
    }

    [Test]
    public void Indices3D()
    {
        var indices = Cutear.Tessellate(new double[] { 10, 0, 0, 0, 50, 0, 60, 60, 0, 70, 10, 0 }, Array.Empty<int>(), 3);
        Assert.That(indices, Is.EqualTo(new[] { 1, 0, 3, 1, 3, 2 }));
    }

    [Test]
    public void Empty()
    {
        var indices = Cutear.Tessellate(Array.Empty<double>(), Array.Empty<int>());
        Assert.That(indices, Is.Empty);
    }

    [Test]
    public void InfiniteLoop()
    {
        var indices = Cutear.Tessellate(new double[] { 1, 2, 2, 2, 1, 2, 1, 1, 1, 2, 4, 1, 5, 1, 3, 2, 4, 2, 4, 1 }, new[] { 5 }, 2);
        Assert.That(indices, Is.Not.Null);
    }

    [Test]
    public void CollinearPolygonHasZeroDeviation()
    {
        var vertices = new[] { 0.1, 0.2, 1.3, 2.6, 2.5, 5.0, 3.7, 7.4 }; // on the line y = 2x
        var triangles = Cutear.Tessellate(vertices, Array.Empty<int>());
        Assert.That(triangles, Is.Empty);
        Assert.That(Cutear.Deviation(vertices, Array.Empty<int>(), triangles.ToArray()), Is.EqualTo(0));
    }

    [Test]
    public void RefineImprovesBadQuadDiagonal()
    {
        var vertices = new double[] { 0, 0, 3, 0, 10, 1, 0, 2 };
        var triangles = new[] { 2, 3, 0, 2, 0, 1 };
        var beforePerimeter = TrianglePerimeter(triangles, vertices);
        Cutear.Refine(triangles, vertices);
        var afterPerimeter = TrianglePerimeter(triangles, vertices);

        Assert.That(triangles, Is.EqualTo(new[] { 2, 3, 1, 3, 0, 1 }));
        Assert.That(afterPerimeter, Is.LessThan(beforePerimeter * 0.7));
        Assert.That(Cutear.Deviation(vertices, Array.Empty<int>(), triangles), Is.EqualTo(0));
    }

    [Test]
    public void RefineLeavesGoodQuadDiagonalAlone()
    {
        var vertices = new double[] { 0, 0, 5, 0, 4, 1, 0, 4 };
        var triangles = new[] { 2, 3, 0, 2, 0, 1 };

        Cutear.Refine(triangles, vertices);

        Assert.That(triangles, Is.EqualTo(new[] { 2, 3, 0, 2, 0, 1 }));
        Assert.That(Cutear.Deviation(vertices, Array.Empty<int>(), triangles), Is.EqualTo(0));
    }

    [Test]
    public void RefinePreservesConcavePolygon()
    {
        var vertices = new double[] { 0, 0, 4, 0, 4, 1, 1, 1, 1, 4, 0, 4 };
        var triangles = Cutear.Tessellate(vertices, Array.Empty<int>());
        var length = triangles.Count;
        var beforePerimeter = TrianglePerimeter(triangles, vertices);
        Cutear.Refine(triangles, vertices);
        var afterPerimeter = TrianglePerimeter(triangles, vertices);

        Assert.That(triangles.Count, Is.EqualTo(length));
        Assert.That(afterPerimeter, Is.LessThan(beforePerimeter * 0.9));
        Assert.That(Cutear.Deviation(vertices, Array.Empty<int>(), triangles.ToArray()), Is.EqualTo(0));
    }

    [Test, CancelAfter(5000)]
    public void RefineTerminatesOnNearCocircularPoints()
    {
        // Four near-cocircular points make the non-robust inCircle test give inconsistent signs for an
        // edge and its flip; without the tie margin the Lawson cascade flips one edge back and forth
        // forever. Should return with a valid mesh of unchanged size and zero deviation.
        var vertices = new[]
        {
            127.65906365022843, 9.336137742499535, 124.21725103117963, 30.888097161477972,
            91.35514946628345, 89.65621376119454, 40.10446780041529, 121.5550560957686,
            -110.83205604043928, 64.03323632184248, -127.20394987965459, -14.253249980770189,
            61.074962259031416, -112.48932831632469, 127.37846573978545, -12.598669206638515,
            127.77010311801033, -7.668164657400608
        };
        var triangles = Cutear.Tessellate(vertices, Array.Empty<int>());
        var length = triangles.Count;
        Cutear.Refine(triangles, vertices);
        Assert.That(triangles.Count, Is.EqualTo(length));
        Assert.That(Cutear.Deviation(vertices, Array.Empty<int>(), triangles.ToArray()), Is.LessThan(1e-15));
    }

    [Test]
    public void RefineLegalizesAllConvexInteriorEdges()
    {
        var (vertices, holes, dimensions) = Cutear.Flatten(LoadFixture("earcut"));
        var triangles = Cutear.Tessellate(vertices, holes, dimensions);

        Cutear.Refine(triangles, vertices, dimensions);

        Assert.That(CountIllegalEdges(triangles, vertices, dimensions), Is.EqualTo(0));
        Assert.That(Cutear.Deviation(vertices, holes, triangles.ToArray(), dimensions), Is.EqualTo(0));
    }

    [Test]
    public void MvtFixtureHasZeroDeviationAndRefinedQuality()
    {
        var polys = TilesFixture.Read(Path.Combine(AppContext.BaseDirectory, "fixtures", "tiles-fixture.bin"));

        int nonzero = 0, refinedNonzero = 0, lengthChanged = 0;
        double basePerimeter = 0, refinedPerimeter = 0;

        foreach (var (vertices, holes, dimensions) in polys)
        {
            var triangles = Cutear.Tessellate(vertices, holes, dimensions);
            var length = triangles.Count;
            basePerimeter += TrianglePerimeter(triangles, vertices, dimensions);
            if (Cutear.Deviation(vertices, holes, triangles.ToArray(), dimensions) != 0) nonzero++;

            Cutear.Refine(triangles, vertices, dimensions);
            refinedPerimeter += TrianglePerimeter(triangles, vertices, dimensions);
            if (triangles.Count != length) lengthChanged++;
            if (Cutear.Deviation(vertices, holes, triangles.ToArray(), dimensions) != 0) refinedNonzero++;
        }

        Assert.That(polys.Count, Is.EqualTo(119680));
        Assert.That(nonzero, Is.EqualTo(0), "polygons with nonzero deviation");
        Assert.That(lengthChanged, Is.EqualTo(0), "refined triangulations changed triangle count");
        Assert.That(refinedNonzero, Is.EqualTo(0), "refined polygons with nonzero deviation");
        Assert.That(refinedPerimeter, Is.LessThan(basePerimeter * 0.72));
    }

    // Regression for the hole-bridge block index (mapbox/earcut#183): a collinear-rich outer ring
    // (integer grid, like MVT data) plus multiple holes used to drop a hole when filterPoints
    // healed a collinear run across a block boundary, leaving the surviving edge outside its
    // block's stale bbox so the leftward-ray scan false-skipped it. Assert full coverage.
    [Test]
    public void BlockIndexCollinear()
    {
        const int N = 30;
        var outer = new List<double[]>();
        for (int x = 0; x <= N; x++) outer.Add(new double[] { x, 0 });
        for (int y = 1; y <= N; y++) outer.Add(new double[] { N, y });
        for (int x = N - 1; x >= 0; x--) outer.Add(new double[] { x, N });
        for (int y = N - 1; y >= 1; y--) outer.Add(new double[] { 0, y });
        static double[][] Rect(double x0, double y0, double w, double h) =>
            new[] { new[] { x0, y0 }, new[] { x0, y0 + h }, new[] { x0 + w, y0 + h }, new[] { x0 + w, y0 } };
        var rings = new[] { outer.ToArray(), Rect(5, 5, 2, 4), Rect(2, 23, 1, 1) };

        foreach (var rotation in new[] { 0, 90, 180, 270 })
        {
            var (vertices, holes, dimensions) = Cutear.Flatten(Rotate(rings, rotation));
            var indices = Cutear.Tessellate(vertices, holes, dimensions);
            var err = Cutear.Deviation(vertices, holes, indices.ToArray(), dimensions);
            Assert.That(err, Is.LessThan(1e-9), $"rotation {rotation}: deviation (hole dropped?)");
        }
    }

    private static double TrianglePerimeter(IReadOnlyList<int> triangles, double[] vertices, int dim = 2)
    {
        double perimeter = 0;
        for (int i = 0; i < triangles.Count; i += 3)
        {
            double ax = vertices[triangles[i] * dim], ay = vertices[triangles[i] * dim + 1];
            double bx = vertices[triangles[i + 1] * dim], by = vertices[triangles[i + 1] * dim + 1];
            double cx = vertices[triangles[i + 2] * dim], cy = vertices[triangles[i + 2] * dim + 1];

            perimeter += Hypot(ax - bx, ay - by) + Hypot(bx - cx, by - cy) + Hypot(cx - ax, cy - ay);
        }
        return perimeter;
    }

    private static double Hypot(double x, double y) => Math.Sqrt(x * x + y * y);

    private static int CountIllegalEdges(List<int> triangles, double[] vertices, int dim = 2)
    {
        var halfEdges = Enumerable.Repeat(-1, triangles.Count).ToArray();
        var edges = new Dictionary<(int, int), int>();

        for (int e = 0; e < triangles.Count; e++)
        {
            int a = triangles[e], b = triangles[NextHalfEdge(e)];
            var key = a < b ? (a, b) : (b, a);
            if (edges.Remove(key, out var twin))
            {
                halfEdges[e] = twin;
                halfEdges[twin] = e;
            }
            else
            {
                edges[key] = e;
            }
        }

        int illegal = 0;
        for (int a = 0; a < triangles.Count; a++)
        {
            int b = halfEdges[a];
            if (b == -1 || a > b) continue;

            int a0 = a - a % 3, b0 = b - b % 3;
            int ar = a0 + (a + 2) % 3, al = a0 + (a + 1) % 3, bl = b0 + (b + 2) % 3;
            int p0 = triangles[ar], pr = triangles[a], pl = triangles[al], p1 = triangles[bl];

            double x0 = vertices[p0 * dim], y0 = vertices[p0 * dim + 1];
            double xr = vertices[pr * dim], yr = vertices[pr * dim + 1];
            double xl = vertices[pl * dim], yl = vertices[pl * dim + 1];
            double x1 = vertices[p1 * dim], y1 = vertices[p1 * dim + 1];
            bool convex = Orient(x0, y0, xr, yr, x1, y1) > 0 && Orient(x0, y0, x1, y1, xl, yl) > 0;

            if (convex && !InCircle(x0, y0, xr, yr, xl, yl, x1, y1)) illegal++;
        }
        return illegal;
    }

    private static int NextHalfEdge(int e) => e - e % 3 + (e + 1) % 3;

    private static double Orient(double ax, double ay, double bx, double by, double cx, double cy) =>
        (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);

    private static bool InCircle(double ax, double ay, double bx, double by, double cx, double cy, double px, double py)
    {
        double dx = ax - px, dy = ay - py, ex = bx - px, ey = by - py, fx = cx - px, fy = cy - py;
        double ap = dx * dx + dy * dy, bp = ex * ex + ey * ey, cp = fx * fx + fy * fy;
        return dx * (ey * cp - bp * fy) - dy * (ex * cp - bp * fx) + ap * (ex * fy - ey * fx) <= 0;
    }
}
