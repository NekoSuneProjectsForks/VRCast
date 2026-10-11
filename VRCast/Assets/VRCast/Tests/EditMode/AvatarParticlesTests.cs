using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VRCast.Avatars;
using VRCast.Core;

namespace VRCast.Tests
{
    /// <summary>
    /// アバターのパーティクルのまとまりの検出（FX の切り替え単位・エフェクトのフォルダー）と、ON/OFF・名前・記録の復元を検証する。
    /// </summary>
    public class AvatarParticlesTests
    {
        private GameObject _root;

        [SetUp]
        public void SetUp()
        {
            // ルート直下:
            //   Aura（表示中、子のパーティクル付き）
            //   Toggle（非表示のフォルダー、中にパーティクル 2 つ）
            //   Head（メッシュのボーン）の下に HeadFx（パーティクル）
            //   Fx（FX で切り替えるオブジェクト、中にメッシュとパーティクル）
            _root = new GameObject("Avatar");
            GameObject aura = AddParticle("Aura", _root.transform);
            AddParticle("Sparkle", aura.transform);

            GameObject hidden = AddChild("Toggle", _root.transform);
            hidden.SetActive(false);
            AddParticle("Hearts", hidden.transform);
            AddParticle("Stars", hidden.transform);

            GameObject head = AddChild("Head", _root.transform);
            AddParticle("HeadFx", head.transform);
            GameObject body = AddChild("Body", _root.transform);
            body.AddComponent<SkinnedMeshRenderer>().bones = new[] { head.transform };

            GameObject fx = AddChild("Fx", _root.transform);
            AddChild("Mesh", fx.transform).AddComponent<MeshRenderer>();
            AddParticle("Smoke", fx.transform);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
        }

        [Test]
        public void Initialize_GroupsByFxToggleAndEffectFolder()
        {
            AvatarParticles particles = Create(new List<ParticleToggle>(), "Fx");

            // 子のパーティクル・同じフォルダーのものはまとめ、ボーンの上へはたどらず、FX の切り替え単位でまとめること
            Assert.That(Paths(particles), Is.EqualTo(new[] { "Aura", "Toggle", "Head/HeadFx", "Fx" }));
            Assert.That(particles.Groups[0].SystemCount, Is.EqualTo(2));
            Assert.That(particles.Groups[1].SystemCount, Is.EqualTo(2));

            // 非表示の親の下のものは OFF とすること
            Assert.That(particles.Groups[0].Enabled, Is.True);
            Assert.That(particles.Groups[1].Enabled, Is.False);
        }

        [Test]
        public void Initialize_WithoutFxToggles_DoesNotClimbIntoMeshFolders()
        {
            AvatarParticles particles = Create(new List<ParticleToggle>());

            // メッシュを含む親はエフェクトのフォルダーとみなさないこと
            Assert.That(Paths(particles), Has.Member("Fx/Smoke"));
        }

        [Test]
        public void SetEnabled_TogglesRenderersAndShowsHiddenParents()
        {
            AvatarParticles particles = Create(new List<ParticleToggle>(), "Fx");

            // OFF は子のパーティクルの描画も止め、オブジェクトは隠さないこと
            particles.SetEnabled(particles.Groups[0], false);
            Assert.That(Renderer("Aura/Sparkle").enabled, Is.False);
            Assert.That(_root.transform.Find("Aura").gameObject.activeSelf, Is.True);

            // ON は非表示の親も表示し、まとめた全てのパーティクルを描くこと
            particles.SetEnabled(particles.Groups[1], true);
            Assert.That(_root.transform.Find("Toggle").gameObject.activeSelf, Is.True);
            Assert.That(Renderer("Toggle/Hearts").enabled, Is.True);
            Assert.That(Renderer("Toggle/Stars").enabled, Is.True);
            Assert.That(particles.EnabledCount, Is.EqualTo(3));
        }

        [Test]
        public void Initialize_RestoresSavedTogglesAndNames()
        {
            // 記録どおりに当て、見つからないパスは無視すること
            AvatarParticles particles = Create(new List<ParticleToggle>
            {
                new ParticleToggle { path = "Aura", enabled = false, name = "  オーラ  " },
                new ParticleToggle { path = "Toggle", enabled = true },
                new ParticleToggle { path = "Missing", enabled = true },
            }, "Fx");
            Assert.That(particles.Groups[0].Enabled, Is.False);
            Assert.That(Renderer("Aura").enabled, Is.False);
            Assert.That(particles.Groups[1].Enabled, Is.True);

            // 名前は前後の空白を除いて表示し、付けていないものは Unity 上の名前を出すこと
            Assert.That(particles.Groups[0].Label, Is.EqualTo("オーラ"));
            Assert.That(particles.Groups[1].Label, Is.EqualTo("Toggle"));

            // 書き出すと全てのまとまりの状態と名前になること
            List<ParticleToggle> exported = particles.Export();
            Assert.That(exported.Count, Is.EqualTo(4));
            Assert.That(exported[0].enabled, Is.False);
            Assert.That(exported[0].name, Is.EqualTo("オーラ"));
            Assert.That(exported[1].enabled, Is.True);
        }

        [Test]
        public void SetName_EmptyRestoresUnityNameAndLongNameIsCut()
        {
            AvatarParticles particles = Create(new List<ParticleToggle>(), "Fx");
            AvatarParticles.Group group = particles.Groups[0];

            // 長すぎる名前は上限で切り、空にすると Unity 上の名前に戻ること
            AvatarParticles.SetName(group, new string('a', AvatarParticles.MaxNameLength + 10));
            Assert.That(group.Label.Length, Is.EqualTo(AvatarParticles.MaxNameLength));
            AvatarParticles.SetName(group, " ");
            Assert.That(group.Label, Is.EqualTo("Aura"));
        }

        private AvatarParticles Create(List<ParticleToggle> toggles, params string[] fxPaths)
        {
            // ルートに付けて初期化
            var particles = _root.AddComponent<AvatarParticles>();
            particles.Initialize(_root.transform, fxPaths, toggles);
            return particles;
        }

        private static string[] Paths(AvatarParticles particles)
        {
            // まとまりのパスを並び順のまま取る
            var paths = new List<string>();
            foreach (AvatarParticles.Group group in particles.Groups)
            {
                paths.Add(group.Path);
            }

            return paths.ToArray();
        }

        private ParticleSystemRenderer Renderer(string path)
        {
            // パスでパーティクルの描画を取る
            return _root.transform.Find(path).GetComponent<ParticleSystemRenderer>();
        }

        private static GameObject AddChild(string name, Transform parent)
        {
            // 空のオブジェクトを親の下に作る
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        private static GameObject AddParticle(string name, Transform parent)
        {
            // ParticleSystem を付けると描画（ParticleSystemRenderer）も付く
            GameObject go = AddChild(name, parent);
            go.AddComponent<ParticleSystem>();
            return go;
        }
    }
}
