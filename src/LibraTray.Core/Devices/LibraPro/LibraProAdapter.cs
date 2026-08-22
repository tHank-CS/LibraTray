using System.Globalization;
using LibraTray.Core.Identity;
using LibraTray.Core.Networking;
using LibraTray.Core.Protocol;

namespace LibraTray.Core.Devices.LibraPro;

/// <summary>
/// Product-specific command and reconciliation policy for the stock Yeelight
/// Libra Pro protocol identity.
/// </summary>
public sealed class LibraProAdapter : IDisposable
{
    private static readonly string[] StateProperties =
    [
        "power",
        "main_power",
        "bg_power",
        "bright",
        "ct",
        "bg_bright",
        "bg_ct",
        "bg_rgb",
        "bg_hue",
        "bg_sat",
        "bg_lmode",
    ];

    private readonly IYeelightCommandTransport _transport;
    private readonly HashSet<string> _capabilities;
    private readonly LibraProAdapterOptions _options;
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private readonly object _epochLock = new();

    private long _connectionEpoch;
    private long? _recoveryAttemptedEpoch;
    private bool _disposed;

    public LibraProAdapter(
        IYeelightCommandTransport transport,
        string internalModel,
        IEnumerable<string> advertisedCapabilities,
        LibraProAdapterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentException.ThrowIfNullOrWhiteSpace(internalModel);
        ArgumentNullException.ThrowIfNull(advertisedCapabilities);

        if (!ProductIdentityMapper.IsSupportedInternalModel(internalModel))
        {
            throw new ArgumentException(
                "The Libra Pro adapter requires the exact lamp15 protocol identity.",
                nameof(internalModel));
        }

        _transport = transport;
        _capabilities = advertisedCapabilities.ToHashSet(StringComparer.Ordinal);
        _options = options ?? new LibraProAdapterOptions();
    }

    public long ConnectionEpoch
    {
        get
        {
            lock (_epochLock)
            {
                return _connectionEpoch;
            }
        }
    }

