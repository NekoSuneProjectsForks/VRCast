using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VRCast.Animations;

namespace VRCast.Tests
{
    /// <summary>
    /// まばたきの .anim（Unity のテキスト形式の AnimationClip）の読み取りと、再生する重みを検証する。
    /// </summary>
    public class BlinkAnimationTests
    {
        // Unity 2022 が書き出す形（BlendShape 2 本・Transform のカーブ・エディター用の重複・無限大の傾き）
        private const string TimedClip = @"%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!74 &7400000
AnimationClip:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_Name: Blink
  serializedVersion: 7
  m_Legacy: 0
  m_Compressed: 0
  m_UseHighQualityCurve: 1
  m_RotationCurves: []
  m_CompressedRotationCurves: []
  m_EulerCurves: []
  m_PositionCurves:
  - curve:
      serializedVersion: 2
      m_Curve:
      - serializedVersion: 3
        time: 0
        value: {x: 0, y: 1, z: 0}
        inSlope: {x: 0, y: 0, z: 0}
        outSlope: {x: 0, y: 0, z: 0}
        tangentMode: 0
        weightedMode: 0
        inWeight: {x: 0.33333334, y: 0.33333334, z: 0.33333334}
        outWeight: {x: 0.33333334, y: 0.33333334, z: 0.33333334}
      m_PreInfinity: 2
      m_PostInfinity: 2
      m_RotationOrder: 4
    path: Armature/Hips
  m_ScaleCurves: []
  m_FloatCurves:
  - curve:
      serializedVersion: 2
      m_Curve:
      - serializedVersion: 3
        time: 0
        value: 0
        inSlope: 0
        outSlope: 0
        tangentMode: 136
        weightedMode: 0
        inWeight: 0.33333334
        outWeight: 0.33333334
      - serializedVersion: 3
        time: 0.1
        value: 100
        inSlope: Infinity
        outSlope: Infinity
        tangentMode: 103
        weightedMode: 0
        inWeight: 0.33333334
        outWeight: 0.33333334
      - serializedVersion: 3
        time: 0.3
        value: 0
        inSlope: 0
        outSlope: 0
        tangentMode: 136
        weightedMode: 0
        inWeight: 0.33333334
        outWeight: 0.33333334
      m_PreInfinity: 2
      m_PostInfinity: 2
      m_RotationOrder: 4
    attribute: blendShape.まばたき
    path: Body
    classID: 137
    script: {fileID: 0}
    flags: 0
  - curve:
      serializedVersion: 2
      m_Curve:
      - serializedVersion: 3
        time: 0
        value: 30
        inSlope: 0
        outSlope: 0
        tangentMode: 136
        weightedMode: 0
        inWeight: 0.33333334
        outWeight: 0.33333334
      m_PreInfinity: 2
      m_PostInfinity: 2
      m_RotationOrder: 4
    attribute: blendShape.eye_sad
    path: Body
    classID: 137
    script: {fileID: 0}
    flags: 0
  - curve:
      serializedVersion: 2
      m_Curve:
      - serializedVersion: 3
        time: 0
        value: 1
        inSlope: 0
        outSlope: 0
        tangentMode: 136
        weightedMode: 0
        inWeight: 0.33333334
        outWeight: 0.33333334
      m_PreInfinity: 2
      m_PostInfinity: 2
      m_RotationOrder: 4
    attribute: m_IsActive
    path: Hat
    classID: 1
    script: {fileID: 0}
    flags: 0
  m_PPtrCurves: []
  m_SampleRate: 60
  m_WrapMode: 0
  m_Bounds:
    m_Center: {x: 0, y: 0, z: 0}
    m_Extent: {x: 0, y: 0, z: 0}
  m_ClipBindingConstant:
    genericBindings:
    - serializedVersion: 2
      path: 2706878219
      attribute: 1
      script: {fileID: 0}
      typeID: 137
      customType: 20
      isPPtrCurve: 0
      isIntCurve: 0
      isSerializeReferenceCurve: 0
    pptrCurveMapping: []
  m_AnimationClipSettings:
    serializedVersion: 2
    m_StartTime: 0
    m_StopTime: 0.3
    m_LoopTime: 0
  m_EditorCurves:
  - curve:
      serializedVersion: 2
      m_Curve:
      - serializedVersion: 3
        time: 0
        value: 0
        inSlope: 0
        outSlope: 0
        tangentMode: 136
        weightedMode: 0
        inWeight: 0.33333334
        outWeight: 0.33333334
      m_PreInfinity: 2
      m_PostInfinity: 2
      m_RotationOrder: 4
    attribute: blendShape.まばたき
    path: Body
    classID: 137
    script: {fileID: 0}
    flags: 0
  m_EulerEditorCurves: []
  m_HasGenericRootTransform: 0
  m_HasMotionFloatCurves: 0
  m_Events: []
";

