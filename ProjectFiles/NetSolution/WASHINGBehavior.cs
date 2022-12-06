#region Using directives
using System;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using QPlatform.HMIProject;
using QPlatform.NativeUI;
using QPlatform.Alarm;
using QPlatform.NetLogic;
using QPlatform.UI;
using QPlatform.CoreBase;
using QPlatform.Store;
using QPlatform.ODBCStore;
using QPlatform.S7TiaProfinet;
using QPlatform.S7TCP;
using QPlatform.Retentivity;
using QPlatform.CommunicationDriver;
using QPlatform.Core;
using QPlatform.EventLogger;
using QPlatform.SQLiteStore;
#endregion

[CustomBehavior]
public class WASHINGBehavior : BaseNetBehavior
{
    public override void Start()
    {
        // Insert code to be executed when the user-defined behavior is started
    }

    public override void Stop()
    {
        // Insert code to be executed when the user-defined behavior is stopped
    }

#region Auto-generated code, do not edit!
    protected new WASHING Node => (WASHING)base.Node;
#endregion
}
