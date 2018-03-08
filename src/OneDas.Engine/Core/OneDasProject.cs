﻿using OneDas.Plugin;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OneDas.Engine.Core
{
    public class OneDasProject
    {
        #region "Fields"

        OneDasProjectSettings _settings;

        #endregion

        #region "Constructors"

        public OneDasProject(IPluginProvider pluginProvider, OneDasProjectSettings settings)
        {
            _settings = settings;

            this.DataGatewaySet = this.Settings.DataGatewaySettingsSet.Select(pluginSettings => pluginProvider.BuildLogic<DataGatewayPluginLogicBase>(pluginSettings)).ToList();
            this.DataWriterSet = this.Settings.DataWriterSettingsSet.Select(pluginSettings => pluginProvider.BuildLogic<DataWriterPluginLogicBase>(pluginSettings)).ToList();
            this.ChannelHubSet = this.Settings.ChannelHubSettingsSet.Select(channelHubSettings => this.CreateChannelHub(channelHubSettings)).ToList();

            this.UpdateMapping();
        }

        #endregion

        #region "Properties"

        public List<DataGatewayPluginLogicBase> DataGatewaySet { get; private set; }

        public List<DataWriterPluginLogicBase> DataWriterSet { get; private set; }

        public List<ChannelHubBase> ChannelHubSet { get; private set; }

        public List<ChannelHubBase> ActiveChannelHubSet { get; private set; }

        public OneDasProjectSettings Settings
        {
            get
            {
                return _settings.Clone();
            }
        }

        #endregion

        #region "Methods"

        private ChannelHubBase CreateChannelHub(ChannelHubSettings channelHubSettings)
        {
            Type type;

            type = typeof(ChannelHub<>).MakeGenericType(new Type[] { OneDasUtilities.GetTypeFromOneDasDataType(channelHubSettings.DataType) });

            return (ChannelHubBase)Activator.CreateInstance(type, channelHubSettings);
        }

        private void UpdateMapping()
        {
            IEnumerable<DataPort> dataPortSet;
            IDictionary<DataPort, string> dataPortIdMap;

            // assign correct data-gateway instances to all data ports
            this.DataGatewaySet.ForEach(dataGateway =>
            {
                dataGateway.GetDataPortSet().ToList().ForEach(dataPort =>
                {
                    dataPort.AssociatedDataGateway = dataGateway;
                });
            });

            // get data ports
            dataPortSet = this.DataGatewaySet.SelectMany(x => x.GetDataPortSet()).ToList();

            // generate unique identifiers for each data port
            dataPortIdMap = dataPortSet.ToDictionary(x => x, x =>
            {
                return $"{ x.AssociatedDataGateway.Settings.Description.Id } ({ x.AssociatedDataGateway.Settings.Description.InstanceId }) / { x.GetId() }";
            });

            // update mapping
            this.ChannelHubSet.ToList().ForEach(channelHub =>
            {
                string inputId = channelHub.Settings.AssociatedDataInputId;

                if (!string.IsNullOrWhiteSpace(inputId))
                {
                    DataPort foundDataPort = dataPortSet.FirstOrDefault(dataPort => dataPortIdMap[dataPort] == inputId);

                    if (foundDataPort != null && this.IsAssociationAllowed(foundDataPort, channelHub.Settings))
                    {
                        channelHub.SetAssociation(foundDataPort);
                    }
                }

                foreach (string outputId in channelHub.Settings.AssociatedDataOutputIdSet)
                {
                    DataPort foundDataPort = dataPortSet.FirstOrDefault(dataPort => dataPortIdMap[dataPort] == outputId);

                    if (foundDataPort != null && this.IsAssociationAllowed(foundDataPort, channelHub.Settings))
                    {
                        channelHub.SetAssociation(foundDataPort);
                    }
                }
            });

            this.ActiveChannelHubSet = this.ChannelHubSet.Where(channelHub => channelHub.AssociatedDataInput != null).ToList();
        }

        private bool IsAssociationAllowed(DataPort dataPort, ChannelHubSettings channelHub)
        {
            return OneDasUtilities.GetBitLength(dataPort.DataType, true) == OneDasUtilities.GetBitLength(channelHub.DataType, true);
        }

        #endregion

        #region "IDisposable Support"

        private bool isDisposed;

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!isDisposed)
            {
                if (disposing)
                {
                    this.DataGatewaySet.ForEach(dataGateway => dataGateway.Dispose());
                    this.DataWriterSet.ForEach(dataWriter => dataWriter.Dispose());
                }
            }

            isDisposed = true;
        }

        #endregion
    }
}