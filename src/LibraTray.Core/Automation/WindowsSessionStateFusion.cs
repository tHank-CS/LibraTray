namespace LibraTray.Core.Automation;

/// <summary>
/// Fuses WTS lock and secure-screensaver observations into debounced lifecycle
/// edges. Unknown samples never clear a known lock blocker.
/// </summary>
public sealed class WindowsSessionStateFusion
{
    private bool? _effectiveLocked;
    private int _unlockedConfirmationCount;

    public WindowsLifecycleEventKind? Observe(
        bool? sessionLocked,
        bool? secureScreenSaverRunning,
        bool? forcedLocked = null,
        bool explicitUnlock = false)
    {
        bool? effective = forcedLocked == true
            || sessionLocked == true
            || secureScreenSaverRunning == true
                ? true
                : sessionLocked == false && secureScreenSaverRunning == false
                    ? false
                    : forcedLocked;

        if (effective == true)
        {
            _unlockedConfirmationCount = 0;
            if (_effectiveLocked != true)
            {
                _effectiveLocked = true;
                return WindowsLifecycleEventKind.SessionLocked;
            }

            return null;
        }

        if (effective != false || _effectiveLocked != true)
        {
            if (_effectiveLocked is null && effective == false)
            {
                _effectiveLocked = false;
            }

            return null;
        }

        _unlockedConfirmationCount = explicitUnlock
            ? 2
            : _unlockedConfirmationCount + 1;
        if (_unlockedConfirmationCount < 2)
        {
            return null;
        }

        _effectiveLocked = false;
        _unlockedConfirmationCount = 0;
        return WindowsLifecycleEventKind.SessionUnlocked;
    }
}
