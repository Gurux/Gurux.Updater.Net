//
// --------------------------------------------------------------------------
//  Gurux Ltd
// 
//
//
// Filename:        $HeadURL$
//
// Version:         $Revision$,
//                  $Date$
//                  $Author$
//
// Copyright (c) Gurux Ltd
//
//---------------------------------------------------------------------------
//
//  DESCRIPTION
//
// This file is a part of Gurux Device Framework.
//
// Gurux Device Framework is Open Source software; you can redistribute it
// and/or modify it under the terms of the GNU General Public License 
// as published by the Free Software Foundation; version 2 of the License.
// Gurux Device Framework is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of 
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. 
// See the GNU General Public License for more details.
//
// This code is licensed under the GNU General Public License v2. 
// Full text may be retrieved at http://www.gnu.org/licenses/gpl-2.0.txt
//---------------------------------------------------------------------------

using Gurux.Updater.Model;

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
        /// <summary>
        /// The last successful result, or null before the first successful check.
        /// </summary>
        public GXUpdateInfo? Update { get; init; }

        /// <summary>
        /// UTC time of the last successful check.
        /// </summary>
        public DateTimeOffset? CheckedAt { get; init; }

        /// <summary>
        /// The latest error, cleared after success. Cancellation is not an error.
        /// </summary>
        public Exception? Error { get; init; }
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
            if (!ReferenceEquals(observed, Status)) return Status;
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
                next = observed with { Error = ex };
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
            throw new InvalidOperationException("This update monitor is already running.");
        try
        {
            using var timer = new PeriodicTimer(_interval);
            do { await CheckAsync(cancellationToken).ConfigureAwait(false); }
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
