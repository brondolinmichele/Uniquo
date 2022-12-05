#region Using directives
using System;
using UAManagedCore;
using QPlatform.HMIProject;
using QPlatform.UI;
using QPlatform.CoreBase;
using QPlatform.NetLogic;
using QPlatform.System;
using System.Collections.Generic;
using System.Diagnostics;
using QPlatform.OPCUAServer;
using QPlatform.WebUI;
using QPlatform.Datalogger;
#endregion

public class DateTimeConfiguratorLogic : BaseNetLogic
{
    public override void Start()
    {
        systemNode = LogicObject.GetAlias("SystemNode") as QPlatform.System.System;
        if (systemNode == null)
            throw new CoreConfigurationException("SystemNode not found");

        dateTimeConfiguratorLayout = Owner.Owner.GetObject("DateTimeConfiguratorLayout");
        if (dateTimeConfiguratorLayout == null)
            throw new CoreConfigurationException("DateTimeConfiguratorLayout not found");

        dateTimeConfigurationWidgetSectionsHandler = new DateTimeConfigurationWidgetSectionsHandler(dateTimeConfiguratorLayout);

        if (!PlatformCheckerHelper.IsLinuxAsemARM())
        {
            dateTimeConfigurationWidgetSectionsHandler.ButtonsBar.Get<Button>("Apply").Enabled = false;
            dateTimeConfigurationWidgetSectionsHandler.EnableSections(false);
            Log.Error("DateTimeConfiguratorLogic", "This widget supports only HMI50 ASEM devices");
            return;
        }

        isDateTimeConfigurationEmpty = IsDateTimeConfigurationEmpty();
        if (isDateTimeConfigurationEmpty)
        {
            dateTimeConfigurationWidgetSectionsHandler.ButtonsBar.Get<Button>("Apply").Enabled = false;
            dateTimeConfigurationWidgetSectionsHandler.EnableSections(false);
            return;
        }

        synchronizationModeComboBox = dateTimeConfigurationWidgetSectionsHandler.TimeSynchronizationSection.Get<ComboBox>("TimeSynchronizationModeComboBox");
        timeZonesEnumerationObject = dateTimeConfiguratorLayout.GetObject("TimeZonesEnumeration");

        lanCheckBox = dateTimeConfigurationWidgetSectionsHandler.LocalNTPServerInterfacesSection.Get<CheckBox>("LAN");
        wanCheckBox = dateTimeConfigurationWidgetSectionsHandler.LocalNTPServerInterfacesSection.Get<CheckBox>("WAN");
        wiFiCheckBox = dateTimeConfigurationWidgetSectionsHandler.LocalNTPServerInterfacesSection.Get<CheckBox>("WiFi");

        InitializeTimeZonesObject();

        editModelDateTimeConfiguration = InitializeDateTimeConfigurationEditModel();
        dateTimeConfiguratorLayout.Add(editModelDateTimeConfiguration);

        SetDynamicLinksFromEditModelToUI();
        InitializeLocalNTPServerInterfacesCheckBoxes();
        SetSectionsEnabled();

        synchronizationModeComboBox.SelectedValueVariable.VariableChange += SelectedValueVariable_VariableChange;

        lanCheckBox.CheckedVariable.VariableChange += CheckedInterfaceVariable_VariableChange;
        wanCheckBox.CheckedVariable.VariableChange += CheckedInterfaceVariable_VariableChange;
        wiFiCheckBox.CheckedVariable.VariableChange += CheckedInterfaceVariable_VariableChange;
    }

    public override void Stop()
    {
        if (!PlatformCheckerHelper.IsLinuxAsemARM())
            return;

        if (isDateTimeConfigurationEmpty)
            return;

        synchronizationModeComboBox.SelectedValueVariable.VariableChange -= SelectedValueVariable_VariableChange;

        lanCheckBox.CheckedVariable.VariableChange -= CheckedInterfaceVariable_VariableChange;
        wanCheckBox.CheckedVariable.VariableChange -= CheckedInterfaceVariable_VariableChange;
        wiFiCheckBox.CheckedVariable.VariableChange -= CheckedInterfaceVariable_VariableChange;

        dateTimeConfiguratorLayout.Remove(editModelDateTimeConfiguration);
    }

