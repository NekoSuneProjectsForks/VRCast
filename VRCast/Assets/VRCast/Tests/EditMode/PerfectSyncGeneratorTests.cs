using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VRCast.AvatarFormat;
using VRCast.PerfectSync;
using VRCast.Tracking;

namespace VRCast.Tests
{
    /// <summary>
    /// 目印からの顔の座標系（FaceRig）・目印の扱い（FaceLandmarks / FaceMeshGeometry）・形状の生成を確認する。
    /// </summary>
    public class PerfectSyncGeneratorTests
    {
        [Test]
        public void Landmarks_DetailLevelsGrowAndMirror()
        {
            // 細かさが上がるほど目印が増え、最小の段階が必須の目印になること
            int minimal = FaceLandmarks.For(LandmarkDetail.Minimal).Count;
            int standard = FaceLandmarks.For(LandmarkDetail.Standard).Count;
            int detailed = FaceLandmarks.For(LandmarkDetail.Detailed).Count;
            Assert.That(minimal, Is.EqualTo(11));
            Assert.That(standard, Is.GreaterThan(minimal));
            Assert.That(detailed, Is.GreaterThan(standard));
            Assert.That(FaceLandmarks.Required, Has.Count.EqualTo(minimal));

            // 左右の組は互いに相手を持ち、中央の目印は相手が無いこと
            Assert.That(FaceLandmarks.TryGetMirror(LandmarkId.EyeUpperLeft, out LandmarkId right), Is.True);
            Assert.That(right, Is.EqualTo(LandmarkId.EyeUpperRight));
            Assert.That(FaceLandmarks.TryGetMirror(LandmarkId.EyeUpperRight, out LandmarkId left), Is.True);
            Assert.That(left, Is.EqualTo(LandmarkId.EyeUpperLeft));
            Assert.That(FaceLandmarks.TryGetMirror(LandmarkId.Chin, out _), Is.False);
            Assert.That(FaceLandmarks.IsDefined((int)LandmarkId.JawAngleRight), Is.True);
            Assert.That(FaceLandmarks.IsDefined(99), Is.False);
        }

        [Test]
        public void Rig_Build_FindsAxesAndCompletesPoints()
        {
            Dictionary<LandmarkId, Vector3> points = PerfectSyncTestFace.MinimalPoints();
            FaceRig rig = FaceRig.Build(points, PerfectSyncTestFace.Normals(points.Keys), out List<LandmarkId> missing);

            // アバターの左は -X、上は +Y、前は +Z、大きさは両目の中心の間隔になること
            Assert.That(rig, Is.Not.Null);
            Assert.That(missing, Is.Empty);
            Assert.That(Vector3.Distance(rig.Left, Vector3.left), Is.LessThan(1e-4f));
            Assert.That(Vector3.Distance(rig.Up, Vector3.up), Is.LessThan(1e-4f));
            Assert.That(Vector3.Distance(rig.Forward, Vector3.forward), Is.LessThan(1e-4f));
            Assert.That(rig.Scale, Is.EqualTo(0.7f).Within(1e-4f));

            // 打っていない目印も推定で補われ、左側の目頭は左目の中央より内側（局所座標の x が小さい）になること
            Assert.That(rig[LandmarkId.EyeInnerLeft].x, Is.LessThan(rig[LandmarkId.EyeUpperLeft].x));
            Assert.That(rig[LandmarkId.NoseTip].z, Is.GreaterThan(0f));
            Assert.That(rig[LandmarkId.JawAngleRight].x, Is.LessThan(0f));
        }

        [Test]
        public void Rig_Build_ReportsMissingPoints()
        {
            // あごが無ければ作らず、足りない目印として返すこと
            Dictionary<LandmarkId, Vector3> points = PerfectSyncTestFace.MinimalPoints();
            points.Remove(LandmarkId.Chin);
            FaceRig rig = FaceRig.Build(points, new Dictionary<LandmarkId, Vector3>(), out List<LandmarkId> missing);

            Assert.That(rig, Is.Null);
            Assert.That(missing, Is.EqualTo(new[] { LandmarkId.Chin }));
        }

