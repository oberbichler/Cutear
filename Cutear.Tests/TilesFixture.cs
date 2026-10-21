using System.Collections.Generic;
using System.IO;

namespace Cutear.Tests;

// Reads tiles-fixture.bin (from mapbox/earcut's bench): length-delimited packed-varint MVT geometry
// blobs, one per polygon feature. Reconstructs polygons by splitting multipolygons and classifying
// holes by signed area, per the MVT spec.
//
// tiles-fixture.bin format (little-endian LEB128 unsigned varints throughout):
//
//   file    := tile*                     (repeated until EOF)
//   tile    := zoom featureCount feature*
//   feature := geomLen geom              (geomLen = number of varints in geom)
//   geom    := uint32*                   (raw MVT command/parameter integers)
internal static class TilesFixture
{
    public static List<(double[] vertices, int[] holes, int dimensions)> Read(string path)
    {
        var buf = File.ReadAllBytes(path);
        int pos = 0;
        var polys = new List<(double[], int[], int)>();

        while (pos < buf.Length)
        {
            ReadVarint(buf, ref pos); // zoom
            var features = ReadVarint(buf, ref pos);

            for (uint feature = 0; feature < features; feature++)
            {
                var count = ReadVarint(buf, ref pos);
                var geom = new uint[count];
                for (int i = 0; i < count; i++) geom[i] = ReadVarint(buf, ref pos);

                List<double[][]>? current = null;
                foreach (var ring in DecodeMvtRings(geom))
                {
                    if (ring.Length < 3) continue;

                    var area = RingArea(ring);
                    if (area == 0) continue;

                    if (area > 0)
                    {
                        if (current != null) polys.Add(Cutear.Flatten(current.ToArray()));
                        current = new List<double[][]> { ring };
                    }
                    else
                    {
                        current?.Add(ring);
                    }
                }
                if (current != null) polys.Add(Cutear.Flatten(current.ToArray()));
            }
        }

        return polys;
    }

    private static uint ReadVarint(byte[] buf, ref int pos)
    {
        uint val = 0;
        int shift = 0;
        byte b;
        do
        {
            b = buf[pos++];
            val |= (uint)(b & 0x7f) << shift;
            shift += 7;
        } while ((b & 0x80) != 0);
        return val;
    }

    private static List<double[][]> DecodeMvtRings(uint[] geom)
    {
        var rings = new List<double[][]>();
        int x = 0, y = 0;
        List<double[]>? ring = null;
        int i = 0;

        while (i < geom.Length)
        {
            var cmd = geom[i] & 0x7;
            var count = (int)(geom[i] >> 3);
            i++;

            if (cmd == 1)
            {
                for (int k = 0; k < count; k++)
                {
                    x += ZigZagDecode(geom[i++]);
                    y += ZigZagDecode(geom[i++]);
                    if (ring != null) rings.Add(ring.ToArray());
                    ring = new List<double[]> { new double[] { x, y } };
                }
            }
            else if (cmd == 2)
            {
                for (int k = 0; k < count; k++)
                {
                    x += ZigZagDecode(geom[i++]);
                    y += ZigZagDecode(geom[i++]);
                    ring!.Add(new double[] { x, y });
                }
            }
            else if (cmd == 7 && ring != null)
            {
                rings.Add(ring.ToArray());
                ring = null;
            }
        }

        if (ring != null) rings.Add(ring.ToArray());
        return rings;
    }

    private static int ZigZagDecode(uint n) => (int)(n >> 1) ^ -(int)(n & 1);

    private static double RingArea(double[][] ring)
    {
        double sum = 0;
        for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
        {
            sum += (ring[j][0] - ring[i][0]) * (ring[i][1] + ring[j][1]);
        }
        return sum / 2;
    }
}
