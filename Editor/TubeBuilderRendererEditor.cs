using UnityEngine;
using UnityEditor;
using UnityEditorInternal;

[CustomEditor(typeof(TubeBuilderRenderer))]
public class TubeBuilderRendererEditor : Editor
{
    SerializedProperty segmentsProp;
    SerializedProperty gizmoCurveColor;
    SerializedProperty showWeightsDebugProp;
    SerializedProperty autoRebuildProp;
    ReorderableList segmentsList;
    int selectedIndex = -1;

    void OnEnable()
    {
        segmentsProp = serializedObject.FindProperty("segments");
        gizmoCurveColor = serializedObject.FindProperty("gizmoCurveColor");
        showWeightsDebugProp = serializedObject.FindProperty("showWeightsDebug");
        autoRebuildProp = serializedObject.FindProperty("autoRebuild");
        SetupList();
    }

    void SetupList()
    {
        segmentsList = new ReorderableList(serializedObject, segmentsProp, true, true, true, true);
        segmentsList.drawHeaderCallback = delegate(Rect rect) { EditorGUI.LabelField(rect, "Tube Segments"); };
        segmentsList.onSelectCallback = delegate(ReorderableList list) { selectedIndex = list.index; Tools.hidden = true; };
        segmentsList.onAddCallback = delegate(ReorderableList list) {
            int idx = list.serializedProperty.arraySize++;
            SerializedProperty seg = list.serializedProperty.GetArrayElementAtIndex(idx);
            seg.FindPropertyRelative("name").stringValue = "Segment " + idx;
            seg.FindPropertyRelative("enabled").boolValue = true;
            seg.FindPropertyRelative("p2").vector3Value = Vector3.up;
            seg.FindPropertyRelative("segments").intValue = 8;
            seg.FindPropertyRelative("radialSegments").intValue = 8;
            seg.FindPropertyRelative("uvTiling").vector2Value = Vector2.one;
            seg.FindPropertyRelative("generateTube").boolValue = true;
            seg.FindPropertyRelative("startColor").colorValue = Color.white;
            seg.FindPropertyRelative("endColor").colorValue = Color.white;
            seg.FindPropertyRelative("bonesPerSegment").intValue = 2;
            seg.FindPropertyRelative("radialShapeCurve").animationCurveValue = AnimationCurve.Constant(0, 1, 1);
            seg.FindPropertyRelative("radiusProfile").animationCurveValue = AnimationCurve.Constant(0, 1, 0.05f);
        };
        segmentsList.drawElementCallback = delegate(Rect rect, int index, bool isActive, bool isFocused) {
            SerializedProperty seg = segmentsProp.GetArrayElementAtIndex(index);
            EditorGUI.PropertyField(new Rect(rect.x, rect.y + 2, rect.width, EditorGUIUtility.singleLineHeight), seg.FindPropertyRelative("name"));
        };
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        TubeBuilderRenderer r = (TubeBuilderRenderer)target;

        EditorGUILayout.LabelField("Global Settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(gizmoCurveColor);
        EditorGUILayout.PropertyField(autoRebuildProp);
        EditorGUILayout.PropertyField(showWeightsDebugProp);

        if (GUILayout.Button("Force Rebuild Mesh")) { r.MarkDirty(); r.Rebuild(); }

        EditorGUILayout.Space();
        segmentsList.DoLayoutList();

        if (selectedIndex >= 0 && selectedIndex < segmentsProp.arraySize)
        {
            DrawSegmentSettings(segmentsProp.GetArrayElementAtIndex(selectedIndex));
        }

        if (GUI.changed) { r.MarkDirty(); }
        serializedObject.ApplyModifiedProperties();
    }

    void DrawSegmentSettings(SerializedProperty seg)
    {
        EditorGUILayout.LabelField("Selected Segment Settings", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        
        EditorGUILayout.PropertyField(seg.FindPropertyRelative("name"));
        EditorGUILayout.PropertyField(seg.FindPropertyRelative("enabled"));
        EditorGUILayout.PropertyField(seg.FindPropertyRelative("generateTube"));
        EditorGUILayout.PropertyField(seg.FindPropertyRelative("useHardEdges"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Path Points", EditorStyles.miniBoldLabel);
        EditorGUILayout.PropertyField(seg.FindPropertyRelative("p0"));
        EditorGUILayout.PropertyField(seg.FindPropertyRelative("p1"));
        EditorGUILayout.PropertyField(seg.FindPropertyRelative("p2"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Geometry Detail", EditorStyles.miniBoldLabel);
        EditorGUILayout.PropertyField(seg.FindPropertyRelative("segments"), new GUIContent("Curve Segments"));
        EditorGUILayout.PropertyField(seg.FindPropertyRelative("radialSegments"), new GUIContent("Radial Segments"));
        EditorGUILayout.PropertyField(seg.FindPropertyRelative("radiusProfile"));
        EditorGUILayout.PropertyField(seg.FindPropertyRelative("radialShapeCurve"));
        EditorGUILayout.PropertyField(seg.FindPropertyRelative("twist"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Cap Settings", EditorStyles.miniBoldLabel);
        DrawCapProperty(seg.FindPropertyRelative("startCap"), "Start Cap");
        DrawCapProperty(seg.FindPropertyRelative("endCap"), "End Cap");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Skinning / Bones", EditorStyles.miniBoldLabel);
        EditorGUILayout.PropertyField(seg.FindPropertyRelative("useBones"));
        if (seg.FindPropertyRelative("useBones").boolValue)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(seg.FindPropertyRelative("bonesPerSegment"));
            EditorGUILayout.PropertyField(seg.FindPropertyRelative("useNestedChain"));
            EditorGUILayout.PropertyField(seg.FindPropertyRelative("blendOffset"));
            EditorGUILayout.PropertyField(seg.FindPropertyRelative("blendScaler"));
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Visuals", EditorStyles.miniBoldLabel);
        EditorGUILayout.PropertyField(seg.FindPropertyRelative("startColor"));
        EditorGUILayout.PropertyField(seg.FindPropertyRelative("endColor"));
        EditorGUILayout.PropertyField(seg.FindPropertyRelative("uvTiling"));
        EditorGUILayout.PropertyField(seg.FindPropertyRelative("uvOffset"));

        EditorGUILayout.EndVertical();
    }

    void DrawCapProperty(SerializedProperty cap, string label)
    {
        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        EditorGUI.indentLevel++;
        SerializedProperty typeProp = cap.FindPropertyRelative("type");
        EditorGUILayout.PropertyField(typeProp);
        TubeBuilderRenderer.TubeCapType type = (TubeBuilderRenderer.TubeCapType)typeProp.enumValueIndex;
        
        if (type != TubeBuilderRenderer.TubeCapType.None)
        {
            EditorGUILayout.PropertyField(cap.FindPropertyRelative("scale"));
            if (type == TubeBuilderRenderer.TubeCapType.Rounded) {
                EditorGUILayout.PropertyField(cap.FindPropertyRelative("bulge"));
                EditorGUILayout.PropertyField(cap.FindPropertyRelative("segments"));
            }
            if (type == TubeBuilderRenderer.TubeCapType.FullSphere) {
                EditorGUILayout.PropertyField(cap.FindPropertyRelative("sphereRadius"));
                EditorGUILayout.PropertyField(cap.FindPropertyRelative("sphereResolution"));
            }
        }
        EditorGUI.indentLevel--;
    }

    void OnSceneGUI()
    {
        TubeBuilderRenderer r = (TubeBuilderRenderer)target;
        if (selectedIndex < 0 || selectedIndex >= r.segments.Length) return;
        Undo.RecordObject(r, "Move Path Points");
        EditorGUI.BeginChangeCheck();
        Vector3 p0 = r.transform.TransformPoint(r.segments[selectedIndex].p0), p1 = r.transform.TransformPoint(r.segments[selectedIndex].p1), p2 = r.transform.TransformPoint(r.segments[selectedIndex].p2);
        p0 = Handles.PositionHandle(p0, Quaternion.identity); p1 = Handles.PositionHandle(p1, Quaternion.identity); p2 = Handles.PositionHandle(p2, Quaternion.identity);
        if (EditorGUI.EndChangeCheck()) {
            r.segments[selectedIndex].p0 = r.transform.InverseTransformPoint(p0); r.segments[selectedIndex].p1 = r.transform.InverseTransformPoint(p1); r.segments[selectedIndex].p2 = r.transform.InverseTransformPoint(p2);
            r.MarkDirty();
        }
    }
}
