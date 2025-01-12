#if UNITY_EDITOR
using System;
using System.Buffers;
using System.Collections.Generic;
using UnityEditor;
#endif

using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Pool;
using UnityEngine.Rendering.Universal;

namespace Sloane.FakeVolumetricLightGenerator
{
    [ExecuteAlways]
    public class FakeVolumetricLightGenerator : MonoBehaviour
    {
#if UNITY_EDITOR
        [Serializable]
        struct ConnectedSegment : IEquatable<ConnectedSegment>
        {
            public Vector2Int Start;
            public Vector2Int End;

            public bool Equals(ConnectedSegment other)
            {
                return Start == other.Start && End == other.End;
            }

            public override bool Equals(object obj)
            {
                return obj is ConnectedSegment other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(Start, End);
            }

            public static bool operator ==(ConnectedSegment left, ConnectedSegment right)
            {
                return left.Equals(right);
            }

            public static bool operator !=(ConnectedSegment left, ConnectedSegment right)
            {
                return !left.Equals(right);
            }
        }

        [Serializable]
        struct ConnectedSegmentSet
        {
            public BoundsInt BoundingBox;
            public List<ConnectedSegment> Segments;
        }
        const int BOUNDING_BOX_EXTEND = 8;

        [SerializeField]
        RenderTexture m_SourceTexture;
        [SerializeField]
        RenderTexture m_ConnectedComponentMap;
        [SerializeField]
        List<RenderTexture> m_FragmentSet;
        [SerializeField]
        Camera m_Camera;
        [SerializeField]
        Vector2 m_NearPlane = new Vector2(0.5f, 0.5f);
        [SerializeField, Min(1.0f)]
        float m_FarPlaneScale = 1.618f;
        [SerializeField]
        float m_CastDistance = 2.6179241f;
        [SerializeField]
        int m_CastResolution = 512;
        [SerializeField]
        Mesh m_Mesh;
        [SerializeField, HideInInspector]
        UniversalRenderPipelineAsset m_RenderPipelineAsset;
        [SerializeField, HideInInspector]
        ComputeShader m_ConnectedComponentMapComputeShader;
        [SerializeField]
        List<ConnectedSegmentSet> m_OutputConnectedSegmentSets = new List<ConnectedSegmentSet>();
        [SerializeField, Range(0.0f, 1.0f)]
        float m_Tolerance = 0.2f;

        [SerializeField, HideInInspector]
        List<Vector3> m_Vertices = new List<Vector3>();
        [SerializeField, HideInInspector]
        List<int> m_Indices = new List<int>();

        MeshFilter m_MeshFilter;
        MeshRenderer m_MeshRenderer;

        Vector2 m_FarPlane => new Vector2(m_NearPlane.x * m_FarPlaneScale, m_NearPlane.y * m_FarPlaneScale);

        int SourceWidth => Mathf.Min(m_NearPlane.x, m_NearPlane.y) == m_NearPlane.x ? m_CastResolution : (int)(m_CastResolution * m_NearPlane.x / m_NearPlane.y);
        int SourceHeight => Mathf.Min(m_NearPlane.x, m_NearPlane.y) == m_NearPlane.y ? m_CastResolution : (int)(m_CastResolution * m_NearPlane.y / m_NearPlane.x);

        RenderTextureDescriptor SourceTextureDescriptor => new RenderTextureDescriptor(SourceWidth, SourceHeight, RenderTextureFormat.Default, 32)
        {
            volumeDepth = 1,
            msaaSamples = 1,
            enableRandomWrite = true,
            dimension = UnityEngine.Rendering.TextureDimension.Tex2D,
        };

        RenderTextureDescriptor ConnectedComponentMapDescriptor => GetConnectedComponentMapDescriptor(SourceWidth, SourceHeight);

        struct VertexData
        {
            public Vector3 position;
        }

