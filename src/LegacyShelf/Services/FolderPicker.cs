using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LegacyShelf.Services
{
    public static class FolderPicker
    {
        private const uint FosPickFolders = 0x00000020;
        private const uint FosForceFileSystem = 0x00000040;
        private const uint FosPathMustExist = 0x00000800;
        private const uint FosNoChangeDir = 0x00000008;
        private const uint SigdnFileSysPath = 0x80058000;
        private const int Cancelled = unchecked((int)0x800704C7);

        public static string Pick(string title, string initialPath = null, Window ownerWindow = null)
        {
            try
            {
                var dialog = (IFileDialog)new FileOpenDialogRcw();
                dialog.SetOptions(FosPickFolders | FosForceFileSystem | FosPathMustExist | FosNoChangeDir);
                dialog.SetTitle(title ?? "Select a folder");
                dialog.SetOkButtonLabel("Select folder");

                IntPtr owner = IntPtr.Zero;
                if (ownerWindow != null)
                {
                    owner = new WindowInteropHelper(ownerWindow).Handle;
                }
                else if (Application.Current != null && Application.Current.MainWindow != null)
                {
                    owner = new WindowInteropHelper(Application.Current.MainWindow).Handle;
                }

                var hr = dialog.Show(owner);
                if (hr == Cancelled || hr != 0)
                {
                    Marshal.ReleaseComObject(dialog);
                    return null;
                }

                IShellItem item;
                hr = dialog.GetResult(out item);
                if (hr != 0 || item == null)
                {
                    Marshal.ReleaseComObject(dialog);
                    return null;
                }

                IntPtr namePtr;
                hr = item.GetDisplayName(SigdnFileSysPath, out namePtr);
                var path = hr == 0 && namePtr != IntPtr.Zero
                    ? Marshal.PtrToStringUni(namePtr)
                    : null;

                if (namePtr != IntPtr.Zero) Marshal.FreeCoTaskMem(namePtr);
                Marshal.ReleaseComObject(item);
                Marshal.ReleaseComObject(dialog);

                return string.IsNullOrWhiteSpace(path) ? null : path;
            }
            catch (Exception ex)
            {
                Log.Warn("Modern folder picker failed; using the classic picker. " + ex.Message);
                return PickClassic(title, initialPath);
            }
        }

        private static string PickClassic(string title, string initialPath)
        {
            try
            {
                using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
                {
                    dialog.Description = title ?? "Select a folder";
                    dialog.ShowNewFolderButton = true;
                    if (!string.IsNullOrWhiteSpace(initialPath) && System.IO.Directory.Exists(initialPath))
                    {
                        dialog.SelectedPath = initialPath;
                    }

                    return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK
                        ? dialog.SelectedPath
                        : null;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Folder picker failed.", ex);
                return null;
            }
        }

        [ComImport]
        [Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        private class FileOpenDialogRcw
        {
        }

        [ComImport]
        [Guid("42f85136-db7e-439c-85f1-e4075d135fc8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialog
        {
            [PreserveSig]
            int Show(IntPtr parent);

            void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
            void SetFileTypeIndex(uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise(IntPtr pfde, out uint pdwCookie);
            void Unadvise(uint dwCookie);
            void SetOptions(uint fos);
            void GetOptions(out uint pfos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);

            [PreserveSig]
            int GetResult(out IShellItem ppsi);
            void AddPlace(IShellItem psi, int fdap);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr pFilter);
        }

        [ComImport]
        [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);

            [PreserveSig]
            int GetDisplayName(uint sigdnName, out IntPtr ppszName);

            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }
    }
}
