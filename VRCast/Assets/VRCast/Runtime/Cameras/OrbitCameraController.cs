using UnityEngine;
using VRCast.Core;

namespace VRCast.Cameras
{
    /// <summary>
    /// 注視点を中心に回転・パン・ズームするカメラ操作。
    /// 右ドラッグ: 回転 / 中ドラッグ: パン / ホイール: ズーム。
    /// ZoomInsteadOfEntering が true なら、アバターの体に入り込む手前の距離で止め、それ以上は画角を狭めて拡大する。
    /// </summary>
    [RequireComponent(typeof(UnityEngine.Camera))]
    public class OrbitCameraController : MonoBehaviour
    {
        // マウス操作の感度
        private const float RotateSpeed = 4f;
        private const float PanSpeed = 0.02f;
        private const float ZoomSpeed = 0.1f;

        // 距離と仰角の制限
        private const float MinDistance = 0.1f;
        private const float MaxDistance = 50f;
        private const float MinPitch = -89f;
        private const float MaxPitch = 89f;

        // Frame 時に対象の周囲へ確保する余白率
        private const float FramePadding = 1.1f;

        // 体に入り込まない距離（アバターの高さに対する割合）と、画角で拡大する倍率の上限
        private const float NoEnterDistanceRatio = 0.25f;
        private const float MaxZoom = 10f;

        // 近くの描画が切れる距離（m。アップでも体の手前が欠けないよう短くする）
        private const float NearClip = 0.02f;

        // アバター未読込時の既定の注視点（目線の高さ付近）
        private static readonly Vector3 DefaultTarget = new Vector3(0f, 1.2f, 0f);
        private const float DefaultDistance = 2.5f;
        private const float DefaultFieldOfView = 30f;

        // VRChat アバターは +Z 向きなので、正面から見るため 180 度回す
        private const float FrontYaw = 180f;

        private UnityEngine.Camera _camera;
        private Vector3 _target;
        private float _distance;
        private float _yaw;
        private float _pitch;

        // 画角（スライダーの値）と、体に入り込まないときの画角による拡大の倍率（1 = 拡大なし）
        private float _baseFieldOfView = DefaultFieldOfView;
        private float _zoom = 1f;

        // 体に入り込まない距離（Frame 時にアバターの大きさから決める）
        private float _noEnterDistance = MinDistance;
        private bool _zoomInsteadOfEntering;

        // Reset で戻す位置（Frame 時に更新）
        private Vector3 _homeTarget;
        private float _homeDistance;

        /// <summary>
        /// UI 操作中などマウス入力を無視したいときに true にする。
        /// </summary>
        public bool InputBlocked { get; set; }

        /// <summary>
        /// 位置を決めた後に誤って動かさないよう、マウス操作を受け付けないときに true にする。
        /// </summary>
        public bool Locked { get; set; }

        /// <summary>
        /// 体に入り込む手前で止め、それ以上は画角を狭めて拡大するなら true。OFF にすると拡大を戻す。
        /// </summary>
        public bool ZoomInsteadOfEntering
        {
            get => _zoomInsteadOfEntering;
            set
            {
                // 変わったときだけ反映（OFF にしたら画角による拡大をやめる）
                if (_zoomInsteadOfEntering == value)
                {
                    return;
                }

                _zoomInsteadOfEntering = value;
                _zoom = value ? _zoom : 1f;
                ApplyFieldOfView();
            }
        }

        /// <summary>
        /// 画角（スライダーで決める値。画角による拡大の分は含まない）。
        /// </summary>
        public float FieldOfView
        {
            get => _baseFieldOfView;
            set
            {
                _baseFieldOfView = Mathf.Clamp(value, 1f, 120f);
                ApplyFieldOfView();
            }
        }

        /// <summary>
        /// 現在の視点（注視点・距離・向き・画角）。
        /// </summary>
        public CameraPose Pose => new CameraPose
        {
            target = _target,
            distance = _distance,
            yaw = _yaw,
            pitch = _pitch,
            fieldOfView = FieldOfView,
            zoom = _zoom,
        };

        /// <summary>
        /// 保存しておいた視点に合わせる（範囲外の値は補正、壊れた値は無視）。Reset の戻り先は変えない。
        /// </summary>
        public void SetPose(CameraPose pose)
        {
            if (!pose.IsFinite)
            {
                return;
            }

            // 画角による拡大は、使う設定のときだけ戻す（旧版の記録は 0 = 拡大なし）
            _zoom = _zoomInsteadOfEntering ? Mathf.Clamp(pose.zoom, 1f, MaxZoom) : 1f;
            FieldOfView = pose.fieldOfView;
            _target = pose.target;
            _distance = Mathf.Clamp(pose.distance, MinDistance, MaxDistance);
            _yaw = pose.yaw;
            _pitch = Mathf.Clamp(pose.pitch, MinPitch, MaxPitch);
            Apply();
        }