        void InitializeGenerator()
        {
            InitializeRenderTexture();
            InitializeCamera();

            if (m_MeshFilter == null)
            {
                m_MeshFilter = GetComponent<MeshFilter>();
                if (m_MeshFilter == null)
                {
                    m_MeshFilter = gameObject.AddComponent<MeshFilter>();
                }
            }

            m_MeshRenderer = GetComponent<MeshRenderer>();
            if (m_MeshRenderer == null)
            {
                m_MeshRenderer = gameObject.AddComponent<MeshRenderer>();
            }
        }

        void InitializeRenderTexture()
        {
            CleanUpRenderTexture();

            m_OutputConnectedSegmentSets.Clear();

            m_SourceTexture = new RenderTexture(SourceTextureDescriptor);
            m_SourceTexture.name = "FakeVolumetricLightDepthTexture";
            m_SourceTexture.hideFlags = HideFlags.DontSave;
            m_SourceTexture.Create();

            m_ConnectedComponentMap = new RenderTexture(ConnectedComponentMapDescriptor);
            m_ConnectedComponentMap.name = "FakeVolumetricLightConnectedComponentMap";
            m_ConnectedComponentMap.hideFlags = HideFlags.DontSave;
            m_ConnectedComponentMap.Create();
        }

        void InitializeCamera()
        {
            if (m_Camera == null)
            {
                var go = new GameObject("FakeVolumetricLightCamera");
                go.hideFlags = HideFlags.DontSave;
                go.transform.SetParent(transform);
                m_Camera = go.AddComponent<Camera>();
            }

            if (m_FarPlaneScale <= 1.0f)
            {
                m_Camera.orthographic = true;
                m_Camera.orthographicSize = m_NearPlane.y * 0.5f;
                m_Camera.nearClipPlane = 0.0f;
                m_Camera.farClipPlane = m_CastDistance;
                m_Camera.transform.localPosition = Vector3.zero;
                m_Camera.transform.localRotation = Quaternion.identity;
            }
            else
            {
                m_Camera.orthographic = false;
                float nearPlaneDistance = m_CastDistance / (m_FarPlaneScale - 1.0f);
                m_Camera.fieldOfView = 2.0f * Mathf.Atan2(Mathf.Max(m_NearPlane.x, m_NearPlane.y) / 2.0f, nearPlaneDistance) * Mathf.Rad2Deg;
                m_Camera.aspect = m_NearPlane.x / m_NearPlane.y;
                m_Camera.nearClipPlane = nearPlaneDistance;
                m_Camera.farClipPlane = m_CastDistance + nearPlaneDistance;
                m_Camera.transform.localPosition = new Vector3(0, 0, -nearPlaneDistance);
                m_Camera.transform.localRotation = Quaternion.identity;
            }

            m_Camera.targetTexture = m_SourceTexture;
            m_Camera.backgroundColor = new Color(0.0f, 0.0f, 0.0f, 0.0f);
            m_Camera.clearFlags = CameraClearFlags.SolidColor;

            m_Camera.gameObject.SetActive(false);
        }

        void CleanUpRenderTexture()
        {
            if (m_Camera != null && m_Camera.targetTexture != null)
            {
                m_Camera.targetTexture = null;
            }

            if (m_SourceTexture != null)
            {
                m_SourceTexture.Release();
                DestroyImmediate(m_SourceTexture);
                m_SourceTexture = null;
            }

            if (m_ConnectedComponentMap != null)
            {
                m_ConnectedComponentMap.Release();
                DestroyImmediate(m_ConnectedComponentMap);
                m_ConnectedComponentMap = null;
            }

            for (int i = 0; i < m_FragmentSet.Count; i++)
            {
                if (m_FragmentSet[i] != null)
                {
                    m_FragmentSet[i].Release();
                    DestroyImmediate(m_FragmentSet[i]);
                }
            }
            m_FragmentSet.Clear();
        }

        void CleanUp()
        {
            CleanUpRenderTexture();

            if (m_Camera != null)
            {
                DestroyImmediate(m_Camera.gameObject);
                m_Camera = null;
            }

            CleanUpMesh();
        }

