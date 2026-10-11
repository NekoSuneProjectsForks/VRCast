using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using UnityEngine;
using VRCast.AvatarFormat;
using VRCast.Core;
using VRCast.PerfectSync;
using VRCast.Platform;
using VRCast.Tracking;

namespace VRCast.UI
{
    /// <summary>
    /// 目印の自動配置（パーフェクトシンク化の編集画面）。
    /// 「BlendShape から」はまばたき・口・眉の BlendShape で動く頂点から（LandmarkEstimator）、
    /// 「画像認識」は今の視点の顔を画像にして同梱の MediaPipe トラッカー（--face-image）で点を求め、顔メッシュへ光線を当てる。
    /// どちらも打っていない目印だけを置き（手で打った目印は変えない）、置く前の状態を履歴に積む。
    /// </summary>
    public sealed partial class PerfectSyncEditor
    {
        // 画像認識を待つ最長時間（秒。トラッカーの起動とモデルの読み込みを含む）
        private const float DetectionTimeout = 60f;

        // 画像認識に渡す画像の長辺の上限（画素）と、出力を読み終えるのを待つ時間（ミリ秒）
        private const int DetectionMaxSize = 1280;
        private const int DetectionOutputWait = 1000;

        // 画像認識に渡す画像のファイル名（一時フォルダ。終わったら消す）
        private const string DetectionImageName = "perfect_sync_face.png";

        // まばたき・口・眉として使う ARKit 名（PerfectSyncAutoMapper の対応表で既存の BlendShape を探す）
        private static readonly string[] BrowShapes = { "browOuterUpLeft", "browInnerUp", "browDownLeft" };

        // 画像認識中のトラッカーと、その出力（別スレッドから足す）・開始時刻・画像・撮ったときのカメラ
        private NativeProcess _detection;
        private readonly List<string> _detectionOutput = new List<string>();
        private float _detectionStarted;
        private string _detectionImage;
        private Matrix4x4 _detectionInverse;

        /// <summary>
        /// 画像認識の結果を待っていれば true（自動配置のボタンを押せなくする）。
        /// </summary>
        private bool IsDetecting => _detection != null;

        private void AutoPlaceFromShapes()
        {
            // 顔メッシュのまばたき・口・眉の BlendShape を 1 つずつ当てた形を作る
            LandmarkShapeSamples samples = BakeSamples(out int found);
            if (found == 0)
            {
                SetMessage(Loc.T(
                    "No blink or mouth blend shapes were found on the face mesh. Try image recognition or place the points by hand.",
                    "顔メッシュにまばたき・口の BlendShape が見つかりません。画像認識を試すか、手で打ってください。",
                    "얼굴 메시에서 눈 깜빡임·입 BlendShape를 찾을 수 없습니다. 이미지 인식을 시도하거나 직접 찍으세요.",
                    "在面部网格中找不到眨眼·嘴部 BlendShape。请尝试图像识别或手动放置。",
                    "在臉部網格中找不到眨眼·嘴部 BlendShape。請嘗試影像辨識或手動放置。"), true);
                return;
            }

            // 動く頂点から目印の位置を推定し、顔の表面（前を向いた面）の最も近い点にする
            FaceAxes axes = CurrentAxes();
            LandmarkEstimate estimate = LandmarkEstimator.Estimate(_world, _regionVertices, samples, axes);
            var landmarks = new Dictionary<LandmarkId, PerfectSyncLandmark>();
            foreach (KeyValuePair<LandmarkId, Vector3> pair in estimate.Points)
            {
                landmarks[pair.Key] = _geometry.Nearest(_world, _region, pair.Value, axes.Forward, (int)pair.Key)
                    ?? _geometry.Nearest(_world, _region, pair.Value, Vector3.zero, (int)pair.Key);
            }

            ReportAutoPlaced(ApplyAutoLandmarks(landmarks), estimate.Guessed);
        }

        private LandmarkShapeSamples BakeSamples(out int found)
        {
            // 顔メッシュの BlendShape から、名前の対応表で使えるものを探す
            Mesh mesh = _face.sharedMesh;
            var candidates = new List<PerfectSyncAutoMapper.Candidate>(mesh.blendShapeCount);
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                candidates.Add(new PerfectSyncAutoMapper.Candidate(_facePath, mesh.GetBlendShapeName(i)));
            }

            var byArKit = new Dictionary<string, string>();
            foreach (PerfectSyncBinding binding in PerfectSyncAutoMapper.Map(candidates))
            {
                byArKit[binding.name] = binding.blendShape;
            }

