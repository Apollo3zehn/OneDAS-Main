using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.PackageManagement;
using NuGet.Packaging.Core;
using NuGet.ProjectManagement;
using NuGet.ProjectModel;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Resolver;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static NuGet.Frameworks.FrameworkConstants;

namespace OneDas.PackageManagement
{
    public class OneDasPackageManager
    {
        #region "Events"

        public event EventHandler InstalledPackagesChanged;

        #endregion

        #region "Fields"

        OneDasOptions _options;
        OneDasNugetProject _project;
        NuGetPackageManager _packageManager;
        SourceRepositoryProvider _sourceRepositoryProvider;
        OneDasNuGetProjectContext _projectContext;

        ISettings _settings;

        #endregion

        #region "Constructors"

        public OneDasPackageManager(IOptions<OneDasOptions> options, ILoggerFactory loggerFactory)
        {
            _options = options.Value;

            // settings
            if (File.Exists(Path.Combine(_options.NugetDirectoryPath, "NuGet.Config")))
            {
                _settings = new Settings(_options.NugetDirectoryPath);
            }
            else
            {
                _settings = new Settings(_options.NugetDirectoryPath);
                _settings.AddOrUpdate(ConfigurationConstants.PackageSources, new AddItem("MyGet (CI)", "https://www.myget.org/F/onedas/api/v3/index.json"));
                _settings.AddOrUpdate(ConfigurationConstants.PackageSources, new AddItem("local", "local"));
                _settings.AddOrUpdate(ConfigurationConstants.Config, new AddItem("globalPackagesFolder", "cache"));
            }

            if (!File.Exists(_options.NugetProjectFilePath))
            {
                var jobject = new JObject();
                var versionRange = new VersionRange(new NuGetVersion("2.0.3"));

                JsonConfigUtility.AddFramework(jobject, new NuGetFramework(FrameworkIdentifiers.NetStandard, new Version(2, 1, 0, 0)));
                JsonConfigUtility.AddDependency(jobject, new PackageDependency("NETStandard.Library", versionRange));

                jobject.Add("runtimes", new JObject(new JProperty(_options.RestoreRuntimeId, new JObject())));

                File.WriteAllText(_options.NugetProjectFilePath, jobject.ToString(Formatting.Indented));
            }

            _project = new OneDasNugetProject(_options.NugetProjectFilePath);
            _projectContext = new OneDasNuGetProjectContext(_settings, loggerFactory.CreateLogger("Nuget"));
            _sourceRepositoryProvider = this.CreateSourceRepositoryProvider();
            _packageManager = this.CreateNuGetPackageManager(_sourceRepositoryProvider);

            this.PackageSourceSet = SettingsUtility.GetEnabledSources(_settings).ToList();
        }

        #endregion

        #region "Properties"

        public List<PackageSource> PackageSourceSet { get; private set; }

        #endregion

        #region "Methods"

        public LockFile GetLockFile()
        {
            return LockFileUtilities.GetLockFile(_project.GetAssetsFilePathAsync().Result, _projectContext.LoggerAdapter);
        }

        public async Task<List<OneDasPackageMetaData>> GetInstalledPackagesAsync()
        {
            var installedPackageSet = await _project.GetInstalledPackagesAsync(new CancellationToken());

            var packageMetadataSet = installedPackageSet.
                    Where(packageReference => packageReference.PackageIdentity.Id != "NETStandard.Library").
                    Select(packageReference => new OneDasPackageMetaData(packageReference.PackageIdentity.Id, string.Empty, packageReference.PackageIdentity.Version.ToString(), true, false)).ToList();

            return packageMetadataSet;
        }

