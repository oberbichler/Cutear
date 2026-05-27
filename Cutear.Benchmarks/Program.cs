using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using static Cutear.Cutear;

namespace Benchmarks
{
    public class Program
    {
        public static void Main(string[] args)
        {
            BenchmarkRunner.Run<CutearBenchmarks>();
        }
    }

    [MemoryDiagnoser]
    public class CutearBenchmarks
    {
        private double[] _buildingArray = null!;
        private int[] _buildingHolesArray = null!;
        private Cutear.Polygon _buildingPolygon;

        private double[] _waterArray = null!;
        private int[] _waterHolesArray = null!;
        private Cutear.Polygon _waterPolygon;

        private double[] _waterHugeArray = null!;
        private int[] _waterHugeHolesArray = null!;
        private Cutear.Polygon _waterHugePolygon;

        [GlobalSetup]
        public void Setup()
        {
            string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            
            var (buildingData, buildingHoles) = LoadFixture(baseDir, "building");
            _buildingArray = buildingData.ToArray();
            _buildingHolesArray = buildingHoles.ToArray();
            _buildingPolygon = CreatePolygonFromArrays(_buildingArray, _buildingHolesArray);

            var (waterData, waterHoles) = LoadFixture(baseDir, "water");
            _waterArray = waterData.ToArray();
            _waterHolesArray = waterHoles.ToArray();
            _waterPolygon = CreatePolygonFromArrays(_waterArray, _waterHolesArray);

            var (waterHugeData, waterHugeHoles) = LoadFixture(baseDir, "water-huge");
            _waterHugeArray = waterHugeData.ToArray();
            _waterHugeHolesArray = waterHugeHoles.ToArray();
            _waterHugePolygon = CreatePolygonFromArrays(_waterHugeArray, _waterHugeHolesArray);
        }

        private static (List<double> data, List<int> holes) LoadFixture(string baseDir, string name)
        {
            string path = Path.Combine(baseDir, "fixtures", $"{name}.json");
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Fixture '{name}' not found at '{path}'");
            }

            var polylines = JsonSerializer.Deserialize<double[][][]>(File.ReadAllText(path));
            if (polylines == null)
            {
                throw new InvalidOperationException($"Failed to deserialize fixture '{name}'");
            }

            var data = new List<double>();
            var holes = new List<int>();

            foreach (var polyline in polylines)
            {
                if (data.Any())
                {
                    holes.Add(data.Count / 2);
                }

                foreach (var point in polyline)
                {
                    data.Add(point[0]);
                    data.Add(point[1]);
                }
            }

            return (data, holes);
        }

        private static Cutear.Polygon CreatePolygonFromArrays(double[] data, int[] holeIndices)
        {
            var rings = new List<Cutear.Point[]>();
            int dim = 2;
            int ringCount = holeIndices.Length + 1;
            
            for (int i = 0; i < ringCount; i++)
            {
                int start = i == 0 ? 0 : holeIndices[i - 1] * dim;
                int end = i < ringCount - 1 ? holeIndices[i] * dim : data.Length;
                
                var points = new Cutear.Point[(end - start) / dim];
                for (int j = start, ptIndex = 0; j < end; j += dim, ptIndex++)
                {
                    points[ptIndex] = new Cutear.Point(data[j], data[j + 1]);
                }
                rings.Add(points);
            }
            return new Cutear.Polygon(rings.ToArray());
        }

        // --- Building Benchmarks ---

        [Benchmark]
        public List<int> Building_Array() => Tessellate(_buildingArray, _buildingHolesArray);

        [Benchmark]
        public List<int> Building_Span() => Tessellate(new ReadOnlySpan<double>(_buildingArray), new ReadOnlySpan<int>(_buildingHolesArray));

        [Benchmark]
        public List<int> Building_Polygon() => Tessellate(_buildingPolygon);

        // --- Water Benchmarks ---

        [Benchmark]
        public List<int> Water_Array() => Tessellate(_waterArray, _waterHolesArray);

        [Benchmark]
        public List<int> Water_Span() => Tessellate(new ReadOnlySpan<double>(_waterArray), new ReadOnlySpan<int>(_waterHolesArray));

        [Benchmark]
        public List<int> Water_Polygon() => Tessellate(_waterPolygon);

        // --- WaterHuge Benchmarks ---

        [Benchmark]
        public List<int> WaterHuge_Array() => Tessellate(_waterHugeArray, _waterHugeHolesArray);

        [Benchmark]
        public List<int> WaterHuge_Span() => Tessellate(new ReadOnlySpan<double>(_waterHugeArray), new ReadOnlySpan<int>(_waterHugeHolesArray));

        [Benchmark]
        public List<int> WaterHuge_Polygon() => Tessellate(_waterHugePolygon);
    }
}
