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
