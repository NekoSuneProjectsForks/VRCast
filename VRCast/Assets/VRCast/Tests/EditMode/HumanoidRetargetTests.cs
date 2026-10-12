using NUnit.Framework;
using UnityEngine;
using VRCast.Tracking;

namespace VRCast.Tests
{
    /// <summary>
    /// 全身の姿勢をアバターへ写す計算（親子の並び・左右の対応・T ポーズからの回転・正面の向き）を検証する。
    /// </summary>
    public class HumanoidRetargetTests
    {
        [Test]
        public void ParentFirstOrder_PlacesParentsBeforeChildren()
        {
            // すべてのボーンが 1 回ずつ並び、親が子より先に来ること
            var position = new int[HumanoidPoseFrame.BoneCount];
            for (int i = 0; i < HumanoidRetarget.ParentFirstOrder.Length; i++)
            {
                position[HumanoidRetarget.ParentFirstOrder[i]] = i;
            }

            Assert.That(HumanoidRetarget.ParentFirstOrder.Length, Is.EqualTo(HumanoidPoseFrame.BoneCount));
            for (int bone = 0; bone < HumanoidPoseFrame.BoneCount; bone++)
            {
                int parent = HumanoidRetarget.Parent[bone];
                if (parent >= 0)
                {
                    Assert.That(position[parent], Is.LessThan(position[bone]), ((HumanBodyBones)bone).ToString());
                }
            }

            // 腰だけが親を持たないこと
            Assert.That(HumanoidRetarget.Parent[(int)HumanBodyBones.Hips], Is.EqualTo(-1));
            Assert.That(HumanoidRetarget.Parent[(int)HumanBodyBones.LeftIndexDistal],
                Is.EqualTo((int)HumanBodyBones.LeftIndexIntermediate));
        }

        [Test]
        public void Mirror_SwapsLeftAndRight()
        {
            // 左右のボーンは入れ替わり、中央のボーンは自分自身であること
            Assert.That(HumanoidRetarget.Mirror[(int)HumanBodyBones.LeftUpperArm], Is.EqualTo((int)HumanBodyBones.RightUpperArm));
            Assert.That(HumanoidRetarget.Mirror[(int)HumanBodyBones.RightLittleDistal],
                Is.EqualTo((int)HumanBodyBones.LeftLittleDistal));
            Assert.That(HumanoidRetarget.Mirror[(int)HumanBodyBones.Spine], Is.EqualTo((int)HumanBodyBones.Spine));
        }

        [Test]
        public void ComputeAvatarRotations_ChainsReceivedLocalRotations()
        {
            // 腰・背骨・左上腕の回転を親から順に掛け、届かないボーン（胸・上胸・左肩）は親の回転を引き継ぐこと
            var pose = new HumanoidPoseFrame();
            Receive(pose, HumanBodyBones.Hips, Quaternion.Euler(0f, 90f, 0f));
            Receive(pose, HumanBodyBones.Spine, Quaternion.Euler(10f, 0f, 0f));
            Receive(pose, HumanBodyBones.LeftUpperArm, Quaternion.Euler(0f, 0f, 60f));

            var result = new Quaternion[HumanoidPoseFrame.BoneCount];
            Quaternion root = Quaternion.Euler(0f, -90f, 0f);
            HumanoidRetarget.ComputeAvatarRotations(pose, root, result);

            Quaternion spine = root * Quaternion.Euler(0f, 90f, 0f) * Quaternion.Euler(10f, 0f, 0f);
            Assert.That(Quaternion.Angle(result[(int)HumanBodyBones.Spine], spine), Is.LessThan(0.01f));
            Assert.That(Quaternion.Angle(result[(int)HumanBodyBones.LeftShoulder], spine), Is.LessThan(0.01f));
            Assert.That(Quaternion.Angle(result[(int)HumanBodyBones.LeftUpperArm], spine * Quaternion.Euler(0f, 0f, 60f)),
                Is.LessThan(0.01f));
        }

        [Test]
        public void YawOf_ReturnsHorizontalTurn()
        {
            // 縦軸まわりの回転だけを角度にし、前後に傾けても変わらないこと
            Assert.That(HumanoidRetarget.YawOf(Quaternion.Euler(0f, 30f, 0f)), Is.EqualTo(30f).Within(0.01f));
            Assert.That(HumanoidRetarget.YawOf(Quaternion.Euler(0f, -45f, 0f) * Quaternion.Euler(20f, 0f, 0f)),
                Is.EqualTo(-45f).Within(0.01f));
        }

        private static void Receive(HumanoidPoseFrame pose, HumanBodyBones bone, Quaternion rotation)
        {
            // 届いたボーンとして回転を入れる
            pose.Rotations[(int)bone] = rotation;
            pose.Received[(int)bone] = true;
            pose.ReceivedCount++;
        }
    }
}
