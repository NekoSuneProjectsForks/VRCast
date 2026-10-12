namespace VRCast.Tracking
{
    /// <summary>
    /// 全身の姿勢（ボーンごとの回転）の入力元。全身を受信できない入力元（MediaPipe・OpenSeeFace・iFacialMocap）では常に false を返す。
    /// </summary>
    public interface IHumanoidPoseProvider
    {
        /// <summary>
        /// 有効かつ途絶していない姿勢があれば true。pose は受信側が持つものを共有する（書き換えない）。
        /// </summary>
        bool TryGetPose(out HumanoidPoseFrame pose);
    }
}
