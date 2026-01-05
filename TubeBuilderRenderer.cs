using UnityEngine;
using System.Collections.Generic;
using System;

[ExecuteInEditMode]
public class TubeBuilderRenderer : MonoBehaviour
{
    public enum TubeCapType { None, Flat, Point, Rounded, FullSphere }
    public enum UVMappingType { Normalized, WorldSpace }

    [System.Serializable]
    public struct CapSettings
    {
        public TubeCapType type;
        public float scale;
        public float bulge;
        public int segments;
        public int sphereResolution;
        public float sphereRadius;
    }

    [System.Serializable]
    public struct TubeSegment
    {
        public string name;
        public bool enabled;

        [Header("Path")]
        public bool connectToPrevious; 
        public Vector3 p0;
        public Vector3 p1;
        public Vector3 p2;
        public int segments;
        public int radialSegments;
        public float twist;

        [Header("Caps")]
        public CapSettings startCap;
        public CapSettings endCap;

        [Header("Visuals")]
        public UVMappingType uvMapping;
        public AnimationCurve radiusProfile;
        public AnimationCurve radialShapeCurve;
        public bool useHardEdges;
        public Vector2 uvTiling;
        public Vector2 uvOffset;
        public Color startColor;
        public Color endColor;
        public bool generateTube;
        public float colorLerpOffset;
        public float colorLerpScale;
        public int colorCutoffSegment;

        [Header("Bones")]
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
    public bool showBonesGizmo = true;
    public bool autoRebuild = true;

    private Mesh mesh;
    private Vector3[] verts = new Vector3[0];
    private Vector2[] uvs = new Vector2[0];
    private Color[] colors = new Color[0];
    private Vector4[] tangents = new Vector4[0];
    private BoneWeight[] weights = new BoneWeight[0];
    private int[] tris = new int[0];
    private float[] vertNormalizedV = new float[0];

    private Vector3[] curvePos = new Vector3[0];
    private Vector3[] curveTan = new Vector3[0];
    
    private bool isDirty = true;
    private bool wasSkinningActive = false;

    private float[] radialSin;
    private float[] radialCos;
    private float[] sampledRadius;
    private float[] sampledShape;

    private Vector3 globalNprev;
    private Vector3 globalBprev;

    // ==========================================
    // ANIMATION API (C# 4.0 SAFE)
    // ==========================================

    /// <summary>Sets the local position of a control point (0=P0, 1=P1, 2=P2)</summary>
    public void SetPoint(int segmentIndex, int pointIndex, Vector3 localPosition)
    {
        if (segmentIndex < 0 || segmentIndex >= segments.Length) return;
        
        if (pointIndex == 0) segments[segmentIndex].p0 = localPosition;
        else if (pointIndex == 1) segments[segmentIndex].p1 = localPosition;
        else if (pointIndex == 2) segments[segmentIndex].p2 = localPosition;
        
        MarkDirty();
    }

    /// <summary>Offsets the local position of a control point additively</summary>
    public void OffsetPoint(int segmentIndex, int pointIndex, Vector3 offset)
    {
        if (segmentIndex < 0 || segmentIndex >= segments.Length) return;

        if (pointIndex == 0) segments[segmentIndex].p0 += offset;
        else if (pointIndex == 1) segments[segmentIndex].p1 += offset;
        else if (pointIndex == 2) segments[segmentIndex].p2 += offset;

        MarkDirty();
    }

    /// <summary>Moves all three points of a segment by an offset</summary>
    public void MoveSegment(int segmentIndex, Vector3 offset)
    {
        if (segmentIndex < 0 || segmentIndex >= segments.Length) return;
        segments[segmentIndex].p0 += offset;
        segments[segmentIndex].p1 += offset;
        segments[segmentIndex].p2 += offset;
        MarkDirty();
    }

    public Vector3 GetPoint(int segmentIndex, int pointIndex)
    {
        if (segmentIndex < 0 || segmentIndex >= segments.Length) return Vector3.zero;
        if (pointIndex == 0) return segments[segmentIndex].p0;
        if (pointIndex == 1) return segments[segmentIndex].p1;
        return segments[segmentIndex].p2;
    }

