using System;
using UnityEngine;

namespace VRCast.Tracking
{
    /// <summary>
    /// 外部アプリ（VMC プロトコル）から届いた送信側アバターの全身の姿勢。
    /// 回転・位置は送信側アバターの各ボーンのローカル値（親ボーン基準、Unity 座標系）で、
    /// 正規化済みの VRM（T ポーズで全ボーンの回転が単位回転）として扱う。
    /// </summary>
    public sealed class HumanoidPoseFrame
    {
        /// <summary>
        /// Humanoid のボーンの種類数（HumanBodyBones の LastBone は列挙の終端）。
        /// </summary>
        public const int BoneCount = (int)HumanBodyBones.LastBone;

        // ボーンごとのローカル回転・ローカル位置と、受け取ったことがあるか（HumanBodyBones の値で引く）
        public readonly Quaternion[] Rotations = new Quaternion[BoneCount];
        public readonly Vector3[] Positions = new Vector3[BoneCount];
        public readonly bool[] Received = new bool[BoneCount];

        // 送信側アバターのルート（体全体の位置・向き・拡大率）と、受け取ったことがあるか
        public bool HasRoot;
        public Vector3 RootPosition;
        public Quaternion RootRotation = Quaternion.identity;
        public Vector3 RootScale = Vector3.one;

        // 受け取ったことのあるボーンの数（診断・状態表示用）
        public int ReceivedCount;

        // 更新するたびに増える番号（新しい値が届いたかの判定用）
        public int Sequence;

        /// <summary>
        /// 腰と両上腕がそろっていれば true（顔だけの送信元が送る頭・首だけでは全身として扱わない）。
        /// </summary>
        public bool HasBody => Received[(int)HumanBodyBones.Hips]
            && Received[(int)HumanBodyBones.LeftUpperArm] && Received[(int)HumanBodyBones.RightUpperArm];

        /// <summary>
        /// 送信側の腰の位置（ルートの位置・向き・拡大率を反映した、送信側のワールド座標）。
        /// </summary>
        public Vector3 HipsPosition
        {
            get
            {
                // ルートが届いていなければ腰のローカル位置そのまま
                Vector3 local = Positions[(int)HumanBodyBones.Hips];
                return HasRoot ? RootPosition + RootRotation * Vector3.Scale(RootScale, local) : local;
            }
        }

        /// <summary>
        /// 値をすべて捨てる（入力元の切替・待ち受けのやり直し時）。
        /// </summary>
        public void Clear()
        {
            Array.Clear(Rotations, 0, Rotations.Length);
            Array.Clear(Positions, 0, Positions.Length);
            Array.Clear(Received, 0, Received.Length);
            HasRoot = false;
            RootPosition = Vector3.zero;
            RootRotation = Quaternion.identity;
            RootScale = Vector3.one;
            ReceivedCount = 0;
        }

        /// <summary>
        /// source の値を写す（番号は写さない）。
        /// </summary>
        public void CopyFrom(HumanoidPoseFrame source)
        {
            Array.Copy(source.Rotations, Rotations, BoneCount);
            Array.Copy(source.Positions, Positions, BoneCount);
            Array.Copy(source.Received, Received, BoneCount);
            HasRoot = source.HasRoot;
            RootPosition = source.RootPosition;
            RootRotation = source.RootRotation;
            RootScale = source.RootScale;
            ReceivedCount = source.ReceivedCount;
        }
    }
}