        [Test]
        public void Rig_Build_WithoutBrows_EstimatesBrows()
        {
            // 眉が無いアバター: 眉の目印を打たなくても作れ、向きは同じで、眉は目より上に推定されること
            Dictionary<LandmarkId, Vector3> points = PerfectSyncTestFace.MinimalPoints();
            points.Remove(LandmarkId.BrowMidLeft);
            points.Remove(LandmarkId.BrowMidRight);
            FaceRig rig = FaceRig.Build(points, PerfectSyncTestFace.Normals(points.Keys), out List<LandmarkId> missing,
                FaceParts.Brows);

            Assert.That(rig, Is.Not.Null);
            Assert.That(missing, Is.Empty);
            Assert.That(Vector3.Distance(rig.Up, Vector3.up), Is.LessThan(1e-3f));
            Assert.That(rig[LandmarkId.BrowMidLeft].y, Is.GreaterThan(rig[LandmarkId.EyeUpperLeft].y));
            Assert.That(rig[LandmarkId.BrowOuterRight].x, Is.LessThan(0f));

            // 眉が「ある」ままなら、眉の目印が足りないと返すこと
            Assert.That(FaceRig.Build(points, PerfectSyncTestFace.Normals(points.Keys), out missing), Is.Null);
            Assert.That(missing, Is.EqualTo(new[] { LandmarkId.BrowMidLeft, LandmarkId.BrowMidRight }));
        }

        [Test]
        public void FaceParts_FilterLandmarksAndClassifyShapes()
        {
            // 無い部位の目印は一覧と必須から外れること
            List<LandmarkId> withoutBrows = FaceLandmarks.For(LandmarkDetail.Detailed, FaceParts.Brows | FaceParts.Nose);
            Assert.That(withoutBrows, Has.None.EqualTo(LandmarkId.BrowInnerLeft));
            Assert.That(withoutBrows, Has.None.EqualTo(LandmarkId.NoseTip));
            Assert.That(withoutBrows, Has.Some.EqualTo(LandmarkId.Chin));
            Assert.That(FaceLandmarks.RequiredFor(FaceParts.Brows), Has.Count.EqualTo(FaceLandmarks.Required.Count - 2));

            // 形状の部位分け
            Assert.That(FacePartRules.Shape("browInnerUp"), Is.EqualTo(FaceParts.Brows));
            Assert.That(FacePartRules.Shape("noseSneerLeft"), Is.EqualTo(FaceParts.Nose));
            Assert.That(FacePartRules.Shape("jawOpen"), Is.EqualTo(FaceParts.MouthInterior));
            Assert.That(FacePartRules.Shape("eyeBlinkLeft"), Is.EqualTo(FaceParts.None));
            Assert.That(FacePartRules.Landmark(LandmarkId.NoseWingRight), Is.EqualTo(FaceParts.Nose));
            Assert.That(FacePartRules.Landmark(LandmarkId.EyeUpperLeft), Is.EqualTo(FaceParts.None));
        }

