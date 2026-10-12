using UnityEngine;
using VRCast.Avatars;
using VRCast.Core;

namespace VRCast.UI
{
    /// <summary>
    /// オブジェクトタブのパーティクルのカード（VRChat で一緒に ON/OFF するまとまりごとの ON/OFF・名前の変更、アバターごとに記録）。
    /// パーティクルが見つかったアバターだけに出す。
    /// </summary>
    public class ParticleSection
    {
        // 名前の変更ボタンと、決定・取り消しボタンの幅
        private const float RenameButtonWidth = 120f;
        private const float ConfirmButtonWidth = 70f;

        private readonly AvatarSession _session;
        private readonly AvatarComponentCache _avatar;
        private readonly AppSettings _settings;
        private AvatarParticles _particles;

        // 名前を変更中のパーティクルのまとまり（null = 変更していない）と、入力中の名前
        private AvatarParticles.Group _renamingParticle;
        private string _particleNameInput;

        public ParticleSection(AvatarSession session, AppSettings settings)
        {
            _session = session;
            _avatar = new AvatarComponentCache(session);
            _settings = settings;
        }

        public void Draw()
        {
            // アバターが替わったときだけ取り直す（名前の変更中ならやめる）
            if (_avatar.Refresh())
            {
                _particles = _avatar.Get<AvatarParticles>();
                _renamingParticle = null;
            }

            // パーティクルが見つかったアバターだけに出す
            if (_particles == null || _particles.Groups.Count == 0)
            {
                return;
            }

            GuiControls.BeginCard(Loc.T("Particles", "パーティクル", "파티클", "粒子", "粒子"));
            GuiControls.Hint(Loc.T(
                $"{_particles.Groups.Count} found ({_particles.EnabledCount} on). Particles switched together in VRChat are grouped. Turning one on also shows its hidden parent objects. Saved per avatar.",
                $"{_particles.Groups.Count} 個見つかりました（{_particles.EnabledCount} 個 ON）。VRChat で一緒に ON/OFF するものは 1 つにまとめています。ON にすると、非表示の親のオブジェクトも表示します。アバターごとに保存されます。",
                $"{_particles.Groups.Count}개 발견 ({_particles.EnabledCount}개 켜짐). VRChat에서 함께 켜고 끄는 것은 하나로 묶었습니다. 켜면 숨겨진 상위 오브젝트도 표시합니다. 아바타별로 저장됩니다.",
                $"找到 {_particles.Groups.Count} 个（{_particles.EnabledCount} 个开启）。在 VRChat 中一起开关的粒子会合并为一项。开启后也会显示被隐藏的父对象。按虚拟形象分别保存。",
                $"找到 {_particles.Groups.Count} 個（{_particles.EnabledCount} 個開啟）。在 VRChat 中一起開關的粒子會合併為一項。開啟後也會顯示被隱藏的父物件。依虛擬形象分別儲存。"));

            // まとめて ON / OFF
            bool changed = false;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("All on", "すべて ON", "모두 켜기", "全部开启", "全部開啟"), GuiControls.Shrinkable))
            {
                _particles.SetAll(true);
                changed = true;
            }

            if (GUILayout.Button(Loc.T("All off", "すべて OFF", "모두 끄기", "全部关闭", "全部關閉"), GuiControls.Shrinkable))
            {
                _particles.SetAll(false);
                changed = true;
            }

            GUILayout.EndHorizontal();

            // まとまりごとの ON/OFF・名前の変更と、Unity 上の場所
            foreach (AvatarParticles.Group group in _particles.Groups)
            {
                changed |= DrawGroup(group);
            }

            // 変えたらこのアバターの分として記録する（保存は終了時）
            if (changed && _session.Current != null)
            {
                _settings.SetParticleToggles(_session.Current.SourcePath, _particles.Export());
            }

            GuiControls.EndCard();
        }

        private bool DrawGroup(AvatarParticles.Group group)
        {
            bool changed = false;

            // ON/OFF（複数をまとめたものは数も出す）と、名前の変更ボタン
            GUILayout.BeginHorizontal();
            string label = group.SystemCount > 1 ? $"{group.Label} ({group.SystemCount})" : group.Label;
            bool enabled = GUILayout.Toggle(group.Enabled, label, GuiControls.Shrinkable);
            if (enabled != group.Enabled)
            {
                _particles.SetEnabled(group, enabled);
                changed = true;
            }

            // 名前の入力を始める（今の名前から）
            if (_renamingParticle != group
                && GUILayout.Button(Loc.T("Rename", "名前を変更", "이름 변경", "重命名", "重新命名"),
                    GUILayout.Width(RenameButtonWidth)))
            {
                _renamingParticle = group;
                _particleNameInput = group.Label;
            }

            GUILayout.EndHorizontal();

            // 名前の入力中: 決定で付ける（空なら Unity 上の名前に戻す）、取り消しで閉じる
            if (_renamingParticle == group)
            {
                GUILayout.BeginHorizontal();
                _particleNameInput = GUILayout.TextField(
                    _particleNameInput ?? string.Empty, AvatarParticles.MaxNameLength, GuiControls.Shrinkable);
                if (GUILayout.Button("OK", GUILayout.Width(ConfirmButtonWidth)))
                {
                    AvatarParticles.SetName(group, _particleNameInput);
                    _renamingParticle = null;
                    changed = true;
                }

                if (GUILayout.Button(Loc.T("Cancel", "取消", "취소", "取消", "取消"), GUILayout.Width(ConfirmButtonWidth)))
                {
                    _renamingParticle = null;
                }

                GUILayout.EndHorizontal();
                GuiControls.Hint(Loc.T(
                    "Leave empty to use the name in Unity.",
                    "空にすると Unity 上の名前に戻ります。",
                    "비워 두면 Unity의 이름으로 돌아갑니다.",
                    "留空则恢复为 Unity 中的名称。",
                    "留空則恢復為 Unity 中的名稱。"));
            }

            // Unity 上の場所（名前を変えたときは元の名前も分かるように）
            GuiControls.Hint(Loc.T("Path: ", "場所: ", "경로: ", "路径: ", "路徑: ") + group.Path);
            return changed;
        }
    }
}
