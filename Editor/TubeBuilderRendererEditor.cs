using UnityEngine;
using UnityEditor;
using UnityEditorInternal;

[CustomEditor(typeof(TubeBuilderRenderer))]
public class TubeBuilderRendererEditor : Editor
{
    private SerializedProperty segmentsProp, tubeMaterialProp, gizmoCurveColorProp, showWeightsDebugProp, showBonesGizmoProp, autoRebuildProp;
    private ReorderableList list;
    private int selectedIndex = -1;

    void OnEnable() {
        segmentsProp = serializedObject.FindProperty("segments"); tubeMaterialProp = serializedObject.FindProperty("tubeMaterial");
        gizmoCurveColorProp = serializedObject.FindProperty("gizmoCurveColor"); showWeightsDebugProp = serializedObject.FindProperty("showWeightsDebug");
        showBonesGizmoProp = serializedObject.FindProperty("showBonesGizmo"); autoRebuildProp = serializedObject.FindProperty("autoRebuild");
        list = new ReorderableList(serializedObject, segmentsProp, true, true, true, true);
        list.drawHeaderCallback = delegate(Rect rect) { EditorGUI.LabelField(rect, "Tube Segments"); };
        list.onSelectCallback = delegate(ReorderableList l) { selectedIndex = l.index; Tools.hidden = (selectedIndex >= 0); };
        list.onAddCallback = delegate(ReorderableList l) {
            int index = l.serializedProperty.arraySize++; SerializedProperty s = l.serializedProperty.GetArrayElementAtIndex(index);
            s.FindPropertyRelative("name").stringValue = "Segment " + index; s.FindPropertyRelative("enabled").boolValue = true;
            s.FindPropertyRelative("p2").vector3Value = Vector3.up; s.FindPropertyRelative("segments").intValue = 8;
            s.FindPropertyRelative("radialSegments").intValue = 8; s.FindPropertyRelative("uvTiling").vector2Value = Vector2.one;
            s.FindPropertyRelative("generateTube").boolValue = true; s.FindPropertyRelative("startColor").colorValue = Color.white;
            s.FindPropertyRelative("endColor").colorValue = Color.white; s.FindPropertyRelative("bonesPerSegment").intValue = 2;
            s.FindPropertyRelative("blendScaler").floatValue = 1f; s.FindPropertyRelative("radialShapeCurve").animationCurveValue = AnimationCurve.Linear(0,1,1,1);
            s.FindPropertyRelative("radiusProfile").animationCurveValue = AnimationCurve.Linear(0,0.05f,1,0.05f);
            SerializedProperty sc = s.FindPropertyRelative("startCap"); sc.FindPropertyRelative("type").enumValueIndex = 3; 
            sc.FindPropertyRelative("scale").floatValue = 1f; sc.FindPropertyRelative("bulge").floatValue = 1f; sc.FindPropertyRelative("segments").intValue = 4;
            sc.FindPropertyRelative("sphereResolution").intValue = 8; sc.FindPropertyRelative("sphereRadius").floatValue = 0.05f;
            SerializedProperty ec = s.FindPropertyRelative("endCap"); ec.FindPropertyRelative("type").enumValueIndex = 3; 
            ec.FindPropertyRelative("scale").floatValue = 1f; ec.FindPropertyRelative("bulge").floatValue = 1f; ec.FindPropertyRelative("segments").intValue = 4;
            ec.FindPropertyRelative("sphereResolution").intValue = 8; ec.FindPropertyRelative("sphereRadius").floatValue = 0.05f;
            serializedObject.ApplyModifiedProperties(); ((TubeBuilderRenderer)target).Rebuild();
        };
        list.drawElementCallback = delegate(Rect rect, int index, bool isActive, bool isFocused) {
            SerializedProperty nameProp = segmentsProp.GetArrayElementAtIndex(index).FindPropertyRelative("name");
            EditorGUI.LabelField(new Rect(rect.x, rect.y + 2, rect.width, 16), "Segment " + index + ": " + nameProp.stringValue, EditorStyles.boldLabel);
        };
    }

