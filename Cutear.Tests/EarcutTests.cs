using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using NUnit.Framework;

namespace Cutear.Tests;

[TestFixture]
public class EarcutTests
{
    private static TestCaseData LoadTestCaseData(string filename, int expectedTriangles, double expectedDeviation = 1e-14, double? expectedDeviationWithRotation = null)
    {
        return new TestCaseData(filename, expectedTriangles, expectedDeviation, expectedDeviationWithRotation ?? expectedDeviation).SetName(filename);
    }

    private static IEnumerable<TestCaseData> TestCases()
    {
        yield return LoadTestCaseData("building", 13);
        yield return LoadTestCaseData("dude", 106, 2e-15);
        yield return LoadTestCaseData("water", 2482, 0.0008);
        yield return LoadTestCaseData("water2", 1212);
        yield return LoadTestCaseData("water3", 197);
        yield return LoadTestCaseData("water3b", 25);
        yield return LoadTestCaseData("water4", 705);
        yield return LoadTestCaseData("water-huge", 5175, 0.0011, 0.0035);
        yield return LoadTestCaseData("water-huge2", 4462, 0.004, 0.061);
        yield return LoadTestCaseData("degenerate", 0);
        yield return LoadTestCaseData("bad-hole", 42, 0.019, 0.04);
        yield return LoadTestCaseData("empty-square", 0);
        yield return LoadTestCaseData("issue16", 12, 4e-16, 8e-16);
        yield return LoadTestCaseData("issue17", 11, 2e-16);
        yield return LoadTestCaseData("steiner", 9);
        yield return LoadTestCaseData("issue29", 40, 2e-15);
        yield return LoadTestCaseData("issue34", 139);
        yield return LoadTestCaseData("issue35", 844);
        yield return LoadTestCaseData("self-touching", 124, 2e-13);
        yield return LoadTestCaseData("outside-ring", 64);
        yield return LoadTestCaseData("simplified-us-border", 120);
        yield return LoadTestCaseData("touching-holes", 57);
        yield return LoadTestCaseData("touching-holes2", 10);
        yield return LoadTestCaseData("touching-holes3", 82, 1e-14, 0.13);
        yield return LoadTestCaseData("touching-holes4", 55, 1e-14, 0.05);
        yield return LoadTestCaseData("touching-holes5", 133, 1e-14, 0.07);
        yield return LoadTestCaseData("touching-holes6", 3096);
        yield return LoadTestCaseData("hole-touching-outer", 77);
        yield return LoadTestCaseData("hilbert", 1024);
        yield return LoadTestCaseData("issue45", 10);
        yield return LoadTestCaseData("eberly-3", 73);
        yield return LoadTestCaseData("eberly-6", 1429, 2e-14);
        yield return LoadTestCaseData("issue52", 109);
        yield return LoadTestCaseData("shared-points", 4);
        yield return LoadTestCaseData("bad-diagonals", 7);
        yield return LoadTestCaseData("issue83", 0);
        yield return LoadTestCaseData("issue107", 0);
        yield return LoadTestCaseData("issue111", 18);
        yield return LoadTestCaseData("boxy", 57);
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
    }

    [TestCaseSource(nameof(TestCases))]
    public void AreaTest(string filename, int expectedTriangles, double expectedDeviation, double expectedDeviationWithRotation)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", $"{filename}.json");
        var polylines = JsonSerializer.Deserialize<double[][][]>(File.ReadAllText(path));
        if (polylines == null)
        {
            throw new InvalidOperationException($"Failed to deserialize fixture '{filename}'");
        }

        var rotations = new[] { 0, 90, 180, 270 };
        foreach (var rotation in rotations)
        {
            var data = new List<double>();
            var holeIndices = new List<int>();

            double xx = 1, xy = 0, yx = 0, yy = 1;
            if (rotation != 0)
            {
                var theta = rotation * Math.PI / 180.0;
                xx = Math.Round(Math.Cos(theta));
                xy = Math.Round(-Math.Sin(theta));
                yx = Math.Round(Math.Sin(theta));
                yy = Math.Round(Math.Cos(theta));
            }

            foreach (var polyline in polylines)
            {
                if (data.Count > 0)
                {
                    holeIndices.Add(data.Count / 2);
                }

                foreach (var point in polyline)
                {
                    var x = point[0];
                    var y = point[1];
                    if (rotation != 0)
                    {
                        var rx = xx * x + xy * y;
                        var ry = yx * x + yy * y;
                        data.Add(rx);
                        data.Add(ry);
                    }
                    else
                    {
                        data.Add(x);
                        data.Add(y);
                    }
                }
            }

            var triangles = Cutear.Tessellate(CollectionsMarshal.AsSpan(data), CollectionsMarshal.AsSpan(holeIndices));

            if (rotation == 0)
            {
                if (expectedTriangles > 0)
                {
                    Assert.That(triangles.Count / 3, Is.EqualTo(expectedTriangles), $"Rotation {rotation}: expected {expectedTriangles} triangles, but got {triangles.Count / 3}");
                }
                else
                {
                    Assert.That(triangles.Count / 3, Is.EqualTo(0), $"Rotation {rotation}: expected 0 triangles, but got {triangles.Count / 3}");
                }
            }

            if (expectedTriangles > 0)
            {
                var actualDeviation = Cutear.Deviation(CollectionsMarshal.AsSpan(data), CollectionsMarshal.AsSpan(holeIndices), CollectionsMarshal.AsSpan(triangles));
                var allowedDeviation = rotation != 0 ? expectedDeviationWithRotation : expectedDeviation;
                Assert.That(actualDeviation, Is.LessThanOrEqualTo(allowedDeviation), $"Rotation {rotation}: actual deviation {actualDeviation} exceeds allowed deviation {allowedDeviation}");
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

        Assert.That(triangles, Is.Not.Null);
        Assert.That(triangles.Count, Is.GreaterThan(0));
        Assert.That(triangles.Count % 3, Is.EqualTo(0));
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

        Assert.That(triangles, Is.EqualTo(new[] { 1, 0, 3, 3, 2, 1 }));
    }

    [Test]
    public void Indices2D()
    {
        var indices = Cutear.Tessellate(new double[] { 10, 0, 0, 50, 60, 60, 70, 10 }, Array.Empty<int>());
        Assert.That(indices, Is.EqualTo(new[] { 1, 0, 3, 3, 2, 1 }));
    }

    [Test]
    public void Indices3D()
    {
        var indices = Cutear.Tessellate(new double[] { 10, 0, 0, 0, 50, 0, 60, 60, 0, 70, 10, 0 }, Array.Empty<int>(), 3);
        Assert.That(indices, Is.EqualTo(new[] { 1, 0, 3, 3, 2, 1 }));
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
}
