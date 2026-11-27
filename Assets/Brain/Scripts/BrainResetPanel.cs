using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BrainVR
{
    /// <summary>
    /// Panneau VR : remet le cerveau dans son état de départ (stable) en rechargeant la scène.
    /// 1er clic = « Confirmer ? », 2e clic dans le délai = réinitialisation.
    /// </summary>
    public class BrainResetPanel : MonoBehaviour
    {
        public Button resetButton;
        public TMP_Text buttonLabel;
        public string idleLabel = "Réinitialiser";
        public string confirmLabel = "Confirmer ?";
        [Tooltip("Temps laissé pour confirmer (s)")]
        public float confirmDelay = 3f;

        float confirmUntil = -1f;
        bool loading;

        void Awake()
        {
            if (resetButton) resetButton.onClick.AddListener(OnClick);
            var canvas = GetComponent<Canvas>();
            if (canvas && canvas.worldCamera == null) canvas.worldCamera = Camera.main;
            SetLabel(idleLabel);
        }

        void Update()
        {
            if (confirmUntil > 0f && Time.unscaledTime > confirmUntil)
            {
                confirmUntil = -1f;
                SetLabel(idleLabel);
            }
        }

        void OnClick()
        {
            if (loading) return;
            if (confirmUntil > 0f)
            {
                ResetBrain();
                return;
            }
            confirmUntil = Time.unscaledTime + confirmDelay;
            SetLabel(confirmLabel);
        }

        [ContextMenu("Réinitialiser le cerveau")]
        public void ResetBrain()
        {
            if (loading) return;
            loading = true;
            SetLabel("…");
            Time.timeScale = 1f;
            var scene = gameObject.scene;
            if (scene.buildIndex >= 0) SceneManager.LoadSceneAsync(scene.buildIndex);
            else SceneManager.LoadSceneAsync(scene.name);
        }

        void SetLabel(string s)
        {
            if (buttonLabel) buttonLabel.text = s;
        }
    }
}
