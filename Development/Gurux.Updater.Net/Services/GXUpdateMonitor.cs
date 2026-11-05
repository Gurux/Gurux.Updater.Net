// --------------------------------------------------------------------------
// Gurux Ltd
//
// Copyright (c) Gurux Ltd.
//
// This file is part of Gurux Device Framework.
//
// Commit: $Format:%H$
// Date:   $Format:%cI$
//
// This software is available under either of the following licenses:
//
// 1. GNU General Public License, version 2 only (GPL-2.0-only).
//    See the LICENSE file included with this distribution.
//
// 2. A commercial license granted by Gurux Ltd under a separate written
//    agreement. Use under this option requires a valid commercial license
//    and is governed by the terms of that agreement.
//
// If you have not obtained a commercial license from Gurux Ltd,
// use of this software is governed by GPL-2.0-only.
//
// Commercial licensing: https://www.gurux.fi/
//
// SPDX-License-Identifier: GPL-2.0-only OR LicenseRef-Gurux-Commercial
// --------------------------------------------------------------------------

using Gurux.Updater.Model;
using Gurux.Updater.Enums;

namespace Gurux.Updater.Services;

/// <summary>
/// Monitors one update target independently of an application host or UI.
/// </summary>
public sealed class GXUpdateMonitor
{
    /// <summary>
    /// A snapshot retaining the last successful result when a later check fails.
    /// </summary>
    public sealed record State
    {
        /// <summary>The latest outcome; stale successful data does not mask failures.</summary>
        public GXUpdateStatus Status => Error is not null ? GXUpdateStatus.CheckFailed : Update?.Status ?? GXUpdateStatus.Unknown;

        /// <summary>
        /// The last successful result, or null before the first successful check.
        /// </summary>
        public GXUpdateInfo? Update
        {
            get; init;
        }

        /// <summary>
        /// UTC time of the last successful check.
        /// </summary>
        public DateTimeOffset? CheckedAt
        {
            get; init;
        }

        /// <summary>
        /// The latest error, cleared after success. Cancellation is not an error.
        /// </summary>
        public Exception? Error
        {
            get; init;
        }
    }

    private readonly GXGitHubUpdateService _updater;
    private readonly GXUpdateTarget _target;
    private readonly TimeSpan _interval;
    private readonly SemaphoreSlim _checkLock = new(1, 1);
    private State _status = new();
    private int _running;

    /// <summary>
    /// Creates a monitor. The caller owns the updater's HttpClient and configures its
    /// User-Agent and timeout. Do not modify the target while the monitor is in use.
    /// </summary>
    public GXUpdateMonitor(GXGitHubUpdateService updater, GXUpdateTarget target, TimeSpan interval)
    {
        ArgumentNullException.ThrowIfNull(updater);
        ArgumentNullException.ThrowIfNull(target);
        if (interval <= TimeSpan.Zero || interval.TotalMilliseconds > uint.MaxValue - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(interval));
        }
        _updater = updater;
        _target = target;
        _interval = interval;
    }

    /// <summary>Reads the cached snapshot without contacting the release server.</summary>
    public State Status => Volatile.Read(ref _status);

    /// <summary>
    /// Raised after each completed check on the checking thread. Handlers must not throw;
    /// UI consumers must marshal to their UI thread. Read Status for the latest snapshot
    /// if concurrent notifications arrive out of order.
    /// </summary>
    public event Action<State>? StatusChanged;

    /// <summary>Checks now. Overlapping checks share the result; no files are downloaded.</summary>
    public async Task<State> CheckAsync(CancellationToken cancellationToken = default)
    {
        var observed = Status;
        State next;
        await _checkLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!ReferenceEquals(observed, Status))
            {
                return Status;
            }

            try
            {
                var update = await _updater.CheckAsync(_target, cancellationToken).ConfigureAwait(false);
                next = new State { Update = update, CheckedAt = DateTimeOffset.UtcNow };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                next = observed with
                {
                    Error = ex
                };
            }
            Volatile.Write(ref _status, next);
        }
        finally
        {
            _checkLock.Release();
        }
        StatusChanged?.Invoke(next);
        return next;
    }

    /// <summary>
    /// Checks immediately and repeats until cancelled. Cancellation ends normally.
    /// Only one polling loop may run per monitor; manual checks may run alongside it.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            throw new InvalidOperationException("This update monitor is already running.");
        }

        try
        {
            using var timer = new PeriodicTimer(_interval);
            do
            {
                await CheckAsync(cancellationToken).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }
}
