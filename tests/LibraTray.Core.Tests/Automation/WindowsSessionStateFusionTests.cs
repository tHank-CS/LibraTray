using LibraTray.Core.Automation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LibraTray.Core.Tests.Automation;

[TestClass]
public sealed class WindowsSessionStateFusionTests
{
    [TestMethod]
    public void SecureScreenSaverRemainsLockedUntilExplicitSessionUnlock()
    {
        var fusion = new WindowsSessionStateFusion();

        Assert.AreEqual(
            WindowsLifecycleEventKind.SessionLocked,
            fusion.Observe(sessionLocked: false, secureScreenSaverRunning: true));
        Assert.IsNull(
            fusion.Observe(sessionLocked: true, secureScreenSaverRunning: false));
        Assert.IsNull(
            fusion.Observe(sessionLocked: true, secureScreenSaverRunning: false));
        Assert.AreEqual(
            WindowsLifecycleEventKind.SessionUnlocked,
            fusion.Observe(
                sessionLocked: false,
                secureScreenSaverRunning: false,
                forcedLocked: false,
                explicitUnlock: true));
    }

    [TestMethod]
    public void TwoConfirmedUnlockedSamplesReleaseWithoutAnEvent()
    {
        var fusion = new WindowsSessionStateFusion();
        _ = fusion.Observe(sessionLocked: true, secureScreenSaverRunning: false);

        Assert.IsNull(
            fusion.Observe(sessionLocked: false, secureScreenSaverRunning: false));
        Assert.AreEqual(
            WindowsLifecycleEventKind.SessionUnlocked,
            fusion.Observe(sessionLocked: false, secureScreenSaverRunning: false));
    }

    [TestMethod]
    public void UnknownQueriesDoNotClearAnExistingLock()
    {
        var fusion = new WindowsSessionStateFusion();
        _ = fusion.Observe(sessionLocked: true, secureScreenSaverRunning: false);

        Assert.IsNull(
            fusion.Observe(sessionLocked: null, secureScreenSaverRunning: null));
        Assert.IsNull(
            fusion.Observe(sessionLocked: false, secureScreenSaverRunning: false));
    }

    [TestMethod]
    public void ProgramStartingWhileLockedEmitsOneLockEdge()
    {
        var fusion = new WindowsSessionStateFusion();

        Assert.AreEqual(
            WindowsLifecycleEventKind.SessionLocked,
            fusion.Observe(sessionLocked: true, secureScreenSaverRunning: false));
        Assert.IsNull(
            fusion.Observe(sessionLocked: true, secureScreenSaverRunning: false));
    }
}
