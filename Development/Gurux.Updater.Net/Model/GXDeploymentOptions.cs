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

/// <summary>Trusted, server-owned configuration for one deployment.</summary>
public sealed class GXDeploymentOptions
{
    /// <summary>Installation method: static (default) or compose.</summary>
    public string Mode { get; set; } = "static";
    /// <summary>Host-only environment variable holding the HMAC request authentication key.</summary>
    public string? RequestKeyEnvironmentVariable { get; set; }
    /// <summary>Server-owned Compose project configuration.</summary>
    public GXComposeOptions? Compose { get; set; }
    /// <summary>Time allowed for AMI to finish sending Accepted before an unready request expires.</summary>
    public int RequestReadyTimeoutSeconds { get; set; } = 120;
    /// <summary>Absolute installation root containing releases, current, queue and journal.</summary>
    public string Root { get; set; } = "";
    /// <summary>Trusted catalog URL, owned by the server administrator.</summary>
    public string CatalogUrl { get; set; } = "";
    /// <summary>Catalog product identifying exactly one deployable ZIP per release.</summary>
    public string ProductId { get; set; } = "Gurux.DLMS.AMI";
    /// <summary>Public Apache/nginx base URL used for post-activation checks.</summary>
    public string PublicUrl { get; set; } = "";
    /// <summary>Full AMI publication (backend plus wwwroot), otherwise a standalone wwwroot ZIP.</summary>
    public bool IncludeBackend { get; set; }
    /// <summary>Backend systemd unit; ignored in WASM-only mode.</summary>
    public string ServiceName { get; set; } = "gurux-ami.service";
    /// <summary>Published backend DLL basename.</summary>
    public string BackendAssembly { get; set; } = "Gurux.DLMS.AMI.dll";
    /// <summary>Public plain-text API version endpoint.</summary>
    public string? ApiVersionUrl { get; set; }
    /// <summary>Current API version used for standalone client compatibility checks.</summary>
    public string? ApiVersion { get; set; }
    /// <summary>Maximum post-activation HTTP verification duration.</summary>
    public int CheckTimeoutSeconds { get; set; } = 120;
    /// <summary>Maximum compressed download size.</summary>
    public long MaxPackageBytes { get; set; } = 1024L * 1024 * 1024;
    /// <summary>Maximum total extracted size.</summary>
    public long MaxExtractedBytes { get; set; } = 4L * 1024 * 1024 * 1024;
    /// <summary>Name of an environment variable containing a private GitHub token; never a browser setting.</summary>
    public string? GitHubTokenEnvironmentVariable { get; set; }
}

/// <summary>Durable transaction journal and browser-visible outcome.</summary>
public sealed class GXDeploymentStatus
{
    /// <summary>Compose recovery journal; contains host-owned paths and immutable image references.</summary>
    public GXComposeTransaction? Compose { get; set; }
    /// <summary>Internal transaction identifier, distinguishing retries of one version.</summary>
    public string? RequestId { get; set; }
    /// <summary>Requested catalog version.</summary>
    public string Version { get; set; } = "";
    /// <summary>Current transaction state or its terminal outcome.</summary>
    public string State { get; set; } = "Idle";
    /// <summary>Previous current-link target retained for recovery.</summary>
    public string? PreviousTarget { get; set; }
    /// <summary>Last failure description, if any.</summary>
    public string? Error { get; set; }
    /// <summary>Last durable state change in UTC.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Only a catalog version crosses the application-to-updater boundary.</summary>
public sealed record GXDeploymentRequest(string Version, string? RequestId = null, string? Signature = null);
