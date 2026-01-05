using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using System.Collections.Generic;

[CustomEditor(typeof(TubeBuilderRenderer))]
public class TubeBuilderRendererEditor : Editor
{
    SerializedProperty segmentsProp;
    SerializedProperty gizmoCurveColor;
    SerializedProperty showWeightsDebugProp; // Added for weight heatmap
    ReorderableList segmentsList;
    int selectedIndex = -1;
    static bool s_ShowDebug = false;
    static bool s_AutoRebuild = true;

    void OnEnable()
    {
        segmentsProp = serializedObject.FindProperty("segments");
        gizmoCurveColor = serializedObject.FindProperty("gizmoCurveColor");
        showWeightsDebugProp = serializedObject.FindProperty("showWeightsDebug");
        s_AutoRebuild = EditorPrefs.GetBool("TubeBuilderRenderer_AutoRebuild", true);
        SetupList();
    }

    void OnDisable()
    {
        Tools.hidden = false;
    }

    void SetupList()
    {
        if (segmentsProp == null) return;

        segmentsList = new ReorderableList(serializedObject, segmentsProp, true, true, true, true);

        segmentsList.drawHeaderCallback = delegate(Rect rect)
        {
            EditorGUI.LabelField(rect, "tube segments");
        };

        segmentsList.onAddCallback = delegate(ReorderableList list)
        {
            int index = list.serializedProperty.arraySize;
            list.serializedProperty.arraySize++;
            SerializedProperty newElement = list.serializedProperty.GetArrayElementAtIndex(index);
            InitSegmentDefaults(newElement, index);
            serializedObject.ApplyModifiedProperties();
            selectedIndex = index;
            Tools.hidden = true;
        };

        segmentsList.onRemoveCallback = delegate(ReorderableList list)
        {
            if (EditorUtility.DisplayDialog("delete tube segment", "remove selected segment?", "yes", "no"))
            {
                int idx = list.index;
                ReorderableList.defaultBehaviours.DoRemoveButton(list);
                serializedObject.ApplyModifiedProperties();

                if (segmentsProp.arraySize == 0)
                {
                    selectedIndex = -1;
                    Tools.hidden = false;
                }
                else
                {
                    selectedIndex = Mathf.Clamp(idx - 1, 0, segmentsProp.arraySize - 1);
                    Tools.hidden = selectedIndex >= 0;
                }
            }
        };

        segmentsList.onSelectCallback = delegate(ReorderableList list)
        {
            selectedIndex = list.index;
            Tools.hidden = true;
        };

        segmentsList.drawElementCallback = delegate(Rect rect, int index, bool isActive, bool isFocused)
        {
            SerializedProperty segProp = segmentsProp.GetArrayElementAtIndex(index);
            if (segProp == null) return;

            rect.y += 2f;
            float line = EditorGUIUtility.singleLineHeight;

            SerializedProperty colorProp = segProp.FindPropertyRelative("startColor");
            SerializedProperty nameProp = segProp.FindPropertyRelative("name");

            Rect colorRect = new Rect(rect.x, rect.y + 2f, 30f, line - 4f);
            EditorGUI.DrawRect(colorRect, colorProp.colorValue);

            Rect labelRect = new Rect(colorRect.xMax + 4f, rect.y, rect.width - colorRect.width - 4f, line);
            EditorGUI.LabelField(labelRect, nameProp.stringValue);
        };

        segmentsList.elementHeightCallback = delegate(int index)
        {
            return EditorGUIUtility.singleLineHeight + 6f;
        };
    }

