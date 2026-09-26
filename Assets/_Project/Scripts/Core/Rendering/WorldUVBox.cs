using UnityEngine;

namespace TheDeep.Core.Rendering
{
    /// <summary>
    /// Gives a scaled cube UVs in metres, so textures tile evenly instead of stretching
    /// (a 10m wall shows 10 panels, not one giant stretched one). The mesh is rebuilt on load.
    /// </summary>
    [ExecuteAlways, RequireComponent(typeof(MeshFilter))]
    public class WorldUVBox : MonoBehaviour
    {
        [SerializeField] float tilesPerMeter = 1f;

        static Mesh sourceCube;
        Mesh mesh;
        Vector3 builtScale;

        void OnEnable() => Rebuild();

        void Update()
        {
            if (!Application.isPlaying && transform.lossyScale != builtScale) Rebuild();
        }

        void OnDestroy()
        {
            if (mesh == null) return;
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
        }

        void Rebuild()
        {
            if (sourceCube == null) sourceCube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            if (mesh == null)
            {
                mesh = Instantiate(sourceCube);
                mesh.name = "WorldUVBox";
                mesh.hideFlags = HideFlags.DontSave;
            }

            builtScale = transform.lossyScale;
            Vector3 s = new Vector3(Mathf.Abs(builtScale.x), Mathf.Abs(builtScale.y), Mathf.Abs(builtScale.z)) * tilesPerMeter;
            Vector3[] vertices = sourceCube.vertices;
            Vector3[] normals = sourceCube.normals;
            var uv = new Vector2[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 p = vertices[i];
                Vector3 n = normals[i];
                if (Mathf.Abs(n.x) > 0.5f) uv[i] = new Vector2(p.z * s.z, p.y * s.y);
                else if (Mathf.Abs(n.y) > 0.5f) uv[i] = new Vector2(p.x * s.x, p.z * s.z);
                else uv[i] = new Vector2(p.x * s.x, p.y * s.y);
            }
            mesh.uv = uv;
            GetComponent<MeshFilter>().sharedMesh = mesh;
        }
    }
}
