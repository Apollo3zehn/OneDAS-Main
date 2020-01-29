﻿declare var moment: any

class AppViewModel
{
    public IsMainViewRequested: KnockoutObservable<boolean>
    public Campaigns: KnockoutObservableArray<CampaignInfoViewModel>
    public SelectedDatasets: KnockoutObservableArray<DatasetViewModel>
    public SampleRateSet: KnockoutObservableArray<string>
    public SelectedSampleRate: KnockoutObservable<string>
    public SelectedFileFormat: KnockoutObservable<FileFormatEnum>
    public SelectedFileGranularity: KnockoutObservable<FileGranularityEnum>
    public IsConnected: KnockoutObservable<boolean>
    public ExplorerState: KnockoutObservable<OneDasExplorerStateEnum>
    public InactivityMessage: KnockoutObservable<string>
    public ByteCount: KnockoutObservable<number>
    public Progress: KnockoutObservable<number>
    public Message: KnockoutObservable<string>
    public StartDate: KnockoutObservable<Date>
    public EndDate: KnockoutObservable<Date>   
    public DataAvailabilityStatistics: KnockoutObservable<DataAvailabilityStatisticsViewModel>
    public SelectedCampaignInfo: KnockoutObservable<CampaignInfoViewModel>
    public ShowChart: KnockoutObservable<boolean>

    public CanLoadData: KnockoutComputed<boolean>

    private _variableInfos: VariableViewModel[]
    private _datasetInfos: DatasetViewModel[]
    private _chart: Chart

