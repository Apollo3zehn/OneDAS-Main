﻿let ViewModelConstructor = (model: any, identification: ExtensionIdentificationViewModel) => new ModbusRtuServerViewModel(model, identification)

class ModbusRtuServerViewModel extends ModbusServerViewModel
{
    public UnitIdentifier: KnockoutObservable<number>
    public Port: KnockoutObservable<string>
    public BaudRate: KnockoutObservable<number>
    public Handshake: KnockoutObservable<HandshakeEnum>
    public Parity: KnockoutObservable<ParityEnum>
    public StopBits: KnockoutObservable<StopBitsEnum>

    public HandshakeSet: KnockoutObservableArray<HandshakeEnum>
    public ParitySet: KnockoutObservableArray<ParityEnum>
    public StopBitsSet: KnockoutObservableArray<StopBitsEnum>

    constructor(model, identification: ExtensionIdentificationViewModel)
    {
        super(model, identification)

        EnumerationHelper.Description["HandshakeEnum_None"] = "no control"
        EnumerationHelper.Description["HandshakeEnum_XOnXOff"] = "XON/XOFF software control"
        EnumerationHelper.Description["HandshakeEnum_RequestToSend"] = "RTS hardware control"
        EnumerationHelper.Description["HandshakeEnum_RequestToSendXOnXOff"] = "XON/XOFF and RTS control"

        EnumerationHelper.Description["ParityEnum_None"] = "No parity"
        EnumerationHelper.Description["ParityEnum_Odd"] = "Odd"
        EnumerationHelper.Description["ParityEnum_Even"] = "Even"


        EnumerationHelper.Description["StopBitsEnum_None"] = "0"
        EnumerationHelper.Description["StopBitsEnum_One"] = "1"
        EnumerationHelper.Description["StopBitsEnum_Two"] = "2"
        EnumerationHelper.Description["StopBitsEnum_OnePointFive"] = "1.5"

        this.UnitIdentifier = ko.observable<number>(model.UnitIdentifier)
        this.Port = ko.observable<string>(model.Port)
        this.BaudRate = ko.observable<number>(model.BaudRate)
        this.Handshake = ko.observable<HandshakeEnum>(model.Handshake)
        this.Parity = ko.observable<ParityEnum>(model.Parity)
        this.StopBits = ko.observable<StopBitsEnum>(model.StopBits)

        this.HandshakeSet = ko.observableArray<HandshakeEnum>(EnumerationHelper.GetEnumValues("HandshakeEnum"))
        this.ParitySet = ko.observableArray<ParityEnum>(EnumerationHelper.GetEnumValues("ParityEnum"))
        this.StopBitsSet = ko.observableArray<StopBitsEnum>(EnumerationHelper.GetEnumValues("StopBitsEnum"))
    }

    public ExtendModel(model: any)
    {
        super.ExtendModel(model)

        model.UnitIdentifier = <number>this.UnitIdentifier()
        model.Port = <string>this.Port()
        model.BaudRate = <number>this.BaudRate()
        model.Handshake = <HandshakeEnum>this.Handshake()
        model.Parity = <ParityEnum>this.Parity()
        model.StopBits = <StopBitsEnum>this.StopBits()
    }
}