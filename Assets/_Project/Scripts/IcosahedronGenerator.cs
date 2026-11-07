using System.Collections.Generic;
using UnityEngine;

namespace LuckyNumbers.PhysicsPrototype
{
    /// <summary>
    /// Generates and manages the icosahedron geometry, visual mesh, exterior convex collider,
    /// and the 20 seamless interior convex prism colliders.
    /// </summary>
    public class IcosahedronGenerator : MonoBehaviour
    {
        [Header("Dimensions")]
        [SerializeField] private float innerRadius = 8.0f;
        [SerializeField] private float wallThickness = 0.5f;

        [Header("Materials")]
        [SerializeField] private Material interiorMaterial;
        [SerializeField] private Material exteriorMaterial;
        [SerializeField] private PhysicsMaterial interiorPhysicMaterial;

        [Header("Layers")]
        [SerializeField] private string exteriorLayerName = "ExteriorShell";
        [SerializeField] private string interiorLayerName = "InteriorSurface";

        public float InnerRadius => innerRadius;
        public float WallThickness => wallThickness;
        public float FaceCenterDistance => innerRadius * 0.7557613f;

        private void Awake()
        {
            // Build geometry if not already populated in the scene
            Transform interiorParent = transform.Find("InteriorColliders");
            if (interiorParent == null || interiorParent.childCount == 0)
            {
                BuildIcosahedron();
            }
        }

        [ContextMenu("Rebuild Icosahedron")]
        public void BuildIcosahedron()
        {
            // 1. Clear existing children
            ClearExistingChildren();

            // 2. Compute canonical regular icosahedron vertices & faces
            Vector3[] unitVertices;
            int[][] faces;
            GetIcosahedronGeometry(out unitVertices, out faces);

            // 3. Align face 0 to be the flat bottom floor (outward normal pointing straight down)
            AlignBottomFace(unitVertices, faces);

            // 4. Generate visual mesh (both inward and outward faces)
            SetupVisualMesh(unitVertices, faces);

            // 5. Generate exterior convex MeshCollider (Layer: ExteriorShell)
            SetupExteriorCollider(unitVertices, faces);

            // 6. Generate 20 interior convex prism colliders (Layer: InteriorSurface)
            SetupInteriorColliders(unitVertices, faces);

            // 7. Setup interior point light for clear visibility
            SetupInteriorLight();
        }

        private void SetupInteriorLight()
        {
            GameObject lightObj = new GameObject("InteriorLight");
            lightObj.transform.SetParent(transform, false);
            lightObj.transform.localPosition = Vector3.zero;

            Light light = lightObj.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = innerRadius * 2.5f;
            light.intensity = 1.8f;
            light.color = new Color(1.0f, 0.98f, 0.92f);
        }

        private void ClearExistingChildren()
        {
            List<GameObject> childrenToDestroy = new List<GameObject>();
            for (int i = 0; i < transform.childCount; i++)
            {
                childrenToDestroy.Add(transform.GetChild(i).gameObject);
            }

            foreach (GameObject child in childrenToDestroy)
            {
                if (Application.isPlaying)
                {
                    Destroy(child);
                }
                else
                {
                    DestroyImmediate(child);
                }
            }
        }

        private static void GetIcosahedronGeometry(out Vector3[] unitVertices, out int[][] faces)
        {
            float phi = (1.0f + Mathf.Sqrt(5.0f)) * 0.5f;

            Vector3[] rawVertices = new Vector3[]
            {
                new Vector3(-1f,  phi,  0f),
                new Vector3( 1f,  phi,  0f),
                new Vector3(-1f, -phi,  0f),
                new Vector3( 1f, -phi,  0f),
                new Vector3( 0f, -1f,  phi),
                new Vector3( 0f,  1f,  phi),
                new Vector3( 0f, -1f, -phi),
                new Vector3( 0f,  1f, -phi),
                new Vector3( phi,  0f, -1f),
                new Vector3( phi,  0f,  1f),
                new Vector3(-phi,  0f, -1f),
                new Vector3(-phi,  0f,  1f)
            };

            unitVertices = new Vector3[12];
            for (int i = 0; i < 12; i++)
            {
                unitVertices[i] = rawVertices[i].normalized;
            }

            faces = new int[][]
            {
                new int[] { 0, 11, 5 },
                new int[] { 0, 5, 1 },
                new int[] { 0, 1, 7 },
                new int[] { 0, 7, 10 },
                new int[] { 0, 10, 11 },

                new int[] { 1, 5, 9 },
                new int[] { 5, 11, 4 },
                new int[] { 11, 10, 2 },
                new int[] { 10, 7, 6 },
                new int[] { 7, 1, 8 },

                new int[] { 3, 9, 4 },
                new int[] { 3, 4, 2 },
                new int[] { 3, 2, 6 },
                new int[] { 3, 6, 8 },
                new int[] { 3, 8, 9 },

                new int[] { 4, 9, 5 },
                new int[] { 2, 4, 11 },
                new int[] { 6, 2, 10 },
                new int[] { 8, 6, 7 },
                new int[] { 9, 8, 1 }
            };

            // Ensure consistent outward winding order for each face
            for (int i = 0; i < 20; i++)
            {
                Vector3 v0 = unitVertices[faces[i][0]];
                Vector3 v1 = unitVertices[faces[i][1]];
                Vector3 v2 = unitVertices[faces[i][2]];
                Vector3 center = (v0 + v1 + v2) / 3f;
                Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0);
                if (Vector3.Dot(normal, center) < 0f)
                {
                    // Swap winding order to make it outward
                    int temp = faces[i][1];
                    faces[i][1] = faces[i][2];
                    faces[i][2] = temp;
                }
            }
        }

