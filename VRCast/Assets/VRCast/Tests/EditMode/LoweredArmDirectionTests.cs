using NUnit.Framework;
using UnityEngine;
using VRCast.Animations;

namespace VRCast.Tests
{
    /// <summary>
    /// 待機ポーズで下ろした上腕の向き（開き・前後）の計算を検証する。アバターは +Z を向き、右腕は +X 側。
    /// </summary>
    public class LoweredArmDirectionTests
    {
        private const float Tolerance = 1e-4f;

        [Test]
        public void NoSpreadNoForward_PointsStraightDown()
        {
            Vector3 direction = PoseController.LoweredArmDirection(Vector3.up, Vector3.forward, Vector3.right, 0f, 0f);
            Assert.That(Vector3.Distance(direction, Vector3.down), Is.LessThan(Tolerance));
        }

        [Test]
        public void Spread_OpensOutwardOnly()
        {
            // 開きだけなら前後には出ず、真下から外側へその角度だけ傾く
            Vector3 direction = PoseController.LoweredArmDirection(Vector3.up, Vector3.forward, Vector3.right, 12f, 0f);
            Assert.That(direction.z, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(direction.x, Is.GreaterThan(0f));
            Assert.That(Vector3.Angle(direction, Vector3.down), Is.EqualTo(12f).Within(0.01f));
        }

        [Test]
        public void Forward_MovesArmInFrontAndBackward_MovesBehind()
        {
            Vector3 front = PoseController.LoweredArmDirection(Vector3.up, Vector3.forward, Vector3.right, 12f, 20f);
            Vector3 back = PoseController.LoweredArmDirection(Vector3.up, Vector3.forward, Vector3.right, 12f, -20f);
            Assert.That(front.z, Is.GreaterThan(0f));
            Assert.That(back.z, Is.LessThan(0f));

            // 開きの向き（外側）は前後に関係なく同じ
            Assert.That(front.x, Is.EqualTo(back.x).Within(Tolerance));
        }

        [Test]
        public void Direction_IsUnitLength()
        {
            Vector3 direction = PoseController.LoweredArmDirection(Vector3.up, Vector3.forward, -Vector3.right, 40f, 50f);
            Assert.That(direction.magnitude, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(direction.x, Is.LessThan(0f));
        }
    }
}
