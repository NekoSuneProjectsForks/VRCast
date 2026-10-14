using NUnit.Framework;
using UnityEngine;
using VRCast.Converter.Editor;

namespace VRCast.Tests
{
    /// <summary>
    /// OutfitPoseChecker の衣装の姿勢の付け直し（T ポーズの衣装を A ポーズのアバターへ）を検証する。
    /// </summary>
    public class OutfitPoseCheckerTests
    {
        private const float Tolerance = 1e-3f;

        private GameObject _root;
        private Transform _upper;
        private Transform _lower;
        private Transform _hand;
        private Transform _finger;

        [SetUp]
        public void SetUp()
        {
            // A ポーズの左腕（肩から 45° 下へ、肘はまっすぐ）
            _root = new GameObject("Avatar");
            Vector3 down = new Vector3(1f, -1f, 0f).normalized;
            _upper = CreateBone("UpperArm", _root.transform, new Vector3(0.2f, 1.4f, 0f));
            _lower = CreateBone("LowerArm", _upper, _upper.position + down * 0.25f);
            _hand = CreateBone("Hand", _lower, _lower.position + down * 0.25f);
            _finger = CreateBone("Finger", _hand, _hand.position + down * 0.08f);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
        }

        private static Transform CreateBone(string name, Transform parent, Vector3 position)
        {
            var bone = new GameObject(name).transform;
            bone.SetParent(parent, false);
            bone.position = position;
            return bone;
        }

        private void AddTPoseOutfit(string prefix)
        {
            // 統合後の T ポーズの衣装（各ボーンがアバターの同名ボーンの直下、腕は真横）
            Vector3 shoulder = _upper.position;
            CreateBone(prefix + "UpperArm", _upper, shoulder);
            CreateBone(prefix + "LowerArm", _lower, shoulder + Vector3.right * 0.25f);
            CreateBone(prefix + "Hand", _hand, shoulder + Vector3.right * 0.5f);
            CreateBone(prefix + "Finger", _finger, shoulder + Vector3.right * 0.58f);
        }

        [Test]
        public void FixChain_TPoseOutfitOnAPoseAvatar_MovesJointsOntoAvatar()
        {
            AddTPoseOutfit("Outfit_");

            OutfitPoseChecker.FixChain(new[] { _upper, _lower, _hand }, "Outfit_", string.Empty);

            // 関節がアバターの関節に重なり、指（先端から先）も一緒についてくること
            Assert.That(Vector3.Distance(_lower.Find("Outfit_LowerArm").position, _lower.position), Is.LessThan(Tolerance));
            Assert.That(Vector3.Distance(_hand.Find("Outfit_Hand").position, _hand.position), Is.LessThan(Tolerance));
            Assert.That(Vector3.Distance(_finger.Find("Outfit_Finger").position, _finger.position), Is.LessThan(Tolerance));
        }

        [Test]
        public void FixChain_RotatesOutfitBonesByTheSameAngle()
        {
            AddTPoseOutfit("Outfit_");
            Transform outfitUpper = _upper.Find("Outfit_UpperArm");

            OutfitPoseChecker.FixChain(new[] { _upper, _lower, _hand }, "Outfit_", string.Empty);

            // 真横 → 45° 下へ回した分だけ、衣装の付け根のボーンが回っていること（付け根の位置は動かない）
            Assert.That(Quaternion.Angle(outfitUpper.rotation, Quaternion.Euler(0f, 0f, -45f)), Is.LessThan(0.1f));
            Assert.That(Vector3.Distance(outfitUpper.position, _upper.position), Is.LessThan(Tolerance));
        }

        [Test]
        public void FixChain_OtherOutfitIsNotMoved()
        {
            // 前後の文字列が違う衣装は対象外
            AddTPoseOutfit("Other_");
            Vector3 before = _lower.Find("Other_LowerArm").position;

            OutfitPoseChecker.FixChain(new[] { _upper, _lower, _hand }, "Outfit_", string.Empty);

            Assert.That(_lower.Find("Other_LowerArm").position, Is.EqualTo(before));
        }
    }
}
