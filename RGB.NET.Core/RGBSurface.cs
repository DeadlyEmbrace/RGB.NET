﻿// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable UnusedMember.Global

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace RGB.NET.Core
{
    /// <summary>
    /// Represents a RGB-surface containing multiple devices.
    /// </summary>
    public static partial class RGBSurface
    {
        #region Properties & Fields

        private static DateTime _lastUpdate;

        private static IList<IRGBDeviceProvider> _deviceProvider = new List<IRGBDeviceProvider>();
        private static IList<IRGBDevice> _devices = new List<IRGBDevice>();

        // ReSharper disable InconsistentNaming

        private static readonly LinkedList<ILedGroup> _ledGroups = new LinkedList<ILedGroup>();

        private static readonly Rectangle _surfaceRectangle = new Rectangle();

        // ReSharper restore InconsistentNaming

        /// <summary>
        /// Gets a readonly list containing all loaded <see cref="IRGBDevice"/>.
        /// </summary>
        public static IEnumerable<IRGBDevice> Devices => new ReadOnlyCollection<IRGBDevice>(_devices);

        /// <summary>
        /// Gets a copy of the <see cref="Rectangle"/> representing this <see cref="RGBSurface"/>.
        /// </summary>
        public static Rectangle SurfaceRectangle => new Rectangle(_surfaceRectangle);

        /// <summary>
        /// Gets a list of all <see cref="Led"/> on this <see cref="RGBSurface"/>.
        /// </summary>
        public static IEnumerable<Led> Leds => _devices.SelectMany(x => x);

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="RGBSurface"/> class.
        /// </summary>
        static RGBSurface()
        {
            _lastUpdate = DateTime.Now;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Perform an update for all dirty <see cref="Led"/>, or all <see cref="Led"/>, if flushLeds is set to true.
        /// </summary>
        /// <param name="flushLeds">Specifies whether all <see cref="Led"/>, (including clean ones) should be updated.</param>
        public static void Update(bool flushLeds = false)
        {
            OnUpdating();

            lock (_ledGroups)
            {
                // Update effects
                foreach (ILedGroup ledGroup in _ledGroups)
                    ledGroup.UpdateEffects();

                // Render brushes
                foreach (ILedGroup ledGroup in _ledGroups.OrderBy(x => x.ZIndex))
                    Render(ledGroup);
            }

            foreach (IRGBDevice device in Devices)
                device.Update(flushLeds);

            OnUpdated();
        }

        /// <summary>
        /// Renders a ledgroup.
        /// </summary>
        /// <param name="ledGroup">The led group to render.</param>
        private static void Render(ILedGroup ledGroup)
        {
            IList<Led> leds = ledGroup.GetLeds().ToList();
            IBrush brush = ledGroup.Brush;

            if ((brush == null) || !brush.IsEnabled) return;

            try
            {
                switch (brush.BrushCalculationMode)
                {
                    case BrushCalculationMode.Relative:
                        Rectangle brushRectangle = new Rectangle(leds.Select(x => GetDeviceLedLocation(x)));
                        Point offset = new Point(-brushRectangle.Location.X, -brushRectangle.Location.Y);
                        brushRectangle.Location.X = 0;
                        brushRectangle.Location.Y = 0;
                        brush.PerformRender(brushRectangle,
                                            leds.Select(x => new BrushRenderTarget(x, GetDeviceLedLocation(x, offset))));
                        break;
                    case BrushCalculationMode.Absolute:
                        brush.PerformRender(SurfaceRectangle, leds.Select(x => new BrushRenderTarget(x, GetDeviceLedLocation(x))));
                        break;
                    default:
                        throw new ArgumentException();
                }

                brush.UpdateEffects();
                brush.PerformFinalize();

                foreach (KeyValuePair<BrushRenderTarget, Color> renders in brush.RenderedTargets)
                    renders.Key.Led.Color = renders.Value;
            }
            // ReSharper disable once CatchAllClause
            catch (Exception ex)
            {
                OnException(ex);
            }
        }

        private static Rectangle GetDeviceLedLocation(Led led, Point extraOffset = null)
        {
            return extraOffset != null
                       ? new Rectangle(led.LedRectangle.Location + led.Device.Location + extraOffset, led.LedRectangle.Size)
                       : new Rectangle(led.LedRectangle.Location + led.Device.Location, led.LedRectangle.Size);
        }

        /// <summary>
        /// Attaches the given <see cref="ILedGroup"/>.
        /// </summary>
        /// <param name="ledGroup">The <see cref="ILedGroup"/> to attach.</param>
        /// <returns><c>true</c> if the <see cref="ILedGroup"/> could be attached; otherwise, <c>false</c>.</returns>
        public static bool AttachLedGroup(ILedGroup ledGroup)
        {
            if (ledGroup == null) return false;

            lock (_ledGroups)
            {
                if (_ledGroups.Contains(ledGroup)) return false;

                _ledGroups.AddLast(ledGroup);
                ledGroup.OnAttach();

                return true;
            }
        }

        /// <summary>
        /// Detaches the given <see cref="ILedGroup"/>.
        /// </summary>
        /// <param name="ledGroup">The <see cref="ILedGroup"/> to detached.</param>
        /// <returns><c>true</c> if the <see cref="ILedGroup"/> could be detached; otherwise, <c>false</c>.</returns>
        public static bool DetachLedGroup(ILedGroup ledGroup)
        {
            if (ledGroup == null) return false;

            lock (_ledGroups)
            {
                LinkedListNode<ILedGroup> node = _ledGroups.Find(ledGroup);
                if (node == null) return false;

                _ledGroups.Remove(node);
                node.Value.OnDetach();

                return true;
            }
        }

        private static void UpdateSurfaceRectangle()
        {
            Rectangle devicesRectangle = new Rectangle(_devices.Select(d => new Rectangle(d.Location, d.Size)));

            _surfaceRectangle.Size.Width = devicesRectangle.Location.X + devicesRectangle.Size.Width;
            _surfaceRectangle.Size.Height = devicesRectangle.Location.Y + devicesRectangle.Size.Height;
        }

        #endregion
    }
}