        void CleanUpMesh()
        {
            if (m_Mesh != null)
            {
                DestroyImmediate(m_Mesh);
                m_Mesh = null;
            }
        }

        void OnDestroy()
        {
            CleanUp();
        }

        public void Generate()
        {
            InitializeGenerator();

            m_MeshRenderer.enabled = false;

            var pipelineCache = QualitySettings.renderPipeline;
            QualitySettings.renderPipeline = m_RenderPipelineAsset;

            m_Camera.Render();

            QualitySettings.renderPipeline = pipelineCache;

            CleanUpMesh();
            GenerateConnectedComponentMap();
            CleanUpConnectedSegmentSet(m_Tolerance);
            GenerateVertexData();

            if (m_Mesh == null)
            {
                m_Mesh = new Mesh();
            }

            m_Mesh.name = "FakeVolumetricLightMesh";
            m_Mesh.SetVertices(m_Vertices);
            m_Mesh.SetIndices(m_Indices, MeshTopology.Triangles, 0);

            m_MeshFilter.mesh = m_Mesh;

            m_MeshRenderer.enabled = true;
        }

        RenderTextureDescriptor GetConnectedComponentMapDescriptor(int width, int height)
        {
            return new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB64, 32)
            {
                volumeDepth = 1,
                msaaSamples = 1,
                enableRandomWrite = true,
                dimension = UnityEngine.Rendering.TextureDimension.Tex2D,
            };
        }

