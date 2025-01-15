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
    public class FakeVolumetricLightClipPlane : MonoBehaviour
    {
#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            if (Selection.activeGameObject != gameObject && !(Selection.activeGameObject != null && transform.IsChildOf(Selection.activeGameObject.transform)))
                return;
            
            Vector3 size = new Vector3(transform.localScale.x, transform.localScale.y, 0);
            Gizmos.matrix = transform.localToWorldMatrix;
            
            Gizmos.color = new Color(0, 1, 1, 0.25f); // 半透明填充颜色
            Gizmos.DrawCube(Vector3.zero, size);

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(Vector3.zero, size);

            Gizmos.matrix = Matrix4x4.identity;
        }
#endif
    }
}
