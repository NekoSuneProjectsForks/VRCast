using System;
using UnityEngine;
using VRCast.Core;

namespace VRCast.Animations
{
    /// <summary>
    /// Humanoid の待機ポーズ。読込時の姿勢（通常 T ポーズ）を基準に、腕の上下と肘の曲げだけを変える。
    /// 腕を下ろし切ると気を付けの姿勢（上腕が真下から開きの角度だけ外側）になる。
    /// 骨格や衣装によって腕が体・スカートの後ろへ隠れるアバター向けに、下ろした腕の前後・ひねりを左右別に調整できる。
    /// Animator Controller を持たないため、一度設定した姿勢は次の変更まで保持される。
    /// </summary>
    public class PoseController : MonoBehaviour
    {
        // 「肘を曲げ切る」ときの筋肉値（-1〜1）
        private const float ElbowBentMuscle = 0.3f;

        // 変更対象の筋肉名（HumanTrait.MuscleName と一致させる）
        private static readonly string[] ElbowMuscleNames = { "Left Forearm Stretch", "Right Forearm Stretch" };

        private AppSettings _settings;
        private HumanPoseHandler _handler;
        private HumanPose _basePose;
        private float[] _baseMuscles;
        private int[] _elbowIndices;

        // 基準姿勢へ誤差なく戻すための元の位置・回転
        private Transform[] _bones;
        private Vector3[] _bonePositions;
        private Quaternion[] _boneRotations;

        public bool IsAvailable => _handler != null;

        public float ArmDown
        {
            get => _settings.poseArmDown;
            set => SetValue(ref _settings.poseArmDown, value);
        }

        public float ElbowBend
        {
            get => _settings.poseElbowBend;
            set => SetValue(ref _settings.poseElbowBend, value);
        }

        /// <summary>
        /// 下ろした腕の開き（真下から外側への角度、度。両腕共通）。
        /// </summary>
        public float ArmSpread
        {
            get => _settings.poseArmSpread;
            set => SetValue(ref _settings.poseArmSpread, value, 0f, AppSettings.MaxPoseArmSpread);
        }

        /// <summary>
        /// 下ろした左腕・右腕の前後（度、+ = 前。左右はアバターから見た向き）。
        /// </summary>
        public float ArmForwardLeft
        {
            get => _settings.poseArmForwardLeft;
            set => SetValue(ref _settings.poseArmForwardLeft, value, AppSettings.MinPoseArmForward,
                AppSettings.MaxPoseArmForward);
        }

        public float ArmForwardRight
        {
            get => _settings.poseArmForwardRight;
            set => SetValue(ref _settings.poseArmForwardRight, value, AppSettings.MinPoseArmForward,
                AppSettings.MaxPoseArmForward);
        }

        /// <summary>
        /// 下ろした左腕・右腕のひねり（上腕の軸まわり、度、+ = 内側。曲げた肘から先の向きが変わる）。
        /// </summary>
        public float ArmTwistLeft
        {
            get => _settings.poseArmTwistLeft;
            set => SetValue(ref _settings.poseArmTwistLeft, value, -AppSettings.MaxPoseArmTwist,
                AppSettings.MaxPoseArmTwist);
        }

        public float ArmTwistRight
        {
            get => _settings.poseArmTwistRight;
            set => SetValue(ref _settings.poseArmTwistRight, value, -AppSettings.MaxPoseArmTwist,
                AppSettings.MaxPoseArmTwist);
        }

        /// <summary>
        /// 腕の向きの微調整を既定（開き 12 度・前後とひねり 0）へ戻す。
        /// </summary>
        public void ResetArmAdjust()
        {
            _settings.poseArmSpread = AppSettings.DefaultPoseArmSpread;
            _settings.poseArmForwardLeft = 0f;
            _settings.poseArmForwardRight = 0f;
            _settings.poseArmTwistLeft = 0f;
            _settings.poseArmTwistRight = 0f;
            Apply();
        }

        /// <summary>
        /// アバター全体の向き（度）。Humanoid 以外でも有効。
        /// </summary>
        public float BodyYaw
        {
            get => _settings.avatarYaw;
            set
            {
                // 変化があった場合のみルートを回転
                if (Mathf.Approximately(_settings.avatarYaw, value))
                {
                    return;
                }

                _settings.avatarYaw = value;
                ApplyYaw();
            }
        }

        public void Initialize(Animator animator, AppSettings settings)
        {
            _settings = settings;

            // 保存済みの向きを反映
            ApplyYaw();

            // Humanoid 以外はポーズ操作不可（設定値だけ保持）
            if (animator == null || !animator.isHuman || animator.avatar == null)
            {
                return;
            }

            // 基準となる読込時の姿勢を記録
            RecordBones(animator);
            _handler = new HumanPoseHandler(animator.avatar, animator.transform);
            _basePose.muscles = new float[HumanTrait.MuscleCount];
            _handler.GetHumanPose(ref _basePose);
            _baseMuscles = (float[])_basePose.muscles.Clone();

            // 対象筋肉のインデックスを解決
            _elbowIndices = ResolveMuscles(ElbowMuscleNames);

            // 保存済みの度合いを反映
            Apply();
        }

        /// <summary>
        /// 設定値を直接書き換えた後（全設定のリセット等）に、向きと待機ポーズを反映し直す。
        /// </summary>
        public void Reapply()
        {
            ApplyYaw();
            Apply();
        }

        private void ApplyYaw()
        {
            // 本コンポーネントはアバタールートに付く
            transform.localRotation = Quaternion.Euler(0f, _settings.avatarYaw, 0f);
        }

