using System;
using System.Collections.Generic;
using LibreHardwareMonitor.Hardware;

namespace MiniStats
{
    public sealed class HardwareMonitorService : IDisposable
    {
        private readonly Computer computer;
        private string selectedGpuName = "Auto";

        public HardwareMonitorService()
        {
            computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true
            };

            computer.Open();
        }

        public void SetSelectedGpuName(string? gpuName)
        {
            selectedGpuName = string.IsNullOrWhiteSpace(gpuName) ? "Auto" : gpuName;
        }

        public string[] GetAvailableGpuNames()
        {
            List<string> gpuNames = new List<string>();

            foreach (IHardware hardware in computer.Hardware)
            {
                if (!IsGpuHardware(hardware))
                {
                    continue;
                }

                hardware.Update();

                if (!string.IsNullOrWhiteSpace(hardware.Name) && !gpuNames.Contains(hardware.Name))
                {
                    gpuNames.Add(hardware.Name);
                }
            }

            return gpuNames.ToArray();
        }

        public HardwareSnapshot ReadSnapshot()
        {
            float? cpuTemperature = null;
            float? gpuTemperature = null;
            float? cpuLoad = null;
            float? gpuLoad = null;
            float? fps = null;

            foreach (IHardware hardware in computer.Hardware)
            {
                UpdateHardwareRecursive(hardware, ref cpuTemperature, ref gpuTemperature, ref cpuLoad, ref gpuLoad, ref fps);
            }

            return new HardwareSnapshot
            {
                CpuTemperature = cpuTemperature,
                GpuTemperature = gpuTemperature,
                CpuLoad = cpuLoad,
                GpuLoad = gpuLoad,
                Fps = fps.HasValue && fps.Value > 0 ? fps : null
            };
        }

        private void UpdateHardwareRecursive(
            IHardware hardware,
            ref float? cpuTemperature,
            ref float? gpuTemperature,
            ref float? cpuLoad,
            ref float? gpuLoad,
            ref float? fps)
        {
            hardware.Update();

            if (hardware.HardwareType == HardwareType.Cpu)
            {
                ReadCpuSensors(hardware, ref cpuTemperature, ref cpuLoad);
            }

            if (IsGpuHardware(hardware) && ShouldReadGpuHardware(hardware))
            {
                ReadGpuSensors(hardware, ref gpuTemperature, ref gpuLoad, ref fps);
            }

            foreach (IHardware subHardware in hardware.SubHardware)
            {
                UpdateHardwareRecursive(subHardware, ref cpuTemperature, ref gpuTemperature, ref cpuLoad, ref gpuLoad, ref fps);
            }
        }