    void InitSegmentDefaults(SerializedProperty segProp, int index)
    {
        segProp.FindPropertyRelative("name").stringValue = "tube " + index;
        segProp.FindPropertyRelative("enabled").boolValue = true;

        segProp.FindPropertyRelative("p0").vector3Value = Vector3.zero;
        segProp.FindPropertyRelative("p1").vector3Value = Vector3.up * 0.5f;
        segProp.FindPropertyRelative("p2").vector3Value = Vector3.up;

        segProp.FindPropertyRelative("startColor").colorValue = Color.white;
        segProp.FindPropertyRelative("endColor").colorValue = Color.white;
        segProp.FindPropertyRelative("colorLerpOffset").floatValue = 0f;
        segProp.FindPropertyRelative("colorLerpScale").floatValue = 1f;

        segProp.FindPropertyRelative("segments").intValue = 6;
        segProp.FindPropertyRelative("radialSegments").intValue = 6;

        segProp.FindPropertyRelative("radiusProfile").animationCurveValue =
            AnimationCurve.Linear(0f, 0.03f, 1f, 0.01f);

        segProp.FindPropertyRelative("capStart").enumValueIndex = (int)TubeBuilderRenderer.TubeCapType.Rounded;
        segProp.FindPropertyRelative("capEnd").enumValueIndex = (int)TubeBuilderRenderer.TubeCapType.Rounded;

        segProp.FindPropertyRelative("bulgePower").floatValue = 1f;
        segProp.FindPropertyRelative("sphereRadius").floatValue = 0.03f;
        segProp.FindPropertyRelative("roundedCapSegments").intValue = 4;
        segProp.FindPropertyRelative("twist").floatValue = 0f;

        segProp.FindPropertyRelative("generateTube").boolValue = true;
        segProp.FindPropertyRelative("colorCutoffSegment").intValue = -1;
        segProp.FindPropertyRelative("capScale").floatValue = 1f;

        // Bone Defaults
        segProp.FindPropertyRelative("useBones").boolValue = false;
        segProp.FindPropertyRelative("bonesPerSegment").intValue = 2;
        segProp.FindPropertyRelative("useNestedChain").boolValue = true;
        segProp.FindPropertyRelative("blendOffset").floatValue = 0f;
        segProp.FindPropertyRelative("blendScaler").floatValue = 1f;
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        TubeBuilderRenderer r = (TubeBuilderRenderer)target;

        EditorGUILayout.LabelField("gizmo", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(gizmoCurveColor, new GUIContent("curve line color"));
        EditorGUILayout.Space();

        EditorGUILayout.LabelField("mesh tools", EditorStyles.boldLabel);
        s_AutoRebuild = EditorGUILayout.Toggle("auto rebuild", s_AutoRebuild);
        EditorPrefs.SetBool("TubeBuilderRenderer_AutoRebuild", s_AutoRebuild);

        // Heatmap toggle
        EditorGUILayout.PropertyField(showWeightsDebugProp, new GUIContent("show bone weights"));

        if (GUILayout.Button("force rebuild"))
        {
            r.MarkDirty();
            r.Rebuild();
        }

        if (GUILayout.Button("build mesh asset..."))
        {
            BuildMeshAsset(r);
        }

        EditorGUILayout.Space();

        EditorGUILayout.LabelField("tube segments", EditorStyles.boldLabel);
        if (segmentsList == null) SetupList();
        segmentsList.DoLayoutList();

        if (selectedIndex >= 0 && selectedIndex < segmentsProp.arraySize)
        {
            if (GUILayout.Button("duplicate segment"))
            {
                DuplicateSegment(selectedIndex);
                return;
            }
        }

        if (GUILayout.Button("deselect segment"))
        {
            selectedIndex = -1;
            Tools.hidden = false;
        }

        if (selectedIndex >= segmentsProp.arraySize) selectedIndex = segmentsProp.arraySize - 1;
        if (segmentsProp.arraySize == 0) selectedIndex = -1;

        EditorGUILayout.Space();

        DrawSelectedSegmentSection();
        EditorGUILayout.Space();

        DrawDebugSection(r);

        serializedObject.ApplyModifiedProperties();
    }

    void DuplicateSegment(int index)
    {
        serializedObject.Update();
        segmentsProp.InsertArrayElementAtIndex(index);
        SerializedProperty newSeg = segmentsProp.GetArrayElementAtIndex(index + 1);
        SerializedProperty oldSeg = segmentsProp.GetArrayElementAtIndex(index);
        newSeg.FindPropertyRelative("name").stringValue = oldSeg.FindPropertyRelative("name").stringValue + " copy";
        selectedIndex = index + 1;
        Tools.hidden = true;
        serializedObject.ApplyModifiedProperties();
    }

    void DrawSelectedSegmentSection()
    {
        if (selectedIndex < 0 || selectedIndex >= segmentsProp.arraySize) return;

        SerializedProperty segProp = segmentsProp.GetArrayElementAtIndex(selectedIndex);
        if (segProp == null) return;

        EditorGUILayout.LabelField("selected tube", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        SerializedProperty nameProp = segProp.FindPropertyRelative("name");
        nameProp.stringValue = EditorGUILayout.TextField("name", nameProp.stringValue);

        SerializedProperty enabledProp = segProp.FindPropertyRelative("enabled");
        enabledProp.boolValue = EditorGUILayout.Toggle("enabled", enabledProp.boolValue);

        // BONE SETTINGS SECTION
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("bone settings", EditorStyles.miniBoldLabel);
        SerializedProperty useBonesProp = segProp.FindPropertyRelative("useBones");
        useBonesProp.boolValue = EditorGUILayout.Toggle("use bones", useBonesProp.boolValue);

        if (useBonesProp.boolValue)
        {
            EditorGUI.indentLevel++;
            SerializedProperty bPerSegProp = segProp.FindPropertyRelative("bonesPerSegment");
            bPerSegProp.intValue = EditorGUILayout.IntSlider("bones per segment", bPerSegProp.intValue, 1, 10);

            SerializedProperty nestedProp = segProp.FindPropertyRelative("useNestedChain");
            nestedProp.boolValue = EditorGUILayout.Toggle("ik nested chain", nestedProp.boolValue);

            SerializedProperty bOffsetProp = segProp.FindPropertyRelative("blendOffset");
            bOffsetProp.floatValue = EditorGUILayout.Slider("blend offset", bOffsetProp.floatValue, -0.5f, 0.5f);

            SerializedProperty bScaleProp = segProp.FindPropertyRelative("blendScaler");
            bScaleProp.floatValue = EditorGUILayout.Slider("blend sharpness", bScaleProp.floatValue, 0.1f, 3.0f);
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.Space();

        SerializedProperty genTubeProp = segProp.FindPropertyRelative("generateTube");
        genTubeProp.boolValue = EditorGUILayout.Toggle("generate tube", genTubeProp.boolValue);

        SerializedProperty p0 = segProp.FindPropertyRelative("p0");
        SerializedProperty p1 = segProp.FindPropertyRelative("p1");
        SerializedProperty p2 = segProp.FindPropertyRelative("p2");

        EditorGUILayout.LabelField("start point (p0)");
        p0.vector3Value = EditorGUILayout.Vector3Field(GUIContent.none, p0.vector3Value);
        EditorGUILayout.LabelField("control point (p1)");
        p1.vector3Value = EditorGUILayout.Vector3Field(GUIContent.none, p1.vector3Value);
        EditorGUILayout.LabelField("end point (p2)");
        p2.vector3Value = EditorGUILayout.Vector3Field(GUIContent.none, p2.vector3Value);

        SerializedProperty segCount = segProp.FindPropertyRelative("segments");
        SerializedProperty radCount = segProp.FindPropertyRelative("radialSegments");
        SerializedProperty twistProp = segProp.FindPropertyRelative("twist");

        segCount.intValue = Mathf.Max(2, EditorGUILayout.IntField("curve segments", segCount.intValue));
        radCount.intValue = Mathf.Max(3, EditorGUILayout.IntField("radial segments", radCount.intValue));
        twistProp.floatValue = EditorGUILayout.FloatField("twist (deg)", twistProp.floatValue);

        SerializedProperty curveProp = segProp.FindPropertyRelative("radiusProfile");
        curveProp.animationCurveValue = EditorGUILayout.CurveField("radius profile", curveProp.animationCurveValue);

        SerializedProperty capStartProp = segProp.FindPropertyRelative("capStart");
        SerializedProperty capEndProp = segProp.FindPropertyRelative("capEnd");

        EditorGUILayout.PropertyField(capStartProp, new GUIContent("start cap"));
        EditorGUILayout.PropertyField(capEndProp, new GUIContent("end cap"));

        SerializedProperty latProp = segProp.FindPropertyRelative("roundedCapSegments");
        SerializedProperty sphereRadiusProp = segProp.FindPropertyRelative("sphereRadius");

        latProp.intValue = Mathf.Max(0, EditorGUILayout.IntField("rounded cap segments", latProp.intValue));
        sphereRadiusProp.floatValue = Mathf.Max(0f, EditorGUILayout.FloatField("sphere radius", sphereRadiusProp.floatValue));

        SerializedProperty capScaleProp = segProp.FindPropertyRelative("capScale");
        capScaleProp.floatValue = EditorGUILayout.FloatField("cap scale", capScaleProp.floatValue);

        EditorGUILayout.PropertyField(segProp.FindPropertyRelative("bulgePower"), new GUIContent("bulge power"));

        EditorGUILayout.LabelField("start color");
        EditorGUILayout.PropertyField(segProp.FindPropertyRelative("startColor"), GUIContent.none);

        EditorGUILayout.LabelField("end color");
        EditorGUILayout.PropertyField(segProp.FindPropertyRelative("endColor"), GUIContent.none);

        SerializedProperty offsetProp = segProp.FindPropertyRelative("colorLerpOffset");
        SerializedProperty scaleProp = segProp.FindPropertyRelative("colorLerpScale");

        offsetProp.floatValue = EditorGUILayout.FloatField("color lerp offset", offsetProp.floatValue);
        scaleProp.floatValue = EditorGUILayout.FloatField("color lerp scale", scaleProp.floatValue);

        SerializedProperty cutoffProp = segProp.FindPropertyRelative("colorCutoffSegment");
        cutoffProp.intValue = EditorGUILayout.IntField("color cutoff segment", cutoffProp.intValue);

        EditorGUILayout.EndVertical();
    }

    void DrawDebugSection(TubeBuilderRenderer r)
    {
        s_ShowDebug = EditorGUILayout.ToggleLeft("show debug info", s_ShowDebug, EditorStyles.boldLabel);
        if (!s_ShowDebug) return;

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        int count = segmentsProp != null ? segmentsProp.arraySize : 0;
        EditorGUILayout.LabelField("tube count: " + count);

        // Check for SMR or MR
        Mesh mesh = null;
        SkinnedMeshRenderer smr = r.GetComponent<SkinnedMeshRenderer>();
        if (smr != null)
        {
            mesh = smr.sharedMesh;
            EditorGUILayout.LabelField("renderer: SkinnedMeshRenderer");
            EditorGUILayout.LabelField("active bones: " + (smr.bones != null ? smr.bones.Length : 0));
        }
        else
        {
            MeshFilter mf = r.GetComponent<MeshFilter>();
            if (mf != null) mesh = mf.sharedMesh;
            EditorGUILayout.LabelField("renderer: MeshRenderer");
        }

        if (mesh != null)
        {
            EditorGUILayout.LabelField("mesh vertices: " + mesh.vertexCount);
            EditorGUILayout.LabelField("mesh triangles: " + (mesh.triangles != null ? mesh.triangles.Length / 3 : 0));
        }
        else EditorGUILayout.LabelField("mesh: (none)");

        EditorGUILayout.EndVertical();
    }

    void BuildMeshAsset(TubeBuilderRenderer r)
    {
        r.Rebuild();
        Mesh meshToSave = null;
        
        SkinnedMeshRenderer smr = r.GetComponent<SkinnedMeshRenderer>();
        if (smr != null) meshToSave = smr.sharedMesh;
        else
        {
            MeshFilter mf = r.GetComponent<MeshFilter>();
            if (mf != null) meshToSave = mf.sharedMesh;
        }

        if (meshToSave == null)
        {
            EditorUtility.DisplayDialog("no mesh", "no mesh available to save.", "ok");
            return;
        }

        string path = EditorUtility.SaveFilePanelInProject("save mesh asset", "TubeMesh", "asset", "save the generated mesh.");
        if (string.IsNullOrEmpty(path)) return;

        Mesh newMesh = Object.Instantiate(meshToSave);
        newMesh.name = "TubeMesh";

        AssetDatabase.CreateAsset(newMesh, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("mesh saved", "mesh asset created successfully.", "ok");
    }

    void OnSceneGUI()
    {
        TubeBuilderRenderer r = (TubeBuilderRenderer)target;
        if (r == null || r.segments == null) return;
        if (segmentsProp == null) return;

        Tools.hidden = selectedIndex >= 0;

        Transform tr = r.transform;
        serializedObject.Update();

        if (selectedIndex >= 0 && selectedIndex < segmentsProp.arraySize)
        {
            SerializedProperty segProp = segmentsProp.GetArrayElementAtIndex(selectedIndex);
            if (segProp != null)
            {
                SerializedProperty p0Prop = segProp.FindPropertyRelative("p0");
                SerializedProperty p1Prop = segProp.FindPropertyRelative("p1");
                SerializedProperty p2Prop = segProp.FindPropertyRelative("p2");

                Vector3 p0 = tr.TransformPoint(p0Prop.vector3Value);
                Vector3 p1 = tr.TransformPoint(p1Prop.vector3Value);
                Vector3 p2 = tr.TransformPoint(p2Prop.vector3Value);

                Handles.color = r.gizmoCurveColor;
                Handles.DrawLine(p0, p1);
                Handles.DrawLine(p1, p2);

                EditorGUI.BeginChangeCheck();
                Vector3 newP0 = Handles.PositionHandle(p0, Quaternion.identity);
                Vector3 newP1 = Handles.PositionHandle(p1, Quaternion.identity);
                Vector3 newP2 = Handles.PositionHandle(p2, Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(r, "move tube control points");
                    p0Prop.vector3Value = tr.InverseTransformPoint(newP0);
                    p1Prop.vector3Value = tr.InverseTransformPoint(newP1);
                    p2Prop.vector3Value = tr.InverseTransformPoint(newP2);
                    serializedObject.ApplyModifiedProperties();
                    r.MarkDirty();
                    if (s_AutoRebuild) r.Rebuild();
                }
            }
        }

        serializedObject.ApplyModifiedProperties();
    }
}