        public async Task<OneDasPackageMetaData[]> SearchAsync(string searchTerm, string source, int skip, int take)
        {
            IEnumerable<IPackageSearchMetadata> searchMetadataSet;

            var packageSource = new PackageSource(source);
            var sourceRepository = _sourceRepositoryProvider.CreateRepository(packageSource);
            var searchFilter = new SearchFilter(true) { PackageTypes = new List<string>() { "Dependency" } }; // _options.PackageTypeName -> not really working

            var packageSearchResource = await sourceRepository.GetResourceAsync<PackageSearchResource>();

            if (packageSource.IsLocal)
                searchMetadataSet = await packageSearchResource.SearchAsync(searchTerm, searchFilter, skip, take, _projectContext.LoggerAdapter, CancellationToken.None);
            else
                searchMetadataSet = await packageSearchResource.SearchAsync($"{ searchTerm.Replace(" ", "+") }+{ _options.PackageTypeName }", searchFilter, skip, take, _projectContext.LoggerAdapter, CancellationToken.None);

            var installedPackageSet = await _project.GetInstalledPackagesAsync(new CancellationToken());

            var taskSet = searchMetadataSet.Select(async searchMetadata =>
            {
                var isUpdateAvailable = false;
                var versionInfoSet = await searchMetadata.GetVersionsAsync();
                var installedPackage = installedPackageSet.FirstOrDefault(packageReference => packageReference.PackageIdentity.Id == searchMetadata.Identity.Id);
                var isInstalled = installedPackage != null;

                if (isInstalled)
                    isUpdateAvailable = versionInfoSet.Last().Version.CompareTo(installedPackage.PackageIdentity.Version, VersionComparison.VersionReleaseMetadata) > 0;

                return new OneDasPackageMetaData(searchMetadata.Identity.Id, searchMetadata.Description, searchMetadata.Identity.Version.ToString(), isInstalled, isUpdateAvailable);
            }).ToList();

            return await Task.WhenAll(taskSet);
        }

        public async Task InstallAsync(string packageId)
        {
            var resolutionContext = new ResolutionContext(DependencyBehavior.Lowest, includePrelease: true, includeUnlisted: false, VersionConstraints.None);
            var packageDownloadContext = new PackageDownloadContext(NullSourceCacheContext.Instance);
            var sourceRepositorySet = this.PackageSourceSet.Select(packageSource => _sourceRepositoryProvider.CreateRepository(new PackageSource(packageSource.Source))).ToList();
            
            var actionSet = await _packageManager.PreviewInstallPackageAsync(
                _project,
                packageId,
                resolutionContext,
                _projectContext,
                sourceRepositorySet,
                null,
                CancellationToken.None);

            await _packageManager.ExecuteNuGetProjectActionsAsync(_project, actionSet, _projectContext, packageDownloadContext, CancellationToken.None);

            this.OnInstalledPackagesChanged();
        }

        public async Task UpdateAsync(string packageId)
        {
            var resolutionContext = new ResolutionContext(DependencyBehavior.Lowest, includePrelease: true, includeUnlisted: false, VersionConstraints.None);
            var packageDownloadContext = new PackageDownloadContext(NullSourceCacheContext.Instance);
            var sourceRepositorySet = this.PackageSourceSet.Select(packageSource => _sourceRepositoryProvider.CreateRepository(new PackageSource(packageSource.Source))).ToList();

            var actionSet = await _packageManager.PreviewUpdatePackagesAsync(
                        packageId,
                        new List<NuGetProject>() { _project },
                        resolutionContext,
                        _projectContext,
                        sourceRepositorySet,
                        sourceRepositorySet,
                        CancellationToken.None);

            await _packageManager.ExecuteNuGetProjectActionsAsync(_project, actionSet, _projectContext, packageDownloadContext, CancellationToken.None);

            this.OnInstalledPackagesChanged();
        }

        public async Task UninstallAsync(string packageId)
        {
            var uninstallationContext = new UninstallationContext();

            var installedPackageSet = await _project.GetInstalledPackagesAsync(new CancellationToken());
            var installedPackage = installedPackageSet.FirstOrDefault(packageReference => packageReference.PackageIdentity.Id == packageId);

            if (installedPackage != null)
            {
                await _packageManager.UninstallPackageAsync(_project, packageId, uninstallationContext, _projectContext, CancellationToken.None);
            }

            this.OnInstalledPackagesChanged();
        }

        private void OnInstalledPackagesChanged()
        {
            this.InstalledPackagesChanged?.Invoke(this, EventArgs.Empty);
        }

        private NuGetPackageManager CreateNuGetPackageManager(SourceRepositoryProvider sourceRepositoryProvider)
        {
            var solutionManager = new OneDasSolutionManager(_projectContext, _project, _options.NugetDirectoryPath);
            var deleteOnRestartManager = new OneDasDeleteOnRestartManager();
            var packageManager = new NuGetPackageManager(sourceRepositoryProvider, _settings, solutionManager, deleteOnRestartManager);

            return packageManager;
        }

        private SourceRepositoryProvider CreateSourceRepositoryProvider()
        {
            var providerSet = new List<Lazy<INuGetResourceProvider>>();
            providerSet.AddRange(Repository.Provider.GetCoreV3());

            var sourceRepositoryProvider = new SourceRepositoryProvider(_settings, providerSet);

            return sourceRepositoryProvider;
        }

        #endregion
    }
}