        void GenerateConnectedComponentMap()
        {
            if (m_ConnectedComponentMapComputeShader == null)
                return;

            int width = m_SourceTexture.width;
            int height = m_SourceTexture.height;

            // 初始化连通域图
            int kernelHandle = m_ConnectedComponentMapComputeShader.FindKernel("Initialize");
            m_ConnectedComponentMapComputeShader.SetTexture(kernelHandle, "_SourceTexture", m_SourceTexture);
            m_ConnectedComponentMapComputeShader.SetTexture(kernelHandle, "_ConnectedComponentMap", m_ConnectedComponentMap);
            m_ConnectedComponentMapComputeShader.SetInt("_Width", width);
            m_ConnectedComponentMapComputeShader.SetInt("_Height", height);
            m_ConnectedComponentMapComputeShader.Dispatch(kernelHandle, Mathf.CeilToInt(width / 8.0f), Mathf.CeilToInt(height / 8.0f), 1);

            // 填充
            RenderTexture temp = RenderTexture.GetTemporary(ConnectedComponentMapDescriptor);

            ComputeBuffer floodFlag = new ComputeBuffer(1, sizeof(int));
            bool flag = true;
            kernelHandle = m_ConnectedComponentMapComputeShader.FindKernel("FloodFill");
            m_ConnectedComponentMapComputeShader.SetTexture(kernelHandle, "_ConnectedComponentMap", m_ConnectedComponentMap);
            m_ConnectedComponentMapComputeShader.SetTexture(kernelHandle, "_PrevConnectedComponentMap", temp);
            m_ConnectedComponentMapComputeShader.SetInt("_Width", width);
            m_ConnectedComponentMapComputeShader.SetInt("_Height", height);
            m_ConnectedComponentMapComputeShader.SetBuffer(kernelHandle, "_FloodFlag", floodFlag);

            const int SAFE_ITERATION = 8192;
            var flags = ArrayPool<int>.Shared.Rent(1);
            int i = 0;

            for (; i < SAFE_ITERATION && flag; i++)
            {
                flags[0] = 0;
                floodFlag.SetData(flags, 0, 0, 1);
                Graphics.Blit(m_ConnectedComponentMap, temp);
                m_ConnectedComponentMapComputeShader.Dispatch(kernelHandle, Mathf.CeilToInt(width / 8.0f), Mathf.CeilToInt(height / 8.0f), 1);

                floodFlag.GetData(flags, 0, 0, 1);
                flag = flags[0] != 0;
            }

            // Debug.Log($"Iteration: {i}, Flag: {flag}");

            RenderTexture.ReleaseTemporary(temp);
            ArrayPool<int>.Shared.Return(flags);
            floodFlag.Release();

            var connectedComponentSet = DictionaryPool<Vector4, BoundsInt>.Get();
            GetUniqueColorInRenderTexture(m_ConnectedComponentMap, connectedComponentSet);
            foreach (var kv in connectedComponentSet)
            {
                if (kv.Key.z == 0.0f)
                {
                    continue;
                }

                Debug.Log($"{kv.Key} -> {kv.Value}");
                BoundsInt boundingBox = kv.Value;
                boundingBox.xMin = boundingBox.xMin - BOUNDING_BOX_EXTEND;
                boundingBox.yMin = boundingBox.yMin - BOUNDING_BOX_EXTEND;
                boundingBox.xMax = boundingBox.xMax + BOUNDING_BOX_EXTEND;
                boundingBox.yMax = boundingBox.yMax + BOUNDING_BOX_EXTEND;

                var fragment = new RenderTexture(GetConnectedComponentMapDescriptor(boundingBox.size.x, boundingBox.size.y));
                fragment.name = $"FakeVolumetricLightFragment_{kv.Key}";
                fragment.Create();
                m_FragmentSet.Add(fragment);

                kernelHandle = m_ConnectedComponentMapComputeShader.FindKernel("InitializeFragment");
                m_ConnectedComponentMapComputeShader.SetTexture(kernelHandle, "_ConnectedComponentMap", m_ConnectedComponentMap);
                m_ConnectedComponentMapComputeShader.SetTexture(kernelHandle, "_ConnectedComponentFragment", fragment);
                m_ConnectedComponentMapComputeShader.SetInt("_Width", width);
                m_ConnectedComponentMapComputeShader.SetInt("_Height", height);
                m_ConnectedComponentMapComputeShader.SetInt("_BoundingOffsetx", boundingBox.xMin);
                m_ConnectedComponentMapComputeShader.SetInt("_BoundingOffsety", boundingBox.yMin);
                m_ConnectedComponentMapComputeShader.SetInt("_BoundingWidth", boundingBox.size.x);
                m_ConnectedComponentMapComputeShader.SetInt("_BoundingHeight", boundingBox.size.y);
                m_ConnectedComponentMapComputeShader.SetVector("_FragmentIndex", kv.Key);

                m_ConnectedComponentMapComputeShader.Dispatch(kernelHandle, Mathf.CeilToInt(boundingBox.size.x / 8.0f), Mathf.CeilToInt(boundingBox.size.y / 8.0f), 1);

                // 填充
                temp = RenderTexture.GetTemporary(GetConnectedComponentMapDescriptor(boundingBox.size.x, boundingBox.size.y));

                floodFlag = new ComputeBuffer(1, sizeof(int));
                flag = true;
                kernelHandle = m_ConnectedComponentMapComputeShader.FindKernel("FloodFill");
                m_ConnectedComponentMapComputeShader.SetTexture(kernelHandle, "_ConnectedComponentMap", fragment);
                m_ConnectedComponentMapComputeShader.SetTexture(kernelHandle, "_PrevConnectedComponentMap", temp);
                m_ConnectedComponentMapComputeShader.SetInt("_Width", boundingBox.size.x);
                m_ConnectedComponentMapComputeShader.SetInt("_Height", boundingBox.size.y);
                m_ConnectedComponentMapComputeShader.SetBuffer(kernelHandle, "_FloodFlag", floodFlag);

                flags = ArrayPool<int>.Shared.Rent(1);
                i = 0;

                for (; i < SAFE_ITERATION && flag; i++)
                {
                    flags[0] = 0;
                    floodFlag.SetData(flags, 0, 0, 1);
                    Graphics.Blit(fragment, temp);
                    m_ConnectedComponentMapComputeShader.Dispatch(kernelHandle, Mathf.CeilToInt(boundingBox.size.x / 8.0f), Mathf.CeilToInt(boundingBox.size.y / 8.0f), 1);

                    floodFlag.GetData(flags, 0, 0, 1);
                    flag = flags[0] != 0;
                }

                // Debug.Log($"Iteration: {i}, Flag: {flag}");

                RenderTexture.ReleaseTemporary(temp);
                ArrayPool<int>.Shared.Return(flags);
                floodFlag.Release();

                ProcessFragment(fragment, boundingBox);
            }

            DictionaryPool<Vector4, BoundsInt>.Release(connectedComponentSet);
        }

