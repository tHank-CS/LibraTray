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
        ValidateColorTemperature(colorTemperature, nameof(colorTemperature));
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
        ValidateColorTemperature(colorTemperature, nameof(colorTemperature));
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
        if (before.MainPower != after.MainPower)
        {
            throw new LibraProStateVerificationException(
                "A background operation unexpectedly changed main_power.");
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

    private static void ValidateColorTemperature(int value, string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, 3_000, parameterName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 6_500, parameterName);
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);
}
