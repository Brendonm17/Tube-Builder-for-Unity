using UnityEngine;
using UnityEditor;
using UnityEditorInternal;

[CustomEditor(typeof(TubeBuilderRenderer))]
public class TubeBuilderRendererEditor : Editor
{
    private SerializedProperty segmentsProp, tubeMaterialProp, gizmoCurveColorProp, showWeightsDebugProp, autoRebuildProp;
    private ReorderableList list;
    private int selectedIndex = -1;

    void OnEnable() {
        segmentsProp = serializedObject.FindProperty("segments");
        tubeMaterialProp = serializedObject.FindProperty("tubeMaterial");
        gizmoCurveColorProp = serializedObject.FindProperty("gizmoCurveColor");
        showWeightsDebugProp = serializedObject.FindProperty("showWeightsDebug");
        autoRebuildProp = serializedObject.FindProperty("autoRebuild");

        // LIVE UPDATE FIX: Rebuild mesh when the user hits Undo/Redo (catches Curve Window edits)
        Undo.undoRedoPerformed += ForceRebuild;

        list = new ReorderableList(serializedObject, segmentsProp, true, true, true, true);
        list.drawHeaderCallback = delegate(Rect rect) { EditorGUI.LabelField(rect, "Tube Segments"); };
        list.onSelectCallback = delegate(ReorderableList l) { selectedIndex = l.index; Tools.hidden = (selectedIndex >= 0); };
        
        list.onAddCallback = delegate(ReorderableList l) {
            int index = l.serializedProperty.arraySize++;
            SerializedProperty s = l.serializedProperty.GetArrayElementAtIndex(index);
            InitSegmentDefaults(s, index);
            serializedObject.ApplyModifiedProperties();
            ForceRebuild();
        };

        list.drawElementCallback = delegate(Rect rect, int index, bool isActive, bool isFocused) {
            if (index >= segmentsProp.arraySize) return;
            SerializedProperty nameProp = segmentsProp.GetArrayElementAtIndex(index).FindPropertyRelative("name");
            EditorGUI.LabelField(new Rect(rect.x, rect.y + 2, rect.width, 16), "Segment " + index + ": " + nameProp.stringValue, EditorStyles.boldLabel);
        };
    }

    void OnDisable() {
        Undo.undoRedoPerformed -= ForceRebuild;
        Tools.hidden = false;
    }
    void OnInspectorUpdate() {
        Repaint();
    }

    void ForceRebuild() {
        if (target == null) return;
        TubeBuilderRenderer r = (TubeBuilderRenderer)target;
        r.MarkDirty();
        r.Rebuild();
        SceneView.RepaintAll();
    }

