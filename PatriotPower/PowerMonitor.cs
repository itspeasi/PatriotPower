using System;
using System.Runtime.InteropServices;

namespace PatriotPower
{
    public static class PowerMonitor
    {
        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern uint CallNtPowerInformation(
            int InformationLevel,
            IntPtr lpInputBuffer,
            int nInputBufferSize,
            out SYSTEM_BATTERY_STATE lpOutputBuffer,
            int nOutputBufferSize);

        // Information level 5 corresponds to SystemBatteryState
        private const int SystemBatteryState = 5;

        // Struct must exactly match the C++ memory layout for the Windows API
        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_BATTERY_STATE
        {
            public byte AcOnLine;
            public byte BatteryPresent;
            public byte Charging;
            public byte Discharging;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
            public byte[] Spare1;
            public byte Tag;
            public uint MaxCapacity;
            public uint RemainingCapacity; // Remaining capacity in milliwatt-hours (mWh)
            public uint Rate;              // Discharge rate in milliwatts (mW)
            public uint EstimatedTime;
            public uint DefaultAlert1;
            public uint DefaultAlert2;
        }

        /// <summary>
        /// Gets the current total system discharge rate in Watts and the estimated time remaining.
        /// Returns 0.0 Watts and null time if plugged in or reading is invalid.
        /// </summary>
        public static (double Watts, TimeSpan? TimeRemaining) GetPowerMetrics()
        {
            uint retval = CallNtPowerInformation(
                SystemBatteryState,
                IntPtr.Zero,
                0,
                out SYSTEM_BATTERY_STATE state,
                Marshal.SizeOf(typeof(SYSTEM_BATTERY_STATE)));

            // If API call succeeded, battery exists, and it is actively discharging
            if (retval == 0 && state.BatteryPresent == 1 && state.Discharging == 1)
            {
                // Cast the uint to a signed int to correctly handle two's complement binary from the driver
                int signedRate = (int)state.Rate;
                double absRateMw = Math.Abs(signedRate);
                double watts = absRateMw / 1000.0;

                TimeSpan? timeRemaining = null;

                // Prevent divide-by-zero if sensor is transitioning
                if (absRateMw > 0 && state.RemainingCapacity > 0)
                {
                    // Mathematically truthful calculation: (mWh) / (mW) = Hours
                    double hoursRemaining = state.RemainingCapacity / absRateMw;
                    timeRemaining = TimeSpan.FromHours(hoursRemaining);
                }

                return (watts, timeRemaining);
            }

            return (0.0, null);
        }

        /// <summary>
        /// Checks if the system is currently plugged into AC power.
        /// </summary>
        public static bool IsPluggedIn()
        {
            uint retval = CallNtPowerInformation(
                SystemBatteryState,
                IntPtr.Zero,
                0,
                out SYSTEM_BATTERY_STATE state,
                Marshal.SizeOf(typeof(SYSTEM_BATTERY_STATE)));

            return retval == 0 && state.AcOnLine == 1;
        }
    }
}