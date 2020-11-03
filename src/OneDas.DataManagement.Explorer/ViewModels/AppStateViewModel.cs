﻿using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using OneDas.DataManagement.Database;
using OneDas.DataManagement.Explorer.Core;
using OneDas.DataManagement.Infrastructure;
using OneDas.Extension.Csv;
using OneDas.Infrastructure;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace OneDas.DataManagement.Explorer.ViewModels
{
    public class AppStateViewModel : BindableBase, IDisposable
    {
        #region Fields

        private string _searchString;
        private string _downloadMessage;
        private string _sampleRate;

        private double _downloadProgress;
        private double _visualizeProgress;

        private bool _isEditEnabled;
        private bool _visualizeBeginAtZero;

        private ClientState _clientState;
        private IJSRuntime _jsRuntime;

        private DataService _dataService;
        private ProjectContainer _projectContainer;
        private OneDasDatabaseManager _databaseManager;
        private OneDasExplorerOptions _options;
        private CancellationTokenSource _cts_download;
        private PropertyChangedEventHandler _propertyChanged;
        private AuthenticationStateProvider _authenticationStateProvider;

        private List<ChannelInfoViewModel> _channelGroup;
        private Dictionary<string, List<DatasetInfoViewModel>> _sampleRateToSelectedDatasetsMap;
        private Dictionary<ProjectContainer, List<ChannelInfoViewModel>> _projectContainerToChannelsMap;

        #endregion

        #region Constructors

        public AppStateViewModel(IJSRuntime jsRuntime,
                                 AuthenticationStateProvider authenticationStateProvider,
                                 StateManager stateManager,
                                 OneDasDatabaseManager databaseManager,
                                 OneDasExplorerOptions options,
                                 DataService dataService)
        {
            _jsRuntime = jsRuntime;
            _authenticationStateProvider = authenticationStateProvider;
            _databaseManager = databaseManager;
            _options = options;
            _dataService = dataService;        

            this.Version = Assembly.GetEntryAssembly().GetName().Version.ToString();
            this.FileGranularityValues = Utilities.GetEnumValues<FileGranularity>();
            this.FileFormatValues = Utilities.GetEnumValues<FileFormat>();
            this.CsvRowIndexFormatValues = Utilities.GetEnumValues<CsvRowIndexFormat>();
            this.NewsPaper = NewsPaper.Load(Path.Combine(options.DataBaseFolderPath, "news.json"));
            this.VisualizeBeginAtZero = true;

            // project containers and dependent init steps
            var projectContainers = databaseManager.Database.ProjectContainers;
            var restrictedProjects = databaseManager.Config.RestrictedProjects;

            this.ProjectContainersInfo = this.SplitCampainContainersAsync(projectContainers, restrictedProjects, Constants.HiddenProjects).Result;

            this.SampleRateValues = this.ProjectContainersInfo.Accessible.SelectMany(projectContainer =>
            {
                return projectContainer.Project.Channels.SelectMany(channel =>
                {
                    return channel.Datasets.Select(dataset => dataset.Id.Split('_')[0]);
                });
            }).Distinct().OrderBy(x => x, new SampleRateStringComparer()).ToList();

            this.InitializeProjectContainerToChannelMap();
            this.InitializeSampleRateToDatasetsMap();

            // state manager
            _propertyChanged = (sender, e) =>
            {
                if (e.PropertyName == nameof(Core.StateManager.State))
                {
                    this.RaisePropertyChanged(e.PropertyName);
                }
            };

            this.StateManager = stateManager;
            this.StateManager.PropertyChanged += _propertyChanged;

            // export parameters
            this.SetExportParameters(new ExportParameters());
        }

        #endregion

        #region Properties - General

        public string Version { get; }

        public bool IsEditEnabled
        {
            get { return _isEditEnabled; }
            set
            {
#warning Make this more efficient. Maybe by tracking changes.
                if (_isEditEnabled && !value)
                {
                    _databaseManager.Database.ProjectContainers.ForEach(projectContainer =>
                    {
                        _databaseManager.SaveProjectMeta(projectContainer.ProjectMeta);
                    });
                }

                this.SetProperty(ref _isEditEnabled, value);
            }
        }

        public ClientState ClientState
        {
            get { return _clientState; }
            set { this.SetProperty(ref _clientState, value); }
        }

        public double DownloadProgress
        {
            get { return _downloadProgress; }
            set { this.SetProperty(ref _downloadProgress, value); }
        }

        public string DownloadMessage
        {
            get { return _downloadMessage; }
            set { this.SetProperty(ref _downloadMessage, value); }
        }

        internal StateManager StateManager { get; }

        internal ExportParameters ExportParameters { get; private set; }

        #endregion

        #region Properties - Settings

        // this is required because MatBlazor converts dates to local representation which is not desired
        public DateTime DateTimeBeginWorkaround
        {
            get { return DateTime.SpecifyKind(this.DateTimeBegin, DateTimeKind.Local); }
            set 
            { 
                if (value.Kind == DateTimeKind.Local)
                    this.DateTimeBegin = DateTime.SpecifyKind(value, DateTimeKind.Utc);
                else
                {
                    this.DateTimeBegin = DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(value, TimeZoneInfo.Local), DateTimeKind.Utc);
                }
                //this.DateTimeBegin = DateTime.SpecifyKind(value.Add(-this.BrowserTimeZoneOffset), DateTimeKind.Utc);

                if (this.DateTimeBegin >= this.DateTimeEnd)
                    this.DateTimeEnd = this.DateTimeBegin;
            }
        }

        public DateTime DateTimeEndWorkaround
        {
            get { return DateTime.SpecifyKind(this.DateTimeEnd, DateTimeKind.Local); }
            set
            {
                if (value.Kind == DateTimeKind.Local)
                    this.DateTimeEnd = DateTime.SpecifyKind(value, DateTimeKind.Utc);
                else
                    this.DateTimeEnd = DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(value, TimeZoneInfo.Local), DateTimeKind.Utc);

                if (this.DateTimeEnd <= this.DateTimeBegin)
                    this.DateTimeBegin = this.DateTimeEnd;
            }
        }

        public List<string> SampleRateValues { get; set; }

        public List<FileGranularity> FileGranularityValues { get; }

        public List<FileFormat> FileFormatValues { get; }

        public List<CsvRowIndexFormat> CsvRowIndexFormatValues { get; }

        public bool VisualizeBeginAtZero
        {
            get { return _visualizeBeginAtZero; }
            set { this.SetProperty(ref _visualizeBeginAtZero, value); }
        }

        public string SampleRate
        {
            get { return _sampleRate; }
            set { this.SetProperty(ref _sampleRate, value); }
        }

        #endregion

        #region Properties - Channel Selection

        public ProjectContainer ProjectContainer
        {
            get
            {
                return _projectContainer;
            }
            set
            {
                this.SetProperty(ref _projectContainer, value);

                _searchString = string.Empty;

                if (this.ProjectContainersInfo.Accessible.Contains(value))
                {
                    this.UpdateGroupedChannels();
                    this.UpdateAttachments();
                }
                else
                {
                    this.GroupedChannels = null;
                    this.Attachments = null;
                }
            }
        }

        public List<string> Attachments { get; private set; }

        public SplittedProjectContainers ProjectContainersInfo { get; }

        public Dictionary<string, List<ChannelInfoViewModel>> GroupedChannels { get; private set; }

        public List<ChannelInfoViewModel> ChannelGroup
        {
            get { return _channelGroup; }
            set { base.SetProperty(ref _channelGroup, value); }
        }

        public string SearchString
        {
            get { return _searchString; }
            set
            {
                base.SetProperty(ref _searchString, value);
                this.UpdateGroupedChannels();
            }
        }

        #endregion

        #region Properties - News

        public NewsPaper NewsPaper { get; }

        #endregion

        #region Properties - Download Area

        public IReadOnlyCollection<DatasetInfoViewModel> SelectedDatasets => this.GetSelectedDatasets();

        #endregion

        #region Properties - Visualization

        public double VisualizeProgress
        {
            get { return _visualizeProgress; }
            set { this.SetProperty(ref _visualizeProgress, value); }
        }

        #endregion

        #region Properties - Relay Properties

        public OneDasExplorerState State => this.StateManager.State;

        public DateTime DateTimeBegin
        {
            get { return this.ExportParameters.Begin; }
            set
            {
                this.ExportParameters.Begin = value;
                this.RaisePropertyChanged();
            }
        }

        public DateTime DateTimeEnd
        {
            get { return this.ExportParameters.End; }
            set
            {
                this.ExportParameters.End = value;
                this.RaisePropertyChanged();
            }
        }

        public FileGranularity FileGranularity
        {
            get { return this.ExportParameters.FileGranularity; }
            set 
            {
                this.ExportParameters.FileGranularity = value;
                this.RaisePropertyChanged();
            }
        }

        public FileFormat FileFormat
        {
            get { return this.ExportParameters.FileFormat; }
            set
            {
                this.ExportParameters.FileFormat = value;
                this.RaisePropertyChanged();
            }
        }

        #endregion

        #region Methods

        public List<string> GetPresets()
        {
            var folderPath = "PRESETS";
            return Directory.EnumerateFiles(folderPath, "*.json", SearchOption.TopDirectoryOnly).ToList();
        }

        public bool CanDownload()
        {
            if (this.SampleRate != null)
            {
                var samplePeriod = new SampleRateContainer(this.SampleRate).Period.TotalSeconds;


                return this.DateTimeBegin < this.DateTimeEnd &&
                       this.SelectedDatasets.Count > 0 &&
                       (ulong)this.FileGranularity >= samplePeriod &&
                       this.State == OneDasExplorerState.Ready;
            }
            else
            {
                return false;
            }
        }

        public async Task DownloadAsync(IPAddress remoteIpAdress)
        {
            _cts_download = new CancellationTokenSource();

            EventHandler<ProgressUpdatedEventArgs> eventHandler = (sender, e) =>
            {
                this.DownloadMessage = e.Message;
                this.DownloadProgress = e.Progress;
            };

            try
            {
                this.ClientState = ClientState.PrepareDownload;
                _dataService.Progress.ProgressChanged += eventHandler;

                var selectedDatasets = this.GetSelectedDatasets().Select(dataset => dataset.Model).ToList();

                var downloadLink = await _dataService.ExportDataWithSecurityCheckAsync(remoteIpAdress,
                                                                       this.ExportParameters,
                                                                       selectedDatasets,
                                                                       _cts_download.Token);

                if (!string.IsNullOrWhiteSpace(downloadLink))
                {
                    var fileName = downloadLink.Split("/").Last();
                    await _jsRuntime.FileSaveAs(fileName, downloadLink);
                }
            }
            finally
            {
                _dataService.Progress.ProgressChanged -= eventHandler;
                this.ClientState = ClientState.Normal;
                this.DownloadMessage = string.Empty;
                this.DownloadProgress = 0;
            }
        }

        public void CancelDownload()
        {
            _cts_download.Cancel();
        }

        public void ToggleDataAvailability()
        {
            if (this.ClientState != ClientState.DataAvailability)
                this.ClientState = ClientState.DataAvailability;
            else
                this.ClientState = ClientState.Normal;
        }

        public bool CanVisualize()
        {
            return this.SelectedDatasets.Any()
                && this.DateTimeBegin < this.DateTimeEnd;
        }

        public bool IsSizeLimitExceeded()
        {
            return this.GetByteCount() > 20 * 1000 * 1000;
        }

        public void ToggleDataVisualization()
        {
            if (this.ClientState != ClientState.DataVisualizing)
                this.ClientState = ClientState.DataVisualizing;
            else
                this.ClientState = ClientState.Normal;
        }

        [JSInvokable]
        public void SetVisualizeProgress(double progress)
        {
            this.VisualizeProgress = progress;
        }

        public async Task<DataAvailabilityStatistics> GetDataAvailabilityStatisticsAsync()
        {
            return await _dataService.GetDataAvailabilityStatisticsAsync(this.ProjectContainer.Id, this.DateTimeBegin, this.DateTimeEnd);
        }

        public void SetExportParameters(ExportParameters exportParameters)
        {
            this.InitializeSampleRateToDatasetsMap();

            // find sample rate
            var sampleRates = exportParameters.ChannelPaths.Select(channelPath 
                => new SampleRateContainer(channelPath.Split("/").Last()).ToUnitString());

            if (sampleRates.Any())
                this.SampleRate = sampleRates.First();

            //
            this.ExportParameters = exportParameters;
            var selectedDatasets = this.GetSelectedDatasets();

            exportParameters.ChannelPaths.ForEach(value =>
            {
                var pathParts = value.Split('/');
                var projectName = $"/{pathParts[1]}/{pathParts[2]}/{pathParts[3]}";
                var channelName = pathParts[4];
                var datasetName = pathParts[5];

                var projectContainer = this.ProjectContainersInfo.Accessible.FirstOrDefault(current => current.Id == projectName);

                if (projectContainer != null)
                {
                    var channels = _projectContainerToChannelsMap[projectContainer];
                    var channel = channels.FirstOrDefault(current => current.Id == channelName);

                    if (channel != null)
                    {
                        var dataset = channel.Datasets.FirstOrDefault(current => current.Name == datasetName);

                        if (dataset != null)
                            selectedDatasets.Add(dataset);
                    }
                }
            });
           
            this.RaisePropertyChanged(nameof(AppStateViewModel.ExportParameters));
        }

        public bool IsDatasetSeleced(DatasetInfoViewModel dataset)
        {
            return this.SelectedDatasets.Contains(dataset);
        }

        public void ToggleDatasetSelection(DatasetInfoViewModel dataset)
        {
            var isSelected = this.SelectedDatasets.Contains(dataset);

            if (isSelected)
                this.GetSelectedDatasets().Remove(dataset);
            else
                this.GetSelectedDatasets().Add(dataset);

            this.UpdateExportParameters();
            this.RaisePropertyChanged(nameof(AppStateViewModel.SelectedDatasets));
        }

        public long GetByteCount()
        {
            var totalDays = (this.DateTimeEnd - this.DateTimeBegin).TotalDays;
            var frequency = string.IsNullOrWhiteSpace(this.SampleRate) ? 0 : new SampleRateContainer(this.SampleRate).SamplesPerDay;

            return (long)this.GetSelectedDatasets().Sum(dataset =>
            {
                var elementSize = OneDasUtilities.SizeOf(dataset.DataType);

                return frequency * totalDays * elementSize;
            });
        }

        private void UpdateAttachments()
        {
            this.Attachments = null;

            if (this.ProjectContainer != null)
            {
                var folderPath = Path.Combine(_options.DataBaseFolderPath, "ATTACHMENTS", this.ProjectContainer.PhysicalName);

                if (Directory.Exists(folderPath))
                    this.Attachments = Directory.GetFiles(folderPath, "*").ToList();
            }
        }

        private void UpdateGroupedChannels()
        {
            if (this.ProjectContainer != null)
            {
                this.GroupedChannels = new Dictionary<string, List<ChannelInfoViewModel>>();

                foreach (var channel in _projectContainerToChannelsMap[this.ProjectContainer])
                {
                    if (this.ChannelMatchesFilter(channel))
                    {
                        var groupNames = channel.Group.Split('\n');

                        foreach (string groupName in groupNames)
                        {
                            var success = this.GroupedChannels.TryGetValue(groupName, out var group);

                            if (!success)
                            {
                                group = new List<ChannelInfoViewModel>();
                                this.GroupedChannels[groupName] = group;
                            }

                            group.Add(channel);
                        }
                    }
                }

                foreach (var entry in this.GroupedChannels)
                {
                    entry.Value.Sort((x, y) => x.Name.CompareTo(y.Name));
                }
            }

            if (this.GroupedChannels.Any())
                this.ChannelGroup = this.GroupedChannels.OrderBy(entry => entry.Key).First().Value;
            else
                this.ChannelGroup = null;
        }

        private void UpdateExportParameters()
        {
            this.ExportParameters.ChannelPaths = this.GetSelectedDatasets().Select(dataset =>
            {
                return $"{dataset.Parent.Parent.Id}/{dataset.Parent.Id}/{dataset.Name}";
            }).ToList();
        }

        private bool ChannelMatchesFilter(ChannelInfoViewModel channel)
        {
            if (string.IsNullOrWhiteSpace(this.SearchString))
                return true;

            if (channel.Name.Contains(this.SearchString, StringComparison.OrdinalIgnoreCase) 
             || channel.Description.Contains(this.SearchString, StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private void InitializeProjectContainerToChannelMap()
        {
            _projectContainerToChannelsMap = new Dictionary<ProjectContainer, List<ChannelInfoViewModel>>();

            foreach (var projectContainer in this.ProjectContainersInfo.Accessible)
            {
                _projectContainerToChannelsMap[projectContainer] = projectContainer.Project.Channels.Select(channel =>
                {
                    var channelMeta = projectContainer.ProjectMeta.Channels.First(channelMeta => channelMeta.Id == channel.Id);
                    return new ChannelInfoViewModel(channel, channelMeta);
                }).ToList();
            }
        }

        private void InitializeSampleRateToDatasetsMap()
        {
            _sampleRateToSelectedDatasetsMap = this.SampleRateValues.ToDictionary(sampleRate => sampleRate, sampleRate => new List<DatasetInfoViewModel>());
        }

        private List<DatasetInfoViewModel> GetSelectedDatasets()
        {
            var containsKey = !string.IsNullOrWhiteSpace(this.SampleRate) && _sampleRateToSelectedDatasetsMap.ContainsKey(this.SampleRate);

            if (containsKey)
                return _sampleRateToSelectedDatasetsMap[this.SampleRate];
            else
                return new List<DatasetInfoViewModel>();
        }

        private async Task<SplittedProjectContainers> SplitCampainContainersAsync(List<ProjectContainer> projectContainers,
                                                                                  List<string> restrictedProjects,
                                                                                  List<string> hiddenProjects)
        {
            var authState = await _authenticationStateProvider.GetAuthenticationStateAsync();
            var principal = authState.User;

            var accessible = projectContainers.Where(projectContainer =>
            {
                return Utilities.IsProjectAccessible(principal, projectContainer.Project.Id, restrictedProjects)
                    && Utilities.IsProjectVisible(principal, projectContainer.Project.Id, hiddenProjects);
            }).OrderBy(projectContainer => projectContainer.Id).ToList();

            var restricted = projectContainers.Where(projectContainer =>
            {
                return !Utilities.IsProjectAccessible(principal, projectContainer.Project.Id, restrictedProjects)
                    && Utilities.IsProjectVisible(principal, projectContainer.Project.Id, hiddenProjects);
            }).OrderBy(projectContainer => projectContainer.Id).ToList();

            return new SplittedProjectContainers(accessible, restricted);
        }

        #endregion

        #region Records

        public record SplittedProjectContainers(List<ProjectContainer> Accessible, List<ProjectContainer> Restricted);

        #endregion

        #region IDisposable Support

        private bool disposedValue = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _cts_download?.Cancel();
                    this.StateManager.PropertyChanged -= _propertyChanged;
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        #endregion
    }
}
