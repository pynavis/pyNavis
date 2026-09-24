using System;
using System.Runtime.InteropServices;

namespace PyNavis.Runtime.Forms
{
    /// <summary>Vista+ folder picker via IFileOpenDialog (no WinForms dependency).</summary>
    public static class FolderPicker
    {
        public static string Pick(string title, string initialDir)
        {
            var dialog = (IFileOpenDialog)new FileOpenDialogRcw();
            try
            {
                dialog.SetOptions(FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM);
                if (!string.IsNullOrEmpty(title)) dialog.SetTitle(title);
                if (!string.IsNullOrEmpty(initialDir) && System.IO.Directory.Exists(initialDir)
                    && SHCreateItemFromParsingName(initialDir, IntPtr.Zero, typeof(IShellItem).GUID, out var folder) == 0)
                    dialog.SetFolder(folder);
                if (dialog.Show(GetActiveWindow()) != 0) return null;   // cancelled
                dialog.GetResult(out var item);
                item.GetDisplayName(SIGDN_FILESYSPATH, out var path);
                return path;
            }
            catch (Exception ex)
            {
                Log.Error("Folder picker failed", ex);
                return null;
            }
        }

        private const uint FOS_PICKFOLDERS = 0x20, FOS_FORCEFILESYSTEM = 0x40;
        private const uint SIGDN_FILESYSPATH = 0x80058000;

        [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHCreateItemFromParsingName(string path, IntPtr bc, [MarshalAs(UnmanagedType.LPStruct)] Guid iid, out IShellItem item);

        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        private class FileOpenDialogRcw { }

        [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint count, IntPtr types);
            void SetFileTypeIndex(uint index);
            void GetFileTypeIndex(out uint index);
            void Advise(IntPtr events, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint options);
            void GetOptions(out uint options);
            void SetDefaultFolder(IShellItem item);
            void SetFolder(IShellItem item);
            void GetFolder(out IShellItem item);
            void GetCurrentSelection(out IShellItem item);
            void SetFileName(string name);
            void GetFileName(out string name);
            void SetTitle(string title);
            void SetOkButtonLabel(string label);
            void SetFileNameLabel(string label);
            void GetResult(out IShellItem item);
            void AddPlace(IShellItem item, int placement);
            void SetDefaultExtension(string extension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr filter);
            void GetResults(out IntPtr results);
            void GetSelectedItems(out IntPtr items);
        }

        [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr bc, ref Guid bhid, ref Guid riid, out IntPtr obj);
            void GetParent(out IShellItem parent);
            void GetDisplayName(uint sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string name);
            void GetAttributes(uint mask, out uint attributes);
            void Compare(IShellItem other, uint hint, out int order);
        }
    }
}
