﻿// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable UnusedMember.Global

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using RGB.NET.Core;
using RGB.NET.Devices.Corsair.Native;

namespace RGB.NET.Devices.Corsair
{
    /// <inheritdoc />
    /// <summary>
    /// Represents a device provider responsible for corsair (CUE) devices.
    /// </summary>
    public class CorsairDeviceProvider : IRGBDeviceProvider
    {
        #region Properties & Fields

        private static CorsairDeviceProvider _instance;
        /// <summary>
        /// Gets the singleton <see cref="CorsairDeviceProvider"/> instance.
        /// </summary>
        public static CorsairDeviceProvider Instance => _instance ?? new CorsairDeviceProvider();

        /// <summary>
        /// Gets a modifiable list of paths used to find the native SDK-dlls for x86 applications.
        /// The first match will be used.
        /// </summary>
        public static List<string> PossibleX86NativePaths { get; } = new List<string> { "x86/CUESDK.dll", "x86/CUESDK_2015.dll", "x86/CUESDK_2013.dll" };

        /// <summary>
        /// Gets a modifiable list of paths used to find the native SDK-dlls for x64 applications.
        /// The first match will be used.
        /// </summary>
        public static List<string> PossibleX64NativePaths { get; } = new List<string> { "x64/CUESDK.dll", "x64/CUESDK_2015.dll", "x64/CUESDK_2013.dll" };

        /// <inheritdoc />
        /// <summary>
        /// Indicates if the SDK is initialized and ready to use.
        /// </summary>
        public bool IsInitialized { get; private set; }

        /// <summary>
        /// Gets the loaded architecture (x64/x86).
        /// </summary>
        public string LoadedArchitecture => _CUESDK.LoadedArchitecture;

        /// <summary>
        /// Gets the protocol details for the current SDK-connection.
        /// </summary>
        public CorsairProtocolDetails ProtocolDetails { get; private set; }

        /// <inheritdoc />
        /// <summary>
        /// Gets whether the application has exclusive access to the SDK or not.
        /// </summary>
        public bool HasExclusiveAccess { get; private set; }

        /// <summary>
        /// Gets the last error documented by CUE.
        /// </summary>
        public CorsairError LastError => _CUESDK.CorsairGetLastError();

        /// <inheritdoc />
        public IEnumerable<IRGBDevice> Devices { get; private set; }

        /// <summary>
        /// The <see cref="DeviceUpdateTrigger"/> used to trigger the updates for corsair devices. 
        /// </summary>
        public DeviceUpdateTrigger UpdateTrigger { get; private set; }
        private CorsairUpdateQueue _updateQueue;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="CorsairDeviceProvider"/> class.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if this constructor is called even if there is already an instance of this class.</exception>
        public CorsairDeviceProvider()
        {
            if (_instance != null) throw new InvalidOperationException($"There can be only one instance of type {nameof(CorsairDeviceProvider)}");
            _instance = this;

            UpdateTrigger = new DeviceUpdateTrigger();
            _updateQueue = new CorsairUpdateQueue(UpdateTrigger);
        }

        #endregion

        #region Methods