    constructor(appModel: any)
    {
        let campaignModels: any = appModel.Campaigns;

        this.Campaigns = ko.observableArray<CampaignInfoViewModel>();
        this.SampleRateSet = ko.observableArray<string>()
        this.IsMainViewRequested = ko.observable<boolean>(true)
        this.SelectedDatasets = ko.observableArray<DatasetViewModel>()
        this.SelectedSampleRate = ko.observable<string>()
        this.SelectedFileFormat = ko.observable<FileFormatEnum>(FileFormatEnum.CSV)
        this.SelectedFileGranularity = ko.observable<FileGranularityEnum>(FileGranularityEnum.Hour)
        this.IsConnected = ko.observable<boolean>(true)
        this.ExplorerState = ko.observable<OneDasExplorerStateEnum>()
        this.InactivityMessage = ko.observable<string>("")
        this.ByteCount = ko.observable<number>(0)
        this.Progress = ko.observable<number>(0)
        this.Message = ko.observable<string>("")
        this.StartDate = ko.observable<Date>(moment().add(-1, 'days').startOf('day').toDate()).extend({ rateLimit: { timeout: 500, method: "notifyWhenChangesStop" } });
        this.EndDate = ko.observable<Date>(moment().add(0, 'days').startOf('day').toDate()).extend({ rateLimit: { timeout: 500, method: "notifyWhenChangesStop" } });
        this.DataAvailabilityStatistics = ko.observable<DataAvailabilityStatisticsViewModel>()
        this.SelectedCampaignInfo = ko.observable<CampaignInfoViewModel>()
        this.ShowChart = ko.observable<boolean>(false)

        this.CanLoadData = ko.computed<boolean>(() =>
            (this.StartDate().valueOf() < this.EndDate().valueOf()) &&
            this.SelectedDatasets().length > 0 &&
            this.SelectedFileGranularity() >= 86400 / this.GetSamplesPerDayFromString(this.SelectedSampleRate()) &&
            this.ExplorerState() === OneDasExplorerStateEnum.Idle &&
            this.IsConnected()
        )

        // enumeration description
        EnumerationHelper.Description["FileFormatEnum_CSV"] = "Comma-separated (*.csv)"
        EnumerationHelper.Description["FileFormatEnum_FAMOS"] = "imc FAMOS v2 (*.dat)"
        EnumerationHelper.Description["FileFormatEnum_MAT73"] = "Matlab v7.3 (*.mat)"
        EnumerationHelper.Description["FileFormatEnum_CSV2"] = "Comma-separated (unix time) (*.csv)"

        EnumerationHelper.Description["FileGranularityEnum_Minute_1"] = "1 file per minute"
        EnumerationHelper.Description["FileGranularityEnum_Minute_10"] = "1 file per 10 minutes"
        EnumerationHelper.Description["FileGranularityEnum_Hour"] = "1 file per hour"
        EnumerationHelper.Description["FileGranularityEnum_Day"] = "1 file per day"

        // state
        this.ExplorerState.subscribe(async (newValue) =>
        {
            let inactivityMessage: string

            if (newValue === OneDasExplorerStateEnum.Inactive)
            {
                inactivityMessage = await _broadcaster.invoke("GetInactivityMessage")

                this.InactivityMessage(inactivityMessage)
            }
            else
            {
                this.InactivityMessage("")
            }
        })

        this.ExplorerState(appModel.ExplorerState)

        // campaign info
        this.Campaigns.subscribe(newValue =>
        {
            this._variableInfos = MapMany(this.Campaigns(), campaignInfo => campaignInfo.Variables)
            this._datasetInfos = MapMany(this._variableInfos, variableInfo => variableInfo.Datasets)

            this.SelectedDatasets().forEach(datasetInfo => {
                let newDataSetInfo: DatasetViewModel

                newDataSetInfo = this._datasetInfos.find(current => current.Parent.Name === datasetInfo.Parent.Name && current.Name === datasetInfo.Name)

                if (newDataSetInfo) {
                    newDataSetInfo.IsSelected(true)
                    console.log("selected " + newDataSetInfo.Parent.Name + " " + newDataSetInfo.Name)
                }
            })

            this.Campaigns().forEach(campaignInfo => {
                campaignInfo.Variables.forEach(variableInfo => {
                    variableInfo.Datasets.forEach(datasetInfo => {
                        datasetInfo.OnIsSelectedChanged.subscribe((sender, isSelected) => {
                            this.UpdateSelectedDatasets()
                        })
                    })
                })
            })

            this.SelectedSampleRate(null)

            //                                                         temporarily disable 10 min averages due to incompatibility with 60s chunks
            this.SampleRateSet([...new Set(this._datasetInfos.filter(datasetInfo => !datasetInfo.Name.includes("600 s")).map(datasetInfo => datasetInfo.Name.split("_")[0]))].sort((a, b) => {
                switch (true) {
                    case a.includes('Hz') && !b.includes('Hz'):
                        return -1;
                    case !a.includes('Hz') && b.includes('Hz'):
                        return 1;
                    case a.includes('Hz') && b.includes('Hz') || !a.includes('Hz') && !b.includes('Hz'):

                        if (a.includes('Hz')) {
                            switch (true) {
                                case parseFloat(a) < parseFloat(b):
                                    return 1
                                case parseFloat(a) > parseFloat(b):
                                    return -1
                                default:
                                    return 0
                            }
                        }
                        else {
                            switch (true) {
                                case parseFloat(a) < parseFloat(b):
                                    return -1
                                case parseFloat(a) > parseFloat(b):
                                    return 1
                                default:
                                    return 0
                            }
                        }
                }
            }))
        })

        this.Campaigns(campaignModels.map(campaignModel => new CampaignInfoViewModel(campaignModel)));

        // start / end date
        let startDate: Date
        let endDate: Date

        startDate = new Date()
        startDate.setHours(0, 0, 0, 0);
        startDate = addDays(startDate, -1);

        endDate = new Date()
        endDate.setHours(0, 0, 0, 0);

        this.StartDate(startDate)
        this.EndDate(endDate)
      
        // sample rate
        this.SelectedSampleRate.subscribe(newValue => {
            this._variableInfos.forEach(variableInfo => {
                variableInfo.Datasets.forEach(datasetInfo => {
                    datasetInfo.IsVisible(datasetInfo.Name.split("_")[0] === this.SelectedSampleRate() && !datasetInfo.Name.endsWith("status"))
                })
            })

            this.UpdateSelectedDatasets()
        })

        // chart
        this.StartDate.subscribe(async (newValue) =>
        {
            if (!this.IsMainViewRequested())
            {
                this.PrepareChart()
            }
        })

        this.EndDate.subscribe(async (newValue) =>
        {
            if (!this.IsMainViewRequested())
            {
                this.PrepareChart()
            }
        })

        this.IsMainViewRequested.subscribe(newValue =>
        {
            if (!newValue)
            {
                this.PrepareChart()
            }
        })

        this.DataAvailabilityStatistics.subscribe(newValue =>
        {
            if (newValue && newValue.Data.length > 0)
            {
                let context: any

                this.ShowChart(true)
                context = document.getElementById("chart_data_availability");
                this._chart = this.CreateChart(context, newValue)
            }
            else
            {
                this.ShowChart(false)
            }
        })

        // callback
        _broadcaster.on("SendState", (explorerState: OneDasExplorerStateEnum) =>
        {
            this.ExplorerState(explorerState)
        })

        _broadcaster.on("SendProgress", (progress: number, message: string) =>
        {
            this.Progress(progress)
            this.Message(message)
        })

        _broadcaster.on("SendByteCount", (byteCount: number) =>
        {
            this.ByteCount(byteCount)
        })
    }  

