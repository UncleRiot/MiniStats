using System;
using LibreHardwareMonitor.Hardware;

namespace MiniStats
{
    public sealed class HardwareMonitorService : IDisposable
    {
        private readonly Computer computer;

        public HardwareMonitorService()
        {
            computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true
            };

            computer.Open();
        }

        public HardwareSnapshot ReadSnapshot()
        {
            float? cpuTemperature = null;
            float? gpuTemperature = null;
            float? fps = null;

            foreach (IHardware hardware in computer.Hardware)
            {
                UpdateHardwareRecursive(hardware, ref cpuTemperature, ref gpuTemperature, ref fps);
            }

            return new HardwareSnapshot
            {
                CpuTemperature = cpuTemperature,
                GpuTemperature = gpuTemperature,
                Fps = fps.HasValue && fps.Value > 0 ? fps : null
            };
        }

        private void UpdateHardwareRecursive(IHardware hardware, ref float? cpuTemperature, ref float? gpuTemperature, ref float? fps)
        {
            hardware.Update();

            if (hardware.HardwareType == HardwareType.Cpu)
            {
                ReadCpuSensors(hardware, ref cpuTemperature);
            }

            if (hardware.HardwareType == HardwareType.GpuNvidia ||
                hardware.HardwareType == HardwareType.GpuAmd ||
                hardware.HardwareType == HardwareType.GpuIntel)
            {
                ReadGpuSensors(hardware, ref gpuTemperature, ref fps);
            }

            foreach (IHardware subHardware in hardware.SubHardware)
            {
                UpdateHardwareRecursive(subHardware, ref cpuTemperature, ref gpuTemperature, ref fps);
            }
        }

        private static void ReadCpuSensors(IHardware hardware, ref float? cpuTemperature)
        {
            foreach (ISensor sensor in hardware.Sensors)
            {
                if (sensor.SensorType != SensorType.Temperature || !sensor.Value.HasValue)
                {
                    continue;
                }

                float value = sensor.Value.Value;
                if (value <= 0)
                {
                    continue;
                }

                string name = sensor.Name ?? string.Empty;

                if (name.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Tctl/Tdie", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Tdie", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Tctl", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    cpuTemperature = value;
                    return;
                }

                if (cpuTemperature == null)
                {
                    cpuTemperature = value;
                }
            }

            foreach (IHardware subHardware in hardware.SubHardware)
            {
                subHardware.Update();

                foreach (ISensor sensor in subHardware.Sensors)
                {
                    if (sensor.SensorType != SensorType.Temperature || !sensor.Value.HasValue)
                    {
                        continue;
                    }

                    float value = sensor.Value.Value;
                    if (value <= 0)
                    {
                        continue;
                    }

                    string name = sensor.Name ?? string.Empty;

                    if (name.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Tctl/Tdie", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Tdie", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("Tctl", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        cpuTemperature = value;
                        return;
                    }

                    if (cpuTemperature == null)
                    {
                        cpuTemperature = value;
                    }
                }
            }
        }

        private static void ReadGpuSensors(IHardware hardware, ref float? gpuTemperature, ref float? fps)
        {
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
                            gpuTemperature = value;
                        }
                        else if (!gpuTemperature.HasValue)
                        {
                            gpuTemperature = value;
                        }
                    }
                }

                if (IsFpsSensor(sensor))
                {
                    fps = SelectHigherValue(fps, sensor.Value);
                }
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