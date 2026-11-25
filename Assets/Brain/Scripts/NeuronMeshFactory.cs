using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BrainVR
{
    /// <summary>Construction procédurale des neurones et des axones (utilisé en éditeur ET en jeu).</summary>
    public static class NeuronMeshFactory
    {
        public const float AxonRadius = 0.022f;

        public static void BuildNeuron(BrainMeshBuilder b, float r)
        {
            var scale = new Vector3(1f, Random.Range(0.8f, 1.1f), Random.Range(0.9f, 1.35f));
            b.AddSphere(Vector3.zero, r, Quaternion.identity, scale, 3, 0.12f, Random.insideUnitSphere * 10f, 0);
            int count = Random.Range(5, 9);
            for (int k = 0; k < count; k++)
            {
                var dir = Random.onUnitSphere;
                Branch(b, dir * r * 0.8f, dir, Random.Range(0.9f, 2f), r * 0.32f, 0);
            }
        }

        static void Branch(BrainMeshBuilder b, Vector3 start, Vector3 dir, float length, float r0, int depth)
        {
            int segs = Mathf.Max(4, Mathf.RoundToInt(length * 7f));
            var pts = new List<Vector3>(segs + 1);
            var radii = new List<float>(segs + 1);
            var dirs = new List<Vector3>(segs + 1);
            Vector3 p = start, d = dir;
            float step = length / segs;
            for (int i = 0; i <= segs; i++)
            {
                pts.Add(p);
                radii.Add(Mathf.Lerp(r0, 0.004f, Mathf.Pow(i / (float)segs, 0.8f)));
                dirs.Add(d);
                d = (d + Random.insideUnitSphere * 0.45f).normalized;
                p += d * step;
            }
            b.AddTube(pts, radii, depth == 0 ? 6 : 5, 0);

            if (depth >= 2) return;
            int kids = depth == 0 ? Random.Range(1, 3) : Random.Range(0, 3);
            for (int k = 0; k < kids; k++)
            {
                int at = Random.Range(segs / 3, segs * 3 / 4 + 1);
                var nd = (dirs[at] + Random.insideUnitSphere * 1.1f).normalized;
                Branch(b, pts[at], nd, length * Random.Range(0.45f, 0.7f), radii[at] * 0.85f, depth + 1);
            }
        }

        /// <summary>Courbe de Bézier légèrement aléatoire entre deux neurones.</summary>
        public static Vector3[] AxonPath(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            float len = Mathf.Max(0.01f, d.magnitude);
            Vector3 dir = d / len;
            Vector3 perp1 = Vector3.Cross(dir, Random.onUnitSphere).normalized;
            Vector3 perp2 = Vector3.Cross(dir, Random.onUnitSphere).normalized;
            Vector3 p1 = a + d * 0.33f + perp1 * len * Random.Range(0.1f, 0.35f);
            Vector3 p2 = a + d * 0.66f + perp2 * len * Random.Range(0.1f, 0.35f);
            int n = Mathf.Clamp(Mathf.CeilToInt(len * 6f), 12, 60);
            var pts = new Vector3[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n, it = 1f - t;
                pts[i] = it * it * it * a + 3f * it * it * t * p1 + 3f * it * t * t * p2 + t * t * t * b;
            }
            return pts;
        }

        public static void BuildAxon(BrainMeshBuilder b, Vector3[] pts, float endSomaRadius)
        {
            var radii = new float[pts.Length];
            for (int i = 0; i < radii.Length; i++) radii[i] = AxonRadius;
            b.AddTube(pts, radii, 6, 0);

            var cum = Cumulative(pts);
            float total = cum[cum.Length - 1];

            // Gaines de myéline (avec nœuds de Ranvier entre elles)
            for (float s = 0.45f; s < total - 0.7f; s += 0.42f)
            {
                Vector3 c = SampleAt(pts, cum, s + 0.14f);
                Vector3 t = SampleAt(pts, cum, s + 0.22f) - SampleAt(pts, cum, s + 0.06f);
                if (t.sqrMagnitude < 1e-8f) continue;
                b.AddSphere(c, 1f, Quaternion.LookRotation(t.normalized), new Vector3(0.05f, 0.05f, 0.14f), 1, 0f, Vector3.zero, 1);
            }

            // Bouton synaptique à l'arrivée
            b.AddSphere(SampleAt(pts, cum, total - endSomaRadius - 0.05f), 0.065f, Quaternion.identity, Vector3.one, 2, 0.1f,
                        Random.insideUnitSphere * 10f, 0);
        }

        public static float[] Cumulative(Vector3[] pts)
        {
            var cum = new float[pts.Length];
            for (int i = 1; i < pts.Length; i++) cum[i] = cum[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);
            return cum;
        }

        public static Vector3 SampleAt(Vector3[] pts, float[] cum, float d)
        {
            if (d <= 0f) return pts[0];
            for (int i = 1; i < pts.Length; i++)
            {
                if (cum[i] >= d)
                {
                    float t = (d - cum[i - 1]) / Mathf.Max(1e-5f, cum[i] - cum[i - 1]);
                    return Vector3.Lerp(pts[i - 1], pts[i], t);
                }
            }
            return pts[pts.Length - 1];
        }

        public static void ConfigureRenderer(Renderer r)
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
    }
}
