using UnityEngine;

[ExecuteInEditMode]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
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

        public int colorCutoffSegment; // 0 = all end color, <0 or >= seg = disabled, in-range = hard cutoff

        public float capScale; // 1 = normal, >1 = extrude, <1 = shrink
    }

    public TubeSegment[] segments;
    public Color gizmoCurveColor = Color.white;

    Mesh mesh;

    Vector3[] verts = new Vector3[0];
    Vector2[] uvs = new Vector2[0];
    Color[] colors = new Color[0];
    int[] tris = new int[0];

    Vector3[] curvePos = new Vector3[0];
    Vector3[] curveTan = new Vector3[0];

    int[] ringStarts = new int[0];

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

    public void MarkDirty()
    {
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
        if (mesh != null)
            UnityEditor.EditorUtility.SetDirty(mesh);
#endif
    }

    void Awake()
    {
        var mf = GetComponent<MeshFilter>();
        if (mesh == null)
        {
            mesh = new Mesh();
            mesh.name = "TubeBuilderBatchMesh";
            mesh.MarkDynamic();
        }
        if (mf.sharedMesh != mesh)
            mf.sharedMesh = mesh;
    }

    void OnEnable()
    {
        Awake();
        Rebuild();
    }

    void Update()
    {
        Rebuild();
    }

    public void Rebuild()
    {
        EnsureMesh();
        if (segments == null || segments.Length == 0)
        {
            if (mesh != null)
                mesh.Clear();
            return;
        }

        int totalV = 0;
        int totalT = 0;

        // pass 1: count vertices and triangles
        for (int i = 0; i < segments.Length; i++)
        {
            TubeSegment s = segments[i];
            if (!s.enabled) continue;

            int seg = Mathf.Max(2, s.segments);
            int ring = Mathf.Max(3, s.radialSegments);
            int lat = s.roundedCapSegments > 0 ? Mathf.Max(2, s.roundedCapSegments) : LatSeg(ring);

            bool fullSphere =
                (s.capStart == TubeCapType.FullSphere &&
                 s.capEnd == TubeCapType.FullSphere &&
                 s.segments <= 1);

            if (fullSphere)
            {
                int g = ring + 1;
                totalV += g * g;
                totalT += ring * ring * 6;
                continue;
            }

            bool generateTube = s.generateTube;

            bool hardCut = false;
            int cutIndex = s.colorCutoffSegment;

            if (cutIndex > 0 && cutIndex < seg)
            {
                hardCut = true;
            }

            if (generateTube)
            {
                int extraRing = hardCut ? 1 : 0;
                int ringCount = seg + extraRing;
                totalV += ringCount * ring;
                totalT += (ringCount - 1) * ring * 6;
            }

            // start cap
            if (s.capStart == TubeCapType.Flat || s.capStart == TubeCapType.Point)
            {
                if (generateTube) totalV += 1 + ring;
                else totalV += ring + 1;
                totalT += ring * 3;
            }
            else if (s.capStart == TubeCapType.Rounded)
            {
                if (generateTube) totalV += lat * ring + 1 + ring;
                else totalV += ring + lat * ring + 1;
                totalT += lat * ring * 6 + ring * 3;
            }
            else if (s.capStart == TubeCapType.FullSphere)
            {
                int g = ring + 1;
                totalV += g * g;
                totalT += ring * ring * 6;
            }

            // end cap
            if (s.capEnd == TubeCapType.Flat || s.capEnd == TubeCapType.Point)
            {
                if (generateTube) totalV += 1 + ring;
                else totalV += ring + 1;
                totalT += ring * 3;
            }
            else if (s.capEnd == TubeCapType.Rounded)
            {
                if (generateTube) totalV += lat * ring + 1 + ring;
                else totalV += ring + lat * ring + 1;
                totalT += lat * ring * 6 + ring * 3;
            }
            else if (s.capEnd == TubeCapType.FullSphere)
            {
                int g = ring + 1;
                totalV += g * g;
                totalT += ring * ring * 6;
            }
        }

        if (verts.Length != totalV)
        {
            verts = new Vector3[totalV];
            uvs = new Vector2[totalV];
            colors = new Color[totalV];
        }
        if (tris.Length != totalT)
            tris = new int[totalT];

        int v = 0;
        int t = 0;

        // pass 2: build geometry
        for (int si = 0; si < segments.Length; si++)
        {
            TubeSegment s = segments[si];
            if (!s.enabled) continue;

            int seg = Mathf.Max(2, s.segments);
            int ring = Mathf.Max(3, s.radialSegments);
            int latSeg = s.roundedCapSegments > 0 ? Mathf.Max(2, s.roundedCapSegments) : LatSeg(ring);

            Color startColor = s.startColor;
            Color endColor = s.endColor;

            bool fullSphere =
                (s.capStart == TubeCapType.FullSphere &&
                 s.capEnd == TubeCapType.FullSphere &&
                 s.segments <= 1);

            if (fullSphere)
            {
                float rSphere = s.sphereRadius > 0f ? s.sphereRadius :
                    (s.radiusProfile != null ? s.radiusProfile.Evaluate(0f) : 0.03f);

                Vector3 P0_fs = s.p0;
                Vector3 P1_fs = s.p1;
                Vector3 P2_fs = s.p2;

                Vector3 d_fs =
                    P0_fs * (-2f) +
                    P1_fs * (2f) +
                    P2_fs * (0f);

                if (d_fs.sqrMagnitude < 1e-6f)
                    d_fs = Vector3.forward;

                Vector3 T_fs = d_fs.normalized;

                Vector3 refDir_fs = P1_fs - P0_fs;
                refDir_fs -= T_fs * Vector3.Dot(T_fs, refDir_fs);

                if (refDir_fs.sqrMagnitude < 1e-6f)
                {
                    refDir_fs = Vector3.Cross(T_fs, Vector3.up);
                    if (refDir_fs.sqrMagnitude < 1e-6f)
                        refDir_fs = Vector3.Cross(T_fs, Vector3.right);
                }

                refDir_fs.Normalize();
                Vector3 N_fs = refDir_fs;
                Vector3 B_fs = Vector3.Cross(T_fs, N_fs);

                BuildSphere(
                    s.p0,
                    rSphere,
                    ring,
                    startColor,
                    T_fs,
                    N_fs,
                    B_fs,
                    0f,
                    ref v,
                    ref t
                );

                continue;
            }

            EnsureCurveBuffers(seg);

            Vector3 P0 = s.p0;
            Vector3 P1 = s.p1;
            Vector3 P2 = s.p2;

            float inv = 1f / (seg - 1);

            for (int i = 0; i < seg; i++)
            {
                float tt = i * inv;
                float omt = 1f - tt;

                curvePos[i] =
                    P0 * (omt * omt) +
                    P1 * (2f * omt * tt) +
                    P2 * (tt * tt);

                Vector3 d =
                    P0 * (-2f * omt) +
                    P1 * (2f - 4f * tt) +
                    P2 * (2f * tt);

                if (d.sqrMagnitude < 1e-6f)
                    d = Vector3.forward;

                curveTan[i] = d.normalized;
            }

            Vector3 T0 = curveTan[0];
            Vector3 refDir = P1 - P0;
            refDir -= T0 * Vector3.Dot(T0, refDir);

            if (refDir.sqrMagnitude < 1e-6f)
            {
                refDir = Vector3.Cross(T0, Vector3.up);
                if (refDir.sqrMagnitude < 1e-6f)
                    refDir = Vector3.Cross(T0, Vector3.right);
            }

            refDir.Normalize();

            Vector3 Nprev = refDir;
            Vector3 Bprev = Vector3.Cross(T0, Nprev);

            Vector3 startN = Nprev;
            Vector3 startB = Bprev;

            float twistTotalDeg = s.twist;

            bool hardCut = false;
            bool hardCutAllEnd = false;
            int cutIndex = s.colorCutoffSegment;

            if (cutIndex == 0)
            {
                hardCutAllEnd = true;
            }
            else if (cutIndex > 0 && cutIndex < seg)
            {
                hardCut = true;
            }

            int firstRing = -1;
            int lastRing = -1;

            if (s.generateTube)
            {
                int tubeBase = v;

                if (hardCutAllEnd)
                {
                    // whole tube uses end color, no duplication, no seam
                    for (int i = 0; i < seg; i++)
                    {
                        Vector3 Tcur = curveTan[i];
                        Vector3 N, B;

                        if (i == 0)
                        {
                            N = Nprev;
                            B = Bprev;
                        }
                        else
                        {
                            Vector3 Np = Nprev - Tcur * Vector3.Dot(Tcur, Nprev);
                            if (Np.sqrMagnitude < 1e-6f)
                            {
                                Np = Vector3.Cross(Tcur, Vector3.up);
                                if (Np.sqrMagnitude < 1e-6f)
                                    Np = Vector3.Cross(Tcur, Vector3.right);
                            }

                            N = Np.normalized;
                            B = Vector3.Cross(Tcur, N);

                            Nprev = N;
                            Bprev = B;
                        }

                        float tc = seg > 1 ? (float)i / (seg - 1) : 0f;

                        if (Mathf.Abs(s.twist) > 0.0001f)
                        {
                            float twistHereDeg = twistTotalDeg * tc;
                            Quaternion twistQ = Quaternion.AngleAxis(twistHereDeg, Tcur);
                            N = twistQ * N;
                            B = twistQ * B;
                        }

                        float rad = s.radiusProfile != null ? s.radiusProfile.Evaluate(tc) : 0.03f;
                        if (rad < 0.0001f) rad = 0.0001f;

                        int ringStart = v;

                        for (int j = 0; j < ring; j++)
                        {
                            float ang = (j / (float)ring) * Mathf.PI * 2f;
                            float cs = Mathf.Cos(ang);
                            float sn = Mathf.Sin(ang);

                            Vector3 pos = curvePos[i] + (N * cs + B * sn) * rad;

                            verts[v] = pos;
                            uvs[v] = new Vector2(j / (float)ring, tc);
                            colors[v] = endColor;
                            v++;
                        }

                        if (i == 0) firstRing = ringStart;
                        if (i == seg - 1) lastRing = ringStart;
                    }

                    // stitch tube normally
                    for (int i = 0; i < seg - 1; i++)
                    {
                        int ringA = tubeBase + i * ring;
                        int ringB = tubeBase + (i + 1) * ring;

                        for (int j = 0; j < ring; j++)
                        {
                            int j1 = (j + 1) % ring;

                            int a = ringA + j;
                            int b = ringA + j1;
                            int c0 = ringB + j;
                            int d = ringB + j1;

                            tris[t++] = a; tris[t++] = b; tris[t++] = c0;
                            tris[t++] = b; tris[t++] = d; tris[t++] = c0;
                        }
                    }
                }
                else if (!hardCut)
                {
                    // continuous color gradient mode
                    for (int i = 0; i < seg; i++)
                    {
                        Vector3 Tcur = curveTan[i];
                        Vector3 N, B;

                        if (i == 0)
                        {
                            N = Nprev;
                            B = Bprev;
                        }
                        else
                        {
                            Vector3 Np = Nprev - Tcur * Vector3.Dot(Tcur, Nprev);
                            if (Np.sqrMagnitude < 1e-6f)
                            {
                                Np = Vector3.Cross(Tcur, Vector3.up);
                                if (Np.sqrMagnitude < 1e-6f)
                                    Np = Vector3.Cross(Tcur, Vector3.right);
                            }

                            N = Np.normalized;
                            B = Vector3.Cross(Tcur, N);

                            Nprev = N;
                            Bprev = B;
                        }

                        float tc = seg > 1 ? (float)i / (seg - 1) : 0f;

                        if (Mathf.Abs(s.twist) > 0.0001f)
                        {
                            float twistHereDeg = twistTotalDeg * tc;
                            Quaternion twistQ = Quaternion.AngleAxis(twistHereDeg, Tcur);
                            N = twistQ * N;
                            B = twistQ * B;
                        }

                        float rad = s.radiusProfile != null ? s.radiusProfile.Evaluate(tc) : 0.03f;
                        if (rad < 0.0001f) rad = 0.0001f;

                        float tc2 = tc;
                        tc2 = tc2 * s.colorLerpScale;
                        tc2 = tc2 + s.colorLerpOffset;
                        tc2 = Mathf.Clamp01(tc2);
                        Color cHere = LerpColor(startColor, endColor, tc2);

                        int ringStart = v;

                        for (int j = 0; j < ring; j++)
                        {
                            float ang = (j / (float)ring) * Mathf.PI * 2f;
                            float cs = Mathf.Cos(ang);
                            float sn = Mathf.Sin(ang);

                            Vector3 pos = curvePos[i] + (N * cs + B * sn) * rad;

                            verts[v] = pos;
                            uvs[v] = new Vector2(j / (float)ring, tc);
                            colors[v] = cHere;
                            v++;
                        }

                        if (i == 0) firstRing = ringStart;
                        if (i == seg - 1) lastRing = ringStart;
                    }

                    // stitch tube
                    for (int i = 0; i < seg - 1; i++)
                    {
                        int ringA = tubeBase + i * ring;
                        int ringB = tubeBase + (i + 1) * ring;

                        for (int j = 0; j < ring; j++)
                        {
                            int j1 = (j + 1) % ring;

                            int a = ringA + j;
                            int b = ringA + j1;
                            int c0 = ringB + j;
                            int d = ringB + j1;

                            tris[t++] = a; tris[t++] = b; tris[t++] = c0;
                            tris[t++] = b; tris[t++] = d; tris[t++] = c0;
                        }
                    }
                }
                else
                {
                    // hard color cutoff with duplicated ring at index
                    int maxRings = seg + 1;
                    EnsureRingStarts(maxRings);
                    int ringCount = 0;
                    int seamRingIndex = -1;

                    for (int i = 0; i < seg; i++)
                    {
                        Vector3 Tcur = curveTan[i];
                        Vector3 N, B;

                        if (i == 0)
                        {
                            N = Nprev;
                            B = Bprev;
                        }
                        else
                        {
                            Vector3 Np = Nprev - Tcur * Vector3.Dot(Tcur, Nprev);
                            if (Np.sqrMagnitude < 1e-6f)
                            {
                                Np = Vector3.Cross(Tcur, Vector3.up);
                                if (Np.sqrMagnitude < 1e-6f)
                                    Np = Vector3.Cross(Tcur, Vector3.right);
                            }

                            N = Np.normalized;
                            B = Vector3.Cross(Tcur, N);

                            Nprev = N;
                            Bprev = B;
                        }

                        float tc = seg > 1 ? (float)i / (seg - 1) : 0f;

                        if (Mathf.Abs(s.twist) > 0.0001f)
                        {
                            float twistHereDeg = twistTotalDeg * tc;
                            Quaternion twistQ = Quaternion.AngleAxis(twistHereDeg, Tcur);
                            N = twistQ * N;
                            B = twistQ * B;
                        }

                        float rad = s.radiusProfile != null ? s.radiusProfile.Evaluate(tc) : 0.03f;
                        if (rad < 0.0001f) rad = 0.0001f;

                        if (i < cutIndex)
                        {
                            int ringStart = v;
                            ringStarts[ringCount++] = ringStart;

                            for (int j = 0; j < ring; j++)
                            {
                                float ang = (j / (float)ring) * Mathf.PI * 2f;
                                float cs = Mathf.Cos(ang);
                                float sn = Mathf.Sin(ang);

                                Vector3 pos = curvePos[i] + (N * cs + B * sn) * rad;

                                verts[v] = pos;
                                uvs[v] = new Vector2(j / (float)ring, tc);
                                colors[v] = startColor;
                                v++;
                            }
                        }
                        else if (i == cutIndex)
                        {
                            // start side ring
                            int ringStartStart = v;
                            ringStarts[ringCount++] = ringStartStart;

                            for (int j = 0; j < ring; j++)
                            {
                                float ang = (j / (float)ring) * Mathf.PI * 2f;
                                float cs = Mathf.Cos(ang);
                                float sn = Mathf.Sin(ang);

                                Vector3 pos = curvePos[i] + (N * cs + B * sn) * rad;

                                verts[v] = pos;
                                uvs[v] = new Vector2(j / (float)ring, tc);
                                colors[v] = startColor;
                                v++;
                            }

                            seamRingIndex = ringCount - 1;

                            // end side ring
                            int ringStartEnd = v;
                            ringStarts[ringCount++] = ringStartEnd;

                            for (int j = 0; j < ring; j++)
                            {
                                float ang = (j / (float)ring) * Mathf.PI * 2f;
                                float cs = Mathf.Cos(ang);
                                float sn = Mathf.Sin(ang);

                                Vector3 pos = curvePos[i] + (N * cs + B * sn) * rad;

                                verts[v] = pos;
                                uvs[v] = new Vector2(j / (float)ring, tc);
                                colors[v] = endColor;
                                v++;
                            }
                        }
                        else
                        {
                            int ringStart = v;
                            ringStarts[ringCount++] = ringStart;

                            for (int j = 0; j < ring; j++)
                            {
                                float ang = (j / (float)ring) * Mathf.PI * 2f;
                                float cs = Mathf.Cos(ang);
                                float sn = Mathf.Sin(ang);

                                Vector3 pos = curvePos[i] + (N * cs + B * sn) * rad;

                                verts[v] = pos;
                                uvs[v] = new Vector2(j / (float)ring, tc);
                                colors[v] = endColor;
                                v++;
                            }
                        }
                    }

                    if (ringCount > 0)
                    {
                        firstRing = ringStarts[0];
                        lastRing = ringStarts[ringCount - 1];
                    }

                    // stitch rings, skipping seam pair
                    for (int k = 0; k < ringCount - 1; k++)
                    {
                        if (k == seamRingIndex)
                            continue;

                        int ringA = ringStarts[k];
                        int ringB = ringStarts[k + 1];

                        for (int j = 0; j < ring; j++)
                        {
                            int j1 = (j + 1) % ring;

                            int a = ringA + j;
                            int b = ringA + j1;
                            int c0 = ringB + j;
                            int d = ringB + j1;

                            tris[t++] = a; tris[t++] = b; tris[t++] = c0;
                            tris[t++] = b; tris[t++] = d; tris[t++] = c0;
                        }
                    }
                }
            }
            else
            {
                // geometry with only caps and no continuous tube

                // start ring (start frame already correct)
                if (s.capStart != TubeCapType.None && s.capStart != TubeCapType.FullSphere)
                {
                    firstRing = v;
                    float r0ng = s.radiusProfile != null ? s.radiusProfile.Evaluate(0f) : 0.03f;
                    if (r0ng < 0.0001f) r0ng = 0.0001f;

                    for (int j = 0; j < ring; j++)
                    {
                        float ang = (j / (float)ring) * Mathf.PI * 2f;
                        float cs = Mathf.Cos(ang);
                        float sn = Mathf.Sin(ang);

                        Vector3 pos = curvePos[0] + (startN * cs + startB * sn) * r0ng;

                        verts[v] = pos;
                        uvs[v] = new Vector2(j / (float)ring, 0f);
                        colors[v] = startColor;
                        v++;
                    }
                }

                // rebuild end frame properly when no tube is generated
                Vector3 endT2 = curveTan[seg - 1];

                Vector3 Nend = startN - endT2 * Vector3.Dot(endT2, startN);
                if (Nend.sqrMagnitude < 1e-6f)
                {
                    Nend = Vector3.Cross(endT2, Vector3.up);
                    if (Nend.sqrMagnitude < 1e-6f)
                        Nend = Vector3.Cross(endT2, Vector3.right);
                }
                Nend.Normalize();
                Vector3 Bend = Vector3.Cross(endT2, Nend);

                if (Mathf.Abs(s.twist) > 0.0001f)
                {
                    Quaternion twistQ = Quaternion.AngleAxis(s.twist, endT2);
                    Nend = twistQ * Nend;
                    Bend = twistQ * Bend;
                }

                // end ring using twisted end frame
                if (s.capEnd != TubeCapType.None && s.capEnd != TubeCapType.FullSphere)
                {
                    lastRing = v;
                    float r1ng = s.radiusProfile != null ? s.radiusProfile.Evaluate(1f) : 0.03f;
                    if (r1ng < 0.0001f) r1ng = 0.0001f;

                    for (int j = 0; j < ring; j++)
                    {
                        float ang = (j / (float)ring) * Mathf.PI * 2f;
                        float cs = Mathf.Cos(ang);
                        float sn = Mathf.Sin(ang);

                        Vector3 pos = curvePos[seg - 1] + (Nend * cs + Bend * sn) * r1ng;

                        verts[v] = pos;
                        uvs[v] = new Vector2(j / (float)ring, 1f);
                        colors[v] = endColor;
                        v++;
                    }
                }

                Vector3 startT2 = curveTan[0];
                Vector3 startN2 = startN;
                Vector3 startB2 = startB;

                // start cap only
                if (s.capStart == TubeCapType.Flat)
                {
                    int ci; BuildFlat(curvePos[0], startColor, ref v, out ci);
                    if (firstRing >= 0)
                        BuildFanRev(ci, firstRing, ring, ref t);
                }
                else if (s.capStart == TubeCapType.Point)
                {
                    int ci; BuildPoint(curvePos[0], -startT2, s, 0f, startColor, ref v, out ci);
                    if (firstRing >= 0)
                        BuildFanRev(ci, firstRing, ring, ref t);
                }
                else if (s.capStart == TubeCapType.Rounded)
                {
                    float r0 = s.radiusProfile != null ? s.radiusProfile.Evaluate(0f) : 0.03f;

                    if (firstRing >= 0)
                    {
                        BuildRoundedCapStart_UsingTubeRing(
                            verts,
                            firstRing,
                            curvePos[0],
                            r0,
                            ring,
                            latSeg,
                            -startT2,
                            startN2,
                            startB2,
                            startColor,
                            s.capScale,
                            s.bulgePower,
                            ref v,
                            ref t
                        );
                    }
                }
                else if (s.capStart == TubeCapType.FullSphere)
                {
                    float r0 = s.sphereRadius > 0f ? s.sphereRadius :
                        (s.radiusProfile != null ? s.radiusProfile.Evaluate(0f) : 0.03f);

                    BuildSphere(
                        curvePos[0],
                        r0,
                        ring,
                        startColor,
                        startT2,
                        startN2,
                        startB2,
                        0f,
                        ref v,
                        ref t
                    );
                }

                // end cap only, using twisted end frame
                if (s.capEnd == TubeCapType.Flat)
                {
                    int ci; BuildFlat(curvePos[seg - 1], endColor, ref v, out ci);
                    if (lastRing >= 0)
                        BuildFan(ci, lastRing, ring, ref t);
                }
                else if (s.capEnd == TubeCapType.Point)
                {
                    int ci; BuildPoint(curvePos[seg - 1], endT2, s, 1f, endColor, ref v, out ci);
                    if (lastRing >= 0)
                        BuildFan(ci, lastRing, ring, ref t);
                }
                else if (s.capEnd == TubeCapType.Rounded)
                {
                    float r1 = s.radiusProfile != null ? s.radiusProfile.Evaluate(1f) : 0.03f;

                    if (lastRing >= 0)
                    {
                        BuildRoundedCapEnd_UsingTubeRing(
                            verts,
                            lastRing,
                            curvePos[seg - 1],
                            r1,
                            ring,
                            latSeg,
                            endT2,
                            Nend,
                            Bend,
                            s.twist,
                            endColor,
                            s.capScale,
                            s.bulgePower,
                            ref v,
                            ref t
                        );
                    }
                }
                else if (s.capEnd == TubeCapType.FullSphere)
                {
                    float r1 = s.sphereRadius > 0f ? s.sphereRadius :
                        (s.radiusProfile != null ? s.radiusProfile.Evaluate(1f) : 0.03f);

                    BuildSphere(
                        curvePos[seg - 1],
                        r1,
                        ring,
                        endColor,
                        endT2,
                        Nend,
                        Bend,
                        s.twist,
                        ref v,
                        ref t
                    );
                }

                continue;
            }

            // caps when tube geometry exists
            Vector3 startTgen = curveTan[0];
            Vector3 endTgen = curveTan[seg - 1];

            Color startCol = startColor;
            Color endCol = endColor;

            // start cap
            if (s.capStart == TubeCapType.Flat)
            {
                int ci; BuildFlat(curvePos[0], startCol, ref v, out ci);

                if (firstRing >= 0)
                {
                    int dupStartRing = v;
                    for (int j = 0; j < ring; j++)
                    {
                        verts[v] = verts[firstRing + j];
                        uvs[v] = uvs[firstRing + j];
                        colors[v] = startCol;
                        v++;
                    }

                    BuildFanRev(ci, dupStartRing, ring, ref t);
                }
            }
            else if (s.capStart == TubeCapType.Point)
            {
                int ci; BuildPoint(curvePos[0], -startTgen, s, 0f, startCol, ref v, out ci);

                if (firstRing >= 0)
                {
                    int dupStartRing = v;
                    for (int j = 0; j < ring; j++)
                    {
                        verts[v] = verts[firstRing + j];
                        uvs[v] = uvs[firstRing + j];
                        colors[v] = startCol;
                        v++;
                    }

                    BuildFanRev(ci, dupStartRing, ring, ref t);
                }
            }
            else if (s.capStart == TubeCapType.Rounded)
            {
                float r0 = s.radiusProfile != null ? s.radiusProfile.Evaluate(0f) : 0.03f;

                if (firstRing >= 0)
                {
                    int dupStartRing = v;
                    for (int j = 0; j < ring; j++)
                    {
                        verts[v] = verts[firstRing + j];
                        uvs[v] = uvs[firstRing + j];
                        colors[v] = startCol;
                        v++;
                    }

                    BuildRoundedCapStart_UsingTubeRing(
                        verts,
                        dupStartRing,
                        curvePos[0],
                        r0,
                        ring,
                        latSeg,
                        -startTgen,
                        startN,
                        startB,
                        startCol,
                        s.capScale,
                        s.bulgePower,
                        ref v,
                        ref t
                    );
                }
            }
            else if (s.capStart == TubeCapType.FullSphere)
            {
                float r0 = s.sphereRadius > 0f ? s.sphereRadius :
                    (s.radiusProfile != null ? s.radiusProfile.Evaluate(0f) : 0.03f);

                BuildSphere(
                    curvePos[0],
                    r0,
                    ring,
                    startCol,
                    startTgen,
                    startN,
                    startB,
                    0f,
                    ref v,
                    ref t
                );
            }

            // end cap
            if (s.capEnd == TubeCapType.Flat)
            {
                int ci; BuildFlat(curvePos[seg - 1], endCol, ref v, out ci);

                if (lastRing >= 0)
                {
                    int dupEndRing = v;
                    for (int j = 0; j < ring; j++)
                    {
                        verts[v] = verts[lastRing + j];
                        uvs[v] = uvs[lastRing + j];
                        colors[v] = endCol;
                        v++;
                    }

                    BuildFan(ci, dupEndRing, ring, ref t);
                }
            }
            else if (s.capEnd == TubeCapType.Point)
            {
                int ci; BuildPoint(curvePos[seg - 1], endTgen, s, 1f, endCol, ref v, out ci);

                if (lastRing >= 0)
                {
                    int dupEndRing = v;
                    for (int j = 0; j < ring; j++)
                    {
                        verts[v] = verts[lastRing + j];
                        uvs[v] = uvs[lastRing + j];
                        colors[v] = endCol;
                        v++;
                    }

                    BuildFan(ci, dupEndRing, ring, ref t);
                }
            }
            else if (s.capEnd == TubeCapType.Rounded)
            {
                float r1 = s.radiusProfile != null ? s.radiusProfile.Evaluate(1f) : 0.03f;

                if (lastRing >= 0)
                {
                    int dupEndRing = v;
                    for (int j = 0; j < ring; j++)
                    {
                        verts[v] = verts[lastRing + j];
                        uvs[v] = uvs[lastRing + j];
                        colors[v] = endCol;
                        v++;
                    }

                    BuildRoundedCapEnd_UsingTubeRing(
                        verts,
                        dupEndRing,
                        curvePos[seg - 1],
                        r1,
                        ring,
                        latSeg,
                        endTgen,
                        Nprev,
                        Bprev,
                        s.twist,
                        endCol,
                        s.capScale,
                        s.bulgePower,
                        ref v,
                        ref t
                    );
                }
            }
            else if (s.capEnd == TubeCapType.FullSphere)
            {
                float r1 = s.sphereRadius > 0f ? s.sphereRadius :
                    (s.radiusProfile != null ? s.radiusProfile.Evaluate(1f) : 0.03f);

                BuildSphere(
                    curvePos[seg - 1],
                    r1,
                    ring,
                    endCol,
                    endTgen,
                    Nprev,
                    Bprev,
                    s.twist,
                    ref v,
                    ref t
                );
            }
        }

        mesh.Clear();
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.colors = colors;
        mesh.triangles = tris;
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 9999f);
        mesh.RecalculateNormals();
    }

    void BuildFlat(Vector3 c, Color col, ref int v, out int centerIndex)
    {
        centerIndex = v;
        verts[v] = c;
        uvs[v] = new Vector2(0.5f, 0.5f);
        colors[v] = col;
        v++;
    }

    void BuildPoint(Vector3 c, Vector3 dir, TubeSegment s, float tCurve, Color col, ref int v, out int tipIndex)
    {
        float rad = s.radiusProfile != null ? s.radiusProfile.Evaluate(tCurve) : 0.03f;
        if (rad < 0.0001f) rad = 0.0001f;

        float scale = s.capScale;
        if (Mathf.Approximately(scale, 0f)) scale = 1f;

        tipIndex = v;
        verts[v] = c + dir.normalized * (rad * scale);
        uvs[v] = new Vector2(0.5f, 1f);
        colors[v] = col;
        v++;
    }

    void BuildFan(int centerIndex, int ringStart, int ring, ref int t)
    {
        for (int j = 0; j < ring; j++)
        {
            int j1 = (j + 1) % ring;
            int a = ringStart + j;
            int b = ringStart + j1;
            tris[t++] = a; tris[t++] = b; tris[t++] = centerIndex;
        }
    }

    void BuildFanRev(int centerIndex, int ringStart, int ring, ref int t)
    {
        for (int j = 0; j < ring; j++)
        {
            int j1 = (j + 1) % ring;
            int a = ringStart + j;
            int b = ringStart + j1;
            tris[t++] = a; tris[t++] = centerIndex; tris[t++] = b;
        }
    }

    void BuildSphere(
        Vector3 c,
        float r,
        int ring,
        Color col,
        Vector3 axis,
        Vector3 frameN,
        Vector3 frameB,
        float twistDeg,
        ref int v,
        ref int t)
    {
        axis.Normalize();
        frameN.Normalize();
        frameB.Normalize();

        Quaternion twistQ = Quaternion.AngleAxis(twistDeg, axis);
        Vector3 Ntw = twistQ * frameN;
        Vector3 Btw = twistQ * frameB;

        int g = ring + 1;
        int baseV = v;

        for (int iy = 0; iy < g; iy++)
        {
            float ty = (float)iy / ring;
            float theta = (ty - 0.5f) * Mathf.PI;

            float cy = Mathf.Cos(theta);
            float sy = Mathf.Sin(theta);

            for (int ix = 0; ix < g; ix++)
            {
                float tx = (float)ix / ring;
                float phi = tx * Mathf.PI * 2f;

                float cp = Mathf.Cos(phi);
                float sp = Mathf.Sin(phi);

                Vector3 nLocal = new Vector3(cp * cy, sy, sp * cy);

                Vector3 n =
                    Ntw * nLocal.x +
                    axis * nLocal.y +
                    Btw * nLocal.z;

                verts[v] = c + n * r;
                uvs[v] = new Vector2(tx, ty);
                colors[v] = col;
                v++;
            }
        }

        for (int iy = 0; iy < ring; iy++)
        {
            int rowA = baseV + iy * g;
            int rowB = baseV + (iy + 1) * g;

            for (int ix = 0; ix < ring; ix++)
            {
                int a = rowA + ix;
                int b = rowA + ix + 1;
                int c0 = rowB + ix;
                int d = rowB + ix + 1;

                tris[t++] = a; tris[t++] = b; tris[t++] = c0;
                tris[t++] = b; tris[t++] = d; tris[t++] = c0;
            }
        }
    }

    void BuildRoundedCapStart_UsingTubeRing(
        Vector3[] vertsArray,
        int ringStart,
        Vector3 c,
        float r,
        int ring,
        int latSeg,
        Vector3 axis,
        Vector3 frameN,
        Vector3 frameB,
        Color col,
        float capScale,
        float bulgePower,
        ref int v,
        ref int t)
    {
        axis.Normalize();
        if (axis.sqrMagnitude < 1e-6f)
            axis = Vector3.up;

        Vector3 U = frameN;
        Vector3 V = frameB;
        Vector3 W = axis;

        if (Mathf.Approximately(capScale, 0f))
            capScale = 1f;

        int capBase = v;

        for (int lat = 1; lat < latSeg; lat++)
        {
            float tLat = (float)lat / latSeg;
            float theta = tLat * (Mathf.PI * 0.5f);

            float cosT = Mathf.Cos(theta);
            float sinT = Mathf.Sin(theta);

            float radial = r * Mathf.Pow(cosT, bulgePower);
            float height = r * sinT * capScale;

            for (int j = 0; j < ring; j++)
            {
                float tLon = (float)j / ring;
                float phi = tLon * Mathf.PI * 2f;

                float cp = Mathf.Cos(phi);
                float sp = Mathf.Sin(phi);

                Vector3 dir = U * cp + V * sp;

                vertsArray[v] = c + dir * radial + W * height;
                uvs[v] = new Vector2(tLon, tLat);
                colors[v] = col;
                v++;
            }
        }

        int firstDomeRing = capBase;

        for (int j = 0; j < ring; j++)
        {
            int j1 = (j + 1) % ring;

            int eq0 = ringStart + j;
            int eq1 = ringStart + j1;

            int c0 = firstDomeRing + j;
            int c1 = firstDomeRing + j1;

            tris[t++] = eq0; tris[t++] = c0; tris[t++] = c1;
            tris[t++] = eq0; tris[t++] = c1; tris[t++] = eq1;
        }

        for (int lat = 0; lat < latSeg - 2; lat++)
        {
            int rowA = capBase + lat * ring;
            int rowB = rowA + ring;

            for (int j = 0; j < ring; j++)
            {
                int j1 = (j + 1) % ring;

                int a = rowA + j;
                int b = rowA + j1;
                int c0 = rowB + j;
                int d = rowB + j1;

                tris[t++] = a; tris[t++] = c0; tris[t++] = d;
                tris[t++] = a; tris[t++] = d; tris[t++] = b;
            }
        }

        int poleIndex = v;
        vertsArray[v] = c + W * (r * capScale);
        uvs[v] = new Vector2(0.5f, 1f);
        colors[v] = col;
        v++;

        int lastRingStart = capBase + (latSeg - 2) * ring;

        for (int j = 0; j < ring; j++)
        {
            int j1 = (j + 1) % ring;

            int a = lastRingStart + j;
            int b = lastRingStart + j1;

            tris[t++] = a; tris[t++] = poleIndex; tris[t++] = b;
        }
    }

    void BuildRoundedCapEnd_UsingTubeRing(
        Vector3[] vertsArray,
        int ringStart,
        Vector3 c,
        float r,
        int ring,
        int latSeg,
        Vector3 axis,
        Vector3 frameN,
        Vector3 frameB,
        float twistDeg,
        Color col,
        float capScale,
        float bulgePower,
        ref int v,
        ref int t)
    {
        axis.Normalize();
        if (axis.sqrMagnitude < 1e-6f)
            axis = Vector3.up;

        Quaternion twistQ = Quaternion.AngleAxis(twistDeg, axis);
        Vector3 U = twistQ * frameN;
        Vector3 V = twistQ * frameB;
        Vector3 W = axis;

        if (Mathf.Approximately(capScale, 0f))
            capScale = 1f;

        int capBase = v;

        for (int lat = 1; lat < latSeg; lat++)
        {
            float tLat = (float)lat / latSeg;
            float theta = tLat * (Mathf.PI * 0.5f);

            float cosT = Mathf.Cos(theta);
            float sinT = Mathf.Sin(theta);

            float radial = r * Mathf.Pow(cosT, bulgePower);
            float height = r * sinT * capScale;

            for (int j = 0; j < ring; j++)
            {
                float tLon = (float)j / ring;
                float phi = tLon * Mathf.PI * 2f;

                float cp = Mathf.Cos(phi);
                float sp = Mathf.Sin(phi);

                Vector3 dir = U * cp + V * sp;

                vertsArray[v] = c + dir * radial + W * height;
                uvs[v] = new Vector2(tLon, tLat);
                colors[v] = col;
                v++;
            }
        }

        int firstDomeRing = capBase;

        for (int j = 0; j < ring; j++)
        {
            int j1 = (j + 1) % ring;

            int eq0 = ringStart + j;
            int eq1 = ringStart + j1;

            int c0 = firstDomeRing + j;
            int c1 = firstDomeRing + j1;

            tris[t++] = eq0; tris[t++] = c1; tris[t++] = c0;
            tris[t++] = eq0; tris[t++] = eq1; tris[t++] = c1;
        }

        for (int lat = 0; lat < latSeg - 2; lat++)
        {
            int rowA = capBase + lat * ring;
            int rowB = rowA + ring;

            for (int j = 0; j < ring; j++)
            {
                int j1 = (j + 1) % ring;

                int a = rowA + j;
                int b = rowA + j1;
                int c0 = rowB + j;
                int d = rowB + j1;

                tris[t++] = a; tris[t++] = d; tris[t++] = c0;
                tris[t++] = a; tris[t++] = b; tris[t++] = d;
            }
        }

        int poleIndex = v;
        vertsArray[v] = c + W * (r * capScale);
        uvs[v] = new Vector2(0.5f, 1f);
        colors[v] = col;
        v++;

        int lastRingStart = capBase + (latSeg - 2) * ring;

        for (int j = 0; j < ring; j++)
        {
            int j1 = (j + 1) % ring;

            int a = lastRingStart + j;
            int b = lastRingStart + j1;

            tris[t++] = a; tris[t++] = b; tris[t++] = poleIndex;
        }
    }

    void EnsureMesh()
    {
        MeshFilter mf = GetComponent<MeshFilter>();
        if (mf.sharedMesh == null)
        {
            Mesh m = new Mesh();
            m.name = "TubeBuilderRuntimeMesh";
            mf.sharedMesh = m;
        }

        mesh = mf.sharedMesh;
    }
}
