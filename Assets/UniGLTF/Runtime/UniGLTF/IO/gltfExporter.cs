using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using VRMShaders;


namespace UniGLTF
{
    public class gltfExporter : IDisposable
    {
        protected glTF glTF;

        public GameObject Copy
        {
            get;
            protected set;
        }

        public List<Mesh> Meshes { get; private set; } = new List<Mesh>();

        /// <summary>
        /// Mesh毎に、元のBlendShapeIndex => ExportされたBlendShapeIndex の対応を記録する
        ///
        /// BlendShape が空の場合にスキップするので
        /// </summary>
        /// <value></value>
        public Dictionary<Mesh, Dictionary<int, int>> MeshBlendShapeIndexMap
        {
            get;
            private set;
        }

        public List<Transform> Nodes
        {
            get;
            private set;
        }

        public List<Material> Materials
        {
            get;
            private set;
        }

        public ITextureExporter TextureExporter => m_textureExporter;

        protected virtual IMaterialExporter CreateMaterialExporter()
        {
            return new MaterialExporter();
        }

        /// <summary>
        /// このエクスポーターがサポートするExtension
        /// </summary>
        protected virtual IEnumerable<string> ExtensionUsed
        {
            get
            {
                yield return glTF_KHR_materials_unlit.ExtensionName;
                yield return glTF_KHR_texture_transform.ExtensionName;
            }
        }

        TextureExporter m_textureExporter;

        GltfExportSettings m_settings;

        public gltfExporter(glTF gltf, GltfExportSettings settings)
        {
            glTF = gltf;

            glTF.extensionsUsed.AddRange(ExtensionUsed);

            glTF.asset = new glTFAssets
            {
                generator = "UniGLTF-" + UniGLTFVersion.VERSION,
                version = "2.0",
            };

            m_settings = settings;
            if (m_settings == null)
            {
                // default
                m_settings = new GltfExportSettings();
            }
        }

        GameObject m_tmpParent = null;

        public virtual void Prepare(GameObject go)
        {
            // コピーを作って左手系を右手系に変換する
            Copy = GameObject.Instantiate(go);
            Copy.transform.ReverseRecursive(m_settings.InverseAxis.Create());

            // Export の root は gltf の scene になるので、
            // エクスポート対象が単一の GameObject の場合に、
            // ダミー親 "m_tmpParent" を一時的に作成する。
            //
            // https://github.com/vrm-c/UniVRM/pull/736
            if (Copy.transform.childCount == 0)
            {
                m_tmpParent = new GameObject("tmpParent");
                Copy.transform.SetParent(m_tmpParent.transform, true);
                Copy = m_tmpParent;
            }

            if (Copy.transform.GetComponent<Renderer>() != null)
            {
                // should throw ?
                Debug.LogError("root mesh is not exported");
            }
        }

        public void Dispose()
        {
            if (m_tmpParent != null)
            {
                var child = m_tmpParent.transform.GetChild(0);
                child.SetParent(null);
                Copy = child.gameObject;
                if (Application.isPlaying)
                {
                    GameObject.Destroy(m_tmpParent);
                }
                else
                {
                    GameObject.DestroyImmediate(m_tmpParent);
                }
            }

            if (Application.isEditor)
            {
                GameObject.DestroyImmediate(Copy);
            }
            else
            {
                GameObject.Destroy(Copy);
            }
        }