    [ExportMethod]
    public void ApplyDateTimeConfiguration()
    {
        var modelDateTimeConfiguration = systemNode.DateTimeConfiguration;
        modelDateTimeConfiguration.SynchronizationMode = editModelDateTimeConfiguration.SynchronizationMode;
        modelDateTimeConfiguration.RemoteNTPServer = editModelDateTimeConfiguration.RemoteNTPServer;
        modelDateTimeConfiguration.DateTime = editModelDateTimeConfiguration.DateTime;

        if (!AreEquivalentTimeZones(startingTimeZone, editModelDateTimeConfiguration.TimeZone))
            modelDateTimeConfiguration.TimeZone = editModelDateTimeConfiguration.TimeZone;

        modelDateTimeConfiguration.LocalNTPServerInterfaces = editModelDateTimeConfiguration.LocalNTPServerInterfaces;
    }

    private DateTimeConfiguration InitializeDateTimeConfigurationEditModel()
    {
        var editModelDateTimeConfiguration = InformationModel.MakeObject<DateTimeConfiguration>("EditModelDateTimeConfiguration");
        editModelDateTimeConfiguration.SynchronizationMode = systemNode.DateTimeConfiguration.SynchronizationMode;
        editModelDateTimeConfiguration.RemoteNTPServer = systemNode.DateTimeConfiguration.RemoteNTPServer;
        editModelDateTimeConfiguration.DateTime = systemNode.DateTimeConfiguration.DateTime;

        // Keep track of the starting timeZone. It is necessary to keep the same value when Apply is executed if no changes occurred.
        // Use case: timeZone1="Europe/Dublin" is configured, but the chosen representative in timeZonesMap is "Europe/London".
        // The timeZone is normalized to the equivalent "Europe/London" for internal uses.
        // During ApplyDateTimeConfiguration method the timezone is written only if it was changed, othewise the starting "Europe/Dublin" is maintained.
        startingTimeZone = systemNode.DateTimeConfiguration.TimeZone;
        var representativeTimeZone = GetTimeZoneRepresentative(startingTimeZone);
        if (string.IsNullOrEmpty(representativeTimeZone))
            throw new CoreConfigurationException($"Time zone {startingTimeZone} is not recognized as a valid time zone");

        editModelDateTimeConfiguration.TimeZone = representativeTimeZone;
        editModelDateTimeConfiguration.LocalNTPServerInterfaces = systemNode.DateTimeConfiguration.LocalNTPServerInterfaces;

        return editModelDateTimeConfiguration;
    }

    private string GetTimeZoneRepresentative(string timeZone)
    {
        // Get the representative timeZone for each group (by default the first element of the group)
        foreach(var timeZonePair in timeZonesMap)
        {
            var currentTimeZone = timeZonePair.Key;
            if (currentTimeZone.Contains(timeZone))
                return currentTimeZone[0];
        }

        return string.Empty;
    }

    private bool AreEquivalentTimeZones(string timeZone1, string timeZone2)
    {
        return GetTimeZoneRepresentative(timeZone1) == GetTimeZoneRepresentative(timeZone2);
    }

    private void SetDynamicLinksFromEditModelToUI()
    {
        synchronizationModeComboBox.SelectedValueVariable.ResetDynamicLink();
        synchronizationModeComboBox.SelectedValueVariable.SetDynamicLink(editModelDateTimeConfiguration.SynchronizationModeVariable, DynamicLinkMode.ReadWrite);

        // Remote NTP server
        var remoteNTPServerVariable = dateTimeConfigurationWidgetSectionsHandler.RemoteNTPServerSection.Get<TextBox>("RemoteNTPServerTextBox");
        remoteNTPServerVariable.TextVariable.ResetDynamicLink();
        remoteNTPServerVariable.TextVariable.SetDynamicLink(editModelDateTimeConfiguration.RemoteNTPServerVariable, DynamicLinkMode.ReadWrite);

        // Date and time
        var dateAndTimePicker = dateTimeConfigurationWidgetSectionsHandler.DateAndTimeSection.Get<DateTimePicker>("DateAndTimePicker");
        dateAndTimePicker.ValueVariable.ResetDynamicLink();
        dateAndTimePicker.ValueVariable.SetDynamicLink(editModelDateTimeConfiguration.DateTimeVariable, DynamicLinkMode.ReadWrite);

        // TimeZone
        var timeZoneComboBox = dateTimeConfigurationWidgetSectionsHandler.TimeZoneSection.Get<ComboBox>("TimeZoneComboBox");
        timeZoneComboBox.SelectedValueVariable.ResetDynamicLink();
        timeZoneComboBox.SelectedValueVariable.SetDynamicLink(editModelDateTimeConfiguration.TimeZoneVariable, DynamicLinkMode.ReadWrite);
    }

