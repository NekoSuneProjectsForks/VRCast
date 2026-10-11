using System;
using System.Reflection;
using UnityEngine;

namespace VRCast.Converter.Editor
{
    /// <summary>
    /// VRChat へのアップロード時と同じ前処理（VRCFury・NDMF / Modular Avatar 等）を、アセンブリ参照なしで複製アバターへ適用する。
    /// VRChat SDK の前処理を呼べないとき（SDK が無い・アバターに Avatar Descriptor が無い）は NDMF だけを適用する。
    /// </summary>
    public static class AvatarPreprocessor
    {
        /// <summary>
        /// 適用した前処理の種類。
        /// </summary>
        public enum Mode
        {
            // 何も適用しなかった
            None,
            // VRChat SDK のアップロード前処理をすべて適用した（VRCFury・NDMF 等を含む）
            VrcSdk,
            // NDMF だけを適用した
            NdmfOnly,
        }

        // アップロード前処理（IVRCSDKPreprocessAvatarCallback）を順に呼ぶ SDK の入口
        private const string CallbacksTypeName = "VRC.SDKBase.Editor.BuildPipeline.VRCBuildPipelineCallbacks";
        private const string PreprocessMethodName = "OnPreprocessAvatar";

        /// <summary>
        /// root に前処理を適用し、適用した種類を返す。どちらも無ければ root は変更しない。
        /// </summary>
        public static Mode Process(GameObject root)
        {
            // Avatar Descriptor が無いと VRChat 向けのツールが動けないため、SDK の前処理は使わない
            MethodInfo method = VrcDescriptorReader.FindDescriptor(root) != null ? FindPreprocessMethod() : null;
            if (method == null)
            {
                // NDMF だけでも適用し、MA の改変は反映する
                return NdmfProcessor.Process(root) ? Mode.NdmfOnly : Mode.None;
            }

            bool succeeded;
            try
            {
                succeeded = method.Invoke(null, new object[] { root }) is bool result && result;
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                // リフレクションの包み例外ではなく元の例外のメッセージを見せる
                throw new InvalidOperationException(
                    "VRChat SDK avatar preprocessing (VRCFury / NDMF etc.) failed: " + e.InnerException.Message,
                    e.InnerException);
            }

            // アップロードでも中断される失敗なので、途中まで改変された複製は書き出さない（原因は各ツールが Console に出す）
            if (!succeeded)
            {
                throw new InvalidOperationException(
                    "VRChat SDK avatar preprocessing (VRCFury / NDMF etc.) failed. See the Console for details.");
            }

            return Mode.VrcSdk;
        }

        private static MethodInfo FindPreprocessMethod()
        {
            // SDK のアセンブリ名はバージョンで変わり得るため型名で探す
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(CallbacksTypeName, false);
                if (type == null)
                {
                    continue;
                }

                // SDK のバージョンで公開範囲が違っても見つかるよう非公開も含める
                return type.GetMethod(
                    PreprocessMethodName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null,
                    new[] { typeof(GameObject) },
                    null);
            }

            return null;
        }
    }
}
