namespace MiniStats
{
    public sealed class HardwareSnapshot
    {
        public float? CpuTemperature { get; set; }
        public float? GpuTemperature { get; set; }
        public float? CpuLoad { get; set; }
        public float? GpuLoad { get; set; }
        public float? Fps { get; set; }
    }
}