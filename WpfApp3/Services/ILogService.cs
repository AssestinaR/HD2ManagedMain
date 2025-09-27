namespace LiberTeaManager.Services
{
    public interface ILogService
    {
        void Log(string message);
    }

    public sealed class UiLogService : ILogService
    {
        private readonly Action<string> _sink;
        public UiLogService(Action<string> sink) => _sink = sink;
        public void Log(string message) => _sink?.Invoke(message);
    }
}
