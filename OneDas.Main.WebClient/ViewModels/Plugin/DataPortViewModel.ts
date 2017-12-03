﻿class DataPortViewModel
{
    // fields
    public Name: KnockoutObservable<string>
    public readonly OneDasDataType: OneDasDataTypeEnum
    public readonly DataDirection: DataDirectionEnum

    public IsSelected: KnockoutObservable<boolean>
    public AssociatedChannelHubSet: KnockoutObservableArray<ChannelHubViewModel>
    public readonly AssociatedDataGateway: DataGatewayViewModelBase
    public readonly LiveDescription: KnockoutComputed<string>

    // constructors
    constructor(dataPortModel: any, associatedDataGateway: DataGatewayViewModelBase)
    {
        this.Name = ko.observable(dataPortModel.Name)
        this.OneDasDataType = dataPortModel.OneDasDataType
        this.DataDirection = dataPortModel.DataDirection

        this.IsSelected = ko.observable<boolean>(false)
        this.AssociatedChannelHubSet = ko.observableArray<ChannelHubViewModel>()
        this.AssociatedDataGateway = associatedDataGateway

        this.LiveDescription = ko.computed(() =>
        {
            let result: string

            result = "<div class='text-left'>" + this.Name() + "</div><div class='text-left'>" + EnumerationHelper.GetEnumLocalization('OneDasDataTypeEnum', this.OneDasDataType) + "</div>"

            if (this.AssociatedChannelHubSet().length > 0)
            {
                this.AssociatedChannelHubSet().forEach(channelHub =>
                {
                    result += "</br ><div class='text-left'>" + channelHub.Name() + "</div><div class='text-left'>" + EnumerationHelper.GetEnumLocalization('OneDasDataTypeEnum', channelHub.OneDasDataType()) + "</div>"
                })
            }

            return result
        })
    }

    // methods
    public GetId(): string
    {
        return this.Name()
    }

    public ToFullQualifiedIdentifier(): string
    {
        return this.AssociatedDataGateway.Description.Id + " (" + this.AssociatedDataGateway.Description.InstanceId + ") / " + this.GetId();
    }

    public ExtendModel(model: any)
    {
        //
    }

    public ToModel()
    {
        let model: any = {
            Name: <string>this.Name(),
            OneDasDataType: <OneDasDataTypeEnum>this.OneDasDataType,
            DataDirection: <DataDirectionEnum>this.DataDirection
        }

        this.ExtendModel(model)

        return model
    }

    public ResetAssociations(maintainWeakReference: boolean)
    {
        if (this.AssociatedChannelHubSet().length > 0)
        {
            this.AssociatedChannelHubSet().forEach(channelHub => channelHub.ResetAssociation(maintainWeakReference, this))
        }
    }
}