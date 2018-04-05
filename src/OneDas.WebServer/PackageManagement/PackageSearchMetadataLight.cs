﻿using System.Runtime.Serialization;

namespace OneDas.WebServer.PackageManagement
{
    [DataContract]
    public class PackageSearchMetadataLight
    {
        #region "Constructors"

        public PackageSearchMetadataLight(string packageId, string description, string version, bool isInstalled, bool isUpdateAvailable)
        {
            this.PackageId = packageId;
            this.Description = description;
            this.Version = version;
            this.IsInstalled = isInstalled;
            this.IsUpdateAvailable = isUpdateAvailable;
        }

        #endregion

        #region "Properties"

        [DataMember]
        string PackageId { get; }

        [DataMember]
        string Description { get; }

        [DataMember]
        string Version { get; }

        [DataMember]
        bool IsInstalled { get; }

        [DataMember]
        bool IsUpdateAvailable { get; }

        #endregion
    }
}