        void ProcessFragment(RenderTexture fragment, BoundsInt boundingBox)
        {
            var subFragmentSet = DictionaryPool<Vector4, BoundsInt>.Get();
            GetUniqueColorInRenderTexture(fragment, subFragmentSet);

            foreach (var kv in subFragmentSet)
            {
                Vector4 subFragmentIndex = kv.Key;

                if (subFragmentIndex.z == 0.0f)
                {
                    continue;
                }

                int boundingExtend = BOUNDING_BOX_EXTEND;
                if (subFragmentIndex.x == 0.0f && subFragmentIndex.y == 0.0f)
                {
                    boundingExtend = 0;
                }

                var subFragmentBoundingBox = kv.Value;
                subFragmentBoundingBox.xMin = subFragmentBoundingBox.xMin - boundingExtend;
                subFragmentBoundingBox.yMin = subFragmentBoundingBox.yMin - boundingExtend;
                subFragmentBoundingBox.xMax = subFragmentBoundingBox.xMax + boundingExtend;
                subFragmentBoundingBox.yMax = subFragmentBoundingBox.yMax + boundingExtend;

                var temp = RenderTexture.GetTemporary(GetConnectedComponentMapDescriptor(subFragmentBoundingBox.size.x, subFragmentBoundingBox.size.y));

                int kernelHandle = m_ConnectedComponentMapComputeShader.FindKernel("InitializeSubFragment");
                m_ConnectedComponentMapComputeShader.SetTexture(kernelHandle, "_ConnectedComponentMap", fragment);
                m_ConnectedComponentMapComputeShader.SetTexture(kernelHandle, "_ConnectedComponentFragment", temp);
                m_ConnectedComponentMapComputeShader.SetInt("_Width", boundingBox.size.x);
                m_ConnectedComponentMapComputeShader.SetInt("_Height", boundingBox.size.y);
                m_ConnectedComponentMapComputeShader.SetInt("_BoundingOffsetx", subFragmentBoundingBox.xMin);
                m_ConnectedComponentMapComputeShader.SetInt("_BoundingOffsety", subFragmentBoundingBox.yMin);
                m_ConnectedComponentMapComputeShader.SetInt("_BoundingWidth", subFragmentBoundingBox.size.x);
                m_ConnectedComponentMapComputeShader.SetInt("_BoundingHeight", subFragmentBoundingBox.size.y);
                m_ConnectedComponentMapComputeShader.SetVector("_FragmentIndex", kv.Key);

                m_ConnectedComponentMapComputeShader.Dispatch(kernelHandle, Mathf.CeilToInt(subFragmentBoundingBox.size.x / 8.0f), Mathf.CeilToInt(subFragmentBoundingBox.size.y / 8.0f), 1);

                var segmentCount = ArrayPool<int>.Shared.Rent(1);
                segmentCount[0] = 0;
                var segmentSet = ArrayPool<ConnectedSegment>.Shared.Rent(65536);
                kernelHandle = m_ConnectedComponentMapComputeShader.FindKernel("FillSegmentSet");

                ComputeBuffer countBuffer = new ComputeBuffer(1, sizeof(int));
                ComputeBuffer segmentBuffer = new ComputeBuffer(65536, sizeof(int) * 4);
                countBuffer.SetData(segmentCount, 0, 0, 1);

                m_ConnectedComponentMapComputeShader.SetBuffer(kernelHandle, "_ConnectedSegmentSet", segmentBuffer);
                m_ConnectedComponentMapComputeShader.SetBuffer(kernelHandle, "_ConnectedSegmentIndex", countBuffer);
                m_ConnectedComponentMapComputeShader.SetTexture(kernelHandle, "_ConnectedComponentFragment", temp);
                m_ConnectedComponentMapComputeShader.SetInt("_Width", subFragmentBoundingBox.size.x);
                m_ConnectedComponentMapComputeShader.SetInt("_Height", subFragmentBoundingBox.size.y);

                m_ConnectedComponentMapComputeShader.Dispatch(kernelHandle, Mathf.CeilToInt(subFragmentBoundingBox.size.x / 8.0f), Mathf.CeilToInt(subFragmentBoundingBox.size.y / 8.0f), 1);

                countBuffer.GetData(segmentCount, 0, 0, 1);
                segmentBuffer.GetData(segmentSet, 0, 0, segmentCount[0]);

                subFragmentBoundingBox.xMax += boundingBox.xMin;
                subFragmentBoundingBox.yMax += boundingBox.yMin;
                subFragmentBoundingBox.xMin += boundingBox.xMin;
                subFragmentBoundingBox.yMin += boundingBox.yMin;
                ArraySegment<ConnectedSegment> clippedSegmentSet = new ArraySegment<ConnectedSegment>(segmentSet, 0, segmentCount[0]);

                var connectedSegmentSet = new ConnectedSegmentSet
                {
                    BoundingBox = subFragmentBoundingBox,
                    Segments = new List<ConnectedSegment>(clippedSegmentSet)
                };

                m_OutputConnectedSegmentSets.Add(connectedSegmentSet);

                countBuffer.Release();
                segmentBuffer.Release();
                ArrayPool<int>.Shared.Return(segmentCount);
                ArrayPool<ConnectedSegment>.Shared.Return(segmentSet);
                RenderTexture.ReleaseTemporary(temp);

                var dict = DictionaryPool<Vector2Int, Vector2Int>.Get();
                Vector2Int startPos = new Vector2Int(65535, 65535);
                for (int i = 0; i < connectedSegmentSet.Segments.Count; i++)
                {
                    var segment = connectedSegmentSet.Segments[i];
                    dict[segment.Start] = segment.End;

                    if (startPos.x > segment.Start.x)
                    {
                        startPos = segment.Start;
                    }
                    else if (startPos.x == segment.Start.x && startPos.y > segment.Start.y)
                    {
                        startPos = segment.Start;
                    }
                }

                Vector2Int currentVertice = startPos;
                Vector2Int startVertice = currentVertice;
                connectedSegmentSet.Segments.Clear();

                while (dict.TryGetValue(currentVertice, out Vector2Int next))
                {
                    connectedSegmentSet.Segments.Add(new ConnectedSegment { Start = currentVertice, End = next });
                    currentVertice = next;
                    if (currentVertice == startVertice)
                    {
                        break;
                    }
                }

                DictionaryPool<Vector2Int, Vector2Int>.Release(dict);
            }

            DictionaryPool<Vector4, BoundsInt>.Release(subFragmentSet);
        }

