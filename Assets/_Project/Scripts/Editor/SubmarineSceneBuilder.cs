using TheDeep.Core.Rendering;
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
    /// Generates the submarine scene: interior (room, props, terminal, player), exterior hull and seafloor.
    /// Menu: The Deep > Build Submarine Scene. Re-running it overwrites the scene.
    /// Split across partial files: .Details (interior atmosphere) and .Exterior (outside + seafloor).
    ///
    /// Layout (metres): floor top y=0, ceiling y=2.6, walls at z=+/-1.6 (port = +z),
    /// stern wall x=-5 (ladder, dive hatch, lockers), bow wall x=+5 (viewport, winch, terminal).
    /// </summary>
    public static partial class SubmarineSceneBuilder
    {
        const string ScenePath = "Assets/_Project/Scenes/Submarine.unity";
        const string MaterialFolder = "Assets/_Project/Materials";

        // Materials shared by the build helpers.
        static Material hull, hullDark, floor, grating, rust, hazard, painted, beige, wood, locker, cork;
        static Material brass, rope, rubber, paper, notePaper, yellowTank, redPaint, glassDark, gaugeFace;
        static Material lampOn, lampOff, lampRed, indGreen, indAmber, indRed, screenGreen, screenAmber;

        [MenuItem("The Deep/Build Submarine Scene")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateMaterials();

            var sub = new GameObject("Submarine").transform;
            BuildHull(Group("Hull", sub));
            BuildLighting(Group("Lights", sub));
            BuildStern(Group("Stern_DiveArea", sub));
            BuildMidship(Group("Midship_Consoles", sub));
            BuildBow(Group("Bow_Winch", sub));
            var terminal = Group("Terminal", sub);
            BuildTerminal(terminal);
            BuildChipReader(terminal);
            BuildInteriorDetails(Group("Details", sub));
            BuildExterior(Group("Exterior", sub));
            BuildSeafloor(new GameObject("Environment").transform);
            BuildPointsOfInterest(new GameObject("PointsOfInterest").transform);
            var bodyPrefab = BuildBodyPrefab();
            BuildLostDivers(bodyPrefab, GameObject.Find("Zone4_Trench").transform);
            BuildFootageRig();
            BuildExpeditionState();
            BuildSpawnPoints(Group("SpawnPoints", sub));
            var menuCamera = BuildDevTools(sub);
            BuildNetworking(BuildPlayerPrefab(bodyPrefab), menuCamera, bodyPrefab);
            ApplyNormalCullingMasks();
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            EditorSceneManager.SaveScene(scene, ScenePath);
            // Networked objects placed in the scene get their network ID from their place in the saved
            // scene file, so it can only be generated after the first save. Generate, then save again.
            var validate = typeof(Unity.Netcode.NetworkObject).GetMethod("OnValidate",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            foreach (var networkObject in Object.FindObjectsByType<Unity.Netcode.NetworkObject>(FindObjectsSortMode.None))
            {
                validate?.Invoke(networkObject, null);
                EditorUtility.SetDirty(networkObject);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            EnsureEmissionKeywords();
            AssetDatabase.SaveAssets();
            Debug.Log($"[The Deep] Built {ScenePath}");
        }

        // ---------------------------------------------------------------- materials

        static void CreateMaterials()
        {
            hull = Mat("Sub_Hull", ProceduralTextures.HullPanel(), Color.white, metallic: 0.4f);
            hullDark = Mat("Sub_HullDark", ProceduralTextures.HullPanel(), new Color(0.6f, 0.62f, 0.65f), metallic: 0.4f);
            floor = Mat("Sub_Floor", ProceduralTextures.FloorPlate(), Color.white, metallic: 0.5f, smoothness: 0.3f);
            grating = Mat("Sub_Grating", ProceduralTextures.Grating(), Color.white, metallic: 0.5f);
            rust = Mat("Sub_Rust", ProceduralTextures.Rust(), Color.white, metallic: 0.2f);
            hazard = Mat("Sub_Hazard", ProceduralTextures.Hazard(), Color.white);
            painted = Mat("Sub_PaintedMetal", ProceduralTextures.PaintedMetal(), Color.white, metallic: 0.3f);
            beige = Mat("Sub_ComputerBeige", ProceduralTextures.Beige(), Color.white);
            wood = Mat("Sub_Desk", ProceduralTextures.Wood(), Color.white);
            locker = Mat("Sub_Locker", ProceduralTextures.Locker(), Color.white, metallic: 0.3f);
            cork = Mat("Sub_Cork", ProceduralTextures.Cork(), Color.white);

            brass = Mat("Sub_Brass", null, new Color(0.55f, 0.42f, 0.18f), metallic: 0.8f, smoothness: 0.45f);
            rope = Mat("Sub_Rope", null, new Color(0.55f, 0.47f, 0.32f));
            rubber = Mat("Sub_Rubber", null, new Color(0.05f, 0.05f, 0.05f), smoothness: 0.35f);
            paper = Mat("Sub_Paper", null, new Color(0.78f, 0.76f, 0.7f));
            notePaper = Mat("Sub_NotePaper", null, new Color(0.8f, 0.72f, 0.3f));
            yellowTank = Mat("Sub_TankYellow", null, new Color(0.75f, 0.58f, 0.08f), metallic: 0.3f, smoothness: 0.4f);
            redPaint = Mat("Sub_RedPaint", null, new Color(0.5f, 0.08f, 0.05f), metallic: 0.2f);
            glassDark = Mat("Sub_PortholeGlass", null, new Color(0.01f, 0.03f, 0.04f), smoothness: 0.9f, emission: Color.black);
            gaugeFace = Mat("Sub_GaugeFace", null, new Color(0.8f, 0.78f, 0.65f), emission: new Color(0.25f, 0.24f, 0.18f));

            lampOn = Mat("Sub_LampWarm", null, new Color(1f, 0.8f, 0.5f), emission: new Color(1.5f, 1.0f, 0.5f));
            lampOff = Mat("Sub_LampOff", null, new Color(0.25f, 0.22f, 0.18f));
            lampRed = Mat("Sub_LampRed", null, new Color(0.8f, 0.1f, 0.05f), emission: new Color(2f, 0.1f, 0.05f));
            indGreen = Mat("Sub_IndicatorGreen", null, Color.green, emission: new Color(0.2f, 1.5f, 0.3f));
            indAmber = Mat("Sub_IndicatorAmber", null, new Color(1f, 0.6f, 0f), emission: new Color(1.6f, 0.8f, 0.05f));
            indRed = Mat("Sub_IndicatorRed", null, Color.red, emission: new Color(1.8f, 0.1f, 0.05f));
            screenGreen = Mat("Sub_ScreenGreen", null, new Color(0.05f, 0.2f, 0.08f), emission: new Color(0.1f, 0.6f, 0.2f));
            screenAmber = Mat("Sub_ScreenAmber", null, new Color(0.2f, 0.12f, 0.02f), emission: new Color(0.7f, 0.4f, 0.05f));
        }

        // ---------------------------------------------------------------- hull

        static void BuildHull(Transform t)
        {
            Box("Floor", t, new Vector3(0, -0.1f, 0), new Vector3(10.4f, 0.2f, 3.6f), floor);
            Box("Walkway_Grating", t, new Vector3(0.2f, 0.005f, 0), new Vector3(8.6f, 0.01f, 0.9f), grating, collider: false);
            Box("Ceiling", t, new Vector3(0, 2.7f, 0), new Vector3(10.4f, 0.2f, 3.6f), hullDark);
            Box("Wall_Port", t, new Vector3(0, 1.3f, 1.7f), new Vector3(10.4f, 2.6f, 0.2f), hull);
            Box("Wall_Starboard", t, new Vector3(0, 1.3f, -1.7f), new Vector3(10.4f, 2.6f, 0.2f), hull);
            Box("Wall_Bow", t, new Vector3(5.1f, 1.3f, 0), new Vector3(0.2f, 2.6f, 3.6f), hull);
            Box("Wall_Stern", t, new Vector3(-5.1f, 1.3f, 0), new Vector3(0.2f, 2.6f, 3.6f), hull);

            // Painted lower band on the long walls.
            Box("Wainscot_Port", t, new Vector3(0, 0.45f, 1.58f), new Vector3(10f, 0.9f, 0.04f), painted, collider: false);
            Box("Wainscot_Starboard", t, new Vector3(0, 0.45f, -1.58f), new Vector3(10f, 0.9f, 0.04f), painted, collider: false);

            // Angled panels where ceiling meets walls, so it reads as a pressure hull, not a box.
            Box("Chamfer_Port", t, new Vector3(0, 2.35f, 1.35f), new Vector3(10.2f, 0.08f, 0.72f), hullDark, collider: false)
                .transform.localRotation = Quaternion.Euler(45, 0, 0);
            Box("Chamfer_Starboard", t, new Vector3(0, 2.35f, -1.35f), new Vector3(10.2f, 0.08f, 0.72f), hullDark, collider: false)
                .transform.localRotation = Quaternion.Euler(-45, 0, 0);

            // Structural ribs.
            for (int i = -2; i <= 2; i++)
            {
                float x = i * 2f;
                Box("Rib_Ceiling", t, new Vector3(x, 2.52f, 0), new Vector3(0.14f, 0.16f, 2.4f), rust, collider: false);
                Box("Rib_Port", t, new Vector3(x, 1.05f, 1.53f), new Vector3(0.14f, 2.1f, 0.12f), rust, collider: false);
                Box("Rib_Starboard", t, new Vector3(x, 1.05f, -1.53f), new Vector3(0.14f, 2.1f, 0.12f), rust, collider: false);
            }

            // Pipes and cable tray.
            Pipe("Pipe_Port_A", t, new Vector3(0, 2.3f, 1.05f), 0.07f, 10f, rust);
            Pipe("Pipe_Port_B", t, new Vector3(0, 2.45f, 0.88f), 0.045f, 10f, painted);
            Pipe("Pipe_Starboard", t, new Vector3(0, 2.32f, -1.05f), 0.09f, 10f, hull);
            Box("CableTray", t, new Vector3(0, 2.5f, 0.38f), new Vector3(9.6f, 0.03f, 0.34f), grating, collider: false);
            for (int i = 0; i < 3; i++)
                Pipe("Cable", t, new Vector3(0, 2.535f, 0.28f + i * 0.09f), 0.02f + i * 0.005f, 9.6f, rubber);

            // Valve wheels on the pipes.
            foreach (float x in new[] { -1.2f, 3.4f })
                ValveWheel(t, new Vector3(x, 2.3f, 0.96f), Vector3.back, 0.28f);

            // Vertical pipe drop with valve near midship port wall.
            VerticalPipe(t, new Vector3(-2.35f, 1.3f, 1.42f), 0.06f, 2.6f, rust);
            VerticalPipe(t, new Vector3(-2.15f, 1.3f, 1.45f), 0.04f, 2.6f, painted);
            ValveWheel(t, new Vector3(-2.35f, 1.2f, 1.33f), Vector3.back, 0.22f);

            // Portholes (dark water outside, for now).
            Porthole(t, new Vector3(-2.2f, 1.75f, -1.58f), Vector3.forward, 0.55f);
            Porthole(t, new Vector3(1.0f, 1.75f, -1.58f), Vector3.forward, 0.55f);
            Porthole(t, new Vector3(-1.0f, 1.75f, 1.58f), Vector3.back, 0.55f);
            // Big bow viewport.
            Porthole(t, new Vector3(4.98f, 1.45f, 0.1f), Vector3.left, 1.1f);
        }

        // ---------------------------------------------------------------- lighting

        static void BuildLighting(Transform t)
        {
            RenderSettings.skybox = null;
            // No sky down here: without this, shiny surfaces reflect Unity's default blue sky.
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = null;
            RenderSettings.reflectionIntensity = 0f;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.035f, 0.045f, 0.055f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            // Murky deep water. Also applies inside, which gives the cabin a faint haze.
            RenderSettings.fogColor = new Color(0.01f, 0.035f, 0.045f);
            RenderSettings.fogDensity = 0.045f;

            CeilingLamp(t, new Vector3(-0.5f, 0, 0), shadows: true, flicker: false);
            CeilingLamp(t, new Vector3(2.6f, 0, 0), shadows: true, flicker: false);
            CeilingLamp(t, new Vector3(-3.5f, 0, 0), shadows: false, flicker: true);

            Box("EmergencyLamp", t, new Vector3(-4.95f, 2.2f, 1.2f), new Vector3(0.1f, 0.15f, 0.2f), lampRed, collider: false);
            var red = PointLight("EmergencyLight", t, new Vector3(-4.7f, 2.1f, 1.2f), new Color(1f, 0.1f, 0.05f), 1.0f, 4f, false);
            red.gameObject.AddComponent<FlickerLight>();
        }

        static void CeilingLamp(Transform t, Vector3 at, bool shadows, bool flicker)
        {
            var lamp = Group("CeilingLamp", t);
            lamp.localPosition = at;
            var bulb = Box("Bulb", lamp, new Vector3(0, 2.52f, -0.25f), new Vector3(0.45f, 0.06f, 0.2f), lampOn, collider: false, worldUV: false);
            // Wire cage around the bulb.
            foreach (float x in new[] { -0.22f, 0f, 0.22f })
                Box("Cage", lamp, new Vector3(x, 2.47f, -0.25f), new Vector3(0.015f, 0.015f, 0.24f), rubber, collider: false, worldUV: false);
            Box("Cage", lamp, new Vector3(0, 2.47f, -0.25f), new Vector3(0.47f, 0.015f, 0.015f), rubber, collider: false, worldUV: false);

            var light = PointLight("Light", lamp, new Vector3(0, 2.3f, -0.25f), new Color(1f, 0.78f, 0.5f), 1.5f, 5.5f, shadows);
            if (flicker)
            {
                var f = light.gameObject.AddComponent<FlickerLight>();
                Assign(f, "lampRenderer", bulb.GetComponent<Renderer>());
                Assign(f, "onMaterial", lampOn);
                Assign(f, "offMaterial", lampOff);
            }
        }

        // ---------------------------------------------------------------- stern: dive area

        static void BuildStern(Transform t)
        {
            // Ladder up to the surface hatch.
            foreach (float z in new[] { -0.25f, 0.25f })
                Box("LadderRail", t, new Vector3(-4.88f, 1.3f, z), new Vector3(0.05f, 2.6f, 0.05f), painted, collider: false, worldUV: false);
            for (float y = 0.3f; y < 2.5f; y += 0.3f)
                Box("LadderRung", t, new Vector3(-4.88f, y, 0), new Vector3(0.04f, 0.04f, 0.5f), rust, collider: false, worldUV: false);
            Cylinder("SurfaceHatchRing", t, new Vector3(-4.55f, 2.6f, 0), new Vector3(0.85f, 0.04f, 0.85f), rust);
            Cylinder("SurfaceHatch", t, new Vector3(-4.55f, 2.58f, 0), new Vector3(0.7f, 0.04f, 0.7f), painted);
            Stencil("SURFACE HATCH", t, new Vector3(-4.97f, 2.2f, -0.7f), Quaternion.Euler(0, -90, 0), 0.07f, new Color(0.85f, 0.8f, 0.6f));

            // Floor dive hatch (where divers will exit in step 2).
            Cylinder("DiveHatch_Hazard", t, new Vector3(-3.4f, 0.012f, 0), new Vector3(1.35f, 0.012f, 1.35f), hazard);
            Cylinder("DiveHatch_Door", t, new Vector3(-3.4f, 0.03f, 0), new Vector3(1.0f, 0.03f, 1.0f), painted);
            ValveWheel(t, new Vector3(-3.4f, 0.08f, 0), Vector3.up, 0.4f);
            // Look down at the hatch and press E to dive. You drop out under the hull, feet first,
            // low enough that the whole body clears the hull (bottom is at about y = -1.15).
            var exitPoint = Group("WaterExitPoint", t);
            exitPoint.localPosition = new Vector3(-3.4f, -3.4f, 0);
            exitPoint.localRotation = Quaternion.Euler(0, 90, 0);
            var entryPoint = Group("CabinEntryPoint", t);
            entryPoint.localPosition = new Vector3(-2.5f, 0.05f, 0);
            entryPoint.localRotation = Quaternion.Euler(0, 90, 0);
            HatchTrigger("DiveHatch_Cabin", t, new Vector3(-3.4f, 0.3f, 0), new Vector3(1.1f, 0.6f, 1.1f), DiveHatch.Side.Cabin, exitPoint);
            // Zone under the hull: swim up into it (or look at it) and press E to climb back in.
            HatchTrigger("DiveHatch_Water", t, new Vector3(-3.4f, -1.7f, 0), new Vector3(2.0f, 1.1f, 2.0f), DiveHatch.Side.Water, entryPoint);

            Stencil("DIVE HATCH 01", t, new Vector3(-3.4f, 0.035f, -0.62f), Quaternion.Euler(90, 90, 0), 0.08f, new Color(0.9f, 0.85f, 0.7f));

            // Diver lockers along the port wall + a dive helmet on top.
            float[] lockerX = { -4.35f, -3.78f, -3.21f };
            foreach (float x in lockerX)
            {
                Box("Locker", t, new Vector3(x, 0.95f, 1.33f), new Vector3(0.55f, 1.9f, 0.5f), locker, worldUV: false);
                Box("LockerHandle", t, new Vector3(x + 0.18f, 1.0f, 1.07f), new Vector3(0.03f, 0.18f, 0.03f), brass, collider: false, worldUV: false);
            }
            var helmet = Sphere("DiveHelmet", t, new Vector3(-3.78f, 2.08f, 1.33f), 0.34f, brass);
            Cylinder("HelmetVisor", helmet.transform.parent, new Vector3(-3.78f, 2.1f, 1.16f), new Vector3(0.18f, 0.02f, 0.18f), glassDark)
                .transform.localRotation = Quaternion.Euler(90, 0, 0);
            Stencil("DIVE TEAM", t, new Vector3(-3.78f, 1.75f, 1.075f), Quaternion.identity, 0.06f, new Color(0.9f, 0.9f, 0.8f));

            // Oxygen tanks on the starboard side.
            for (int i = 0; i < 3; i++)
            {
                float x = -4.55f + i * 0.28f;
                Cylinder("O2Tank", t, new Vector3(x, 0.6f, -1.38f), new Vector3(0.22f, 0.6f, 0.22f), yellowTank, collider: true);
                Sphere("O2TankTop", t, new Vector3(x, 1.2f, -1.38f), 0.22f, yellowTank);
                Cylinder("O2Valve", t, new Vector3(x, 1.33f, -1.38f), new Vector3(0.05f, 0.05f, 0.05f), brass);
            }
            Box("TankStrap", t, new Vector3(-4.27f, 0.9f, -1.38f), new Vector3(0.85f, 0.06f, 0.26f), rubber, collider: false, worldUV: false);
            Stencil("O2", t, new Vector3(-4.27f, 1.55f, -1.575f), Quaternion.Euler(0, 180, 0), 0.12f, new Color(0.9f, 0.9f, 0.85f));

            // Crates + toolbox.
            Box("Crate", t, new Vector3(-3.0f, 0.35f, -1.2f), new Vector3(0.7f, 0.7f, 0.7f), painted, worldUV: false);
            Box("Crate_Small", t, new Vector3(-3.05f, 0.95f, -1.22f), new Vector3(0.5f, 0.5f, 0.5f), locker, worldUV: false)
                .transform.localRotation = Quaternion.Euler(0, 14, 0);
            Box("Toolbox", t, new Vector3(-2.45f, 0.1f, -1.3f), new Vector3(0.45f, 0.2f, 0.22f), redPaint, worldUV: false);

            // Depth gauge on the stern wall.
            Gauge(t, new Vector3(-4.93f, 1.5f, 0.85f), Vector3.right, 0.22f, -40f);
        }

        static void HatchTrigger(string name, Transform t, Vector3 center, Vector3 size, DiveHatch.Side side, Transform destination)
        {
            var go = Group(name, t);
            go.localPosition = center;
            var box = go.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;
            var hatch = go.gameObject.AddComponent<DiveHatch>();
            Assign(hatch, "side", (int)side);
            Assign(hatch, "destination", destination);
        }

        // ---------------------------------------------------------------- midship: consoles

        static void BuildMidship(Transform t)
        {
            // Starboard control bank: base, slanted panel full of lights, and a riser with gauges.
            Box("Console_Base", t, new Vector3(0, 0.45f, -1.33f), new Vector3(2.8f, 0.9f, 0.5f), painted);
            Box("Console_Riser", t, new Vector3(0, 1.13f, -1.53f), new Vector3(2.8f, 0.46f, 0.14f), painted);

            var panel = Group("Console_Panel", t);
            panel.localPosition = new Vector3(0, 0.98f, -1.33f);
            panel.localRotation = Quaternion.Euler(35, 0, 0);
            Box("PanelTop", panel, Vector3.zero, new Vector3(2.8f, 0.05f, 0.45f), hullDark, collider: false);

            var rng = new System.Random(7);
            Material[] indicatorColors = { indGreen, indGreen, indAmber, indRed };
            for (int row = 0; row < 3; row++)
            for (int col = 0; col < 16; col++)
            {
                if (rng.NextDouble() < 0.25) continue;
                var mat = indicatorColors[rng.Next(indicatorColors.Length)];
                var light = Box("Indicator", panel, new Vector3(-0.9f + col * 0.08f, 0.03f, -0.12f + row * 0.09f),
                    new Vector3(0.035f, 0.02f, 0.035f), mat, collider: false, worldUV: false);
                if (rng.NextDouble() < 0.3)
                {
                    var blink = light.AddComponent<BlinkingIndicator>();
                    Assign(blink, "interval", 0.3f + (float)rng.NextDouble() * 1.2f);
                }
            }
            // Toggle switches.
            for (int i = 0; i < 8; i++)
            {
                var sw = Box("Switch", panel, new Vector3(0.5f + i * 0.07f, 0.04f, 0.05f), new Vector3(0.015f, 0.06f, 0.015f), brass, collider: false, worldUV: false);
                sw.transform.localRotation = Quaternion.Euler(rng.NextDouble() < 0.5 ? 25 : -25, 0, 0);
            }
            // Small sonar CRT on the bank.
            Box("SonarCRT", t, new Vector3(-0.95f, 1.18f, -1.3f), new Vector3(0.4f, 0.32f, 0.3f), beige, worldUV: false);
            Box("SonarScreen", t, new Vector3(-0.95f, 1.19f, -1.149f), new Vector3(0.3f, 0.22f, 0.01f), screenGreen, collider: false, worldUV: false);

            foreach (var (x, needle) in new[] { (0.1f, 20f), (0.45f, -60f), (0.8f, 75f) })
                Gauge(t, new Vector3(x, 1.2f, -1.455f), Vector3.forward, 0.16f, needle);
            Stencil("BALLAST / TRIM", t, new Vector3(0.45f, 1.33f, -1.455f), Quaternion.Euler(0, 180, 0), 0.045f, new Color(0.9f, 0.9f, 0.8f));

            PointLight("ConsoleGlow", t, new Vector3(0, 1.3f, -0.95f), new Color(0.35f, 0.9f, 0.5f), 0.25f, 1.6f, false);
            Cylinder("Stool", t, new Vector3(0.3f, 0.25f, -0.75f), new Vector3(0.38f, 0.25f, 0.38f), rubber, collider: true);

            // Electrical panel on the port wall.
            Box("FuseBox", t, new Vector3(0.3f, 1.4f, 1.5f), new Vector3(0.5f, 0.65f, 0.14f), painted, worldUV: false);
            for (int i = 0; i < 4; i++)
            {
                var l = Box("FuseLight", t, new Vector3(0.14f + i * 0.1f, 1.62f, 1.425f), new Vector3(0.03f, 0.03f, 0.01f),
                    i == 3 ? indRed : indGreen, collider: false, worldUV: false);
                if (i == 3) l.AddComponent<BlinkingIndicator>();
            }
            Stencil("DANGER  440V", t, new Vector3(0.3f, 1.3f, 1.425f), Quaternion.identity, 0.05f, new Color(0.95f, 0.8f, 0.1f));
        }

        // ---------------------------------------------------------------- bow: winch

        static void BuildBow(Transform t)
        {
            // Rope winch in the starboard bow corner: the divers' tether reel.
            Box("Winch_Hazard", t, new Vector3(4.2f, 0.004f, -0.95f), new Vector3(1.5f, 0.008f, 1.1f), hazard, collider: false);
            foreach (float x in new[] { 3.65f, 4.75f })
                Box("Winch_Frame", t, new Vector3(x, 0.5f, -1.1f), new Vector3(0.08f, 1.0f, 0.7f), painted);
            var drum = Cylinder("Winch_Drum", t, new Vector3(4.2f, 0.65f, -1.1f), new Vector3(0.62f, 0.52f, 0.62f), rope, collider: true);
            drum.transform.localRotation = Quaternion.Euler(0, 0, 90);
            foreach (float x in new[] { 3.72f, 4.68f })
                Cylinder("Winch_Flange", t, new Vector3(x, 0.65f, -1.1f), new Vector3(0.85f, 0.02f, 0.85f), rust)
                    .transform.localRotation = Quaternion.Euler(0, 0, 90);
            Box("Winch_Motor", t, new Vector3(4.95f, 0.45f, -1.1f), new Vector3(0.2f, 0.5f, 0.45f), painted, worldUV: false);
            // Rope running from the drum down through a floor slot.
            Cylinder("Rope_ToFloor", t, new Vector3(4.2f, 0.18f, -0.72f), new Vector3(0.035f, 0.26f, 0.035f), rope)
                .transform.localRotation = Quaternion.Euler(-35, 0, 0);
            Box("FloorSlot", t, new Vector3(4.2f, 0.006f, -0.6f), new Vector3(0.3f, 0.01f, 0.12f), rubber, collider: false, worldUV: false);
            // Kept between the rib at x=4 and the bow wall so the rib doesn't cover it.
            Stencil("WINCH A - TETHER 01", t, new Vector3(4.55f, 1.62f, -1.575f), Quaternion.Euler(0, 180, 0), 0.05f, new Color(0.9f, 0.85f, 0.7f));
            Stencil("MAX 120 M", t, new Vector3(4.55f, 1.52f, -1.575f), Quaternion.Euler(0, 180, 0), 0.05f, new Color(0.9f, 0.3f, 0.2f));

            // Filing cabinet in the port bow corner.
            Box("FilingCabinet", t, new Vector3(4.55f, 0.55f, 1.3f), new Vector3(0.55f, 1.1f, 0.55f), locker, worldUV: false);
            for (int i = 0; i < 3; i++)
                Box("DrawerHandle", t, new Vector3(4.55f, 0.3f + i * 0.35f, 1.02f), new Vector3(0.14f, 0.025f, 0.03f), brass, collider: false, worldUV: false);
            for (int i = 0; i < 4; i++)
                Box("Binder", t, new Vector3(4.4f + i * 0.08f, 1.24f, 1.3f), new Vector3(0.06f, 0.28f, 0.3f),
                    i % 2 == 0 ? redPaint : painted, collider: false, worldUV: false);
        }

        // ---------------------------------------------------------------- terminal desk

        static void BuildTerminal(Transform root)
        {
            root.localPosition = new Vector3(2.6f, 0, 1.15f);

            Box("Desk", root, new Vector3(0, 0.72f, 0), new Vector3(1.6f, 0.05f, 0.7f), wood);
            Box("DeskCabinet_L", root, new Vector3(-0.6f, 0.35f, 0), new Vector3(0.38f, 0.7f, 0.66f), painted, worldUV: false);
            Box("DeskCabinet_R", root, new Vector3(0.6f, 0.35f, 0), new Vector3(0.38f, 0.7f, 0.66f), painted, worldUV: false);
            Box("Keyboard", root, new Vector3(0, 0.755f, -0.2f), new Vector3(0.45f, 0.03f, 0.15f), beige, collider: false, worldUV: false);
            Box("MonitorBody", root, new Vector3(0, 1.05f, 0.08f), new Vector3(0.62f, 0.52f, 0.5f), beige, worldUV: false);
            Box("MonitorBack", root, new Vector3(0, 1.03f, 0.3f), new Vector3(0.45f, 0.4f, 0.2f), beige, collider: false, worldUV: false);
            Box("MonitorBase", root, new Vector3(0, 0.77f, 0.08f), new Vector3(0.35f, 0.06f, 0.3f), beige, collider: false, worldUV: false);
            Box("StickyNote", root, new Vector3(0.26f, 1.27f, -0.172f), new Vector3(0.06f, 0.06f, 0.002f), notePaper, collider: false, worldUV: false)
                .transform.localRotation = Quaternion.Euler(0, 0, 8);

            // Radio base station (the other end of the divers' walkie-talkies).
            Box("Radio", root, new Vector3(0.58f, 0.83f, 0.12f), new Vector3(0.36f, 0.17f, 0.28f), painted, collider: false, worldUV: false);
            Box("RadioDisplay", root, new Vector3(0.53f, 0.86f, -0.021f), new Vector3(0.16f, 0.05f, 0.005f), screenAmber, collider: false, worldUV: false);
            foreach (float x in new[] { 0.67f, 0.72f })
                Cylinder("RadioKnob", root, new Vector3(x, 0.8f, -0.03f), new Vector3(0.035f, 0.01f, 0.035f), rubber)
                    .transform.localRotation = Quaternion.Euler(90, 0, 0);
            Box("Handset", root, new Vector3(0.62f, 0.77f, -0.18f), new Vector3(0.07f, 0.04f, 0.18f), rubber, collider: false, worldUV: false)
                .transform.localRotation = Quaternion.Euler(0, 20, 0);

            // Clutter.
            Cylinder("Mug", root, new Vector3(-0.55f, 0.795f, -0.15f), new Vector3(0.08f, 0.05f, 0.08f), redPaint);
            Box("Papers", root, new Vector3(-0.42f, 0.748f, 0.05f), new Vector3(0.21f, 0.004f, 0.29f), paper, collider: false, worldUV: false)
                .transform.localRotation = Quaternion.Euler(0, -12, 0);
            Box("Papers", root, new Vector3(-0.38f, 0.752f, 0.08f), new Vector3(0.21f, 0.004f, 0.29f), paper, collider: false, worldUV: false)
                .transform.localRotation = Quaternion.Euler(0, 7, 0);

            // Desk lamp.
            Cylinder("LampBase", root, new Vector3(-0.62f, 0.755f, 0.2f), new Vector3(0.12f, 0.01f, 0.12f), rubber);
            Box("LampArm", root, new Vector3(-0.62f, 0.93f, 0.17f), new Vector3(0.02f, 0.35f, 0.02f), rubber, collider: false, worldUV: false)
                .transform.localRotation = Quaternion.Euler(-12, 0, 0);
            Box("LampHead", root, new Vector3(-0.6f, 1.08f, 0.08f), new Vector3(0.12f, 0.06f, 0.1f), redPaint, collider: false, worldUV: false);
            PointLight("DeskLampLight", root, new Vector3(-0.58f, 1.0f, 0.0f), new Color(1f, 0.75f, 0.45f), 0.5f, 1.4f, false);

            // Corkboard: the missing dive team.
            Box("Corkboard", root, new Vector3(0, 1.75f, 0.32f), new Vector3(1.3f, 0.7f, 0.03f), cork, collider: false, worldUV: false);
            var rng = new System.Random(3);
            for (int i = 0; i < 7; i++)
            {
                var note = Box("PinnedNote", root,
                    new Vector3(-0.5f + i * 0.16f, 1.68f + (float)rng.NextDouble() * 0.2f, 0.303f),
                    new Vector3(0.1f + (float)rng.NextDouble() * 0.04f, 0.13f, 0.003f),
                    i % 3 == 0 ? notePaper : paper, collider: false, worldUV: false);
                note.transform.localRotation = Quaternion.Euler(0, 0, (float)(rng.NextDouble() - 0.5) * 16f);
            }
            Stencil("DIVE TEAM 7 - STILL MISSING", root, new Vector3(0, 2.03f, 0.3f), Quaternion.identity, 0.045f, new Color(0.95f, 0.9f, 0.8f));

            Box("Chair_Seat", root, new Vector3(0, 0.45f, -0.75f), new Vector3(0.45f, 0.06f, 0.45f), rubber, worldUV: false);
            Box("Chair_Back", root, new Vector3(0, 0.75f, -0.96f), new Vector3(0.42f, 0.5f, 0.05f), rubber, collider: false, worldUV: false);
            Cylinder("Chair_Post", root, new Vector3(0, 0.22f, -0.75f), new Vector3(0.06f, 0.22f, 0.06f), painted);

            // Screen: world-space canvas on the monitor's front face (800x600 units = 0.5m x 0.375m).
            var screenGo = new GameObject("Screen", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var screen = (RectTransform)screenGo.transform;
            screen.SetParent(root, false);
            screen.localPosition = new Vector3(0, 1.06f, 0.08f - 0.251f);
            screen.sizeDelta = new Vector2(800, 600);
            screen.localScale = Vector3.one * (0.5f / 800f);
            screenGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            screenGo.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;
            // Apps appear on the desktop in this order.
            screenGo.AddComponent<CommsApp>();
            screenGo.AddComponent<DiverMapApp>();
            var cameras = screenGo.AddComponent<CamerasApp>();
            Assign(cameras, "feedMaterial", ShaderMaterial("M_CCTVFeed", "TheDeep/CCTVFeed"));
            screenGo.AddComponent<BalanceApp>();
            screenGo.AddComponent<CaseFilesApp>();
            screenGo.AddComponent<NavApp>();
            screenGo.AddComponent<RadioApp>();
            AddFootageApp(screenGo);
            var os = screenGo.AddComponent<TerminalOS>();

            PointLight("ScreenGlow", root, new Vector3(0, 1.0f, -0.6f), new Color(0.4f, 0.8f, 0.8f), 0.25f, 1.8f, false);

            var viewPoint = Group("ViewPoint", root);
            viewPoint.localPosition = new Vector3(0, 1.06f, -0.48f);

            // Generous invisible "use" zone over the desk + monitor so E works from any sensible angle.
            var useZone = root.gameObject.AddComponent<BoxCollider>();
            useZone.isTrigger = true;
            useZone.center = new Vector3(0, 1.25f, -0.05f);
            useZone.size = new Vector3(1.4f, 1.2f, 0.75f);

            var station = root.gameObject.AddComponent<TerminalStation>();
            Assign(station, "viewPoint", viewPoint);
            Assign(station, "os", os);
        }

        static Material RetroScreenMaterial() => ShaderMaterial("M_RetroScreen", "TheDeep/RetroScreen");

        /// <summary>Material asset for one of our custom shaders (created once, then reused).</summary>
        static Material ShaderMaterial(string name, string shader)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find(shader));
                AssetDatabase.CreateAsset(mat, path);
            }
            return mat;
        }

        // ---------------------------------------------------------------- prop helpers

        static void Porthole(Transform t, Vector3 pos, Vector3 facing, float diameter)
        {
            Quaternion rot = Facing(facing);
            Cylinder("PortholeRing", t, pos, new Vector3(diameter, 0.04f, diameter), brass).transform.localRotation = rot;
            // Glass sits slightly proud of the (solid) rim cylinder so the rim reads as a ring around it.
            Cylinder("PortholeGlass", t, pos + rot * Vector3.up * 0.012f, new Vector3(diameter * 0.78f, 0.04f, diameter * 0.78f), glassDark)
                .transform.localRotation = rot;
            // Bolts around the rim.
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                Vector3 offset = rot * new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * diameter * 0.45f;
                Sphere("Bolt", t, pos + offset + rot * Vector3.up * 0.03f, 0.035f, brass);
            }
        }

        static void ValveWheel(Transform t, Vector3 pos, Vector3 facing, float diameter)
        {
            Quaternion rot = Facing(facing);
            Cylinder("ValveRim", t, pos, new Vector3(diameter, 0.012f, diameter), redPaint).transform.localRotation = rot;
            for (int i = 0; i < 2; i++)
            {
                var spoke = Box("ValveSpoke", t, pos, new Vector3(diameter * 0.9f, 0.02f, 0.02f), redPaint, collider: false, worldUV: false);
                spoke.transform.localRotation = rot * Quaternion.Euler(0, i * 90, 0);
            }
        }

        static void Gauge(Transform t, Vector3 pos, Vector3 facing, float diameter, float needleAngle)
        {
            Quaternion rot = Facing(facing);
            Cylinder("GaugeRim", t, pos, new Vector3(diameter, 0.03f, diameter), brass).transform.localRotation = rot;
            Cylinder("GaugeFace", t, pos + rot * Vector3.up * 0.02f, new Vector3(diameter * 0.85f, 0.02f, diameter * 0.85f), gaugeFace)
                .transform.localRotation = rot;
            var needle = Box("GaugeNeedle", t, pos + rot * Vector3.up * 0.045f, new Vector3(0.008f, 0.004f, diameter * 0.38f), rubber, collider: false, worldUV: false);
            needle.transform.localRotation = rot * Quaternion.Euler(0, needleAngle, 0);
        }

        /// <summary>Rotation that points a cylinder's flat face (its local up) along <paramref name="facing"/>.</summary>
        static Quaternion Facing(Vector3 facing) => Quaternion.FromToRotation(Vector3.up, facing);

        static void VerticalPipe(Transform t, Vector3 center, float radius, float length, Material mat) =>
            Cylinder("VerticalPipe", t, center, new Vector3(radius * 2, length * 0.5f, radius * 2), mat);

        static void Pipe(string name, Transform t, Vector3 center, float radius, float length, Material mat) =>
            Cylinder(name, t, center, new Vector3(radius * 2, length * 0.5f, radius * 2), mat).transform.localRotation = Quaternion.Euler(0, 0, 90);

        /// <summary>
        /// Painted-on text (world-space canvas). It reads correctly when you look along the rotation's
        /// forward axis, e.g. identity for the port wall (+Z), Euler(0,180,0) for the starboard wall.
        /// </summary>
        static void Stencil(string text, Transform parent, Vector3 pos, Quaternion rot, float height, Color color)
        {
            const int fontSize = 40;
            var go = new GameObject("Stencil_" + text, typeof(RectTransform), typeof(Canvas));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.localPosition = pos;
            rt.localRotation = rot;
            rt.sizeDelta = new Vector2(text.Length * fontSize * 0.75f, fontSize * 1.3f);
            rt.localScale = Vector3.one * (height / fontSize);
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;

            var label = new GameObject("Text", typeof(RectTransform), typeof(Text));
            var lrt = (RectTransform)label.transform;
            lrt.SetParent(rt, false);
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            var t = label.GetComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.text = text;
            t.fontSize = fontSize;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = color * new Color(1, 1, 1, 0.85f);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.raycastTarget = false;
        }

        // ---------------------------------------------------------------- primitives

        static Transform Group(string name, Transform parent)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        static Material Mat(string name, Texture2D texture, Color color, float metallic = 0f, float smoothness = 0.2f, Color? emission = null)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetTexture("_BaseMap", texture);
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", smoothness);
            if (emission.HasValue)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emission.Value);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// Safety net: URP re-validates materials and turns _EMISSION off unless the GI flags say
        /// the material is emissive, so make sure every material with a glow colour has both.
        /// </summary>
        static void EnsureEmissionKeywords()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { MaterialFolder }))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (!mat.HasProperty("_EmissionColor") || mat.GetColor("_EmissionColor").maxColorComponent <= 0.001f) continue;
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                EditorUtility.SetDirty(mat);
            }
        }

        static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 scale, Material mat, bool collider = true, bool worldUV = true)
        {
            var go = Primitive(PrimitiveType.Cube, name, parent, pos, scale, mat, collider);
            if (worldUV) go.AddComponent<WorldUVBox>();
            return go;
        }

        static GameObject Cylinder(string name, Transform parent, Vector3 pos, Vector3 scale, Material mat, bool collider = false)
        {
            var go = Primitive(PrimitiveType.Cylinder, name, parent, pos, scale, mat, collider);
            if (collider)
            {
                // Swap the capsule collider for a box so the scaled cylinder collides sensibly.
                Object.DestroyImmediate(go.GetComponent<Collider>());
                go.AddComponent<BoxCollider>();
            }
            return go;
        }

        static GameObject Sphere(string name, Transform parent, Vector3 pos, float diameter, Material mat) =>
            Primitive(PrimitiveType.Sphere, name, parent, pos, Vector3.one * diameter, mat, collider: false);

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

        static Light PointLight(string name, Transform parent, Vector3 pos, Color color, float intensity, float range, bool shadows)
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
            return light;
        }

        static void Assign(Component component, string field, Object value)
        {
            var so = new SerializedObject(component);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void Assign(Component component, string field, float value)
        {
            var so = new SerializedObject(component);
            so.FindProperty(field).floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void Assign(Component component, string field, int value)
        {
            var so = new SerializedObject(component);
            so.FindProperty(field).intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void Assign(Component component, string field, string value)
        {
            var so = new SerializedObject(component);
            so.FindProperty(field).stringValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void Assign(Component component, string field, Vector3 value)
        {
            var so = new SerializedObject(component);
            so.FindProperty(field).vector3Value = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
