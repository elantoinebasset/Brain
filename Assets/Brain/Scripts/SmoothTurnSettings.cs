using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

namespace BrainVR
{

    public class SmoothTurnSettings : MonoBehaviour
    {
        public bool smoothTurn = true;
        public float turnSpeed = 60f;

        void Start() => Apply();

        void OnValidate()
        {
            if (Application.isPlaying) Apply();
        }

        public void Apply()
        {
            foreach (var m in FindObjectsByType<ControllerInputActionManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                m.smoothTurnEnabled = smoothTurn;
            foreach (var t in FindObjectsByType<ContinuousTurnProvider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                t.turnSpeed = turnSpeed;
        }
    }
}