    private void InitializeLocalNTPServerInterfacesCheckBoxes()
    {
        var localNTPServerInterfacesArray = (Array)systemNode.DateTimeConfiguration.LocalNTPServerInterfaces;

        lanCheckBox.Checked = false;
        wanCheckBox.Checked = false;
        wiFiCheckBox.Checked = false;

        foreach (string localNTPServerInterface in localNTPServerInterfacesArray)
        {
            if (localNTPServerInterface == lanInterfaceName)
                lanCheckBox.Checked = true;
            else if (localNTPServerInterface == wanInterfaceName)
                wanCheckBox.Checked = true;
            else if (localNTPServerInterface == wiFiInterfaceName)
                wiFiCheckBox.Checked = true;
        }
    }

    private void CheckedInterfaceVariable_VariableChange(object sender, VariableChangeEventArgs e)
    {
        var listOfEnabledLocalNTPServerInterfaces = new List<string>();
        if (lanCheckBox.Checked)
            listOfEnabledLocalNTPServerInterfaces.Add(lanInterfaceName);

        if (wanCheckBox.Checked)
            listOfEnabledLocalNTPServerInterfaces.Add(wanInterfaceName);

        if (wiFiCheckBox.Checked)
            listOfEnabledLocalNTPServerInterfaces.Add(wiFiInterfaceName);

        editModelDateTimeConfiguration.LocalNTPServerInterfaces = listOfEnabledLocalNTPServerInterfaces.ToArray();
    }

    private void SelectedValueVariable_VariableChange(object sender, VariableChangeEventArgs e)
    {
        SetSectionsEnabled();
    }

    private void SetSectionsEnabled()
    {
        var selectedTimeSynchronizationMode = (UInt32)synchronizationModeComboBox.SelectedValueVariable.Value;
        dateTimeConfigurationWidgetSectionsHandler.DisableIncompatibleSections((TimeSynchronizationMode)selectedTimeSynchronizationMode);
    }

    private void InitializeTimeZonesObject()
    {
        int cnt = 0;
        foreach (var timeZonePair in timeZonesMap)
            AddTimeZone(timeZonePair.Key[0], timeZonePair.Value, cnt++);
    }

    private void AddTimeZone(string value, string displayName, int index)
    {
        var timeZoneVariable = InformationModel.MakeVariable("HMI50TimeZone" + index, UAManagedCore.OpcUa.DataTypes.String);
        timeZoneVariable.Value = value;

        var localeIds = Session.User.LocaleIds;
        if (localeIds.Length == 0)
            Log.Error("DateTimeConfiguratorLogic", "No locales found for the current user");

        foreach (var localeId in localeIds)
            timeZoneVariable.DisplayName = new LocalizedText("HMI50TimeZone" + index + "DisplayName", displayName, localeId);

        timeZonesEnumerationObject.Add(timeZoneVariable);
    }

    private bool IsDateTimeConfigurationEmpty()
    {
        var dateTimeConfiguration = systemNode.DateTimeConfiguration;

        return dateTimeConfiguration.SynchronizationMode == TimeSynchronizationMode.Manual &&
            string.IsNullOrEmpty(dateTimeConfiguration.RemoteNTPServer) &&
            dateTimeConfiguration.DateTime == minimumOpcUaDate &&
            string.IsNullOrEmpty(dateTimeConfiguration.TimeZone) &&
            dateTimeConfiguration.LocalNTPServerInterfaces.Length == 0;
    }

    private QPlatform.System.System systemNode;
    private DateTimeConfiguration editModelDateTimeConfiguration;

    private IUAObject dateTimeConfiguratorLayout;
    private DateTimeConfigurationWidgetSectionsHandler dateTimeConfigurationWidgetSectionsHandler;

    private ComboBox synchronizationModeComboBox;
    private CheckBox lanCheckBox;
    private CheckBox wanCheckBox;
    private CheckBox wiFiCheckBox;

    private string startingTimeZone;
    private IUAObject timeZonesEnumerationObject;

    // OPC UA DateTime minimum value is "1601-01-01T00:00:00.000Z". It indicates that the value was not set.
    private readonly DateTime minimumOpcUaDate = new DateTime(1601, 01, 01, 0, 0, 0, 0, DateTimeKind.Utc);