        void CleanUpConnectedSegmentSet(float tolerance = 0.2f)
        {
            for (int i = 0; i < m_OutputConnectedSegmentSets.Count; i++)
            {
                CleanUpConnectedSegmentSet(m_OutputConnectedSegmentSets[i], tolerance * m_CastResolution / 5);
            }
        }

        void CleanUpConnectedSegmentSet(ConnectedSegmentSet set, float tolerance)
        {
            if(set.Segments.Count == 0) return;
            
            List<int> pointIndexsToKeep = ListPool<int>.Get();
            pointIndexsToKeep.Clear();

            int firstPoint = 0;
            int lastPoint = set.Segments.Count;

            pointIndexsToKeep.Add(0);
            pointIndexsToKeep.Add(set.Segments.Count);

            List<Vector2Int> points = ListPool<Vector2Int>.Get();
            points.Clear();
            for (int i = 0; i < set.Segments.Count; i++)
            {
                points.Add(set.Segments[i].Start);
            }

            points.Add(set.Segments[0].Start);
            DouglasPeuckerReduction(points, firstPoint, lastPoint, tolerance, pointIndexsToKeep);

            set.Segments.Clear();
            pointIndexsToKeep.Sort();
            for (int i = 0; i < pointIndexsToKeep.Count - 1; i++)
            {
                set.Segments.Add(new ConnectedSegment { Start = points[pointIndexsToKeep[i]], End = points[pointIndexsToKeep[i + 1]] });
            }
            /* set.Segments.Add(new ConnectedSegment { Start = points[pointIndexsToKeep[pointIndexsToKeep.Count - 1]], End = points[pointIndexsToKeep[0]] }); */


            ListPool<int>.Release(pointIndexsToKeep);
            ListPool<Vector2Int>.Release(points);
        }

