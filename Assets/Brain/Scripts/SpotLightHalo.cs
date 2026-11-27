using UnityEngine;
using UnityEngine.Rendering;

namespace BrainVR
{
    /// <summary>
    /// Rend visible le faisceau d'une Spot Light : un cône lumineux doux (additif) qui suit
    /// l'angle, la portée et la couleur de la lumière. URP n'affiche pas le faisceau dans l'air tout seul.
    /// </summary>
    [RequireComponent(typeof(Light))]
    [ExecuteAlways]
    public class SpotLightHalo : MonoBehaviour
    {
        [Tooltip("Matériau additif (ex. Trainee_impulsion ou Halo_THC). Utilise la couleur des sommets.")]
        public Material haloMaterial;
        [Tooltip("Intensité du cône (0 = invisible)")]
        [Range(0f, 1f)] public float opacity = 0.25f;
        [Tooltip("Longueur du cône par rapport à la portée de la lumière")]
        [Range(0.1f, 1f)] public float lengthFactor = 0.8f;
        [Tooltip("Plus haut = cône plus doux sur les bords")]
        [Range(0f, 1f)] public float edgeSoftness = 0.6f;
        public int segments = 32;
        [Tooltip("Légère pulsation de l'intensité")]
        public float flicker = 0.05f;

        Light lightComp;
        MeshFilter mf;
        MeshRenderer mr;
        Transform cone;
        float lastAngle, lastRange, lastSoft, lastLength;
        Color lastColor;
        float lastOpacity = -1f;

#if UNITY_EDITOR
        void Reset()
        {
            // Matériau additif du projet par défaut
            haloMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Brain/Materials/Trainee_impulsion.mat");
            var l = GetComponent<Light>();
            if (l != null && l.type == LightType.Spot && l.intensity < 3f) l.intensity = 6f;
        }
#endif

        void OnEnable()
        {
            lightComp = GetComponent<Light>();
            if (cone == null)
            {
                var existing = transform.Find("Halo (cone)");
                if (existing != null) cone = existing;
                else
                {
                    var go = new GameObject("Halo (cone)");
                    go.hideFlags = HideFlags.DontSave;
                    cone = go.transform;
                    cone.SetParent(transform, false);
                }
                mf = cone.GetComponent<MeshFilter>();
                if (mf == null) mf = cone.gameObject.AddComponent<MeshFilter>();
                mr = cone.GetComponent<MeshRenderer>();
                if (mr == null) mr = cone.gameObject.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            lastOpacity = -1f;
        }

        void OnDisable()
        {
            if (cone != null) DestroyImmediate(cone.gameObject);
            cone = null;
        }

        void LateUpdate()
        {
            if (lightComp == null || cone == null) return;
            bool visible = lightComp.enabled && lightComp.type == LightType.Spot && haloMaterial != null && opacity > 0f;
            mr.enabled = visible;
            if (!visible) return;
            mr.sharedMaterial = haloMaterial;
            cone.localPosition = Vector3.zero;
            cone.localRotation = Quaternion.identity;
            cone.localScale = Vector3.one;

            float f = 1f + (Application.isPlaying ? flicker * (Mathf.PerlinNoise(Time.time * 2f, 0.3f) * 2f - 1f) : 0f);
            float op = opacity * f;
            if (!Mathf.Approximately(lastAngle, lightComp.spotAngle) || !Mathf.Approximately(lastRange, lightComp.range)
                || lastColor != lightComp.color || !Mathf.Approximately(lastOpacity, op)
                || !Mathf.Approximately(lastSoft, edgeSoftness) || !Mathf.Approximately(lastLength, lengthFactor))
            {
                Build(op);
                lastAngle = lightComp.spotAngle;
                lastRange = lightComp.range;
                lastColor = lightComp.color;
                lastOpacity = op;
                lastSoft = edgeSoftness;
                lastLength = lengthFactor;
            }
        }

        void Build(float op)
        {
            int n = Mathf.Max(8, segments);
            float length = lightComp.range * lengthFactor;
            float radius = Mathf.Tan(lightComp.spotAngle * 0.5f * Mathf.Deg2Rad) * length;
            // 3 anneaux : sommet, milieu, base. Le cœur est plus lumineux que le bord (douceur).
            var verts = new Vector3[1 + n * 2];
            var cols = new Color[verts.Length];
            Color c = lightComp.color;
            verts[0] = Vector3.zero;
            cols[0] = new Color(c.r, c.g, c.b, op);
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                verts[1 + i] = dir * radius * 0.5f + Vector3.forward * length * 0.5f;
                verts[1 + n + i] = dir * radius + Vector3.forward * length;
                cols[1 + i] = new Color(c.r, c.g, c.b, op * Mathf.Lerp(1f, 0.35f, edgeSoftness));
                cols[1 + n + i] = new Color(c.r, c.g, c.b, 0f);
            }
            var tris = new System.Collections.Generic.List<int>();
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                int m0 = 1 + i, m1 = 1 + j, b0 = 1 + n + i, b1 = 1 + n + j;
                // Faces dans les deux sens : visible de l'extérieur comme de l'intérieur du cône
                tris.AddRange(new[] { 0, m0, m1, 0, m1, m0 });
                tris.AddRange(new[] { m0, b0, b1, m0, b1, m1, m0, b1, b0, m0, m1, b1 });
            }
            var mesh = mf.sharedMesh;
            if (mesh == null)
            {
                mesh = new Mesh { name = "Halo spot", hideFlags = HideFlags.DontSave };
                mf.sharedMesh = mesh;
            }
            mesh.Clear();
            mesh.vertices = verts;
            mesh.colors = cols;
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
        }
    }
}
