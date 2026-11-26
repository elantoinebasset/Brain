using UnityEditor;
using UnityEngine;

namespace BrainVR.EditorTools
{
    [CustomEditor(typeof(TextSequence))]
    public class TextSequenceEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "Chaque groupe apparaît mot par mot (« Word Interval » secondes entre chaque mot), " +
                "reste affiché « Delay After » secondes, puis le groupe suivant commence.\n" +
                "Gras : **mot** ou <b>mot</b>.\n" +
                "Bouton A (ou Espace) : texte suivant. Bouton B (ou touche B) : cacher / afficher le panneau.",
                MessageType.Info);
            DrawDefaultInspector();

            var seq = (TextSequence)target;
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Relancer la séquence")) seq.Restart();
                if (GUILayout.Button("Texte suivant (A)")) seq.Skip();
                if (GUILayout.Button("Cacher / afficher (B)")) seq.TogglePanel();
                EditorGUILayout.EndHorizontal();
            }
            if (!Application.isPlaying)
                EditorGUILayout.HelpBox("Les boutons de test marchent en Play mode.", MessageType.None);
        }
    }
}