        private static void DouglasPeuckerReduction(List<Vector2Int> points, int firstPoint, int lastPoint, float tolerance, List<int> pointIndexsToKeep)
        {
            float maxDistance = 0;
            int indexFarthest = 0;

            for (int index = firstPoint + 1; index < lastPoint; index++)
            {
                float distance = PerpendicularDistance(points[firstPoint], points[lastPoint], points[index]);
                if (distance > maxDistance)
                {
                    maxDistance = distance;
                    indexFarthest = index;
                }
            }

            if (maxDistance > tolerance && indexFarthest != 0)
            {
                pointIndexsToKeep.Add(indexFarthest);

                DouglasPeuckerReduction(points, firstPoint, indexFarthest, tolerance, pointIndexsToKeep);
                DouglasPeuckerReduction(points, indexFarthest, lastPoint, tolerance, pointIndexsToKeep);
            }
        }

        private static float PerpendicularDistance(Vector2Int point1, Vector2Int point2, Vector2Int point)
        {
            if (point1 == point2)
            {
                return Vector2Int.Distance(point1, point);
            }

            float area = Mathf.Abs(0.5f * (point1.x * point2.y + point2.x * point.y + point.x * point1.y - point1.y * point2.x - point2.y * point.x - point.y * point1.x));
            float baseLength = Mathf.Sqrt(Mathf.Pow(point1.x - point2.x, 2) + Mathf.Pow(point1.y - point2.y, 2));
            float distance = area / baseLength * 2;

            return distance;
        }


        void GenerateVertexData()
        {
            int totalVetices = 0;
            for (int i = 0; i < m_OutputConnectedSegmentSets.Count; i++)
            {
                totalVetices += m_OutputConnectedSegmentSets[i].Segments.Count * 2;
            }

            m_Vertices.Clear();
            m_Indices.Clear();

            Vector2Int center = new Vector2Int(SourceWidth / 2, SourceHeight / 2);
            int indexOffset = 0;
            for (int i = 0; i < m_OutputConnectedSegmentSets.Count; i++)
            {
                var connectedSegmentSet = m_OutputConnectedSegmentSets[i];
                for (int j = 0; j < connectedSegmentSet.Segments.Count; j++)
                {
                    var segment = connectedSegmentSet.Segments[j];
                    Vector2Int startCoord = new Vector2Int(segment.Start.x + connectedSegmentSet.BoundingBox.xMin, segment.Start.y + connectedSegmentSet.BoundingBox.yMin) - center;

                    m_Vertices.Add(new Vector3(startCoord.x * m_NearPlane.x / SourceWidth, startCoord.y * m_NearPlane.y / SourceHeight, 0));
                    m_Vertices.Add(new Vector3(startCoord.x * m_FarPlane.x / SourceWidth, startCoord.y * m_FarPlane.y / SourceHeight, m_CastDistance));

                    int currIndexStart = j * 2;
                    int nextIndexStart = j * 2 + 2;
                    if (j == connectedSegmentSet.Segments.Count - 1) nextIndexStart = 0;
                    currIndexStart += indexOffset;
                    nextIndexStart += indexOffset;

                    if (currIndexStart >= totalVetices || nextIndexStart >= totalVetices)
                    {
                        Debug.LogError("Index out of range");
                        return;
                    }

                    m_Indices.Add(nextIndexStart);
                    m_Indices.Add(currIndexStart + 1);
                    m_Indices.Add(currIndexStart);

                    m_Indices.Add(nextIndexStart + 1);
                    m_Indices.Add(currIndexStart + 1);
                    m_Indices.Add(nextIndexStart);
                }

                indexOffset = m_Vertices.Count;
            }

        }