    // ==========================================
    // CORE ENGINE
    // ==========================================

    public void MarkDirty() { isDirty = true; }

    void Awake() { EnsureMesh(); }
    void OnEnable() { MarkDirty(); }

    void Update()
    {
        if (isDirty && autoRebuild)
        {
            ProcessMirroring();
            Rebuild();
            isDirty = false;
        }
    }

    void ProcessMirroring()
    {
        if (segments == null || segments.Length < 2) return;
        for (int i = 1; i < segments.Length; i++)
        {
            if (segments[i].connectToPrevious)
            {
                segments[i].p0 = segments[i - 1].p2;
                segments[i].p1 = segments[i - 1].p2 + (segments[i - 1].p2 - segments[i - 1].p1);
            }
        }
    }

    private void PrecomputeRadialTable(int radialCount)
    {
        if (radialSin == null || radialSin.Length != radialCount)
        {
            radialSin = new float[radialCount];
            radialCos = new float[radialCount];
            for (int i = 0; i < radialCount; i++)
            {
                float angle = (i / (float)radialCount) * Mathf.PI * 2f;
                radialSin[i] = Mathf.Sin(angle);
                radialCos[i] = Mathf.Cos(angle);
            }
        }
    }

    private float[] PreSampleCurve(AnimationCurve curve)
    {
        float[] sample = new float[100];
        if (curve == null || curve.length == 0) { for (int i = 0; i < 100; i++) sample[i] = 1f; return sample; }
        for (int i = 0; i < 100; i++) sample[i] = curve.Evaluate(i / 99f);
        return sample;
    }

    private float GetSampledValue(float[] table, float t)
    {
        return table[Mathf.Clamp((int)(t * 99f), 0, 99)];
    }

