using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VRCast.UI
{
    /// <summary>
    /// パーフェクトシンク化の画面で、顔メッシュの線と目印の点を画面へ重ねて描く。
    /// OnGUI の Repaint で画面の座標に直接描くため、カメラの映像（仮想カメラ・Spout）には映らない。
    /// </summary>
    public sealed class PerfectSyncOverlay
    {
        // 線の描画に使う組み込みシェーダー（ビルドにも常に含まれる）
        private const string ShaderName = "Hidden/Internal-Colored";

        private Material _material;

        // 画面の座標に直した頂点（毎回確保しないよう使い回す）と、その頂点が見えているか
        private Vector3[] _screen = new Vector3[0];
        private bool[] _visible = new bool[0];

        /// <summary>
        /// 顔のまわりの三角形の辺を描く。カメラから見て顔の中心より奥の三角形（後頭部側）は描かない。
        /// </summary>
        /// <param name="camera">アバターを映しているカメラ</param>
        /// <param name="world">メッシュの頂点（ワールド座標）</param>
        /// <param name="triangles">メッシュの三角形（頂点番号 3 つずつ）</param>
        /// <param name="region">描く三角形の番号</param>
        /// <param name="regionVertices">描く三角形が使う頂点の番号</param>
        /// <param name="faceCenter">顔の中心（ワールド座標）</param>
        public void DrawWireframe(Camera camera, Vector3[] world, int[] triangles, List<int> region,
            List<int> regionVertices, Vector3 faceCenter, Color color)
        {
            if (!Begin(camera))
            {
                return;
            }

            // 使う頂点だけ画面の座標に直す（カメラより後ろは描かない）
            EnsureCapacity(world.Length);
            foreach (int v in regionVertices)
            {
                _screen[v] = ToScreen(camera, world[v], out _visible[v]);
            }

            // カメラの向きに沿って、顔の中心より手前にある三角形だけ描く
            Vector3 forward = camera.transform.forward;
            GL.Begin(GL.LINES);
            GL.Color(color);
            foreach (int triangle in region)
            {
                int i = triangle * 3;
                int a = triangles[i];
                int b = triangles[i + 1];
                int c = triangles[i + 2];
                if (!_visible[a] || !_visible[b] || !_visible[c])
                {
                    continue;
                }

                // 三角形の中心が顔の中心より奥（後頭部側）なら描かない
                Vector3 centroid = (world[a] + world[b] + world[c]) / 3f;
                if (Vector3.Dot(centroid - faceCenter, forward) > 0f)
                {
                    continue;
                }

                // 3 辺を線で描く（隣の三角形と共有する辺は 2 回描くが見た目は変わらない）
                Line(_screen[a], _screen[b]);
                Line(_screen[b], _screen[c]);
                Line(_screen[c], _screen[a]);
            }

            GL.End();
            GL.PopMatrix();
        }

        /// <summary>
        /// 目印の点（ワールド座標）を色付きの四角で描く。size は画面の画素数（一辺）。
        /// </summary>
        public void DrawPoints(Camera camera, IReadOnlyList<(Vector3 World, Color Color, float Size)> points)
        {
            if (points.Count == 0 || !Begin(camera))
            {
                return;
            }

            GL.Begin(GL.QUADS);
            foreach ((Vector3 world, Color color, float size) in points)
            {
                // カメラより後ろの点は描かない
                Vector3 screen = ToScreen(camera, world, out bool visible);
                if (!visible)
                {
                    continue;
                }

                // 黒い縁取りを先に描き、その上に色の四角を描く（明るい肌の上でも見えるように）
                Quad(screen, size + 2f, Color.black);
                Quad(screen, size, color);
            }

            GL.End();
            GL.PopMatrix();
        }

        /// <summary>
        /// ワールド座標の点を GUI の座標（左上原点・UI の倍率で割った値）に直す。カメラより後ろなら false。
        /// </summary>
        public static bool ToGui(Camera camera, Vector3 world, float uiScale, out Vector2 gui)
        {
            Vector3 screen = ToScreen(camera, world, out bool visible);
            gui = new Vector2(screen.x / uiScale, (Screen.height - screen.y) / uiScale);
            return visible;
        }

        /// <summary>
        /// ワールド座標の点を画面の画素の座標（左下原点）に直す。カメラより後ろなら visible = false。
        /// </summary>
        public static Vector3 ToScreen(Camera camera, Vector3 world, out bool visible)
        {
            // ビューポート座標（0〜1）から画面全体の画素へ（カメラは画面全体に描いている）
            Vector3 viewport = camera.WorldToViewportPoint(world);
            visible = viewport.z > 0f;
            return new Vector3(viewport.x * Screen.width, viewport.y * Screen.height, 0f);
        }

        public void Dispose()
        {
            // 作ったマテリアルを破棄する
            if (_material != null)
            {
                Object.Destroy(_material);
                _material = null;
            }
        }

        private bool Begin(Camera camera)
        {
            // Repaint 以外・カメラが無い・シェーダーが無いときは描かない
            if (Event.current == null || Event.current.type != EventType.Repaint || camera == null || !EnsureMaterial())
            {
                return false;
            }

            // 画面の画素の座標（左下原点）で描く
            _material.SetPass(0);
            GL.PushMatrix();
            GL.LoadPixelMatrix();
            return true;
        }

        private bool EnsureMaterial()
        {
            if (_material != null)
            {
                return true;
            }

            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                return false;
            }

            // 半透明・深度を見ない・両面の単色描画（UI と同じく常に手前に出す）
            _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            _material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _material.SetInt("_Cull", (int)CullMode.Off);
            _material.SetInt("_ZWrite", 0);
            _material.SetInt("_ZTest", (int)CompareFunction.Always);
            return true;
        }

        private void EnsureCapacity(int count)
        {
            // 頂点数が増えたときだけ配列を作り直す
            if (_screen.Length < count)
            {
                _screen = new Vector3[count];
                _visible = new bool[count];
            }
        }

        private static void Line(Vector3 a, Vector3 b)
        {
            GL.Vertex(a);
            GL.Vertex(b);
        }

        private static void Quad(Vector3 center, float size, Color color)
        {
            // 中心から半分ずつ広げた四角
            float half = size * 0.5f;
            GL.Color(color);
            GL.Vertex3(center.x - half, center.y - half, 0f);
            GL.Vertex3(center.x - half, center.y + half, 0f);
            GL.Vertex3(center.x + half, center.y + half, 0f);
            GL.Vertex3(center.x + half, center.y - half, 0f);
        }
    }
}
