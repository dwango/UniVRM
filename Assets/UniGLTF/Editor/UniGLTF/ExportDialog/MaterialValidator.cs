using System.Collections.Generic;
using UnityEngine;

namespace UniGLTF
{
    public interface IMaterialValidator
    {
        /// <summary>
        /// shaderName から glTF マテリアルタイプ を得る 
        /// 
        /// shaderName が エクスポートできるものでないときは null を返す(gltfデフォルトの pbr として処理される)
        /// </summary>
        /// <param name="shaderName"></param>
        /// <returns></returns>
        string GetGltfMaterialTypeFromUnityShaderName(string shaderName);

        /// <summary>
        /// テクスチャーを使うプロパティを列挙する
        /// </summary>
        /// <param name="propertyName"></param>
        /// <param name="m"></param>
        /// <returns></returns>
        IEnumerable<(string propertyName, Texture texture)> EnumerateTextureProperties(Material m);
    }

    public class DefaultMaterialValidator : IMaterialValidator
    {
        public virtual string GetGltfMaterialTypeFromUnityShaderName(string shaderName)
        {
            if (shaderName == "Standard")
            {
                return "pbr";
            }
            if (MaterialExporter.IsUnlit(shaderName))
            {
                return "unlit";
            }
            return null;
        }

        public virtual IEnumerable<(string propertyName, Texture texture)> EnumerateTextureProperties(Material m)
        {
            // main color
            yield return (MaterialExporter.COLOR_TEXTURE_PROP, m.GetTexture(MaterialExporter.COLOR_TEXTURE_PROP));

            if (GetGltfMaterialTypeFromUnityShaderName(m.shader.name) == "unlit")
            {
                yield break;
            }

            // PBR
            yield return (MaterialExporter.METALLIC_TEX_PROP, m.GetTexture(MaterialExporter.METALLIC_TEX_PROP));
            yield return (MaterialExporter.NORMAL_TEX_PROP, m.GetTexture(MaterialExporter.NORMAL_TEX_PROP));
            yield return (MaterialExporter.EMISSION_TEX_PROP, m.GetTexture(MaterialExporter.EMISSION_TEX_PROP));
            yield return (MaterialExporter.OCCLUSION_TEX_PROP, m.GetTexture(MaterialExporter.OCCLUSION_TEX_PROP));
        }
    }
}
