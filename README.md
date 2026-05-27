<p align="center">
  <img src="logo.png" alt="Cutear Logo" width="200" />
</p>

# Cutear

[![CI](https://github.com/oberbichler/earcut.net/actions/workflows/ci.yml/badge.svg)](https://github.com/oberbichler/earcut.net/actions/workflows/ci.yml)
[![.NET](https://img.shields.io/badge/.NET-Standard%202.0%20%7C%202.1%20%7C%2010.0-blue.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A high-performance, **zero-allocation** C# port of the [Mapbox Earcut](https://github.com/mapbox/earcut) polygon triangulation library, targeting .NET Standard 2.0, .NET Standard 2.1, and .NET 10.0.

## Features

- **Fast & Robust Triangulation**: Handles 2D/3D polygons with nested holes, touch-points, and self-intersections.
- **Zero Heap Allocations**: Uses thread-local pooling (`[ThreadStatic]`) to guarantee 0 bytes of internal memory allocation during triangulation.
- **Direct Memory Spans**: Native support for `ReadOnlySpan<double>` and `ReadOnlySpan<int>` to eliminate copy and interface overhead.
- **100% Mapbox Parity**: Aligned with the latest bugfixes and corrections from [Mapbox Earcut v2.2.4+](https://github.com/mapbox/earcut).
- **No Dependencies**: Self-contained with absolutely zero external package dependencies.
- **Highly Compatible**: Targets `.NET Standard 2.0`, `.NET Standard 2.1`, and `.NET 10.0` (ideal for Unity, Mono, and modern .NET).

---

## Quick Start

Add the **[Cutear](https://www.nuget.org/packages/Cutear/)** NuGet package to your project:

```bash
dotnet add package Cutear
```

---

## Usage Example

### Basic 2D Triangulation
```csharp
using Cutear;

// Flat array of vertex coordinates (X, Y)
var vertices = new double[] {
      0,   0, // Vertex 0 (outline)
    100,   0, // Vertex 1 (outline)
    100, 100, // Vertex 2 (outline)
      0, 100, // Vertex 3 (outline)
     20,  20, // Vertex 4 (hole)
     80,  20, // Vertex 5 (hole)
     80,  80, // Vertex 6 (hole)
     20,  80  // Vertex 7 (hole)
};

// Indices where holes start
var holeIndices = new int[] { 4 };

// Triangulate
List<int> triangles = Cutear.Tessellate(vertices, holeIndices);

// triangles contains: [3, 0, 4, 5, 4, 0, 3, 4, 7, ... ]
// Each group of three indices forms a triangle.
for (int i = 0; i < triangles.Count; i += 3)
{
    int indexA = triangles[i];
    int indexB = triangles[i + 1];
    int indexC = triangles[i + 2];
    Console.WriteLine($"Triangle: ({indexA}, {indexB}, {indexC})");
}
```

### High-Performance Span Overload
```csharp
ReadOnlySpan<double> verticesSpan = stackalloc double[] { 0, 0, 10, 0, 10, 10, 0, 10 };
ReadOnlySpan<int> holesSpan = ReadOnlySpan<int>.Empty;

// 100% allokationsfrei (bis auf das Ergebnis-Objekt)
List<int> triangles = Cutear.Tessellate(verticesSpan, holesSpan, dim: 2);
```

### 3D Triangulation (Ignoring Z-Coordinate)
Supports multi-dimensional arrays (such as 3D vertices with X, Y, Z coordinates) by projecting them onto the 2D plane (X, Y) during triangulation. Simply specify `dim: 3`:

```csharp
// Flat array of 3D coordinates (X, Y, Z)
ReadOnlySpan<double> vertices3D = stackalloc double[] {
    10,  0, 1.5, // Vertex 0
     0, 50, 2.5, // Vertex 1
    60, 60, 3.5, // Vertex 2
    70, 10, 4.5  // Vertex 3
};

// dim: 3 tells the engine that each vertex has 3 coordinates
List<int> triangles = Cutear.Tessellate(vertices3D, ReadOnlySpan<int>.Empty, dim: 3);

// triangles contains: [1, 0, 3, 3, 2, 1]
```

### GeoJSON / Multi-Dimensional Winding
```csharp
// Multi-dimensional coordinates (GeoJSON-like structure)
double[][][] polygon = new double[][][] {
    new double[][] { new[] {0.0, 0.0}, new[] {10.0, 0.0}, new[] {10.0, 10.0}, new[] {0.0, 10.0} } // Outer ring
};

// Flatten geometry for earcut
var (vertices, holes, dimensions) = Cutear.Flatten(polygon);

List<int> triangles = Cutear.Tessellate(vertices, holes);
```

### Object-Oriented / Generic Polygon Triangulation
```csharp
// Create a polygon using lightweight Point and Polygon structs
var outerRing = new[] { new Point(0, 0), new Point(10, 0), new Point(10, 10), new Point(0, 10) };
var holeRing = new[] { new Point(2, 2), new Point(8, 2), new Point(8, 8), new Point(2, 8) };

var polygon = new Polygon(new[] { outerRing, holeRing });

// Completely zero-allocation triangulation directly over generic IPolygon<TPoint>
List<int> triangles = Cutear.Tessellate(polygon);
```

For 3D polygons, simply use 3D `Point` constructors and pass `dim: 3`:
```csharp
var outerRing3D = new[] {
    new Point(10, 0, 1.5),
    new Point(0, 50, 2.5),
    new Point(60, 60, 3.5),
    new Point(70, 10, 4.5)
};
var polygon3D = new Polygon(new[] { outerRing3D });

List<int> triangles = Cutear.Tessellate(polygon3D, dim: 3);
```

---

## Performance & Benchmarks

Triangulation of three representative datasets on **macOS (Apple M1 Pro, .NET 10)**:

### 1. Execution Speed

| API Method | Building (Small) | Water (Medium) | WaterHuge (Large) | Description |
|:---|---:|---:|---:|:---|
| **`Array`** | 349.1 ns | 634.9 µs | 8.25 ms | Native array-based fast-path. |
| **`Span`** | 349.4 ns | 630.0 µs | 8.20 ms | Memory span. |
| **`Polygon`** | 373.4 ns | 645.0 µs | 8.40 ms | Generic `IPolygon<T>` API. |

---

### 2. Managed Memory Allocations

| API Method | Building (Small) | Water (Medium) | WaterHuge (Large) |
|:---|---:|---:|---:|
| **All APIs** | **0.21 KB** (216 B) | **29.60 KB** | **66.40 KB** |

*Note:* Under the optimized engine, the only memory allocated on the heap is the returned output `List<int>` object. Internal nodes, temporary array copies, and queues allocate perfectly 0 bytes.

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE) for the full text.