    // methods
    private GetSamplesPerDayFromString = (datasetName: string) =>
    {
        if (!datasetName)
        {
            return 0;
        }

        // Hz
        var regexHz = /([0-9|\.]+)\sHz/;
        var matchHz = regexHz.exec(datasetName);

        if (matchHz)
            return 86400 * Number.parseInt(matchHz[1]);

        // s
        var regexT = /([0-9|\.]+)\ss/;
        var matchT = regexT.exec(datasetName);

        if (matchT)
            return 86400 / Number.parseInt(matchT[1]);

        // else
        throw new Error("DatasetName cannot be converted to samples per day.");
    }

    private RemoveTimeZoneOffset = (date: Date) =>
    {
        return moment(date).add(<any>moment(date).utcOffset(), "minute").toDate()
    }

    private PrepareChart = async () =>
    {
        if (this._chart)
        {
            this._chart.destroy()
        }

        try
        {
            this.DataAvailabilityStatistics(await _broadcaster.invoke("GetDataAvailabilityStatistics",
                                                                    this.SelectedCampaignInfo().Name,
                                                                    this.RemoveTimeZoneOffset(this.StartDate()),
                                                                    this.RemoveTimeZoneOffset(this.EndDate())))
        } catch (e)
        {
            alert(e.message)
        }
    }

    private CreateChart = (context: any, dataAvailabilityStatistics: DataAvailabilityStatisticsViewModel) =>
    {
        let xLabels: any[]
        let data: any[]
        let dateFormat: string
        let yLabel: string
        let yLimit: number
        let showYAxis: boolean

        xLabels = []
        data = []

        switch (dataAvailabilityStatistics.Granularity)
        {
            case DataAvailabilityGranularityEnum.ChunkLevel:

                for (var i = 0; i < dataAvailabilityStatistics.Data.length; i++)
                {
                    xLabels.push(moment(this.StartDate()).add(i, "minutes"))
                }

                for (var i = 0; i < dataAvailabilityStatistics.Data.length; i++)
                {
                    data.push(dataAvailabilityStatistics.Data[i])
                }

                yLabel = "availability"
                yLimit = 1.3
                showYAxis = false
                dateFormat = "DD.MM  -  HH:mm"

                break

            case DataAvailabilityGranularityEnum.DayLevel:

                for (var i = 0; i < dataAvailabilityStatistics.Data.length; i++)
                {
                    xLabels.push(moment(this.StartDate()).add(i, "days"))
                }

                for (var i = 0; i < dataAvailabilityStatistics.Data.length; i++)
                {
                    data.push(dataAvailabilityStatistics.Data[i])
                }

                yLabel = "availability in %"
                yLimit = 120
                showYAxis = true
                dateFormat = "DD.MM"

                break

            case DataAvailabilityGranularityEnum.MonthLevel:

                for (var i = 0; i < dataAvailabilityStatistics.Data.length; i++)
                {
                    xLabels.push(moment(this.StartDate()).add(i, "months"))
                }

                for (var i = 0; i < dataAvailabilityStatistics.Data.length; i++)
                {
                    data.push(dataAvailabilityStatistics.Data[i])
                }

                yLabel = "availability in %"
                yLimit = 120
                showYAxis = true
                dateFormat = "MMM YYYY"

                break

            default:
                new Error("not supported")
        }

        return new Chart(context,
            {
                type: "line",
                data: {
                    labels: xLabels,
                    datasets: [{
                        data: data,
                        backgroundColor: "rgba(75, 192, 192, 0.1)",
                        borderColor: "rgba(75, 192, 192)",
                        borderWidth: 1,
                        lineTension: 0.25,
                        pointRadius: 0
                    }]
                },
                options: {
                    legend: {
                        display: false
                    },
                    scales: {
                        xAxes: [{
                            display: true,
                            scaleLabel: {
                                display: true,
                                labelString: 'date / time'
                            },
                            ticks: {
                                autoSkip: true,
                                minRotation: 45,
                                maxRotation: 45,
                            },
                            time: {
                                displayFormats: {
                                    "minute": dateFormat,
                                    "hour": dateFormat,
                                    "day": dateFormat,
                                    "month": dateFormat,
                                    "year": dateFormat
                                }
                            },
                            type: "time",
                        }],
                        yAxes: [{
                            display: showYAxis,
                            position: "left",
                            scaleLabel: {
                                display: true,
                                labelString: yLabel
                            },
                            ticks: <any>{
                                beginAtZero: true,
                                max: yLimit
                            },
                            type: "linear"                           
                        }]
                    },
                    title: {
                        display: true,
                        fontColor: "#555",
                        fontSize: 17,
                        fontStyle: "",
                        padding: 25,
                        text: "Data availability of " + this.SelectedCampaignInfo().GetDisplayName()
                    },
                    tooltips: {
                        enabled: false
                    }
                }
            })
    }

