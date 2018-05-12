﻿using Microsoft.DotNet.PlatformAbstractions;
using Microsoft.Extensions.DependencyModel;
using Microsoft.Extensions.Logging;
using NuGet.Common;
using NuGet.Frameworks;
using NuGet.ProjectModel;
using OneDas.Extensibility;
using OneDas.Extensibility.PackageManagement;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using ILogger = Microsoft.Extensions.Logging.ILogger;

// https://www.codeproject.com/Articles/1194332/Resolving-Assemblies-in-NET-Core

namespace OneDas.WebServer.Core
{
    public class ExtensionLoader
    {
        private ILogger _logger;
        private IExtensionFactory _extensionFactory;
        private OneDasPackageManager _packageManager;

        public ExtensionLoader(ILoggerFactory loggerFactory, IExtensionFactory extensionFactory, OneDasPackageManager packageManager)
        {
            _logger = loggerFactory.CreateLogger("Nuget");
            _packageManager = packageManager;
            _extensionFactory = extensionFactory;

            packageManager.InstalledPackagesChanged += (sender, e) => this.ReloadPackages();
        }

        public void ReloadPackages()
        {
            List<Assembly> assemblySet;

            _extensionFactory.Clear();

            assemblySet = this.LoadPackages();

            assemblySet.ToList().ForEach(assembly =>
            {
                _extensionFactory.ScanAssembly(assembly);
            });
        }

        private List<Assembly> LoadPackages()
        {
            LockFile lockFile;
            LockFileTarget lockFileTarget;
            OneDasAssemblyLoadContext loadContext;
            HashSet<Assembly> assemblySet;

            assemblySet = new HashSet<Assembly>();

            lockFile = _packageManager.GetLockFile();
            lockFileTarget = lockFile?.GetTarget(FrameworkConstants.CommonFrameworks.NetStandard20, RuntimeEnvironment.GetRuntimeIdentifier());
            loadContext = new OneDasAssemblyLoadContext();

            loadContext.Resolving += (assemblyLoadContext, assemblyName) =>
            {
                _logger.LogDebug("########## Searching for: " + assemblyName + " #############");
                return null;
            };

            if (lockFileTarget != null)
            {
                lockFileTarget.Libraries.ToList().ForEach(targetLibrary =>
                {
                    string basePath;
                    string absoluteFilePath;
                    bool _isAlreadyIncluded;

                    LockFileLibrary lockFileLibrary;

                    lockFileLibrary = lockFile.GetLibrary(targetLibrary.Name, targetLibrary.Version);
                    basePath = Path.Combine(lockFile.PackageFolders.First().Path, lockFileLibrary.Path);

                    _logger.LogDebug("########## Processing library: " + targetLibrary.Name + " #############");

                    _isAlreadyIncluded = DependencyContext.Default.RuntimeLibraries.Any(runtimeLibrary => runtimeLibrary.Name == targetLibrary.Name && runtimeLibrary.Version == targetLibrary.Version.ToString())
                                      || targetLibrary.Name == "OneDas.Extensibility.Abstractions";

                    if (_isAlreadyIncluded)
                    {
                        _logger.LogDebug("skipping library: " + targetLibrary.Name + "/" + targetLibrary.Version.ToString() + " ##");
                        return;
                    }

                    // RuntimeAssemblies
                    targetLibrary.RuntimeAssemblies.ToList().ForEach(runtimeAssembly =>
                    {
                        absoluteFilePath = PathUtility.GetPathWithBackSlashes(Path.Combine(basePath, runtimeAssembly.Path));

                        _logger.LogDebug("## processing file: " + absoluteFilePath + " ##");

                        if (runtimeAssembly.Path.EndsWith("/_._"))
                        {
                            _logger.LogDebug("skipping file: " + absoluteFilePath + " ##");

                            return;
                        }

                        try
                        {
                            //assemblySet.Add(loadContext.LoadFromAssemblyPath(absoluteFilePath));
                            assemblySet.Add(AssemblyLoadContext.Default.LoadFromAssemblyPath(absoluteFilePath));

                            _logger.LogDebug("file loaded: " + absoluteFilePath);
                        }
                        catch
                        {
                            _logger.LogDebug("file loading failed: " + absoluteFilePath);
                            throw;
                        }
                    });

                    // NativeLibraries
                    targetLibrary.NativeLibraries.ToList().ForEach(nativeLibrary =>
                    {
                        absoluteFilePath = PathUtility.GetPathWithBackSlashes(Path.Combine(basePath, nativeLibrary.Path));

                        _logger.LogDebug("## processing file: " + absoluteFilePath + " ##");

                        try
                        {
                            loadContext.LoadFromNativeAssemblyPath(absoluteFilePath);
                            _logger.LogDebug("native file loaded: " + absoluteFilePath);
                        }
                        catch
                        {
                            _logger.LogDebug("native file loading failed: " + absoluteFilePath);
                            throw;
                        }
                    });
                });
            }

            return assemblySet.ToList();
        }

        private class OneDasAssemblyLoadContext : AssemblyLoadContext
        {
            public void LoadFromNativeAssemblyPath(string assemblyFilePath)
            {
                this.LoadUnmanagedDllFromPath(assemblyFilePath);
            }

            protected override Assembly Load(AssemblyName assemblyName)
            {
                Debug.WriteLine("LOAD: request received to load: " + assemblyName);
                return null;
            }

            //protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
            //{
            //    Debug.WriteLine("LoadUnmanagedDll: request received to load: " + unmanagedDllName);

            //    return base.LoadUnmanagedDll(unmanagedDllName);
            //}
        }
    }
}