        private void RecordBones(Animator animator)
        {
            // 全 Humanoid ボーンの位置・回転を保存（LastBone は列挙の終端）
            var count = (int)HumanBodyBones.LastBone;
            _bones = new Transform[count];
            _bonePositions = new Vector3[count];
            _boneRotations = new Quaternion[count];
            for (int i = 0; i < count; i++)
            {
                // 未割り当てのボーンは null のまま
                _bones[i] = animator.GetBoneTransform((HumanBodyBones)i);
                if (_bones[i] != null)
                {
                    _bonePositions[i] = _bones[i].localPosition;
                    _boneRotations[i] = _bones[i].localRotation;
                }
            }
        }

        private static int[] ResolveMuscles(string[] names)
        {
            var indices = new int[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                // 見つからなければ -1（Unity の仕様変更時も落ちないように）
                indices[i] = Array.IndexOf(HumanTrait.MuscleName, names[i]);
            }

            return indices;
        }

        private void SetValue(ref float field, float value, float min = 0f, float max = 1f)
        {
            // 範囲補正して変化があった場合のみ再適用
            value = Mathf.Clamp(value, min, max);
            if (Mathf.Approximately(field, value))
            {
                return;
            }

            field = value;
            Apply();
        }

        private void Apply()
        {
            // 初期化前・非 Humanoid は何もしない
            if (_handler == null)
            {
                return;
            }

            // 度合いが 0 なら基準姿勢を誤差なく復元
            if (_settings.poseArmDown <= 0f && _settings.poseElbowBend <= 0f)
            {
                RestoreBones();
                return;
            }

            // 基準の筋肉値から肘だけを補間した姿勢を作る（腕は基準のまま）
            var muscles = (float[])_baseMuscles.Clone();
            LerpMuscles(muscles, _elbowIndices, ElbowBentMuscle, _settings.poseElbowBend);

            // 筋肉値を適用
            HumanPose pose = _basePose;
            pose.muscles = muscles;
            _handler.SetHumanPose(ref pose);

            // 体の位置・向きは Body yaw の影響を受けないよう記録した腰の値へ戻す
            RestoreBone(HumanBodyBones.Hips);

            // 腕は筋肉の可動域に縛られないよう上腕ボーンを直接下ろす
            LowerArm(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, _settings.poseArmDown,
                _settings.poseArmForwardLeft, -_settings.poseArmTwistLeft);
            LowerArm(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, _settings.poseArmDown,
                _settings.poseArmForwardRight, _settings.poseArmTwistRight);
        }

        /// <summary>
        /// 下ろし切った上腕の向き（ワールド）。真下から外側へ spread 度開き、前へ forward 度振る。
        /// </summary>
        public static Vector3 LoweredArmDirection(Vector3 up, Vector3 forward, Vector3 outward, float spread,
            float forwardAngle)
        {
            float side = spread * Mathf.Deg2Rad;
            float front = forwardAngle * Mathf.Deg2Rad;
            Vector3 sagittal = -up * Mathf.Cos(front) + forward * Mathf.Sin(front);
            return sagittal * Mathf.Cos(side) + outward * Mathf.Sin(side);
        }

        private void LowerArm(HumanBodyBones upperBone, HumanBodyBones lowerBone, float amount, float forwardAngle,
            float twist)
        {
            // ボーン未割り当て・度合い 0 は何もしない
            Transform upper = _bones[(int)upperBone];
            Transform lower = _bones[(int)lowerBone];
            if (upper == null || lower == null || amount <= 0f)
            {
                return;
            }

            // 現在（基準姿勢）の上腕の向き。長さ 0 の異常な骨格は対象外
            Vector3 current = lower.position - upper.position;
            if (current.sqrMagnitude < 1e-8f)
            {
                return;
            }

            // 外側はアバターの左右軸のうち上腕が伸びている側
            Vector3 outward = Vector3.Dot(current, transform.right) >= 0f ? transform.right : -transform.right;

            // 真下から開き・前後の角度だけ傾けた向きへ、度合いぶん回す
            Vector3 down = LoweredArmDirection(transform.up, transform.forward, outward, _settings.poseArmSpread,
                forwardAngle);
            Vector3 target = Vector3.Slerp(current.normalized, down, amount);
            upper.rotation = Quaternion.FromToRotation(current, target) * upper.rotation;

            // 上腕の軸まわりにひねる（twist は下向きの軸まわりの角度。右腕は + で、左腕は - で内側へ回る）
            if (!Mathf.Approximately(twist, 0f))
            {
                upper.rotation = Quaternion.AngleAxis(twist * amount, target) * upper.rotation;
            }
        }

        private void LerpMuscles(float[] muscles, int[] indices, float target, float t)
        {
            foreach (int index in indices)
            {
                // 解決できなかった筋肉は無視
                if (index >= 0)
                {
                    muscles[index] = Mathf.Lerp(_baseMuscles[index], target, t);
                }
            }
        }

        private void RestoreBones()
        {
            for (int i = 0; i < _bones.Length; i++)
            {
                RestoreBone((HumanBodyBones)i);
            }
        }

        private void RestoreBone(HumanBodyBones id)
        {
            // 割り当てのあるボーンだけ記録した位置・回転へ戻す
            Transform bone = _bones[(int)id];
            if (bone != null)
            {
                bone.SetLocalPositionAndRotation(_bonePositions[(int)id], _boneRotations[(int)id]);
            }
        }

        private void OnDestroy()
        {
            // ネイティブリソースを解放
            _handler?.Dispose();
            _handler = null;
        }
    }
}
