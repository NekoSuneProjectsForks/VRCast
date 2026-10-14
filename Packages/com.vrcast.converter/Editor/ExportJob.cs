using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using UnityEditor;

namespace VRCast.Converter.Editor
{
    /// <summary>
    /// 1 工程ずつ進める書き出し（<see cref="AvatarExporter.Start"/> で作る）。
    /// 工程の合間にエディタへ戻るので、ウィンドウに進み具合を描ける。進めている間はスクリプトの再読み込みを止める。
    /// </summary>
    public sealed class ExportJob : IDisposable
    {
        /// <summary>
        /// これから行う工程（並びは進み具合の計算に使う）。
        /// </summary>
        public enum Step
        {
            Preparing,
            ApplyingTools,
            ReadingAvatar,
            ReadingExpressions,
            Cleaning,
            SavingPrefab,
            BuildingBundle,
            Writing,
        }

        // 工程の数（進み具合の分母）
        private const int StepCount = (int)Step.Writing + 1;

        internal sealed class ResultBox
        {
            public AvatarExporter.Report Report;
        }

        private readonly IEnumerator<Step> _steps;
        private readonly ResultBox _result;
        private bool _reloadLocked;

        internal ExportJob(IEnumerator<Step> steps, ExportLog log, ResultBox result)
        {
            _steps = steps;
            _result = result;
            Log = log;
            EditorApplication.LockReloadAssemblies();
            _reloadLocked = true;
        }

        public ExportLog Log { get; }

        public Step Current { get; private set; }

        public bool IsDone { get; private set; }

        public Exception Error { get; private set; }

        public AvatarExporter.Report Report => _result.Report;

        /// <summary>
        /// 0〜1 の進み具合（終わっていれば 1）。
        /// </summary>
        public float Progress => IsDone ? 1f : (float)Current / StepCount;

        /// <summary>
        /// 次の工程まで進める。終わった（成功・失敗とも）なら false。
        /// </summary>
        public bool MoveNext()
        {
            if (IsDone)
            {
                return false;
            }

            // 工程の間だけ記録先にする（合間にほかの処理が Console に出したものは取り込まない）
            using (Log.Begin())
            {
                try
                {
                    if (_steps.MoveNext())
                    {
                        Current = _steps.Current;
                        return true;
                    }
                }
                catch (Exception e)
                {
                    Error = e;
                }
            }

            Finish();
            return false;
        }

        /// <summary>
        /// 失敗していれば、その例外を元の場所の情報のまま投げ直す。
        /// </summary>
        public void ThrowIfFailed()
        {
            if (Error != null)
            {
                ExceptionDispatchInfo.Capture(Error).Throw();
            }
        }

        /// <summary>
        /// 終わっていなければ中断する（複製と一時ファイルは片付ける）。
        /// </summary>
        public void Dispose()
        {
            if (!IsDone)
            {
                using (Log.Begin())
                {
                    _steps.Dispose();
                }
            }

            Finish();
        }

        private void Finish()
        {
            IsDone = true;
            if (_reloadLocked)
            {
                _reloadLocked = false;
                EditorApplication.UnlockReloadAssemblies();
            }
        }
    }
}