        [Test]
        public void Geometry_RaycastNearestAndReflect()
        {
            FaceMeshGeometry geometry = PerfectSyncTestFace.CreateGeometry();

            // 前から撃った光線が当たった点を、目印の位置として同じ場所に戻せること
            var target = new Vector3(-0.33f, 0.21f, 0f);
            var ray = new Ray(target + Vector3.forward, Vector3.back);
            Assert.That(geometry.Raycast(geometry.Vertices, ray, null, out PerfectSyncLandmark hit, out float distance), Is.True);
            Assert.That(distance, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(geometry.TryGetPoint(hit, out Vector3 point), Is.True);
            Assert.That(Vector3.Distance(point, target), Is.LessThan(1e-4f));

            // 表面から離れた点に最も近い表面の点は、真下（z = 0）の同じ位置になること
            PerfectSyncLandmark nearest = geometry.Nearest(new Vector3(0.33f, 0.21f, 0.5f), (int)LandmarkId.EyeLowerRight);
            Assert.That(nearest.id, Is.EqualTo((int)LandmarkId.EyeLowerRight));
            Assert.That(geometry.TryGetPoint(nearest, out Vector3 surface), Is.True);
            Assert.That(Vector3.Distance(surface, new Vector3(0.33f, 0.21f, 0f)), Is.LessThan(1e-4f));

            // X = 0 の面で反転すると x の符号だけ変わること
            Vector3 reflected = FaceMeshGeometry.Reflect(target, Vector3.zero, Vector3.right);
            Assert.That(Vector3.Distance(reflected, new Vector3(0.33f, 0.21f, 0f)), Is.LessThan(1e-5f));

            // 左右の組から推定した対称の面は X = 0 になること
            FaceRig.EstimateSymmetryPlane(PerfectSyncTestFace.MinimalPoints(), out Vector3 planePoint, out Vector3 planeNormal);
            Assert.That(Mathf.Abs(planePoint.x), Is.LessThan(1e-4f));
            Assert.That(Mathf.Abs(Vector3.Dot(planeNormal, Vector3.right)), Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void Generate_MovesTheRightRegions()
        {
            FaceMeshGeometry geometry = PerfectSyncTestFace.CreateGeometry();
            List<PerfectSyncShape> shapes = GenerateAll(geometry, 1f);

            // まばたき（左）は左目のまわり（-X 側）だけを動かし、上まぶたは下がること
            PerfectSyncShape blinkLeft = Find(shapes, "eyeBlinkLeft");
            Assert.That(blinkLeft, Is.Not.Null);
            foreach (int vertex in blinkLeft.Vertices)
            {
                Assert.That(geometry.Vertices[vertex].x, Is.LessThan(0f));
            }

            int upperLid = PerfectSyncTestFace.VertexAt(new Vector3(-0.35f, 0.35f, 0f));
            Assert.That(Delta(blinkLeft, upperLid).y, Is.LessThan(0f));

            // まばたき（右）は +X 側だけを動かすこと
            PerfectSyncShape blinkRight = Find(shapes, "eyeBlinkRight");
            Assert.That(blinkRight, Is.Not.Null);
            foreach (int vertex in blinkRight.Vertices)
            {
                Assert.That(geometry.Vertices[vertex].x, Is.GreaterThan(0f));
            }

            // 口を開くとあご先は下がり、額は動かないこと
            PerfectSyncShape jawOpen = Find(shapes, "jawOpen");
            Assert.That(jawOpen, Is.Not.Null);
            Assert.That(Delta(jawOpen, PerfectSyncTestFace.VertexAt(new Vector3(0f, -0.85f, 0f))).y, Is.LessThan(0f));
            Assert.That(Array.IndexOf(jawOpen.Vertices, PerfectSyncTestFace.VertexAt(new Vector3(0f, 0.85f, 0f))),
                Is.EqualTo(-1));

            // 作った形状は保存できる内容であること
            var data = new PerfectSyncShapeData();
            data.Shapes.AddRange(shapes);
            Assert.That(data.Validate(), Is.Null);
        }

        [Test]
        public void Generate_HonorsStrengthAndMouthParts()
        {
            FaceMeshGeometry geometry = PerfectSyncTestFace.CreateGeometry();

            // 強さ 0 なら何も作らないこと
            Assert.That(GenerateAll(geometry, 0f), Is.Empty);

            // 強さ 2 なら同じ頂点が 2 倍動くこと
            PerfectSyncShape single = Find(GenerateAll(geometry, 1f), "jawOpen");
            PerfectSyncShape doubled = Find(GenerateAll(geometry, 2f), "jawOpen");
            int chin = PerfectSyncTestFace.VertexAt(new Vector3(0f, -0.85f, 0f));
            Assert.That(Delta(doubled, chin).y, Is.EqualTo(Delta(single, chin).y * 2f).Within(1e-5f));

            // 歯・舌のメッシュには顎の形状だけを作ること
            Dictionary<LandmarkId, Vector3> points = PerfectSyncTestFace.MinimalPoints();
            FaceRig rig = FaceRig.Build(points, PerfectSyncTestFace.Normals(points.Keys), out _);
            var teeth = new PerfectSyncGenerator.MeshInput
            {
                Path = "Teeth",
                Vertices = new[] { new Vector3(0f, -0.5f, -0.05f), new Vector3(0f, -0.6f, -0.05f) },
                IsMouthPart = true,
            };
            List<PerfectSyncShape> shapes = PerfectSyncGenerator.Generate(rig, new[] { teeth }, Strengths(1f));
            Assert.That(shapes, Is.Not.Empty);
            foreach (PerfectSyncShape shape in shapes)
            {
                Assert.That(shape.Name, Does.StartWith("jaw"));
                Assert.That(shape.Path, Is.EqualTo("Teeth"));
            }
        }

        private static List<PerfectSyncShape> GenerateAll(FaceMeshGeometry geometry, float strength)
        {
            // 平らな顔の全形状を同じ強さで作る
            Dictionary<LandmarkId, Vector3> points = PerfectSyncTestFace.MinimalPoints();
            FaceRig rig = FaceRig.Build(points, PerfectSyncTestFace.Normals(points.Keys), out _);
            var face = new PerfectSyncGenerator.MeshInput
            {
                Path = "Body",
                Vertices = geometry.Vertices,
                Normals = geometry.Normals,
            };
            return PerfectSyncGenerator.Generate(rig, new[] { face }, Strengths(strength));
        }

        private static float[] Strengths(float strength)
        {
            // ARKit の全形状に同じ強さ
            var strengths = new float[ArKitFace.BlendShapeNames.Length];
            for (int i = 0; i < strengths.Length; i++)
            {
                strengths[i] = strength;
            }

            return strengths;
        }

        private static PerfectSyncShape Find(List<PerfectSyncShape> shapes, string name)
        {
            return shapes.Find(s => s.Name == name);
        }

        private static Vector3 Delta(PerfectSyncShape shape, int vertex)
        {
            // 頂点の移動量（動かない頂点は 0）
            int i = Array.IndexOf(shape.Vertices, vertex);
            return i < 0 ? Vector3.zero : new Vector3(shape.Deltas[i * 3], shape.Deltas[i * 3 + 1], shape.Deltas[i * 3 + 2]);
        }
    }
}
