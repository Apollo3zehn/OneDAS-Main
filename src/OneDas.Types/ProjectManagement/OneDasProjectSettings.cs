﻿using OneDas.Extensibility;
using OneDas.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace OneDas.ProjectManagement
{
    [DataContract]
    public class OneDasProjectSettings
    {
        #region "Constructors"

        static OneDasProjectSettings()
        {
            OneDasProjectSettings.CurrentFormatVersion = 1; // first version
            OneDasProjectSettings.CurrentFormatVersion = 2; // 11.08.2017 (unit_set, transfer_function_set)
        }

        public OneDasProjectSettings(string primaryGroupName, string secondaryGroupName, string projectName): this(primaryGroupName, secondaryGroupName, projectName, new List<DataGatewayExtensionSettingsBase>(), new List<DataWriterExtensionSettingsBase>())
        {
            //
        }  

        public OneDasProjectSettings(string primaryGroupName, string projectSecondaryGroup, string projectName, IEnumerable<DataGatewayExtensionSettingsBase> dataGatewaySettingsSet, IEnumerable<DataWriterExtensionSettingsBase> dataWriterSettingsSet)
        {
            int nextId;
            
            this.Description = new OneDasProjectDescription(Guid.NewGuid(), 0, primaryGroupName, projectSecondaryGroup, projectName);
            this.ChannelHubSettingsSet = new List<ChannelHubSettings>();

            this.DataGatewaySettingsSet = dataGatewaySettingsSet;
            this.DataWriterSettingsSet = dataWriterSettingsSet;
            
            nextId = 1;
            this.DataGatewaySettingsSet.ToList().ForEach(settings => settings.Description.InstanceId = nextId++);

            nextId = 1;
            this.DataWriterSettingsSet.ToList().ForEach(settings => settings.Description.InstanceId = nextId++);

            this.FormatVersion = OneDasProjectSettings.CurrentFormatVersion;
        }

        #endregion

        #region "Properties"

        public static int CurrentFormatVersion { get; private set; }

        [DataMember]
        public int FormatVersion { get; set; }

        [DataMember]
        public OneDasProjectDescription Description { get; set; }

        [DataMember]
        public List<ChannelHubSettings> ChannelHubSettingsSet { get; private set; }

        [DataMember]
        public IEnumerable<DataGatewayExtensionSettingsBase> DataGatewaySettingsSet { get; private set; }

        [DataMember]
        public IEnumerable<DataWriterExtensionSettingsBase> DataWriterSettingsSet { get; private set; }

        #endregion

        #region "Methods"

        public OneDasProjectSettings Clone()
        {
            return (OneDasProjectSettings)this.MemberwiseClone();
        }

        public void Validate()
        {
            IEnumerable<Guid> guidSet;

            string errorDescription;

            // -> naming convention
            if (!OneDasUtilities.CheckNamingConvention(this.Description.PrimaryGroupName, out errorDescription, includeValue: true))
            {
                throw new Exception($"{ErrorMessage.OneDasProject_PrimaryGroupNameInvalid}: {errorDescription}");
            }

            if (!OneDasUtilities.CheckNamingConvention(this.Description.SecondaryGroupName, out errorDescription, includeValue: true))
            {
                throw new Exception($"{ErrorMessage.OneDasProject_SecondaryGroupNameInvalid}: {errorDescription}");
            }

            if (!OneDasUtilities.CheckNamingConvention(this.Description.ProjectName, out errorDescription, includeValue: true))
            {
                throw new Exception($"{ErrorMessage.OneDasProject_ProjectNameInvalid}: {errorDescription}");
            }

            if (!this.ChannelHubSettingsSet.ToList().TrueForAll(x => OneDasUtilities.CheckNamingConvention(x.Name, out errorDescription, includeValue: true)))
            {
                throw new Exception($"{ErrorMessage.OneDasProject_ChannelHubNameInvalid}: {errorDescription}");
            }

            // -> ChannelHub
            guidSet = this.ChannelHubSettingsSet.Select(x => x.Guid).ToList();

            if (guidSet.Count() > guidSet.Distinct().Count())
            {
                throw new Exception(ErrorMessage.OneDasProject_ChannelHubNotUnqiue);
            }

            // -> data gateway settings
            if (this.DataGatewaySettingsSet.Select(x => x.Description.InstanceId).Count() > this.DataGatewaySettingsSet.Select(x => x.Description.InstanceId).Distinct().Count())
            {
                throw new Exception(ErrorMessage.OneDasProject_DataGatewaySettingsIdNotUnique);
            }

            this.DataGatewaySettingsSet.ToList().ForEach(dataGatewaySettings => dataGatewaySettings.Validate());

            // -> data writer settings
            if (this.DataWriterSettingsSet.Select(x => x.Description.InstanceId).Count() > this.DataWriterSettingsSet.Select(x => x.Description.InstanceId).Distinct().Count())
            {
                throw new Exception(ErrorMessage.OneDasProject_DataWriterSettingsIdNotUnique);
            }

            this.DataWriterSettingsSet.ToList().ForEach(dataWriterSettings => dataWriterSettings.Validate());
        }

        #endregion
    }
}