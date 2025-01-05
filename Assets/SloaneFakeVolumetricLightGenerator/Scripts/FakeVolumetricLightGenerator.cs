#if UNITY_EDITOR
using UnityEditor;
#endif

using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Sloane.FakeVolumetricLightGenerator
{
    [ExecuteAlways]
    public class FakeVolumetricLightGenerator : MonoBehaviour
    {
        [SerializeField]
        RenderTexture m_SourceTexture;
        [SerializeField]
        Camera m_Camera;
        [SerializeField]
        Vector2 m_NearPlane = new Vector2(0.5f, 0.5f);
        [SerializeField, Min(1.0f)]
        float m_FarPlaneScale = 1.618f;
        [SerializeField]
        float m_CastDistance = 2.6179241f;
        [SerializeField]
        Vector2Int m_CastResolution = new Vector2Int(512, 512);

#if UNITY_EDITOR
        void InitializeGenerator()
        {

        }

        void InitializeRenderTexture()
        {
            if (m_SourceTexture != null)
            {
                m_SourceTexture.Release();
                DestroyImmediate(m_SourceTexture);
                m_SourceTexture = null;
            }

            RenderTextureDescriptor desc = new RenderTextureDescriptor(m_CastResolution.x, m_CastResolution.y)
            {
                depthBufferBits = 24,
                /* graphicsFormat = GraphicsFormat,
                volumeDepth = 1,
                msaaSamples = 1,
                dimension = TextureDimension.Tex2D */
            };
            
            m_SourceTexture = new RenderTexture(desc);
            m_SourceTexture.name = "FakeVolumetricLightDepthTexture";
            m_SourceTexture.hideFlags = HideFlags.DontSave;
            m_SourceTexture.Create();
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

            m_Camera.gameObject.SetActive(false);
        }

        public void Generate()
        {
            InitializeGenerator();
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
