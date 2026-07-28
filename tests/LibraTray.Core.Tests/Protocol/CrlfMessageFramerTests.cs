using System.Text;
using LibraTray.Core.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibraTray.Core.Tests.Protocol;

[TestClass]
public sealed class CrlfMessageFramerTests
{
    [TestMethod]
    public void AppendSplitFrameIncludingSplitDelimiterEmitsOnlyWhenComplete()
    {
        var framer = new CrlfMessageFramer();

        IReadOnlyList<byte[]> first = framer.Append(
            Encoding.UTF8.GetBytes("""{"id":1,"res"""));
        IReadOnlyList<byte[]> second = framer.Append(
            Encoding.UTF8.GetBytes("""ult":["ok"]}""" + "\r"));
        IReadOnlyList<byte[]> third = framer.Append([(byte)'\n']);

        Assert.HasCount(0, first);
        Assert.HasCount(0, second);
        Assert.HasCount(1, third);
        Assert.AreEqual(
            """{"id":1,"result":["ok"]}""",
            Encoding.UTF8.GetString(third[0]));
        Assert.AreEqual(0, framer.BufferedByteCount);
    }

    [TestMethod]
    public void AppendStickyFramesEmitsEveryFrameInOrder()
    {
        var framer = new CrlfMessageFramer();

        IReadOnlyList<byte[]> frames = framer.Append(
            Encoding.ASCII.GetBytes("one\r\ntwo\r\nthree\r\n"));

        Assert.HasCount(3, frames);
        Assert.AreEqual("one", Encoding.ASCII.GetString(frames[0]));
        Assert.AreEqual("two", Encoding.ASCII.GetString(frames[1]));
        Assert.AreEqual("three", Encoding.ASCII.GetString(frames[2]));
    }

    [TestMethod]
    public void AppendCarriageReturnNotFollowedByLineFeedRemainsPayload()
    {
        var framer = new CrlfMessageFramer();

        IReadOnlyList<byte[]> frames = framer.Append(
            Encoding.ASCII.GetBytes("a\rb\r\n"));

        Assert.HasCount(1, frames);
        CollectionAssert.AreEqual(
            Encoding.ASCII.GetBytes("a\rb"),
            frames[0]);
    }

    [TestMethod]
    public void AppendFrameAtLimitIsAccepted()
    {
        var framer = new CrlfMessageFramer(maximumFrameBytes: 8);

        IReadOnlyList<byte[]> frames = framer.Append(
            Encoding.ASCII.GetBytes("12345678\r\n"));

        Assert.HasCount(1, frames);
        Assert.AreEqual("12345678", Encoding.ASCII.GetString(frames[0]));
    }

    [TestMethod]
    public void AppendFrameOverLimitIsRejectedAndFramerCanBeReused()
    {
        var framer = new CrlfMessageFramer(maximumFrameBytes: 8);

        YeelightFrameTooLargeException exception =
            Assert.ThrowsExactly<YeelightFrameTooLargeException>(
                () => framer.Append(Encoding.ASCII.GetBytes("123456789")));

        Assert.AreEqual(8, exception.MaximumFrameBytes);
        Assert.AreEqual(0, framer.BufferedByteCount);

        IReadOnlyList<byte[]> recovered = framer.Append(
            Encoding.ASCII.GetBytes("ok\r\n"));
        Assert.HasCount(1, recovered);
        Assert.AreEqual("ok", Encoding.ASCII.GetString(recovered[0]));
    }
}
