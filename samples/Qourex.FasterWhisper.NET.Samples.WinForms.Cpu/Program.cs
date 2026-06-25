// Copyright (c) 2026 Qourex. Licensed under the MIT License.

using System;
using System.Windows.Forms;

namespace Qourex.FasterWhisper.NET.Samples.WinForms.Cpu
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            
            // Enable official .NET 9/10 WinForms Dark Mode
            Application.SetColorMode(SystemColorMode.Dark);
            
            Application.Run(new MainForm());
        }
    }
}