        #region Export
        static glTFNode ExportNode(Transform x, List<Transform> nodes, IReadOnlyList<MeshExportInfo> meshWithRenderers, List<SkinnedMeshRenderer> skins)
        {
            var node = new glTFNode
            {
                name = x.name,
                children = x.transform.GetChildren().Select(y => nodes.IndexOf(y)).ToArray(),
                rotation = x.transform.localRotation.ToArray(),
                translation = x.transform.localPosition.ToArray(),
                scale = x.transform.localScale.ToArray(),
            };

            if (x.gameObject.activeInHierarchy)
            {
                var meshRenderer = x.GetComponent<MeshRenderer>();

                if (meshRenderer != null)
                {
                    var meshFilter = x.GetComponent<MeshFilter>();
                    if (meshFilter != null)
                    {
                        var mesh = meshFilter.sharedMesh;
                        var materials = meshRenderer.sharedMaterials;
                        if (MeshExportInfo.TryGetSameMeshIndex(meshWithRenderers, mesh, materials, out int meshIndex))
                        {
                            node.mesh = meshIndex;
                        }
                        else if (mesh == null)
                        {
                            // mesh が無い
                            node.mesh = -1;
                        }
                        else if (mesh.vertexCount == 0)
                        {
                            // 頂点データが無い場合
                            node.mesh = -1;
                        }
                        else
                        {
                            // MeshとMaterialが一致するものが見つからなかった
                            throw new Exception("Mesh not found.");
                        }
                    }
                }

                var skinnedMeshRenderer = x.GetComponent<SkinnedMeshRenderer>();
                if (skinnedMeshRenderer != null)
                {
                    var mesh = skinnedMeshRenderer.sharedMesh;
                    var materials = skinnedMeshRenderer.sharedMaterials;
                    if (MeshExportInfo.TryGetSameMeshIndex(meshWithRenderers, mesh, materials, out int meshIndex))
                    {
                        node.mesh = meshIndex;
                        node.skin = skins.IndexOf(skinnedMeshRenderer);
                    }
                    else if (mesh == null)
                    {
                        // mesh が無い
                        node.mesh = -1;
                    }
                    else if (mesh.vertexCount == 0)
                    {
                        // 頂点データが無い場合
                        node.mesh = -1;
                    }
                    else
                    {
                        // MeshとMaterialが一致するものが見つからなかった
                        throw new Exception("Mesh not found.");
                    }
                }
            }

            return node;
        }

        public virtual void ExportExtensions(ITextureSerializer textureSerializer)
        {
            // do nothing
        }

