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

using System.Security.Cryptography;
using System.Text;
using Gurux.Updater.Model;

namespace Gurux.Updater.Services;

/// <summary>Authenticates a version request without giving AMI access to the Docker socket.</summary>
public static class GXDeploymentAuthentication
{
    /// <summary>Signs a canonical transaction ID and version using the installation's shared key.</summary>
    public static GXDeploymentRequest Sign(string version, string requestId, string key) => Sign(version, requestId, key, false);
    /// <summary>Signs the transaction identity and explicit restore intent.</summary>
    public static GXDeploymentRequest Sign(string version, string requestId, string key, bool restorePrevious)
    {
        GXDeploymentQueue.ValidateVersion(version);
        if (!Guid.TryParseExact(requestId, "N", out _))
        {
            throw new ArgumentException("A transaction GUID is required.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        string signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key.Trim()), Encoding.UTF8.GetBytes(requestId + "\n" + version + (restorePrevious ? "\nrestore-previous" : "")))).ToLowerInvariant();
        return new(version, requestId, signature, restorePrevious);
    }
    /// <summary>Rejects altered or unauthenticated requests using a constant-time signature comparison.</summary>
    public static void Verify(GXDeploymentRequest request, string key)
    {
        try
        {
            var expected = Sign(request.Version, request.RequestId ?? "", key, request.RestorePrevious).Signature!;
            if (request.Signature is not { Length: 64 } || !request.Signature.All(Uri.IsHexDigit) ||
                !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expected), Convert.FromHexString(request.Signature)))
            {
                throw new UnauthorizedAccessException("Invalid deployment request signature.");
            }
        }
        catch (ArgumentException) { throw new UnauthorizedAccessException("Invalid deployment request identity."); }
    }
}