    public void Rebuild()
    {
        if (segments == null || segments.Length == 0) { if (mesh != null) mesh.Clear(); return; }

        bool anyBones = false;
        for (int i = 0; i < segments.Length; i++) { if (segments[i].enabled && segments[i].useBones) { anyBones = true; break; } }

        SyncRenderer(anyBones);
        EnsureMesh();

        int totalV = 0; int totalT = 0;
        for (int i = 0; i < segments.Length; i++)
        {
            TubeSegment s = segments[i];
            if (!s.enabled) continue;
            int ring = Mathf.Max(3, s.radialSegments);
            int seg = Mathf.Max(2, s.segments);
            if (s.generateTube) {
                if (s.useHardEdges) { totalV += (seg - 1) * ring * 4; totalT += (seg - 1) * ring * 6; }
                else { totalV += seg * ring; totalT += (seg - 1) * ring * 6; }
            }
            totalV += GetCapVertCount(s.startCap, ring); totalT += GetCapTriCount(s.startCap, ring);
            totalV += GetCapVertCount(s.endCap, ring); totalT += GetCapTriCount(s.endCap, ring);
        }

        if (verts.Length < totalV) {
            verts = new Vector3[totalV]; uvs = new Vector2[totalV]; colors = new Color[totalV]; 
            weights = new BoneWeight[totalV]; tangents = new Vector4[totalV]; vertNormalizedV = new float[totalV];
        }
        if (tris.Length < totalT) tris = new int[totalT];

        int v = 0; int t = 0;
        Vector3 minB = Vector3.one * float.MaxValue; Vector3 maxB = Vector3.one * float.MinValue;
        List<Transform> allBones = new List<Transform>();
        List<Matrix4x4> bindPoses = new List<Matrix4x4>();
        allBones.Add(transform); bindPoses.Add(transform.worldToLocalMatrix * transform.localToWorldMatrix);

        Transform globalLastBone = null;
        bool isFirstSegment = true;

        for (int si = 0; si < segments.Length; si++)
        {
            TubeSegment s = segments[si];
            if (!s.enabled) continue;
            int vStart = v; int boneIdx = 0;

            PrecomputeRadialTable(s.radialSegments);
            sampledRadius = PreSampleCurve(s.radiusProfile);
            sampledShape = PreSampleCurve(s.radialShapeCurve);
            int seg = Mathf.Max(2, s.segments); int ring = s.radialSegments;

            EnsureCurveBuffers(seg);
            float inv = 1f / (seg - 1);
            for (int i = 0; i < seg; i++) {
                float tt = i * inv; float omt = 1f - tt;
                curvePos[i] = s.p0 * (omt * omt) + s.p1 * (2f * omt * tt) + s.p2 * (tt * tt);
                Vector3 d = s.p0 * (-2f * omt) + s.p1 * (2f - 4f * tt) + s.p2 * (2f * tt);
                curveTan[i] = (d.sqrMagnitude < 1e-6f) ? Vector3.forward : d.normalized;
            }

            if (isFirstSegment || !s.connectToPrevious)
            {
                Vector3 T0 = curveTan[0], refDir = (s.p1 - s.p0);
                refDir -= T0 * Vector3.Dot(T0, refDir);
                if (refDir.sqrMagnitude < 1e-6f) refDir = Vector3.Cross(T0, Vector3.up);
                refDir.Normalize();
                globalNprev = refDir;
                globalBprev = Vector3.Cross(T0, globalNprev);
                isFirstSegment = false;
            }

            if (s.useBones) { boneIdx = allBones.Count; globalLastBone = SetupSegmentBones(si, ref allBones, ref bindPoses, globalLastBone, globalNprev); }

            Vector3 startN = globalNprev; Vector3 startB = globalBprev;
            int firstRingIdx = -1, lastRingIdx = -1;
            float accumulatedDist = 0f;

            if (s.generateTube) {
                if (s.useHardEdges) {
                    for (int i = 0; i < seg - 1; i++) {
                        float dist = Vector3.Distance(curvePos[i], curvePos[i+1]);
                        float tc0 = (float)i / (seg - 1), tc1 = (float)(i + 1) / (seg - 1);
                        float v0 = (s.uvMapping == UVMappingType.WorldSpace) ? accumulatedDist : tc0;
                        float v1 = (s.uvMapping == UVMappingType.WorldSpace) ? (accumulatedDist + dist) : tc1;
                        Vector3 T0c = curveTan[i], T1c = curveTan[i+1];
                        Vector3 N0 = (i == 0) ? startN : CalculateParallelTransport(T0c, ref globalNprev, ref globalBprev);
                        Vector3 B0 = Vector3.Cross(T0c, N0);
                        Vector3 N1 = CalculateParallelTransport(T1c, ref globalNprev, ref globalBprev);
                        Vector3 B1 = Vector3.Cross(T1c, N1);
                        float r0 = GetSampledValue(sampledRadius, tc0), r1 = GetSampledValue(sampledRadius, tc1);
                        for (int j = 0; j < ring; j++) {
                            int j1 = (j + 1) % ring;
                            float rs0 = GetSampledValue(sampledShape, (float)j/ring), rs1 = GetSampledValue(sampledShape, (float)j1/ring);
                            int bIdx = v;
                            AddVertex(curvePos[i] + (N0 * radialCos[j] + B0 * radialSin[j]) * r0 * rs0, new Vector2((float)j/ring * s.uvTiling.x + s.uvOffset.x, v0 * s.uvTiling.y + s.uvOffset.y), tc0, Color.Lerp(s.startColor, s.endColor, tc0), T0c, ref v, ref minB, ref maxB);
                            AddVertex(curvePos[i] + (N0 * radialCos[j1] + B0 * radialSin[j1]) * r0 * rs1, new Vector2((float)(j+1)/ring * s.uvTiling.x + s.uvOffset.x, v0 * s.uvTiling.y + s.uvOffset.y), tc0, Color.Lerp(s.startColor, s.endColor, tc0), T0c, ref v, ref minB, ref maxB);
                            AddVertex(curvePos[i+1] + (N1 * radialCos[j] + B1 * radialSin[j]) * r1 * rs0, new Vector2((float)j/ring * s.uvTiling.x + s.uvOffset.x, v1 * s.uvTiling.y + s.uvOffset.y), tc1, Color.Lerp(s.startColor, s.endColor, tc1), T1c, ref v, ref minB, ref maxB);
                            AddVertex(curvePos[i+1] + (N1 * radialCos[j1] + B1 * radialSin[j1]) * r1 * rs1, new Vector2((float)(j+1)/ring * s.uvTiling.x + s.uvOffset.x, v1 * s.uvTiling.y + s.uvOffset.y), tc1, Color.Lerp(s.startColor, s.endColor, tc1), T1c, ref v, ref minB, ref maxB);
                            tris[t++] = bIdx; tris[t++] = bIdx + 2; tris[t++] = bIdx + 1; tris[t++] = bIdx + 1; tris[t++] = bIdx + 2; tris[t++] = bIdx + 3;
                        }
                        accumulatedDist += dist;
                    }
                } else {
                    int tubeBase = v;
                    for (int i = 0; i < seg; i++) {
                        if (i > 0) accumulatedDist += Vector3.Distance(curvePos[i], curvePos[i-1]);
                        float tc = (float)i / (seg - 1);
                        float vCoord = (s.uvMapping == UVMappingType.WorldSpace) ? accumulatedDist : tc;
                        Vector3 T = curveTan[i], N = (i == 0) ? startN : CalculateParallelTransport(T, ref globalNprev, ref globalBprev), B = Vector3.Cross(T, N);
                        if (Mathf.Abs(s.twist) > 0.001f) { Quaternion q = Quaternion.AngleAxis(s.twist * tc, T); N = q * N; B = q * B; }
                        float rad = GetSampledValue(sampledRadius, tc); int rS = v;
                        for (int j = 0; j < ring; j++) {
                            float rs = GetSampledValue(sampledShape, (float)j/ring);
                            AddVertex(curvePos[i] + (N * radialCos[j] + B * radialSin[j]) * rad * rs, new Vector2((float)j/ring * s.uvTiling.x + s.uvOffset.x, vCoord * s.uvTiling.y + s.uvOffset.y), tc, Color.Lerp(s.startColor, s.endColor, tc), T, ref v, ref minB, ref maxB);
                        }
                        if (i == 0) firstRingIdx = rS; if (i == seg - 1) lastRingIdx = rS;
                    }
                    for (int i = 0; i < seg - 1; i++) {
                        int rA = tubeBase + i * ring, rB = tubeBase + (i + 1) * ring;
                        for (int j = 0; j < ring; j++) {
                            int j1 = (j + 1) % ring;
                            tris[t++] = rA + j; tris[t++] = rA + j1; tris[t++] = rB + j; tris[t++] = rA + j1; tris[t++] = rB + j1; tris[t++] = rB + j;
                        }
                    }
                }
            }

            if (s.startCap.type != TubeCapType.None) {
                if (s.startCap.type == TubeCapType.Flat) { int ci; BuildFlat(curvePos[0], s.startColor, ref v, out ci, ref minB, ref maxB); if (firstRingIdx >= 0) BuildFanRev(ci, firstRingIdx, ring, ref t); }
                else if (s.startCap.type == TubeCapType.Rounded) { if (firstRingIdx >= 0) BuildRoundedCap(verts, firstRingIdx, curvePos[0], GetSampledValue(sampledRadius, 0f), ring, s.startCap, -curveTan[0], startN, startB, s.startColor, true, ref v, ref t, ref minB, ref maxB); }
                else if (s.startCap.type == TubeCapType.FullSphere) BuildSphere(curvePos[0], s.startCap, s.startColor, -curveTan[0], startN, startB, ref v, ref t, ref minB, ref maxB);
            }
            if (s.endCap.type != TubeCapType.None) {
                if (s.endCap.type == TubeCapType.Flat) { int ci; BuildFlat(curvePos[seg-1], s.endColor, ref v, out ci, ref minB, ref maxB); if (lastRingIdx >= 0) BuildFan(ci, lastRingIdx, ring, ref t); }
                else if (s.endCap.type == TubeCapType.Rounded) { if (lastRingIdx >= 0) BuildRoundedCap(verts, lastRingIdx, curvePos[seg-1], GetSampledValue(sampledRadius, 1f), ring, s.endCap, curveTan[seg-1], globalNprev, globalBprev, s.endColor, false, ref v, ref t, ref minB, ref maxB); }
                else if (s.endCap.type == TubeCapType.FullSphere) BuildSphere(curvePos[seg-1], s.endCap, s.endColor, curveTan[seg-1], globalNprev, globalBprev, ref v, ref t, ref minB, ref maxB);
            }
            ApplyBoneWeights(vStart, v, s, boneIdx);
        }
        FinalizeMesh(v, t, anyBones, allBones, bindPoses, minB, maxB);
    }

