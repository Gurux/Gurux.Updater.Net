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
            throw new ArgumentException("A transaction GUID is required.");
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
                throw new UnauthorizedAccessException("Invalid deployment request signature.");
        }
        catch (ArgumentException) { throw new UnauthorizedAccessException("Invalid deployment request identity."); }
    }
}
