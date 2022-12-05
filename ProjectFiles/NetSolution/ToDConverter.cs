#region Using directives
using System;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using QPlatform.HMIProject;
using QPlatform.Retentivity;
using QPlatform.NativeUI;
using QPlatform.UI;
using QPlatform.S7TCP;
using QPlatform.S7TiaProfinet;
using QPlatform.NetLogic;
using QPlatform.CommunicationDriver;
using QPlatform.CoreBase;
using QPlatform.Core;
using QPlatform.Store;
using QPlatform.ODBCStore;
using QPlatform.EventLogger;
using QPlatform.SQLiteStore;
using QPlatform.System;
using QPlatform.OPCUAServer;
using QPlatform.WebUI;
using QPlatform.Datalogger;
#endregion

public class ToDConverter : BaseNetLogic
{

    public override void Start()
    {
        DTFromPLCToHMI();
    }

    public override void Stop()
    {
        // Insert code to be executed when the user-defined logic is stopped
    }

    public void DTFromPLCToHMI()
    {
        DateTimePicker dtp = (DateTimePicker)Owner;
        dtp.Value = DateTime.Today.Add(TimeSpan.FromSeconds(dtp.GetVariable("PlcValue").Value / 1000));
    }
    
    [ExportMethod]
    public void DTFromHMIToPLC()
    {
        DateTimePicker dtp = (DateTimePicker)Owner;
        dtp.GetVariable("PlcValue").Value = dtp.Value.TimeOfDay.TotalMilliseconds;
    }
}
