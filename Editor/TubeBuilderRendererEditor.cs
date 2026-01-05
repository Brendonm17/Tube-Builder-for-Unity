using UnityEngine;
using UnityEditor;
using UnityEditorInternal;

[CustomEditor(typeof(TubeBuilderRenderer))]
public class TubeBuilderRendererEditor : Editor
{
    private SerializedProperty segmentsProp, gizmoCurveColor, showWeightsDebugProp, showBonesGizmoProp, autoRebuildProp;
    private ReorderableList list;
    private int selectedIndex = -1;

    // Custom UI Styles
    private GUIStyle headerStyle;
    private GUIStyle subHeaderStyle;
    private GUIStyle boxStyle;

    void OnEnable()
    {
        segmentsProp = serializedObject.FindProperty("segments");
        gizmoCurveColor = serializedObject.FindProperty("gizmoCurveColor");
        showWeightsDebugProp = serializedObject.FindProperty("showWeightsDebug");
        showBonesGizmoProp = serializedObject.FindProperty("showBonesGizmo");
        autoRebuildProp = serializedObject.FindProperty("autoRebuild");

        list = new ReorderableList(serializedObject, segmentsProp, true, true, true, true);
        list.drawHeaderCallback = delegate(Rect r) { EditorGUI.LabelField(r, "Tube Segment Stack"); };
        list.onSelectCallback = delegate(ReorderableList l) { selectedIndex = l.index; Tools.hidden = true; };
        list.drawElementCallback = delegate(Rect r, int i, bool a, bool f) {
            SerializedProperty nameP = segmentsProp.GetArrayElementAtIndex(i).FindPropertyRelative("name");
            EditorGUI.LabelField(new Rect(r.x, r.y, r.width, 16), "Segment [" + i + "]: " + nameP.stringValue, EditorStyles.boldLabel);
        };
    }

    private void InitStyles()
    {
        if (headerStyle != null) return;
        headerStyle = new GUIStyle(GUI.skin.box);
        headerStyle.normal.background = MakeTex(2, 2, new Color(0.15f, 0.15f, 0.15f, 1f));
        headerStyle.fontStyle = FontStyle.Bold;
        headerStyle.alignment = TextAnchor.MiddleCenter;

        subHeaderStyle = new GUIStyle(EditorStyles.helpBox);
        subHeaderStyle.fontStyle = FontStyle.Bold;
        subHeaderStyle.normal.textColor = Color.white;

        boxStyle = new GUIStyle(GUI.skin.box);
        boxStyle.padding = new RectOffset(10, 10, 10, 10);
    }

    public override void OnInspectorGUI()
    {
        InitStyles();
        serializedObject.Update();
        TubeBuilderRenderer r = (TubeBuilderRenderer)target;

        // --- GLOBAL SETTINGS ---
        GUILayout.Space(10);
        GUILayout.BeginVertical(headerStyle);
        GUILayout.Label("GLOBAL BUILDER SETTINGS", GUILayout.Height(25));
        GUILayout.EndVertical();

        EditorGUILayout.BeginVertical(boxStyle);
        EditorGUILayout.PropertyField(autoRebuildProp, new GUIContent("Live Rebuild"));
        EditorGUILayout.PropertyField(gizmoCurveColor, new GUIContent("Path Color"));
        
        GUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(showWeightsDebugProp, new GUIContent("Heatmap Debug"));
        EditorGUILayout.PropertyField(showBonesGizmoProp, new GUIContent("Show Bones"));
        GUILayout.EndHorizontal();

        if (GUILayout.Button("FORCE MESH REGENERATE", GUILayout.Height(30))) { r.MarkDirty(); r.Rebuild(); }
        EditorGUILayout.EndVertical();

        // --- SEGMENT LIST ---
        GUILayout.Space(10);
        list.DoLayoutList();

        if (selectedIndex >= 0 && selectedIndex < segmentsProp.arraySize)
        {
            DrawSegmentSettings(segmentsProp.GetArrayElementAtIndex(selectedIndex), selectedIndex);
        }
        else
        {
            EditorGUILayout.HelpBox("Select a segment from the list to edit its properties.", MessageType.Info);
        }

        if (GUI.changed) { r.MarkDirty(); }
        serializedObject.ApplyModifiedProperties();
    }

