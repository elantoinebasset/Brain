# Intro (Linda) — écran titre, règles, choix de dose, voix off, transition

Tout est dans ce dossier : la scène du cerveau (`Assets/Scenes/SampleScene.unity`) n'est pas modifiée.

## Créer la scène
Menu **Tools > Intro VR > Créer la scène Intro**
→ crée `Assets/Intro/Scenes/Intro.unity` et la met en 1re position dans la liste des scènes du build (avant SampleScene).

## Déroulé
1. Écran titre « Entrez dans la simulation » → bouton **Entrer**
2. Voix off d'introduction (le cerveau, le THC) — sous-titres phrase par phrase
3. Règles → bouton **J'ai compris**
4. Choix de la quantité : **49 mg (1 joint)** ou **490 mg (10 joints)**
5. « Plongez au cœur de notre centre de contrôle… » → particules qui accélèrent + fondu au noir → SampleScene

Le choix est appliqué automatiquement dans la scène du cerveau : le bloc « Consommer » donne +49 mg ou +490 mg par prise
(`SimulationSettings` règle `CannabisState.doseStepMg` au chargement).

## Test sans casque (vue Game)
- **Entrée** : bouton du panneau affiché
- **1 / 2** : choix 49 mg / 490 mg
- **Espace** : phrase suivante

## Voix off
Sélectionner l'objet **Intro (deroule)** → composant **IntroFlow** → listes *Intro Lines* et *Transition Lines* :
glisser un clip audio dans **Voice** pour chaque phrase. Sans clip, la durée est calculée d'après le nombre de mots.
Le champ **Cue** envoie un événement (`IntroSequencer.Cue`) que d'autres scripts peuvent écouter.

## Fichiers
- `Scripts/IntroFlow.cs` — déroulé de la scène (panneaux, choix, transition)
- `Scripts/IntroSequencer.cs` — séquenceur voix off + sous-titres + événements
- `Scripts/SimulationSettings.cs` — garde le choix de dose et l'applique dans la scène du cerveau
- `Editor/IntroSceneBuilder.cs` — construit la scène (menu Tools > Intro VR)
