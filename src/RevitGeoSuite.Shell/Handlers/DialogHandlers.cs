using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.SharedUI.Shell;
using RevitGeoSuite.SharedUI.Web.Contracts;

namespace RevitGeoSuite.Shell.Handlers;

public sealed class DialogOpenFolderHandler : IRpcHandler
{
    public string Method => "dialog.openFolder";

    public Task<object?> HandleAsync(object? payload)
    {
        try
        {
            JObject? request = payload as JObject;
            string title = request?.Value<string>("title")?.Trim() ?? "Select Folder";
            string? initialPath = request?.Value<string>("initialPath");
            string? path = ExplorerFolderDialog.Show(title, initialPath);

            if (!string.IsNullOrWhiteSpace(path))
            {
                return Task.FromResult<object?>(new DialogOpenFolderResponse { Path = path });
            }

            return Task.FromResult<object?>(new DialogOpenFolderResponse());
        }
        catch (Exception ex)
        {
            return Task.FromResult<object?>(new DialogOpenFolderResponse { Error = ex.Message });
        }
    }

    private static class ExplorerFolderDialog
    {
        private const int ErrorCancelled = unchecked((int)0x800704C7);
        private static readonly Guid FileOpenDialogClsid = new Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");

        public static string? Show(string title, string? initialPath)
        {
            IFileOpenDialog? dialog = null;
            IShellItem? folder = null;
            IShellItem? result = null;

            try
            {
                Type dialogType = Type.GetTypeFromCLSID(FileOpenDialogClsid, throwOnError: true)!;
                dialog = (IFileOpenDialog)Activator.CreateInstance(dialogType)!;
                dialog.GetOptions(out FileOpenOptions options);
                dialog.SetOptions(options
                    | FileOpenOptions.PickFolders
                    | FileOpenOptions.ForceFileSystem
                    | FileOpenOptions.PathMustExist);
                dialog.SetTitle(string.IsNullOrWhiteSpace(title) ? "Select Folder" : title);

                if (TryCreateShellItem(initialPath, out folder) && folder is not null)
                {
                    dialog.SetFolder(folder);
                    dialog.SetDefaultFolder(folder);
                }

                int hr = dialog.Show(IntPtr.Zero);
                if (hr == ErrorCancelled)
                {
                    return null;
                }

                if (hr < 0)
                {
                    Marshal.ThrowExceptionForHR(hr);
                }

                dialog.GetResult(out result);
                if (result is null)
                {
                    return null;
                }

                return GetFileSystemPath(result);
            }
            finally
            {
                ReleaseComObject(result);
                ReleaseComObject(folder);
                ReleaseComObject(dialog);
            }
        }

        private static bool TryCreateShellItem(string? path, out IShellItem? item)
        {
            item = null;
            string? trimmedPath = path?.Trim();
            if (string.IsNullOrWhiteSpace(trimmedPath))
            {
                return false;
            }

            try
            {
                string fullPath = Path.GetFullPath(trimmedPath);
                if (!Directory.Exists(fullPath))
                {
                    return false;
                }

                Guid shellItemId = typeof(IShellItem).GUID;
                int hr = SHCreateItemFromParsingName(fullPath, IntPtr.Zero, ref shellItemId, out item);
                if (hr < 0)
                {
                    item = null;
                    return false;
                }

                return item is not null;
            }
            catch
            {
                item = null;
                return false;
            }
        }

        private static string? GetFileSystemPath(IShellItem item)
        {
            IntPtr pathPtr = IntPtr.Zero;
            try
            {
                item.GetDisplayName(ShellItemDisplayName.FileSystemPath, out pathPtr);
                return Marshal.PtrToStringUni(pathPtr);
            }
            finally
            {
                if (pathPtr != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(pathPtr);
                }
            }
        }

        private static void ReleaseComObject(object? instance)
        {
            if (instance is not null && Marshal.IsComObject(instance))
            {
                Marshal.FinalReleaseComObject(instance);
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem? ppv);
        [ComImport]
        [Guid("D57C7288-D4AD-4768-BE02-9D969532D960")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            [PreserveSig]
            int Show(IntPtr parent);

            void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);

            void SetFileTypeIndex(uint iFileType);

            void GetFileTypeIndex(out uint piFileType);

            void Advise(IntPtr pfde, out uint pdwCookie);

            void Unadvise(uint dwCookie);

            void SetOptions(FileOpenOptions fos);

            void GetOptions(out FileOpenOptions pfos);

            void SetDefaultFolder(IShellItem psi);

            void SetFolder(IShellItem psi);

            void GetFolder(out IShellItem ppsi);

            void GetCurrentSelection(out IShellItem ppsi);

            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);

            void GetFileName(out IntPtr pszName);

            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);

            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);

            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);

            void GetResult(out IShellItem ppsi);

            void AddPlace(IShellItem psi, FileDialogAddPlace fdap);

            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);

            void Close(int hr);

            void SetClientGuid(ref Guid guid);

            void ClearClientData();

            void SetFilter(IntPtr pFilter);

            void GetResults(out IntPtr ppenum);

            void GetSelectedItems(out IntPtr ppsai);
        }

        [ComImport]
        [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);

            void GetParent(out IShellItem ppsi);

            void GetDisplayName(ShellItemDisplayName sigdnName, out IntPtr ppszName);

            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);

            void Compare(IShellItem psi, uint hint, out int piOrder);
        }

        [Flags]
        private enum FileOpenOptions : uint
        {
            PickFolders = 0x00000020,
            ForceFileSystem = 0x00000040,
            PathMustExist = 0x00000800
        }

        private enum FileDialogAddPlace
        {
            Bottom = 0,
            Top = 1
        }

        private enum ShellItemDisplayName : uint
        {
            FileSystemPath = 0x80058000
        }
    }
}