    void DrawSegmentSettings(SerializedProperty s, int idx)
    {
        GUILayout.Space(10);
        GUILayout.BeginVertical(headerStyle);
        GUILayout.Label("SEGMENT #" + idx + ": " + s.FindPropertyRelative("name").stringValue.ToUpper(), GUILayout.Height(25));
        GUILayout.EndVertical();

        EditorGUILayout.BeginVertical(boxStyle);

        // -- Transform Section --
        DrawSectionHeader("PATH & CONTINUITY");
        EditorGUILayout.PropertyField(s.FindPropertyRelative("name"));
        EditorGUILayout.PropertyField(s.FindPropertyRelative("enabled"));
        EditorGUILayout.PropertyField(s.FindPropertyRelative("connectToPrevious"), new GUIContent("Snap to Prev (Smoothing)"));
        
        EditorGUILayout.Space();
        EditorGUILayout.PropertyField(s.FindPropertyRelative("p0"), new GUIContent("Start (P0)"));
        EditorGUILayout.PropertyField(s.FindPropertyRelative("p1"), new GUIContent("Control (P1)"));
        EditorGUILayout.PropertyField(s.FindPropertyRelative("p2"), new GUIContent("End (P2)"));

        // -- Geometry Section --
        DrawSectionHeader("GEOMETRY & SHAPE");
        EditorGUILayout.IntSlider(s.FindPropertyRelative("segments"), 2, 64, new GUIContent("Path Segments"));
        EditorGUILayout.IntSlider(s.FindPropertyRelative("radialSegments"), 3, 32, new GUIContent("Radial Sides"));
        EditorGUILayout.PropertyField(s.FindPropertyRelative("radiusProfile"), new GUIContent("Thickness Profile"));
        EditorGUILayout.PropertyField(s.FindPropertyRelative("radialShapeCurve"), new GUIContent("Cross-Section Shape"));
        EditorGUILayout.Slider(s.FindPropertyRelative("twist"), -720f, 720f, new GUIContent("Curve Twist"));
        EditorGUILayout.PropertyField(s.FindPropertyRelative("useHardEdges"), new GUIContent("Hard Faceted Edges"));

        // -- Caps Section --
        DrawSectionHeader("END CAPS");
        GUILayout.BeginHorizontal();
        DrawCap(s.FindPropertyRelative("startCap"), "Start");
        DrawCap(s.FindPropertyRelative("endCap"), "End");
        GUILayout.EndHorizontal();

        // -- Materials Section --
        DrawSectionHeader("VISUALS & MAPPING");
        EditorGUILayout.PropertyField(s.FindPropertyRelative("uvMapping"), new GUIContent("Mapping Mode"));
        
        GUILayout.BeginHorizontal();
        SerializedProperty tiling = s.FindPropertyRelative("uvTiling");
        tiling.vector2Value = EditorGUILayout.Vector2Field("Tiling", tiling.vector2Value);
        GUILayout.EndHorizontal();

        EditorGUILayout.PropertyField(s.FindPropertyRelative("startColor"));
        EditorGUILayout.PropertyField(s.FindPropertyRelative("endColor"));

        // -- Bones Section --
        DrawSectionHeader("BONE RIGGING");
        SerializedProperty useBones = s.FindPropertyRelative("useBones");
        EditorGUILayout.PropertyField(useBones);
        if (useBones.boolValue)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.IntSlider(s.FindPropertyRelative("bonesPerSegment"), 1, 10, "Bone Density");
            EditorGUILayout.PropertyField(s.FindPropertyRelative("useNestedChain"), new GUIContent("IK Chain Logic"));
            EditorGUILayout.Slider(s.FindPropertyRelative("blendScaler"), 0.1f, 5.0f, "Weight Blend Sharpness");
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawSectionHeader(string text)
    {
        GUILayout.Space(10);
        EditorGUILayout.LabelField(text, EditorStyles.boldLabel);
        Rect rect = GUILayoutUtility.GetRect(0, 2);
        EditorGUI.DrawRect(rect, new Color(1, 1, 1, 0.2f));
        GUILayout.Space(5);
    }

    private void DrawCap(SerializedProperty cap, string label)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField(label, EditorStyles.centeredGreyMiniLabel);
        SerializedProperty t = cap.FindPropertyRelative("type");
        EditorGUILayout.PropertyField(t, GUIContent.none);
        
        if (t.enumValueIndex != 0)
        {
            EditorGUILayout.PropertyField(cap.FindPropertyRelative("scale"), new GUIContent("Size"));
            if (t.enumValueIndex == 3) EditorGUILayout.PropertyField(cap.FindPropertyRelative("segments"), new GUIContent("Res"));
            if (t.enumValueIndex == 4) EditorGUILayout.PropertyField(cap.FindPropertyRelative("sphereResolution"), new GUIContent("Res"));
        }
        EditorGUILayout.EndVertical();
    }

    private Texture2D MakeTex(int width, int height, Color col)
    {
        Color[] pix = new Color[width * height];
        for (int i = 0; i < pix.Length; ++i) pix[i] = col;
        Texture2D result = new Texture2D(width, height);
        result.SetPixels(pix);
        result.Apply();
        return result;
    }

    void OnSceneGUI()
    {
        TubeBuilderRenderer r = (TubeBuilderRenderer)target;
        if (selectedIndex < 0 || selectedIndex >= r.segments.Length) return;
        Undo.RecordObject(r, "Move Points");
        EditorGUI.BeginChangeCheck();
        Vector3 p0 = r.transform.TransformPoint(r.segments[selectedIndex].p0), p1 = r.transform.TransformPoint(r.segments[selectedIndex].p1), p2 = r.transform.TransformPoint(r.segments[selectedIndex].p2);
        p0 = Handles.PositionHandle(p0, Quaternion.identity); p1 = Handles.PositionHandle(p1, Quaternion.identity); p2 = Handles.PositionHandle(p2, Quaternion.identity);
        if (EditorGUI.EndChangeCheck()) {
            r.segments[selectedIndex].p0 = r.transform.InverseTransformPoint(p0); r.segments[selectedIndex].p1 = r.transform.InverseTransformPoint(p1); r.segments[selectedIndex].p2 = r.transform.InverseTransformPoint(p2);
            r.MarkDirty();
        }
    }
}
