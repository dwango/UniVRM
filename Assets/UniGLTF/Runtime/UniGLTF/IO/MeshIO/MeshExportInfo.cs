using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace UniGLTF
{
    [Serializable]
    public class MeshExportInfo
    {
        #region この２つの組が gltf mesh の Unique なキーとなる
        public Mesh Mesh;
        public Material[] Materials;

        public bool IsSameMeshAndMaterials(MeshExportInfo other)
        {
            return IsSameMeshAndMaterials(other.Mesh, other.Materials);
        }

        public bool IsSameMeshAndMaterials(Mesh mesh, Material[] materials)
        {
            if (Mesh != mesh) return false;
            if (Materials.Length != materials.Length) return false;
            for (var i = 0; i < Materials.Length; i++)
            {
                if (Materials[i] != materials[i]) return false;
            }
            return true;
        }

        public bool IsSameMeshAndMaterials(Renderer r)
        {
            if (r is SkinnedMeshRenderer smr)
            {
                return IsSameMeshAndMaterials(smr.sharedMesh, smr.sharedMaterials);
            }
            else if (r is MeshRenderer mr)
            {
                var filter = r.GetComponent<MeshFilter>();
                if (filter == null)
                {
                    return false;
                }
                return IsSameMeshAndMaterials(filter.sharedMesh, mr.sharedMaterials);
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        public static bool TryGetSameMeshIndex(IReadOnlyList<MeshExportInfo> meshWithRenderers, Mesh mesh, Material[] materials, out int meshIndex)
        {
            for (var i = 0; i < meshWithRenderers.Count; i++)
            {
                if (meshWithRenderers[i].IsSameMeshAndMaterials(mesh, materials))
                {
                    meshIndex = i;
                    return true;
                }
            }

            meshIndex = -1;
            return false;
        }

        public bool CanExport
        {
            get
            {
                if (Mesh == null)
                {
                    return false;
                }
                if (Mesh.vertexCount == 0)
                {
                    return false;
                }
                if (Materials == null)
                {
                    return false;
                }
                if (Materials.Length == 0)
                {
                    return false;
                }
                return true;
            }
        }
        #endregion

        /// <summary>
        /// ひとつの Mesh を複数の Renderer が共有することがありうる
        /// </summary>
        List<(Renderer, Transform[] UniqueBones)> _renderers;
        public IReadOnlyList<(Renderer, Transform[] UniqueBones)> Renderers => _renderers;

        public bool IsRendererActive => Renderers.Any(x => x.Item1.EnableForExport());

        #region  SkinnedMeshRenderer
        public bool Skinned;
        int[] JointIndexMap;

        /// <summary>
        /// glTF は　skinning の boneList の重複を許可しない
        /// (unity は ok)
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public int GetJointIndex(int index)
        {
            if (index < 0)
            {
                return index;
            }

            if (JointIndexMap != null)
            {
                return JointIndexMap[index];
            }
            else
            {
                return index;
            }
        }

        public IEnumerable<Matrix4x4> GetBindPoses()
        {
            var used = new HashSet<int>();
            for (int i = 0; i < JointIndexMap.Length; ++i)
            {
                var index = JointIndexMap[i];
                if (used.Add(index))
                {
                    yield return Mesh.bindposes[i];
                }
            }
        }
        #endregion

        #region VertexAttribute
        public bool HasNormal => Mesh != null && Mesh.normals != null && Mesh.normals.Length == Mesh.vertexCount;
        public bool HasUV => Mesh != null && Mesh.uv != null && Mesh.uv.Length == Mesh.vertexCount;

        public bool HasVertexColor => Mesh != null && Mesh.colors != null && Mesh.colors.Length == Mesh.vertexCount
            && VertexColor == VertexColorState.ExistsAndIsUsed
            || VertexColor == VertexColorState.ExistsAndMixed // Export する
            ;

        public bool HasSkinning => Mesh != null && Mesh.boneWeights != null && Mesh.boneWeights.Length == Mesh.vertexCount;

        public VertexColorState VertexColor;

        public int VertexCount;

        /// <summary>
        /// Position, UV, Normal
        /// [Color]
        /// [SkinningWeight]
        /// </summary>
        public int ExportVertexSize;
        #endregion

        #region  Triangles
        public int IndexCount;

        // int 決め打ち
        public int IndicesSize => IndexCount * 4;
        #endregion

        #region BlendShape
        public int ExportBlendShapeVertexSize;

        public int TotalBlendShapeCount;

        public int ExportBlendShapeCount;
        #endregion

        #region  Summary
        public int ExportByteSize => ExportVertexSize * VertexCount + IndicesSize + ExportBlendShapeCount * ExportBlendShapeVertexSize * VertexCount;

        public string Summary;
        #endregion

        public MeshExportInfo(Renderer renderer, GltfExportSettings settings)
        {
            if (renderer == null)
            {
                throw new ArgumentNullException();
            }

            Materials = renderer.sharedMaterials;
            _renderers = new List<(Renderer, Transform[])>();
            if (renderer is SkinnedMeshRenderer smr)
            {
                Skinned = true;
                Mesh = smr.sharedMesh;
            }
            else if (renderer is MeshRenderer mr)
            {
                var filter = mr.GetComponent<MeshFilter>();
                if (filter != null)
                {
                    Mesh = filter.sharedMesh;
                }
            }
            else
            {
                throw new NotImplementedException();
            }

            if (Mesh == null)
            {
                Summary = "no mesh";
            }

            VertexColor = VertexColorUtility.DetectVertexColor(Mesh, Materials);

            PushRenderer(renderer);
        }

        public void PushRenderer(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer smr)
            {
                if (smr.bones != null && smr.bones.Length > 0)
                {
                    var uniqueBones = smr.bones.Distinct().ToArray();
                    var jointIndexMap = new int[smr.bones.Length];
                    var bones = smr.bones;
                    for (int i = 0; i < bones.Length; ++i)
                    {
                        jointIndexMap[i] = Array.IndexOf(uniqueBones, bones[i]);
                    }
                    _renderers.Add((smr, uniqueBones));

                    if (JointIndexMap != null)
                    {
                        if (!JointIndexMap.SequenceEqual(jointIndexMap))
                        {
                            // edge case
                            throw new NotImplementedException("different jointIndexMap is not supported");
                        }
                    }
                    JointIndexMap = jointIndexMap;
                }
                else
                {
                    // maybe blendshape only SkinnedMeshRenderer
                    _renderers.Add((smr, null));
                }
            }
            else if (renderer is MeshRenderer mr)
            {
                _renderers.Add((mr, null));
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        static bool TryGetMeshInfo()
        {

            return true;
        }

        public void CalcMeshSize(
            GameObject root,
            Renderer renderer,
            GltfExportSettings settings,
            IBlendShapeExportFilter blendShapeFilter
            )
        {
            var relativePath = UniGLTF.UnityExtensions.RelativePathFrom(renderer.transform, root.transform);
            CalcMeshSize(relativePath, settings, blendShapeFilter);
        }

        public void CalcMeshSize(
            string relativePath,
            GltfExportSettings settings,
            IBlendShapeExportFilter blendShapeFilter
            )
        {
            var sb = new StringBuilder();
            if (!IsRendererActive)
            {
                sb.Append("[NotActive]");
            }

            if (Mesh == null)
            {
                sb.Append("[NoMesh]");
            }
            else
            {

                VertexCount = Mesh.vertexCount;
                ExportVertexSize = 0;
                TotalBlendShapeCount = 0;
                ExportBlendShapeCount = 0;

                // float4 x 3
                // vertices
                sb.Append($"(Pos");
                if (HasNormal)
                {
                    sb.Append("+Nom");
                    ExportVertexSize += 4 * 3;
                }
                if (HasUV)
                {
                    sb.Append("+UV");
                    ExportVertexSize += 4 * 2;
                }
                if (HasVertexColor)
                {
                    sb.Append("+Col");
                    ExportVertexSize += 4 * 4;
                }
                if (HasSkinning)
                {
                    // short, float x 4 weights
                    sb.Append("+Skin");
                    ExportVertexSize += (2 + 4) * 4;
                }
                // indices
                IndexCount = Mesh.triangles.Length;

                // postion + normal ?. always tangent is ignored
                TotalBlendShapeCount = Mesh.blendShapeCount;
                ExportBlendShapeVertexSize = settings.ExportOnlyBlendShapePosition ? 4 * 3 : 4 * (3 + 3);
                for (var i = 0; i < Mesh.blendShapeCount; ++i)
                {
                    if (!blendShapeFilter.UseBlendShape(i, relativePath))
                    {
                        continue;
                    }

                    ++ExportBlendShapeCount;
                }

                if (ExportBlendShapeCount > 0)
                {
                    sb.Append($"+Morph x {ExportBlendShapeCount}");
                }
                sb.Append($") x {Mesh.vertexCount}");
                switch (VertexColor)
                {
                    case VertexColorState.ExistsAndIsUsed:
                    case VertexColorState.ExistsAndMixed: // エクスポートする
                        sb.Insert(0, "[use vcolor]");
                        break;
                    case VertexColorState.ExistsButNotUsed:
                        sb.Insert(0, "[remove vcolor]");
                        break;
                }
                if (ExportBlendShapeCount > 0 && !HasSkinning)
                {
                    sb.Insert(0, "[morph without skin]");
                }

                // total bytes
                sb.Insert(0, $"{ExportByteSize:#,0} Bytes = ");
            }
            Summary = sb.ToString();
        }
    }

    public class MeshExportList : IReadOnlyList<MeshExportInfo>
    {
        List<MeshExportInfo> m_list = new List<MeshExportInfo>();

        public int Count => m_list.Count;

        public MeshExportInfo this[int index] => m_list[index];

        public IEnumerator<MeshExportInfo> GetEnumerator()
        {
            return m_list.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public IEnumerable<Material> GetUniqueMaterials()
        {
            return m_list.SelectMany(x => x.Materials).Where(x => x != null).Distinct();
        }

        /// <summary>
        /// ヒエラルキーからエクスポートする Mesh の情報を収集する
        /// </summary>
        /// <param name="root"></param>
        /// <param name="list"></param>
        /// <param name="settings"></param>
        /// <param name="blendShapeFilter"> blendShape の export を filtering する </param>
        public void GetInfo(IEnumerable<Transform> nodes, GltfExportSettings settings)
        {
            m_list.Clear();
            foreach (var node in nodes)
            {
                var renderer = node.GetComponent<Renderer>();
                if (renderer == null)
                {
                    continue;
                }

                var found = m_list.FirstOrDefault(x => x.IsSameMeshAndMaterials(renderer));
                if (found != null)
                {
                    found.PushRenderer(renderer);
                    continue;
                }

                var info = new MeshExportInfo(renderer, settings);
                if (info.Mesh != null)
                {
                    m_list.Add(info);
                }
            }
        }

        public static MeshExportInfo Create(GameObject go)
        {
            var list = new MeshExportList();
            list.GetInfo(go.transform.Traverse(), new GltfExportSettings());
            return list.m_list[0];
        }
    }
}