        public virtual void Export(GltfExportSettings meshExportSettings, ITextureSerializer textureSerializer)
        {
            var bytesBuffer = new ArrayByteBuffer(new byte[50 * 1024 * 1024]);
            var bufferIndex = glTF.AddBuffer(bytesBuffer);

            Nodes = Copy.transform.Traverse()
                .Skip(1) // exclude root object for the symmetry with the importer
                .ToList();

            var uniqueUnityMeshes = new MeshExportList();
            uniqueUnityMeshes.GetInfo(Nodes, meshExportSettings);

            #region Materials and Textures
            Materials = uniqueUnityMeshes.GetUniqueMaterials().ToList();

            m_textureExporter = new TextureExporter(textureSerializer);

            var materialExporter = CreateMaterialExporter();
            glTF.materials = Materials.Select(x => materialExporter.ExportMaterial(x, TextureExporter, m_settings)).ToList();
            #endregion

            #region Meshes
            MeshBlendShapeIndexMap = new Dictionary<Mesh, Dictionary<int, int>>();
            foreach (var unityMesh in uniqueUnityMeshes)
            {
                var (gltfMesh, blendShapeIndexMap) = meshExportSettings.DivideVertexBuffer
                    ? MeshExporter_DividedVertexBuffer.Export(glTF, bufferIndex, unityMesh, Materials, m_settings.InverseAxis.Create(), meshExportSettings)
                    : MeshExporter_SharedVertexBuffer.Export(glTF, bufferIndex, unityMesh, Materials, m_settings.InverseAxis.Create(), meshExportSettings)
                    ;
                glTF.meshes.Add(gltfMesh);
                Meshes.Add(unityMesh.Mesh);
                if (!MeshBlendShapeIndexMap.ContainsKey(unityMesh.Mesh))
                {
                    // 重複防止
                    MeshBlendShapeIndexMap.Add(unityMesh.Mesh, blendShapeIndexMap);
                }
            }
            #endregion

            #region Nodes and Skins
            var skins = uniqueUnityMeshes
                .SelectMany(x => x.Renderers)
                .Where(x => x.Item1 is SkinnedMeshRenderer && x.UniqueBones != null)
                .Select(x => x.Item1 as SkinnedMeshRenderer)
                .ToList()
                ;
            foreach (var node in Nodes)
            {
                var gltfNode = ExportNode(node, Nodes, uniqueUnityMeshes, skins);
                glTF.nodes.Add(gltfNode);
            }
            glTF.scenes = new List<gltfScene>
                {
                    new gltfScene
                    {
                        nodes = Copy.transform.GetChildren().Select(x => Nodes.IndexOf(x)).ToArray(),
                    }
                };

            foreach (var x in uniqueUnityMeshes)
            {
                foreach (var (renderer, uniqueBones) in x.Renderers)
                {
                    if (uniqueBones != null && renderer is SkinnedMeshRenderer smr)
                    {
                        var matrices = x.GetBindPoses().Select(m_settings.InverseAxis.Create().InvertMat4).ToArray();
                        var accessor = glTF.ExtendBufferAndGetAccessorIndex(bufferIndex, matrices, glBufferTarget.NONE);
                        var skin = new glTFSkin
                        {
                            inverseBindMatrices = accessor,
                            joints = uniqueBones.Select(y => Nodes.IndexOf(y)).ToArray(),
                            skeleton = Nodes.IndexOf(smr.rootBone),
                        };
                        var skinIndex = glTF.skins.Count;
                        glTF.skins.Add(skin);

                        foreach (var z in Nodes.Where(y => y.Has(renderer)))
                        {
                            var nodeIndex = Nodes.IndexOf(z);
                            var node = glTF.nodes[nodeIndex];
                            node.skin = skinIndex;
                        }
                    }
                }
            }
            #endregion

#if UNITY_EDITOR
            #region Animations

            var clips = new List<AnimationClip>();
            var animator = Copy.GetComponent<Animator>();
            var animation = Copy.GetComponent<Animation>();
            if (animator != null)
            {
                clips = AnimationExporter.GetAnimationClips(animator);
            }
            else if (animation != null)
            {
                clips = AnimationExporter.GetAnimationClips(animation);
            }

            if (clips.Any())
            {
                foreach (AnimationClip clip in clips)
                {
                    var animationWithCurve = AnimationExporter.Export(clip, Copy.transform, Nodes);

                    foreach (var kv in animationWithCurve.SamplerMap)
                    {
                        var sampler = animationWithCurve.Animation.samplers[kv.Key];

                        var inputAccessorIndex = glTF.ExtendBufferAndGetAccessorIndex(bufferIndex, kv.Value.Input);
                        sampler.input = inputAccessorIndex;

                        var outputAccessorIndex = glTF.ExtendBufferAndGetAccessorIndex(bufferIndex, kv.Value.Output);
                        sampler.output = outputAccessorIndex;

                        // modify accessors
                        var outputAccessor = glTF.accessors[outputAccessorIndex];
                        var channel = animationWithCurve.Animation.channels.First(x => x.sampler == kv.Key);
                        switch (glTFAnimationTarget.GetElementCount(channel.target.path))
                        {
                            case 1:
                                outputAccessor.type = "SCALAR";
                                //outputAccessor.count = ;
                                break;
                            case 3:
                                outputAccessor.type = "VEC3";
                                outputAccessor.count /= 3;
                                break;

                            case 4:
                                outputAccessor.type = "VEC4";
                                outputAccessor.count /= 4;
                                break;

                            default:
                                throw new NotImplementedException();
                        }
                    }
                    animationWithCurve.Animation.name = clip.name;
                    glTF.animations.Add(animationWithCurve.Animation);
                }
            }
            #endregion
#endif

            ExportExtensions(textureSerializer);

            // Extension で Texture が増える場合があるので最後に呼ぶ
            var exported = m_textureExporter.Export();
            for (var exportedTextureIdx = 0; exportedTextureIdx < exported.Count; ++exportedTextureIdx)
            {
                var (unityTexture, colorSpace) = exported[exportedTextureIdx];
                glTF.PushGltfTexture(bufferIndex, unityTexture, colorSpace, textureSerializer);
            }

            FixName(glTF);
        }

        /// <summary>
        /// GlbLowPevelParser.FixNameUnique で付与した Suffix を remove
        /// </summary>
        public static void FixName(glTF gltf)
        {
            var regex = new Regex($@"{GlbLowLevelParser.UniqueFixResourceSuffix}\d+$");
            foreach (var gltfImages in gltf.images)
            {
                if (regex.IsMatch(gltfImages.name))
                {
                    gltfImages.name = regex.Replace(gltfImages.name, string.Empty);
                }
            }
            foreach (var gltfMaterial in gltf.materials)
            {
                if (regex.IsMatch(gltfMaterial.name))
                {
                    gltfMaterial.name = regex.Replace(gltfMaterial.name, string.Empty);
                }
            }
            foreach (var gltfAnimation in gltf.animations)
            {
                if (regex.IsMatch(gltfAnimation.name))
                {
                    gltfAnimation.name = regex.Replace(gltfAnimation.name, string.Empty);
                }
            }
        }
        #endregion
    }
}