        private void Awake()
        {
            // カメラ参照と既定値の初期化
            _camera = GetComponent<UnityEngine.Camera>();
            _camera.nearClipPlane = NearClip;
            FieldOfView = DefaultFieldOfView;
            _homeTarget = DefaultTarget;
            _homeDistance = DefaultDistance;
            ResetView();
        }

        /// <summary>
        /// 境界の高さと幅が画面に収まる距離で正面から映す。Reset の戻り先にもなる。
        /// </summary>
        public void Frame(Bounds bounds)
        {
            // 縦・横それぞれの半画角（横はアスペクト比から算出。画角による拡大は含めない）
            float tanHalfV = Mathf.Tan(_baseFieldOfView * 0.5f * Mathf.Deg2Rad);
            float tanHalfH = tanHalfV * _camera.aspect;

            // 高さ・幅の両方が収まる距離に、手前側の奥行き分を加える
            Vector3 extents = bounds.extents;
            float fitDistance = Mathf.Max(extents.y / tanHalfV, extents.x / tanHalfH) * FramePadding + extents.z;

            // 体に入り込まない距離はアバターの高さに比例させる（小さいアバターほど近づける）
            _noEnterDistance = Mathf.Clamp(bounds.size.y * NoEnterDistanceRatio, MinDistance, MaxDistance);
            _homeTarget = bounds.center;
            _homeDistance = Mathf.Clamp(fitDistance, MinDistance, MaxDistance);
            ResetView();
        }

        public void ResetView()
        {
            // 保存された注視点・距離に戻し、正面水平・拡大なしにする
            _target = _homeTarget;
            _distance = _homeDistance;
            _yaw = FrontYaw;
            _pitch = 0f;
            _zoom = 1f;
            ApplyFieldOfView();
            Apply();
        }

        private void LateUpdate()
        {
            // UI 操作中・固定中はカメラを動かさない
            if (!InputBlocked && !Locked)
            {
                HandleInput();
            }

            Apply();
        }

        private void HandleInput()
        {
            float dx = Input.GetAxis("Mouse X");
            float dy = Input.GetAxis("Mouse Y");

            // 右ドラッグで注視点まわりに回転
            if (Input.GetMouseButton(1))
            {
                _yaw += dx * RotateSpeed;
                _pitch = Mathf.Clamp(_pitch - dy * RotateSpeed, MinPitch, MaxPitch);
            }

            // 中ドラッグで画面平面に沿って注視点を移動（距離に比例して速く）
            if (Input.GetMouseButton(2))
            {
                _target -= (transform.right * dx + transform.up * dy) * (_distance * PanSpeed);
            }

            // ホイールで距離を指数的に変更（体に入り込まない設定では、近づき切った分を画角の拡大に回す）
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0f)
            {
                Zoom(Mathf.Max(0.1f, 1f - scroll * ZoomSpeed));
            }
        }

        private void Zoom(float factor)
        {
            // 従来どおり距離だけを変える
            if (!_zoomInsteadOfEntering)
            {
                _distance = Mathf.Clamp(_distance * factor, MinDistance, MaxDistance);
                return;
            }

            if (factor < 1f)
            {
                // 近づく: 入り込まない距離までは距離を縮め、足りない分は画角で拡大する
                float desired = _distance * factor;
                float rest = desired >= _noEnterDistance ? 1f : _noEnterDistance / Mathf.Max(desired, 1e-4f);
                _distance = Mathf.Max(desired, Mathf.Min(_distance, _noEnterDistance));
                _zoom = Mathf.Min(MaxZoom, _zoom * rest);
            }
            else
            {
                // 離れる: 先に画角の拡大を戻し、戻し切った残りで距離を広げる
                float zoom = _zoom / factor;
                float rest = zoom >= 1f ? 1f : 1f / zoom;
                _zoom = Mathf.Max(1f, zoom);
                _distance = Mathf.Clamp(_distance * rest, MinDistance, MaxDistance);
            }

            ApplyFieldOfView();
        }

        private void ApplyFieldOfView()
        {
            // 未初期化なら何もしない
            if (_camera == null)
            {
                return;
            }

            // 拡大の倍率だけ画角を狭める（倍率は画面上の大きさの比になるよう tan で割る）
            float tanHalf = Mathf.Tan(_baseFieldOfView * 0.5f * Mathf.Deg2Rad) / _zoom;
            _camera.fieldOfView = Mathf.Atan(tanHalf) * 2f * Mathf.Rad2Deg;
        }

        private void Apply()
        {
            // 体に入り込まない設定では、近すぎる距離（以前の記録・設定の切り替え直後）を入り込まない距離へ戻す
            if (_zoomInsteadOfEntering && _distance < _noEnterDistance)
            {
                _distance = _noEnterDistance;
            }

            // 球面座標からカメラ位置と向きを決定
            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            transform.SetPositionAndRotation(_target - rotation * Vector3.forward * _distance, rotation);
        }
    }
}
