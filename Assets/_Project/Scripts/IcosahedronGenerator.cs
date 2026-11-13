using System.Collections.Generic;
using UnityEngine;

namespace LuckyNumbers.PhysicsPrototype
{
    /// <summary>
    /// Generates and manages the icosahedron (DnD d20) geometry, visual mesh, exterior convex collider,
    /// 20 seamless interior convex prism colliders, edge frames, and face numbers.
    /// </summary>
    public class IcosahedronGenerator : MonoBehaviour
    {
        [Header("Dimensions")]
        [SerializeField] private float innerRadius = 8.0f;
        [SerializeField] private float wallThickness = 0.5f;

        [Header("Materials")]
        [SerializeField] private Material interiorMaterial;
        [SerializeField] private Material exteriorMaterial;
        [SerializeField] private Material edgeMaterial;
        [SerializeField] private PhysicsMaterial interiorPhysicMaterial;
        [SerializeField] private PhysicsMaterial exteriorPhysicMaterial;
        [Header("Exterior Collision")]
        [SerializeField] private bool useSphericalExteriorCollider = true;
        [SerializeField] private float sphericalColliderRadius = 6.424f;

        [Header("DnD Visual Features")]
        [SerializeField] private bool showDndNumbers = true;
        [SerializeField] private bool showEdgeFrames = true;

        [Header("Layers")]
        [SerializeField] private string exteriorLayerName = "ExteriorShell";
        [SerializeField] private string interiorLayerName = "InteriorSurface";

        public float InnerRadius => innerRadius;
        public float WallThickness => wallThickness;
        public float FaceCenterDistance => innerRadius * 0.7557613f;