    private void AddVertex(Vector3 p, Vector2 u, float normV, Color c, Vector3 tan, ref int v, ref Vector3 min, ref Vector3 max) {
        verts[v] = p; uvs[v] = u; colors[v] = c; tangents[v] = new Vector4(tan.x, tan.y, tan.z, 1.0f); vertNormalizedV[v] = normV;
        if (p.x < min.x) min.x = p.x; if (p.y < min.y) min.y = p.y; if (p.z < min.z) min.z = p.z;
        if (p.x > max.x) max.x = p.x; if (p.y > max.y) max.y = p.y; if (p.z > max.z) max.z = p.z;
        v++;
    }

    private void FinalizeMesh(int v, int t, bool any, List<Transform> b, List<Matrix4x4> bp, Vector3 min, Vector3 max) {
        Vector3[] fV = new Vector3[v]; Vector2[] fU = new Vector2[v]; Color[] fC = new Color[v]; Vector4[] fTan = new Vector4[v]; int[] fT = new int[t];
        Array.Copy(verts, fV, v); Array.Copy(uvs, fU, v); Array.Copy(colors, fC, v); Array.Copy(tangents, fTan, v); Array.Copy(tris, fT, t);
        if (showWeightsDebug && any) { for (int i = 0; i < v; i++) fC[i] = new Color(weights[i].weight0, 0, 1f - weights[i].weight0, 1f); }
        mesh.Clear(); mesh.vertices = fV; mesh.uv = fU; mesh.colors = fC; mesh.tangents = fTan; mesh.triangles = fT;
        if (any) { BoneWeight[] fW = new BoneWeight[v]; Array.Copy(weights, fW, v); mesh.boneWeights = fW; mesh.bindposes = bp.ToArray(); GetComponent<SkinnedMeshRenderer>().bones = b.ToArray(); }
        mesh.RecalculateNormals(); mesh.bounds = new Bounds((min + max) * 0.5f, (max - min));
    }

