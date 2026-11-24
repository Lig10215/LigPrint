using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing.Printing;
using System.Runtime.InteropServices;

namespace LigPrint {
public static class PrinterQueues {
    // Level 4 returns the local spooler's installed and cached connected queue names.
    const uint LocalAndConnections = 2 | 4;
    const int InsufficientBuffer = 122;
    [StructLayout(LayoutKind.Sequential)]
    struct PrinterInfo4 {
        public IntPtr Name;
        public IntPtr Server;
        public uint Attributes;
    }
    [DllImport("winspool.drv", EntryPoint="EnumPrintersW", SetLastError=true)]
    static extern bool EnumPrinters(uint flags, IntPtr server, uint level, IntPtr buffer,
        uint size, out uint needed, out uint returned);

    public static List<string> Installed() {
        IntPtr buffer = IntPtr.Zero;
        uint capacity = 0;
        try {
            while (true) {
                uint needed, count;
                if (EnumPrinters(LocalAndConnections, IntPtr.Zero, 4, buffer, capacity, out needed, out count)) {
                    var names = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
                    int stride = Marshal.SizeOf(typeof(PrinterInfo4));
                    for (int i = 0; i < count; i++) {
                        var info = (PrinterInfo4)Marshal.PtrToStructure(IntPtr.Add(buffer, checked(i * stride)), typeof(PrinterInfo4));
                        string name = Marshal.PtrToStringUni(info.Name);
                        if (!String.IsNullOrEmpty(name)) names.Add(name);
                    }
                    return new List<string>(names);
                }
                int error = Marshal.GetLastWin32Error();
                if (error != InsufficientBuffer || needed <= capacity) throw new Win32Exception(error);
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                buffer = IntPtr.Zero;
                capacity = needed;
                buffer = Marshal.AllocHGlobal(checked((int)capacity));
            }
        } finally {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
        }
    }
    const string PreferenceKey = @"Software\LigPrint";
    public static string ReadLastUsed() {
        try {
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(PreferenceKey))
                return key == null ? null : key.GetValue("LastPrinter") as string;
        } catch (UnauthorizedAccessException) {}
        catch (System.Security.SecurityException) {}
        catch (System.IO.IOException) {}
        return null;
    }
    public static void Remember(string name) {
        try {
            using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(PreferenceKey))
                key.SetValue("LastPrinter", name);
        } catch (UnauthorizedAccessException) {}
        catch (System.Security.SecurityException) {}
        catch (System.IO.IOException) {}
    }
    public static int PreferredIndex(List<string> installed, string previous) {
        int index = installed.FindIndex(name => String.Equals(name, previous, StringComparison.OrdinalIgnoreCase));
        if (index >= 0 || installed.Count == 0) return Math.Max(index, 0);
        try {
            string systemDefault = new PrinterSettings().PrinterName;
            index = installed.FindIndex(name => String.Equals(name, systemDefault, StringComparison.OrdinalIgnoreCase));
        } catch (Win32Exception) {}
        catch (InvalidPrinterException) {}
        return Math.Max(index, 0);
    }
}
}

