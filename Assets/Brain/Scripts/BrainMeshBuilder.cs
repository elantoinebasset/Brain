using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BrainVR
{
    /// <summary>Petit constructeur de mesh procédural (tubes, sphères bruitées, meshes bruts).</summary>
    public class BrainMeshBuilder
    {
        public readonly List<Vector3> v = new List<Vector3>();
        public readonly List<Vector3> n = new List<Vector3>();
        public readonly List<int>[] subs;

        public BrainMeshBuilder(int subMeshCount)
        {
            subs = new List<int>[subMeshCount];
            for (int i = 0; i < subMeshCount; i++) subs[i] = new List<int>();
        }

        public void AddTube(IList<Vector3> pts, IList<float> radii, int sides, int sub)
        {
            if (pts.Count < 2) return;
            int start = v.Count;
            Vector3 prevT = (pts[1] - pts[0]).normalized;
            Vector3 normal = Vector3.Cross(prevT, Mathf.Abs(prevT.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            int last = pts.Count - 1;

            for (int i = 0; i <= last; i++)
            {
                Vector3 t = i == 0 ? pts[1] - pts[0] : i == last ? pts[i] - pts[i - 1] : pts[i + 1] - pts[i - 1];
                t = t.sqrMagnitude > 1e-10f ? t.normalized : prevT;
                if (i > 0) normal = (Quaternion.FromToRotation(prevT, t) * normal).normalized;
                Vector3 binormal = Vector3.Cross(t, normal);
                for (int s = 0; s < sides; s++)
                {
                    float ang = s * Mathf.PI * 2f / sides;
                    Vector3 dir = normal * Mathf.Cos(ang) + binormal * Mathf.Sin(ang);
                    v.Add(pts[i] + dir * radii[i]);
                    n.Add(dir);
                }
                prevT = t;
            }

            var tris = subs[sub];
            for (int i = 0; i < last; i++)
            {
                for (int s = 0; s < sides; s++)
                {
                    int a = start + i * sides + s;
                    int b = start + i * sides + (s + 1) % sides;
                    int c = a + sides;
                    int d = b + sides;
                    tris.Add(a); tris.Add(b); tris.Add(c);
                    tris.Add(b); tris.Add(d); tris.Add(c);
                }
            }
        }

        public void AddSphere(Vector3 center, float radius, Quaternion rot, Vector3 scale, int level,
                              float noiseAmp, Vector3 noiseOffset, int sub)
        {
            var ico = Icosphere(level);
            int start = v.Count;
            var tris = subs[sub];
            int tStart = tris.Count;
            foreach (var u in ico.verts)
            {
                float r = radius * (1f + noiseAmp * (BrainNoise.Sample(u * 2.5f + noiseOffset) * 2f - 1f));
                v.Add(center + rot * Vector3.Scale(u * r, scale));
                n.Add(Vector3.zero);
            }
            foreach (int t in ico.tris) tris.Add(start + t);
            SmoothNormals(start, sub, tStart);
        }

        public void AddRaw(IList<Vector3> verts, IList<int> triangles, bool flip, int sub)
        {
            int start = v.Count;
            var tris = subs[sub];
            int tStart = tris.Count;
            foreach (var p in verts) { v.Add(p); n.Add(Vector3.zero); }
            for (int i = 0; i < triangles.Count; i += 3)
            {
                tris.Add(start + triangles[i]);
                if (flip) { tris.Add(start + triangles[i + 2]); tris.Add(start + triangles[i + 1]); }
                else { tris.Add(start + triangles[i + 1]); tris.Add(start + triangles[i + 2]); }
            }
            SmoothNormals(start, sub, tStart);
        }

        void SmoothNormals(int vStart, int sub, int tStart)
        {
            var tris = subs[sub];
            for (int i = tStart; i < tris.Count; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                Vector3 fn = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
                n[a] += fn; n[b] += fn; n[c] += fn;
            }
            for (int i = vStart; i < v.Count; i++) n[i] = n[i].normalized;
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            Fill(m);
            m.UploadMeshData(false);
            return m;
        }

        /// <summary>Remplit (ou remplace) le contenu d'un mesh existant.</summary>
        public void Fill(Mesh m)
        {
            m.Clear();
            m.indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            m.SetVertices(v);
            m.SetNormals(n);
            m.subMeshCount = subs.Length;
            for (int i = 0; i < subs.Length; i++) m.SetTriangles(subs[i], i, false);
            m.RecalculateBounds();
        }

        // ---------- Icosphère ----------
        public class Ico
        {
            public List<Vector3> verts;
            public List<int> tris;
        }

        static readonly Dictionary<int, Ico> icoCache = new Dictionary<int, Ico>();

        public static Ico Icosphere(int level)
        {
            if (icoCache.TryGetValue(level, out var cached)) return cached;
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
            };
            for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;
            var f = new List<int>
            {
                0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1
            };
            for (int l = 0; l < level; l++)
            {
                var cache = new Dictionary<long, int>();
                var nf = new List<int>(f.Count * 4);
                for (int i = 0; i < f.Count; i += 3)
                {
                    int a = f[i], b = f[i + 1], c = f[i + 2];
                    int ab = Mid(a, b, v, cache), bc = Mid(b, c, v, cache), ca = Mid(c, a, v, cache);
                    nf.Add(a); nf.Add(ab); nf.Add(ca);
                    nf.Add(b); nf.Add(bc); nf.Add(ab);
                    nf.Add(c); nf.Add(ca); nf.Add(bc);
                    nf.Add(ab); nf.Add(bc); nf.Add(ca);
                }
                f = nf;
            }
            // Orientation : toutes les faces vers l'extérieur
            for (int i = 0; i < f.Count; i += 3)
            {
                Vector3 p0 = v[f[i]], p1 = v[f[i + 1]], p2 = v[f[i + 2]];
                if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), p0 + p1 + p2) < 0f)
                {
                    int tmp = f[i + 1]; f[i + 1] = f[i + 2]; f[i + 2] = tmp;
                }
            }
            var ico = new Ico { verts = v, tris = f };
            icoCache[level] = ico;
            return ico;
        }

        static int Mid(int a, int b, List<Vector3> v, Dictionary<long, int> cache)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (cache.TryGetValue(key, out int idx)) return idx;
            v.Add(((v[a] + v[b]) * 0.5f).normalized);
            idx = v.Count - 1;
            cache[key] = idx;
            return idx;
        }
    }

    public static class BrainNoise
    {
        /// <summary>Bruit 3D approximatif (0..1) à partir de Perlin 2D.</summary>
        public static float Sample(Vector3 p)
        {
            float xy = Mathf.PerlinNoise(p.x + 31.7f, p.y + 11.3f);
            float yz = Mathf.PerlinNoise(p.y + 7.1f, p.z + 53.9f);
            float zx = Mathf.PerlinNoise(p.z + 91.3f, p.x + 3.7f);
            float s = (xy + yz + zx) / 3f;
            return Mathf.Clamp01((s - 0.5f) * 2.2f + 0.5f);
        }

        /// <summary>Bruit "en crêtes" : 1 sur les plis, 0 dans les creux (effet circonvolutions).</summary>
        public static float Ridged(Vector3 p)
        {
            float r1 = 1f - Mathf.Abs(Sample(p) * 2f - 1f);
            float r2 = 1f - Mathf.Abs(Sample(p * 2.1f + new Vector3(17f, 3f, 29f)) * 2f - 1f);
            return Mathf.Clamp01(r1 * 0.7f + r2 * 0.3f);
        }
    }
}