    private UpdateSelectedDatasets = (() =>
    {
        this.SelectedDatasets(this._datasetInfos.filter(datasetInfo => datasetInfo.IsVisible() && datasetInfo.IsSelected()))
    })

    // commands
    public InitializeDatePicker = () =>
    {
        (<any>$("#start-date")).datetimepicker(
            {
                format: "DD/MM/YYYY HH:mm",
                minDate: new Date("2000-01-01T00:00:00.000Z"),
                maxDate: new Date("2030-01-01T00:00:00.000Z"),
                defaultDate: this.StartDate(),
                ignoreReadonly: true,
                //calendarWeeks: true // not working
            }
        );

        (<any>$("#end-date")).datetimepicker(
            {
                format: "DD/MM/YYYY HH:mm",
                minDate: new Date("2000-01-01T00:00:00.000Z"),
                maxDate: new Date("2030-01-01T00:00:00.000Z"),
                defaultDate: this.EndDate(),
                ignoreReadonly: true,
                //calendarWeeks: true // not working
            }
        );

        (<any>$("#start-date")).on("change.datetimepicker", e =>
        {
            (<any>$('#end-date')).datetimepicker('minDate', e.date);

            this.StartDate(e.date)
        });

        (<any>$("#end-date")).on("change.datetimepicker", e =>
        {
            (<any>$('#start-date')).datetimepicker('maxDate', e.date);

            this.EndDate(e.date)
        });
    }

    public ShowDataAvailability = (campaignInfo: CampaignInfoViewModel) =>
    {
        this.SelectedCampaignInfo(campaignInfo)
        this.IsMainViewRequested(false)
    }

    public ShowMainView = () =>
    {
        this.SelectedCampaignInfo(null)
        this.IsMainViewRequested(true)
    }

    public CancelLoadData = () =>
    {
        _broadcaster.invoke("CancelGetData")
    }

    public LoadData = async () =>
    {
        let campaignInfoSet: Map<string, Map<string, string[]>>
        let variableInfos: Map<string, string[]>
        let datasetInfos: string[]
        let downloadLink: string

        campaignInfoSet = new Map<string, Map<string, string[]>>()

        this.SelectedDatasets().forEach(datasetInfo =>
        {
            if (campaignInfoSet.has(datasetInfo.Parent.Parent.Name))
            {
                variableInfos = campaignInfoSet.get(datasetInfo.Parent.Parent.Name)
            }
            else
            {
                variableInfos = new Map<string, string[]>();
                campaignInfoSet.set(datasetInfo.Parent.Parent.Name, variableInfos)
            }

            if (variableInfos.has(datasetInfo.Parent.Name))
            {
                datasetInfos = variableInfos.get(datasetInfo.Parent.Name)
            }
            else
            {
                datasetInfos = [];
                variableInfos.set(datasetInfo.Parent.Name, datasetInfos)
            }

            datasetInfos.push(datasetInfo.Name)
        })

        try
        {
            this.Progress(0)
            this.Message("")

            downloadLink = await _broadcaster.invoke(
                "GetData",
                this.RemoveTimeZoneOffset(this.StartDate()),
                this.RemoveTimeZoneOffset(this.EndDate()),
                this.SelectedSampleRate(),
                this.SelectedFileFormat(),
                this.SelectedFileGranularity(),
                campaignInfoSet
            )

            if (downloadLink !== "")
            {
                window.open(downloadLink);
            }
        } catch (e)
        {
            alert(e.message)
        }
    }
}