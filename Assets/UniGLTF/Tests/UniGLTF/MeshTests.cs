using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace UniGLTF
{
    public class MeshTests
    {
        [Test]
        public void AccessorTest()
        {
            byte[] bytes = default;
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(1.0f);
                w.Write(2.0f);
                w.Write(3.0f);
                w.Write(4.0f);
                w.Write(5.0f);
                w.Write(6.0f);
                w.Write(7.0f);
                w.Write(8.0f);
                bytes = ms.ToArray();
            }
            var storage = new SimpleStorage(new ArraySegment<byte>(bytes));

            var gltf = new glTF
            {
                buffers = new List<glTFBuffer>
                {
                    new glTFBuffer
                    {
                    }
                },
                bufferViews = new List<glTFBufferView>
                {
                    new glTFBufferView{
                        buffer=0,
                        byteLength=32,
                        byteOffset=0,
                    }
                },
                accessors = new List<glTFAccessor>
                {
                    new glTFAccessor{
                        bufferView = 0,
                        componentType=glComponentType.FLOAT,
                        count=2,
                        byteOffset=0,
                        type="VEC4",
                    }
                }
            };
            gltf.buffers[0].OpenStorage(storage);

            var (getter, len) = WeightsAccessor.GetAccessor(gltf, 0);
            Assert.AreEqual((1.0f, 2.0f, 3.0f, 4.0f), getter(0));
            Assert.AreEqual((5.0f, 6.0f, 7.0f, 8.0f), getter(1));
        }

        /// <summary>
        /// 0 1 2
        /// +-+-+
        /// |A|B|
        /// +-+-+
        /// 5 4 3
        ///
        /// 6 vertices
        /// 4 triangles
        /// 2 materials
        /// 
        /// </summary>
        static (GameObject, Mesh) CreateMesh(Material[] materials)
        {
            var unityMesh = new Mesh();
            unityMesh.vertices = new Vector3[]
            {
                new Vector3(0, 1, 0),
                new Vector3(1, 1, 0),
                new Vector3(2, 1, 0),
                new Vector3(2, 0, 0),
                new Vector3(1, 0, 0),
                new Vector3(0, 0, 0),
            };
            unityMesh.uv = new Vector2[]
            {
                new Vector2(0, 0),
                new Vector2(1, 0),
                new Vector2(2, 0),
                new Vector2(2, 1),
                new Vector2(1, 1),
                new Vector2(0, 1),
            };

            unityMesh.subMeshCount = 2;
            unityMesh.SetTriangles(new int[]{
                0, 1, 5,
                5, 1, 4,
            }, 0);
            unityMesh.SetTriangles(new int[]{
                1, 2, 4,
                4, 2, 3,
            }, 1);

            unityMesh.RecalculateNormals();
            unityMesh.RecalculateTangents();

            var go = new GameObject();
            go.AddComponent<MeshRenderer>().sharedMaterials = materials;
            go.AddComponent<MeshFilter>().sharedMesh = unityMesh;

            return (go, unityMesh);
        }

        [Test]
        public void SharedVertexBufferTest()
        {
            var glTF = new glTF();
            var bytesBuffer = new ArrayByteBuffer(new byte[50 * 1024 * 1024]);
            var bufferIndex = glTF.AddBuffer(bytesBuffer);

            var Materials = new List<Material>{
                new Material(Shader.Find("Standard")), // A
                new Material(Shader.Find("Standard")), // B
            };

            var (go, mesh) = CreateMesh(Materials.ToArray());
            var meshExportSettings = new GltfExportSettings
            {
                DivideVertexBuffer = false
            };
            var axisInverter = Axes.X.Create();

            var unityMesh = MeshExportList.Create(go);
            var (gltfMesh, blendShapeIndexMap) = meshExportSettings.DivideVertexBuffer
                ? MeshExporter_DividedVertexBuffer.Export(glTF, bufferIndex, unityMesh, Materials, axisInverter, meshExportSettings)
                : MeshExporter_SharedVertexBuffer.Export(glTF, bufferIndex, unityMesh, Materials,axisInverter, meshExportSettings)
                ;

            {
                var indices = glTF.GetIndices(gltfMesh.primitives[0].indices);
                Assert.AreEqual(0, indices[0]);
                Assert.AreEqual(1, indices[1]);
                Assert.AreEqual(5, indices[2]);
                Assert.AreEqual(5, indices[3]);
                Assert.AreEqual(1, indices[4]);
                Assert.AreEqual(4, indices[5]);
            }
            {
                var indices = glTF.GetIndices(gltfMesh.primitives[1].indices);
                Assert.AreEqual(1, indices[0]);
                Assert.AreEqual(2, indices[1]);
                Assert.AreEqual(4, indices[2]);
                Assert.AreEqual(4, indices[3]);
                Assert.AreEqual(2, indices[4]);
                Assert.AreEqual(3, indices[5]);
            }

            var positions = glTF.GetArrayFromAccessor<Vector3>(gltfMesh.primitives[0].attributes.POSITION);
            Assert.AreEqual(6, positions.Length);
        }

        [Test]
        public void DividedVertexBufferTest()
        {
            var glTF = new glTF();
            var bytesBuffer = new ArrayByteBuffer(new byte[50 * 1024 * 1024]);
            var bufferIndex = glTF.AddBuffer(bytesBuffer);

            var Materials = new List<Material>{
                new Material(Shader.Find("Standard")), // A
                new Material(Shader.Find("Standard")), // B
            };

            var (go, mesh) = CreateMesh(Materials.ToArray());
            var meshExportSettings = new GltfExportSettings
            {
                DivideVertexBuffer = true
            };
            var axisInverter = Axes.X.Create();

            var unityMesh = MeshExportList.Create(go);
            var (gltfMesh, blendShapeIndexMap) = meshExportSettings.DivideVertexBuffer
                ? MeshExporter_DividedVertexBuffer.Export(glTF, bufferIndex, unityMesh, Materials, axisInverter, meshExportSettings)
                : MeshExporter_SharedVertexBuffer.Export(glTF, bufferIndex, unityMesh, Materials,axisInverter, meshExportSettings)
                ;

            {
                var indices = glTF.GetIndices(gltfMesh.primitives[0].indices);
                Assert.AreEqual(0, indices[0]);
                Assert.AreEqual(1, indices[1]);
                Assert.AreEqual(3, indices[2]);
                Assert.AreEqual(3, indices[3]);
                Assert.AreEqual(1, indices[4]);
                Assert.AreEqual(2, indices[5]);
            }
            {
                var positions = glTF.GetArrayFromAccessor<Vector3>(gltfMesh.primitives[0].attributes.POSITION);
                Assert.AreEqual(4, positions.Length);
            }

            {
                var indices = glTF.GetIndices(gltfMesh.primitives[1].indices);
                Assert.AreEqual(0, indices[0]);
                Assert.AreEqual(1, indices[1]);
                Assert.AreEqual(3, indices[2]);
                Assert.AreEqual(3, indices[3]);
                Assert.AreEqual(1, indices[4]);
                Assert.AreEqual(2, indices[5]);
            }
            {
                var positions = glTF.GetArrayFromAccessor<Vector3>(gltfMesh.primitives[1].attributes.POSITION);
                Assert.AreEqual(4, positions.Length);
            }
        }
    }
}
