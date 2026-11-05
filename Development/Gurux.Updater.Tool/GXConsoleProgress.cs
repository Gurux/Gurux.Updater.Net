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

namespace Gurux.Updater.Tool;

/// <summary>
/// Writes package download progress to a text writer.
/// </summary>
internal sealed class GXConsoleProgress : IProgress<GXUpdateProgress>
{
    private readonly TextWriter _writer;
    private int _lastLength;

    /// <summary>
    /// Initializes download progress output using the supplied writer or standard output.
    /// </summary>
    public GXConsoleProgress(TextWriter? writer = null)
    {
        _writer = writer ?? Console.Out;
    }

    /// <summary>
    /// Writes the received byte count and, when the total size is known, a percentage and progress bar.
    /// </summary>
    public void Report(GXUpdateProgress value)
    {
        string text;
        if (value.TotalBytes > 0)
        {
            double percentage = (double)value.BytesReceived / value.TotalBytes * 100;
            const int width = 30;
            int completed = Math.Clamp((int)Math.Round(percentage / 100 * width), 0, width);
            string bar = new string('=', completed) + new string(' ', width - completed);
            text = $"{percentage,6:0.0}% [{bar}] {FormatBytes(value.BytesReceived)} / {FormatBytes(value.TotalBytes)}";
        }
        else
        {
            text = $"{FormatBytes(value.BytesReceived)} downloaded";
        }

        int padding = Math.Max(0, _lastLength - text.Length);
        _writer.Write("\\r  " + text + new string(' ', padding));
        _writer.Flush();
        _lastLength = text.Length;
    }

    /// <summary>
    /// Ends the current progress output with a newline and resets the output length.
    /// </summary>
    public void Complete()
    {
        if (_lastLength != 0)
        {
            _writer.WriteLine();
            _writer.Flush();
            _lastLength = 0;
        }
    }

    /// <summary>
    /// Formats a byte count using units from bytes to terabytes and powers of 1024.
    /// </summary>
    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            ++unit;
        }
        return $"{value:0.0} {units[unit]}";
    }
}
