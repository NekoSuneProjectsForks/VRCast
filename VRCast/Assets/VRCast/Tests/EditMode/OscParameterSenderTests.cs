using System.Collections.Generic;
using NUnit.Framework;
using VRCast.Remote;
using VRCast.Tracking;

namespace VRCast.Tests
{
    public class OscParameterSenderTests
    {
        [TestCaseSource(nameof(ArKitMappingCases))]
        public void ArKitMappingIndices_MatchBlendshapeNames(int index, string expectedName)
        {
            Assert.That(index, Is.GreaterThanOrEqualTo(0));
            Assert.That(index, Is.LessThan(ArKitFace.BlendShapeNames.Length));
            Assert.That(ArKitFace.BlendShapeNames[index], Is.EqualTo(expectedName));
        }

        private static IEnumerable<object[]> ArKitMappingCases()
        {
            yield return new object[] { ArKitFace.EyeBlinkLeft, "eyeBlinkLeft" };
            yield return new object[] { ArKitFace.EyeBlinkRight, "eyeBlinkRight" };
            yield return new object[] { ArKitFace.JawOpen, "jawOpen" };
            yield return new object[] { ArKitFace.MouthSmileLeft, "mouthSmileLeft" };
            yield return new object[] { ArKitFace.MouthSmileRight, "mouthSmileRight" };
            yield return new object[] { ArKitFace.BrowInnerUp, "browInnerUp" };
        }

        [Test]
        public void CreateFloat_EncodesVRChatAvatarParameterMessage()
        {
            byte[] packet = OscParameterPacket.CreateFloat("/avatar/parameters/Blink", 0.75f);
            var messages = new List<OscMessage>();

            OscPacket.Parse(packet, 0, packet.Length, messages);

            Assert.That(messages.Count, Is.EqualTo(1));
            Assert.That(messages[0].Address, Is.EqualTo("/avatar/parameters/Blink"));
            Assert.That(messages[0].Kind, Is.EqualTo(OscValueKind.Float));
            Assert.That(messages[0].Float, Is.EqualTo(0.75f).Within(0.0001f));
        }

        [TestCase(true, OscValueKind.True)]
        [TestCase(false, OscValueKind.False)]
        public void CreateBool_EncodesOscBoolean(bool value, OscValueKind expected)
        {
            byte[] packet = OscParameterPacket.CreateBool("/avatar/parameters/HandGesture", value);
            var messages = new List<OscMessage>();

            OscPacket.Parse(packet, 0, packet.Length, messages);

            Assert.That(messages.Count, Is.EqualTo(1));
            Assert.That(messages[0].Address, Is.EqualTo("/avatar/parameters/HandGesture"));
            Assert.That(messages[0].Kind, Is.EqualTo(expected));
        }
    }
}