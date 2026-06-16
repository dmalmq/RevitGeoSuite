using System;
using System.Collections.Generic;
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

public sealed class DialogOpenFileHandler : IRpcHandler
{
    public string Method => "dialog.openFile";

    public Task<object?> HandleAsync(object? payload)
    {
        try
        {
            JObject? request = payload as JObject;
            string title = request?.Value<string>("title")?.Trim() ?? "Select GIS File";
            string? initialPath = request?.Value<string>("initialPath");
            string[] paths = ExplorerFileDialog.Show(title, initialPath);

            if (paths.Length > 0)
            {
                return Task.FromResult<object?>(new DialogOpenFileResponse { Path = paths[0], Paths = paths });
            }

            return Task.FromResult<object?>(new DialogOpenFileResponse());
        }
        catch (Exception ex)
        {
            return Task.FromResult<object?>(new DialogOpenFileResponse { Error = ex.Message });
        }
    }

    private static class ExplorerFileDialog
    {
        private const int ErrorCancelled = unchecked((int)0x800704C7);
        private const int GisFilterCount = 3;
        private static readonly Guid FileOpenDialogClsid = new Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");

        public static string[] Show(string title, string? initialPath)
        {
            IFileOpenDialog? dialog = null;
            IShellItem? folder = null;
            IShellItemArray? results = null;
            IntPtr filtersPtr = IntPtr.Zero;

            try
            {
                Type dialogType = Type.GetTypeFromCLSID(FileOpenDialogClsid, throwOnError: true)!;
                dialog = (IFileOpenDialog)Activator.CreateInstance(dialogType)!;
                dialog.GetOptions(out FileOpenOptions options);
                dialog.SetOptions(options
                    | FileOpenOptions.ForceFileSystem
                    | FileOpenOptions.PathMustExist
                    | FileOpenOptions.FileMustExist
                    | FileOpenOptions.AllowMultiSelect);
                dialog.SetTitle(string.IsNullOrWhiteSpace(title) ? "Select GIS File" : title);
                SetGisFilters(dialog, out filtersPtr);

                string? initialFileName = null;
                if (TryCreateInitialFolder(initialPath, out folder, out initialFileName) && folder is not null)
                {
                    dialog.SetFolder(folder);
                    dialog.SetDefaultFolder(folder);
                }

                if (!string.IsNullOrWhiteSpace(initialFileName))
                {
                    dialog.SetFileName(initialFileName!);
                }

                int hr = dialog.Show(IntPtr.Zero);
                if (hr == ErrorCancelled)
                {
                    return Array.Empty<string>();
                }

                if (hr < 0)
                {
                    Marshal.ThrowExceptionForHR(hr);
                }

                dialog.GetResults(out results);
                if (results is null)
                {
                    return Array.Empty<string>();
                }

                results.GetCount(out uint count);
                int capacity = count > int.MaxValue ? int.MaxValue : (int)count;
                List<string> paths = new(capacity);
                for (uint index = 0; index < count; index++)
                {
                    IShellItem? item = null;
                    try
                    {
                        results.GetItemAt(index, out item);
                        if (item is null)
                        {
                            continue;
                        }

                        string? path = GetFileSystemPath(item);
                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            paths.Add(path!);
                        }
                    }
                    finally
                    {
                        ReleaseComObject(item);
                    }
                }

                return paths.ToArray();
            }
            finally
            {
                if (filtersPtr != IntPtr.Zero)
                {
                    DestroyGisFilters(filtersPtr);
                    Marshal.FreeCoTaskMem(filtersPtr);
                }

                ReleaseComObject(results);
                ReleaseComObject(folder);
                ReleaseComObject(dialog);
            }
        }

        private static void SetGisFilters(IFileOpenDialog dialog, out IntPtr filtersPtr)
        {
            FileDialogFilterSpec[] filters =
            {
                new FileDialogFilterSpec("GIS files (*.shp;*.gpkg)", "*.shp;*.gpkg"),
                new FileDialogFilterSpec("Shapefile (*.shp)", "*.shp"),
                new FileDialogFilterSpec("GeoPackage (*.gpkg)", "*.gpkg"),
            };

            if (filters.Length != GisFilterCount)
            {
                throw new InvalidOperationException("GIS file dialog filter count is out of sync.");
            }

            int structSize = Marshal.SizeOf(typeof(FileDialogFilterSpec));
            filtersPtr = Marshal.AllocCoTaskMem(structSize * filters.Length);
            for (int i = 0; i < filters.Length; i++)
            {
                Marshal.StructureToPtr(filters[i], filtersPtr + (i * structSize), fDeleteOld: false);
            }

            dialog.SetFileTypes((uint)filters.Length, filtersPtr);
            dialog.SetFileTypeIndex(1);
            dialog.SetDefaultExtension("gpkg");
        }

        private static void DestroyGisFilters(IntPtr filtersPtr)
        {
            int structSize = Marshal.SizeOf(typeof(FileDialogFilterSpec));
            for (int i = 0; i < GisFilterCount; i++)
            {
                Marshal.DestroyStructure(filtersPtr + (i * structSize), typeof(FileDialogFilterSpec));
            }
        }

        private static bool TryCreateInitialFolder(string? path, out IShellItem? item, out string? fileName)
        {
            item = null;
            fileName = null;
            string? trimmedPath = path?.Trim();
            if (string.IsNullOrWhiteSpace(trimmedPath))
            {
                return false;
            }

            try
            {
                string fullPath = Path.GetFullPath(trimmedPath);
                string? folderPath = null;

                if (File.Exists(fullPath))
                {
                    folderPath = Path.GetDirectoryName(fullPath);
                    fileName = Path.GetFileName(fullPath);
                }
                else if (Directory.Exists(fullPath))
                {
                    folderPath = fullPath;
                }

                if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                {
                    return false;
                }

                Guid shellItemId = typeof(IShellItem).GUID;
                int hr = SHCreateItemFromParsingName(folderPath!, IntPtr.Zero, ref shellItemId, out item);
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
                fileName = null;
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

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct FileDialogFilterSpec
        {
            [MarshalAs(UnmanagedType.LPWStr)]
            public string Name;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string Spec;

            public FileDialogFilterSpec(string name, string spec)
            {
                Name = name;
                Spec = spec;
            }
        }

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

            void GetResults(out IShellItemArray ppenum);

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

        [ComImport]
        [Guid("B63EA76D-1F85-456F-A19C-48159EFA858B")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemArray
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppvOut);

            void GetPropertyStore(int flags, ref Guid riid, out IntPtr ppv);

            void GetPropertyDescriptionList(ref PropertyKey keyType, ref Guid riid, out IntPtr ppv);

            void GetAttributes(uint attribFlags, uint sfgaoMask, out uint psfgaoAttribs);

            void GetCount(out uint pdwNumItems);

            void GetItemAt(uint dwIndex, out IShellItem ppsi);

            void EnumItems(out IntPtr ppenumShellItems);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyKey
        {
            public Guid FormatId;
            public uint PropertyId;
        }

        [Flags]
        private enum FileOpenOptions : uint
        {
            AllowMultiSelect = 0x00000200,
            ForceFileSystem = 0x00000040,
            PathMustExist = 0x00000800,
            FileMustExist = 0x00001000
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