    private readonly string lanInterfaceName = "LAN";
    private readonly string wanInterfaceName = "WAN";
    private readonly string wiFiInterfaceName = "Wi-Fi";

    bool isDateTimeConfigurationEmpty = true;

    #region TimeZonesMap
    private readonly Dictionary<List<string>, string> timeZonesMap = new Dictionary<List<string>, string>()
    {
        { new List<string>(){"UTC"                                                                                               } , "(UTC) Coordinated Universal Time"},
        { new List<string>(){"Africa/Casablanca"                                                                                 } , "(UTC+00:00) Casablanca"},
        { new List<string>(){"Europe/London", "Europe/Dublin", "Europe/Lisbon", "GMT"                                            } , "(UTC+00:00) Dublin, Edinburgh, Lisbon, London"},
        { new List<string>(){"Africa/Monrovia", "Atlantic/Reykjavik"                                                             } , "(UTC+00:00) Monrovia, Reykjavik"},
        { new List<string>(){"CET", "Europe/Amsterdam", "Europe/Berlin", "Europe/Rome", "Europe/Stockholm", "Europe/Vienna"      } , "(UTC+01:00) Amsterdam, Berlin, Bern, Rome, Stockholm, Vienna"},
        { new List<string>(){"Europe/Budapest", "Europe/Belgrade", "Europe/Bratislava", "Europe/Ljubljana", "Europe/Prague"      } , "(UTC+01:00) Belgrade, Bratislava, Budapest, Ljubljana, Prague"},
        { new List<string>(){"Europe/Brussels", "Europe/Copenhagen", "Europe/Madrid", "Europe/Paris"                             } , "(UTC+01:00) Brussels, Copenhagen, Madrid, Paris"},
        { new List<string>(){"Europe/Belgrade", "Europe/Sarajevo", "Europe/Skopje", "Europe/Warsaw", "Europe/Zagreb"             } , "(UTC+01:00) Sarajevo, Skopje, Warsaw, Zagreb"},
        { new List<string>(){"Africa/Lagos"                                                                                      } , "(UTC+01:00) West Central Africa"},
        { new List<string>(){"Africa/Windhoek"                                                                                   } , "(UTC+01:00) Windhoek"},
        { new List<string>(){"Asia/Amman"                                                                                        } , "(UTC+02:00) Amman"},
        { new List<string>(){"Europe/Athens", "Europe/Bucharest"                                                                 } , "(UTC+02:00) Athens, Bucharest"},
        { new List<string>(){"Asia/Beirut"                                                                                       } , "(UTC+02:00) Beirut"},
        { new List<string>(){"Africa/Cairo"                                                                                      } , "(UTC+02:00) Cairo"},
        { new List<string>(){"Europe/Chisinau"                                                                                   } , "(UTC+02:00) Chisinau"},
        { new List<string>(){"Asia/Damascus"                                                                                     } , "(UTC+02:00) Damascus"},
        { new List<string>(){"Asia/Gaza", "Asia/Hebron"                                                                          } , "(UTC+02:00) Gaza, Hebron"},
        { new List<string>(){"Africa/Harare"                                                                                     } , "(UTC+02:00) Harare, Pretoria"},
        { new List<string>(){"Europe/Helsinki", "Europe/Kyiv", "Europe/Riga", "Europe/Sofia", "Europe/Tallinn", "Europe/Vilnius" } , "(UTC+02:00) Helsinki, Kyiv, Riga, Sofia, Tallinn, Vilnius"},
        { new List<string>(){"Asia/Jerusalem"                                                                                    } , "(UTC+02:00) Jerusalem"},
        { new List<string>(){"Africa/Tripoli"                                                                                    } , "(UTC+02:00) Tripoli"},
        { new List<string>(){"Europe/Kalining"                                                                                   } , "(UTC+02:00) Kaliningrad"},
        { new List<string>(){"Asia/Baghdad"                                                                                      } , "(UTC+03:00) Baghdad"},
        { new List<string>(){"Asia/Riyadh", "Asia/Kuwait"                                                                        } , "(UTC+03:00) Kuwait, Riyadh"},
        { new List<string>(){"Europe/Istanbul"                                                                                   } , "(UTC+03:00) Istanbul"},
        { new List<string>(){"Europe/Minsk"                                                                                      } , "(UTC+03:00) Minsk"},
        { new List<string>(){"Europe/Moscow", "Europe/Volgogra"                                                                  } , "(UTC+03:00) Moscow, St. Petersburg, Volgograd"},
        { new List<string>(){"Africa/Nairobi"                                                                                    } , "(UTC+03:00) Nairobi"},
        { new List<string>(){"Asia/Tehran"                                                                                       } , "(UTC+03:30) Tehran"},
        { new List<string>(){"Asia/Dubai"                                                                                        } , "(UTC+04:00) Abu Dhabi, Muscat"},
        { new List<string>(){"Europe/Astrakhan", "Europe/Ulyanovsk"                                                              } , "(UTC+04:00) Astrakhan, Ulyanovsk"},
        { new List<string>(){"Asia/Baku"                                                                                         } , "(UTC+04:00) Baku"},
        { new List<string>(){"Europe/Samara"                                                                                     } , "(UTC+04:00) Izhevsk, Samara"},
        { new List<string>(){"Indian/Mauritius"                                                                                  } , "(UTC+04:00) Port Louis"},
        { new List<string>(){"Asia/Tbilisi"                                                                                      } , "(UTC+04:00) Tbilisi"},
        { new List<string>(){"Asia/Yerevan"                                                                                      } , "(UTC+04:00) Yerevan"},
        { new List<string>(){"Asia/Kabul"                                                                                        } , "(UTC+04:30) Kabul"},
        { new List<string>(){"Asia/Yekaterinburg"                                                                                } , "(UTC+05:00) Ekaterinburg"},
        { new List<string>(){"Asia/Karachi"                                                                                      } , "(UTC+05:00) Islamabad, Karachi"},
        { new List<string>(){"Asia/Ashgabat", "Asia/Tashkent"                                                                    } , "(UTC+05:00) Ashgabat, Tashkent"},
        { new List<string>(){"Asia/Kolkata"                                                                                      } , "(UTC+05:30) Chennai, Kolkata, Mumbai, New Delhi"},
        { new List<string>(){"Asia/Colombo"                                                                                      } , "(UTC+05:30) Sri Jayawardenepura"},
        { new List<string>(){"Asia/Kathmandu"                                                                                    } , "(UTC+05:45) Kathmandu"},
        { new List<string>(){"Asia/Almaty"                                                                                       } , "(UTC+06:00) Astana"},
        { new List<string>(){"Asia/Dhaka"                                                                                        } , "(UTC+06:00) Dhaka"},
        { new List<string>(){"Asia/Omsk"                                                                                         } , "(UTC+06:00) Omsk"},
        { new List<string>(){"Asia/Yangon", "Asia/Rangoon"                                                                       } , "(UTC+06:30) Yangon (Rangoon)"},
        { new List<string>(){"Asia/Bangkok", "Asia/Jakarta"                                                                      } , "(UTC+07:00) Bangkok, Hanoi, Jakarta"},
        { new List<string>(){"Asia/Barnaul"                                                                                      } , "(UTC+07:00) Barnaul, Gorno-Altaysk"},
        { new List<string>(){"Asia/Hovd"                                                                                         } , "(UTC+07:00) Hovd"},
        { new List<string>(){"Asia/Krasnoyarsk"                                                                                  } , "(UTC+07:00) Krasnoyarsk"},
        { new List<string>(){"Asia/Novosibirsk"                                                                                  } , "(UTC+07:00) Novosibirsk"},
        { new List<string>(){"Asia/Tomsk"                                                                                        } , "(UTC+07:00) Tomsk"},
        { new List<string>(){"Asia/Hong_Kong", "Asia/Chongqing", "Asia/Urumqi"                                                   } , "(UTC+08:00) Beijing, Chongqing, Hong Kong, Urumqi"},
        { new List<string>(){"Asia/Irkutsk"                                                                                      } , "(UTC+08:00) Irkutsk"},
        { new List<string>(){"Asia/Singapore", "Asia/Kuala_Lumpur"                                                               } , "(UTC+08:00) Kuala Lumpur, Singapore"},
        { new List<string>(){"Australia/Perth"                                                                                   } , "(UTC+08:00) Perth"},
        { new List<string>(){"Asia/Taipei"                                                                                       } , "(UTC+08:00) Taipei"},
        { new List<string>(){"Asia/Ulaanbaatar"                                                                                  } , "(UTC+08:00) Ulaanbaatar"},
        { new List<string>(){"Asia/Pyongyang"                                                                                    } , "(UTC+08:30) Pyongyang"},
        { new List<string>(){"Australia/Eucla"                                                                                   } , "(UTC+08:45) Eucla"},
        { new List<string>(){"Asia/Chita"                                                                                        } , "(UTC+09:00) Chita"},
        { new List<string>(){"Asia/Tokyo"                                                                                        } , "(UTC+09:00) Osaka, Sapporo, Tokyo"},
        { new List<string>(){"Asia/Seoul"                                                                                        } , "(UTC+09:00) Seoul"},
        { new List<string>(){"Asia/Yakutsk"                                                                                      } , "(UTC+09:00) Yakutsk"},
        { new List<string>(){"Australia/Adelaide"                                                                                } , "(UTC+09:30) Adelaide"},
        { new List<string>(){"Australia/Darwin"                                                                                  } , "(UTC+09:30) Darwin"},
        { new List<string>(){"Australia/Brisbane"                                                                                } , "(UTC+10:00) Brisbane"},
        { new List<string>(){"Australia/Sydney", "Australia/Canberra", "Australia/Melbourne"                                     } , "(UTC+10:00) Canberra, Melbourne, Sydney"},
        { new List<string>(){"Pacific/Guam", "Pacific/Port_Moresby"                                                              } , "(UTC+10:00) Guam, Port Moresby"},
        { new List<string>(){"Australia/Hobart"                                                                                  } , "(UTC+10:00) Hobart"},
        { new List<string>(){"Asia/Vladivostok"                                                                                  } , "(UTC+10:00) Vladivostok"},
        { new List<string>(){"Australia/Lord_Howe"                                                                               } , "(UTC+10:30) Lord Howe Island"},
        { new List<string>(){"Pacific/Bougainville"                                                                              } , "(UTC+11:00) Bougainville Island"},
        { new List<string>(){"Asia/Srednekolymsk"                                                                                } , "(UTC+11:00) Chokurdakh"},
        { new List<string>(){"Asia/Magadan"                                                                                      } , "(UTC+11:00) Magadan"},
        { new List<string>(){"Pacific/Norfolk"                                                                                   } , "(UTC+11:00) Norfolk Island"},
        { new List<string>(){"Asia/Sakhalin"                                                                                     } , "(UTC+11:00) Sakhalin"},
        { new List<string>(){"Pacific/Noumea"                                                                                    } , "(UTC+11:00) Solomon Is., New Caledonia"},
        { new List<string>(){"Asia/Anadyr"                                                                                       } , "(UTC+12:00) Anadyr, Petropavlovsk-Kamchatsky"},
        { new List<string>(){"Pacific/Auckland"                                                                                  } , "(UTC+12:00) Auckland, Wellington"},
        { new List<string>(){"Pacific/Funafuti"                                                                                  } , "(UTC+12:00) Coordinated Universal Time +12"},
        { new List<string>(){"Pacific/Fiji"                                                                                      } , "(UTC+12:00) Fiji"},
        { new List<string>(){"Pacific/Chatham"                                                                                   } , "(UTC+12:45) Chatham Islands"},
        { new List<string>(){"Pacific/Tongatapu"                                                                                 } , "(UTC+13:00) Nuku'alofa"},
        { new List<string>(){"Pacific/Apia"                                                                                      } , "(UTC+13:00) Samoa"},
        { new List<string>(){"Pacific/Kiritimati"                                                                                } , "(UTC+14:00) Kiritimati Island"},
        { new List<string>(){"Atlantic/Azores"                                                                                   } , "(UTC-01:00) Azores"},
        { new List<string>(){"Atlantic/Cape_Verde"                                                                               } , "(UTC-01:00) Cabo Verde Is."},
        { new List<string>(){"America/Noronha"                                                                                   } , "(UTC-02:00) Coordinated Universal Time -02"},
        { new List<string>(){"America/Araguaina"                                                                                 } , "(UTC-03:00) Araguaina"},
        { new List<string>(){"America/Sao_Paulo"                                                                                 } , "(UTC-03:00) Brasilia"},
        { new List<string>(){"America/Buenos_Aires", "America/Argentina/Buenos_Aires"                                            } , "(UTC-03:00) Buenos Aires"},
        { new List<string>(){"America/Cayenne", "America/Fortaleza"                                                              } , "(UTC-03:00) Cayenne, Fortaleza"},
        { new List<string>(){"America/Montevideo"                                                                                } , "(UTC-03:00) Montevideo"},
        { new List<string>(){"America/Miquelon"                                                                                  } , "(UTC-03:00) Saint Pierre and Miquelon"},
        { new List<string>(){"America/St_Johns"                                                                                  } , "(UTC-03:30) Newfoundland"},
        { new List<string>(){"America/Asuncion"                                                                                  } , "(UTC-04:00) Asuncion"},
        { new List<string>(){"America/Halifax"                                                                                   } , "(UTC-04:00) Atlantic Time (Canada)"},
        { new List<string>(){"America/Caracas"                                                                                   } , "(UTC-04:00) Caracas"},
        { new List<string>(){"America/Cuiaba"                                                                                    } , "(UTC-04:00) Cuiaba"},
        { new List<string>(){"America/Manaus", "America/Argentina/San_Juan"                                                      } , "(UTC-04:00) Georgetown, La Paz, Manaus, San Juan"},
        { new List<string>(){"America/Godthab"                                                                                   } , "(UTC-03:00) Greenland"},
        { new List<string>(){"America/Santiago"                                                                                  } , "(UTC-04:00) Santiago"},
        { new List<string>(){"America/Grand_Turk"                                                                                } , "(UTC-04:00) Turks and Caicos"},
        { new List<string>(){"America/Bogota", "America/Lima", "America/Rio_Branco"                                              } , "(UTC-05:00) Bogota, Lima, Quito, Rio Branco"},
        { new List<string>(){"America/Cancun"                                                                                    } , "(UTC-05:00) Chetumal"},
        { new List<string>(){"Canada/Eastern"                                                                                    } , "(UTC-05:00) Eastern Time (US & Canada)"},
        { new List<string>(){"America/Havana"                                                                                    } , "(UTC-05:00) Havana"},
        { new List<string>(){"America/Port-au-Prince"                                                                            } , "(UTC-05:00) Haiti"},
        { new List<string>(){"America/Indiana/Indianapolis"                                                                      } , "(UTC-05:00) Indiana (East)"},
        { new List<string>(){"America/Chicago"                                                                                   } , "(UTC-06:00) Central America"},
        { new List<string>(){"America/Winnipeg"                                                                                  } , "(UTC-06:00) Central Time (US & Canada)"},
        { new List<string>(){"Pacific/Easter"                                                                                    } , "(UTC-06:00) Easter Island"},
        { new List<string>(){"America/Mexico_City"                                                                               } , "(UTC-06:00) Guadalajara, Mexico City, Monterrey"},
        { new List<string>(){"America/Regina"                                                                                    } , "(UTC-06:00) Saskatchewan"},
        { new List<string>(){"America/Phoenix"                                                                                   } , "(UTC-07:00) Arizona"},
        { new List<string>(){"America/Chihuahua", "America/Mazatlan"                                                             } , "(UTC-07:00) Chihuahua, La Paz, Mazatlan"},
        { new List<string>(){"America/Edmonton"                                                                                  } , "(UTC-07:00) Mountain Time (US & Canada)"},
        { new List<string>(){"America/Tijuana"                                                                                   } , "(UTC-08:00) Baja California"},
        { new List<string>(){"Pacific/Pitcairn"                                                                                  } , "(UTC-08:00) Coordinated Universal Time-08"},
        { new List<string>(){"America/Vancouver"                                                                                 } , "(UTC-08:00) Pacific Time (US & Canada)"},
        { new List<string>(){"America/Anchorage"                                                                                 } , "(UTC-09:00) Alaska"},
        { new List<string>(){"Pacific/Gambier"                                                                                   } , "(UTC-09:00) Coordinated Universal Time-09"},
        { new List<string>(){"Pacific/Marquesas"                                                                                 } , "(UTC-09:30) Marquesas Islands"},
        { new List<string>(){"America/Adak"                                                                                      } , "(UTC-10:00) Aleutian Islands"},
        { new List<string>(){"Pacific/Honolulu"                                                                                  } , "(UTC-10:00) Hawaii"},
        { new List<string>(){"Pacific/Pago_Pago"                                                                                 } , "(UTC-11:00) Coordinated Universal Time -11"}
    };
    #endregion

