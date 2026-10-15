using NUnit.Framework;
using UnityEngine;
using VRCast.Tracking;

namespace VRCast.Tests
{
    /// <summary>
    /// トラッカーの値へなめらかに寄せる PointSmoother を検証する。
    /// </summary>
    public class PointSmootherTests
    {
        private const float SmoothTime = 0.05f;
        private const float Frame = 1f / 60f;

        [Test]
        public void FirstPush_SnapsToValue()
        {
            var smoother = new PointSmoother(1, SmoothTime);
            smoother.Push(new[] { new Vector3(1f, 2f, 3f) }, 0f);
            smoother.Update(0f);

            Assert.That(smoother.HasValue, Is.True);
            Assert.That(smoother.Current[0], Is.EqualTo(new Vector3(1f, 2f, 3f)));
        }

        [Test]
        public void MovesTowardTarget_WithoutStopping()
        {
            // 次の値が届かない間も毎フレーム少しずつ近づき続ける（途中で止まらない）
            var smoother = new PointSmoother(1, SmoothTime);
            smoother.Push(new[] { Vector3.zero }, 0f);
            smoother.Push(new[] { Vector3.right }, 0f);

            float previous = 0f;
            for (int i = 1; i <= 6; i++)
            {
                smoother.Update(i * Frame);
                float x = smoother.Current[0].x;
                Assert.That(x, Is.GreaterThan(previous));
                Assert.That(x, Is.LessThanOrEqualTo(1f));
                previous = x;
            }
        }

        [Test]
        public void ReachesTarget_AfterEnoughTime()
        {
            var smoother = new PointSmoother(1, SmoothTime);
            smoother.Push(new[] { Vector3.zero }, 0f);
            smoother.Push(new[] { Vector3.right }, 0f);
            for (int i = 1; i <= 60; i++)
            {
                smoother.Update(i * Frame);
            }

            Assert.That(smoother.Current[0].x, Is.EqualTo(1f).Within(1e-3f));
        }

        [Test]
        public void Reset_SnapsNextValue()
        {
            var smoother = new PointSmoother(1, SmoothTime);
            smoother.Push(new[] { Vector3.zero }, 0f);
            smoother.Reset();

            Assert.That(smoother.HasValue, Is.False);

            // 見失った後の最初の値は寄せずに合わせる
            smoother.Push(new[] { Vector3.up }, 1f);
            smoother.Update(1f);
            Assert.That(smoother.Current[0], Is.EqualTo(Vector3.up));
        }

        [Test]
        public void IntervalShare_FollowsChangeInterval()
        {
            var smoother = new PointSmoother(1, 0.02f, 0.1f, 0.8f);

            // 間隔が分かるまでは上限
            smoother.Push(new[] { Vector3.zero }, 0f);
            Assert.That(smoother.SmoothTime, Is.EqualTo(0.1f));

            // 値が 1/30 秒ごとに変わるなら、寄る秒数はその 0.8 倍
            for (int i = 1; i <= 30; i++)
            {
                smoother.Push(new[] { Vector3.right * i }, i / 30f);
            }

            Assert.That(smoother.SmoothTime, Is.EqualTo(0.8f / 30f).Within(1e-4f));
        }

        [Test]
        public void IntervalShare_IgnoresRepeatedValues()
        {
            var smoother = new PointSmoother(1, 0.02f, 0.2f, 1f);
            smoother.Push(new[] { Vector3.zero }, 0f);

            // 1/30 秒ごとに届くが値は 2 回に 1 回しか変わらない（トラッカーが推定を間引いた）なら、変わる間隔（1/15 秒）に合わせる
            for (int i = 1; i <= 40; i++)
            {
                smoother.Push(new[] { Vector3.right * (i / 2) }, i / 30f);
            }

            Assert.That(smoother.SmoothTime, Is.EqualTo(1f / 15f).Within(1e-3f));
        }

        [Test]
        public void SmoothTimeFloor_OnlyLengthens()
        {
            var smoother = new PointSmoother(1, 0.02f, 0.1f, 0.8f);
            smoother.Push(new[] { Vector3.zero }, 0f);
            for (int i = 1; i <= 30; i++)
            {
                smoother.Push(new[] { Vector3.right * i }, i / 30f);
            }

            // 下限が間隔に合わせた秒数より長ければ下限、短ければ間隔に合わせた秒数のまま
            smoother.SmoothTimeFloor = 0.07f;
            Assert.That(smoother.SmoothTime, Is.EqualTo(0.07f));
            smoother.SmoothTimeFloor = 0.01f;
            Assert.That(smoother.SmoothTime, Is.EqualTo(0.8f / 30f).Within(1e-4f));
        }

        [Test]
        public void IntervalShare_ClampsToRange()
        {
            var smoother = new PointSmoother(1, 0.03f, 0.1f, 1f);
            smoother.Push(new[] { Vector3.zero }, 0f);

            // 毎描画フレーム（1/120 秒）変わっても下限より短くしない
            for (int i = 1; i <= 30; i++)
            {
                smoother.Push(new[] { Vector3.right * i }, i / 120f);
            }

            Assert.That(smoother.SmoothTime, Is.EqualTo(0.03f));
        }
    }
}
