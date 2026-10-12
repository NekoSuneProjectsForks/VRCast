using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRCast.Tracking
{
    /// <summary>
    /// 送信側（正規化済み VRM）の全身の姿勢を、読み込んだアバターへ写すための計算。
    /// 送信側はローカル回転を親から順に掛けると「T ポーズからの回転（アバタールート基準）」になるので、
    /// それをアバターの T ポーズの回転（アバタールート基準）に掛けたものを、アバターのボーンの向きとする。
    /// </summary>
    public static class HumanoidRetarget
    {
        // 指の本数・1 本あたりの節の数と、左右の親指の付け根（HumanBodyBones の値）
        private const int FingerCount = 5;
        private const int JointsPerFinger = 3;
        private const int LeftFingerStart = (int)HumanBodyBones.LeftThumbProximal;
        private const int RightFingerStart = (int)HumanBodyBones.RightThumbProximal;

        /// <summary>
        /// 送信側の骨格での親ボーン（HumanBodyBones の値。腰は -1）。省略できるボーン（上胸等）も含めた完全な骨格での親。
        /// </summary>
        public static readonly int[] Parent = CreateParents();

        /// <summary>
        /// 親が子より先に来る並び（送信側の回転を親から順に掛けるため）。
        /// </summary>
        public static readonly int[] ParentFirstOrder = CreateOrder();

        /// <summary>
        /// 左右を入れ替えたボーン（中央のボーンは自分自身）。
        /// </summary>
        public static readonly int[] Mirror = CreateMirror();

        /// <summary>
        /// 全身の追従で動かさないボーン（目・顎は顔のトラッキングが受け持つ）。
        /// </summary>
        public static bool IsExcluded(int bone)
        {
            return bone == (int)HumanBodyBones.LeftEye || bone == (int)HumanBodyBones.RightEye
                || bone == (int)HumanBodyBones.Jaw;
        }

        /// <summary>
        /// 送信側の各ボーンの「T ポーズからの回転（送信側アバタールート基準）」を求める。
        /// root は腰より上の回転（送信側のルートの向きと、正面の補正）。届いていないボーンは親と同じ回転（ローカル回転なし）とする。
        /// </summary>
        public static void ComputeAvatarRotations(HumanoidPoseFrame pose, Quaternion root, Quaternion[] result)
        {
            foreach (int bone in ParentFirstOrder)
            {
                // 親の回転（腰はルート）に、届いていればそのボーンのローカル回転を掛ける
                int parent = Parent[bone];
                Quaternion parentRotation = parent >= 0 ? result[parent] : root;
                result[bone] = pose.Received[bone] ? parentRotation * pose.Rotations[bone] : parentRotation;
            }
        }

        /// <summary>
        /// 送信側の腰の向きの左右（縦軸まわり、度）。正面の補正に使う。腰が真上・真下を向いていれば 0。
        /// </summary>
        public static float YawOf(Quaternion rotation)
        {
            // 前方向を水平面へ落とした向きの角度
            Vector3 forward = rotation * Vector3.forward;
            return new Vector2(forward.x, forward.z).sqrMagnitude < 1e-6f
                ? 0f
                : Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        }

        private static int[] CreateParents()
        {
            var parents = new int[HumanoidPoseFrame.BoneCount];

            // 体幹・脚・腕・頭（上胸は胸の子、首と肩は上胸の子）
            Set(parents, HumanBodyBones.Hips, null);
            Set(parents, HumanBodyBones.LeftUpperLeg, HumanBodyBones.Hips);
            Set(parents, HumanBodyBones.RightUpperLeg, HumanBodyBones.Hips);
            Set(parents, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftUpperLeg);
            Set(parents, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightUpperLeg);
            Set(parents, HumanBodyBones.LeftFoot, HumanBodyBones.LeftLowerLeg);
            Set(parents, HumanBodyBones.RightFoot, HumanBodyBones.RightLowerLeg);
            Set(parents, HumanBodyBones.LeftToes, HumanBodyBones.LeftFoot);
            Set(parents, HumanBodyBones.RightToes, HumanBodyBones.RightFoot);
            Set(parents, HumanBodyBones.Spine, HumanBodyBones.Hips);
            Set(parents, HumanBodyBones.Chest, HumanBodyBones.Spine);
            Set(parents, HumanBodyBones.UpperChest, HumanBodyBones.Chest);
            Set(parents, HumanBodyBones.Neck, HumanBodyBones.UpperChest);
            Set(parents, HumanBodyBones.Head, HumanBodyBones.Neck);
            Set(parents, HumanBodyBones.LeftEye, HumanBodyBones.Head);
            Set(parents, HumanBodyBones.RightEye, HumanBodyBones.Head);
            Set(parents, HumanBodyBones.Jaw, HumanBodyBones.Head);
            Set(parents, HumanBodyBones.LeftShoulder, HumanBodyBones.UpperChest);
            Set(parents, HumanBodyBones.RightShoulder, HumanBodyBones.UpperChest);
            Set(parents, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftShoulder);
            Set(parents, HumanBodyBones.RightUpperArm, HumanBodyBones.RightShoulder);
            Set(parents, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftUpperArm);
            Set(parents, HumanBodyBones.RightLowerArm, HumanBodyBones.RightUpperArm);
            Set(parents, HumanBodyBones.LeftHand, HumanBodyBones.LeftLowerArm);
            Set(parents, HumanBodyBones.RightHand, HumanBodyBones.RightLowerArm);

            // 指は付け根が手の子、残りの節は 1 つ前の節の子
            SetFingers(parents, LeftFingerStart, (int)HumanBodyBones.LeftHand);
            SetFingers(parents, RightFingerStart, (int)HumanBodyBones.RightHand);
            return parents;
        }

        private static void Set(int[] parents, HumanBodyBones bone, HumanBodyBones? parent)
        {
            // 親なし（腰）は -1
            parents[(int)bone] = parent.HasValue ? (int)parent.Value : -1;
        }

        private static void SetFingers(int[] parents, int start, int hand)
        {
            for (int finger = 0; finger < FingerCount; finger++)
            {
                // 付け根は手、2 節目以降は 1 つ前の節
                int proximal = start + finger * JointsPerFinger;
                parents[proximal] = hand;
                for (int joint = 1; joint < JointsPerFinger; joint++)
                {
                    parents[proximal + joint] = proximal + joint - 1;
                }
            }
        }

        private static int[] CreateOrder()
        {
            // 腰からの深さ（親をたどった数）が浅い順に並べる（同じ深さは HumanBodyBones の順）
            var bones = new List<int>(HumanoidPoseFrame.BoneCount);
            for (int bone = 0; bone < HumanoidPoseFrame.BoneCount; bone++)
            {
                bones.Add(bone);
            }

            var depths = new int[HumanoidPoseFrame.BoneCount];
            for (int bone = 0; bone < HumanoidPoseFrame.BoneCount; bone++)
            {
                for (int parent = Parent[bone]; parent >= 0; parent = Parent[parent])
                {
                    depths[bone]++;
                }
            }

            bones.Sort((a, b) => depths[a] != depths[b] ? depths[a].CompareTo(depths[b]) : a.CompareTo(b));
            return bones.ToArray();
        }

        private static int[] CreateMirror()
        {
            // 名前の Left と Right を入れ替えたボーン（どちらも含まない中央のボーンは自分自身）
            var mirror = new int[HumanoidPoseFrame.BoneCount];
            for (int bone = 0; bone < HumanoidPoseFrame.BoneCount; bone++)
            {
                string name = ((HumanBodyBones)bone).ToString();
                string swapped = name.StartsWith("Left", StringComparison.Ordinal)
                    ? "Right" + name.Substring("Left".Length)
                    : name.StartsWith("Right", StringComparison.Ordinal)
                        ? "Left" + name.Substring("Right".Length)
                        : name;
                mirror[bone] = (int)(HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), swapped);
            }

            return mirror;
        }
    }
}