        /// <inheritdoc />
        /// <exception cref="RGBDeviceException">Thrown if the SDK is already initialized or if the SDK is not compatible to CUE.</exception>
        /// <exception cref="CUEException">Thrown if the CUE-SDK provides an error.</exception>
        public bool Initialize(RGBDeviceType loadFilter = RGBDeviceType.All, bool exclusiveAccessIfPossible = false, bool throwExceptions = false)
        {
            IsInitialized = false;

            try
            {
                UpdateTrigger?.Stop();

                _CUESDK.Reload();

                ProtocolDetails = new CorsairProtocolDetails(_CUESDK.CorsairPerformProtocolHandshake());

                CorsairError error = LastError;
                if (error != CorsairError.Success)
                    throw new CUEException(error);

                if (ProtocolDetails.BreakingChanges)
                    throw new RGBDeviceException("The SDK currently used isn't compatible with the installed version of CUE.\r\n"
                                              + $"CUE-Version: {ProtocolDetails.ServerVersion} (Protocol {ProtocolDetails.ServerProtocolVersion})\r\n"
                                              + $"SDK-Version: {ProtocolDetails.SdkVersion} (Protocol {ProtocolDetails.SdkProtocolVersion})");

                if (exclusiveAccessIfPossible)
                {
                    if (!_CUESDK.CorsairRequestControl(CorsairAccessMode.ExclusiveLightingControl))
                        throw new CUEException(LastError);

                    HasExclusiveAccess = true;
                }
                else
                    HasExclusiveAccess = false;

                IList<IRGBDevice> devices = new List<IRGBDevice>();
                int deviceCount = _CUESDK.CorsairGetDeviceCount();
                for (int i = 0; i < deviceCount; i++)
                {
                    try
                    {
                        _CorsairDeviceInfo nativeDeviceInfo = (_CorsairDeviceInfo)Marshal.PtrToStructure(_CUESDK.CorsairGetDeviceInfo(i), typeof(_CorsairDeviceInfo));
                        CorsairRGBDeviceInfo info = new CorsairRGBDeviceInfo(i, RGBDeviceType.Unknown, nativeDeviceInfo);
                        if (!info.CapsMask.HasFlag(CorsairDeviceCaps.Lighting))
                            continue; // Everything that doesn't support lighting control is useless

                        ICorsairRGBDevice device = GetRGBDevice(info, i, nativeDeviceInfo);
                        if ((device == null) || !loadFilter.HasFlag(device.DeviceInfo.DeviceType)) continue;

                        device.Initialize(_updateQueue);
                        AddSpecialParts(device);

                        error = LastError;
                        if (error != CorsairError.Success)
                            throw new CUEException(error);

                        devices.Add(device);
                    }
                    catch { if (throwExceptions) throw; }
                }

                UpdateTrigger?.Start();

                Devices = new ReadOnlyCollection<IRGBDevice>(devices);
                IsInitialized = true;
            }
            catch
            {
                Reset();
                if (throwExceptions) throw;
                return false;
            }

            return true;
        }

        private static ICorsairRGBDevice GetRGBDevice(CorsairRGBDeviceInfo info, int i, _CorsairDeviceInfo nativeDeviceInfo)
        {
            switch (info.CorsairDeviceType)
            {
                case CorsairDeviceType.Keyboard:
                    return new CorsairKeyboardRGBDevice(new CorsairKeyboardRGBDeviceInfo(i, nativeDeviceInfo));

                case CorsairDeviceType.Mouse:
                    return new CorsairMouseRGBDevice(new CorsairMouseRGBDeviceInfo(i, nativeDeviceInfo));

                case CorsairDeviceType.Headset:
                    return new CorsairHeadsetRGBDevice(new CorsairHeadsetRGBDeviceInfo(i, nativeDeviceInfo));

                case CorsairDeviceType.Mousepad:
                    return new CorsairMousepadRGBDevice(new CorsairMousepadRGBDeviceInfo(i, nativeDeviceInfo));

                case CorsairDeviceType.HeadsetStand:
                    return new CorsairHeadsetStandRGBDevice(new CorsairHeadsetStandRGBDeviceInfo(i, nativeDeviceInfo));

                // ReSharper disable once RedundantCaseLabel
                case CorsairDeviceType.Unknown:
                default:
                    throw new RGBDeviceException("Unknown Device-Type");
            }
        }

        private void AddSpecialParts(ICorsairRGBDevice device)
        {
            if (device.DeviceInfo.Model.Equals("K95 RGB Platinum", StringComparison.OrdinalIgnoreCase))
                device.AddSpecialDevicePart(new LightbarSpecialPart(device));
        }

        /// <inheritdoc />
        public void ResetDevices()
        {
            if (IsInitialized)
                try
                {
                    _CUESDK.Reload();
                }
                catch {/* shit happens */}
        }

        private void Reset()
        {
            ProtocolDetails = null;
            HasExclusiveAccess = false;
            Devices = null;
            IsInitialized = false;
        }

        /// <inheritdoc />
        public void Dispose()
        { }

        #endregion
    }
}
