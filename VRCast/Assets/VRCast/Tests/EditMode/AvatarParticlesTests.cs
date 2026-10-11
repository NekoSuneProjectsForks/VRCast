using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VRCast.Avatars;
using VRCast.Core;

namespace VRCast.Tests
{
    /// <summary>
    /// アバターのパーティクルのまとまりの検出と、ON/OFF・記録の復元を検証する。
    /// </summary>
    public class AvatarParticlesTests
    {
        private GameObject _root;

        [SetUp]
        public void SetUp()
        {
            // ルート直下に表示中のエフェクト（子のパーティクル付き）と、非表示の親の下のエフェクトを用意
            _root = new GameObject("Avatar");
            GameObject aura = AddParticle("Aura", _root.transform);
            AddParticle("Sparkle", aura.transform);
            var hidden = new GameObject("Toggle");
            hidden.transform.SetParent(_root.transform, false);
            hidden.SetActive(false);
            AddParticle("Hearts", hidden.transform);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
        }

        [Test]
        public void Initialize_GroupsByTopmostParticleAndReadsVisibility()
        {
            AvatarParticles particles = Create(new List<ParticleToggle>());

            // 子のパーティクルは親のまとまりに含め、非表示の親の下のものは OFF とすること
            Assert.That(particles.Groups.Count, Is.EqualTo(2));
            Assert.That(particles.Groups[0].Path, Is.EqualTo("Aura"));
            Assert.That(particles.Groups[0].Enabled, Is.True);
            Assert.That(particles.Groups[1].Path, Is.EqualTo("Toggle/Hearts"));
            Assert.That(particles.Groups[1].Enabled, Is.False);
        }

        [Test]
        public void SetEnabled_TogglesRenderersAndShowsHiddenParents()
        {
            AvatarParticles particles = Create(new List<ParticleToggle>());

            // OFF は子のパーティクルの描画も止め、オブジェクトは隠さないこと
            particles.SetEnabled(particles.Groups[0], false);
            Assert.That(Renderer("Aura/Sparkle").enabled, Is.False);
            Assert.That(_root.transform.Find("Aura").gameObject.activeSelf, Is.True);

            // ON は非表示の親も表示すること
            particles.SetEnabled(particles.Groups[1], true);
            Assert.That(_root.transform.Find("Toggle").gameObject.activeSelf, Is.True);
            Assert.That(Renderer("Toggle/Hearts").enabled, Is.True);
            Assert.That(particles.EnabledCount, Is.EqualTo(1));
        }

        [Test]
        public void Initialize_RestoresSavedToggles()
        {
            // 記録どおりに当て、見つからないパスは無視すること
            AvatarParticles particles = Create(new List<ParticleToggle>
            {
                new ParticleToggle { path = "Aura", enabled = false },
                new ParticleToggle { path = "Toggle/Hearts", enabled = true },
                new ParticleToggle { path = "Missing", enabled = true },
            });
            Assert.That(particles.Groups[0].Enabled, Is.False);
            Assert.That(Renderer("Aura").enabled, Is.False);
            Assert.That(particles.Groups[1].Enabled, Is.True);

            // 書き出すと全てのまとまりの状態になること
            List<ParticleToggle> exported = particles.Export();
            Assert.That(exported.Count, Is.EqualTo(2));
            Assert.That(exported[0].enabled, Is.False);
            Assert.That(exported[1].enabled, Is.True);
        }

        private AvatarParticles Create(List<ParticleToggle> toggles)
        {
            // ルートに付けて初期化
            var particles = _root.AddComponent<AvatarParticles>();
            particles.Initialize(_root.transform, toggles);
            return particles;
        }

        private ParticleSystemRenderer Renderer(string path)
        {
            // パスでパーティクルの描画を取る
            return _root.transform.Find(path).GetComponent<ParticleSystemRenderer>();
        }

        private static GameObject AddParticle(string name, Transform parent)
        {
            // ParticleSystem を付けると描画（ParticleSystemRenderer）も付く
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<ParticleSystem>();
            return go;
        }
    }
}
