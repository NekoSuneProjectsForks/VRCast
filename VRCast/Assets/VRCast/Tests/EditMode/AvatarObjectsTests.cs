using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VRCast.Avatars;
using VRCast.Core;

namespace VRCast.Tests
{
    /// <summary>
    /// アバターの階層の一覧（並び・描画の有無・FX の印）と、表示の ON/OFF・記録の復元・元に戻す操作を検証する。
    /// </summary>
    public class AvatarObjectsTests
    {
        private GameObject _root;

        [SetUp]
        public void SetUp()
        {
            // ルート直下:
            //   Armature/Hips/Hat（Hat はメッシュ付き）
            //   Body（メッシュ付き）
            //   Outfit（非表示、FX で切り替える）の下に Jacket（メッシュ付き）
            _root = new GameObject("Avatar");
            GameObject armature = AddChild("Armature", _root.transform);
            GameObject hips = AddChild("Hips", armature.transform);
            AddChild("Hat", hips.transform).AddComponent<MeshRenderer>();
            AddChild("Body", _root.transform).AddComponent<SkinnedMeshRenderer>();
            GameObject outfit = AddChild("Outfit", _root.transform);
            AddChild("Jacket", outfit.transform).AddComponent<MeshRenderer>();
            outfit.SetActive(false);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
        }

        [Test]
        public void Initialize_ListsHierarchyInOrderWithoutRoot()
        {
            AvatarObjects objects = Create(new List<ObjectToggle>(), "Outfit");

            // ルート自身を除き、ヒエラルキーと同じ深さ優先の並びになること
            Assert.That(Paths(objects), Is.EqualTo(new[]
            {
                "Armature", "Armature/Hips", "Armature/Hips/Hat", "Body", "Outfit", "Outfit/Jacket",
            }));
            Assert.That(objects.Nodes[2].Depth, Is.EqualTo(2));
            Assert.That(objects.Nodes[0].SubtreeEnd, Is.EqualTo(2));
            Assert.That(objects.Nodes[0].HasChildren, Is.True);
            Assert.That(objects.Nodes[3].HasChildren, Is.False);

            // 子孫の描画と FX の切り替えを親へ伝えること
            Assert.That(objects.Nodes[1].HasRenderer, Is.True);
            Assert.That(objects.Nodes[4].IsFxToggle, Is.True);
            Assert.That(objects.FxToggleCount, Is.EqualTo(1));

            // 読込時の状態を覚え、親が OFF の子は見えていない扱いになること
            Assert.That(objects.Nodes[4].Active, Is.False);
            Assert.That(objects.Nodes[5].Active, Is.True);
            Assert.That(objects.Nodes[5].Visible, Is.False);
        }

        [Test]
        public void SetActive_RecordsOnlyChangesFromInitialState()
        {
            AvatarObjects objects = Create(new List<ObjectToggle>());

            // 読込時と違う状態だけを記録すること
            objects.SetActive(objects.Nodes[4], true);
            objects.SetActive(objects.Nodes[3], false);
            Assert.That(_root.transform.Find("Outfit").gameObject.activeSelf, Is.True);
            Assert.That(objects.ChangedCount, Is.EqualTo(2));

            // 読込時の状態へ戻したものは記録から外すこと
            objects.SetActive(objects.Nodes[3], true);
            List<ObjectToggle> exported = objects.Export();
            Assert.That(exported.Count, Is.EqualTo(1));
            Assert.That(exported[0].path, Is.EqualTo("Outfit"));
            Assert.That(exported[0].active, Is.True);
        }

        [Test]
        public void Initialize_RestoresSavedTogglesAndIgnoresUnknownPaths()
        {
            // 記録どおりに当て、見つからないパス・ルート自身は無視すること
            AvatarObjects objects = Create(new List<ObjectToggle>
            {
                new ObjectToggle { path = "Outfit", active = true },
                new ObjectToggle { path = "Armature/Hips/Hat", active = false },
                new ObjectToggle { path = "Missing", active = false },
                new ObjectToggle { path = string.Empty, active = false },
            });
            Assert.That(_root.activeSelf, Is.True);
            Assert.That(_root.transform.Find("Outfit").gameObject.activeSelf, Is.True);
            Assert.That(_root.transform.Find("Armature/Hips/Hat").gameObject.activeSelf, Is.False);
            Assert.That(objects.ChangedCount, Is.EqualTo(2));
        }

        [Test]
        public void ResetAll_RestoresOnlyChangedObjects()
        {
            AvatarObjects objects = Create(new List<ObjectToggle>());
            objects.SetActive(objects.Nodes[4], true);

            // 一覧の外で表示したもの（パーティクルの ON で表示した親など）は触らないこと
            _root.transform.Find("Armature/Hips/Hat").gameObject.SetActive(false);
            objects.ResetAll();
            Assert.That(_root.transform.Find("Outfit").gameObject.activeSelf, Is.False);
            Assert.That(_root.transform.Find("Armature/Hips/Hat").gameObject.activeSelf, Is.False);
            Assert.That(objects.ChangedCount, Is.EqualTo(0));
        }

        private AvatarObjects Create(List<ObjectToggle> toggles, params string[] fxPaths)
        {
            // ルートに付けて初期化
            var objects = _root.AddComponent<AvatarObjects>();
            objects.Initialize(_root.transform, fxPaths, toggles);
            return objects;
        }

        private static string[] Paths(AvatarObjects objects)
        {
            // 行のパスを並び順のまま取る
            var paths = new List<string>();
            foreach (AvatarObjects.Node node in objects.Nodes)
            {
                paths.Add(node.Path);
            }

            return paths.ToArray();
        }

        private static GameObject AddChild(string name, Transform parent)
        {
            // 空のオブジェクトを親の下に作る
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }
    }
}
