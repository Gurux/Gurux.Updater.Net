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

namespace Gurux.Updater.Model;

/// <summary>Immutable images and publisher-audited database rollback policy for one AMI release.</summary>
public sealed class GXComposeRelease
{
    /// <summary>Publisher release channel.</summary>
    public string Channel { get; set; } = "stable";
    /// <summary>Supported Docker platform identifiers.</summary>
    public List<string> Platforms { get; set; } = [];
    /// <summary>Logical service roles mapped to immutable registry digest references.</summary>
    public Dictionary<string, string> Images { get; set; } = [];
    /// <summary>Whether the public WASM publication must also be checked.</summary>
    public bool ContainsWasm { get; set; } = true;
    /// <summary>Audited installed-version range whose images can read the database after this release starts.</summary>
    public string? RollbackCompatibleFrom { get; set; }
    /// <summary>Requires a separate database-aware maintenance procedure.</summary>
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
