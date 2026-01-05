using UnityEngine;
using UnityEditor;
using UnityEditorInternal;

[CustomEditor(typeof(TubeBuilderRenderer))]
public class TubeBuilderRendererEditor : Editor
{
    SerializedProperty segmentsProp, gizmoCurveColor, showWeightsDebugProp, showBonesGizmoProp, autoRebuildProp;
    ReorderableList list;
    int selectedIndex = -1;

    void OnEnable() {
        segmentsProp = serializedObject.FindProperty("segments"); gizmoCurveColor = serializedObject.FindProperty("gizmoCurveColor");
        showWeightsDebugProp = serializedObject.FindProperty("showWeightsDebug"); showBonesGizmoProp = serializedObject.FindProperty("showBonesGizmo");
        autoRebuildProp = serializedObject.FindProperty("autoRebuild");
        list = new ReorderableList(serializedObject, segmentsProp, true, true, true, true);
        list.drawHeaderCallback = delegate(Rect r) { EditorGUI.LabelField(r, "Tube Segments"); };
        list.onSelectCallback = delegate(ReorderableList l) { selectedIndex = l.index; Tools.hidden = true; };
        list.onAddCallback = delegate(ReorderableList l) {
            int idx = l.serializedProperty.arraySize++; SerializedProperty s = l.serializedProperty.GetArrayElementAtIndex(idx);
            s.FindPropertyRelative("name").stringValue = "Seg " + idx; s.FindPropertyRelative("enabled").boolValue = true;
            s.FindPropertyRelative("p2").vector3Value = Vector3.up; s.FindPropertyRelative("segments").intValue = 8;
            s.FindPropertyRelative("radialSegments").intValue = 8; s.FindPropertyRelative("uvTiling").vector2Value = Vector2.one;
            s.FindPropertyRelative("radialShapeCurve").animationCurveValue = AnimationCurve.Constant(0, 1, 1);
            s.FindPropertyRelative("radiusProfile").animationCurveValue = AnimationCurve.Constant(0, 1, 0.05f);
        };
        list.drawElementCallback = delegate(Rect r, int i, bool a, bool f) { EditorGUI.PropertyField(new Rect(r.x, r.y + 2, r.width, 16), segmentsProp.GetArrayElementAtIndex(i).FindPropertyRelative("name")); };
    }

    public override void OnInspectorGUI() {
        serializedObject.Update(); TubeBuilderRenderer r = (TubeBuilderRenderer)target;
        EditorGUILayout.LabelField("Global Settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(gizmoCurveColor); EditorGUILayout.PropertyField(autoRebuildProp);
        EditorGUILayout.PropertyField(showWeightsDebugProp); EditorGUILayout.PropertyField(showBonesGizmoProp);
        if (GUILayout.Button("Force Rebuild")) { r.MarkDirty(); r.Rebuild(); }
        list.DoLayoutList();
        if (selectedIndex >= 0 && selectedIndex < segmentsProp.arraySize) DrawSettings(segmentsProp.GetArrayElementAtIndex(selectedIndex));
        if (GUI.changed) { r.MarkDirty(); }
        serializedObject.ApplyModifiedProperties();
    }

    void DrawSettings(SerializedProperty s) {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.PropertyField(s.FindPropertyRelative("name")); EditorGUILayout.PropertyField(s.FindPropertyRelative("enabled"));
        EditorGUILayout.PropertyField(s.FindPropertyRelative("connectToPrevious"));
        EditorGUILayout.PropertyField(s.FindPropertyRelative("generateTube")); EditorGUILayout.PropertyField(s.FindPropertyRelative("useHardEdges"));
        EditorGUILayout.PropertyField(s.FindPropertyRelative("uvMapping"));
        EditorGUILayout.PropertyField(s.FindPropertyRelative("p0")); EditorGUILayout.PropertyField(s.FindPropertyRelative("p1")); EditorGUILayout.PropertyField(s.FindPropertyRelative("p2"));
        EditorGUILayout.PropertyField(s.FindPropertyRelative("segments")); EditorGUILayout.PropertyField(s.FindPropertyRelative("radialSegments"));
        EditorGUILayout.PropertyField(s.FindPropertyRelative("radiusProfile")); EditorGUILayout.PropertyField(s.FindPropertyRelative("radialShapeCurve"));
        DrawCap(s.FindPropertyRelative("startCap"), "Start Cap"); DrawCap(s.FindPropertyRelative("endCap"), "End Cap");
        EditorGUILayout.PropertyField(s.FindPropertyRelative("useBones"));
        if (s.FindPropertyRelative("useBones").boolValue) {
            EditorGUI.indentLevel++; EditorGUILayout.PropertyField(s.FindPropertyRelative("bonesPerSegment"));
            EditorGUILayout.PropertyField(s.FindPropertyRelative("useNestedChain"));
            EditorGUILayout.PropertyField(s.FindPropertyRelative("blendOffset")); EditorGUILayout.PropertyField(s.FindPropertyRelative("blendScaler"));
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.PropertyField(s.FindPropertyRelative("startColor")); EditorGUILayout.PropertyField(s.FindPropertyRelative("endColor"));
        EditorGUILayout.PropertyField(s.FindPropertyRelative("uvTiling")); EditorGUILayout.PropertyField(s.FindPropertyRelative("uvOffset"));
        EditorGUILayout.EndVertical();
    }

    void DrawCap(SerializedProperty c, string l) {
        EditorGUILayout.LabelField(l, EditorStyles.boldLabel); EditorGUI.indentLevel++;
        SerializedProperty tP = c.FindPropertyRelative("type"); EditorGUILayout.PropertyField(tP);
        if (tP.enumValueIndex != 0) {
            EditorGUILayout.PropertyField(c.FindPropertyRelative("scale"));
            if (tP.enumValueIndex == 3) { EditorGUILayout.PropertyField(c.FindPropertyRelative("bulge")); EditorGUILayout.PropertyField(c.FindPropertyRelative("segments")); }
            if (tP.enumValueIndex == 4) { EditorGUILayout.PropertyField(c.FindPropertyRelative("sphereRadius")); EditorGUILayout.PropertyField(c.FindPropertyRelative("sphereResolution")); }
        }
        EditorGUI.indentLevel--;
    }

    void OnSceneGUI() {
        TubeBuilderRenderer r = (TubeBuilderRenderer)target; if (selectedIndex < 0 || selectedIndex >= r.segments.Length) return;
        Undo.RecordObject(r, "Move Points"); EditorGUI.BeginChangeCheck();
        Vector3 p0 = r.transform.TransformPoint(r.segments[selectedIndex].p0), p1 = r.transform.TransformPoint(r.segments[selectedIndex].p1), p2 = r.transform.TransformPoint(r.segments[selectedIndex].p2);
        p0 = Handles.PositionHandle(p0, Quaternion.identity); p1 = Handles.PositionHandle(p1, Quaternion.identity); p2 = Handles.PositionHandle(p2, Quaternion.identity);
        if (EditorGUI.EndChangeCheck()) {
            r.segments[selectedIndex].p0 = r.transform.InverseTransformPoint(p0); r.segments[selectedIndex].p1 = r.transform.InverseTransformPoint(p1); r.segments[selectedIndex].p2 = r.transform.InverseTransformPoint(p2);
            r.MarkDirty();
        }
    }
}
