# Tube Builder for Unity
Tube Builder is a small mesh‑generation tool for Unity that creates tubes using quadratic Bézier curves. Each tube is built from user‑defined control points and a set of per‑segment parameters. The system is designed for cases where you need lightweight, procedural tube geometry such as stylized hair, wires, ropes, vegetation, or general curved shapes.

The package includes a custom inspector, a SceneView editing workflow, and a dedicated curve editor for shaping radius profiles.

# Features
Quadratic Bézier tube generation

Per‑segment control points (p0, p1, p2)

Adjustable curve and radial resolution

Optional tube body (caps‑only mode supported)

Multiple cap types: Flat, Point, Rounded, Full Sphere, or None

Per‑segment radius profile using an AnimationCurve

Per‑segment color settings with optional hard cutoff

Per‑segment twist

Per‑segment cap scaling for Point and Rounded caps

Custom inspector with reorderable segment list

SceneView handles for interactive editing

Built‑in curve editor window for radius profiles

Mesh export to asset

# How It Works
Each segment defines a quadratic Bézier curve and a set of parameters that control how the tube is generated along that curve. The renderer evaluates the curve, constructs a stable frame, generates rings along the path, and attaches caps based on the selected cap type. All geometry is written directly into a MeshFilter.

# Included Scripts
TubeBuilderRenderer.cs
Core mesh generator.

TubeBuilderRendererEditor.cs
Custom inspector and SceneView editing tools.

TubeCurveEditorWindow.cs
Curve editor for radius profiles.

# Usage
Add the TubeBuilderRenderer component to a GameObject.

Create one or more segments in the inspector.

Adjust control points in the SceneView.

Edit radius, caps, colors, twist, and other settings per segment.

Rebuild the mesh manually or enable auto‑rebuild.

Export the mesh as an asset if needed.

# License
MIT