    void OnDrawGizmosSelected() {
        if (!showBonesGizmo || segments == null) return;
        Gizmos.color = Color.cyan;
        for (int i = 0; i < segments.Length; i++) {
            if (segments[i].boneInstances == null) continue;
            for (int j = 0; j < segments[i].boneInstances.Count; j++) {
                Transform curr = segments[i].boneInstances[j];
                if (curr == null) continue;
                Gizmos.DrawWireSphere(curr.position, 0.015f);
                if (curr.parent != null && curr.parent != transform) Gizmos.DrawLine(curr.position, curr.parent.position);
            }
        }
    }

    private Vector3 CalculateParallelTransport(Vector3 T, ref Vector3 Np, ref Vector3 Bp) { Vector3 N = Np - T * Vector3.Dot(T, Np); if (N.sqrMagnitude < 1e-6f) N = Vector3.Cross(T, Vector3.up); N.Normalize(); Np = N; Bp = Vector3.Cross(T, N); return N; }
    void BuildFlat(Vector3 c, Color col, ref int v, out int ci, ref Vector3 mi, ref Vector3 ma) { ci = v; AddVertex(c, new Vector2(0.5f, 0.5f), 0f, col, Vector3.up, ref v, ref mi, ref ma); }
    void BuildPoint(Vector3 c, Vector3 d, float s, float r, Color col, ref int v, out int ti, ref Vector3 mi, ref Vector3 ma) { ti = v; AddVertex(c + d.normalized * (r * s), new Vector2(0.5f, 1f), 1f, col, d, ref v, ref mi, ref ma); }
    void BuildFan(int ci, int rS, int r, ref int t) { for (int j = 0; j < r; j++) { tris[t++] = rS + j; tris[t++] = rS + (j + 1) % r; tris[t++] = ci; } }
    void BuildFanRev(int ci, int rS, int r, ref int t) { for (int j = 0; j < r; j++) { tris[t++] = rS + j; tris[t++] = ci; tris[t++] = rS + (j + 1) % r; } }
    void BuildSphere(Vector3 c, CapSettings cp, Color col, Vector3 ax, Vector3 fN, Vector3 fB, ref int v, ref int t, ref Vector3 mi, ref Vector3 ma) {
        int res = Mathf.Max(3, cp.sphereResolution); int g = res + 1; int bV = v;
        for (int iy = 0; iy < g; iy++) {
            float ty = (float)iy / res, th = (ty - 0.5f) * Mathf.PI, cy = Mathf.Cos(th), sy = Mathf.Sin(th);
            for (int ix = 0; ix < g; ix++) {
                float tx = (float)ix / res, ph = tx * Mathf.PI * 2f; Vector3 nL = new Vector3(Mathf.Cos(ph) * cy, sy, Mathf.Sin(ph) * cy);
                AddVertex(c + (fN * nL.x + ax * nL.y + fB * nL.z) * cp.sphereRadius, new Vector2(tx, ty), ty, col, ax, ref v, ref mi, ref ma);
            }
        }
        for (int iy = 0; iy < res; iy++) { int rA = bV + iy * g, rB = bV + (iy + 1) * g; for (int ix = 0; ix < res; ix++) { tris[t++] = rA + ix; tris[t++] = rA + ix + 1; tris[t++] = rB + ix; tris[t++] = rA + ix + 1; tris[t++] = rB + ix + 1; tris[t++] = rB + ix; } }
    }
    void BuildRoundedCap(Vector3[] vA, int rS, Vector3 c, float r, int ring, CapSettings cp, Vector3 ax, Vector3 fN, Vector3 fB, Color col, bool isS, ref int v, ref int t, ref Vector3 mi, ref Vector3 ma) {
        int cB = v, lat = Mathf.Max(2, cp.segments);
        for (int i = 1; i < lat; i++) {
            float tL = (float)i / lat, th = tL * Mathf.PI * 0.5f, rad = r * Mathf.Pow(Mathf.Cos(th), cp.bulge), h = r * Mathf.Sin(th) * cp.scale;
            for (int j = 0; j < ring; j++) { float ph = (j / (float)ring) * Mathf.PI * 2f; AddVertex(c + (fN * Mathf.Cos(ph) + fB * Mathf.Sin(ph)) * rad + ax * h, new Vector2(j / (float)ring, tL), isS ? (1f-tL) : tL, col, ax, ref v, ref mi, ref ma); }
        }
        for (int j = 0; j < ring; j++) { int j1 = (j + 1) % ring; if (isS) { tris[t++] = rS + j; tris[t++] = cB + j; tris[t++] = cB + j1; tris[t++] = rS + j; tris[t++] = cB + j1; tris[t++] = rS + j1; } else { tris[t++] = rS + j; tris[t++] = cB + j1; tris[t++] = cB + j; tris[t++] = rS + j; tris[t++] = rS + j1; tris[t++] = cB + j1; } }
        for (int i = 0; i < lat - 2; i++) { int rA = cB + i * ring, rB = rA + ring; for (int j = 0; j < ring; j++) { int j1 = (j + 1) % ring; if(isS){ tris[t++] = rA + j; tris[t++] = rB + j; tris[t++] = rB + j1; tris[t++] = rA + j; tris[t++] = rB + j1; tris[t++] = rA + j1; } else { tris[t++] = rA + j; tris[t++] = rB + j1; tris[t++] = rB + j; tris[t++] = rA + j; tris[t++] = rA + j1; tris[t++] = rB + j1; } } }
        int pI = v; AddVertex(c + ax * (r * cp.scale), new Vector2(0.5f, 1f), isS ? 0f : 1f, col, ax, ref v, ref mi, ref ma);
        int lS = cB + (lat - 2) * ring; for (int j = 0; j < ring; j++) { tris[t++] = lS + j; if (isS) { tris[t++] = pI; tris[t++] = lS + (j + 1) % ring; } else { tris[t++] = lS + (j + 1) % ring; tris[t++] = pI; } }
    }
    int GetCapVertCount(CapSettings cp, int r) { if (cp.type == TubeCapType.Flat || cp.type == TubeCapType.Point) return r + 1; if (cp.type == TubeCapType.Rounded) return (Mathf.Max(2, cp.segments) * r + 1 + r); if (cp.type == TubeCapType.FullSphere) { int g = Mathf.Max(3, cp.sphereResolution) + 1; return g * g; } return 0; }
    int GetCapTriCount(CapSettings cp, int r) { if (cp.type == TubeCapType.Flat || cp.type == TubeCapType.Point) return r * 3; if (cp.type == TubeCapType.Rounded) return Mathf.Max(2, cp.segments) * r * 6 + r * 3; if (cp.type == TubeCapType.FullSphere) { int res = Mathf.Max(3, cp.sphereResolution); return res * res * 6; } return 0; }
    