    public override void OnInspectorGUI() {
        serializedObject.Update(); TubeBuilderRenderer r = (TubeBuilderRenderer)target;
        EditorGUILayout.LabelField("Global Settings", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.PropertyField(autoRebuildProp); EditorGUILayout.PropertyField(tubeMaterialProp);
        EditorGUILayout.PropertyField(gizmoCurveColorProp); EditorGUILayout.PropertyField(showWeightsDebugProp);
        EditorGUILayout.PropertyField(showBonesGizmoProp);
        if (GUILayout.Button("Force Rebuild Mesh")) { r.MarkDirty(); r.Rebuild(); }
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(); list.DoLayoutList();
        if (selectedIndex >= 0 && selectedIndex < segmentsProp.arraySize) DrawSegmentSettings(segmentsProp.GetArrayElementAtIndex(selectedIndex));
        else EditorGUILayout.HelpBox("Select a segment from the list to edit its properties.", MessageType.Info);
        if (GUI.changed) r.MarkDirty();
        serializedObject.ApplyModifiedProperties();
    }

    void DrawSegmentSettings(SerializedProperty s) {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.PropertyField(s.FindPropertyRelative("name")); EditorGUILayout.PropertyField(s.FindPropertyRelative("enabled"));
        EditorGUILayout.PropertyField(s.FindPropertyRelative("generateTube")); EditorGUILayout.PropertyField(s.FindPropertyRelative("connectToPrevious"));
        EditorGUILayout.Space(); EditorGUILayout.LabelField("Path Points", EditorStyles.miniBoldLabel);
        EditorGUILayout.PropertyField(s.FindPropertyRelative("p0")); EditorGUILayout.PropertyField(s.FindPropertyRelative("p1")); EditorGUILayout.PropertyField(s.FindPropertyRelative("p2"));
        EditorGUILayout.Space(); EditorGUILayout.LabelField("Mesh Geometry", EditorStyles.miniBoldLabel);
        EditorGUILayout.PropertyField(s.FindPropertyRelative("segments")); EditorGUILayout.PropertyField(s.FindPropertyRelative("radialSegments"));
        EditorGUILayout.PropertyField(s.FindPropertyRelative("twist")); EditorGUILayout.PropertyField(s.FindPropertyRelative("useHardEdges"));
        EditorGUILayout.Space(); EditorGUILayout.LabelField("Profiles", EditorStyles.miniBoldLabel);
        EditorGUILayout.PropertyField(s.FindPropertyRelative("radiusProfile")); EditorGUILayout.PropertyField(s.FindPropertyRelative("radialShapeCurve"));
        EditorGUILayout.Space(); EditorGUILayout.LabelField("Visuals & Colors", EditorStyles.miniBoldLabel);
        EditorGUILayout.PropertyField(s.FindPropertyRelative("startColor")); EditorGUILayout.PropertyField(s.FindPropertyRelative("endColor"));
        EditorGUILayout.PropertyField(s.FindPropertyRelative("uvMapping")); EditorGUILayout.PropertyField(s.FindPropertyRelative("uvTiling")); EditorGUILayout.PropertyField(s.FindPropertyRelative("uvOffset"));
        EditorGUILayout.Space(); EditorGUILayout.LabelField("End Caps", EditorStyles.miniBoldLabel);
        DrawCap(s.FindPropertyRelative("startCap"), "Start Cap"); DrawCap(s.FindPropertyRelative("endCap"), "End Cap");
        EditorGUILayout.Space(); EditorGUILayout.LabelField("Skinning / Bones", EditorStyles.miniBoldLabel);
        SerializedProperty useBones = s.FindPropertyRelative("useBones"); EditorGUILayout.PropertyField(useBones);
        if (useBones.boolValue) {
            EditorGUI.indentLevel++; EditorGUILayout.PropertyField(s.FindPropertyRelative("bonesPerSegment"));
            EditorGUILayout.PropertyField(s.FindPropertyRelative("useNestedChain")); EditorGUILayout.PropertyField(s.FindPropertyRelative("blendOffset"));
            EditorGUILayout.PropertyField(s.FindPropertyRelative("blendScaler")); EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndVertical();
    }

    void DrawCap(SerializedProperty cap, string label) {
        EditorGUILayout.LabelField(label, EditorStyles.miniLabel); EditorGUI.indentLevel++;
        SerializedProperty type = cap.FindPropertyRelative("type"); EditorGUILayout.PropertyField(type);
        if (type.enumValueIndex != 0) {
            EditorGUILayout.PropertyField(cap.FindPropertyRelative("scale"));
            if (type.enumValueIndex == 3) { EditorGUILayout.PropertyField(cap.FindPropertyRelative("bulge")); EditorGUILayout.PropertyField(cap.FindPropertyRelative("segments")); }
            if (type.enumValueIndex == 4) { EditorGUILayout.PropertyField(cap.FindPropertyRelative("sphereRadius")); EditorGUILayout.PropertyField(cap.FindPropertyRelative("sphereResolution")); }
        }
        EditorGUI.indentLevel--;
    }

    void OnSceneGUI() {
        TubeBuilderRenderer r = (TubeBuilderRenderer)target;
        if (selectedIndex < 0 || selectedIndex >= r.segments.Length) return;
        Undo.RecordObject(r, "Move Path Points"); EditorGUI.BeginChangeCheck();
        Transform t = r.transform;
        Vector3 p0 = t.TransformPoint(r.segments[selectedIndex].p0), p1 = t.TransformPoint(r.segments[selectedIndex].p1), p2 = t.TransformPoint(r.segments[selectedIndex].p2);
        Handles.color = r.gizmoCurveColor; Handles.DrawLine(p0, p1); Handles.DrawLine(p1, p2);
        Vector3 newP0 = Handles.PositionHandle(p0, Quaternion.identity); Vector3 newP1 = Handles.PositionHandle(p1, Quaternion.identity); Vector3 newP2 = Handles.PositionHandle(p2, Quaternion.identity);
        if (EditorGUI.EndChangeCheck()) {
            r.segments[selectedIndex].p0 = t.InverseTransformPoint(newP0); r.segments[selectedIndex].p1 = t.InverseTransformPoint(newP1); r.segments[selectedIndex].p2 = t.InverseTransformPoint(newP2);
            if (r.segments[selectedIndex].connectToPrevious && selectedIndex > 0) {
                if (newP0 != p0) r.segments[selectedIndex - 1].p2 = r.segments[selectedIndex].p0;
                if (newP1 != p1) r.segments[selectedIndex - 1].p1 = r.segments[selectedIndex].p0 - (r.segments[selectedIndex].p1 - r.segments[selectedIndex].p0);
            }
            if (selectedIndex < r.segments.Length - 1 && r.segments[selectedIndex + 1].connectToPrevious) {
                r.segments[selectedIndex + 1].p0 = r.segments[selectedIndex].p2;
                r.segments[selectedIndex + 1].p1 = r.segments[selectedIndex].p2 + (r.segments[selectedIndex].p2 - r.segments[selectedIndex].p1);
            }
            r.MarkDirty();
        }
    }
}