        private bool ShouldReadGpuHardware(IHardware hardware)
        {
            if (string.IsNullOrWhiteSpace(selectedGpuName) ||
                string.Equals(selectedGpuName, "Auto", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return string.Equals(hardware.Name, selectedGpuName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsGpuHardware(IHardware hardware)
        {
            return hardware.HardwareType == HardwareType.GpuNvidia ||
                   hardware.HardwareType == HardwareType.GpuAmd ||
                   hardware.HardwareType == HardwareType.GpuIntel;
        }

        private static void ReadCpuSensors(IHardware hardware, ref float? cpuTemperature, ref float? cpuLoad)
        {
            foreach (ISensor sensor in hardware.Sensors)
            {
                if (sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue)
                {
                    float value = sensor.Value.Value;
                    if (value > 0)
                    {
                        string name = sensor.Name ?? string.Empty;

                        if (name.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("Tctl/Tdie", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("Tdie", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("Tctl", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            cpuTemperature = value;
                        }
                        else if (!cpuTemperature.HasValue)
                        {
                            cpuTemperature = value;
                        }
                    }
                }

                if (sensor.SensorType == SensorType.Load && sensor.Value.HasValue)
                {
                    float value = sensor.Value.Value;
                    if (value >= 0)
                    {
                        string name = sensor.Name ?? string.Empty;

                        if (name.IndexOf("Total", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("CPU Total", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            cpuLoad = value;
                        }
                        else if (!cpuLoad.HasValue)
                        {
                            cpuLoad = value;
                        }
                    }
                }
            }

            foreach (IHardware subHardware in hardware.SubHardware)
            {
                subHardware.Update();

                foreach (ISensor sensor in subHardware.Sensors)
                {
                    if (sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue)
                    {
                        float value = sensor.Value.Value;
                        if (value > 0)
                        {
                            string name = sensor.Name ?? string.Empty;

                            if (name.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                name.IndexOf("Tctl/Tdie", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                name.IndexOf("Tdie", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                name.IndexOf("Tctl", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                cpuTemperature = value;
                            }
                            else if (!cpuTemperature.HasValue)
                            {
                                cpuTemperature = value;
                            }
                        }
                    }

                    if (sensor.SensorType == SensorType.Load && sensor.Value.HasValue)
                    {
                        float value = sensor.Value.Value;
                        if (value >= 0)
                        {
                            string name = sensor.Name ?? string.Empty;

                            if (name.IndexOf("Total", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                name.IndexOf("CPU Total", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                cpuLoad = value;
                            }
                            else if (!cpuLoad.HasValue)
                            {
                                cpuLoad = value;
                            }
                        }
                    }
                }
            }
        }

        private static void ReadGpuSensors(IHardware hardware, ref float? gpuTemperature, ref float? gpuLoad, ref float? fps)
        {
            float? bestTemperature = gpuTemperature;
            float? preferredLoad = null;
            float? fallbackLoad = gpuLoad;

            foreach (ISensor sensor in hardware.Sensors)
            {
                if (sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue)
                {
                    float value = sensor.Value.Value;
                    if (value > 0)
                    {
                        string name = sensor.Name ?? string.Empty;

                        if (name.IndexOf("Hot Spot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("Hotspot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("Memory", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            continue;
                        }

                        if (name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("GPU", StringComparison.OrdinalIgnoreCase) ||
                            name.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("Edge", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            bestTemperature = value;
                        }
                        else if (!bestTemperature.HasValue)
                        {
                            bestTemperature = value;
                        }
                    }
                }

                if (sensor.SensorType == SensorType.Load && sensor.Value.HasValue)
                {
                    float value = sensor.Value.Value;
                    if (value >= 0)
                    {
                        string name = sensor.Name ?? string.Empty;

                        if (name.IndexOf("Memory", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("Bus", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("Video Engine", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("Copy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("Decode", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("Encode", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            continue;
                        }

                        bool isPreferredGpuLoad =
                            name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("GPU", StringComparison.OrdinalIgnoreCase) ||
                            name.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("D3D", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("3D", StringComparison.OrdinalIgnoreCase) >= 0;

                        if (isPreferredGpuLoad)
                        {
                            if (!preferredLoad.HasValue || value > preferredLoad.Value)
                            {
                                preferredLoad = value;
                            }
                        }
                        else if (!fallbackLoad.HasValue || value > fallbackLoad.Value)
                        {
                            fallbackLoad = value;
                        }
                    }
                }

                if (IsFpsSensor(sensor))
                {
                    fps = SelectHigherValue(fps, sensor.Value);
                }
            }

            foreach (IHardware subHardware in hardware.SubHardware)
            {
                subHardware.Update();

                foreach (ISensor sensor in subHardware.Sensors)
                {
                    if (sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue)
                    {
                        float value = sensor.Value.Value;
                        if (value > 0)
                        {
                            string name = sensor.Name ?? string.Empty;

                            if (name.IndexOf("Hot Spot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                name.IndexOf("Hotspot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                name.IndexOf("Memory", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                continue;
                            }

                            if (name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase) ||
                                name.Equals("GPU", StringComparison.OrdinalIgnoreCase) ||
                                name.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                name.IndexOf("Edge", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                bestTemperature = value;
                            }
                            else if (!bestTemperature.HasValue)
                            {
                                bestTemperature = value;
                            }
                        }
                    }

                    if (sensor.SensorType == SensorType.Load && sensor.Value.HasValue)
                    {
                        float value = sensor.Value.Value;
                        if (value >= 0)
                        {
                            string name = sensor.Name ?? string.Empty;

                            if (name.IndexOf("Memory", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                name.IndexOf("Bus", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                name.IndexOf("Video Engine", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                name.IndexOf("Copy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                name.IndexOf("Decode", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                name.IndexOf("Encode", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                continue;
                            }

                            bool isPreferredGpuLoad =
                                name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase) ||
                                name.Equals("GPU", StringComparison.OrdinalIgnoreCase) ||
                                name.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                name.IndexOf("D3D", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                name.IndexOf("3D", StringComparison.OrdinalIgnoreCase) >= 0;

                            if (isPreferredGpuLoad)
                            {
                                if (!preferredLoad.HasValue || value > preferredLoad.Value)
                                {
                                    preferredLoad = value;
                                }
                            }
                            else if (!fallbackLoad.HasValue || value > fallbackLoad.Value)
                            {
                                fallbackLoad = value;
                            }
                        }
                    }

                    if (IsFpsSensor(sensor))
                    {
                        fps = SelectHigherValue(fps, sensor.Value);
                    }
                }
            }

            gpuTemperature = bestTemperature;

            if (preferredLoad.HasValue)
            {
                gpuLoad = preferredLoad.Value;
            }
            else if (fallbackLoad.HasValue)
            {
                gpuLoad = fallbackLoad.Value;
            }
        }

        private static bool IsFpsSensor(ISensor sensor)
        {
            if (!sensor.Value.HasValue || sensor.Value.Value <= 0)
            {
                return false;
            }

            string name = sensor.Name ?? string.Empty;

            return name.IndexOf("fps", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("frame rate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("framerate", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static float? SelectHigherValue(float? currentValue, float? candidate)
        {
            if (!candidate.HasValue)
            {
                return currentValue;
            }

            if (!currentValue.HasValue)
            {
                return candidate.Value;
            }

            return Math.Max(currentValue.Value, candidate.Value);
        }

        public void Dispose()
        {
            computer.Close();
        }
    }
}