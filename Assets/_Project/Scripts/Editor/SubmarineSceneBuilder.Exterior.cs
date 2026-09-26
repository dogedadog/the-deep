using TheDeep.Core;
using TheDeep.Core.Rendering;
using TheDeep.Submarine;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TheDeep.EditorTools
{
    /// <summary>
    /// Outside of the sub (orange research-sub hull wrapped around the interior box) and the
    /// seafloor it hovers over. No colliders on the hull yet; divers get proper collision in step 2.
    /// Hull axis runs along x at y=1.3; hull radius 2.45, so the bottom is at about y=-1.15.
    /// </summary>
    public static partial class SubmarineSceneBuilder
    {
        const float HullY = 1.3f;
        const float HullRadius = 2.45f;
        const float SeafloorY = -9f;

        static void BuildExterior(Transform t)
        {
            var hullPaint = Mat("Ext_HullOrange", ProceduralTextures.HullExterior(), Color.white, metallic: 0.3f, smoothness: 0.3f);
            var hullPaintSmall = Mat("Ext_HullOrangeSmall", ProceduralTextures.HullExterior(), Color.white, metallic: 0.3f, smoothness: 0.3f);
            var darkMetal = Mat("Ext_DarkMetal", ProceduralTextures.HullPanel(), new Color(0.35f, 0.36f, 0.38f), metallic: 0.5f);
            darkMetal.SetTextureScale("_BaseMap", new Vector2(6, 3));
            var windowLit = Mat("Ext_WindowLit", null, new Color(0.9f, 0.7f, 0.4f), emission: new Color(1.2f, 0.75f, 0.35f));
            var lensLit = Mat("Ext_LampLens", null, new Color(0.9f, 0.95f, 1f), emission: new Color(3f, 3.2f, 3.5f));
            var navRed = Mat("Ext_NavRed", null, Color.red, emission: new Color(2.5f, 0.1f, 0.05f));
            var navGreen = Mat("Ext_NavGreen", null, Color.green, emission: new Color(0.1f, 2.5f, 0.4f));

            // Main pressure hull: one lathed mesh (rounded nose, tapered tail), outward-facing only,
            // so from inside the cabin it's invisible and never pokes through the walls.
            var hull = new GameObject("Hull_Outer", typeof(MeshFilter), typeof(MeshRenderer));
            hull.transform.SetParent(t, false);
            hull.GetComponent<MeshFilter>().sharedMesh = HullMesh();
            hull.GetComponent<MeshRenderer>().sharedMaterial = hullPaint;
            hull.AddComponent<MeshCollider>().sharedMesh = hull.GetComponent<MeshFilter>().sharedMesh; // divers bump into it

            // Big glowing bow window (the viewport you see from inside).
            Sphere("BowWindow", t, new Vector3(7.62f, 1.45f, 0.1f), 1f, windowLit).transform.localScale = new Vector3(0.5f, 1.1f, 1.1f);
            // Side portholes, lined up with the interior ones.
            foreach (var (x, side) in new[] { (-2.2f, -1f), (1.0f, -1f), (-1.0f, 1f) })
            {
                float z = side * Mathf.Sqrt(HullRadius * HullRadius - 0.45f * 0.45f);
                var facing = new Vector3(0, 0, side);
                Cylinder("WindowRim", t, new Vector3(x, 1.75f, z), new Vector3(0.62f, 0.05f, 0.62f), brass).transform.localRotation = Facing(facing);
                Cylinder("Window", t, new Vector3(x, 1.75f, z + side * 0.03f), new Vector3(0.46f, 0.05f, 0.46f), windowLit).transform.localRotation = Facing(facing);
            }

            // Conning tower ("sail") with hatch, planes, mast and name.
            float sailTop = HullY + HullRadius + 1.5f;
            Box("Sail", t, new Vector3(0.8f, sailTop - 0.8f, 0), new Vector3(2.6f, 1.6f, 1.0f), hullPaintSmall);
            Cylinder("Sail_Front", t, new Vector3(2.1f, sailTop - 0.8f, 0), new Vector3(1.0f, 0.8f, 1.0f), hullPaintSmall, collider: true);
            Box("Sail_Planes", t, new Vector3(1.4f, sailTop - 0.6f, 0), new Vector3(0.8f, 0.06f, 3.0f), darkMetal, collider: false);
            Cylinder("Sail_HatchRing", t, new Vector3(0.4f, sailTop + 0.01f, 0), new Vector3(0.8f, 0.03f, 0.8f), darkMetal);
            Cylinder("Sail_Hatch", t, new Vector3(0.4f, sailTop + 0.03f, 0), new Vector3(0.65f, 0.03f, 0.65f), hullPaintSmall);
            Cylinder("Mast", t, new Vector3(-0.2f, sailTop + 0.6f, 0), new Vector3(0.06f, 0.6f, 0.06f), darkMetal);
            var beacon = Sphere("MastBeacon", t, new Vector3(-0.2f, sailTop + 1.22f, 0), 0.12f, navRed);
            beacon.AddComponent<BlinkingIndicator>();
            PointLight("MastLight", t, new Vector3(-0.2f, sailTop + 1.35f, 0), new Color(1f, 0.15f, 0.1f), 1.5f, 4f, false);
            var nameColor = new Color(0.95f, 0.93f, 0.85f);
            Stencil("DSV ABYSSAL-3", t, new Vector3(0.8f, sailTop - 0.6f, -0.505f), Quaternion.identity, 0.28f, nameColor);
            Stencil("DSV ABYSSAL-3", t, new Vector3(0.8f, sailTop - 0.6f, 0.505f), Quaternion.Euler(0, 180, 0), 0.28f, nameColor);

            // Ballast tanks and landing skids underneath.
            foreach (float z in new[] { -1.55f, 1.55f })
            {
                Cylinder("BallastTank", t, new Vector3(0, -0.75f, z), new Vector3(0.8f, 4.2f, 0.8f), darkMetal, collider: true)
                    .transform.localRotation = Quaternion.Euler(0, 0, 90);
                Box("Skid", t, new Vector3(0, -1.55f, z), new Vector3(8f, 0.12f, 0.2f), darkMetal, collider: false);
                foreach (float x in new[] { -3f, 3f })
                    Box("SkidStrut", t, new Vector3(x, -1.25f, z), new Vector3(0.12f, 0.6f, 0.12f), darkMetal, collider: false);
            }

            // Stern: cross fins and a slowly turning propeller.
            Box("Fin_Vertical", t, new Vector3(-7.9f, HullY, 0), new Vector3(1.8f, 5.2f, 0.1f), hullPaintSmall);
            Box("Fin_Horizontal", t, new Vector3(-7.9f, HullY, 0), new Vector3(1.8f, 0.1f, 5.2f), hullPaintSmall);
            Cylinder("PropHub", t, new Vector3(-9.0f, HullY, 0), new Vector3(0.35f, 0.25f, 0.35f), darkMetal)
                .transform.localRotation = Quaternion.Euler(0, 0, 90);
            var prop = Group("Propeller", t);
            prop.localPosition = new Vector3(-9.2f, HullY, 0);
            var spin = prop.gameObject.AddComponent<Rotator>();
            Assign(spin, "axis", Vector3.right);
            Assign(spin, "degreesPerSecond", 40f);
            for (int i = 0; i < 4; i++)
            {
                var pivot = Group("Blade", prop);
                pivot.localRotation = Quaternion.Euler(i * 90f, 0, 0);
                Box("BladeMesh", pivot, new Vector3(0, 0.5f, 0), new Vector3(0.05f, 0.95f, 0.28f), brass, collider: false, worldUV: false)
                    .transform.localRotation = Quaternion.Euler(0, 25f, 0);
            }

            // Headlights on the nose and a floodlight under the dive hatch.
            foreach (float z in new[] { -1.3f, 1.3f })
            {
                Cylinder("HeadlightHousing", t, new Vector3(7.15f, 0.2f, z), new Vector3(0.35f, 0.15f, 0.35f), darkMetal)
                    .transform.localRotation = Facing(Vector3.right);
                Cylinder("HeadlightLens", t, new Vector3(7.31f, 0.2f, z), new Vector3(0.26f, 0.02f, 0.26f), lensLit)
                    .transform.localRotation = Facing(Vector3.right);
                SpotLight("Headlight", t, new Vector3(7.4f, 0.2f, z), Quaternion.Euler(12, 90 + z * 6f, 0), new Color(0.75f, 0.88f, 1f), 160f, 45f, 50f);
            }
            SpotLight("HatchFloodlight", t, new Vector3(-3.4f, -1.25f, 0), Quaternion.Euler(90, 0, 0), new Color(0.8f, 0.9f, 1f), 70f, 16f, 70f);
            Cylinder("DiveHatch_Outer", t, new Vector3(-3.4f, -1.17f, 0), new Vector3(1.2f, 0.05f, 1.2f), hazard);

            // Navigation lights: red to port, green to starboard.
            Sphere("NavLight_Port", t, new Vector3(3f, HullY + 1.2f, 2.15f), 0.12f, navRed);
            Sphere("NavLight_Starboard", t, new Vector3(3f, HullY + 1.2f, -2.15f), 0.12f, navGreen);

            // Small manipulator arm folded under the bow.
            Box("Arm_Base", t, new Vector3(5.4f, -1.2f, -0.7f), new Vector3(0.3f, 0.3f, 0.3f), darkMetal, collider: false, worldUV: false);
            Box("Arm_Upper", t, new Vector3(5.9f, -1.35f, -0.7f), new Vector3(0.9f, 0.12f, 0.12f), darkMetal, collider: false, worldUV: false)
                .transform.localRotation = Quaternion.Euler(0, 0, -20);
            Box("Arm_Lower", t, new Vector3(6.35f, -1.65f, -0.7f), new Vector3(0.12f, 0.5f, 0.12f), darkMetal, collider: false, worldUV: false);
            Box("Arm_Claw", t, new Vector3(6.35f, -1.95f, -0.7f), new Vector3(0.2f, 0.12f, 0.08f), brass, collider: false, worldUV: false);

            // Tether fairlead beside the dive hatch, where divers' ropes leave the hull (reeling in
            // brings a diver back to the hatch).
            Cylinder("TetherFairlead", t, new Vector3(-2.4f, -1.1f, 0.55f), new Vector3(0.3f, 0.15f, 0.3f), darkMetal);
            var anchorPoint = Group("TetherAnchor", t);
            anchorPoint.localPosition = new Vector3(-2.4f, -1.22f, 0.55f);
            var anchor = anchorPoint.gameObject.AddComponent<TetherAnchor>();
            Assign(anchor, "seafloorY", SeafloorY);

            // Hull cameras, viewable from the terminal's camera app.
            var cams = Group("HullCameras", t);
            HullCamera(cams, darkMetal, 1, "BOW", new Vector3(7.35f, 2.9f, 0f), new Vector3(15f, -1.5f, 0f), light: true);
            HullCamera(cams, darkMetal, 2, "KEEL / DIVE HATCH", new Vector3(-1.6f, -1.4f, 0.3f), new Vector3(-4.5f, -6f, 0f), light: false);
            HullCamera(cams, darkMetal, 3, "SAIL / AFT", new Vector3(-0.35f, 5.5f, 0f), new Vector3(-9f, 1.5f, 0f), light: true);
            HullCamera(cams, darkMetal, 4, "STARBOARD", new Vector3(4.6f, 2.4f, -2.45f), new Vector3(-6f, 1.2f, -3.2f), light: true);
            HullCamera(cams, darkMetal, 5, "TETHER / FAIRLEAD", new Vector3(-0.9f, -1.5f, 1.6f), new Vector3(-2.9f, -3.6f, 0.2f), light: true);
            HullCamera(cams, darkMetal, 6, "AFT / PROPELLER", new Vector3(-6.4f, 3.9f, 1.5f), new Vector3(-9.3f, 1.0f, -0.4f), light: true);

            // Hull shell and its parts shouldn't throw shadows onto the interior lights' surfaces.
            foreach (var r in t.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>
        /// Surface of revolution along x: tail tip at x=-8.9, full radius from -5.6 to 5.6, nose tip at 7.8.
        /// Saved as an asset so the scene can reference it.
        /// </summary>
        static Mesh HullMesh()
        {
            const string folder = "Assets/_Project/Models/Generated";
            const string path = folder + "/SubHull.asset";
            const int around = 40;
            System.IO.Directory.CreateDirectory(folder);

            // Profile points (x, radius).
            var profile = new System.Collections.Generic.List<Vector2>();
            for (int i = 0; i <= 16; i++)
            {
                float k = i / 16f;                                  // 0 = tail tip, 1 = body start
                profile.Add(new Vector2(-8.9f + k * 3.3f, HullRadius * Mathf.Pow(Mathf.Sin(k * Mathf.PI * 0.5f), 0.8f)));
            }
            for (int i = 1; i <= 8; i++) profile.Add(new Vector2(-5.6f + i * 1.4f, HullRadius));
            for (int i = 1; i <= 14; i++)
            {
                float k = i / 14f;                                  // 0 = body end, 1 = nose tip
                profile.Add(new Vector2(5.6f + k * 2.2f, HullRadius * Mathf.Sqrt(1f - k * k)));
            }

            int ring = around + 1; // duplicate seam vertex for clean UVs
            var vertices = new Vector3[profile.Count * ring];
            var uv = new Vector2[vertices.Length];
            for (int i = 0; i < profile.Count; i++)
            for (int j = 0; j < ring; j++)
            {
                float a = j / (float)around * Mathf.PI * 2f;
                Vector2 p = profile[i];
                vertices[i * ring + j] = new Vector3(p.x, HullY + Mathf.Cos(a) * p.y, Mathf.Sin(a) * p.y);
                uv[i * ring + j] = new Vector2(j / (float)around * 12f, (p.x + 8.9f) / 1.3f);
            }
            var triangles = new int[(profile.Count - 1) * around * 6];
            int n = 0;
            for (int i = 0; i < profile.Count - 1; i++)
            for (int j = 0; j < around; j++)
            {
                int a = i * ring + j, b = a + 1, c = a + ring, d = c + 1;
                triangles[n++] = a; triangles[n++] = b; triangles[n++] = c;
                triangles[n++] = b; triangles[n++] = d; triangles[n++] = c;
            }

            AssetDatabase.DeleteAsset(path);
            var mesh = new Mesh { name = "SubHull", vertices = vertices, uv = uv, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static void BuildSeafloor(Transform t)
        {
            var silt = Mat("Env_Silt", ProceduralTextures.Silt(), Color.white, smoothness: 0.05f);
            var rock = Mat("Env_Rock", ProceduralTextures.Rock(), Color.white, smoothness: 0.15f);
            rock.SetTextureScale("_BaseMap", new Vector2(3, 2));
            var snow = ParticleMat("P_MarineSnow", false);

            // Faint blue light filtering down from far above, just enough to read silhouettes.
            var down = new GameObject("DownwellingLight", typeof(Light));
            down.transform.SetParent(t, false);
            down.transform.localRotation = Quaternion.Euler(80f, 30f, 0f);
            var downLight = down.GetComponent<Light>();
            downLight.type = LightType.Directional;
            downLight.color = new Color(0.35f, 0.55f, 0.65f);
            downLight.intensity = 0.55f;
            downLight.shadows = LightShadows.Hard; // the hull/ceiling keep it out of the cabin

            BuildPostProcessing(t);

            var floorGo = Box("Seafloor", t, new Vector3(0, SeafloorY - 0.5f, 0), new Vector3(160f, 1f, 160f), silt);
            Assign(floorGo.GetComponent<WorldUVBox>(), "tilesPerMeter", 0.5f);

            // Scattered boulders (none directly under the sub).
            var rng = new System.Random(5);
            float R(float min, float max) => min + (float)rng.NextDouble() * (max - min);
            for (int i = 0; i < 45; i++)
            {
                float angle = R(0, Mathf.PI * 2), dist = R(9f, 50f);
                var pos = new Vector3(Mathf.Cos(angle) * dist, SeafloorY, Mathf.Sin(angle) * dist);
                var scale = new Vector3(R(1.5f, 6f), R(0.8f, 3.5f), R(1.5f, 6f));
                var boulder = Primitive(PrimitiveType.Sphere, "Boulder", t, pos + Vector3.up * scale.y * 0.2f, scale, rock, collider: true);
                boulder.transform.localRotation = Quaternion.Euler(R(-15, 15), R(0, 360), R(-15, 15));
            }

            // Trench walls on both sides.
            foreach (float side in new[] { -1f, 1f })
            {
                for (int i = 0; i < 9; i++)
                {
                    var pos = new Vector3(-50f + i * 12.5f + R(-3, 3), SeafloorY + R(6, 12), side * R(26f, 34f));
                    var wall = Box("TrenchWall", t, pos, new Vector3(R(12, 18), R(30, 45), R(8, 14)), rock);
                    wall.transform.localRotation = Quaternion.Euler(side * R(8, 20), R(-25, 25), R(-10, 10));
                    Assign(wall.GetComponent<WorldUVBox>(), "tilesPerMeter", 0.35f);
                }
            }

            // Something on the seabed that shouldn't be there: an old dive helmet and a snapped tether.
            var helmetMat = Mat("Env_OldBrass", null, new Color(0.3f, 0.26f, 0.15f), metallic: 0.6f, smoothness: 0.2f);
            Sphere("OldDiveHelmet", t, new Vector3(9f, SeafloorY + 0.15f, 7f), 0.4f, helmetMat);
            Vector3 p = new Vector3(9.4f, SeafloorY + 0.04f, 7.2f);
            for (int i = 0; i < 7; i++)
            {
                Vector3 next = p + new Vector3(R(0.6f, 1.1f), 0, R(-0.6f, 0.6f));
                var seg = Cylinder("OldTether", t, (p + next) * 0.5f, new Vector3(0.05f, (next - p).magnitude * 0.5f, 0.05f), rope);
                seg.transform.localRotation = Quaternion.FromToRotation(Vector3.up, next - p);
                p = next;
            }

            // Marine snow drifting around the sub (lit, so it glitters only in the headlights).
            var particles = Particles("MarineSnow", t, new Vector3(0, HullY, 0), snow);
            var main = particles.main;
            main.startLifetime = 25f;
            main.startSpeed = 0.05f;
            main.gravityModifier = 0.003f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
            main.startColor = new Color(0.8f, 0.85f, 0.8f);
            main.maxParticles = 1800;
            var emission = particles.emission;
            emission.rateOverTime = 70f;
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 24f;
            shape.radiusThickness = 0.7f; // emit in a shell, so none spawn inside the cabin
        }

        /// <summary>
        /// A small camera housing with a lens and red tally light, plus the (disabled) Camera itself,
        /// aimed at <paramref name="lookAt"/>. Cameras without nearby sub lights get their own lamp.
        /// </summary>
        static void HullCamera(Transform t, Material housing, int number, string label, Vector3 pos, Vector3 lookAt, bool light)
        {
            Quaternion rot = Quaternion.LookRotation(lookAt - pos);
            var rig = Group($"Cam{number:00}_{label}", t);
            rig.localPosition = pos;
            rig.localRotation = rot;

            Box("Housing", rig, new Vector3(0, 0, -0.1f), new Vector3(0.16f, 0.13f, 0.26f), housing, collider: false, worldUV: false);
            Box("Mount", rig, new Vector3(0, -0.1f, -0.12f), new Vector3(0.05f, 0.1f, 0.05f), housing, collider: false, worldUV: false);
            Cylinder("Lens", rig, new Vector3(0, 0, 0.04f), new Vector3(0.1f, 0.02f, 0.1f), glassDark).transform.localRotation = Quaternion.Euler(90, 0, 0);
            var tally = Sphere("TallyLight", rig, new Vector3(0.05f, 0.05f, 0.03f), 0.025f, lampRed);

            var camGo = new GameObject("Camera", typeof(Camera));
            camGo.transform.SetParent(rig, false);
            camGo.transform.localPosition = new Vector3(0, 0, 0.07f);
            var cam = camGo.GetComponent<Camera>();
            cam.enabled = false;
            cam.fieldOfView = 78f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 60f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = RenderSettings.fogColor;
            var subCam = camGo.AddComponent<SubCamera>();
            Assign(subCam, "number", number);
            Assign(subCam, "label", label);
            Assign(subCam, "tallyLight", tally.GetComponent<Renderer>());

            if (light)
                SpotLight("CamLight", rig, new Vector3(0, 0.1f, 0f), Quaternion.identity, new Color(0.85f, 0.92f, 1f), 22f, 20f, 60f);
        }

        static void SpotLight(string name, Transform parent, Vector3 pos, Quaternion rot, Color color, float intensity, float range, float angle)
        {
            var light = PointLight(name, parent, pos, color, intensity, range, false);
            light.type = LightType.Spot;
            light.spotAngle = angle;
            light.innerSpotAngle = angle * 0.5f;
            light.transform.localRotation = rot;
        }

        /// <summary>Glow on bright lights, darkened screen edges and a little film grain.</summary>
        static void BuildPostProcessing(Transform t)
        {
            // Recreated from scratch each build so its settings always match this code.
            const string path = "Assets/_Project/ScriptableObjects/PP_Submarine.asset";
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(0.8f);
            bloom.intensity.Override(0.9f);
            bloom.scatter.Override(0.6f);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.38f);
            vignette.smoothness.Override(0.5f);
            // Brightness setting drives post exposure at runtime (see SettingsRuntime).
            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0f);
            var grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Thin2);
            grain.intensity.Override(0.3f);
            foreach (var component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
            EditorUtility.SetDirty(profile);

            var volumeGo = new GameObject("PostProcessing", typeof(Volume));
            volumeGo.transform.SetParent(t, false);
            var volume = volumeGo.GetComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
        }

        /// <summary>Orbit camera around the sub: title-menu backdrop, and the V-key exterior view in game.</summary>
        static ExteriorPreviewCamera BuildDevTools(Transform sub)
        {
            var center = Group("OrbitCenter", sub);
            center.localPosition = new Vector3(-0.5f, HullY, 0);

            var rig = new GameObject("ExteriorPreviewCamera", typeof(Camera), typeof(AudioListener));
            var cam = rig.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = RenderSettings.fogColor;
            cam.farClipPlane = 200f;
            cam.fieldOfView = 60f;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            var pixel = rig.AddComponent<PixelatedCamera>();
            Assign(pixel, "screenMaterial", RetroScreenMaterial());

            var tools = new GameObject("DevTools");
            var preview = tools.AddComponent<ExteriorPreviewCamera>();
            Assign(preview, "previewRig", rig);
            Assign(preview, "orbitCenter", center);
            return preview;
        }
    }
}