        private static void AlignBottomFace(Vector3[] unitVertices, int[][] faces)
        {
            // Face 0 center
            Vector3 v0 = unitVertices[faces[0][0]];
            Vector3 v1 = unitVertices[faces[0][1]];
            Vector3 v2 = unitVertices[faces[0][2]];
            Vector3 faceCenter = ((v0 + v1 + v2) / 3f).normalized;

            // Rotate so faceCenter points straight down (0, -1, 0)
            Quaternion alignment = Quaternion.FromToRotation(faceCenter, Vector3.down);
            for (int i = 0; i < unitVertices.Length; i++)
            {
                unitVertices[i] = alignment * unitVertices[i];
            }
        }

        private void SetupVisualMesh(Vector3[] unitVertices, int[][] faces)
        {
            MeshFilter filter = GetComponent<MeshFilter>();
            if (filter == null)
            {
                filter = gameObject.AddComponent<MeshFilter>();
            }

            MeshRenderer renderer = GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                renderer = gameObject.AddComponent<MeshRenderer>();
            }

            float rIn = innerRadius;
            float rOut = innerRadius + wallThickness;

            // 20 interior faces (60 verts) + 20 exterior faces (60 verts)
            Vector3[] vertices = new Vector3[120];
            Vector3[] normals = new Vector3[120];
            Vector2[] uvs = new Vector2[120];

            int[] interiorIndices = new int[60];
            int[] exteriorIndices = new int[60];

            // 1. Interior faces (winding CCW looking from inside, normals point inward)
            for (int f = 0; f < 20; f++)
            {
                int baseVert = f * 3;
                Vector3 p0 = unitVertices[faces[f][0]] * rIn;
                Vector3 p1 = unitVertices[faces[f][1]] * rIn;
                Vector3 p2 = unitVertices[faces[f][2]] * rIn;

                // Inward normal
                Vector3 faceNormal = -Vector3.Cross(p1 - p0, p2 - p0).normalized;

                // Winding order reversed (0, 2, 1) so it's visible from inside
                vertices[baseVert + 0] = p0;
                vertices[baseVert + 1] = p2;
                vertices[baseVert + 2] = p1;

                normals[baseVert + 0] = faceNormal;
                normals[baseVert + 1] = faceNormal;
                normals[baseVert + 2] = faceNormal;

                uvs[baseVert + 0] = new Vector2(0.5f, 1.0f);
                uvs[baseVert + 1] = new Vector2(0.0f, 0.0f);
                uvs[baseVert + 2] = new Vector2(1.0f, 0.0f);

                interiorIndices[f * 3 + 0] = baseVert + 0;
                interiorIndices[f * 3 + 1] = baseVert + 1;
                interiorIndices[f * 3 + 2] = baseVert + 2;
            }

            // 2. Exterior faces (winding CCW looking from outside, normals point outward)
            for (int f = 0; f < 20; f++)
            {
                int baseVert = 60 + f * 3;
                Vector3 p0 = unitVertices[faces[f][0]] * rOut;
                Vector3 p1 = unitVertices[faces[f][1]] * rOut;
                Vector3 p2 = unitVertices[faces[f][2]] * rOut;

                Vector3 faceNormal = Vector3.Cross(p1 - p0, p2 - p0).normalized;

                vertices[baseVert + 0] = p0;
                vertices[baseVert + 1] = p1;
                vertices[baseVert + 2] = p2;

                normals[baseVert + 0] = faceNormal;
                normals[baseVert + 1] = faceNormal;
                normals[baseVert + 2] = faceNormal;

                uvs[baseVert + 0] = new Vector2(0.5f, 1.0f);
                uvs[baseVert + 1] = new Vector2(0.0f, 0.0f);
                uvs[baseVert + 2] = new Vector2(1.0f, 0.0f);

                exteriorIndices[f * 3 + 0] = baseVert + 0;
                exteriorIndices[f * 3 + 1] = baseVert + 1;
                exteriorIndices[f * 3 + 2] = baseVert + 2;
            }