        void GetUniqueColorInRenderTexture(RenderTexture renderTexture, Dictionary<Vector4, BoundsInt> boundingSet)
        {
            RenderTexture.active = renderTexture;
            Texture2D texture = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBA64, false);
            texture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
            texture.Apply();
            for (int i = 0; i < texture.width; i++)
            {
                for (int j = 0; j < texture.height; j++)
                {
                    Color color = texture.GetPixel(i, j);

                    if (!boundingSet.TryGetValue(color, out BoundsInt bounds))
                    {
                        bounds = new BoundsInt(i, j, 0, 1, 1, 1);
                        boundingSet[color] = bounds;
                    }
                    else
                    {
                        bounds.xMin = Mathf.Min(bounds.xMin, i);
                        bounds.yMin = Mathf.Min(bounds.yMin, j);
                        bounds.xMax = Mathf.Max(bounds.xMax, i + 1);
                        bounds.yMax = Mathf.Max(bounds.yMax, j + 1);
                        boundingSet[color] = bounds;
                    }
                }
            }
            RenderTexture.active = null;
            DestroyImmediate(texture);
        }

        void OnValidate()
        {

        }

        void OnDrawGizmos()
        {
            if (Selection.activeGameObject != gameObject)
                return;

            // Draw near plane
            Gizmos.color = Color.green;
            Vector3 nearCenter = transform.position;
            float nearHeight = m_NearPlane.y;
            float nearWidth = m_NearPlane.x;
            Matrix4x4 nearMatrix = Matrix4x4.TRS(nearCenter, transform.rotation, Vector3.one);
            Gizmos.matrix = nearMatrix;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(nearWidth, nearHeight, 0));

            // Draw far plane
            Gizmos.color = Color.red;
            Vector3 farCenter = transform.position + transform.forward * m_CastDistance;
            float farHeight = nearHeight * m_FarPlaneScale;
            float farWidth = nearWidth * m_FarPlaneScale;
            Matrix4x4 farMatrix = Matrix4x4.TRS(farCenter, transform.rotation, Vector3.one);
            Gizmos.matrix = farMatrix;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(farWidth, farHeight, 0));

            // Reset Gizmos matrix
            Gizmos.matrix = Matrix4x4.identity;

            // Draw lines between near and far planes
            Vector3[] nearCorners = new Vector3[4];
            Vector3[] farCorners = new Vector3[4];

            nearCorners[0] = nearMatrix.MultiplyPoint3x4(new Vector3(-nearWidth / 2, -nearHeight / 2, 0));
            nearCorners[1] = nearMatrix.MultiplyPoint3x4(new Vector3(nearWidth / 2, -nearHeight / 2, 0));
            nearCorners[2] = nearMatrix.MultiplyPoint3x4(new Vector3(-nearWidth / 2, nearHeight / 2, 0));
            nearCorners[3] = nearMatrix.MultiplyPoint3x4(new Vector3(nearWidth / 2, nearHeight / 2, 0));

            farCorners[0] = farMatrix.MultiplyPoint3x4(new Vector3(-farWidth / 2, -farHeight / 2, 0));
            farCorners[1] = farMatrix.MultiplyPoint3x4(new Vector3(farWidth / 2, -farHeight / 2, 0));
            farCorners[2] = farMatrix.MultiplyPoint3x4(new Vector3(-farWidth / 2, farHeight / 2, 0));
            farCorners[3] = farMatrix.MultiplyPoint3x4(new Vector3(farWidth / 2, farHeight / 2, 0));

            Gizmos.color = Color.yellow;
            for (int i = 0; i < 4; i++)
            {
                Gizmos.DrawLine(nearCorners[i], farCorners[i]);
            }
        }
#endif
    }
}
