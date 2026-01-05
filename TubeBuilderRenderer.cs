using UnityEngine;
using System.Collections.Generic;
using System;

[ExecuteInEditMode]
public class TubeBuilderRenderer : MonoBehaviour
{
    public enum TubeCapType
    {
        None,
        Flat,
        Point,
        Rounded,
        FullSphere
    }

    [System.Serializable]
    public struct TubeSegment
    {
        public string name;
        public bool enabled;

        public Vector3 p0;
        public Vector3 p1;
        public Vector3 p2;

        public Color startColor;
        public Color endColor;

        public int segments;
        public int radialSegments;

        public AnimationCurve radiusProfile;

        public TubeCapType capStart;
        public TubeCapType capEnd;

        public float bulgePower;

        public float sphereRadius;
        public int roundedCapSegments;
        public float twist;

        public bool generateTube;
        public float colorLerpOffset;
        public float colorLerpScale;

        public int colorCutoffSegment; 

        public float capScale; 

        public bool useBones;
        public int bonesPerSegment;
        public bool useNestedChain;
        public float blendOffset;
        public float blendScaler;
        
        [HideInInspector] 
        public List<Transform> boneInstances; 
    }

    public TubeSegment[] segments;
    public Color gizmoCurveColor = Color.white;
    public bool showWeightsDebug;

    private Mesh mesh;

    private Vector3[] verts = new Vector3[0];
    private Vector2[] uvs = new Vector2[0];
    private Color[] colors = new Color[0];
    private BoneWeight[] weights = new BoneWeight[0];
    private int[] tris = new int[0];

    private Vector3[] curvePos = new Vector3[0];
    private Vector3[] curveTan = new Vector3[0];

    private int[] ringStarts = new int[0];
    private bool wasSkinningActive = false;

    static Color LerpColor(Color a, Color b, float t)
    {
        return new Color(
            a.r + (b.r - a.r) * t,
            a.g + (b.g - a.g) * t,
            a.b + (b.b - a.b) * t,
            a.a + (b.a - a.a) * t
        );
    }

    void EnsureCurveBuffers(int seg)
    {
        if (curvePos.Length != seg)
        {
            curvePos = new Vector3[seg];
            curveTan = new Vector3[seg];
        }
    }

    void EnsureRingStarts(int count)
    {
        if (ringStarts.Length < count)
            ringStarts = new int[count];
    }

    int LatSeg(int ring)
    {
        return Mathf.Clamp(ring / 2, 2, 8);
    }

    void Awake()
    {
        EnsureMesh();
    }

    void OnEnable()
    {
        Rebuild();
    }

    void Update()
    {
        Rebuild();
    }

