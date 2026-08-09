using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Kanameliser.EditorPlus.Tests
{
    public class MeshInfoCalculatorTests
    {
        private readonly List<Object> createdObjects = new();
        private MeshInfoCalculator calculator;

        [SetUp]
        public void SetUp()
        {
            calculator = new MeshInfoCalculator();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = createdObjects.Count - 1; i >= 0; i--)
            {
                if (createdObjects[i] != null)
                {
                    Object.DestroyImmediate(createdObjects[i]);
                }
            }

            createdObjects.Clear();
        }

        private GameObject CreateGameObject(string name, GameObject parent = null)
        {
            var gameObject = new GameObject(name);
            if (parent != null)
            {
                gameObject.transform.SetParent(parent.transform, false);
            }

            createdObjects.Add(gameObject);
            return gameObject;
        }

        private GameObject CreateCube(string name, GameObject parent = null)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            if (parent != null)
            {
                cube.transform.SetParent(parent.transform, false);
            }

            createdObjects.Add(cube);
            return cube;
        }

        [Test]
        public void CalculateMeshInfo_CountsParticleSystemAndItsMaterialSlots()
        {
            var root = CreateGameObject("Root");
            var particleObject = CreateGameObject("Particles", root);
            particleObject.AddComponent<ParticleSystem>();
            var particleRenderer = particleObject.GetComponent<ParticleSystemRenderer>();

            var data = calculator.CalculateMeshInfo(new[] { root });

            Assert.AreEqual(1, data.ParticleSystems);
            Assert.GreaterOrEqual(data.ParticleMaterialSlots, 1);
            Assert.AreEqual(particleRenderer.sharedMaterials.Length, data.ParticleMaterialSlots);
            Assert.AreEqual(0, data.TrailLineMaterialSlots);
        }

        [Test]
        public void CalculateMeshInfo_CountsNestedParticleSystems()
        {
            var root = CreateGameObject("Root");
            var first = CreateGameObject("First", root);
            first.AddComponent<ParticleSystem>();
            var second = CreateGameObject("Second", first);
            second.AddComponent<ParticleSystem>();

            var data = calculator.CalculateMeshInfo(new[] { root });

            Assert.AreEqual(2, data.ParticleSystems);
        }

        [Test]
        public void CalculateMeshInfo_CountsTrailAndLineRendererMaterialSlots()
        {
            var root = CreateGameObject("Root");
            var trailObject = CreateGameObject("Trail", root);
            var trailRenderer = trailObject.AddComponent<TrailRenderer>();
            var lineObject = CreateGameObject("Line", root);
            var lineRenderer = lineObject.AddComponent<LineRenderer>();

            var data = calculator.CalculateMeshInfo(new[] { root });

            Assert.AreEqual(0, data.ParticleSystems);
            Assert.AreEqual(0, data.ParticleMaterialSlots);
            Assert.AreEqual(trailRenderer.sharedMaterials.Length + lineRenderer.sharedMaterials.Length,
                data.TrailLineMaterialSlots);
        }

        [Test]
        public void CalculateMeshInfo_SkipsEditorOnlyParticleSystems()
        {
            var root = CreateGameObject("Root");
            var particleObject = CreateGameObject("Particles", root);
            particleObject.tag = "EditorOnly";
            particleObject.AddComponent<ParticleSystem>();

            var data = calculator.CalculateMeshInfo(new[] { root });

            Assert.AreEqual(0, data.ParticleSystems);
            Assert.AreEqual(0, data.ParticleMaterialSlots);
        }

        [Test]
        public void CalculateMeshInfo_KeepsMeshCountsSeparateFromParticleCounts()
        {
            var root = CreateGameObject("Root");
            var cube = CreateCube("Cube", root);
            var meshRenderer = cube.GetComponent<MeshRenderer>();
            var particleObject = CreateGameObject("Particles", root);
            particleObject.AddComponent<ParticleSystem>();

            var data = calculator.CalculateMeshInfo(new[] { root });

            // Mesh-based counts must not include particle materials or slots
            Assert.AreEqual(12, data.Triangles);
            Assert.AreEqual(1, data.Meshes);
            Assert.AreEqual(meshRenderer.sharedMaterials.Length, data.MaterialSlots);
            Assert.AreEqual(1, data.Materials);
            Assert.AreEqual(1, data.ParticleSystems);
        }
    }
}
