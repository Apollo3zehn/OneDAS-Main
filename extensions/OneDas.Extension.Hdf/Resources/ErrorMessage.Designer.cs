﻿//------------------------------------------------------------------------------
// <auto-generated>
//     Dieser Code wurde von einem Tool generiert.
//     Laufzeitversion:4.0.30319.42000
//
//     Änderungen an dieser Datei können falsches Verhalten verursachen und gehen verloren, wenn
//     der Code erneut generiert wird.
// </auto-generated>
//------------------------------------------------------------------------------

namespace OneDas.Extension.Hdf {
    using System;
    
    
    /// <summary>
    ///   Eine stark typisierte Ressourcenklasse zum Suchen von lokalisierten Zeichenfolgen usw.
    /// </summary>
    // Diese Klasse wurde von der StronglyTypedResourceBuilder automatisch generiert
    // -Klasse über ein Tool wie ResGen oder Visual Studio automatisch generiert.
    // Um einen Member hinzuzufügen oder zu entfernen, bearbeiten Sie die .ResX-Datei und führen dann ResGen
    // mit der /str-Option erneut aus, oder Sie erstellen Ihr VS-Projekt neu.
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("System.Resources.Tools.StronglyTypedResourceBuilder", "15.0.0.0")]
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    internal class ErrorMessage {
        
        private static global::System.Resources.ResourceManager resourceMan;
        
        private static global::System.Globalization.CultureInfo resourceCulture;
        
        [global::System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        internal ErrorMessage() {
        }
        
        /// <summary>
        ///   Gibt die zwischengespeicherte ResourceManager-Instanz zurück, die von dieser Klasse verwendet wird.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        internal static global::System.Resources.ResourceManager ResourceManager {
            get {
                if (object.ReferenceEquals(resourceMan, null)) {
                    global::System.Resources.ResourceManager temp = new global::System.Resources.ResourceManager("OneDas.Extension.DataWriter.Hdf.Resources.ErrorMessage", typeof(ErrorMessage).Assembly);
                    resourceMan = temp;
                }
                return resourceMan;
            }
        }
        
        /// <summary>
        ///   Überschreibt die CurrentUICulture-Eigenschaft des aktuellen Threads für alle
        ///   Ressourcenzuordnungen, die diese stark typisierte Ressourcenklasse verwenden.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        internal static global::System.Globalization.CultureInfo Culture {
            get {
                return resourceCulture;
            }
            set {
                resourceCulture = value;
            }
        }
        
        /// <summary>
        ///   Sucht eine lokalisierte Zeichenfolge, die The current chunk has already been written before. A possible cause is sudden negative time change while OneDAS was not running. ähnelt.
        /// </summary>
        internal static string HdfWriter_ChunkAlreadyWritten {
            get {
                return ResourceManager.GetString("HdfWriter_ChunkAlreadyWritten", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Sucht eine lokalisierte Zeichenfolge, die Could not open or create file. ähnelt.
        /// </summary>
        internal static string HdfWriter_CouldNotOpenOrCreateFile {
            get {
                return ResourceManager.GetString("HdfWriter_CouldNotOpenOrCreateFile", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Sucht eine lokalisierte Zeichenfolge, die Could not write chunk (dataset). ähnelt.
        /// </summary>
        internal static string HdfWriter_CouldNotWriteChunk_Dataset {
            get {
                return ResourceManager.GetString("HdfWriter_CouldNotWriteChunk_Dataset", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Sucht eine lokalisierte Zeichenfolge, die Could not write chunk (dataset status). ähnelt.
        /// </summary>
        internal static string HdfWriter_CouldNotWriteChunk_Status {
            get {
                return ResourceManager.GetString("HdfWriter_CouldNotWriteChunk_Status", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Sucht eine lokalisierte Zeichenfolge, die Data type mismatch. ähnelt.
        /// </summary>
        internal static string HdfWriter_DataTypeMismatch {
            get {
                return ResourceManager.GetString("HdfWriter_DataTypeMismatch", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Sucht eine lokalisierte Zeichenfolge, die Element type must be primitive. ähnelt.
        /// </summary>
        internal static string HdfWriter_ElementTypeNonPrimitive {
            get {
                return ResourceManager.GetString("HdfWriter_ElementTypeNonPrimitive", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Sucht eine lokalisierte Zeichenfolge, die The HDF library is not thread safe. ähnelt.
        /// </summary>
        internal static string HdfWriter_HdfLibraryNotThreadSafe {
            get {
                return ResourceManager.GetString("HdfWriter_HdfLibraryNotThreadSafe", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Sucht eine lokalisierte Zeichenfolge, die The sample rate is too low. ähnelt.
        /// </summary>
        internal static string HdfWriter_SampleRateTooLow {
            get {
                return ResourceManager.GetString("HdfWriter_SampleRateTooLow", resourceCulture);
            }
        }
    }
}
