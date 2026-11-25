using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BrainVR
{
    /// <summary>Panneau VR : un bouton qui fait apparaître une orbe à transformer en neurone.</summary>
    public class NeuronSpawnerPanel : MonoBehaviour
    {
        public BrainEnvironment env;
        public Button spawnButton;
        public TMP_Text counterText;
        public Material orbMaterial;
        public float orbRadius = 0.09f;
        [Tooltip("Position d'apparition de l'orbe, relative au panneau (en mètres) : y = hauteur, z négatif = vers le joueur")]
        public Vector3 spawnOffset = new Vector3(0f, -0.25f, -0.35f);

        int created;
        NeuronOrb lastOrb;

        /// <summary>Test sans casque : transforme la dernière orbe en neurone, 2 m devant le panneau.</summary>
        [ContextMenu("Test : placer la dernière orbe")]
        void TestPlaceLastOrb()
        {
            if (lastOrb == null) return;
            lastOrb.transform.position = transform.position + transform.forward * 2.5f + Vector3.up * 0.8f;
            lastOrb.ForceTransform();
        }

        void Awake()
        {
            if (spawnButton) spawnButton.onClick.AddListener(SpawnOrb);
            var canvas = GetComponent<Canvas>();
            if (canvas && canvas.worldCamera == null) canvas.worldCamera = Camera.main;
            UpdateCounter();
        }

        [ContextMenu("Créer une orbe")]
        public void SpawnOrb()
        {
            if (env == null) env = FindFirstObjectByType<BrainEnvironment>();
            if (env == null) return;
            Vector3 jitter = new Vector3(Random.Range(-0.08f, 0.08f), Random.Range(-0.04f, 0.04f), 0f);
            Vector3 pos = transform.position + transform.rotation * (spawnOffset + jitter);
            lastOrb = NeuronOrb.Create(env, this, pos, orbRadius, orbMaterial);
        }

        public void OnNeuronCreated()
        {
            created++;
            UpdateCounter();
        }

        void UpdateCounter()
        {
            if (counterText) counterText.text = "Neurones créés : " + created;
        }
    }
}