        [Test]
        public void TryParse_ReadsBlendShapeCurvesOnly()
        {
            // BlendShape のカーブだけを、パスと名前付きで読むこと（Transform・表示の切り替え・エディター用の重複は使わない）
            Assert.That(BlinkAnimation.TryParse(TimedClip, out BlinkAnimation animation, out BlinkAnimationError error),
                Is.True, error.ToString());
            Assert.That(animation.Tracks.Select(track => track.BlendShape), Is.EqualTo(new[] { "まばたき", "eye_sad" }));
            Assert.That(animation.Tracks[0].Path, Is.EqualTo("Body"));
            Assert.That(animation.Length, Is.EqualTo(0.3f).Within(1e-5f));
            Assert.That(animation.IsPose, Is.False);
        }

        [Test]
        public void TimedClip_PlaysCurvesAsWritten()
        {
            // 長さのあるクリップはカーブの値をそのまま使い、無限大の傾きのキーは次のキーまで値を保つこと
            BlinkAnimation.TryParse(TimedClip, out BlinkAnimation animation, out _);
            BlinkAnimation.Track blink = animation.Tracks[0];
            Assert.That(animation.Duration(1f, 1f, 1f), Is.EqualTo(0.3f).Within(1e-5f));
            Assert.That(animation.Sample(blink, 0f, 1f, 1f, 1f), Is.EqualTo(0f).Within(1e-3f));
            Assert.That(animation.Sample(blink, 0.1f, 1f, 1f, 1f), Is.EqualTo(100f).Within(1e-3f));
            Assert.That(animation.Sample(blink, 0.2f, 1f, 1f, 1f), Is.EqualTo(100f).Within(1e-3f));
            Assert.That(animation.Sample(blink, 0.3f, 1f, 1f, 1f), Is.EqualTo(0f).Within(1e-3f));
        }

        [Test]
        public void PoseClip_FollowsBlinkTimes()
        {
            // 1 コマだけのクリップは閉じた形として、閉じる・閉じたまま・開くの時間に合わせて出すこと
            string pose = TimedClip.Replace("time: 0.1", "time: 0").Replace("time: 0.3", "time: 0");
            Assert.That(BlinkAnimation.TryParse(pose, out BlinkAnimation animation, out _), Is.True);
            Assert.That(animation.IsPose, Is.True);
            Assert.That(animation.Duration(0.1f, 0.2f, 0.3f), Is.EqualTo(0.6f).Within(1e-5f));

            BlinkAnimation.Track sad = animation.Tracks[1];
            Assert.That(animation.Sample(sad, 0f, 0.1f, 0.2f, 0.3f), Is.EqualTo(0f).Within(1e-4f));
            Assert.That(animation.Sample(sad, 0.2f, 0.1f, 0.2f, 0.3f), Is.EqualTo(30f).Within(1e-4f));
            Assert.That(animation.Sample(sad, 0.6f, 0.1f, 0.2f, 0.3f), Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void TryParse_WithoutBlendShapes_IsRejected()
        {
            string transformOnly = TimedClip.Replace("classID: 137", "classID: 4");
            Assert.That(BlinkAnimation.TryParse(transformOnly, out _, out BlinkAnimationError error), Is.False);
            Assert.That(error, Is.EqualTo(BlinkAnimationError.NoBlendShapes));
        }

        [TestCase("", BlinkAnimationError.Malformed)]
        [TestCase("format: VRCastPreset\n", BlinkAnimationError.NotAnimationClip)]
        [TestCase("AnimationClip:\n  m_Name: \"broken\n", BlinkAnimationError.Malformed)]
        public void TryParse_InvalidText_IsRejected(string text, BlinkAnimationError expected)
        {
            Assert.That(BlinkAnimation.TryParse(text, out BlinkAnimation animation, out BlinkAnimationError error), Is.False);
            Assert.That(animation, Is.Null);
            Assert.That(error, Is.EqualTo(expected));
        }

        [Test]
        public void TryLoad_RejectsBinaryFiles()
        {
            // バイナリ形式で保存された .anim は読めないことを伝えること
            string path = Path.Combine(Application.temporaryCachePath, "binary-blink.anim");
            File.WriteAllBytes(path, new byte[] { 0x00, 0x00, 0x00, 0x16, 0x00, 0x01 });
            try
            {
                Assert.That(BlinkAnimation.TryLoad(path, out _, out BlinkAnimationError error), Is.False);
                Assert.That(error, Is.EqualTo(BlinkAnimationError.NotText));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
