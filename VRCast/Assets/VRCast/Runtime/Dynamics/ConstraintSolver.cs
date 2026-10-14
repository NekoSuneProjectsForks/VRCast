using System.Collections.Generic;
using UnityEngine;
using VRCast.AvatarFormat;
using VRCast.Core;

namespace VRCast.Dynamics
{
    /// <summary>
    /// アバターの全 Constraint を毎フレーム評価する。トラッキング適用後・揺れもの計算前に動かす。
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class ConstraintSolver : MonoBehaviour
    {
        // ログのカテゴリ名
        private const string LogCategory = "Constraint";

        private List<ConstraintEvaluator> _evaluators = new List<ConstraintEvaluator>();

        public int Count => _evaluators.Count;

        public void Initialize(Animator animator, ConstraintSet set)
        {
            // 解決でき、骨格を動かさないものだけを、他の Constraint の結果を参照するものが後になる順で残す
            _evaluators = ConstraintEvaluator.CreateAll(transform, animator, set, out int skipped);
            VRCastLog.Info(LogCategory,
                $"Resolved {_evaluators.Count}/{set.constraints.Length} constraints ({skipped} on humanoid bones skipped).");

            // 揺れものが静止姿勢を記録する前に一度適用しておく
            EvaluateAll();
        }

        private void LateUpdate()
        {
            EvaluateAll();
        }

        private void EvaluateAll()
        {
            foreach (ConstraintEvaluator evaluator in _evaluators)
            {
                evaluator.Evaluate();
            }
        }
    }
}
