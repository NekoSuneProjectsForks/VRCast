using System;
using System.IO;
using UnityEngine;
using VRCast.Core;

namespace VRCast.Rendering
{
    /// <summary>
    /// 画面と同じ視点のアバターを、背景を透過した PNG に撮って保存する（操作パネルは映らない）。
    /// シェーダーによって描画結果の透明度が崩れるため、黒と白の背景で 2 回描き、その差から透明度を求める。
    /// 画面・仮想カメラ・Spout の出力には影響しない（別のカメラで描画先のテクスチャへ描く）。
    /// </summary>
    public sealed class AvatarScreenshot
    {
        // ログのカテゴリ名
        private const string LogCategory = "Screenshot";

        // 既定の保存先（ピクチャ内）のフォルダ名
        private const string DefaultFolderName = "VRCast";

        // 縁のギザギザを抑えるマルチサンプル数
        private const int AntiAliasing = 4;

        // これ未満の透明度は完全に透明として扱う（8 bit で 1 段未満）
        private const float MinAlpha = 1f / 255f;

        private readonly UnityEngine.Camera _source;
        private readonly AppSettings _settings;

        public AvatarScreenshot(UnityEngine.Camera source, AppSettings settings)
        {
            _source = source;
            _settings = settings;
        }

        /// <summary>
        /// 最後に保存したファイルのフルパス（未保存なら null）。
        /// </summary>
        public string LastPath { get; private set; }

        /// <summary>
        /// 最後の撮影に失敗した理由（成功・未撮影なら null）。
        /// </summary>
        public string LastError { get; private set; }

        /// <summary>
        /// 既定の保存先（ピクチャ\VRCast）。
        /// </summary>
        public static string DefaultFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), DefaultFolderName);

        /// <summary>
        /// 今の設定での保存先（前後の空白・" を除き、未指定なら既定の保存先）。
        /// </summary>
        public string Folder
        {
            get
            {
                string folder = PathUtility.NormalizeInput(_settings.screenshotFolder);
                return folder.Length == 0 ? DefaultFolder : folder;
            }
        }

        /// <summary>
        /// 撮って保存する。保存できれば true（パスは LastPath、失敗の理由は LastError）。
        /// </summary>
        public bool Capture()
        {
            // 画面と同じ大きさで撮る
            byte[] png = CapturePng(_source, _source.pixelWidth, _source.pixelHeight);
            if (png == null)
            {
                return Fail("Could not render the screenshot");
            }

            try
            {
                // 保存先が無ければ作り、撮った日時（ミリ秒まで）の名前で書く（連続で押しても上書きしない）
                string folder = Folder;
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, $"VRCast_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
                File.WriteAllBytes(path, png);
                LastPath = path;
                LastError = null;
                VRCastLog.Info(LogCategory, "Saved " + path);
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException
                || e is ArgumentException || e is NotSupportedException)
            {
                // 書き込めない・不正なフォルダ名
                return Fail($"Could not save to {Folder}: {e.Message}");
            }
        }

        private bool Fail(string error)
        {
            // 理由を残して警告する
            LastError = error;
            VRCastLog.Warning(LogCategory, error);
            return false;
        }

        /// <summary>
        /// source と同じ設定のカメラで背景を透過した PNG を撮る。撮れなければ null。
        /// </summary>
        public static byte[] CapturePng(UnityEngine.Camera source, int width, int height)
        {
            // カメラが無い・大きさが無いときは撮らない
            if (source == null || width <= 0 || height <= 0)
            {
                return null;
            }

            var cameraObject = new GameObject("VRCast Screenshot") { hideFlags = HideFlags.HideAndDontSave };
            RenderTexture target = null;
            Texture2D readback = null;
            Texture2D image = null;
            RenderTexture active = RenderTexture.active;
            try
            {
                // 画面のカメラの視点・画角・描画設定を写した一時カメラ（自動では描画させず、Render で描く）
                var camera = cameraObject.AddComponent<UnityEngine.Camera>();
                camera.enabled = false;
                camera.CopyFrom(source);
                camera.clearFlags = CameraClearFlags.SolidColor;

                // 透明度を正確に求めるため、色を変換せず小数のまま保つ描画先に描く
                target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                {
                    antiAliasing = AntiAliasing,
                };
                camera.targetTexture = target;
                readback = new Texture2D(width, height, TextureFormat.RGBAHalf, false, true);

                // 黒と白の背景で 1 回ずつ描く（同じフレームなのでアバターの姿勢は同じ）
                Color[] onBlack = Render(camera, Color.black, target, readback);
                Color[] onWhite = Render(camera, Color.white, target, readback);

                // 差から透明度と元の色を求め、PNG にする
                image = new Texture2D(width, height, TextureFormat.RGBA32, false);
                image.SetPixels32(Unmix(onBlack, onWhite, QualitySettings.activeColorSpace == ColorSpace.Linear));
                image.Apply(false);
                return image.EncodeToPNG();
            }
            finally
            {
                // 一時的に作ったものは成否に関わらず片付ける
                RenderTexture.active = active;
                UnityEngine.Object.Destroy(cameraObject);
                if (target != null)
                {
                    target.Release();
                    UnityEngine.Object.Destroy(target);
                }

                if (readback != null)
                {
                    UnityEngine.Object.Destroy(readback);
                }

                if (image != null)
                {
                    UnityEngine.Object.Destroy(image);
                }
            }
        }

        private static Color[] Render(UnityEngine.Camera camera, Color background, RenderTexture target, Texture2D readback)
        {
            // 背景色を変えて描き、描画結果を読み戻す
            camera.backgroundColor = new Color(background.r, background.g, background.b, 0f);
            camera.Render();
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false);
            return readback.GetPixels();
        }

        /// <summary>
        /// 黒背景と白背景で描いた色から、透明度と背景を除いた色（8 bit の sRGB）を求める。
        /// 半透明の物は「色 × 透明度 + 背景 × (1 - 透明度)」で重なるため、白と黒の差が (1 - 透明度) になる。
        /// linear は描画結果がリニア色空間の値か（PNG 用に sRGB へ変換する）。
        /// </summary>
        public static Color32[] Unmix(Color[] onBlack, Color[] onWhite, bool linear)
        {
            var pixels = new Color32[onBlack.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                Color black = onBlack[i];
                Color white = onWhite[i];

                // 3 色の差の平均から透明度を求める（色付きの半透明でもずれにくい）
                float difference = ((white.r - black.r) + (white.g - black.g) + (white.b - black.b)) / 3f;
                float alpha = Mathf.Clamp01(1f - difference);

                // ほぼ透明なら色を捨てる（割り算で色が飛ばないように）
                if (alpha < MinAlpha)
                {
                    pixels[i] = new Color32(0, 0, 0, 0);
                    continue;
                }

                // 黒背景の色は「色 × 透明度」なので透明度で割って元の色に戻し、必要なら sRGB にする
                var color = new Color(black.r / alpha, black.g / alpha, black.b / alpha, alpha);
                if (linear)
                {
                    color = color.gamma;
                    color.a = alpha;
                }

                pixels[i] = ToColor32(color);
            }

            return pixels;
        }

        private static Color32 ToColor32(Color color)
        {
            // 0〜1 に収めて 8 bit に丸める
            return new Color32(ToByte(color.r), ToByte(color.g), ToByte(color.b), ToByte(color.a));
        }

        private static byte ToByte(float value)
        {
            return (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);
        }
    }
}