    public override void OnInspectorGUI() {
        bool changedExternally = serializedObject.UpdateIfRequiredOrScript();
        
        TubeBuilderRenderer r = (TubeBuilderRenderer)target;
        
        EditorGUILayout.LabelField("Global Settings", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.PropertyField(autoRebuildProp);
        EditorGUILayout.PropertyField(tubeMaterialProp);
        EditorGUILayout.PropertyField(gizmoCurveColorProp);
        EditorGUILayout.PropertyField(showWeightsDebugProp);
        
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Force Rebuild Mesh")) { ForceRebuild(); }
        if (GUILayout.Button("Build Mesh Asset...")) { BuildMeshAsset(r); }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(); 
        list.DoLayoutList();

        EditorGUILayout.BeginHorizontal();
        if (selectedIndex >= 0 && selectedIndex < segmentsProp.arraySize) {
            if (GUILayout.Button("Duplicate Selected")) { DuplicateSegment(selectedIndex); }
        }
        if (GUILayout.Button("Deselect All")) { selectedIndex = -1; Tools.hidden = false; }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        if (selectedIndex >= 0 && selectedIndex < segmentsProp.arraySize) {
            DrawSegmentSettings(segmentsProp.GetArrayElementAtIndex(selectedIndex));
        } else {
            EditorGUILayout.HelpBox("Select a segment from the list to edit its properties.", MessageType.Info);
        }

        if (serializedObject.ApplyModifiedProperties() || GUI.changed || changedExternally) {
            if (r.autoRebuild) ForceRebuild();
        }
    }

    void DuplicateSegment(int index) {
        serializedObject.Update();
        segmentsProp.InsertArrayElementAtIndex(index);
        SerializedProperty newSeg = segmentsProp.GetArrayElementAtIndex(index + 1);
        newSeg.FindPropertyRelative("name").stringValue += " (Copy)";
        
        selectedIndex = index + 1;
        serializedObject.ApplyModifiedProperties();
        ForceRebuild();
    }

    void BuildMeshAsset(TubeBuilderRenderer r) {
        // 1. Force a final rebuild of the procedural mesh
        r.Rebuild();
        GameObject originalGo = r.gameObject;

        // 2. Select Save Location
        string folderPath = EditorUtility.SaveFolderPanel("Select Folder to Save Baked Asset", "Assets", "");
        if (string.IsNullOrEmpty(folderPath)) return;
        
        // Convert absolute path to Unity relative path
        if (!folderPath.Contains(Application.dataPath)) {
            EditorUtility.DisplayDialog("Error", "Please select a folder inside your Assets folder.", "OK");
            return;
        }
        folderPath = "Assets" + folderPath.Substring(Application.dataPath.Length);
        
        string meshPath = folderPath + "/" + originalGo.name + "_Mesh.asset";
        string prefabPath = folderPath + "/" + originalGo.name + "_Prefab.prefab";

        // 3. Save the Mesh Asset First
        // We must save the mesh to disk so the prefab has a permanent file to reference
        Mesh meshToSave = null;
        SkinnedMeshRenderer smr = originalGo.GetComponent<SkinnedMeshRenderer>();
        MeshFilter mf = originalGo.GetComponent<MeshFilter>();

        if (smr != null) meshToSave = smr.sharedMesh;
        else if (mf != null) meshToSave = mf.sharedMesh;

        if (meshToSave == null) {
            EditorUtility.DisplayDialog("Error", "No mesh found to bake!", "OK");
            return;
        }

        Mesh meshAsset = Instantiate(meshToSave); // Deep copy
        AssetDatabase.CreateAsset(meshAsset, meshPath);

        // 4. Create the "Clean" Clone
        // We instantiate the whole object so the bone hierarchy is preserved
        GameObject tempClone = (GameObject)Instantiate(originalGo);
        tempClone.name = originalGo.name;

        // 5. Remove the TubeBuilderRenderer script from the clone
        TubeBuilderRenderer scriptOnClone = tempClone.GetComponent<TubeBuilderRenderer>();
        if (scriptOnClone != null) {
            DestroyImmediate(scriptOnClone);
        }

        // 6. Point the clone's renderer to the new saved Mesh Asset
        SkinnedMeshRenderer cloneSmr = tempClone.GetComponent<SkinnedMeshRenderer>();
        MeshFilter cloneMf = tempClone.GetComponent<MeshFilter>();

        if (cloneSmr != null) cloneSmr.sharedMesh = meshAsset;
        if (cloneMf != null) cloneMf.sharedMesh = meshAsset;

        // 7. Save as Prefab
    #if UNITY_2018_3_OR_NEWER
        PrefabUtility.SaveAsPrefabAsset(tempClone, prefabPath);
    #else
        // Unity 2017.4 and 2018.2 use this:
        PrefabUtility.CreatePrefab(prefabPath, tempClone);
    #endif

        // 8. Cleanup
        DestroyImmediate(tempClone); // Remove the temp object from the scene
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Success", "Baked Prefab and Mesh saved to: " + folderPath, "OK");
    }

    void DrawSegmentSettings(SerializedProperty s) {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.PropertyField(s.FindPropertyRelative("name"));
        EditorGUILayout.PropertyField(s.FindPropertyRelative("enabled"));
        EditorGUILayout.PropertyField(s.FindPropertyRelative("generateTube"));
        EditorGUILayout.PropertyField(s.FindPropertyRelative("connectToPrevious"));
        
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Path Points", EditorStyles.miniBoldLabel);
        EditorGUILayout.PropertyField(s.FindPropertyRelative("p0"));
        EditorGUILayout.PropertyField(s.FindPropertyRelative("p1"));
        EditorGUILayout.PropertyField(s.FindPropertyRelative("p2"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Mesh Geometry", EditorStyles.miniBoldLabel);
        EditorGUILayout.PropertyField(s.FindPropertyRelative("segments"));
        EditorGUILayout.PropertyField(s.FindPropertyRelative("radialSegments"));
        EditorGUILayout.PropertyField(s.FindPropertyRelative("twist"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Profiles", EditorStyles.miniBoldLabel);
        
        SerializedProperty curveProp1 = s.FindPropertyRelative("radiusProfile");
        EditorGUILayout.PropertyField(curveProp1, new GUIContent("Radius Profile"));
        if (GUILayout.Button("Open Radius Editor")) {
            serializedObject.ApplyModifiedProperties(); 
            TubeCurveEditorWindow.Open(curveProp1);
        }

        SerializedProperty curveProp2 = s.FindPropertyRelative("radialShapeCurve");
        EditorGUILayout.PropertyField(curveProp2, new GUIContent("Radial Shape Curve"));
        if (GUILayout.Button("Open Radial Shape Editor")) {
            serializedObject.ApplyModifiedProperties();
            TubeCurveEditorWindow.Open(curveProp2);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Visuals & Colors", EditorStyles.miniBoldLabel);
        EditorGUILayout.PropertyField(s.FindPropertyRelative("startColor"));
        EditorGUILayout.PropertyField(s.FindPropertyRelative("endColor"));
        EditorGUILayout.PropertyField(s.FindPropertyRelative("uvMapping"));
        EditorGUILayout.PropertyField(s.FindPropertyRelative("uvTiling"));
        EditorGUILayout.PropertyField(s.FindPropertyRelative("uvOffset"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("End Caps", EditorStyles.miniBoldLabel);
        DrawCap(s.FindPropertyRelative("startCap"), "Start Cap");
        DrawCap(s.FindPropertyRelative("endCap"), "End Cap");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Skinning / Bones", EditorStyles.miniBoldLabel);
        SerializedProperty useBones = s.FindPropertyRelative("useBones");
        EditorGUILayout.PropertyField(useBones);
        if (useBones.boolValue) {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(s.FindPropertyRelative("bonesPerSegment"));
            EditorGUILayout.PropertyField(s.FindPropertyRelative("useNestedChain"));
            EditorGUILayout.PropertyField(s.FindPropertyRelative("blendOffset"));
            EditorGUILayout.PropertyField(s.FindPropertyRelative("blendScaler"));
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndVertical();
    }

    void DrawCap(SerializedProperty cap, string label) {
        EditorGUILayout.LabelField(label, EditorStyles.miniLabel); 
        EditorGUI.indentLevel++;
        SerializedProperty type = cap.FindPropertyRelative("type"); 
        EditorGUILayout.PropertyField(type);
        if (type.enumValueIndex != 0) {
            EditorGUILayout.PropertyField(cap.FindPropertyRelative("scale"));
            if (type.enumValueIndex == 3) {
                EditorGUILayout.PropertyField(cap.FindPropertyRelative("bulge"));
                EditorGUILayout.PropertyField(cap.FindPropertyRelative("segments"));
            }
            if (type.enumValueIndex == 4) {
                EditorGUILayout.PropertyField(cap.FindPropertyRelative("sphereRadius"));
                EditorGUILayout.PropertyField(cap.FindPropertyRelative("sphereResolution"));
            }
        }
        EditorGUI.indentLevel--;
    }

    void OnSceneGUI() {
        TubeBuilderRenderer r = (TubeBuilderRenderer)target;
        if (selectedIndex < 0 || selectedIndex >= r.segments.Length) return;

        EditorGUI.BeginChangeCheck();
        Transform t = r.transform;
        Vector3 p0 = t.TransformPoint(r.segments[selectedIndex].p0);
        Vector3 p1 = t.TransformPoint(r.segments[selectedIndex].p1);
        Vector3 p2 = t.TransformPoint(r.segments[selectedIndex].p2);

        Handles.color = r.gizmoCurveColor;
        Handles.DrawLine(p0, p1); Handles.DrawLine(p1, p2);
        Vector3 n0 = Handles.PositionHandle(p0, Quaternion.identity);
        Vector3 n1 = Handles.PositionHandle(p1, Quaternion.identity);
        Vector3 n2 = Handles.PositionHandle(p2, Quaternion.identity);

        if (EditorGUI.EndChangeCheck()) {
            Undo.RecordObject(r, "Move Path Points");
            r.segments[selectedIndex].p0 = t.InverseTransformPoint(n0);
            r.segments[selectedIndex].p1 = t.InverseTransformPoint(n1);
            r.segments[selectedIndex].p2 = t.InverseTransformPoint(n2);
            
            if (r.segments[selectedIndex].connectToPrevious && selectedIndex > 0) {
                r.segments[selectedIndex - 1].p2 = r.segments[selectedIndex].p0;
            }
            r.MarkDirty();
            if (r.autoRebuild) r.Rebuild();
        }
    }

    void InitSegmentDefaults(SerializedProperty s, int index) {
        s.FindPropertyRelative("name").stringValue = "Segment " + index;
        s.FindPropertyRelative("enabled").boolValue = true;
        s.FindPropertyRelative("p2").vector3Value = Vector3.up;
        s.FindPropertyRelative("segments").intValue = 8;
        s.FindPropertyRelative("radialSegments").intValue = 8;
        s.FindPropertyRelative("uvTiling").vector2Value = Vector2.one;
        s.FindPropertyRelative("generateTube").boolValue = true;
        s.FindPropertyRelative("startColor").colorValue = Color.white;
        s.FindPropertyRelative("endColor").colorValue = Color.white;
        s.FindPropertyRelative("bonesPerSegment").intValue = 2;
        s.FindPropertyRelative("blendScaler").floatValue = 1f;
        s.FindPropertyRelative("radialShapeCurve").animationCurveValue = AnimationCurve.Linear(0,1,1,1);
        s.FindPropertyRelative("radiusProfile").animationCurveValue = AnimationCurve.Linear(0,0.05f,1,0.05f);
        
        SerializedProperty sc = s.FindPropertyRelative("startCap");
        sc.FindPropertyRelative("type").enumValueIndex = 3; 
        sc.FindPropertyRelative("scale").floatValue = 1f;
        sc.FindPropertyRelative("bulge").floatValue = 1f;
        sc.FindPropertyRelative("segments").intValue = 4;
        sc.FindPropertyRelative("sphereRadius").floatValue = 0.05f;
        sc.FindPropertyRelative("sphereResolution").intValue = 8;

        SerializedProperty ec = s.FindPropertyRelative("endCap");
        ec.FindPropertyRelative("type").enumValueIndex = 3; 
        ec.FindPropertyRelative("scale").floatValue = 1f;
        ec.FindPropertyRelative("bulge").floatValue = 1f;
        ec.FindPropertyRelative("segments").intValue = 4;
        ec.FindPropertyRelative("sphereRadius").floatValue = 0.05f;
        ec.FindPropertyRelative("sphereResolution").intValue = 8;
    }
}
