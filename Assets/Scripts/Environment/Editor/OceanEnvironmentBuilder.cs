using UnityEditor;
using UnityEngine;

namespace Rhinotap.EditorScripts
{
    public static class OceanEnvironmentBuilder
    {
        [MenuItem("Tools/Build Ocean Environment")]
        public static string BuildEnvironment()
        {
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;

            var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (activeScene.name != "SampleScene")
            {
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            }

            var oldEnv = GameObject.Find("OceanEnvironment");
            if (oldEnv != null)
            {
                Undo.DestroyObjectImmediate(oldEnv);
            }

            Sprite sBoulder = LoadSprite("Assets/Graphics/Backgrounds/boulder_small.png");
            Sprite sMound = LoadSprite("Assets/Graphics/Backgrounds/coral_mound_a.png");
            Sprite sRubble = LoadSprite("Assets/Graphics/Backgrounds/coral_rubble_b.png");
            Sprite sReefWall = LoadSprite("Assets/Graphics/Backgrounds/rock_coral_right.png");
            Sprite sKelp = LoadSprite("Assets/Graphics/Backgrounds/kelp_tall.png");
            Sprite sGrass = LoadSprite("Assets/Graphics/Backgrounds/seagrass_tuft.png");

            if (sBoulder == null || sMound == null || sRubble == null || sReefWall == null || sKelp == null || sGrass == null)
            {
                return "Error: Could not load all required environment sprites.";
            }

            var root = new GameObject("OceanEnvironment");
            Undo.RegisterCreatedObjectUndo(root, "Create OceanEnvironment");
            root.transform.position = Vector3.zero;
            var envComp = root.AddComponent<Rhinotap.UnderwaterEnvironment>();

            // --- LAYER 1: DISTANT ---
            var l1 = new GameObject("Layer1_Distant");
            l1.transform.SetParent(root.transform, false);
            CreateElement("Distant_ReefLeft", l1.transform, sReefWall, new Vector3(-18.5f, -9.5f, 3.0f), new Vector3(0.55f, 0.55f, 1f), 0f, true, "ParallaxBackground", 10, new Color(43f/255f, 85f/255f, 115f/255f, 0.70f));
            CreateElement("Distant_CoralMound", l1.transform, sMound, new Vector3(4.5f, -11.8f, 3.0f), new Vector3(0.60f, 0.50f, 1f), 0f, false, "ParallaxBackground", 10, new Color(47f/255f, 92f/255f, 124f/255f, 0.75f));
            CreateElement("Distant_RubbleRight", l1.transform, sRubble, new Vector3(19.0f, -10.5f, 3.0f), new Vector3(0.50f, 0.50f, 1f), 0f, false, "ParallaxBackground", 10, new Color(43f/255f, 85f/255f, 115f/255f, 0.70f));

            // --- LAYER 2: MIDGROUND ---
            var l2 = new GameObject("Layer2_Midground");
            l2.transform.SetParent(root.transform, false);
            CreateElement("Mid_SeafloorRidgeLeft", l2.transform, sMound, new Vector3(-14.0f, -11.8f, 1.0f), new Vector3(1.10f, 0.95f, 1f), 0f, false, "Default", -20, new Color(108f/255f, 148f/255f, 168f/255f, 1.0f));
            CreateElement("Mid_RubbleCenterLeft", l2.transform, sRubble, new Vector3(-7.5f, -12.4f, 1.0f), new Vector3(0.85f, 0.80f, 1f), -4f, false, "Default", -20, new Color(122f/255f, 158f/255f, 168f/255f, 1.0f));
            CreateElement("Mid_BoulderRight", l2.transform, sBoulder, new Vector3(11.0f, -12.6f, 1.0f), new Vector3(0.90f, 0.90f, 1f), 3f, false, "Default", -20, new Color(138f/255f, 170f/255f, 184f/255f, 1.0f));
            CreateElement("Mid_ReefWallRight", l2.transform, sReefWall, new Vector3(21.0f, -9.2f, 1.0f), new Vector3(1.15f, 1.15f, 1f), 0f, false, "Default", -20, new Color(122f/255f, 158f/255f, 168f/255f, 1.0f));

            // --- LAYER 3: MID VEGETATION ---
            var l3 = new GameObject("Layer3_MidVegetation");
            l3.transform.SetParent(root.transform, false);
            CreateElement("Kelp_LeftFlank", l3.transform, sKelp, new Vector3(-19.5f, -7.8f, 0.5f), new Vector3(1.05f, 1.20f, 1f), 0f, false, "Default", -10, new Color(85f/255f, 136f/255f, 119f/255f, 1.0f));
            CreateElement("Kelp_RightFlank", l3.transform, sKelp, new Vector3(17.5f, -8.2f, 0.5f), new Vector3(0.95f, 1.10f, 1f), 0f, false, "Default", -10, new Color(85f/255f, 136f/255f, 119f/255f, 1.0f));
            CreateElement("Seagrass_MidLeft", l3.transform, sGrass, new Vector3(-9.0f, -13.0f, 0.5f), new Vector3(0.85f, 0.85f, 1f), 0f, false, "Default", -10, new Color(96f/255f, 144f/255f, 128f/255f, 1.0f));
            CreateElement("Seagrass_MidRight", l3.transform, sGrass, new Vector3(8.5f, -13.1f, 0.5f), new Vector3(0.85f, 0.85f, 1f), 0f, false, "Default", -10, new Color(96f/255f, 144f/255f, 128f/255f, 1.0f));

            // --- LAYER 4: FOREGROUND OCCLUSION ---
            var l4 = new GameObject("Layer4_ForegroundOcclusion");
            l4.transform.SetParent(root.transform, false);
            CreateElement("FG_BoulderCorner_Left", l4.transform, sBoulder, new Vector3(-22.0f, -13.8f, -1.0f), new Vector3(1.40f, 1.35f, 1f), 0f, false, "ParallaxForeground", 10, new Color(71f/255f, 102f/255f, 114f/255f, 1.0f));
            CreateElement("FG_KelpPillar_Left", l4.transform, sKelp, new Vector3(-16.0f, -4.5f, -1.5f), new Vector3(1.45f, 1.50f, 1f), 0f, false, "ParallaxForeground", 20, new Color(58f/255f, 96f/255f, 80f/255f, 1.0f));
            CreateElement("FG_Seagrass_Bottom", l4.transform, sGrass, new Vector3(-1.5f, -14.2f, -1.2f), new Vector3(1.30f, 1.20f, 1f), 0f, false, "ParallaxForeground", 20, new Color(62f/255f, 104f/255f, 88f/255f, 1.0f));
            CreateElement("FG_CoralRidge_Right", l4.transform, sMound, new Vector3(18.5f, -12.5f, -1.0f), new Vector3(1.35f, 1.25f, 1f), -6f, false, "ParallaxForeground", 10, new Color(71f/255f, 102f/255f, 114f/255f, 1.0f));
            CreateElement("FG_KelpStalk_Right", l4.transform, sKelp, new Vector3(23.0f, -4.0f, -1.5f), new Vector3(1.50f, 1.55f, 1f), 0f, true, "ParallaxForeground", 20, new Color(58f/255f, 96f/255f, 80f/255f, 1.0f));

            envComp.InitializeLayers();

            var currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(currentScene);
            bool saved = UnityEditor.SceneManagement.EditorSceneManager.SaveScene(currentScene);

            return "OceanEnvironment created successfully! Scene saved: " + saved;
        }

        private static Sprite LoadSprite(string path)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath(path);
            foreach (var a in assets)
            {
                if (a is Sprite s) return s;
            }
            return null;
        }

        private static GameObject CreateElement(string name, Transform parent, Sprite sprite, Vector3 localPos, Vector3 localScale, float rotZ, bool flipX, string sortingLayer, int order, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            go.transform.localRotation = Quaternion.Euler(0f, 0f, rotZ);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.flipX = flipX;
            sr.sortingLayerName = sortingLayer;
            sr.sortingOrder = order;
            sr.color = color;
            return go;
        }
    }
}