    public bool Supports(string method)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        return _capabilities.Contains(method);
    }

    /// <summary>
    /// Starts a new logical connection epoch. A bounded recovery may be
    /// attempted once in each epoch.
    /// </summary>
    public void BeginConnectionEpoch(long connectionEpoch)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(connectionEpoch);

        lock (_epochLock)
        {
            if (connectionEpoch < _connectionEpoch)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(connectionEpoch),
                    connectionEpoch,
                    "Connection epochs cannot move backwards.");
            }

            if (connectionEpoch != _connectionEpoch)
            {
                _connectionEpoch = connectionEpoch;
                _recoveryAttemptedEpoch = null;
            }
        }
    }

    public async Task<LibraProState> QueryStateAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureCapability("get_prop");
        IReadOnlyList<string> results = await _transport
            .SendAsync("get_prop", StateProperties, timeout, cancellationToken)
            .ConfigureAwait(false);

        return ParseState(results);
    }

    /// <summary>
    /// Changes background power and verifies the readable postcondition. If an
    /// on command is acknowledged but does not take effect, firmware recovery
    /// is attempted at most once in the current connection epoch.
    /// </summary>
    public async Task<LibraProCommandResult> SetBackgroundPowerAsync(
        bool enabled,
        LibraProBackgroundSnapshot? restoreSnapshot,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            LibraProState before = await QueryStateAsync(timeout, cancellationToken)
                .ConfigureAwait(false);
            await SendOkAsync(
                    "bg_set_power",
                    [enabled ? "on" : "off", "sudden", 0],
                    timeout,
                    cancellationToken)
                .ConfigureAwait(false);
            LibraProState after = await QueryStateAsync(timeout, cancellationToken)
                .ConfigureAwait(false);

            if (after.BackgroundPower == enabled)
            {
                EnsureMainStatePreserved(before, after);
                return new LibraProCommandResult(
                    after,
                    RecoveryAttempted: false,
                    ConnectionEpoch);
            }

            if (!enabled
                || _options.ColdStartRecoveryMode
                    != LibraProColdStartRecoveryMode.OnVerifiedFailure
                || !TryClaimRecovery())
            {
                throw CreatePowerMismatch(enabled, after);
            }

            LibraProBackgroundSnapshot snapshot = restoreSnapshot
                ?? new LibraProBackgroundSnapshot(
                    before.BackgroundBrightness,
                    before.BackgroundRgb);
            LibraProState recovered = await RecoverBackgroundCoreAsync(
                    before,
                    snapshot,
                    desiredPower: true,
                    timeout,
                    cancellationToken)
                .ConfigureAwait(false);

            return new LibraProCommandResult(
                recovered,
                RecoveryAttempted: true,
                ConnectionEpoch);
        }
        finally
        {
            _commandLock.Release();
        }
    }

    /// <summary>
    /// Changes the primary light power and verifies <c>main_power</c>.
    /// Firmware or vendor-app coupling may also change background power, so
    /// the returned complete state is authoritative.
    /// </summary>
    public Task<LibraProCommandResult> SetMainPowerAsync(
        bool enabled,
        TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        ExecuteVerifiedAsync(
            "set_power",
            [enabled ? "on" : "off", "sudden", 0],
            state => state.MainPower == enabled,
            $"Main power verification failed: expected {FormatPower(enabled)}.",
            timeout,
            cancellationToken);

    public Task<LibraProCommandResult> SetMainBrightnessAsync(
        int brightness,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ValidateBrightness(brightness, nameof(brightness));
        return ExecuteVerifiedAsync(
            "set_bright",
            [brightness, "sudden", 0],
            state => state.MainBrightness == brightness,
            $"Main brightness verification failed: expected {brightness}.",
            timeout,
            cancellationToken);
    }

    public Task<LibraProCommandResult> SetMainColorTemperatureAsync(
        int colorTemperature,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ValidateMainColorTemperature(colorTemperature, nameof(colorTemperature));
        return ExecuteVerifiedAsync(
            "set_ct_abx",
            [colorTemperature, "sudden", 0],
            state => state.MainColorTemperature == colorTemperature,
            $"Main colour-temperature verification failed: expected {colorTemperature}.",
            timeout,
            cancellationToken);
    }

    public Task<LibraProCommandResult> SetBackgroundBrightnessAsync(
        int brightness,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ValidateBrightness(brightness, nameof(brightness));
        return ExecuteVerifiedAsync(
            "bg_set_bright",
            [brightness, "sudden", 0],
            state => state.BackgroundBrightness == brightness,
            $"Background brightness verification failed: expected {brightness}.",
            timeout,
            cancellationToken);
    }

    public Task<LibraProCommandResult> SetBackgroundColorTemperatureAsync(
        int colorTemperature,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ValidateBackgroundColorTemperature(
            colorTemperature,
            nameof(colorTemperature));
        return ExecuteVerifiedAsync(
            "bg_set_ct_abx",
            [colorTemperature, "sudden", 0],
            state => state.BackgroundColorTemperature == colorTemperature,
            $"Background colour-temperature verification failed: expected {colorTemperature}.",
            timeout,
            cancellationToken);
    }

    public Task<LibraProCommandResult> SetBackgroundRgbAsync(
        int rgb,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rgb);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rgb, 16_777_215);
        return ExecuteVerifiedAsync(
            "bg_set_rgb",
            [rgb, "sudden", 0],
            state => state.BackgroundRgb == rgb,
            $"Background RGB verification failed: expected {rgb}.",
            timeout,
            cancellationToken);
    }

    public async Task<SegmentRgbApplyResult> SetSegmentRgbAsync(
        SegmentRgbRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();
        await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            LibraProState before = await QueryStateAsync(timeout, cancellationToken)
                .ConfigureAwait(false);
            await SendOkAsync(
                    "set_segment_rgb",
                    [request.LeftRgb, request.RightRgb],
                    timeout,
                    cancellationToken)
                .ConfigureAwait(false);
            LibraProState after = await QueryStateAsync(timeout, cancellationToken)
                .ConfigureAwait(false);
            EnsureReadableStatePreserved(before, after, "segment RGB");
            return new SegmentRgbApplyResult(
                after,
                request,
                SegmentRgbApplyStatus.AcceptedUnverified,
                ConnectionEpoch);
        }
        finally
        {
            _commandLock.Release();
        }
    }

    public async Task<LibraProCommandResult> ApplyPowerStateAsync(
        bool mainPower,
        bool backgroundPower,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            LibraProState before = await QueryStateAsync(timeout, cancellationToken)
                .ConfigureAwait(false);
            if (before.MainPower != mainPower)
            {
                await SendOkAsync(
                        "set_power",
                        [mainPower ? "on" : "off", "sudden", 0],
                        timeout,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            LibraProState intermediate = await QueryStateAsync(timeout, cancellationToken)
                .ConfigureAwait(false);
            if (intermediate.BackgroundPower != backgroundPower)
            {
                await SendOkAsync(
                        "bg_set_power",
                        [backgroundPower ? "on" : "off", "sudden", 0],
                        timeout,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            LibraProState after = await QueryStateAsync(timeout, cancellationToken)
                .ConfigureAwait(false);
            if (after.MainPower != mainPower || after.BackgroundPower != backgroundPower)
            {
                throw new LibraProStateVerificationException(
                    "Power-only target verification failed.");
            }

            EnsureAppearancePreserved(before, after);
            return new LibraProCommandResult(after, false, ConnectionEpoch);
        }
        finally
        {
            _commandLock.Release();
        }
    }

    /// <summary>
    /// Restores a lifecycle snapshot without replaying the complete preset
    /// sequence. Main appearance is never rewritten. Background appearance is
    /// applied once after power-on so a device-side on-template cannot remain
    /// visible, while segmented requests remain explicitly unconfirmed.
    /// </summary>
    public async Task<LibraProCommandResult> RestoreLifecycleTargetStateAsync(
        LibraProTargetState target,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ThrowIfDisposed();
        await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await ApplyBoundedTargetStateCoreAsync(
                    target,
                    writeMainAppearance: false,
                    writeBackgroundAppearanceWhenOff: false,
                    timeout,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _commandLock.Release();
        }
    }

    /// <summary>
    /// Applies one complete target without retrying the multi-command sequence.
    /// This is used by presets and startup restoration so a verification
    /// mismatch cannot oscillate a segmented background through repeated power
    /// and appearance writes.
    /// </summary>
    public async Task<LibraProCommandResult> ApplyTargetStateOnceAsync(
        LibraProTargetState target,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ThrowIfDisposed();
        await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await ApplyBoundedTargetStateCoreAsync(
                    target,
                    writeMainAppearance: true,
                    writeBackgroundAppearanceWhenOff: true,
                    timeout,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _commandLock.Release();
        }
    }

    public async Task<LibraProCommandResult> ApplyTargetStateAsync(
        LibraProTargetState target,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ThrowIfDisposed();
        await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await SendOkAsync(
                    "set_bright",
                    [target.MainBrightness, "sudden", 0],
                    timeout,
                    cancellationToken)
                .ConfigureAwait(false);
            await SendOkAsync(
                    "set_ct_abx",
                    [target.MainColorTemperature, "sudden", 0],
                    timeout,
                    cancellationToken)
                .ConfigureAwait(false);
            await SendOkAsync(
                    "bg_set_bright",
                    [target.BackgroundBrightness, "sudden", 0],
                    timeout,
                    cancellationToken)
                .ConfigureAwait(false);
            if (target.AmbientColorMode == AmbientColorMode.Segmented)
            {
                SegmentRgbRequest segment = target.SegmentRgb
                    ?? throw new ArgumentException(
                        "The segmented target is incomplete.",
                        nameof(target));
                await SendOkAsync(
                        "set_segment_rgb",
                        [segment.LeftRgb, segment.RightRgb],
                        timeout,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await SendOkAsync(
                        "bg_set_rgb",
                        [target.BackgroundRgb, "sudden", 0],
                        timeout,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            await SendOkAsync(
                    "set_power",
                    [target.MainPower ? "on" : "off", "sudden", 0],
                    timeout,
                    cancellationToken)
                .ConfigureAwait(false);
            await SendOkAsync(
                    "bg_set_power",
                    [target.BackgroundPower ? "on" : "off", "sudden", 0],
                    timeout,
                    cancellationToken)
                .ConfigureAwait(false);

            LibraProState state = await QueryStateAsync(
                    timeout,
                    cancellationToken)
                .ConfigureAwait(false);
            if (MatchesTarget(state, target))
            {
                return new LibraProCommandResult(
                    state,
                    RecoveryAttempted: false,
                    ConnectionEpoch);
            }

            if (target.BackgroundPower
                && state.BackgroundPower != target.BackgroundPower
                && _options.ColdStartRecoveryMode
                    == LibraProColdStartRecoveryMode.OnVerifiedFailure
                && TryClaimRecovery())
            {
                state = await RecoverBackgroundCoreAsync(
                        state,
                        new LibraProBackgroundSnapshot(
                            target.BackgroundBrightness,
                            target.BackgroundRgb),
                        desiredPower: true,
                        timeout,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (target.AmbientColorMode == AmbientColorMode.Segmented
                    && target.SegmentRgb is { } recoveredSegment)
                {
                    await SendOkAsync(
                            "set_segment_rgb",
                            [recoveredSegment.LeftRgb, recoveredSegment.RightRgb],
                            timeout,
                            cancellationToken)
                        .ConfigureAwait(false);
                    state = await QueryStateAsync(timeout, cancellationToken)
                        .ConfigureAwait(false);
                }

                if (MatchesTarget(state, target))
                {
                    return new LibraProCommandResult(
                        state,
                        RecoveryAttempted: true,
                        ConnectionEpoch);
                }
            }

            throw new LibraProStateVerificationException(
                "Preset verification failed after reading the complete device state.");
        }
        finally
        {
            _commandLock.Release();
        }
    }

    /// <summary>
    /// Explicitly initializes the background renderer and restores the
    /// requested power state. This may create a brief visible flash when the
    /// requested state is off, so normal reconnect handling should prefer
    /// failure-triggered recovery.
    /// </summary>
    public async Task<LibraProCommandResult> RecoverBackgroundAsync(
        LibraProBackgroundSnapshot restoreSnapshot,
        bool desiredPower,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(restoreSnapshot);
        ThrowIfDisposed();
        await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (!TryClaimRecovery())
            {
                throw new LibraProStateVerificationException(
                    "Background recovery was already attempted in this connection epoch.");
            }

            LibraProState before = await QueryStateAsync(timeout, cancellationToken)
                .ConfigureAwait(false);
            LibraProState recovered = await RecoverBackgroundCoreAsync(
                    before,
                    restoreSnapshot,
                    desiredPower,
                    timeout,
                    cancellationToken)
                .ConfigureAwait(false);

            return new LibraProCommandResult(
                recovered,
                RecoveryAttempted: true,
                ConnectionEpoch);
        }
        finally
        {
            _commandLock.Release();
        }
    }

    /// <summary>
    /// Runs an explicit, user-requested renderer initialization at minimum
    /// brightness, then restores the readable appearance and optional segment
    /// request. Unlike automatic recovery, each invocation is user bounded.
    /// </summary>
    public async Task<LibraProCommandResult> RunBackgroundPostAsync(
        LibraProBackgroundSnapshot restoreSnapshot,
        bool desiredPower,
        SegmentRgbRequest? segmentRequest,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(restoreSnapshot);
        ThrowIfDisposed();
        await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            LibraProState before = await QueryStateAsync(timeout, cancellationToken)
                .ConfigureAwait(false);
            await SendOkAsync(
                    "bg_set_scene",
                    ["color", restoreSnapshot.Rgb, 1],
                    timeout,
                    cancellationToken)
                .ConfigureAwait(false);
            await SendOkAsync(
                    "bg_set_bright",
                    [restoreSnapshot.Brightness, "sudden", 0],
                    timeout,
                    cancellationToken)
                .ConfigureAwait(false);
            if (segmentRequest is null)
            {
                await SendOkAsync(
                        "bg_set_rgb",
                        [restoreSnapshot.Rgb, "sudden", 0],
                        timeout,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await SendOkAsync(
                        "set_segment_rgb",
                        [segmentRequest.LeftRgb, segmentRequest.RightRgb],
                        timeout,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            await SendOkAsync(
                    "bg_set_power",
                    [desiredPower ? "on" : "off", "sudden", 0],
                    timeout,
                    cancellationToken)
                .ConfigureAwait(false);
            LibraProState after = await QueryStateAsync(timeout, cancellationToken)
                .ConfigureAwait(false);
            EnsureMainStatePreserved(before, after);
            if (after.BackgroundPower != desiredPower
                || after.BackgroundBrightness != restoreSnapshot.Brightness
                || (segmentRequest is null && after.BackgroundRgb != restoreSnapshot.Rgb))
            {
                throw new LibraProStateVerificationException(
                    "Background POST did not restore the requested readable state.");
            }

            return new LibraProCommandResult(after, true, ConnectionEpoch);
        }
        finally
        {
            _commandLock.Release();
        }
    }

    internal static LibraProState ParseState(IReadOnlyList<string> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        if (results.Count != StateProperties.Length)
        {
            throw new YeelightProtocolException(
                $"Expected {StateProperties.Length.ToString(CultureInfo.InvariantCulture)} "
                + $"Libra Pro state values, received "
                + $"{results.Count.ToString(CultureInfo.InvariantCulture)}.");
        }

        bool aggregate = ParsePower(results[0], "power");
        bool main = ParsePower(results[1], "main_power");
        bool background = ParsePower(results[2], "bg_power");
        var state = new LibraProState(
            aggregate,
            main,
            background,
            ParseInteger(results[3], "bright", 1, 100),
            ParseInteger(results[4], "ct", 1_700, 6_500),
            ParseInteger(results[5], "bg_bright", 1, 100),
            ParseInteger(results[6], "bg_ct", 1_700, 6_500),
            ParseInteger(results[7], "bg_rgb", 0, 16_777_215),
            ParseInteger(results[8], "bg_hue", 0, 359),
            ParseInteger(results[9], "bg_sat", 0, 100),
            ParseInteger(results[10], "bg_lmode", 0, int.MaxValue));

        if (state.AggregatePower != (state.MainPower || state.BackgroundPower))
        {
            throw new YeelightProtocolException(
                "The aggregate power value conflicts with main_power and bg_power.");
        }

        return state;
    }

    public void Dispose()
    {
        lock (_epochLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _commandLock.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<LibraProState> RecoverBackgroundCoreAsync(
        LibraProState before,
        LibraProBackgroundSnapshot snapshot,
        bool desiredPower,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        await SendOkAsync(
                "bg_set_scene",
                ["color", snapshot.Rgb, snapshot.Brightness],
                timeout,
                cancellationToken)
            .ConfigureAwait(false);
        LibraProState initialized = await QueryStateAsync(timeout, cancellationToken)
            .ConfigureAwait(false);
        EnsureMainStatePreserved(before, initialized);

        if (!initialized.BackgroundPower)
        {
            throw new LibraProStateVerificationException(
                "Background scene refresh was acknowledged but bg_power remained off.");
        }

        if (!desiredPower)
        {
            await SendOkAsync(
                    "bg_set_power",
                    ["off", "sudden", 0],
                    timeout,
                    cancellationToken)
                .ConfigureAwait(false);
            initialized = await QueryStateAsync(timeout, cancellationToken)
                .ConfigureAwait(false);
            EnsureMainStatePreserved(before, initialized);
        }

        if (initialized.BackgroundPower != desiredPower)
        {
            throw CreatePowerMismatch(desiredPower, initialized);
        }

        if (initialized.BackgroundBrightness != snapshot.Brightness
            || initialized.BackgroundRgb != snapshot.Rgb)
        {
            throw new LibraProStateVerificationException(
                "Background recovery did not restore the requested brightness and RGB state.");
        }

        return initialized;
    }

    private async Task SendOkAsync(
        string method,
        IReadOnlyList<object?> parameters,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        EnsureCapability(method);
        IReadOnlyList<string> results = await _transport
            .SendAsync(method, parameters, timeout, cancellationToken)
            .ConfigureAwait(false);

        if (results.Count != 1
            || !string.Equals(results[0], "ok", StringComparison.Ordinal))
        {
            throw new YeelightProtocolException(
                $"{method} returned an unexpected result.");
        }
    }

    private async Task<LibraProCommandResult> ExecuteVerifiedAsync(
        string method,
        IReadOnlyList<object?> parameters,
        Func<LibraProState, bool> postcondition,
        string failureMessage,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await SendOkAsync(
                    method,
                    parameters,
                    timeout,
                    cancellationToken)
                .ConfigureAwait(false);
            LibraProState state = await QueryStateAsync(timeout, cancellationToken)
                .ConfigureAwait(false);

            if (!postcondition(state))
            {
                throw new LibraProStateVerificationException(failureMessage);
            }

            return new LibraProCommandResult(
                state,
                RecoveryAttempted: false,
                ConnectionEpoch);
        }
        finally
        {
            _commandLock.Release();
        }
    }

    private bool TryClaimRecovery()
    {
        lock (_epochLock)
        {
            if (_recoveryAttemptedEpoch == _connectionEpoch)
            {
                return false;
            }

            _recoveryAttemptedEpoch = _connectionEpoch;
            return true;
        }
    }

    private static bool MatchesTarget(
        LibraProState state,
        LibraProTargetState target) =>
        state.MainPower == target.MainPower
        && state.MainBrightness == target.MainBrightness
        && state.MainColorTemperature == target.MainColorTemperature
        && state.BackgroundPower == target.BackgroundPower
        && state.BackgroundBrightness == target.BackgroundBrightness
        && (target.AmbientColorMode == AmbientColorMode.Segmented
            || state.BackgroundRgb == target.BackgroundRgb);

    private static bool MatchesLifecycleRestore(
        LibraProState state,
        LibraProTargetState target) =>
        state.MainPower == target.MainPower
        && state.MainBrightness == target.MainBrightness
        && state.MainColorTemperature == target.MainColorTemperature
        && state.BackgroundPower == target.BackgroundPower
        && state.BackgroundBrightness == target.BackgroundBrightness
        && (target.AmbientColorMode == AmbientColorMode.Segmented
            || state.BackgroundRgb == target.BackgroundRgb);

    private async Task<LibraProCommandResult> ApplyBoundedTargetStateCoreAsync(
        LibraProTargetState target,
        bool writeMainAppearance,
        bool writeBackgroundAppearanceWhenOff,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        LibraProState before = await QueryStateAsync(timeout, cancellationToken)
            .ConfigureAwait(false);
        if (writeMainAppearance)
        {
            if (before.MainBrightness != target.MainBrightness)
            {
                await SendOkAsync(
                        "set_bright",
                        [target.MainBrightness, "sudden", 0],
                        timeout,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            if (before.MainColorTemperature != target.MainColorTemperature)
            {
                await SendOkAsync(
                        "set_ct_abx",
                        [target.MainColorTemperature, "sudden", 0],
                        timeout,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        if (before.MainPower != target.MainPower)
        {
            await SendOkAsync(
                    "set_power",
                    [target.MainPower ? "on" : "off", "sudden", 0],
                    timeout,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        LibraProState intermediate = await QueryStateAsync(
                timeout,
                cancellationToken)
            .ConfigureAwait(false);
        if (target.BackgroundPower)
        {
            if (!intermediate.BackgroundPower)
            {
                await SendLifecycleBackgroundAppearanceAsync(
                        target,
                        timeout,
                        cancellationToken)
                    .ConfigureAwait(false);
                await SendOkAsync(
                        "bg_set_power",
                        ["on", "sudden", 0],
                        timeout,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            await SendLifecycleBackgroundAppearanceAsync(
                    target,
                    timeout,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            if (intermediate.BackgroundPower)
            {
                await SendOkAsync(
                        "bg_set_power",
                        ["off", "sudden", 0],
                        timeout,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            if (writeBackgroundAppearanceWhenOff)
            {
                await SendLifecycleBackgroundAppearanceAsync(
                        target,
                        timeout,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (target.AmbientColorMode == AmbientColorMode.Segmented)
            {
                await SendSegmentRequestAsync(
                        target.SegmentRgb!,
                        timeout,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        LibraProState after = await QueryStateAsync(timeout, cancellationToken)
            .ConfigureAwait(false);
        if (!MatchesLifecycleRestore(after, target))
        {
            throw new LibraProStateVerificationException(
                "Target verification failed after one bounded apply sequence.");
        }

        return new LibraProCommandResult(after, false, ConnectionEpoch);
    }

    private async Task SendLifecycleBackgroundAppearanceAsync(
        LibraProTargetState target,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        await SendOkAsync(
                "bg_set_bright",
                [target.BackgroundBrightness, "sudden", 0],
                timeout,
                cancellationToken)
            .ConfigureAwait(false);
        if (target.AmbientColorMode == AmbientColorMode.Segmented)
        {
            await SendSegmentRequestAsync(
                    target.SegmentRgb!,
                    timeout,
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await SendOkAsync(
                "bg_set_rgb",
                [target.BackgroundRgb, "sudden", 0],
                timeout,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private Task SendSegmentRequestAsync(
        SegmentRgbRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        SendOkAsync(
            "set_segment_rgb",
            [request.LeftRgb, request.RightRgb],
            timeout,
            cancellationToken);

    private void EnsureCapability(string method)
    {
        if (!_capabilities.Contains(method))
        {
            throw new NotSupportedException(
                $"The device did not advertise the required {method} capability.");
        }
    }

    private static void EnsureMainStatePreserved(
        LibraProState before,
        LibraProState after)
    {
        if (before.MainPower != after.MainPower
            || before.MainBrightness != after.MainBrightness
            || before.MainColorTemperature != after.MainColorTemperature)
        {
            throw new LibraProStateVerificationException(
                "A background operation unexpectedly changed main_power or main appearance.");
        }
    }

    private static void EnsureReadableStatePreserved(
        LibraProState before,
        LibraProState after,
        string operation)
    {
        if (before != after)
        {
            throw new LibraProStateVerificationException(
                $"The {operation} operation unexpectedly changed readable device state.");
        }
    }

    private static void EnsureAppearancePreserved(
        LibraProState before,
        LibraProState after)
    {
        if (before.MainBrightness != after.MainBrightness
            || before.MainColorTemperature != after.MainColorTemperature
            || before.BackgroundBrightness != after.BackgroundBrightness
            || before.BackgroundColorTemperature != after.BackgroundColorTemperature
            || before.BackgroundRgb != after.BackgroundRgb
            || before.BackgroundHue != after.BackgroundHue
            || before.BackgroundSaturation != after.BackgroundSaturation
            || before.BackgroundLightMode != after.BackgroundLightMode)
        {
            throw new LibraProStateVerificationException(
                "A power-only operation unexpectedly changed lamp appearance.");
        }
    }

    private static LibraProStateVerificationException CreatePowerMismatch(
        bool expected,
        LibraProState actual) =>
        new(
            $"Background power verification failed: expected "
            + $"{FormatPower(expected)}, actual {FormatPower(actual.BackgroundPower)}.");

    private static bool ParsePower(string value, string property) =>
        value switch
        {
            "on" => true,
            "off" => false,
            _ => throw new YeelightProtocolException(
                $"{property} must be on or off."),
        };

    private static int ParseInteger(
        string value,
        string property,
        int minimum,
        int maximum)
    {
        if (!int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int parsed)
            || parsed < minimum
            || parsed > maximum)
        {
            throw new YeelightProtocolException(
                $"{property} must be an integer from "
                + $"{minimum.ToString(CultureInfo.InvariantCulture)} to "
                + $"{maximum.ToString(CultureInfo.InvariantCulture)}.");
        }

        return parsed;
    }

    private static string FormatPower(bool value) => value ? "on" : "off";

    private static void ValidateBrightness(int value, string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, 1, parameterName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 100, parameterName);
    }

    private static void ValidateMainColorTemperature(
        int value,
        string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, 2_700, parameterName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 6_500, parameterName);
    }

    private static void ValidateBackgroundColorTemperature(
        int value,
        string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, 3_000, parameterName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 6_500, parameterName);
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);
}
