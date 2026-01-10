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
        public bool connectToPrevious;

        public Vector3 p0;
        public Vector3 p1;
        public Vector3 p2;

        public int segments;
        public int radialSegments;
        public float twist;

        public CapSettings startCap;
        public CapSettings endCap;

        public UVMappingType uvMapping;
        public AnimationCurve radiusProfile;
        public AnimationCurve radialShapeCurve;
        
        public Vector2 uvTiling;
        public Vector2 uvOffset;

        public Color startColor;
        public Color endColor;
        public bool generateTube;
        public float colorLerpOffset;
        public float colorLerpScale;
        public int colorCutoffSegment;

        public bool useBones;
        public int bonesPerSegment;
        public bool useNestedChain;
        public float blendOffset;
        public float blendScaler;

        [HideInInspector]
        public List<Transform> boneInstances;
    }

    public TubeSegment[] segments;
    public Material tubeMaterial;
    public Color gizmoCurveColor = Color.white;
    public bool showWeightsDebug;
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

    public void SetPoint(int si, int pi, Vector3 pos) { if (si >= 0 && si < segments.Length) { if (pi == 0) segments[si].p0 = pos; else if (pi == 1) segments[si].p1 = pos; else segments[si].p2 = pos; MarkDirty(); } }
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

    public void ProcessMirroring()
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

    public void Rebuild()
    {
        bool anyBones = false;
        if (segments != null) { for (int i = 0; i < segments.Length; i++) { if (segments[i].enabled && segments[i].useBones) { anyBones = true; break; } } }

        SyncRenderer(anyBones);
        EnsureMesh();

        if (segments == null || segments.Length == 0) { if (mesh != null) mesh.Clear(); return; }

        int totalV = 0; int totalT = 0;
        for (int i = 0; i < segments.Length; i++)
        {
            TubeSegment s = segments[i];
            if (!s.enabled) continue;
            int ring = Mathf.Max(3, s.radialSegments); int seg = Mathf.Max(2, s.segments);
            
            if (s.generateTube) {
                totalV += seg * ring; 
                totalT += (seg - 1) * ring * 6;
            }
            
            totalV += GetCapVertCount(s.startCap, ring) + ring;
            totalV += GetCapVertCount(s.endCap, ring) + ring;
            totalT += GetCapTriCount(s.startCap, ring) + GetCapTriCount(s.endCap, ring);
        }

        EnsureBuffers(totalV, totalT);

        int v = 0; int t = 0;
        Vector3 minB = Vector3.one * float.MaxValue; Vector3 maxB = Vector3.one * float.MinValue;
        
        List<Transform> allBones = new List<Transform>();
        List<Matrix4x4> bindPoses = new List<Matrix4x4>();
        
        allBones.Add(transform); 
        bindPoses.Add(transform.worldToLocalMatrix * transform.localToWorldMatrix);

        Transform globalLastBone = null; 
        int lastBoneIdx = 0;
        bool isFirstSeg = true;

        for (int si = 0; si < segments.Length; si++)
        {
            TubeSegment s = segments[si];
            if (!s.enabled) continue;
            int vStart = v;

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

            if (isFirstSeg || !s.connectToPrevious) {
                Vector3 T0 = curveTan[0], refD = (s.p1 - s.p0);
                refD -= T0 * Vector3.Dot(T0, refD); if (refD.sqrMagnitude < 1e-6f) refD = Vector3.Cross(T0, Vector3.up);
                refD.Normalize(); globalNprev = refD; globalBprev = Vector3.Cross(T0, globalNprev); isFirstSeg = false;
            }

            List<int> segmentBoneIndices = null;
            if (s.useBones) { 
                segmentBoneIndices = SetupSegmentBones(si, ref allBones, ref bindPoses, ref globalLastBone, ref lastBoneIdx, globalNprev); 
            }

            Vector3 startN = globalNprev; Vector3 startB = globalBprev;
            float accumulatedDist = 0f;

            if (s.generateTube) {
                int tubeBase = v;
                for (int i = 0; i < seg; i++) {
                    if (i > 0) accumulatedDist += Vector3.Distance(curvePos[i], curvePos[i-1]);
                    float tc = (float)i / (seg - 1); float vC_uv = (s.uvMapping == UVMappingType.WorldSpace) ? accumulatedDist : tc;
                    Vector3 T = curveTan[i], N = (i == 0) ? startN : CalculateParallelTransport(T, ref globalNprev, ref globalBprev), B = Vector3.Cross(T, N);
                    if (Mathf.Abs(s.twist) > 0.001f) { Quaternion q = Quaternion.AngleAxis(s.twist * tc, T); N = q * N; B = q * B; }
                    float rad = sampledRadius[Mathf.Clamp((int)(tc*99),0,99)]; 
                    for (int j = 0; j < ring; j++) {
                        float rs = sampledShape[Mathf.Clamp((int)((float)j/ring*99),0,99)];
                        AddVertex(curvePos[i] + (N * radialCos[j] + B * radialSin[j]) * rad * rs, new Vector2((float)j/ring * s.uvTiling.x + s.uvOffset.x, vC_uv * s.uvTiling.y + s.uvOffset.y), tc, Color.Lerp(s.startColor, s.endColor, tc), T, ref v, ref minB, ref maxB);
                    }
                }
                for (int i = 0; i < seg - 1; i++) {
                    int rA = tubeBase + i * ring, rB = tubeBase + (i + 1) * ring;
                    for (int j = 0; j < ring; j++) { 
                        int j1 = (j + 1) % ring; 
                        tris[t++] = rA + j; tris[t++] = rA + j1; tris[t++] = rB + j; 
                        tris[t++] = rA + j1; tris[t++] = rB + j1; tris[t++] = rB + j; 
                    }
                }
            }

            // Cap Generation
            if (s.startCap.type != TubeCapType.None) {
                int ringS = v; Vector3 T0_c = curveTan[0]; float r0_c = sampledRadius[0];
                for (int j = 0; j < ring; j++) {
                    float rs = sampledShape[Mathf.Clamp((int)((float)j/ring*99),0,99)];
                    AddVertex(curvePos[0] + (startN * radialCos[j] + startB * radialSin[j]) * r0_c * rs, new Vector2((float)j/ring, 0), 0f, s.startColor, -T0_c, ref v, ref minB, ref maxB);
                }
                if (s.startCap.type == TubeCapType.Flat) {
                    int ci; BuildFlat(curvePos[0], s.startColor, 0f, ref v, out ci, ref minB, ref maxB);
                    BuildFanRev(ci, ringS, ring, ref t);
                } else if (s.startCap.type == TubeCapType.Rounded)
                    BuildRoundedCap(verts, ringS, curvePos[0], r0_c, ring, s.startCap, -T0_c, startN, startB, s.startColor, true, ref v, ref t, ref minB, ref maxB);
                else if (s.startCap.type == TubeCapType.FullSphere) 
                    BuildSphere(curvePos[0], s.startCap, s.startColor, -T0_c, startN, startB, 0f, true, ref v, ref t, ref minB, ref maxB);
                else if (s.startCap.type == TubeCapType.Point) {
                    int ti; BuildPoint(curvePos[0], -T0_c, s.startCap.scale, r0_c, s.startColor, 0f, ref v, out ti, ref minB, ref maxB);
                    BuildFanRev(ti, ringS, ring, ref t);
                }
            }
            if (s.endCap.type != TubeCapType.None) {
                int ringE = v; Vector3 T1_c = (curveTan != null && curveTan.Length >= seg) ? curveTan[seg - 1] : Vector3.forward; 
                float r1_c = sampledRadius[99];
                for (int j = 0; j < ring; j++) {
                    float rs = sampledShape[Mathf.Clamp((int)((float)j/ring*99),0,99)];
                    AddVertex(curvePos[seg-1] + (globalNprev * radialCos[j] + globalBprev * radialSin[j]) * r1_c * rs, new Vector2((float)j/ring, 1), 1f, s.endColor, T1_c, ref v, ref minB, ref maxB);
                }
                if (s.endCap.type == TubeCapType.Flat) { int ci; BuildFlat(curvePos[seg-1], s.endColor, 1f, ref v, out ci, ref minB, ref maxB); BuildFan(ci, ringE, ring, ref t); }
                else if (s.endCap.type == TubeCapType.Rounded) BuildRoundedCap(verts, ringE, curvePos[seg-1], r1_c, ring, s.endCap, T1_c, globalNprev, globalBprev, s.endColor, false, ref v, ref t, ref minB, ref maxB);
                else if (s.endCap.type == TubeCapType.FullSphere) 
                    BuildSphere(curvePos[seg-1], s.endCap, s.endColor, T1_c, globalNprev, globalBprev, 1f, false, ref v, ref t, ref minB, ref maxB);
                else if (s.endCap.type == TubeCapType.Point) { int ti; BuildPoint(curvePos[seg-1], T1_c, s.endCap.scale, r1_c, s.endColor, 1f, ref v, out ti, ref minB, ref maxB); BuildFan(ti, ringE, ring, ref t); }
            }
            
            ApplyBoneWeights(vStart, v, s, segmentBoneIndices);
        }

        FinalizeMesh(v, t, anyBones, allBones, bindPoses, minB, maxB);
    }

    private void FinalizeMesh(int v, int t, bool any, List<Transform> b, List<Matrix4x4> bp, Vector3 mi, Vector3 ma) {
        Vector3[] fV = new Vector3[v]; 
        Vector2[] fU = new Vector2[v]; 
        Color[] fC = new Color[v]; 
        Vector4[] fTtan = new Vector4[v]; 
        int[] fTri = new int[t];

        Array.Copy(verts, fV, v); 
        Array.Copy(uvs, fU, v); 
        Array.Copy(colors, fC, v); 
        Array.Copy(tangents, fTtan, v); 
        Array.Copy(tris, fTri, t);

        mesh.Clear(); 
        mesh.vertices = fV; 
        mesh.uv = fU; 
        mesh.colors = fC; 
        mesh.tangents = fTtan; 
        mesh.triangles = fTri;

        Bounds newBounds = new Bounds((mi + ma) * 0.5f, (ma - mi));
        newBounds.Expand(1.5f);
        mesh.bounds = newBounds;

        if (any) {
            BoneWeight[] fW = new BoneWeight[v]; 
            Array.Copy(weights, fW, v);
            int mB = b.Count - 1; 
            for (int i = 0; i < v; i++) { 
                if (fW[i].boneIndex0 > mB) fW[i].boneIndex0 = 0; 
                if (fW[i].boneIndex1 > mB) fW[i].boneIndex1 = 0; 
            }

            if (showWeightsDebug) { 
                for (int i = 0; i < v; i++) {
                    float h0 = (fW[i].boneIndex0 * 0.23f) % 1.0f;
                    float h1 = (fW[i].boneIndex1 * 0.23f) % 1.0f;
                    Color c0 = Color.HSVToRGB(h0, 0.8f, 1.0f);
                    Color c1 = Color.HSVToRGB(h1, 0.8f, 1.0f);
                    fC[i] = Color.Lerp(c1, c0, fW[i].weight0);
                }
                mesh.colors = fC; 
            }

            mesh.bindposes = bp.ToArray(); 
            mesh.boneWeights = fW;

            SkinnedMeshRenderer smr = GetComponent<SkinnedMeshRenderer>();
            smr.bones = b.ToArray(); 
            smr.sharedMesh = mesh;
            smr.localBounds = newBounds;
        }
        else {
            MeshFilter mf = GetComponent<MeshFilter>();
            if (mf != null) mf.sharedMesh = mesh;
        }

        mesh.RecalculateNormals(); 
    }

    private List<int> SetupSegmentBones(int si, ref List<Transform> allB, ref List<Matrix4x4> bp, ref Transform lastB, ref int lastBoneIdx, Vector3 startN)
    {
        TubeSegment s = segments[si];
        bool isShared = s.connectToPrevious && si > 0 && lastB != null;
        int requiredBones = Mathf.Max(1, s.bonesPerSegment);
        int bonesToCreate = isShared ? requiredBones - 1 : requiredBones;

        if (s.boneInstances == null) s.boneInstances = new List<Transform>();

        while (s.boneInstances.Count > bonesToCreate) { 
            int last = s.boneInstances.Count - 1; 
            if (s.boneInstances[last]) DestroyImmediate(s.boneInstances[last].gameObject); 
            s.boneInstances.RemoveAt(last); 
        }
        while (s.boneInstances.Count < bonesToCreate) { 
            GameObject go = new GameObject("Bone"); 
            go.transform.parent = transform; 
            s.boneInstances.Add(go.transform); 
        }

        List<int> segmentIndices = new List<int>();
        if (isShared) segmentIndices.Add(lastBoneIdx);

        Vector3 localN = startN;
        for (int b = 0; b < bonesToCreate; b++)
        {
            float t = isShared ? (float)(b + 1) / (requiredBones - 1) : (float)b / (requiredBones > 1 ? requiredBones - 1 : 1);
            Transform bone = s.boneInstances[b];
            Vector3 tan = GetBezierTangent(s, t);
            localN = localN - tan * Vector3.Dot(tan, localN); 
            localN.Normalize();

            Transform targetParent = (s.useNestedChain && lastB != null) ? lastB : transform;
            if (bone.parent != targetParent) bone.SetParent(targetParent);

            bone.position = transform.TransformPoint(GetBezierPoint(s, t));
            bone.rotation = transform.rotation * Quaternion.LookRotation(tan, localN);
            bone.localScale = Vector3.one;
            bone.name = s.name + "_B" + (isShared ? b + 1 : b);

            lastB = bone;
            lastBoneIdx = allB.Count; 
            segmentIndices.Add(lastBoneIdx);
            allB.Add(bone);
            bp.Add(bone.worldToLocalMatrix * transform.localToWorldMatrix);
        }
        return segmentIndices;
    }

    void ApplyBoneWeights(int sV, int eV, TubeSegment s, List<int> boneIndices) {
        if (!s.useBones || boneIndices == null || boneIndices.Count == 0) { 
            for (int i = sV; i < eV; i++) { weights[i].boneIndex0 = 0; weights[i].weight0 = 1f; } return; 
        }

        int count = boneIndices.Count;
        for (int i = sV; i < eV; i++) {
            float t = Mathf.Clamp01((vertNormalizedV[i] + s.blendOffset) * s.blendScaler);
            float bT = t * (count - 1);
            int localA = Mathf.FloorToInt(bT), localB = Mathf.Clamp(localA + 1, 0, count - 1);
            float wB = bT - localA;

            weights[i].boneIndex0 = boneIndices[localA];
            weights[i].weight0 = 1f - wB;
            weights[i].boneIndex1 = boneIndices[localB];
            weights[i].weight1 = wB;
        }
    }

    private void AddVertex(Vector3 p, Vector2 u, float nV, Color c, Vector3 tan, ref int v, ref Vector3 mi, ref Vector3 ma) {
        verts[v] = p; uvs[v] = u; colors[v] = c; tangents[v] = new Vector4(tan.x, tan.y, tan.z, 1.0f); vertNormalizedV[v] = nV;
        if (p.x < mi.x) mi.x = p.x; if (p.y < mi.y) mi.y = p.y; if (p.z < mi.z) mi.z = p.z;
        if (p.x > ma.x) ma.x = p.x; if (p.y > ma.y) ma.y = p.y; if (p.z > ma.z) ma.z = p.z;
        v++;
    }

    private Vector3 CalculateParallelTransport(Vector3 T, ref Vector3 Np, ref Vector3 Bp) { Vector3 N = Np - T * Vector3.Dot(T, Np); if (N.sqrMagnitude < 1e-6f) N = Vector3.Cross(T, Vector3.up); N.Normalize(); Np = N; Bp = Vector3.Cross(T, N); return N; }
    void BuildFlat(Vector3 c, Color col, float nV, ref int v, out int ci, ref Vector3 mi, ref Vector3 ma) { ci = v; AddVertex(c, new Vector2(0.5f, 0.5f), nV, col, Vector3.up, ref v, ref mi, ref ma); }
    void BuildPoint(Vector3 c, Vector3 d, float s, float r, Color col, float nV, ref int v, out int ti, ref Vector3 mi, ref Vector3 ma) { ti = v; AddVertex(c + d.normalized * (r * s), new Vector2(0.5f, 1f), nV, col, d, ref v, ref mi, ref ma); }
    void BuildFan(int ci, int rS, int r, ref int t) { for (int j = 0; j < r; j++) { tris[t++] = rS + j; tris[t++] = rS + (j + 1) % r; tris[t++] = ci; } }
    void BuildFanRev(int ci, int rS, int r, ref int t) { for (int j = 0; j < r; j++) { tris[t++] = rS + j; tris[t++] = ci; tris[t++] = rS + (j + 1) % r; } }
    
    void BuildSphere(Vector3 c, CapSettings cp, Color col, Vector3 ax, Vector3 fN, Vector3 fB, float nV, bool reverse, ref int v, ref int t, ref Vector3 mi, ref Vector3 ma) {
        int res = Mathf.Max(3, cp.sphereResolution); int g = res + 1; int bV = v;
        for (int iy = 0; iy < g; iy++) {
            float ty = (float)iy / res, th = (ty - 0.5f) * Mathf.PI, cy = Mathf.Cos(th), sy = Mathf.Sin(th);
            for (int ix = 0; ix < g; ix++) {
                float tx = (float)ix / res, ph = tx * Mathf.PI * 2f; float rs = sampledShape[Mathf.Clamp((int)(tx * 99), 0, 99)];
                Vector3 nL = new Vector3(Mathf.Cos(ph) * cy, sy, Mathf.Sin(ph) * cy);
                AddVertex(c + (fN * nL.x + ax * nL.y + fB * nL.z) * cp.sphereRadius * rs, new Vector2(tx, ty), nV, col, ax, ref v, ref mi, ref ma);
            }
        }
        for (int iy = 0; iy < res; iy++) { 
            int rA = bV + iy * g, rB = bV + (iy + 1) * g; 
            for (int ix = 0; ix < res; ix++) { 
                int i0 = rA + ix, i1 = rA + ix + 1, i2 = rB + ix, i3 = rB + ix + 1;
                if (reverse) { tris[t++] = i0; tris[t++] = i2; tris[t++] = i1; tris[t++] = i1; tris[t++] = i2; tris[t++] = i3; } 
                else { tris[t++] = i0; tris[t++] = i1; tris[t++] = i2; tris[t++] = i1; tris[t++] = i3; tris[t++] = i2; }
            } 
        }
    }
    
    void BuildRoundedCap(Vector3[] vA, int rS, Vector3 c, float r, int ring, CapSettings cp, Vector3 ax, Vector3 fN, Vector3 fB, Color col, bool isS, ref int v, ref int t, ref Vector3 mi, ref Vector3 ma) {
        int cB = v, lat = Mathf.Max(2, cp.segments); float nV = isS ? 0f : 1f;
        for (int i = 1; i < lat; i++) {
            float tL = (float)i / lat, th = tL * Mathf.PI * 0.5f, rad = r * Mathf.Pow(Mathf.Cos(th), cp.bulge), h = r * Mathf.Sin(th) * cp.scale;
            for (int j = 0; j < ring; j++) { float ph = (j / (float)ring) * Mathf.PI * 2f; float rs = sampledShape[Mathf.Clamp((int)((j/(float)ring)*99), 0, 99)];
                AddVertex(c + (fN * Mathf.Cos(ph) + fB * Mathf.Sin(ph)) * rad * rs + ax * h, new Vector2(j / (float)ring, tL), nV, col, ax, ref v, ref mi, ref ma); }
        }
        for (int j = 0; j < ring; j++) { int j1 = (j + 1) % ring; if (isS) { tris[t++] = rS+j; tris[t++] = cB+j; tris[t++] = cB+j1; tris[t++] = rS+j; tris[t++] = cB+j1; tris[t++] = rS+j1; } else { tris[t++] = rS+j; tris[t++] = cB+j1; tris[t++] = cB+j; tris[t++] = rS+j; tris[t++] = rS+j1; tris[t++] = cB+j1; } }
        for (int i = 0; i < lat - 2; i++) { int rA = cB + i * ring, rB = rA + ring; for (int j = 0; j < ring; j++) { int j1 = (j + 1) % ring; if(isS){ tris[t++] = rA+j; tris[t++] = rB+j; tris[t++] = rB+j1; tris[t++] = rA+j; tris[t++] = rB+j1; tris[t++] = rA+j1; } else { tris[t++] = rA+j; tris[t++] = rB+j1; tris[t++] = rB+j; tris[t++] = rA+j; tris[t++] = rA+j1; tris[t++] = rB+j1; } } }
        int pI = v; AddVertex(c + ax * (r * cp.scale), new Vector2(0.5f, 1f), nV, col, ax, ref v, ref mi, ref ma);
        int lS = cB + (lat - 2) * ring; for (int j = 0; j < ring; j++) { tris[t++] = lS+j; if (isS) { tris[t++] = pI; tris[t++] = lS+(j+1)%ring; } else { tris[t++] = lS+(j+1)%ring; tris[t++] = pI; } }
    }

    Vector3 GetBezierPoint(TubeSegment s, float t) { Vector3 m0 = Vector3.Lerp(s.p0, s.p1, t), m1 = Vector3.Lerp(s.p1, s.p2, t); return Vector3.Lerp(m0, m1, t); }
    Vector3 GetBezierTangent(TubeSegment s, float t) { return (2f * (1f - t) * (s.p1 - s.p0) + 2f * t * (s.p2 - s.p1)).normalized; }
    void EnsureCurveBuffers(int seg) { if (curvePos == null || curvePos.Length != seg) { curvePos = new Vector3[seg]; curveTan = new Vector3[seg]; } }
    void PrecomputeRadialTable(int radialCount) { radialSin = new float[radialCount]; radialCos = new float[radialCount]; for (int i = 0; i < radialCount; i++) { float angle = (i / (float)radialCount) * Mathf.PI * 2f; radialSin[i] = Mathf.Sin(angle); radialCos[i] = Mathf.Cos(angle); } }
    float[] PreSampleCurve(AnimationCurve curve) { float[] sample = new float[100]; if (curve == null || curve.length == 0) { for (int i = 0; i < 100; i++) sample[i] = 1f; return sample; } for (int i = 0; i < 100; i++) sample[i] = curve.Evaluate(i / 99f); return sample; }
    int GetCapVertCount(CapSettings cp, int r) { if (cp.type == TubeCapType.None) return 0; if (cp.type == TubeCapType.Flat || cp.type == TubeCapType.Point) return r + 1; if (cp.type == TubeCapType.Rounded) return (Mathf.Max(2, cp.segments) * r + 1 + r); if (cp.type == TubeCapType.FullSphere) { int g = Mathf.Max(3, cp.sphereResolution) + 1; return g * g; } return 0; }
    int GetCapTriCount(CapSettings cp, int r) { if (cp.type == TubeCapType.None) return 0; if (cp.type == TubeCapType.Flat || cp.type == TubeCapType.Point) return r * 3; if (cp.type == TubeCapType.Rounded) return Mathf.Max(2, cp.segments) * r * 6 + r * 3; if (cp.type == TubeCapType.FullSphere) { int res = Mathf.Max(3, cp.sphereResolution); return res * res * 6; } return 0; }
    
    void EnsureBuffers(int totalV, int totalT) {
        if (verts.Length != totalV) {
            verts = new Vector3[totalV]; uvs = new Vector2[totalV]; colors = new Color[totalV];
            weights = new BoneWeight[totalV]; tangents = new Vector4[totalV]; vertNormalizedV = new float[totalV];
        }
        else { Array.Clear(weights, 0, weights.Length); }
        if (tris.Length != totalT) tris = new int[totalT];
    }

    void SyncRenderer(bool skin) {
        if (wasSkinningActive == skin && (skin ? (GetComponent<SkinnedMeshRenderer>() != null) : (GetComponent<MeshRenderer>() != null))) {
            Renderer r = GetComponent<Renderer>(); if (r != null && r.sharedMaterial != tubeMaterial) r.sharedMaterial = tubeMaterial; return;
        }
        if (skin) {
            if (GetComponent<MeshRenderer>()) DestroyImmediate(GetComponent<MeshRenderer>()); if (GetComponent<MeshFilter>()) DestroyImmediate(GetComponent<MeshFilter>());
            if (GetComponent<SkinnedMeshRenderer>() == null) gameObject.AddComponent<SkinnedMeshRenderer>().sharedMaterial = tubeMaterial;
        } else {
            if (GetComponent<SkinnedMeshRenderer>()) DestroyImmediate(GetComponent<SkinnedMeshRenderer>());
            if (GetComponent<MeshFilter>() == null) gameObject.AddComponent<MeshFilter>();
            if (GetComponent<MeshRenderer>() == null) gameObject.AddComponent<MeshRenderer>().sharedMaterial = tubeMaterial;
        }
        wasSkinningActive = skin;
    }

    void EnsureMesh() {
        if (mesh == null) { mesh = new Mesh(); mesh.name = "Tube"; mesh.MarkDynamic(); mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; }
        SkinnedMeshRenderer smr = GetComponent<SkinnedMeshRenderer>();
        if (smr != null) smr.sharedMesh = mesh; else { MeshFilter mf = GetComponent<MeshFilter>(); if (mf != null) mf.sharedMesh = mesh; }
    }
}