    private class DateTimeConfigurationWidgetSectionsHandler
    {
        public DateTimeConfigurationWidgetSectionsHandler(IUAObject widgetLayout)
        {
            this.widgetLayout = widgetLayout;
        }

        public RowLayout TimeSynchronizationSection
        {
            get
            {
                var timeSynchronizationSection = widgetLayout.Get<RowLayout>("TimeSynchronizationSection");
                if (timeSynchronizationSection == null)
                    throw new CoreConfigurationException("TimeSynchronizationSection object not found");

                return timeSynchronizationSection;
            }
        }

        public RowLayout RemoteNTPServerSection
        {
            get
            {
                var remoteNTPServerSection = widgetLayout.Get<RowLayout>("RemoteNTPServerSection");
                if (remoteNTPServerSection == null)
                    throw new CoreConfigurationException("RemoteNTPServerSection object not found");

                return remoteNTPServerSection;
            }
        }

        public RowLayout DateAndTimeSection
        {
            get
            {
                var dateAndTimeSection = widgetLayout.Get<RowLayout>("DateAndTimeSection");
                if (dateAndTimeSection == null)
                    throw new CoreConfigurationException("DateAndTimeSection object not found");

                return dateAndTimeSection;
            }
        }

        public RowLayout TimeZoneSection
        {
            get
            {
                var timeZoneSection = widgetLayout.Get<RowLayout>("TimeZoneSection");
                if (timeZoneSection == null)
                    throw new CoreConfigurationException("TimeZoneSection object not found");

                return timeZoneSection;
            }
        }

