using TheDeep.Player;
using TheDeep.Submarine;
using TheDeep.UI.Terminal;
using TheDeep.UI.Terminal.Apps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace TheDeep.EditorTools
{
    /// <summary>
    /// Generates the placeholder submarine interior scene (greybox room, player, terminal).
    /// Menu: The Deep > Build Submarine Scene. Re-running it overwrites the scene.
    /// </summary>
    public static class SubmarineSceneBuilder
    {
        const string ScenePath = "Assets/_Project/Scenes/Submarine.unity";
        const string MaterialFolder = "Assets/_Project/Materials";

        [MenuItem("The Deep/Build Submarine Scene")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var hullMat = Mat("Sub_Hull", new Color(0.23f, 0.25f, 0.26f));
            var floorMat = Mat("Sub_Floor", new Color(0.16f, 0.16f, 0.15f));
            var trimMat = Mat("Sub_Trim", new Color(0.35f, 0.3f, 0.2f));
            var deskMat = Mat("Sub_Desk", new Color(0.3f, 0.26f, 0.2f));
            var beigeMat = Mat("Sub_ComputerBeige", new Color(0.62f, 0.6f, 0.52f));
            var portholeMat = Mat("Sub_PortholeGlass", new Color(0.02f, 0.06f, 0.08f), new Color(0.02f, 0.12f, 0.16f));
            var lampMat = Mat("Sub_LampWarm", new Color(1f, 0.8f, 0.5f), new Color(1.5f, 1.0f, 0.5f));
            var redLampMat = Mat("Sub_LampRed", new Color(0.8f, 0.1f, 0.05f), new Color(2f, 0.1f, 0.05f));

            // --- Hull: a 10m x 3.2m x 2.6m box interior (floor top at y = 0). ---
            var sub = new GameObject("Submarine").transform;
            var hull = new GameObject("Hull").transform;
            hull.SetParent(sub, false);
            Box("Floor", hull, new Vector3(0, -0.1f, 0), new Vector3(10.4f, 0.2f, 3.6f), floorMat);
            Box("Ceiling", hull, new Vector3(0, 2.7f, 0), new Vector3(10.4f, 0.2f, 3.6f), hullMat);
            Box("Wall_Port", hull, new Vector3(0, 1.3f, 1.7f), new Vector3(10.4f, 2.6f, 0.2f), hullMat);
            Box("Wall_Starboard", hull, new Vector3(0, 1.3f, -1.7f), new Vector3(10.4f, 2.6f, 0.2f), hullMat);
            Box("Wall_Bow", hull, new Vector3(5.1f, 1.3f, 0), new Vector3(0.2f, 2.6f, 3.6f), hullMat);
            Box("Wall_Stern", hull, new Vector3(-5.1f, 1.3f, 0), new Vector3(0.2f, 2.6f, 3.6f), hullMat);

            // Ribs every 2m and ceiling pipes, to break up the box.
            for (int i = -2; i <= 2; i++)
            {
                float x = i * 2f;
                Box("Rib", hull, new Vector3(x, 2.5f, 0), new Vector3(0.15f, 0.2f, 3.2f), trimMat, collider: false);
                Box("Rib", hull, new Vector3(x, 1.3f, 1.55f), new Vector3(0.15f, 2.6f, 0.12f), trimMat, collider: false);
                Box("Rib", hull, new Vector3(x, 1.3f, -1.55f), new Vector3(0.15f, 2.6f, 0.12f), trimMat, collider: false);
            }
            Pipe("Pipe", hull, new Vector3(0, 2.4f, 1.25f), 0.07f, 10f, trimMat);
            Pipe("Pipe", hull, new Vector3(0, 2.45f, 1.05f), 0.05f, 10f, hullMat);
            Pipe("Pipe", hull, new Vector3(0, 2.4f, -1.2f), 0.09f, 10f, hullMat);

            // Portholes on the starboard wall (nothing outside yet, just dark glass).
            foreach (float x in new[] { -3f, 1f })
            {
                var ring = Cylinder("PortholeRing", hull, new Vector3(x, 1.5f, -1.58f), new Vector3(0.6f, 0.03f, 0.6f), trimMat);
                ring.transform.localRotation = Quaternion.Euler(90, 0, 0);
                var glass = Cylinder("PortholeGlass", hull, new Vector3(x, 1.5f, -1.555f), new Vector3(0.48f, 0.02f, 0.48f), portholeMat);
                glass.transform.localRotation = Quaternion.Euler(90, 0, 0);
            }

            // Placeholder hatch (stern) and winch housing (bow) for later steps.
            Box("Hatch_Placeholder", hull, new Vector3(-4.95f, 1.0f, 0), new Vector3(0.1f, 2.0f, 1.0f), trimMat);
            Box("Winch_Placeholder", hull, new Vector3(4.4f, 0.6f, -1.1f), new Vector3(1.0f, 1.2f, 0.9f), trimMat);

            // --- Lighting: dim, warm, with a red emergency lamp at the stern. ---
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.035f, 0.045f, 0.055f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.01f, 0.02f, 0.025f);
            RenderSettings.fogDensity = 0.06f;

            var lights = new GameObject("Lights").transform;
            lights.SetParent(sub, false);
            foreach (float x in new[] { -2.5f, 1.5f })
            {
                Box("CeilingLamp", lights, new Vector3(x, 2.57f, 0), new Vector3(0.5f, 0.05f, 0.25f), lampMat, collider: false);
                PointLight("CeilingLight", lights, new Vector3(x, 2.35f, 0), new Color(1f, 0.78f, 0.5f), 1.6f, 5.5f, shadows: true);
            }
            Box("EmergencyLamp", lights, new Vector3(-4.95f, 2.3f, 1.2f), new Vector3(0.1f, 0.15f, 0.2f), redLampMat, collider: false);
            PointLight("EmergencyLight", lights, new Vector3(-4.7f, 2.2f, 1.2f), new Color(1f, 0.1f, 0.05f), 1.2f, 4f, shadows: false);

            // --- Terminal desk against the port wall near the bow. ---
            var terminalRoot = new GameObject("Terminal").transform;
            terminalRoot.SetParent(sub, false);
            terminalRoot.localPosition = new Vector3(2.6f, 0, 1.15f);
            Box("Desk", terminalRoot, new Vector3(0, 0.37f, 0), new Vector3(1.6f, 0.74f, 0.7f), deskMat);
            Box("Keyboard", terminalRoot, new Vector3(0, 0.755f, -0.2f), new Vector3(0.45f, 0.03f, 0.15f), beigeMat, collider: false);
            Box("MonitorBody", terminalRoot, new Vector3(0, 1.05f, 0.08f), new Vector3(0.62f, 0.52f, 0.5f), beigeMat);
            Box("MonitorBase", terminalRoot, new Vector3(0, 0.77f, 0.08f), new Vector3(0.35f, 0.06f, 0.3f), beigeMat, collider: false);
            Box("Chair", terminalRoot, new Vector3(0, 0.45f, -0.75f), new Vector3(0.45f, 0.08f, 0.45f), trimMat);

            // Screen: world-space canvas on the monitor's front face (1024x768 "pixels" = 0.5m x 0.375m).
            var screenGo = new GameObject("Screen", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var screen = (RectTransform)screenGo.transform;
            screen.SetParent(terminalRoot, false);
            screen.localPosition = new Vector3(0, 1.06f, 0.08f - 0.251f);
            screen.sizeDelta = new Vector2(1024, 768);
            screen.localScale = Vector3.one * (0.5f / 1024f);
            screenGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            screenGo.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;
            // Apps appear on the desktop in this order.
            screenGo.AddComponent<CommsApp>();
            screenGo.AddComponent<DiverMapApp>();
            screenGo.AddComponent<BalanceApp>();
            var os = screenGo.AddComponent<TerminalOS>();

            PointLight("ScreenGlow", terminalRoot, new Vector3(0, 1.0f, -0.6f), new Color(0.4f, 0.8f, 0.8f), 0.25f, 1.8f, shadows: false);

            var viewPoint = new GameObject("ViewPoint").transform;
            viewPoint.SetParent(terminalRoot, false);
            viewPoint.localPosition = new Vector3(0, 1.06f, -0.48f);

            var station = terminalRoot.gameObject.AddComponent<TerminalStation>();
            Assign(station, "viewPoint", viewPoint);
            Assign(station, "os", os);

            // --- Player ---
            var player = new GameObject("Player");
            player.transform.position = new Vector3(-2f, 0.05f, 0);
            player.transform.rotation = Quaternion.Euler(0, 90, 0);
            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.3f;
            cc.center = new Vector3(0, 0.9f, 0);
            var head = new GameObject("Head").transform;
            head.SetParent(player.transform, false);
            head.localPosition = new Vector3(0, 1.65f, 0);
            var camGo = new GameObject("Camera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(head, false);
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane = 0.03f;
            cam.fieldOfView = 70f;
            var fpc = player.AddComponent<FirstPersonController>();
            Assign(fpc, "head", head);
            var interactor = player.AddComponent<PlayerInteractor>();
            Assign(interactor, "playerCamera", cam);

            // --- UI event system (needed for clicking the terminal). ---
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log($"[The Deep] Built {ScenePath}");
        }

        static Material Mat(string name, Color color, Color? emission = null)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", 0.2f);
            if (emission.HasValue)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emission.Value);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 scale, Material mat, bool collider = true) =>
            Primitive(PrimitiveType.Cube, name, parent, pos, scale, mat, collider);

        static GameObject Cylinder(string name, Transform parent, Vector3 pos, Vector3 scale, Material mat) =>
            Primitive(PrimitiveType.Cylinder, name, parent, pos, scale, mat, collider: false);

        static void Pipe(string name, Transform parent, Vector3 pos, float radius, float length, Material mat)
        {
            var pipe = Cylinder(name, parent, pos, new Vector3(radius * 2, length * 0.5f, radius * 2), mat);
            pipe.transform.localRotation = Quaternion.Euler(0, 0, 90);
        }

        static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat, bool collider)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static void PointLight(string name, Transform parent, Vector3 pos, Color color, float intensity, float range, bool shadows)
        {
            var go = new GameObject(name, typeof(Light));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var light = go.GetComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        }

        static void Assign(Component component, string field, Object value)
        {
            var so = new SerializedObject(component);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
