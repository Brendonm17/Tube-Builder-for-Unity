# Tube Builder for Unity

Tube Builder is a mesh-generation tool for Unity that creates procedural tubes using quadratic Bézier curves. It is designed for lightweight geometry such as stylized hair, wires, ropes, vegetation, or general shapes. The system supports skeletal skinning (bones), advanced UV mapping, and faceted geometry.

## Features
* **Quadratic Bézier Generation**: Per-segment control points (p0, p1, p2).
* **Bone & Skinning System**:
    * Per-segment bone density with smooth weight blending.
    * Support for nested bone chains (IK compatible) or free population.
    * Visual bone gizmos and weight heatmap debug mode.
* **Geometry Customization**:
    * **Radial Shapes**: Custom curve for non-circular tubes (Stars, Ovals, etc.).
    * **Hard Edges**: Toggle for faceted, low-poly aesthetics.
    * **Continuity**: "Connect to Previous" toggle for seamless segment flow.
    * **Cap Types**: Flat, Point, Rounded, Full Sphere, or None.
* **Visual Mapping**:
    * **UV Mapping**: Choose between Normalized (0-1) or World-Space (Length-based) tiling.
    * **Texture Control**: Per-segment UV tiling and offset.
    * **Lighting**: Manual tangent calculation for Normal Map support.
* **Optimization**: C# 4.0 compatible, no LINQ, and 32-bit index support.
* **Workflow**: Custom inspector with reorderable list and SceneView handles.
* **Mesh Export**: Save generated meshes as assets.

## How It Works
Each segment defines a quadratic Bézier curve and parameters controlling its generation. The renderer evaluates the curve using a stable Parallel Transport Frame to prevent flipping. If bones are enabled, the system generates a bone hierarchy and automatically swaps from a `MeshRenderer` to a `SkinnedMeshRenderer` with calculated bind poses and weights.

## Included Scripts
* **TubeBuilderRenderer.cs**: Core engine, math, and mesh/bone generator.
* **TubeBuilderRendererEditor.cs**: Custom inspector and SceneView editing tools.
* **TubeCurveEditorWindow.cs**: Dedicated window for shaping profiles.

## Usage
1. Add the **TubeBuilderRenderer** component to a GameObject.
2. Create one or more segments in the inspector list.
3. Adjust control points in the SceneView or inspector.
4. Toggle **Use Bones** per segment to enable skinning for animation or IK.
5. Customize radius, caps, colors, twist, and radial shapes.
6. Rebuild the mesh manually or enable auto-rebuild.
7. Export the mesh as an asset if needed.

## License
MIT