        public RowLayout LocalNTPServerInterfacesSection
        {
            get
            {
                var localNTPServerInterfacesSection = widgetLayout.Get<RowLayout>("LocalNTPServerInterfacesSection");
                if (localNTPServerInterfacesSection == null)
                    throw new CoreConfigurationException("LocalNTPServerInterfacesSection object not found");

                return localNTPServerInterfacesSection;
            }
        }

        public RowLayout ButtonsBar
        {
            get
            {
                var buttonsBar = widgetLayout.Get<RowLayout>("ButtonsBar");
                if (buttonsBar == null)
                    throw new CoreConfigurationException("ButtonsBar object not found");

                return buttonsBar;
            }
        }

        public void DisableIncompatibleSections(TimeSynchronizationMode timeSynchronizationMode)
        {
            EnableSections(true);

            if (timeSynchronizationMode == TimeSynchronizationMode.Auto)
                DateAndTimeSection.Enabled = false;
            else // Manual
                RemoteNTPServerSection.Enabled = false;
        }

        public void EnableSections(bool enabled)
        {
            TimeSynchronizationSection.Enabled = enabled;
            RemoteNTPServerSection.Enabled = enabled;
            DateAndTimeSection.Enabled = enabled;
            TimeZoneSection.Enabled = enabled;
            LocalNTPServerInterfacesSection.Enabled = enabled;
        }

