// TubeCurveEditorWindow.cs
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class TubeCurveEditorWindow : EditorWindow
{
    enum TangentMode
    {
        Auto,
        FreeSmooth,
        Broken,
        Linear,
        Constant
    }

    SerializedProperty boundProperty;
    AnimationCurve curve;
    List<TangentMode> keyModes = new List<TangentMode>();

    float minTime = 0f;
    float maxTime = 1f;
    float minValue = 0f;
    float maxValue = 0.1f;

    Vector2 pan = Vector2.zero;
    float zoomX = 1f;
    float zoomY = 1f;

    bool isPanning = false;
    Vector2 lastMouse;

    int selectedKeyIndex = -1;
    bool isDraggingKey = false;
    bool isDraggingInTangent = false;
    bool isDraggingOutTangent = false;

    const float TANGENT_HANDLE_LENGTH = 40f;
    const float GRID_MAJOR = 0.1f;
    const float GRID_MINOR = 0.02f;

    bool showKeyList = false;
    AnimationCurve clipboardCurve = null;

    const string PREF_PREFIX = "TubeCurveEditor_";

    public static void Open(SerializedProperty curveProp)
    {
        TubeCurveEditorWindow win = CreateInstance<TubeCurveEditorWindow>();
        win.boundProperty = curveProp;
        win.curve = new AnimationCurve(curveProp.animationCurveValue.keys);
        win.SyncKeyModesFromCurve();
        win.titleContent = new GUIContent("radius profile");
        win.minSize = new Vector2(500, 350);
        win.ShowUtility();
        win.Focus();
    }

    void OnEnable()
    {
        if (boundProperty != null)
        {
            curve = new AnimationCurve(boundProperty.animationCurveValue.keys);
            SyncKeyModesFromCurve();
        }
        LoadState();
    }

    void OnDestroy()
    {
        SaveState();
        if (boundProperty != null && curve != null)
        {
            boundProperty.animationCurveValue = new AnimationCurve(curve.keys);
            boundProperty.serializedObject.ApplyModifiedProperties();
        }
    }

    void SaveState()
    {
        EditorPrefs.SetFloat(PREF_PREFIX + "minTime", minTime);
        EditorPrefs.SetFloat(PREF_PREFIX + "maxTime", maxTime);
        EditorPrefs.SetFloat(PREF_PREFIX + "minValue", minValue);
        EditorPrefs.SetFloat(PREF_PREFIX + "maxValue", maxValue);
        EditorPrefs.SetFloat(PREF_PREFIX + "zoomX", zoomX);
        EditorPrefs.SetFloat(PREF_PREFIX + "zoomY", zoomY);
        EditorPrefs.SetFloat(PREF_PREFIX + "panX", pan.x);
        EditorPrefs.SetFloat(PREF_PREFIX + "panY", pan.y);
    }

    void LoadState()
    {
        minTime = EditorPrefs.GetFloat(PREF_PREFIX + "minTime", 0f);
        maxTime = EditorPrefs.GetFloat(PREF_PREFIX + "maxTime", 1f);
        minValue = EditorPrefs.GetFloat(PREF_PREFIX + "minValue", 0f);
        maxValue = EditorPrefs.GetFloat(PREF_PREFIX + "maxValue", 0.1f);
        zoomX = EditorPrefs.GetFloat(PREF_PREFIX + "zoomX", 1f);
        zoomY = EditorPrefs.GetFloat(PREF_PREFIX + "zoomY", 1f);
        pan = new Vector2(
            EditorPrefs.GetFloat(PREF_PREFIX + "panX", 0f),
            EditorPrefs.GetFloat(PREF_PREFIX + "panY", 0f)
        );
    }

    void OnGUI()
    {
        if (boundProperty == null)
        {
            EditorGUILayout.HelpBox("curve reference lost.", MessageType.Error);
            if (GUILayout.Button("close")) Close();
            return;
        }

        DrawToolbar();

        GUILayout.BeginHorizontal();
        DrawViewport();
        DrawKeyListPanel();
        GUILayout.EndHorizontal();

        if (GUI.changed && curve != null)
        {
            boundProperty.animationCurveValue = new AnimationCurve(curve.keys);
            boundProperty.serializedObject.ApplyModifiedProperties();
        }
    }

	float usedWidth = 0f;

	// helper to begin a new toolbar row
	void NewRow()
	{
		GUILayout.EndHorizontal();
		GUILayout.BeginHorizontal(EditorStyles.toolbar);
		usedWidth = 0f;
	}

	void DrawToolbar()
	{
		float availableWidth = position.width - 20f;
		usedWidth = 0f;

		GUILayout.BeginHorizontal(EditorStyles.toolbar);

		// x axis fields
		GUIContent xLabel = new GUIContent("x axis:");
		Vector2 xSize = EditorStyles.toolbar.CalcSize(xLabel);
		if (usedWidth + xSize.x > availableWidth) NewRow();
		GUILayout.Label(xLabel, GUILayout.Width(50));
		usedWidth += 50;

		float newMinTime = EditorGUILayout.FloatField(minTime, GUILayout.Width(60));
		usedWidth += 60;
		if (usedWidth > availableWidth) { minTime = newMinTime; NewRow(); } else minTime = newMinTime;

		float newMaxTime = EditorGUILayout.FloatField(maxTime, GUILayout.Width(60));
		usedWidth += 60;
		if (usedWidth > availableWidth) { maxTime = newMaxTime; NewRow(); } else maxTime = newMaxTime;

		// y axis fields
		GUIContent yLabel = new GUIContent("y axis:");
		Vector2 ySize = EditorStyles.toolbar.CalcSize(yLabel);
		if (usedWidth + ySize.x > availableWidth) NewRow();
		GUILayout.Label(yLabel, GUILayout.Width(50));
		usedWidth += 50;

		float newMinValue = EditorGUILayout.FloatField(minValue, GUILayout.Width(60));
		usedWidth += 60;
		if (usedWidth > availableWidth) { minValue = newMinValue; NewRow(); } else minValue = newMinValue;

		float newMaxValue = EditorGUILayout.FloatField(maxValue, GUILayout.Width(60));
		usedWidth += 60;
		if (usedWidth > availableWidth) { maxValue = newMaxValue; NewRow(); } else maxValue = newMaxValue;

		// reset view
		Vector2 resetSize = EditorStyles.toolbarButton.CalcSize(new GUIContent("reset view"));
		if (usedWidth + resetSize.x > availableWidth) NewRow();
		if (GUILayout.Button("reset view", EditorStyles.toolbarButton, GUILayout.Width(resetSize.x)))
		{
			minTime = 0f;
			maxTime = 1f;
			minValue = 0f;
			maxValue = 0.1f;
			zoomX = zoomY = 1f;
			pan = Vector2.zero;
		}
		usedWidth += resetSize.x;

		// auto-fit
		Vector2 autoSize = EditorStyles.toolbarButton.CalcSize(new GUIContent("auto-fit"));
		if (usedWidth + autoSize.x > availableWidth) NewRow();
		if (GUILayout.Button("auto-fit", EditorStyles.toolbarButton, GUILayout.Width(autoSize.x)))
			AutoFit();
		usedWidth += autoSize.x;

		// presets dropdown
		Vector2 presetSize = EditorStyles.toolbarDropDown.CalcSize(new GUIContent("presets"));
		if (usedWidth + presetSize.x > availableWidth) NewRow();
		if (GUILayout.Button("presets", EditorStyles.toolbarDropDown, GUILayout.Width(presetSize.x)))
		{
			GenericMenu menu = new GenericMenu();
			menu.AddItem(new GUIContent("straight"), false, () => ApplyPreset_Straight());
			menu.AddItem(new GUIContent("taper"), false, () => ApplyPreset_Taper());
			menu.AddItem(new GUIContent("bulb"), false, () => ApplyPreset_Bulb());
			menu.AddItem(new GUIContent("pinch"), false, () => ApplyPreset_Pinch());
			menu.AddItem(new GUIContent("thin start"), false, () => ApplyPreset_ThinStart());
			menu.AddItem(new GUIContent("thin end"), false, () => ApplyPreset_ThinEnd());
			menu.DropDown(new Rect(5, 20, 0, 0));
		}
		usedWidth += presetSize.x;

		// copy
		Vector2 copySize = EditorStyles.toolbarButton.CalcSize(new GUIContent("copy"));
		if (usedWidth + copySize.x > availableWidth) NewRow();
		if (GUILayout.Button("copy", EditorStyles.toolbarButton, GUILayout.Width(copySize.x)))
			clipboardCurve = new AnimationCurve(curve.keys);
		usedWidth += copySize.x;

		// paste
		GUI.enabled = clipboardCurve != null;
		Vector2 pasteSize = EditorStyles.toolbarButton.CalcSize(new GUIContent("paste"));
		if (usedWidth + pasteSize.x > availableWidth) NewRow();
		if (GUILayout.Button("paste", EditorStyles.toolbarButton, GUILayout.Width(pasteSize.x)))
		{
			curve = new AnimationCurve(clipboardCurve.keys);
			SyncKeyModesFromCurve();
			GUI.changed = true;
		}
		GUI.enabled = true;
		usedWidth += pasteSize.x;

		// key mode buttons (auto, smooth, broken, linear, const)
		if (selectedKeyIndex >= 0 && selectedKeyIndex < curve.length)
		{
			string[] modes = { "auto", "smooth", "broken", "linear", "const" };
			foreach (string m in modes)
			{
				Vector2 size = EditorStyles.toolbarButton.CalcSize(new GUIContent(m));
				if (usedWidth + size.x > availableWidth) NewRow();

				if (GUILayout.Button(m, EditorStyles.toolbarButton, GUILayout.Width(size.x)))
				{
					switch (m)
					{
						case "auto": SetKeyMode(selectedKeyIndex, TangentMode.Auto); break;
						case "smooth": SetKeyMode(selectedKeyIndex, TangentMode.FreeSmooth); break;
						case "broken": SetKeyMode(selectedKeyIndex, TangentMode.Broken); break;
						case "linear": SetKeyMode(selectedKeyIndex, TangentMode.Linear); break;
						case "const": SetKeyMode(selectedKeyIndex, TangentMode.Constant); break;
					}
					GUI.changed = true;
				}
				usedWidth += size.x;
			}
		}
		else
		{
			Vector2 noKeySize = EditorStyles.toolbar.CalcSize(new GUIContent("no key selected"));
			if (usedWidth + noKeySize.x > availableWidth) NewRow();
			GUILayout.Label("no key selected", GUILayout.Width(noKeySize.x));
			usedWidth += noKeySize.x;
		}

		// keys toggle
		Vector2 keysSize = EditorStyles.toolbarButton.CalcSize(new GUIContent("keys"));
		if (usedWidth + keysSize.x > availableWidth) NewRow();
		showKeyList = GUILayout.Toggle(showKeyList, "keys", EditorStyles.toolbarButton, GUILayout.Width(keysSize.x));
		usedWidth += keysSize.x;

		GUILayout.FlexibleSpace();

		// close button
		Vector2 closeSize = EditorStyles.toolbarButton.CalcSize(new GUIContent("close"));
		if (usedWidth + closeSize.x > availableWidth) NewRow();
		if (GUILayout.Button("close", EditorStyles.toolbarButton, GUILayout.Width(closeSize.x)))
			Close();

		GUILayout.EndHorizontal();
	}

    void DrawViewport()
    {
        Rect rect = GUILayoutUtility.GetRect(10, 10000, 10, 10000, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        EditorGUI.DrawRect(rect, new Color(0.13f, 0.13f, 0.13f));

        HandleInput(rect);
        DrawGrid(rect);
        DrawAxes(rect);
        DrawAxisLabels(rect);
        DrawCurve(rect);
        DrawTangentsAndKeys(rect);
    }

    void DrawKeyListPanel()
    {
        if (!showKeyList || curve == null)
            return;

        GUILayout.BeginVertical("box", GUILayout.Width(320));
        GUILayout.Label("keyframes", EditorStyles.boldLabel);

        for (int i = 0; i < curve.length; i++)
        {
            Keyframe k = curve.keys[i];

            GUILayout.BeginHorizontal();

            if (GUILayout.Toggle(selectedKeyIndex == i, i.ToString(), GUILayout.Width(30)))
                selectedKeyIndex = i;

            float newTime = EditorGUILayout.FloatField(k.time, GUILayout.Width(60));
            float newValue = EditorGUILayout.FloatField(k.value, GUILayout.Width(60));

            GUILayout.Label(keyModes[i].ToString(), GUILayout.Width(80));

            if (GUILayout.Button("x", GUILayout.Width(20)))
            {
                RemoveKeyAt(i);
                if (selectedKeyIndex == i) selectedKeyIndex = -1;
                GUI.changed = true;
                GUILayout.EndHorizontal();
                break;
            }

            GUILayout.EndHorizontal();

            if (newTime != k.time || newValue != k.value)
            {
                k.time = newTime;
                k.value = newValue;
                curve.MoveKey(i, k);
                RecomputeKeyTangents(i);
                GUI.changed = true;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("mode:", GUILayout.Width(40));

            if (GUILayout.Button("auto", GUILayout.Width(45)))
            {
                SetKeyMode(i, TangentMode.Auto);
                GUI.changed = true;
            }
            if (GUILayout.Button("smooth", GUILayout.Width(60)))
            {
                SetKeyMode(i, TangentMode.FreeSmooth);
                GUI.changed = true;
            }
            if (GUILayout.Button("broken", GUILayout.Width(60)))
            {
                SetKeyMode(i, TangentMode.Broken);
                GUI.changed = true;
            }
            if (GUILayout.Button("linear", GUILayout.Width(55)))
            {
                SetKeyMode(i, TangentMode.Linear);
                GUI.changed = true;
            }
            if (GUILayout.Button("const", GUILayout.Width(50)))
            {
                SetKeyMode(i, TangentMode.Constant);
                GUI.changed = true;
            }

            GUILayout.EndHorizontal();
        }

        GUILayout.EndVertical();
    }

    void DrawGrid(Rect rect)
    {
        Handles.BeginGUI();

        Color minor = new Color(1, 1, 1, 0.05f);
        Color major = new Color(1, 1, 1, 0.12f);

        DrawGridLines(rect, GRID_MINOR, minor);
        DrawGridLines(rect, GRID_MAJOR, major);

        Handles.EndGUI();
    }

    void DrawGridLines(Rect rect, float spacing, Color color)
    {
        Handles.color = color;

        float startT = Mathf.Floor((minTime + pan.x) / spacing) * spacing;
        float endT = maxTime + pan.x;

        for (float t = startT; t <= endT; t += spacing)
        {
            float x = Mathf.InverseLerp(minTime, maxTime, t - pan.x) * rect.width + rect.x;
            Handles.DrawLine(new Vector3(x, rect.y), new Vector3(x, rect.yMax));
        }

        float startV = Mathf.Floor((minValue + pan.y) / spacing) * spacing;
        float endV = maxValue + pan.y;

        for (float v = startV; v <= endV; v += spacing)
        {
            float y = Mathf.InverseLerp(maxValue, minValue, v - pan.y) * rect.height + rect.y;
            Handles.DrawLine(new Vector3(rect.x, y), new Vector3(rect.xMax, y));
        }
    }

    void DrawAxes(Rect rect)
    {
        Handles.BeginGUI();
        Handles.color = new Color(1f, 1f, 1f, 0.4f);

        if (minTime + pan.x <= 0 && maxTime + pan.x >= 0)
        {
            float x = Mathf.InverseLerp(minTime, maxTime, 0 - pan.x) * rect.width + rect.x;
            Handles.DrawLine(new Vector3(x, rect.y), new Vector3(x, rect.yMax));
        }

        if (minValue + pan.y <= 0 && maxValue + pan.y >= 0)
        {
            float y = Mathf.InverseLerp(maxValue, minValue, 0 - pan.y) * rect.height + rect.y;
            Handles.DrawLine(new Vector3(rect.x, y), new Vector3(rect.xMax, y));
        }

        Handles.EndGUI();
    }

    void DrawAxisLabels(Rect rect)
    {
        GUIStyle label = new GUIStyle(EditorStyles.miniLabel);
        label.normal.textColor = new Color(1f, 1f, 1f, 0.7f);

        float startT = Mathf.Floor((minTime + pan.x) / GRID_MAJOR) * GRID_MAJOR;
        float endT = maxTime + pan.x;

        for (float t = startT; t <= endT; t += GRID_MAJOR)
        {
            float x = Mathf.InverseLerp(minTime, maxTime, t - pan.x) * rect.width + rect.x;
            GUI.Label(new Rect(x + 2, rect.yMax - 18, 40, 20), t.ToString("0.0"), label);
        }

        float startV = Mathf.Floor((minValue + pan.y) / GRID_MAJOR) * GRID_MAJOR;
        float endV = maxValue + pan.y;

        for (float v = startV; v <= endV; v += GRID_MAJOR)
        {
            float y = Mathf.InverseLerp(maxValue, minValue, v - pan.y) * rect.height + rect.y;
            GUI.Label(new Rect(rect.x + 2, y - 8, 40, 20), v.ToString("0.00"), label);
        }
    }

    void DrawCurve(Rect rect)
    {
        if (curve == null || curve.length == 0)
            return;

        Handles.BeginGUI();
        Handles.color = Color.green;

        Vector3 prev = Vector3.zero;
        bool hasPrev = false;

        for (int i = 0; i <= 200; i++)
        {
            float t = Mathf.Lerp(minTime, maxTime, i / 200f);
            float v = EvaluateWithConstantSupport(t);

            float px = Mathf.InverseLerp(minTime, maxTime, t - pan.x) * rect.width + rect.x;
            float py = Mathf.InverseLerp(maxValue, minValue, v - pan.y) * rect.height + rect.y;

            Vector3 p = new Vector3(px, py, 0);

            if (hasPrev)
                Handles.DrawLine(prev, p);

            prev = p;
            hasPrev = true;
        }

        Handles.EndGUI();
    }

    float EvaluateWithConstantSupport(float t)
    {
        if (curve.length == 0)
            return 0f;

        if (curve.length == 1)
            return curve.keys[0].value;

        for (int i = 0; i < curve.length - 1; i++)
        {
            Keyframe k0 = curve.keys[i];
            Keyframe k1 = curve.keys[i + 1];

            if (t >= k0.time && t <= k1.time)
            {
                if (keyModes[i] == TangentMode.Constant)
                    return k0.value;

                return curve.Evaluate(t);
            }
        }

        if (t < curve.keys[0].time)
            return curve.keys[0].value;

        if (t > curve.keys[curve.length - 1].time)
        {
            int last = curve.length - 1;
            if (keyModes[last] == TangentMode.Constant)
                return curve.keys[last].value;
            return curve.Evaluate(t);
        }

        return curve.Evaluate(t);
    }

    void DrawTangentsAndKeys(Rect rect)
    {
        if (curve == null) return;

        Handles.BeginGUI();

        for (int i = 0; i < curve.length; i++)
        {
            Keyframe k = curve.keys[i];

            Vector2 keyPos = new Vector2(
                Mathf.InverseLerp(minTime, maxTime, k.time - pan.x) * rect.width + rect.x,
                Mathf.InverseLerp(maxValue, minValue, k.value - pan.y) * rect.height + rect.y
            );

            bool isSelected = (i == selectedKeyIndex);

            {
                Vector2 inHandle = GetTangentHandlePosition(rect, i, false);
                Handles.color = new Color(1f, 0.5f, 0f, isSelected ? 1f : 0.6f);
                Handles.DrawLine(keyPos, inHandle);
                DrawHandleDot(inHandle, isSelected ? new Color(1f, 0.6f, 0.2f) : new Color(1f, 0.5f, 0f));
            }

            {
                Vector2 outHandle = GetTangentHandlePosition(rect, i, true);
                Handles.color = new Color(0f, 0.6f, 1f, isSelected ? 1f : 0.6f);
                Handles.DrawLine(keyPos, outHandle);
                DrawHandleDot(outHandle, isSelected ? new Color(0.4f, 0.8f, 1f) : new Color(0f, 0.6f, 1f));
            }

            float size = isSelected ? 8f : 6f;
            Rect r = new Rect(keyPos.x - size * 0.5f, keyPos.y - size * 0.5f, size, size);
            Color c = isSelected ? Color.yellow : Color.white;
            EditorGUI.DrawRect(r, c);
        }

        Handles.EndGUI();
    }

    void DrawHandleDot(Vector2 pos, Color col)
    {
        Rect r = new Rect(pos.x - 3, pos.y - 3, 6, 6);
        EditorGUI.DrawRect(r, col);
    }

    Vector2 GetTangentHandlePosition(Rect rect, int keyIndex, bool outTangent)
    {
        if (curve == null || keyIndex < 0 || keyIndex >= curve.length)
            return Vector2.zero;

        Keyframe k = curve.keys[keyIndex];

        float t0 = k.time;
        float v0 = k.value;

        Vector2 keyPos = new Vector2(
            Mathf.InverseLerp(minTime, maxTime, t0 - pan.x) * rect.width + rect.x,
            Mathf.InverseLerp(maxValue, minValue, v0 - pan.y) * rect.height + rect.y
        );

        float tangent = outTangent ? k.outTangent : k.inTangent;
        if (float.IsNaN(tangent) || float.IsInfinity(tangent))
            tangent = 0f;

        float dt = (maxTime - minTime) * 0.05f;
        if (!outTangent)
            dt = -dt;

        float t1 = t0 + dt;
        float v1 = v0 + tangent * dt;

        Vector2 handlePos = new Vector2(
            Mathf.InverseLerp(minTime, maxTime, t1 - pan.x) * rect.width + rect.x,
            Mathf.InverseLerp(maxValue, minValue, v1 - pan.y) * rect.height + rect.y
        );

        Vector2 dir = (handlePos - keyPos);
        if (dir.sqrMagnitude < 1e-6f)
            dir = new Vector2(outTangent ? 1f : -1f, 0f);

        dir.Normalize();
        return keyPos + dir * TANGENT_HANDLE_LENGTH;
    }

    bool TryBeginDragKeyOrTangent(Rect rect, Vector2 mousePos)
    {
        int keyIdx = FindKeyAtPosition(rect, mousePos, 8f);
        if (keyIdx >= 0)
        {
            selectedKeyIndex = keyIdx;
            isDraggingKey = true;
            isDraggingInTangent = false;
            isDraggingOutTangent = false;
            return true;
        }

        int closestKey = -1;
        bool closestIsOut = false;
        float bestDist = 1e9f;
        float handleRadius = 8f;

        if (curve != null)
        {
            for (int i = 0; i < curve.length; i++)
            {
                Vector2 inPos = GetTangentHandlePosition(rect, i, false);
                float dIn = Vector2.Distance(mousePos, inPos);
                if (dIn < bestDist && dIn <= handleRadius)
                {
                    bestDist = dIn;
                    closestKey = i;
                    closestIsOut = false;
                }

                Vector2 outPos = GetTangentHandlePosition(rect, i, true);
                float dOut = Vector2.Distance(mousePos, outPos);
                if (dOut < bestDist && dOut <= handleRadius)
                {
                    bestDist = dOut;
                    closestKey = i;
                    closestIsOut = true;
                }
            }
        }

        if (closestKey >= 0)
        {
            selectedKeyIndex = closestKey;
            isDraggingKey = false;
            isDraggingInTangent = !closestIsOut;
            isDraggingOutTangent = closestIsOut;
            return true;
        }

        return false;
    }

    int FindKeyAtPosition(Rect rect, Vector2 mousePos, float radius)
    {
        if (curve == null) return -1;

        float r2 = radius * radius;

        for (int i = 0; i < curve.length; i++)
        {
            Keyframe k = curve.keys[i];

            Vector2 keyPos = new Vector2(
                Mathf.InverseLerp(minTime, maxTime, k.time - pan.x) * rect.width + rect.x,
                Mathf.InverseLerp(maxValue, minValue, k.value - pan.y) * rect.height + rect.y
            );

            if ((keyPos - mousePos).sqrMagnitude <= r2)
                return i;
        }

        return -1;
    }

    void SyncKeyModesFromCurve()
    {
        keyModes.Clear();
        if (curve == null) return;

        for (int i = 0; i < curve.length; i++)
            keyModes.Add(TangentMode.Auto);

        for (int i = 0; i < curve.length; i++)
            RecomputeKeyTangents(i);
    }

    int AddKeyWithMode(float time, float value, TangentMode mode)
    {
        int idx = curve.AddKey(time, value);
        SortKeysAndSyncModes();
        int found = FindKeyByTimeValue(time, value, 0.0001f, 0.0001f);
        if (found >= 0)
        {
            keyModes[found] = mode;
            RecomputeKeyTangents(found);
            return found;
        }
        return idx;
    }

    void RemoveKeyAt(int index)
    {
        if (index < 0 || index >= curve.length) return;

        curve.RemoveKey(index);
        keyModes.RemoveAt(index);
    }

    void SortKeysAndSyncModes()
    {
        if (curve == null || curve.length == 0) return;

        var keys = curve.keys;
        System.Array.Sort(keys, (a, b) => a.time.CompareTo(b.time));
        curve.keys = keys;

        List<TangentMode> newModes = new List<TangentMode>(curve.length);
        for (int i = 0; i < curve.length; i++)
        {
            float t = curve.keys[i].time;
            float v = curve.keys[i].value;
            int old = FindKeyByTimeValue(t, v, 0.0001f, 0.0001f);
            if (old >= 0 && old < keyModes.Count)
                newModes.Add(keyModes[old]);
            else
                newModes.Add(TangentMode.Auto);
        }
        keyModes = newModes;
    }

    int FindKeyByTimeValue(float time, float value, float epsTime, float epsValue)
    {
        for (int i = 0; i < curve.length; i++)
        {
            Keyframe k = curve.keys[i];
            if (Mathf.Abs(k.time - time) < epsTime && Mathf.Abs(k.value - value) < epsValue)
                return i;
        }
        return -1;
    }

    void MoveKey(int index, float time, float value)
    {
        if (index < 0 || index >= curve.length)
            return;

        Keyframe k = curve.keys[index];
        k.time = time;
        k.value = value;
        curve.MoveKey(index, k);

        SortKeysAndSyncModes();
        for (int i = 0; i < curve.length; i++)
            RecomputeKeyTangents(i);
    }

    void AdjustTangent(Rect rect, Vector2 mousePos, int keyIndex, bool outTangent)
    {
        if (keyIndex < 0 || keyIndex >= curve.length)
            return;

        Keyframe k = curve.keys[keyIndex];
        TangentMode mode = keyModes[keyIndex];

        Vector2 keyPos = new Vector2(
            Mathf.InverseLerp(minTime, maxTime, k.time - pan.x) * rect.width + rect.x,
            Mathf.InverseLerp(maxValue, minValue, k.value - pan.y) * rect.height + rect.y
        );

        Vector2 dragVec = mousePos - keyPos;
        if (dragVec.sqrMagnitude < 1e-6f)
            return;

        float t1 = ScreenToTime(rect, keyPos);
        float v1 = ScreenToValue(rect, keyPos);

        float t2 = ScreenToTime(rect, mousePos);
        float v2 = ScreenToValue(rect, mousePos);

        float dt = t2 - t1;
        float dv = v2 - v1;

        if (Mathf.Abs(dt) < 1e-5f)
            return;

        float slope = dv / dt;
        if (float.IsNaN(slope) || float.IsInfinity(slope))
            return;

        switch (mode)
        {
            case TangentMode.Auto:
                keyModes[keyIndex] = TangentMode.Broken;
                if (outTangent)
                    k.outTangent = slope;
                else
                    k.inTangent = slope;
                break;

            case TangentMode.FreeSmooth:
            {
                Vector2 dir = dragVec.normalized;
                Vector2 outVec = dir * TANGENT_HANDLE_LENGTH;
                Vector2 inVec = -outVec;

                float outSlope = VectorToSlope(outVec, rect);
                float inSlope = VectorToSlope(inVec, rect);

                k.outTangent = outSlope;
                k.inTangent = inSlope;
                break;
            }

            case TangentMode.Broken:
                if (outTangent)
                    k.outTangent = slope;
                else
                    k.inTangent = slope;
                break;

            case TangentMode.Linear:
                RecomputeKeyTangents(keyIndex);
                break;

            case TangentMode.Constant:
                k.inTangent = float.PositiveInfinity;
                k.outTangent = float.PositiveInfinity;
                break;
        }

        curve.MoveKey(keyIndex, k);
    }

    float VectorToSlope(Vector2 vec, Rect rect)
    {
        float dtPixels = vec.x;
        if (Mathf.Abs(dtPixels) < 1e-5f)
            return 0f;

        float dt = (dtPixels / rect.width) * (maxTime - minTime);
        float dv = (vec.y / rect.height) * (minValue - maxValue);

        if (Mathf.Abs(dt) < 1e-5f)
            return 0f;

        return dv / dt;
    }

    void ApplyFreeSmoothMirroring(int keyIndex, Rect rect, Vector2 mousePos)
    {
        Keyframe k = curve.keys[keyIndex];

        Vector2 keyPos = new Vector2(
            Mathf.InverseLerp(minTime, maxTime, k.time - pan.x) * rect.width + rect.x,
            Mathf.InverseLerp(maxValue, minValue, k.value - pan.y) * rect.height + rect.y
        );

        Vector2 dragVec = mousePos - keyPos;
        if (dragVec.sqrMagnitude < 1e-6f)
            return;

        Vector2 dir = dragVec.normalized;
        Vector2 outVec = dir * TANGENT_HANDLE_LENGTH;
        Vector2 inVec = -outVec;

        float outSlope = VectorToSlope(outVec, rect);
        float inSlope = VectorToSlope(inVec, rect);

        k.outTangent = outSlope;
        k.inTangent = inSlope;

        curve.MoveKey(keyIndex, k);
    }

    void AdjustTangentWithMirroring(Rect rect, Vector2 mousePos, int keyIndex, bool outTangent)
    {
        if (keyModes[keyIndex] == TangentMode.FreeSmooth)
        {
            ApplyFreeSmoothMirroring(keyIndex, rect, mousePos);
            return;
        }

        AdjustTangent(rect, mousePos, keyIndex, outTangent);
    }

    void HandleDrag(Rect rect, Vector2 mousePos)
    {
        if (isDraggingKey && selectedKeyIndex >= 0 && selectedKeyIndex < curve.length)
        {
            float t = ScreenToTime(rect, mousePos);
            float v = ScreenToValue(rect, mousePos);
            MoveKey(selectedKeyIndex, t, v);
            GUI.changed = true;
            return;
        }

        if (isDraggingInTangent && selectedKeyIndex >= 0 && selectedKeyIndex < curve.length)
        {
            AdjustTangentWithMirroring(rect, mousePos, selectedKeyIndex, false);
            GUI.changed = true;
            return;
        }

        if (isDraggingOutTangent && selectedKeyIndex >= 0 && selectedKeyIndex < curve.length)
        {
            AdjustTangentWithMirroring(rect, mousePos, selectedKeyIndex, true);
            GUI.changed = true;
            return;
        }
    }

    void ProcessDragging(Rect rect, Event e)
    {
        if (e.type == EventType.MouseDrag && e.button == 0 && !isPanning)
        {
            HandleDrag(rect, e.mousePosition);
            e.Use();
        }
    }

    void HandleInput(Rect rect)
    {
        Event e = Event.current;

        if (rect.Contains(e.mousePosition) && e.type == EventType.ScrollWheel)
        {
            float zoomDelta = -e.delta.y * 0.1f;

            zoomX = Mathf.Clamp(zoomX + zoomDelta, 0.1f, 20f);
            zoomY = Mathf.Clamp(zoomY + zoomDelta, 0.1f, 20f);

            float mouseT = ScreenToTime(rect, e.mousePosition);
            float mouseV = ScreenToValue(rect, e.mousePosition);

            float newMouseT = ScreenToTime(rect, e.mousePosition);
            float newMouseV = ScreenToValue(rect, e.mousePosition);

            pan.x += (mouseT - newMouseT);
            pan.y += (mouseV - newMouseV);

            e.Use();
        }

        if (rect.Contains(e.mousePosition))
        {
            if ((e.type == EventType.MouseDown && e.button == 2) ||
                (e.type == EventType.MouseDown && e.button == 0 && e.alt))
            {
                isPanning = true;
                lastMouse = e.mousePosition;
                e.Use();
            }
        }

        if (e.type == EventType.MouseUp)
        {
            isPanning = false;
            isDraggingKey = false;
            isDraggingInTangent = false;
            isDraggingOutTangent = false;
        }

        if (isPanning && e.type == EventType.MouseDrag)
        {
            Vector2 delta = e.mousePosition - lastMouse;
            lastMouse = e.mousePosition;

            float dx = delta.x / 200f * (maxTime - minTime) / zoomX;
            float dy = -delta.y / 200f * (maxValue - minValue) / zoomY;

            pan += new Vector2(dx, dy);

            e.Use();
        }

        if (rect.Contains(e.mousePosition))
        {
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                if (e.shift)
                {
                    float t = ScreenToTime(rect, e.mousePosition);
                    float v = curve.Evaluate(t);
                    int idx = AddKeyWithMode(t, v, TangentMode.Auto);
                    selectedKeyIndex = idx;
                    GUI.changed = true;
                    e.Use();
                    return;
                }

                if (TryBeginDragKeyOrTangent(rect, e.mousePosition))
                {
                    e.Use();
                    return;
                }

                selectedKeyIndex = -1;
                GUI.changed = true;
                e.Use();
                return;
            }

            if (e.type == EventType.MouseDown && e.button == 1)
            {
                int idx = FindKeyAtPosition(rect, e.mousePosition, 6f);

                if (idx >= 0)
                {
                    ShowKeyContextMenu(idx);
                    e.Use();
                    return;
                }
                else
                {
                    ShowAddKeyContextMenu(rect, e.mousePosition);
                    e.Use();
                    return;
                }
            }
        }

        ProcessDragging(rect, e);
    }

    void AutoFit()
    {
        if (curve == null || curve.length == 0)
            return;

        float minT = curve.keys[0].time;
        float maxT = curve.keys[curve.length - 1].time;
        float minV = curve.keys[0].value;
        float maxV = curve.keys[0].value;

        foreach (Keyframe k in curve.keys)
        {
            if (k.time < minT) minT = k.time;
            if (k.time > maxT) maxT = k.time;
            if (k.value < minV) minV = k.value;
            if (k.value > maxV) maxV = k.value;
        }

        if (Mathf.Approximately(minT, maxT)) { minT -= 0.1f; maxT += 0.1f; }
        if (Mathf.Approximately(minV, maxV)) { minV -= 0.01f; maxV += 0.01f; }

        minTime = minT;
        maxTime = maxT;
        minValue = minV;
        maxValue = maxV;

        zoomX = zoomY = 1f;
        pan = Vector2.zero;
    }

    float ScreenToTime(Rect rect, Vector2 pos)
    {
        float tNorm = Mathf.InverseLerp(rect.x, rect.xMax, pos.x);
        float t = Mathf.Lerp(minTime, maxTime, tNorm);
        t += pan.x;
        return t;
    }

    float ScreenToValue(Rect rect, Vector2 pos)
    {
        float vNorm = Mathf.InverseLerp(rect.yMax, rect.y, pos.y);
        float v = Mathf.Lerp(minValue, maxValue, vNorm);
        v += pan.y;
        return v;
    }

    void ApplyPreset_Straight()
    {
        curve = AnimationCurve.Linear(0f, 0.03f, 1f, 0.03f);
        SyncKeyModesFromCurve();
        GUI.changed = true;
    }

    void ApplyPreset_Taper()
    {
        curve = AnimationCurve.Linear(0f, 0.04f, 1f, 0.01f);
        SyncKeyModesFromCurve();
        GUI.changed = true;
    }

    void ApplyPreset_Bulb()
    {
        curve = new AnimationCurve(
            new Keyframe(0f, 0.02f),
            new Keyframe(0.5f, 0.05f),
            new Keyframe(1f, 0.01f)
        );
        SyncKeyModesFromCurve();
        GUI.changed = true;
    }

    void ApplyPreset_Pinch()
    {
        curve = new AnimationCurve(
            new Keyframe(0f, 0.04f),
            new Keyframe(0.5f, 0.01f),
            new Keyframe(1f, 0.04f)
        );
        SyncKeyModesFromCurve();
        GUI.changed = true;
    }

    void ApplyPreset_ThinStart()
    {
        curve = new AnimationCurve(
            new Keyframe(0f, 0.01f),
            new Keyframe(1f, 0.04f)
        );
        SyncKeyModesFromCurve();
        GUI.changed = true;
    }

    void ApplyPreset_ThinEnd()
    {
        curve = new AnimationCurve(
            new Keyframe(0f, 0.04f),
            new Keyframe(1f, 0.01f)
        );
        SyncKeyModesFromCurve();
        GUI.changed = true;
    }

    void ShowAddKeyContextMenu(Rect rect, Vector2 mousePos)
    {
        GenericMenu menu = new GenericMenu();

        menu.AddItem(new GUIContent("add key here"), false, () =>
        {
            float t = ScreenToTime(rect, mousePos);
            float v = curve.Evaluate(t);
            int idx = AddKeyWithMode(t, v, TangentMode.Auto);
            selectedKeyIndex = idx;
            GUI.changed = true;
        });

        menu.AddSeparator("");
        menu.AddItem(new GUIContent("cancel"), false, () => { });

        menu.ShowAsContext();
    }

    void ShowKeyContextMenu(int keyIndex)
    {
        GenericMenu menu = new GenericMenu();

        menu.AddItem(new GUIContent("auto"), keyModes[keyIndex] == TangentMode.Auto, () =>
        {
            SetKeyMode(keyIndex, TangentMode.Auto);
            GUI.changed = true;
        });

        menu.AddItem(new GUIContent("free smooth"), keyModes[keyIndex] == TangentMode.FreeSmooth, () =>
        {
            SetKeyMode(keyIndex, TangentMode.FreeSmooth);
            GUI.changed = true;
        });

        menu.AddItem(new GUIContent("broken"), keyModes[keyIndex] == TangentMode.Broken, () =>
        {
            SetKeyMode(keyIndex, TangentMode.Broken);
            GUI.changed = true;
        });

        menu.AddItem(new GUIContent("linear"), keyModes[keyIndex] == TangentMode.Linear, () =>
        {
            SetKeyMode(keyIndex, TangentMode.Linear);
            GUI.changed = true;
        });

        menu.AddItem(new GUIContent("constant"), keyModes[keyIndex] == TangentMode.Constant, () =>
        {
            SetKeyMode(keyIndex, TangentMode.Constant);
            GUI.changed = true;
        });

        menu.AddSeparator("");

        menu.AddItem(new GUIContent("delete key"), false, () =>
        {
            RemoveKeyAt(keyIndex);
            if (selectedKeyIndex == keyIndex) selectedKeyIndex = -1;
            GUI.changed = true;
        });

        menu.ShowAsContext();
    }

    void SetKeyMode(int index, TangentMode mode)
    {
        if (index < 0 || index >= curve.length)
            return;

        keyModes[index] = mode;
        RecomputeKeyTangents(index);
    }

    void RecomputeKeyTangents(int index)
    {
        if (index < 0 || index >= curve.length)
            return;

        Keyframe k = curve.keys[index];
        TangentMode mode = keyModes[index];

        switch (mode)
        {
            case TangentMode.Auto:
            {
                float m = ComputeAutoSlope(index);
                k.inTangent = m;
                k.outTangent = m;
                break;
            }

            case TangentMode.FreeSmooth:
            {
                float m = ComputeAutoSlope(index);
                k.inTangent = m;
                k.outTangent = m;
                break;
            }

            case TangentMode.Broken:
            {
                break;
            }

            case TangentMode.Linear:
            {
                float inM = ComputeLinearInSlope(index);
                float outM = ComputeLinearOutSlope(index);
                k.inTangent = inM;
                k.outTangent = outM;
                break;
            }

            case TangentMode.Constant:
            {
                k.inTangent = float.PositiveInfinity;
                k.outTangent = float.PositiveInfinity;
                break;
            }
        }

        curve.MoveKey(index, k);
    }

    float ComputeAutoSlope(int index)
    {
        if (curve.length <= 1)
            return 0f;

        if (index == 0)
            return ComputeLinearOutSlope(index);

        if (index == curve.length - 1)
            return ComputeLinearInSlope(index);

        Keyframe prev = curve.keys[index - 1];
        Keyframe next = curve.keys[index + 1];

        float dt = next.time - prev.time;
        if (Mathf.Abs(dt) < 1e-5f)
            return 0f;

        return (next.value - prev.value) / dt;
    }

    float ComputeLinearInSlope(int index)
    {
        if (index == 0)
            return 0f;

        Keyframe k = curve.keys[index];
        Keyframe prev = curve.keys[index - 1];

        float dt = k.time - prev.time;
        if (Mathf.Abs(dt) < 1e-5f)
            return 0f;

        return (k.value - prev.value) / dt;
    }

    float ComputeLinearOutSlope(int index)
    {
        if (index == curve.length - 1)
            return 0f;

        Keyframe k = curve.keys[index];
        Keyframe next = curve.keys[index + 1];

        float dt = next.time - k.time;
        if (Mathf.Abs(dt) < 1e-5f)
            return 0f;

        return (next.value - k.value) / dt;
    }
}
