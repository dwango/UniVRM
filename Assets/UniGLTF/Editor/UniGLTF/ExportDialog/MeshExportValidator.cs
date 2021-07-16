using System;
using System.Collections.Generic;
using System.Linq;
using UniGLTF.M17N;
using UnityEditor;
using UnityEngine;

namespace UniGLTF
{
    [Serializable]
    public class MeshExportValidator : ScriptableObject
    {
        public static Mesh GetMesh(Renderer r)
        {
            if (r is SkinnedMeshRenderer smr)
            {
                return smr.sharedMesh;
            }
            if (r is MeshRenderer)
            {
                MeshFilter f = r.GetComponent<MeshFilter>();
                if (f != null)
                {
                    return f.sharedMesh;
                }
            }
            return null;
        }

        public MeshExportList Meshes = new MeshExportList();

        public int ExpectedExportByteSize => Meshes.Where(x => x.IsRendererActive).Sum(x => x.ExportByteSize);

        public void SetRoot(GameObject ExportRoot, GltfExportSettings settings, IBlendShapeExportFilter blendShapeFilter)
        {
            if (ExportRoot == null)
            {
                return;
            }
            Meshes.GetInfo(ExportRoot.transform.Traverse().Skip(1), settings);
            foreach (var info in Meshes)
            {
                info.CalcMeshSize(ExportRoot, info.Renderers[0].Item1, settings, blendShapeFilter);
            }
        }

        public IMaterialValidator MaterialValidator = new DefaultMaterialValidator();

        public enum Messages
        {
            MATERIALS_LESS_THAN_SUBMESH_COUNT,
            MATERIALS_GREATER_THAN_SUBMESH_COUNT,
            MATERIALS_CONTAINS_NULL,
            UNKNOWN_SHADER,
        }

        public IEnumerable<Validation> Validate(GameObject ExportRoot)
        {
            foreach (var info in Meshes)
            {
                // invalid materials.len
                if (info.Materials.Length < info.Mesh.subMeshCount)
                {
                    // submesh より material の方が少ない
                    yield return Validation.Error(Messages.MATERIALS_LESS_THAN_SUBMESH_COUNT.Msg());
                }
                else
                {
                    if (info.Materials.Length > info.Mesh.subMeshCount)
                    {
                        // submesh より material の方が多い
                        yield return Validation.Warning(Messages.MATERIALS_GREATER_THAN_SUBMESH_COUNT.Msg());
                    }

                    if (info.Materials.Take(info.Mesh.subMeshCount).Any(x => x == null))
                    {
                        // material に null が含まれる(unity で magenta になっているはず)
                        yield return Validation.Error($"{info.Renderers}: {Messages.MATERIALS_CONTAINS_NULL.Msg()}");
                    }
                }
            }

            foreach (var m in Meshes.GetUniqueMaterials())
            {
                var gltfMaterial = MaterialValidator.GetGltfMaterialTypeFromUnityShaderName(m.shader.name);
                if (string.IsNullOrEmpty(gltfMaterial))
                {
                    yield return Validation.Warning($"{m}: unknown shader: {m.shader.name} => export as gltf default");
                }

                var used = new HashSet<Texture>();
                foreach (var (propName, texture) in MaterialValidator.EnumerateTextureProperties(m))
                {
                    if (texture == null)
                    {
                        continue;
                    }
                    var assetPath = AssetDatabase.GetAssetPath(texture);
                    if (!string.IsNullOrEmpty(assetPath))
                    {
                        if (AssetImporter.GetAtPath(assetPath) is TextureImporter textureImporter)
                        {
                            switch (textureImporter.textureType)
                            {
                                case TextureImporterType.Default:
                                case TextureImporterType.NormalMap:
                                    break;

                                default:
                                    // EditorTextureSerializer throw Exception
                                    // エクスポート未実装
                                    if (used.Add(texture))
                                    {
                                        yield return Validation.Error($"{texture}: unknown texture type: {textureImporter.textureType}", ValidationContext.Create(texture));
                                    }
                                    break;
                            }
                        }
                    }
                }

                yield break;
            }
        }
    }
}