        private readonly IUAObject widgetLayout;
    }

    #region Platform helper classes
    private static class PlatformCheckerHelper
    {
        public static bool IsLinuxAsemARM()
        {
            if (Environment.OSVersion.Platform != PlatformID.Unix)
                return false;

            if (machineInfo != MachineInfo.Undefined)
                return machineInfo == MachineInfo.ArmAsem;

            string architecture;
            try
            {
                architecture = LaunchProcess("uname", "-m");
            }
            catch (Exception exception)
            {
                Log.Error("DateTimeConfiguratorLogic", $"Unable to determine architecture: {exception.Message}");
                return false;
            }

            if (architecture.StartsWith("arm", StringComparison.InvariantCultureIgnoreCase))
                machineInfo = MachineInfo.ArmAsem;
            else
                machineInfo = MachineInfo.Other;

            return machineInfo == MachineInfo.ArmAsem;
        }

        private static string LaunchProcess(string processName, string parameter)
        {
            string output;
            ProcessStartInfo processStartInfo = new ProcessStartInfo
            {
                FileName = processName,
                UseShellExecute = false,
                Arguments = parameter,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using (Process process = Process.Start(processStartInfo))
            {
                output = process.StandardOutput.ReadToEnd().Trim();
                bool isSuccess = process.WaitForExit(5000);
                if (!isSuccess)
                    Log.Error("DateTimeConfiguratorLogic", "Command 'uname -m' timed out. It will be terminated");
            }

            return output;
        }

        private enum MachineInfo
        {
            Undefined = 0,
            ArmAsem = 1,
            Other = 2
        }

        private static MachineInfo machineInfo;
    }
    #endregion
}
