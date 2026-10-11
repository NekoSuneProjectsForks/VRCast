using UnityEngine;
using VRCast.Avatars;

namespace VRCast.Rendering
{
    /// <summary>
    /// 表示中のアバターの顔を、一時的なカメラで正方形の PNG に撮る（最近使ったアバターの一覧の画像用）。
    /// 画面・仮想カメラ・Spout の出力には影響しない（別のカメラで描画先のテクスチャへ描く）。
    /// </summary>
    public static class AvatarFaceCapture
    {
        // 撮る画像の一辺（一覧の画像の縮小後の長辺と同じ）
        public const int Size = 256;

        // 画角（狭めにして顔の遠近の歪みを抑える）と、手前に入り込む物を避ける近クリップ
        private const float FieldOfView = 25f;
        private const float NearClip = 0.01f;

        // 縁のギザギザを抑えるマルチサンプル数
        private const int AntiAliasing = 8;

        /// <summary>
        /// アバターの顔を正面から撮った PNG を返す。撮れなければ null。
        /// background は背景色（透明度は無視して不透明にする。シェーダーによって透明度が崩れるため）。
        /// </summary>
        public static byte[] CapturePng(LoadedAvatar avatar, Color background)
        {
            // 破棄済み・未生成のアバターは撮らない
            if (avatar?.Instance == null)
            {
                return null;
            }

            // 顔の中心と範囲から、正面（アバターの向き）に置くカメラの距離を決める
            avatar.CalculateFaceFrame(out Vector3 center, out float halfHeight);
            Transform root = avatar.Instance.transform;
            float distance = halfHeight / Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad);

            var cameraObject = new GameObject("VRCast Face Capture") { hideFlags = HideFlags.HideAndDontSave };
            RenderTexture target = null;
            Texture2D image = null;
            RenderTexture active = RenderTexture.active;
            try
            {
                // 一時カメラ（自動では描画させず、Render で 1 回だけ描く）
                var camera = cameraObject.AddComponent<UnityEngine.Camera>();
                camera.enabled = false;
                camera.fieldOfView = FieldOfView;
                camera.nearClipPlane = NearClip;
                camera.farClipPlane = distance * 4f + 1f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(background.r, background.g, background.b, 1f);
                camera.transform.position = center + root.forward * distance;
                camera.transform.LookAt(center, root.up);

                // 色空間（Linear）でも見た目どおりになるよう sRGB の描画先に描く
                target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                {
                    antiAliasing = AntiAliasing,
                };
                camera.targetTexture = target;
                camera.Render();

                // 描画結果を読み戻して PNG にする
                RenderTexture.active = target;
                image = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0, false);
                image.Apply(false);
                return image.EncodeToPNG();
            }
            finally
            {
                // 一時的に作ったものは成否に関わらず片付ける
                RenderTexture.active = active;
                Object.Destroy(cameraObject);
                if (target != null)
                {
                    target.Release();
                    Object.Destroy(target);
                }

                if (image != null)
                {
                    Object.Destroy(image);
                }
            }
        }
    }
}