            Mesh visualMesh = new Mesh();
            visualMesh.name = "IcosahedronVisualMesh";
            visualMesh.subMeshCount = 2;
            visualMesh.vertices = vertices;
            visualMesh.normals = normals;
            visualMesh.uv = uvs;
            visualMesh.SetTriangles(interiorIndices, 0);
            visualMesh.SetTriangles(exteriorIndices, 1);
            visualMesh.RecalculateBounds();

            filter.sharedMesh = visualMesh;

            Material[] mats = new Material[2];
            mats[0] = interiorMaterial != null ? interiorMaterial : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mats[1] = exteriorMaterial != null ? exteriorMaterial : mats[0];
            renderer.sharedMaterials = mats;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void SetupExteriorCollider(Vector3[] unitVertices, int[][] faces)
        {
            GameObject exteriorObj = new GameObject("ExteriorCollider");
            exteriorObj.transform.SetParent(transform, false);

            int layer = LayerMask.NameToLayer(exteriorLayerName);
            if (layer < 0) layer = 9;
            exteriorObj.layer = layer;

            float rOut = innerRadius + wallThickness;
            Vector3[] verts = new Vector3[12];
            for (int i = 0; i < 12; i++)
            {
                verts[i] = unitVertices[i] * rOut;
            }

            int[] indices = new int[60];
            for (int f = 0; f < 20; f++)
            {
                indices[f * 3 + 0] = faces[f][0];
                indices[f * 3 + 1] = faces[f][1];
                indices[f * 3 + 2] = faces[f][2];
            }

            Mesh exteriorMesh = new Mesh();
            exteriorMesh.name = "IcosahedronExteriorMesh";
            exteriorMesh.vertices = verts;
            exteriorMesh.triangles = indices;
            exteriorMesh.RecalculateBounds();

            MeshCollider mc = exteriorObj.AddComponent<MeshCollider>();
            mc.sharedMesh = exteriorMesh;
            mc.convex = true;
        }

        private void SetupInteriorColliders(Vector3[] unitVertices, int[][] faces)
        {
            GameObject parentObj = new GameObject("InteriorColliders");
            parentObj.transform.SetParent(transform, false);

            int layer = LayerMask.NameToLayer(interiorLayerName);
            if (layer < 0) layer = 10;
            parentObj.layer = layer;

            float rIn = innerRadius;
            float rOut = innerRadius + wallThickness;

            for (int f = 0; f < 20; f++)
            {
                GameObject faceObj = new GameObject($"InteriorFace_{f:D2}");
                faceObj.transform.SetParent(parentObj.transform, false);
                faceObj.layer = layer;

                Vector3 u0 = unitVertices[faces[f][0]];
                Vector3 u1 = unitVertices[faces[f][1]];
                Vector3 u2 = unitVertices[faces[f][2]];

                // Inner vertices form the playable boundary surface
                Vector3 v0 = u0 * rIn;
                Vector3 v1 = u1 * rIn;
                Vector3 v2 = u2 * rIn;

                // Outer vertices form the back of the triangular prism towards the shell
                Vector3 v3 = u0 * rOut;
                Vector3 v4 = u1 * rOut;
                Vector3 v5 = u2 * rOut;

                Vector3[] prismVerts = new Vector3[] { v0, v1, v2, v3, v4, v5 };

                // 8 triangles forming a closed, watertight convex triangular prism
                int[] prismTriangles = new int[]
                {
                    // Inner face
                    0, 2, 1,
                    // Outer face
                    3, 4, 5,
                    // Side 0-1
                    0, 1, 4,
                    0, 4, 3,
                    // Side 1-2
                    1, 2, 5,
                    1, 5, 4,
                    // Side 2-0
                    2, 0, 3,
                    2, 3, 5
                };

                Mesh prismMesh = new Mesh();
                prismMesh.name = $"PrismMesh_Face_{f:D2}";
                prismMesh.vertices = prismVerts;
                prismMesh.triangles = prismTriangles;
                prismMesh.RecalculateBounds();

                MeshCollider mc = faceObj.AddComponent<MeshCollider>();
                mc.sharedMesh = prismMesh;
                mc.convex = true;

                if (interiorPhysicMaterial != null)
                {
                    mc.material = interiorPhysicMaterial;
                }
            }
        }
    }
}
