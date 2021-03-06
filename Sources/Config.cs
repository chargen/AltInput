﻿/*
 * AltInput: Alternate input plugin for Kerbal Space Program
 * Copyright © 2014 Pete Batard <pete@akeo.ie>
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <http://www.gnu.org/licenses/>.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Ini;

using UnityEngine;
// IMPORTANT: To be able to work with Unity, which is *BROKEN*,
// you must have a patched version of SharpDX.DirectInput such
// as the one provided with this project (in Libraries/)
// See: https://github.com/sharpdx/SharpDX/issues/406
using SharpDX.DirectInput;

namespace AltInput
{
    /// <summary>
    /// Handles the input device configuration
    /// </summary>
    // NOTE: Users of the B9 or other conflicting plugins may want to use
    // 'KSPAddon.Startup.Instantly' instead of 'KSPAddon.Startup.MainMenu'
    // below and recompile to get AltInput loaded before those plugins.
    // This is not enabled by default because:
    // 1. Doing so removes the ability to reload the config.ini by going
    //    through the menu, which is an important feature.
    // 2. You also lose the ability to see the AltInput debug output as
    //    the last one on the debug console in the menu. If Instantly
    //    is used, you need to go outside the game to check the log...
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    public class Config : MonoBehaviour
    {
        private static readonly String Location = System.Reflection.Assembly.GetExecutingAssembly().Location;
        public static readonly String ini_path = Location.Substring(0, Location.LastIndexOf('\\')) +
            @"\PluginData\AltInput\config.ini";
        public static readonly System.Version dllVersion = typeof(AltDevice).Assembly.GetName().Version;
        public static readonly System.Version currentVersion = new System.Version("1.4");
        public static System.Version iniVersion;
        // Good developers do NOT let end-users fiddle with XML configuration files...
        public static IniFile ini = null;
        private DirectInput directInput = new DirectInput();
        private static readonly char[] Separators = { '[', ']', ' ', '\t' };
        public static List<AltDevice> DeviceList = new List<AltDevice>();
        public static List<String> DetectedList = new List<String>();

        private void ParseMapping(String Section, String Name, AltMapping[] Mapping, int mode)
        {
            // Try to read a mapping from the common/.Flight section first
            var ConfigData = ini.IniReadValue(Section, Name);
            if ((mode != 0) && (ConfigData == ""))
                ConfigData = ini.IniReadValue(Section + "." + GameState.ModeName[0], Name);
            // Then check for an override
            var Override = ini.IniReadValue(Section + "." + GameState.ModeName[mode], Name);
            if (Override != "")
                ConfigData = Override;
            try
            {
                String[] MappingData = ConfigData.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
                if (MappingData[0].EndsWith(".Delta"))
                {
                    Mapping[mode].Type = MappingType.Delta;
                    Mapping[mode].Action = MappingData[0].Remove(MappingData[0].IndexOf(".Delta"));
                    float.TryParse(MappingData[1], out Mapping[mode].Value);
                }
                else if (MappingData.Length == 1)
                {
                    Mapping[mode].Type = MappingType.Range;
                    Mapping[mode].Action = MappingData[0];
                }
                else
                {
                    Mapping[mode].Type = MappingType.Absolute;
                    Mapping[mode].Action = MappingData[0];
                    float.TryParse(MappingData[1], out Mapping[mode].Value);
                }
            } catch (Exception) { }
        }

        private void ParseControl(String Section, String Name, AltControl[] Control, int mode)
        {
            String s;

            // Try to read the inverted attribute from the common/.Flight section first
            s = ini.IniReadValue(Section, Name + ".Inverted");
            Boolean.TryParse(s, out Control[mode].Inverted);
            if ((mode != 0) && (s == ""))
            {
                s = ini.IniReadValue(Section + "." + GameState.ModeName[0], Name + ".Inverted");
                if (s != "")
                    Boolean.TryParse(s, out Control[mode].Inverted);
            }
            // Then check for an override
            var Override = ini.IniReadValue(Section + "." + GameState.ModeName[mode], Name + ".Inverted");
            if (Override != "")
                Boolean.TryParse(Override, out Control[mode].Inverted);

            // Check whether we are dealing with a regular axis or one used as buttons
            if ((ini.IniReadValue(Section, Name + ".Min") == "") && (ini.IniReadValue(Section, Name + ".Max") == ""))
                Control[mode].Type = ControlType.Axis;
            else
            {
                Boolean Continuous;
                Boolean.TryParse(ini.IniReadValue(Section, Name + ".Continuous"), out Continuous);
                Control[mode].Type = Continuous ? ControlType.Continuous: ControlType.OneShot;
            }
        }

        /// <summary>
        /// Parse the configuration file and fill the Direct Input device attributes
        /// </summary>
        private void SetAttributes(AltDirectInputDevice Device, String Section)
        {
            InputRange Range;

            // Parse the global dead zone attribute for this device. This is the dead zone
            // that will be applied if there isn't a specific axis override
            float.TryParse(ini.IniReadValue(Section, "DeadZone"), out Device.DeadZone);
            // Parse the global sensitivity
            float.TryParse(ini.IniReadValue(Section, "Factor"), out Device.Factor);
            if (Device.Factor == 0.0f)
                Device.Factor = 1.0f;

            // Find our which modes have been setup
            for (var m = 1; m < GameState.NumModes; m++)
                Device.enabledModes[m] = (ini.IniReadValue(Section + "." + GameState.ModeName[m], null) != "");

            // Process the axes
            for (var i = 0; i < AltDirectInputDevice.AxisList.GetLength(0); i++)
            {
                // We get a DIERR_NOTFOUND/NotFound exception when probing the range for unused axes.
                // We use this to detect if the axe is available
                try
                {
                    // Parse common axis settings, starting with the Range
                    Range = Device.Joystick.GetObjectPropertiesByName(AltDirectInputDevice.AxisList[i, 0]).Range;
                    Device.Axis[i].Range.Minimum = Range.Minimum;
                    Device.Axis[i].Range.Maximum = Range.Maximum;
                    Device.Axis[i].Range.FloatRange = 1.0f * (Range.Maximum - Range.Minimum);

                    // TODO: Check if mapping name is valid
                    for (var m = 0; m < GameState.NumModes; m++)
                    {
                        if (!Device.enabledModes[m])
                            continue;

                        // Parse the dead zone
                        float.TryParse(ini.IniReadValue(Section, AltDirectInputDevice.AxisList[i, 1] + ".DeadZone"), out Device.Axis[i].Control[m].DeadZone);
                        if (Device.Axis[i].Control[m].DeadZone == 0.0f)
                            // Override with global dead zone if none was specified
                            // NB: This prohibits setting a global dead zone and then an individual one to 0 - oh well...
                            Device.Axis[i].Control[m].DeadZone = Device.DeadZone;
                        // A slider's dead zone is special and needs to be handled separately
                        if (!AltDirectInputDevice.AxisList[i, 0].StartsWith("Slider"))
                            Device.Joystick.GetObjectPropertiesByName(AltDirectInputDevice.AxisList[i, 0]).DeadZone = (int)(10000.0f * Device.Axis[i].Control[m].DeadZone);
                        // Parse the Factor
                        float.TryParse(ini.IniReadValue(Section, AltDirectInputDevice.AxisList[i, 1] + ".Factor"), out Device.Axis[i].Control[m].Factor);
                        if (Device.Axis[i].Control[m].Factor == 0.0f)
                            Device.Axis[i].Control[m].Factor = Device.Factor;

                        ParseControl(Section, AltDirectInputDevice.AxisList[i, 1], Device.Axis[i].Control, m);
                        if (Device.Axis[i].Control[m].Type == ControlType.Axis)
                        {
                            ParseMapping(Section, AltDirectInputDevice.AxisList[i, 1], Device.Axis[i].Mapping1, m);
                        }
                        else
                        {
                            ParseMapping(Section, AltDirectInputDevice.AxisList[i, 1] + ".Min", Device.Axis[i].Mapping1, m);
                            ParseMapping(Section, AltDirectInputDevice.AxisList[i, 1] + ".Max", Device.Axis[i].Mapping2, m);
                        }
                    }

                    Device.Axis[i].isAvailable = (Device.Axis[i].Range.FloatRange != 0.0f);
                    if (!Device.Axis[i].isAvailable)
                        print("Altinput: WARNING - Axis " + AltDirectInputDevice.AxisList[i, 1] +
                            " was disabled because its range is zero.");
                }
                // Typical exception "SharpDX.SharpDXException: HRESULT: [0x80070002], Module: [SharpDX.DirectInput],
                // ApiCode: [DIERR_NOTFOUND/NotFound], Message: The system cannot find the file specified."
                catch (SharpDX.SharpDXException ex)
                {
                    if (ex.ResultCode == 0x80070002)
                        Device.Axis[i].isAvailable = false;
                    else
                        throw ex;
                }
#if (DEBUG)
                if (Device.Axis[i].isAvailable)
                {
                    for (var m = 0; m < GameState.NumModes; m++)
                    {
                        String Mappings = "";
                        if (!Device.enabledModes[m]) continue;
                        if (Device.Axis[i].Control[m].Type == ControlType.Axis)
                        {
                            Mappings += ", Mapping = '" + Device.Axis[i].Mapping1[m].Action + "'";
                        }
                        else
                        {
                            Mappings += ", Mapping.Min = '" + Device.Axis[i].Mapping1[m].Action + "'";
                            Mappings += ", Mapping.Max = '" + Device.Axis[i].Mapping2[m].Action + "'";
                        }
                        print("Altinput: Axis #" + (i + 1) + "[" + GameState.ModeName[m] + "] ('" + 
                            AltDirectInputDevice.AxisList[i, 1] + "'): Range [" +
                            Device.Axis[i].Range.Minimum + ", " + Device.Axis[i].Range.Maximum + "]" +
                            ", DeadZone = " + Device.Axis[i].Control[m].DeadZone +
                            ", Factor = " + Device.Axis[i].Control[m].Factor + Mappings + 
                            ", Inverted = " + Device.Axis[i].Control[m].Inverted);
                    }
                }
#endif
            }

            // Process the POV controls
            for (var i = 0; i < Device.Joystick.Capabilities.PovCount; i++)
            {
                for (var j = 0; j < AltDirectInputDevice.NumPOVPositions; j++)
                {
                    for (var m = 0; m < GameState.NumModes; m++)
                    {
                        if (!Device.enabledModes[m])
                            continue;
                        Boolean.TryParse(ini.IniReadValue(Section + "." + GameState.ModeName[0], "POV" + (i + 1) + "." +
                            AltDirectInputDevice.POVPositionName[j] + ".Continuous"), out Device.Pov[i].Button[j].Continuous[m]);
                        ParseMapping(Section, "POV" + (i + 1) + "." +
                            AltDirectInputDevice.POVPositionName[j], Device.Pov[i].Button[j].Mapping, m);
                    }
                }
#if (DEBUG)
                for (var m = 0; m < GameState.NumModes; m++)
                {
                    if (!Device.enabledModes[m]) continue;
                    String Mappings = "";
                    for (var j = 0; j < AltDirectInputDevice.NumPOVPositions; j++)
                        Mappings += ((j != 0) ? ", " : "") + AltDirectInputDevice.POVPositionName[j] + " = '" +
                            Device.Pov[i].Button[j].Mapping[m].Action + "', Value = " +
                            Device.Pov[i].Button[j].Mapping[m].Value;
                    print("Altinput: POV #" + (i + 1) + " [" + GameState.ModeName[m] + "]: " + Mappings);
                }
#endif
            }

            // Process the buttons
            for (var i = 0; i < Device.Joystick.Capabilities.ButtonCount; i++)
            {
                for (var m = 0; m < GameState.NumModes; m++)
                {
                    if (!Device.enabledModes[m])
                        continue;
                    Boolean.TryParse(ini.IniReadValue(Section + "." + GameState.ModeName[m], "Button" + (i + 1) + ".Continuous"),
                        out Device.Button[i].Continuous[m]);
                    ParseMapping(Section, "Button" + (i + 1), Device.Button[i].Mapping, m);
                }
#if (DEBUG)
                for (var m = 0; m < GameState.NumModes; m++)
                {
                    if (!Device.enabledModes[m]) continue;
                    String Mappings = "Mapping = '" + Device.Button[i].Mapping[m].Action + "'";
                    if (Device.Button[i].Mapping[m].Value != 0.0f)
                        Mappings += ", Value = " + Device.Button[i].Mapping[m].Value;
                    print("Altinput: Button #" + (i + 1) + "[" + GameState.ModeName[m] + "]: " + Mappings);
                }
#endif
            }

        }

        /// <summary>
        /// Process each input section from the config file
        /// </summary>
        void ParseInputs()
        {
            String InterfaceName, ClassName;
            AltDirectInputDevice Device;

            print("AltInput: (re)loading configuration");
            ini = null;
            DeviceList.Clear();
            DetectedList.Clear();
            if (!File.Exists(ini_path))
                return;

            ini = new IniFile(ini_path);
            iniVersion = new System.Version(ini.IniReadValue("global", "Version"));
            if (iniVersion != currentVersion)
                return;

            List<String> sections = ini.IniReadAllSections();
            // Remove the [global] and [___.Mode] sections from our list
            sections.RemoveAll(s => s.Equals("global"));
            foreach (var modeName in GameState.ModeName)
                sections.RemoveAll(s => s.EndsWith("." + modeName));

            foreach (var dev in directInput.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AllDevices))
            {
                Joystick js = new Joystick(directInput, dev.InstanceGuid);
                DetectedList.Add("AltInput: Detected Controller '" + dev.InstanceName + "': " +
                    js.Capabilities.AxeCount + " Axes, " + js.Capabilities.ButtonCount + " Buttons, " +
                    js.Capabilities.PovCount + " POV(s)");

                foreach(var section in sections) 
                {
                    if (dev.InstanceName.Contains(section))
                    {
                        InterfaceName = ini.IniReadValue(section, "Interface");
                        if ((InterfaceName == "") || (ini.IniReadValue(section, "Ignore") == "true"))
                            break;
                        if (InterfaceName != "DirectInput")
                        {
                            print("AltInput[" + section + "]: Only 'DirectInput' is supported for Interface type");
                            continue;
                        }
                        ClassName = ini.IniReadValue(section, "Class");
                        if (ClassName == "")
                            ClassName = "GameControl";
                        else if (ClassName != "GameControl")
                        {
                            print("AltInput[" + section + "]: '" + ClassName + "' is not an allowed Class value");
                            continue;   // ignore the device
                        }
                        // Only add this device if not already in our list
                        if (DeviceList.Where(item => ((AltDirectInputDevice)item).InstanceGuid == dev.InstanceGuid).Any())
                            continue;
                        Device = new AltDirectInputDevice(directInput, DeviceClass.GameControl, dev.InstanceGuid);
                        SetAttributes(Device, section);
                        DeviceList.Add(Device);
                        print("AltInput: Added controller '" + dev.InstanceName + "'");
                    }
                }
            }
        }

        /// <summary>
        /// This method is the first called by the Unity engine when it instantiates
        /// the game element it is associated to (here the main game menu)
        /// </summary>
        void Awake()
        {
            ParseInputs();
        }
    }
}