            // 同じ BlendShape は 1 回だけ形を作る（両目のまばたきが左右で同じなど）
            var cache = new Dictionary<int, Vector3[]>();
            var samples = new LandmarkShapeSamples
            {
                BlinkLeft = BakeShape(FindShape(mesh, byArKit, "eyeBlinkLeft"), cache),
                BlinkRight = BakeShape(FindShape(mesh, byArKit, "eyeBlinkRight"), cache),
                MouthOpen = BakeShape(FindShape(mesh, byArKit, "jawOpen"), cache),
                Pucker = BakeShape(FindShape(mesh, byArKit, "mouthPucker"), cache),
            };
            foreach (string name in BrowShapes)
            {
                samples.Brow ??= BakeShape(FindShape(mesh, byArKit, name), cache);
            }

            // 目か口のどちらかが無ければ推定できない
            found = samples.BlinkLeft != null || samples.BlinkRight != null || samples.MouthOpen != null ? cache.Count : 0;
            return samples;
        }

        private int FindShape(Mesh mesh, Dictionary<string, string> byArKit, string arKitName)
        {
            // 元からある ARKit 名の BlendShape（試しに足した形状ではないもの）を優先し、無ければ対応表の BlendShape
            int arKit = Array.IndexOf(ArKitFace.BlendShapeNames, arKitName);
            if (arKit >= 0 && _shapes[arKit] != null && _shapes[arKit].Native)
            {
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    if (PerfectSyncBlendShapes.Match(mesh.GetBlendShapeName(i)) == arKit)
                    {
                        return i;
                    }
                }
            }