    Transform SetupSegmentBones(int si, ref List<Transform> allB, ref List<Matrix4x4> bp, Transform lastB, Vector3 startN) {
        int ct = Mathf.Max(1, segments[si].bonesPerSegment); if (segments[si].boneInstances == null) segments[si].boneInstances = new List<Transform>();
        while (segments[si].boneInstances.Count > ct) { if (segments[si].boneInstances[0]) DestroyImmediate(segments[si].boneInstances[0].gameObject); segments[si].boneInstances.RemoveAt(0); }
        while (segments[si].boneInstances.Count < ct) { GameObject go = new GameObject("Bone"); go.transform.parent = transform; segments[si].boneInstances.Add(go.transform); }
        Vector3 localN = startN;
        for (int b = 0; b < ct; b++) {
            float t = (float)b / (ct > 1 ? (float)(ct - 1) : 1.0f); Transform bone = segments[si].boneInstances[b];
            Vector3 tan = GetBezierTangent(segments[si], t);
            localN = localN - tan * Vector3.Dot(tan, localN); localN.Normalize();
            bone.localPosition = GetBezierPoint(segments[si], t); bone.localRotation = Quaternion.LookRotation(tan, localN);
            bone.name = segments[si].name + "_B" + b; if (segments[si].useNestedChain) { bone.SetParent(lastB == null ? transform : lastB); lastB = bone; } else { bone.SetParent(transform); }
            allB.Add(bone); bp.Add(bone.worldToLocalMatrix * transform.localToWorldMatrix);
        }
        return lastB;
    }
    Vector3 GetBezierPoint(TubeSegment s, float t) { Vector3 m0 = Vector3.Lerp(s.p0, s.p1, t), m1 = Vector3.Lerp(s.p1, s.p2, t); return Vector3.Lerp(m0, m1, t); }
    Vector3 GetBezierTangent(TubeSegment s, float t) { return (2f * (1f - t) * (s.p1 - s.p0) + 2f * t * (s.p2 - s.p1)).normalized; }
    void SyncRenderer(bool skin) { if (wasSkinningActive == skin) return; if (skin) { if (GetComponent<MeshRenderer>()) DestroyImmediate(GetComponent<MeshRenderer>()); if (GetComponent<MeshFilter>()) DestroyImmediate(GetComponent<MeshFilter>()); if (!GetComponent<SkinnedMeshRenderer>()) gameObject.AddComponent<SkinnedMeshRenderer>(); } else { if (GetComponent<SkinnedMeshRenderer>()) DestroyImmediate(GetComponent<SkinnedMeshRenderer>()); if (!GetComponent<MeshFilter>()) gameObject.AddComponent<MeshFilter>(); if (!GetComponent<MeshRenderer>()) gameObject.AddComponent<MeshRenderer>(); } wasSkinningActive = skin; }
    void EnsureMesh() { if (mesh == null) { mesh = new Mesh(); mesh.name = "Tube"; mesh.MarkDynamic(); mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; } if (GetComponent<SkinnedMeshRenderer>()) GetComponent<SkinnedMeshRenderer>().sharedMesh = mesh; else if (GetComponent<MeshFilter>()) GetComponent<MeshFilter>().sharedMesh = mesh; }
}
