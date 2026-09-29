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

namespace Gurux.Updater.Services;

/// <summary>
/// Defines operations for starting, stopping, and querying a service.
/// </summary>
public interface IGXServiceManager
{
    /// <summary>
    /// Stops the named service and waits for it to stop.
    /// </summary>
    Task StopAsync(string serviceName, CancellationToken cancellationToken = default);
    /// <summary>
    /// Starts the named service and waits for it to run.
    /// </summary>
    Task StartAsync(string serviceName, CancellationToken cancellationToken = default);
    /// <summary>
    /// Checks whether the named service is currently running.
    /// </summary>
    Task<bool> IsRunningAsync(string serviceName, CancellationToken cancellationToken = default);
}