    public void Rebuild()
    {
        if (segments == null || segments.Length == 0)
        {
            if (mesh != null) mesh.Clear();
            return;
        }

        // Pass 0: Check for any active bones (No Linq)
        bool anyBones = false;
        for (int i = 0; i < segments.Length; i++)
        {
            if (segments[i].enabled && segments[i].useBones)
            {
                anyBones = true;
                break;
            }
        }

        SyncRenderer(anyBones);
        EnsureMesh();

        int totalV = 0;
        int totalT = 0;

        // Pass 1: count vertices and triangles
        for (int i = 0; i < segments.Length; i++)
        {
            TubeSegment s = segments[i];
            if (!s.enabled) continue;

            int seg = Mathf.Max(2, s.segments);
            int ring = Mathf.Max(3, s.radialSegments);
            int lat = s.roundedCapSegments > 0 ? Mathf.Max(2, s.roundedCapSegments) : LatSeg(ring);

            bool fullSphere = (s.capStart == TubeCapType.FullSphere && s.capEnd == TubeCapType.FullSphere && s.segments <= 1);

            if (fullSphere)
            {
                int g = ring + 1;
                totalV += g * g;
                totalT += ring * ring * 6;
                continue;
            }

            if (s.generateTube)
            {
                bool hardCut = (s.colorCutoffSegment > 0 && s.colorCutoffSegment < seg);
                int ringCount = seg + (hardCut ? 1 : 0);
                totalV += ringCount * ring;
                totalT += (ringCount - 1) * ring * 6;
            }

            // Start cap
            if (s.capStart == TubeCapType.Flat || s.capStart == TubeCapType.Point) { totalV += (ring + 1); totalT += ring * 3; }
            else if (s.capStart == TubeCapType.Rounded) { totalV += (lat * ring + 1 + ring); totalT += lat * ring * 6 + ring * 3; }
            else if (s.capStart == TubeCapType.FullSphere) { int g = ring + 1; totalV += g * g; totalT += ring * ring * 6; }

            // End cap
            if (s.capEnd == TubeCapType.Flat || s.capEnd == TubeCapType.Point) { totalV += (ring + 1); totalT += ring * 3; }
            else if (s.capEnd == TubeCapType.Rounded) { totalV += (lat * ring + 1 + ring); totalT += lat * ring * 6 + ring * 3; }
            else if (s.capEnd == TubeCapType.FullSphere) { int g = ring + 1; totalV += g * g; totalT += ring * ring * 6; }
        }

        if (verts.Length < totalV)
        {
            verts = new Vector3[totalV];
            uvs = new Vector2[totalV];
            colors = new Color[totalV];
            weights = new BoneWeight[totalV];
        }
        if (tris.Length < totalT) tris = new int[totalT];

        int v = 0;
        int t = 0;

        List<Transform> allBones = new List<Transform>();
        List<Matrix4x4> bindPoses = new List<Matrix4x4>();
        
        // Bone 0: Root Static
        allBones.Add(this.transform);
        bindPoses.Add(this.transform.worldToLocalMatrix * transform.localToWorldMatrix);

        Transform globalLastBone = null;

        // Pass 2: build geometry
        for (int si = 0; si < segments.Length; si++)
        {
            TubeSegment s = segments[si];
            if (!s.enabled) continue;

            int vStart = v; 
            int boneStartGlobalIdx = 0;

            if (s.useBones)
            {
                boneStartGlobalIdx = allBones.Count;
                globalLastBone = SetupSegmentBones(si, ref allBones, ref bindPoses, globalLastBone);
            }

            int seg = Mathf.Max(2, s.segments);
            int ring = Mathf.Max(3, s.radialSegments);
            int latSeg = s.roundedCapSegments > 0 ? Mathf.Max(2, s.roundedCapSegments) : LatSeg(ring);

            Color startColor = s.startColor;
            Color endColor = s.endColor;

            bool fullSphere = (s.capStart == TubeCapType.FullSphere && s.capEnd == TubeCapType.FullSphere && s.segments <= 1);

            if (fullSphere)
            {
                float rSphere = s.sphereRadius > 0f ? s.sphereRadius : (s.radiusProfile != null ? s.radiusProfile.Evaluate(0f) : 0.03f);
                Vector3 P0_fs = s.p0; Vector3 P1_fs = s.p1; Vector3 P2_fs = s.p2;
                Vector3 d_fs = P0_fs * (-2f) + P1_fs * (2f);
                if (d_fs.sqrMagnitude < 1e-6f) d_fs = Vector3.forward;
                Vector3 T_fs = d_fs.normalized;
                Vector3 refDir_fs = P1_fs - P0_fs;
                refDir_fs -= T_fs * Vector3.Dot(T_fs, refDir_fs);
                if (refDir_fs.sqrMagnitude < 1e-6f) refDir_fs = Vector3.Cross(T_fs, Vector3.up);
                refDir_fs.Normalize();
                Vector3 N_fs = refDir_fs; Vector3 B_fs = Vector3.Cross(T_fs, N_fs);
                BuildSphere(s.p0, rSphere, ring, startColor, T_fs, N_fs, B_fs, 0f, ref v, ref t);
                ApplyBoneWeights(vStart, v, s, boneStartGlobalIdx);
                continue;
            }

            EnsureCurveBuffers(seg);
            Vector3 P0 = s.p0; Vector3 P1 = s.p1; Vector3 P2 = s.p2;
            float inv = 1f / (seg - 1);
            for (int i = 0; i < seg; i++)
            {
                float tt = i * inv; float omt = 1f - tt;
                curvePos[i] = P0 * (omt * omt) + P1 * (2f * omt * tt) + P2 * (tt * tt);
                Vector3 d = P0 * (-2f * omt) + P1 * (2f - 4f * tt) + P2 * (2f * tt);
                if (d.sqrMagnitude < 1e-6f) d = Vector3.forward;
                curveTan[i] = d.normalized;
            }

            Vector3 T0 = curveTan[0]; Vector3 refDir = P1 - P0;
            refDir -= T0 * Vector3.Dot(T0, refDir);
            if (refDir.sqrMagnitude < 1e-6f) refDir = Vector3.Cross(T0, Vector3.up);
            refDir.Normalize();
            Vector3 Nprev = refDir; Vector3 Bprev = Vector3.Cross(T0, Nprev);
            Vector3 startN = Nprev; Vector3 startB = Bprev;

            int cutIndex = s.colorCutoffSegment;
            bool hardCut = (cutIndex > 0 && cutIndex < seg);
            bool hardCutAllEnd = (cutIndex == 0);
            int firstRing = -1; int lastRing = -1;

            if (s.generateTube)
            {
                int tubeBase = v;
                if (hardCutAllEnd || !hardCut)
                {
                    for (int i = 0; i < seg; i++)
                    {
                        Vector3 Tcur = curveTan[i];
                        Vector3 N, B;
                        if (i == 0) { N = Nprev; B = Bprev; }
                        else {
                            Vector3 Np = Nprev - Tcur * Vector3.Dot(Tcur, Nprev);
                            if (Np.sqrMagnitude < 1e-6f) Np = Vector3.Cross(Tcur, Vector3.up);
                            N = Np.normalized; B = Vector3.Cross(Tcur, N);
                            Nprev = N; Bprev = B;
                        }
                        float tc = seg > 1 ? (float)i / (seg - 1) : 0f;
                        if (Mathf.Abs(s.twist) > 0.0001f) {
                            Quaternion twistQ = Quaternion.AngleAxis(s.twist * tc, Tcur);
                            N = twistQ * N; B = twistQ * B;
                        }
                        float rad = s.radiusProfile != null ? s.radiusProfile.Evaluate(tc) : 0.03f;
                        Color cHere = hardCutAllEnd ? endColor : LerpColor(startColor, endColor, Mathf.Clamp01(tc * s.colorLerpScale + s.colorLerpOffset));
                        int ringStart = v;
                        for (int j = 0; j < ring; j++) {
                            float ang = (j / (float)ring) * Mathf.PI * 2f;
                            verts[v] = curvePos[i] + (N * Mathf.Cos(ang) + B * Mathf.Sin(ang)) * rad;
                            uvs[v] = new Vector2(j / (float)ring, tc);
                            colors[v] = cHere; v++;
                        }
                        if (i == 0) firstRing = ringStart;
                        if (i == seg - 1) lastRing = ringStart;
                    }
                    for (int i = 0; i < seg - 1; i++) {
                        int rA = tubeBase + i * ring; int rB = tubeBase + (i + 1) * ring;
                        for (int j = 0; j < ring; j++) {
                            int j1 = (j + 1) % ring;
                            tris[t++] = rA + j; tris[t++] = rA + j1; tris[t++] = rB + j;
                            tris[t++] = rA + j1; tris[t++] = rB + j1; tris[t++] = rB + j;
                        }
                    }
                }
                else // Hard Cut
                {
                    int ringCount = 0; int seamRingIndex = -1; EnsureRingStarts(seg + 1);
                    for (int i = 0; i < seg; i++) {
                        Vector3 Tcur = curveTan[i]; Vector3 N, B;
                        if (i == 0) { N = Nprev; B = Bprev; }
                        else {
                            Vector3 Np = Nprev - Tcur * Vector3.Dot(Tcur, Nprev);
                            if (Np.sqrMagnitude < 1e-6f) Np = Vector3.Cross(Tcur, Vector3.up);
                            N = Np.normalized; B = Vector3.Cross(Tcur, N);
                            Nprev = N; Bprev = B;
                        }
                        float tc = seg > 1 ? (float)i / (seg - 1) : 0f;
                        if (Mathf.Abs(s.twist) > 0.0001f) {
                            Quaternion twistQ = Quaternion.AngleAxis(s.twist * tc, Tcur);
                            N = twistQ * N; B = twistQ * B;
                        }
                        float rad = s.radiusProfile != null ? s.radiusProfile.Evaluate(tc) : 0.03f;
                        if (i < cutIndex) {
                            ringStarts[ringCount++] = v;
                            for (int j = 0; j < ring; j++) {
                                float ang = (j / (float)ring) * Mathf.PI * 2f;
                                verts[v] = curvePos[i] + (N * Mathf.Cos(ang) + B * Mathf.Sin(ang)) * rad;
                                uvs[v] = new Vector2(j / (float)ring, tc); colors[v] = startColor; v++;
                            }
                        } else if (i == cutIndex) {
                            ringStarts[ringCount++] = v;
                            for (int j = 0; j < ring; j++) {
                                float ang = (j / (float)ring) * Mathf.PI * 2f;
                                verts[v] = curvePos[i] + (N * Mathf.Cos(ang) + B * Mathf.Sin(ang)) * rad;
                                uvs[v] = new Vector2(j / (float)ring, tc); colors[v] = startColor; v++;
                            }
                            seamRingIndex = ringCount - 1;
                            ringStarts[ringCount++] = v;
                            for (int j = 0; j < ring; j++) {
                                float ang = (j / (float)ring) * Mathf.PI * 2f;
                                verts[v] = curvePos[i] + (N * Mathf.Cos(ang) + B * Mathf.Sin(ang)) * rad;
                                uvs[v] = new Vector2(j / (float)ring, tc); colors[v] = endColor; v++;
                            }
                        } else {
                            ringStarts[ringCount++] = v;
                            for (int j = 0; j < ring; j++) {
                                float ang = (j / (float)ring) * Mathf.PI * 2f;
                                verts[v] = curvePos[i] + (N * Mathf.Cos(ang) + B * Mathf.Sin(ang)) * rad;
                                uvs[v] = new Vector2(j / (float)ring, tc); colors[v] = endColor; v++;
                            }
                        }
                    }
                    if (ringCount > 0) { firstRing = ringStarts[0]; lastRing = ringStarts[ringCount - 1]; }
                    for (int k = 0; k < ringCount - 1; k++) {
                        if (k == seamRingIndex) continue;
                        int rA = ringStarts[k]; int rB = ringStarts[k + 1];
                        for (int j = 0; j < ring; j++) {
                            int j1 = (j + 1) % ring;
                            tris[t++] = rA + j; tris[t++] = rA + j1; tris[t++] = rB + j;
                            tris[t++] = rA + j1; tris[t++] = rB + j1; tris[t++] = rB + j;
                        }
                    }
                }
            }

            Vector3 curT0 = curveTan[0]; Vector3 curT1 = curveTan[seg - 1];
            if (s.capStart == TubeCapType.Flat) {
                int ci; BuildFlat(curvePos[0], startColor, ref v, out ci);
                if (firstRing >= 0) BuildFanRev(ci, firstRing, ring, ref t);
            } else if (s.capStart == TubeCapType.Point) {
                int ci; BuildPoint(curvePos[0], -curT0, s, 0f, startColor, ref v, out ci);
                if (firstRing >= 0) BuildFanRev(ci, firstRing, ring, ref t);
            } else if (s.capStart == TubeCapType.Rounded) {
                if (firstRing >= 0) BuildRoundedCapStart_UsingTubeRing(verts, firstRing, curvePos[0], s.radiusProfile.Evaluate(0f), ring, latSeg, -curT0, startN, startB, startColor, s.capScale, s.bulgePower, ref v, ref t);
            } else if (s.capStart == TubeCapType.FullSphere) {
                BuildSphere(curvePos[0], s.sphereRadius, ring, startColor, curT0, startN, startB, 0f, ref v, ref t);
            }

            if (s.capEnd == TubeCapType.Flat) {
                int ci; BuildFlat(curvePos[seg - 1], endColor, ref v, out ci);
                if (lastRing >= 0) BuildFan(ci, lastRing, ring, ref t);
            } else if (s.capEnd == TubeCapType.Point) {
                int ci; BuildPoint(curvePos[seg - 1], curT1, s, 1f, endColor, ref v, out ci);
                if (lastRing >= 0) BuildFan(ci, lastRing, ring, ref t);
            } else if (s.capEnd == TubeCapType.Rounded) {
                if (lastRing >= 0) BuildRoundedCapEnd_UsingTubeRing(verts, lastRing, curvePos[seg - 1], s.radiusProfile.Evaluate(1f), ring, latSeg, curT1, Nprev, Bprev, s.twist, endColor, s.capScale, s.bulgePower, ref v, ref t);
            } else if (s.capEnd == TubeCapType.FullSphere) {
                BuildSphere(curvePos[seg - 1], s.sphereRadius, ring, endColor, curT1, Nprev, Bprev, s.twist, ref v, ref t);
            }

            ApplyBoneWeights(vStart, v, s, boneStartGlobalIdx);
        }

        // Finalize Mesh Data (Manual Array Copy for No-LINQ / C# 4.0)
        Vector3[] fV = new Vector3[v]; Vector2[] fU = new Vector2[v]; Color[] fC = new Color[v]; int[] fT = new int[t];
        Array.Copy(verts, fV, v); Array.Copy(uvs, fU, v); Array.Copy(colors, fC, v); Array.Copy(tris, fT, t);

        // Debug Weights Over Vertex Colors
        if (showWeightsDebug && anyBones) {
            for (int i = 0; i < v; i++) {
                float w = weights[i].weight0;
                fC[i] = new Color(w, 0, 1f - w, 1f); // Heatmap: Red = Max, Blue = Min
            }
        }

        mesh.Clear(); mesh.vertices = fV; mesh.uv = fU; mesh.colors = fC; mesh.triangles = fT;

        if (anyBones) {
            BoneWeight[] fW = new BoneWeight[v]; Array.Copy(weights, fW, v);
            mesh.boneWeights = fW; mesh.bindposes = bindPoses.ToArray();
            GetComponent<SkinnedMeshRenderer>().bones = allBones.ToArray();
        }

        mesh.RecalculateNormals(); mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 9999f);
    }

    void ApplyBoneWeights(int start, int end, TubeSegment s, int boneIdx)
    {
        if (!s.useBones) {
            for (int i = start; i < end; i++) { weights[i].boneIndex0 = 0; weights[i].weight0 = 1f; }
            return;
        }
        int bCount = s.bonesPerSegment;
        for (int i = start; i < end; i++) {
            float progress = uvs[i].y;
            float t = Mathf.Clamp01((progress + s.blendOffset) * s.blendScaler);
            float bT = t * (float)(bCount - 1);
            int bA = Mathf.FloorToInt(bT); int bB = Mathf.Clamp(bA + 1, 0, bCount - 1);
            float wB = bT - (float)bA;
            weights[i].boneIndex0 = boneIdx + bA; weights[i].weight0 = 1f - wB;
            weights[i].boneIndex1 = boneIdx + bB; weights[i].weight1 = wB;
        }
    }

    Transform SetupSegmentBones(int si, ref List<Transform> allBones, ref List<Matrix4x4> bindPoses, Transform lastBone)
    {
        int count = Mathf.Max(1, segments[si].bonesPerSegment);
        if (segments[si].boneInstances == null) segments[si].boneInstances = new List<Transform>();
        while (segments[si].boneInstances.Count > count) { if (segments[si].boneInstances[0]) DestroyImmediate(segments[si].boneInstances[0].gameObject); segments[si].boneInstances.RemoveAt(0); }
        while (segments[si].boneInstances.Count < count) { GameObject go = new GameObject("Bone"); go.transform.parent = transform; segments[si].boneInstances.Add(go.transform); }

        for (int b = 0; b < count; b++) {
            float t = (float)b / (count > 1 ? (float)(count - 1) : 1.0f);
            Transform bone = segments[si].boneInstances[b];
            bone.localPosition = GetBezierPoint(segments[si], t);
            bone.localRotation = Quaternion.LookRotation(GetBezierTangent(segments[si], t));
            bone.name = segments[si].name + "_Bone_" + b;
            if (segments[si].useNestedChain) { bone.SetParent(lastBone == null ? transform : lastBone); lastBone = bone; }
            else { bone.SetParent(transform); }
            allBones.Add(bone); bindPoses.Add(bone.worldToLocalMatrix * transform.localToWorldMatrix);
        }
        return lastBone;
    }

    Vector3 GetBezierPoint(TubeSegment s, float t) { Vector3 m0 = Vector3.Lerp(s.p0, s.p1, t); Vector3 m1 = Vector3.Lerp(s.p1, s.p2, t); return Vector3.Lerp(m0, m1, t); }
    Vector3 GetBezierTangent(TubeSegment s, float t) { return (2f * (1f - t) * (s.p1 - s.p0) + 2f * t * (s.p2 - s.p1)).normalized; }

    void SyncRenderer(bool skin) {
        if (wasSkinningActive == skin) return;
        if (skin) { if (GetComponent<MeshRenderer>()) DestroyImmediate(GetComponent<MeshRenderer>()); if (GetComponent<MeshFilter>()) DestroyImmediate(GetComponent<MeshFilter>()); if (!GetComponent<SkinnedMeshRenderer>()) gameObject.AddComponent<SkinnedMeshRenderer>(); }
        else { if (GetComponent<SkinnedMeshRenderer>()) DestroyImmediate(GetComponent<SkinnedMeshRenderer>()); if (!GetComponent<MeshFilter>()) gameObject.AddComponent<MeshFilter>(); if (!GetComponent<MeshRenderer>()) gameObject.AddComponent<MeshRenderer>(); }
        wasSkinningActive = skin;
    }

    void EnsureMesh() {
        if (mesh == null) { mesh = new Mesh(); mesh.name = "TubeBatch"; mesh.MarkDynamic(); }
        if (GetComponent<SkinnedMeshRenderer>()) GetComponent<SkinnedMeshRenderer>().sharedMesh = mesh;
        else if (GetComponent<MeshFilter>()) GetComponent<MeshFilter>().sharedMesh = mesh;
    }

    // Geometry Core Functions
    void BuildFlat(Vector3 c, Color col, ref int v, out int ci) { ci = v; verts[v] = c; uvs[v] = new Vector2(0.5f, 0.5f); colors[v] = col; v++; }
    void BuildPoint(Vector3 c, Vector3 dir, TubeSegment s, float tC, Color col, ref int v, out int ti) { float r = s.radiusProfile != null ? s.radiusProfile.Evaluate(tC) : 0.03f; ti = v; verts[v] = c + dir.normalized * (r * s.capScale); uvs[v] = new Vector2(0.5f, 1f); colors[v] = col; v++; }
    void BuildFan(int ci, int rS, int r, ref int t) { for (int j = 0; j < r; j++) { tris[t++] = rS + j; tris[t++] = rS + (j + 1) % r; tris[t++] = ci; } }
    void BuildFanRev(int ci, int rS, int r, ref int t) { for (int j = 0; j < r; j++) { tris[t++] = rS + j; tris[t++] = ci; tris[t++] = rS + (j + 1) % r; } }
    void BuildSphere(Vector3 c, float r, int ring, Color col, Vector3 axis, Vector3 fN, Vector3 fB, float twist, ref int v, ref int t) {
        int g = ring + 1; int baseV = v; Quaternion twQ = Quaternion.AngleAxis(twist, axis); Vector3 nW = twQ * fN; Vector3 bW = twQ * fB;
        for (int iy = 0; iy < g; iy++) { float ty = (float)iy / ring; float theta = (ty - 0.5f) * Mathf.PI; float cy = Mathf.Cos(theta); float sy = Mathf.Sin(theta);
            for (int ix = 0; ix < g; ix++) { float tx = (float)ix / ring; float phi = tx * Mathf.PI * 2f; Vector3 nL = new Vector3(Mathf.Cos(phi) * cy, sy, Mathf.Sin(phi) * cy);
                verts[v] = c + (nW * nL.x + axis * nL.y + bW * nL.z) * r; uvs[v] = new Vector2(tx, ty); colors[v] = col; v++; } }
        for (int iy = 0; iy < ring; iy++) { int rA = baseV + iy * g; int rB = baseV + (iy + 1) * g;
            for (int ix = 0; ix < ring; ix++) { tris[t++] = rA + ix; tris[t++] = rA + ix + 1; tris[t++] = rB + ix; tris[t++] = rA + ix + 1; tris[t++] = rB + ix + 1; tris[t++] = rB + ix; } }
    }
    void BuildRoundedCapStart_UsingTubeRing(Vector3[] vA, int rS, Vector3 c, float r, int ring, int lat, Vector3 ax, Vector3 fN, Vector3 fB, Color col, float sc, float bP, ref int v, ref int t) {
        int cB = v; for (int i = 1; i < lat; i++) { float tL = (float)i / lat; float th = tL * Mathf.PI * 0.5f; float rad = r * Mathf.Pow(Mathf.Cos(th), bP); float h = r * Mathf.Sin(th) * sc;
            for (int j = 0; j < ring; j++) { float phi = (j / (float)ring) * Mathf.PI * 2f; vA[v] = c + (fN * Mathf.Cos(phi) + fB * Mathf.Sin(phi)) * rad + ax * h; uvs[v] = new Vector2(j / (float)ring, tL); colors[v] = col; v++; } }
        for (int j = 0; j < ring; j++) { int j1 = (j + 1) % ring; tris[t++] = rS + j; tris[t++] = cB + j; tris[t++] = cB + j1; tris[t++] = rS + j; tris[t++] = cB + j1; tris[t++] = rS + j1; }
        for (int i = 0; i < lat - 2; i++) { int rA = cB + i * ring; int rB = rA + ring; for (int j = 0; j < ring; j++) { int j1 = (j + 1) % ring; tris[t++] = rA + j; tris[t++] = rB + j; tris[t++] = rB + j1; tris[t++] = rA + j; tris[t++] = rB + j1; tris[t++] = rA + j1; } }
        int pI = v; vA[v] = c + ax * (r * sc); uvs[v] = new Vector2(0.5f, 1f); colors[v] = col; v++; int lS = cB + (lat - 2) * ring; for (int j = 0; j < ring; j++) { tris[t++] = lS + j; tris[t++] = pI; tris[t++] = lS + (j + 1) % ring; }
    }
    void BuildRoundedCapEnd_UsingTubeRing(Vector3[] vA, int rS, Vector3 c, float r, int ring, int lat, Vector3 ax, Vector3 fN, Vector3 fB, float tw, Color col, float sc, float bP, ref int v, ref int t) {
        Quaternion q = Quaternion.AngleAxis(tw, ax); Vector3 U = q * fN; Vector3 V = q * fB; int cB = v;
        for (int i = 1; i < lat; i++) { float tL = (float)i / lat; float th = tL * Mathf.PI * 0.5f; float rad = r * Mathf.Pow(Mathf.Cos(th), bP); float h = r * Mathf.Sin(th) * sc;
            for (int j = 0; j < ring; j++) { float phi = (j / (float)ring) * Mathf.PI * 2f; vA[v] = c + (U * Mathf.Cos(phi) + V * Mathf.Sin(phi)) * rad + ax * h; uvs[v] = new Vector2(j / (float)ring, tL); colors[v] = col; v++; } }
        for (int j = 0; j < ring; j++) { int j1 = (j + 1) % ring; tris[t++] = rS + j; tris[t++] = cB + j1; tris[t++] = cB + j; tris[t++] = rS + j; tris[t++] = rS + j1; tris[t++] = cB + j1; }
        for (int i = 0; i < lat - 2; i++) { int rA = cB + i * ring; int rB = rA + ring; for (int j = 0; j < ring; j++) { int j1 = (j + 1) % ring; tris[t++] = rA + j; tris[t++] = rB + j1; tris[t++] = rB + j; tris[t++] = rA + j; tris[t++] = rA + j1; tris[t++] = rB + j1; } }
        int pI = v; vA[v] = c + ax * (r * sc); uvs[v] = new Vector2(0.5f, 1f); colors[v] = col; v++; int lS = cB + (lat - 2) * ring; for (int j = 0; j < ring; j++) { tris[t++] = lS + j; tris[t++] = lS + (j + 1) % ring; tris[t++] = pI; }
    }
}
