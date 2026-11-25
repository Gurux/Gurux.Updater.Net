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

using System.Text.Json.Serialization;

namespace Gurux.Updater.Model;

/// <summary>Reusable connection settings for a device profile, matching the AMI communication profile shape.</summary>
public sealed class GXCommunicationProfile
{
    /// <summary>Gets the numeric Gurux DLMS interface type.</summary>
    [JsonPropertyName("interfaceType"), JsonRequired]
    public int InterfaceType { get; init; }

    /// <summary>Gets the logical client identifier.</summary>
    [JsonPropertyName("clientAddress"), JsonRequired]
    public int ClientAddress { get; init; }

    /// <summary>Gets the physical server address.</summary>
    [JsonPropertyName("physicalAddress"), JsonRequired]
    public int PhysicalAddress { get; init; }

    /// <summary>Gets the logical server address.</summary>
    [JsonPropertyName("logicalAddress"), JsonRequired]
    public int LogicalAddress { get; init; }
}
