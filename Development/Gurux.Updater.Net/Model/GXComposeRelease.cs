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

/// <summary>Immutable images and publisher-audited database rollback policy for one AMI release.</summary>
public sealed class GXComposeRelease
{
    /// <summary>Publisher release channel.</summary>
    [JsonPropertyName("channel")]
    public string Channel { get; set; } = "stable";
    /// <summary>Supported Docker platform identifiers.</summary>
    [JsonPropertyName("platforms")]
    public List<string> Platforms { get; set; } = [];
    /// <summary>Logical service roles mapped to immutable registry digest references.</summary>
    [JsonPropertyName("images")]
    public Dictionary<string, string> Images { get; set; } = [];
    /// <summary>Whether the public WASM publication must also be checked.</summary>
    [JsonPropertyName("containsWasm")]
    public bool ContainsWasm { get; set; } = true;
    /// <summary>Audited installed-version range whose images can read the database after this release starts.</summary>
    [JsonPropertyName("rollbackCompatibleFrom")]
    public string? RollbackCompatibleFrom { get; set; }
    /// <summary>Requires a separate database-aware maintenance procedure.</summary>
    [JsonPropertyName("maintenanceRequired")]
    public bool MaintenanceRequired { get; set; } = true;
}

/// <summary>Host-owned Compose project and allowed logical-role to service mapping.</summary>
public sealed class GXComposeOptions
{
    /// <summary>Fixed existing Compose project name.</summary>
    public string ProjectName { get; set; } = "gurux-ami";
    /// <summary>Absolute project directory used to resolve installation configuration.</summary>
    public string ProjectDirectory { get; set; } = "";
    /// <summary>Absolute administrator-owned Compose files.</summary>
    public List<string> Files { get; set; } = [];
    /// <summary>Allowed logical roles mapped to fixed Compose service names.</summary>
    public Dictionary<string, string> Services { get; set; } = new() { ["ami"] = "ami" };
    /// <summary>Channel selected by the installation administrator.</summary>
    public string Channel { get; set; } = "stable";
    /// <summary>Docker platform selected by the installation administrator.</summary>
    public string Platform { get; set; } = "linux/amd64";
    /// <summary>Permitted registry/repository names without tags or digest suffixes.</summary>
    public List<string> AllowedImageRepositories { get; set; } = ["guruxorg/dlms-ami"];
    /// <summary>Logical role serving the static client.</summary>
    public string WasmRole { get; set; } = "ami";
    /// <summary>Absolute public-files directory inside the selected container.</summary>
    public string WasmPath { get; set; } = "/app/wwwroot";
    /// <summary>Maximum duration of one Docker CLI command.</summary>
    public int CommandTimeoutSeconds { get; set; } = 600;
    /// <summary>Additional recent terminal transactions retained besides current and last successful deployment.</summary>
    public int KeepTransactions { get; set; } = 3;
}

/// <summary>Recovery information persisted before any Compose service is recreated.</summary>
public sealed class GXComposeTransaction
{
    /// <summary>Private saved effective Compose configuration.</summary>
    public string SnapshotFile { get; set; } = "";
    /// <summary>SHA-256 of the saved configuration.</summary>
    public string SnapshotSha256 { get; set; } = "";
    /// <summary>Public API version observed before replacement.</summary>
    public string PreviousVersion { get; set; } = "";
    /// <summary>Public client marker observed before replacement.</summary>
    public string? PreviousWasmVersion { get; set; }
    /// <summary>Local backup tags by Compose service.</summary>
    public Dictionary<string, string> PreviousImages { get; set; } = [];
    /// <summary>Requested immutable references by Compose service.</summary>
    public Dictionary<string, string> NewImages { get; set; } = [];
    /// <summary>Actual previous local image IDs by service.</summary>
    public Dictionary<string, string> PreviousImageIds { get; set; } = [];
    /// <summary>Verified pulled local image IDs by service.</summary>
    public Dictionary<string, string> NewImageIds { get; set; } = [];
    /// <summary>Whether both forward and recovery checks include client startup resources.</summary>
    public bool ContainsWasm { get; set; }
}

/// <summary>The requested release requires a database-aware maintenance procedure.</summary>
public sealed class GXMaintenanceUpdateRequiredException(string message) : InvalidOperationException(message);