        private void Awake()
        {
            // Build geometry if not already populated or if DnD features are missing
            Transform interiorParent = transform.Find("InteriorColliders");
            Transform dndNumbers = transform.Find("DndFaceNumbers");
            if (interiorParent == null || interiorParent.childCount == 0 || dndNumbers == null)
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

            // 8. Setup DnD edge frames (golden/dark inked edge lines)
            SetupEdgeFrames(unitVertices, faces);

            // 9. Setup DnD face numbers (1 to 20, opposite faces sum to 21)
            SetupDndNumbers(unitVertices, faces);
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
                    int temp = faces[i][1];
                    faces[i][1] = faces[i][2];
                    faces[i][2] = temp;
                }
            }
        }

        private static void AlignBottomFace(Vector3[] unitVertices, int[][] faces)
        {
            Vector3 v0 = unitVertices[faces[0][0]];
            Vector3 v1 = unitVertices[faces[0][1]];
            Vector3 v2 = unitVertices[faces[0][2]];
            Vector3 faceCenter = ((v0 + v1 + v2) / 3f).normalized;

            Quaternion alignment = Quaternion.FromToRotation(faceCenter, Vector3.down);
            for (int i = 0; i < unitVertices.Length; i++)
            {
                unitVertices[i] = alignment * unitVertices[i];
            }
        }

        /// <summary>
        /// Computes the 20 local outward face unit normals and their corresponding DnD face numbers (1-20).
        /// </summary>
        public static (Vector3[] normals, int[] numbers) ComputeCanonicalFaceData()
        {
            GetIcosahedronGeometry(out Vector3[] unitVertices, out int[][] faces);
            AlignBottomFace(unitVertices, faces);

            Vector3[] normals = new Vector3[20];
            for (int i = 0; i < 20; i++)
            {
                Vector3 v0 = unitVertices[faces[i][0]];
                Vector3 v1 = unitVertices[faces[i][1]];
                Vector3 v2 = unitVertices[faces[i][2]];
                normals[i] = ((v0 + v1 + v2) / 3f).normalized;
            }

            int[] numbers = AssignDndFaceNumbers(unitVertices, faces);
            return (normals, numbers);
        }

        /// <summary>
        /// Computes the 20 local outward face unit normals aligned with the canonical bottom floor.
        /// </summary>
        public static Vector3[] ComputeCanonicalFaceNormals()
        {
            return ComputeCanonicalFaceData().normals;
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

            Vector3[] vertices = new Vector3[120];
            Vector3[] normals = new Vector3[120];
            Vector2[] uvs = new Vector2[120];

            int[] interiorIndices = new int[60];
            int[] exteriorIndices = new int[60];

            // 1. Interior faces
            for (int f = 0; f < 20; f++)
            {
                int baseVert = f * 3;
                Vector3 p0 = unitVertices[faces[f][0]] * rIn;
                Vector3 p1 = unitVertices[faces[f][1]] * rIn;
                Vector3 p2 = unitVertices[faces[f][2]] * rIn;

                Vector3 faceNormal = -Vector3.Cross(p1 - p0, p2 - p0).normalized;

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

            // 2. Exterior faces
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

            PhysicsMaterial extMat = exteriorPhysicMaterial != null ? exteriorPhysicMaterial : interiorPhysicMaterial;

            if (useSphericalExteriorCollider)
            {
                SphereCollider sc = exteriorObj.AddComponent<SphereCollider>();
                sc.radius = sphericalColliderRadius > 0.01f ? sphericalColliderRadius : (innerRadius * 0.7557613f + wallThickness);
                if (extMat != null)
                {
                    sc.material = extMat;
                }
            }
            else
            {
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
                if (extMat != null)
                {
                    mc.material = extMat;
                }
            }
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

                Vector3 faceCenter = (u0 + u1 + u2) / 3f;
                Vector3 normal = faceCenter.normalized;
                Vector3 innerCenter = faceCenter * rIn;

                // Exact vertex positions ensure adjacent plates meet seamlessly with zero gaps, ridges, or overlapping lips
                Vector3 v0 = u0 * rIn;
                Vector3 v1 = u1 * rIn;
                Vector3 v2 = u2 * rIn;

                // Thin triangular plate extruded outward into the wall thickness (0.12m).
                // Stays 100% inside the 0.5m wall without protruding through the exterior shell.
                float plateThickness = Mathf.Min(0.12f, wallThickness * 0.25f);
                Vector3 v3 = v0 + normal * plateThickness;
                Vector3 v4 = v1 + normal * plateThickness;
                Vector3 v5 = v2 + normal * plateThickness;

                Vector3[] prismVerts = new Vector3[] { v0, v1, v2, v3, v4, v5 };

                int[] prismTriangles = new int[]
                {
                    0, 2, 1,
                    3, 4, 5,
                    0, 1, 4,
                    0, 4, 3,
                    1, 2, 5,
                    1, 5, 4,
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

        private void SetupEdgeFrames(Vector3[] unitVertices, int[][] faces)
        {
            if (!showEdgeFrames) return;

            GameObject edgeObj = new GameObject("DndEdgeFrames");
            edgeObj.transform.SetParent(transform, false);

            float rOut = innerRadius + wallThickness + 0.01f;
            float ribbonWidth = 0.12f;

            HashSet<long> uniqueEdges = new HashSet<long>();
            List<Vector3> verts = new List<Vector3>();
            List<Vector3> norms = new List<Vector3>();
            List<int> tris = new List<int>();

            for (int f = 0; f < 20; f++)
            {
                int[] face = faces[f];
                for (int e = 0; e < 3; e++)
                {
                    int iA = face[e];
                    int iB = face[(e + 1) % 3];
                    int minI = Mathf.Min(iA, iB);
                    int maxI = Mathf.Max(iA, iB);
                    long key = ((long)minI << 32) | (uint)maxI;

                    if (!uniqueEdges.Add(key)) continue;

                    Vector3 pA = unitVertices[minI] * rOut;
                    Vector3 pB = unitVertices[maxI] * rOut;
                    Vector3 midNorm = ((unitVertices[minI] + unitVertices[maxI]) * 0.5f).normalized;
                    Vector3 edgeDir = pB - pA;
                    Vector3 sideDir = Vector3.Cross(edgeDir, midNorm).normalized * (ribbonWidth * 0.5f);

                    int baseIdx = verts.Count;
                    verts.Add(pA - sideDir);
                    verts.Add(pA + sideDir);
                    verts.Add(pB + sideDir);
                    verts.Add(pB - sideDir);

                    norms.Add(midNorm);
                    norms.Add(midNorm);
                    norms.Add(midNorm);
                    norms.Add(midNorm);

                    tris.Add(baseIdx + 0);
                    tris.Add(baseIdx + 1);
                    tris.Add(baseIdx + 2);

                    tris.Add(baseIdx + 0);
                    tris.Add(baseIdx + 2);
                    tris.Add(baseIdx + 3);
                }
            }

            Mesh edgeMesh = new Mesh();
            edgeMesh.name = "DndEdgeMesh";
            edgeMesh.vertices = verts.ToArray();
            edgeMesh.normals = norms.ToArray();
            edgeMesh.triangles = tris.ToArray();
            edgeMesh.RecalculateBounds();

            MeshFilter mf = edgeObj.AddComponent<MeshFilter>();
            mf.sharedMesh = edgeMesh;

            MeshRenderer mr = edgeObj.AddComponent<MeshRenderer>();
            mr.sharedMaterial = edgeMaterial != null ? edgeMaterial : exteriorMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void SetupDndNumbers(Vector3[] unitVertices, int[][] faces)
        {
            if (!showDndNumbers) return;

            GameObject numbersParent = new GameObject("DndFaceNumbers");
            numbersParent.transform.SetParent(transform, false);

            float rOut = innerRadius + wallThickness + 0.03f;
            int[] faceNumbers = AssignDndFaceNumbers(unitVertices, faces);

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

            for (int f = 0; f < 20; f++)
            {
                int[] face = faces[f];
                Vector3 u0 = unitVertices[face[0]];
                Vector3 u1 = unitVertices[face[1]];
                Vector3 u2 = unitVertices[face[2]];

                Vector3 normal = ((u0 + u1 + u2) / 3f).normalized;
                Vector3 faceCenter = ((u0 + u1 + u2) / 3f) * rOut;
                Vector3 pos = faceCenter + normal * 0.02f;

                Vector3 vUp = u0 - faceCenter;
                Vector3 projUp = Vector3.ProjectOnPlane(vUp, faceCenter).normalized;

                GameObject numObj = new GameObject($"Number_{faceNumbers[f]}");
                numObj.transform.SetParent(numbersParent.transform, false);
                numObj.transform.localPosition = pos;
                numObj.transform.localRotation = Quaternion.LookRotation(-faceCenter, projUp);

                TextMesh tm = numObj.AddComponent<TextMesh>();
                tm.text = faceNumbers[f].ToString();
                tm.fontSize = 54;
                tm.characterSize = 0.08f;
                tm.alignment = TextAlignment.Center;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.color = new Color(0.95f, 0.95f, 0.95f, 0.9f);
                if (font != null) tm.font = font;

                MeshRenderer mr = numObj.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
        }

        public static int[] AssignDndFaceNumbers(Vector3[] unitVertices, int[][] faces)
        {
            int[] result = new int[20];
            Vector3[] faceCenters = new Vector3[20];
            for (int i = 0; i < 20; i++)
            {
                faceCenters[i] = ((unitVertices[faces[i][0]] + unitVertices[faces[i][1]] + unitVertices[faces[i][2]]) / 3f).normalized;
            }

            bool[] paired = new bool[20];
            List<(int f1, int f2)> pairs = new List<(int f1, int f2)>();

            for (int i = 0; i < 20; i++)
            {
                if (paired[i]) continue;
                int bestOpposite = -1;
                float minDot = 1f;
                for (int j = i + 1; j < 20; j++)
                {
                    if (paired[j]) continue;
                    float dot = Vector3.Dot(faceCenters[i], faceCenters[j]);
                    if (dot < minDot)
                    {
                        minDot = dot;
                        bestOpposite = j;
                    }
                }
                if (bestOpposite != -1)
                {
                    paired[i] = true;
                    paired[bestOpposite] = true;
                    pairs.Add((i, bestOpposite));
                }
            }

            pairs.Sort((a, b) =>
            {
                float maxY_a = Mathf.Max(faceCenters[a.f1].y, faceCenters[a.f2].y);
                float maxY_b = Mathf.Max(faceCenters[b.f1].y, faceCenters[b.f2].y);
                return maxY_b.CompareTo(maxY_a);
            });

            // Standard DnD d20 pairings sum to 21
            int high = 20;
            int low = 1;
            foreach (var pair in pairs)
            {
                if (faceCenters[pair.f1].y >= faceCenters[pair.f2].y)
                {
                    result[pair.f1] = high;
                    result[pair.f2] = low;
                }
                else
                {
                    result[pair.f2] = high;
                    result[pair.f1] = low;
                }
                high--;
                low++;
            }

            return result;
        }
    }
}