            return byArKit.TryGetValue(arKitName, out string name) ? mesh.GetBlendShapeIndex(name) : -1;
        }

        private Vector3[] BakeShape(int index, Dictionary<int, Vector3[]> cache)
        {
            // 無い BlendShape は null。作ったことがあればそれを使う
            if (index < 0)
            {
                return null;
            }

            if (cache.TryGetValue(index, out Vector3[] baked))
            {
                return baked;
            }

            // その BlendShape だけを 100 にした今の姿勢の形（すぐ元の値に戻す）
            float previous = _face.GetBlendShapeWeight(index);
            _face.SetBlendShapeWeight(index, 100f);
            baked = BakeWorld(null);
            _face.SetBlendShapeWeight(index, previous);
            cache[index] = baked;
            return baked;
        }

        private void StartImageDetection()
        {
            // 同梱の MediaPipe トラッカーが必要
            if (IsDetecting)
            {
                return;
            }

            string tracker = TrackerProcess.FindBundled(TrackerProcess.BundledRoot, TrackingSource.MediaPipe);
            if (tracker == null)
            {
                SetMessage(Loc.T("The bundled MediaPipe tracker was not found.", "同梱の MediaPipe トラッカーが見つかりません。",
                    "동봉된 MediaPipe 트래커를 찾을 수 없습니다.", "找不到附带的 MediaPipe 追踪器。", "找不到附帶的 MediaPipe 追蹤器。"), true);
                return;
            }

            // 今の視点の顔を画像にして、トラッカーに渡す
            _detectionImage = Path.Combine(Application.temporaryCachePath, DetectionImageName);
            try
            {
                CaptureFace(_detectionImage);
                tracker = Path.GetFullPath(tracker);
                lock (_detectionOutput)
                {
                    _detectionOutput.Clear();
                }

                _detection = NativeProcess.Start(tracker, $"--face-image \"{_detectionImage}\"",
                    Path.GetDirectoryName(tracker), Encoding.UTF8, OnDetectionLine);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is Win32Exception)
            {
                // 画像を書けない・トラッカーを起動できない
                DeleteDetectionImage();
                SetMessage(Loc.T("Could not start image recognition: ", "画像認識を始められませんでした: ",
                    "이미지 인식을 시작하지 못했습니다: ", "无法开始图像识别: ", "無法開始影像辨識: ") + e.Message, true);
                VRCastLog.Warning(LogCategory, $"Failed to start face image recognition: {e.Message}");
                return;
            }

            _detectionStarted = Time.realtimeSinceStartup;
            SetMessage(Loc.T("Recognizing the face...", "顔を画像認識しています...", "얼굴을 이미지 인식하는 중...",
                "正在识别面部...", "正在辨識臉部..."), false);
        }

        private void OnDetectionLine(string line)
        {
            // トラッカーの出力（別スレッドから呼ばれる）
            lock (_detectionOutput)
            {
                _detectionOutput.Add(line);
            }
        }

        private void CaptureFace(string path)
        {
            // 画面と同じ縦横比で、長辺を上限までにした大きさ
            float scale = Mathf.Min(1f, DetectionMaxSize / (float)Mathf.Max(_camera.pixelWidth, _camera.pixelHeight));
            int width = Mathf.Max(1, Mathf.RoundToInt(_camera.pixelWidth * scale));
            int height = Mathf.Max(1, Mathf.RoundToInt(_camera.pixelHeight * scale));

            // カメラを一時的に別の描画先へ向けて 1 回描く（顔の線・目印は GUI で描くので写らない）
            RenderTexture target = RenderTexture.GetTemporary(width, height, 24);
            RenderTexture previousTarget = _camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                _camera.targetTexture = target;
                _camera.Render();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                _camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.Destroy(texture);
            }

            // 画像の点から光線を作るため、撮ったときのカメラを控える（結果を待つ間に視点を動かしてもよい）
            _detectionInverse = (_camera.projectionMatrix * _camera.worldToCameraMatrix).inverse;
        }

        private void PollDetection()
        {
            // 終わるか時間切れになるまで待つ
            if (!IsDetecting)
            {
                return;
            }

            bool exited = _detection.HasExited;
            if (!exited && Time.realtimeSinceStartup - _detectionStarted < DetectionTimeout)
            {
                return;
            }

            // 時間切れなら止め、出力を読み終えてから片付ける
            if (!exited)
            {
                _detection.Kill();
            }

            _detection.WaitForOutput(DetectionOutputWait);
            int exitCode = _detection.ExitCode;
            _detection.Dispose();
            _detection = null;
            DeleteDetectionImage();

            List<string> lines;
            lock (_detectionOutput)
            {
                lines = new List<string>(_detectionOutput);
                _detectionOutput.Clear();
            }

            FinishDetection(exited, exitCode, lines);
        }

        private void FinishDetection(bool exited, int exitCode, List<string> lines)
        {
            // 時間切れ
            if (!exited)
            {
                SetMessage(Loc.T("Image recognition timed out.", "画像認識が時間内に終わりませんでした。", "이미지 인식이 시간 내에 끝나지 않았습니다.",
                    "图像识别超时。", "影像辨識逾時。"), true);
                return;
            }

            // 結果の行が無い（古いトラッカー・モデル欠けなど）
            if (!MediaPipeFaceMap.TryParse(lines, out bool face, out float[] points))
            {
                bool outdated = lines.Exists(line => line != null && line.Contains("unrecognized arguments"));
                SetMessage(outdated
                    ? Loc.T("The bundled tracker is too old for image recognition. Update VRCast.",
                        "同梱のトラッカーが古く、画像認識に対応していません。VRCast を更新してください。",
                        "동봉된 트래커가 오래되어 이미지 인식을 지원하지 않습니다. VRCast를 업데이트하세요.",
                        "附带的追踪器版本过旧，不支持图像识别。请更新 VRCast。", "附帶的追蹤器版本過舊，不支援影像辨識。請更新 VRCast。")
                    : Loc.T($"Image recognition failed (exit code {exitCode}).", $"画像認識に失敗しました（終了コード {exitCode}）。",
                        $"이미지 인식에 실패했습니다 (종료 코드 {exitCode}).", $"图像识别失败（退出代码 {exitCode}）。",
                        $"影像辨識失敗（結束代碼 {exitCode}）。"), true);
                VRCastLog.Warning(LogCategory, $"Face image recognition failed (exit code {exitCode}): {string.Join(" | ", lines)}");
                return;
            }

            // 顔が見つからない（アニメ調の顔は見つかりにくい）
            if (!face)
            {
                SetMessage(Loc.T(
                    "No face was found in the image. Cartoon faces are often hard to recognize; try \"From blend shapes\" or face the camera first.",
                    "画像から顔を見つけられませんでした。アニメ調の顔は認識しにくいことがあります。「BlendShape から」を試すか、顔を正面に向けてからやり直してください。",
                    "이미지에서 얼굴을 찾지 못했습니다. 애니메이션풍 얼굴은 인식하기 어려울 수 있습니다. 'BlendShape에서'를 시도하거나 얼굴을 정면으로 한 뒤 다시 하세요.",
                    "未能在图像中找到面部。动漫风格的脸可能难以识别。请尝试“从 BlendShape”，或先正对脸部后重试。",
                    "未能在影像中找到臉部。動漫風格的臉可能難以辨識。請嘗試「從 BlendShape」，或先正對臉部後重試。"), true);
                return;
            }

            // 目印ごとの画像上の点から、撮ったときのカメラの光線を顔のまわりの三角形に当てる
            var landmarks = new Dictionary<LandmarkId, PerfectSyncLandmark>();
            foreach (LandmarkId id in FaceLandmarks.For(LandmarkDetail.Detailed))
            {
                if (MediaPipeFaceMap.TryGetPoint(points, id, out Vector2 point)
                    && _geometry.Raycast(_world, DetectionRay(point), _region, out PerfectSyncLandmark hit, out float _))
                {
                    landmarks[id] = hit;
                }
            }

            ReportAutoPlaced(ApplyAutoLandmarks(landmarks), null);
        }

        private Ray DetectionRay(Vector2 imagePoint)
        {
            // 画像の座標（左上原点・0〜1）を、撮ったときのカメラの正規化デバイス座標（左下原点・-1〜1）の近くと遠くへ戻す
            float x = imagePoint.x * 2f - 1f;
            float y = (1f - imagePoint.y) * 2f - 1f;
            Vector3 near = _detectionInverse.MultiplyPoint(new Vector3(x, y, -1f));
            Vector3 far = _detectionInverse.MultiplyPoint(new Vector3(x, y, 1f));
            return new Ray(near, (far - near).normalized);
        }

        private int ApplyAutoLandmarks(Dictionary<LandmarkId, PerfectSyncLandmark> found)
        {
            // 置く前の状態（何か置いたときだけ履歴に積む）
            UndoState before = Snapshot();
            int count = 0;
            foreach (LandmarkId id in ActiveLandmarks())
            {
                // 手で打った目印は変えない。左右対称なら右側は左側から決める
                if ((_landmarks.ContainsKey(id) && !_autoMirrored.Contains(id)) || (_mirror && FaceLandmarks.IsRight(id))
                    || !found.TryGetValue(id, out PerfectSyncLandmark landmark) || landmark == null)
                {
                    continue;
                }

                landmark.id = (int)id;
                _landmarks[id] = landmark;
                _autoMirrored.Remove(id);
                count++;
            }

            if (count == 0)
            {
                return 0;
            }

            // 履歴に積み、右側を置き直し、次に打つ目印を選ぶ
            PushUndo(before);
            Remirror(null);
            _lastPlaced = null;
            _selected = NextUnplaced() ?? _selected;
            _dirty = true;
            return count;
        }

        private void ReportAutoPlaced(int count, HashSet<LandmarkId> guessed)
        {
            // 置けなかった
            if (count == 0)
            {
                SetMessage(Loc.T("No points could be placed automatically (points you placed are kept).",
                    "自動で置ける目印がありませんでした（打った目印はそのままです）。",
                    "자동으로 찍을 수 있는 표시가 없었습니다(찍은 표시는 그대로입니다).",
                    "没有可自动放置的标记（已放置的标记保持不变）。", "沒有可自動放置的標記（已放置的標記保持不變）。"), true);
                return;
            }

            // 置いた数と、比率で推定しただけの目印（ずれやすいので確かめてもらう）
            string text = Loc.T($"Placed {count} points. Drag any that are off. Undo (Ctrl+Z) removes them.",
                $"{count} 個の目印を置きました。ずれている目印はドラッグで直してください（元に戻す（Ctrl+Z）で取り消せます）。",
                $"표시 {count}개를 찍었습니다. 어긋난 표시는 드래그로 고치세요(실행 취소(Ctrl+Z)로 취소할 수 있습니다).",
                $"已放置 {count} 个标记。偏移的标记请拖动修正（可用撤销（Ctrl+Z）取消）。",
                $"已放置 {count} 個標記。偏移的標記請拖曳修正（可用復原（Ctrl+Z）取消）。");
            var rough = new List<string>();
            foreach (LandmarkId id in guessed ?? new HashSet<LandmarkId>())
            {
                if (_landmarks.ContainsKey(id) && !_autoMirrored.Contains(id))
                {
                    rough.Add(PerfectSyncLabels.Landmark(id));
                }
            }

            if (rough.Count > 0)
            {
                text += Loc.T(" Rough guesses: ", " 目安の位置: ", " 대략적인 위치: ", " 大致位置: ", " 大致位置: ") + string.Join(", ", rough);
            }

            SetMessage(text, false);
        }

        private void CancelDetection()
        {
            // 画像認識中なら止めて片付ける（閉じたとき）
            if (_detection != null)
            {
                _detection.Kill();
                _detection.Dispose();
                _detection = null;
            }

            DeleteDetectionImage();
        }

        private void DeleteDetectionImage()
        {
            // 一時ファイルを消す（消せなくても次回上書きする）
            if (string.IsNullOrEmpty(_detectionImage))
            {
                return;
            }

            try
            {
                File.Delete(_detectionImage);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                VRCastLog.Warning(LogCategory, $"Failed to delete {_detectionImage}: {e.Message}");
            }

            _detectionImage = null;
        }
    }
}
