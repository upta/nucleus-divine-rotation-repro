using Nucleus.Managers.NetworkLoop;

namespace DivineRotationRepro
{
    /// <summary>Steps the loop only when called, on the calling thread (the nucleus-dotnet-host skill's provider).</summary>
    public sealed class ManualLoopStepProvider : INetworkLoopStepProvider
    {
        private readonly NetworkLoopStepDriver _driver = new();

        public bool IsStarted { get; private set; }

        public bool IsTickFrame => _driver.IsTickFrame;

        public void Initialize(NetworkLoopManager networkLoopManager) =>
            _driver.Initialize(networkLoopManager, 1000f / networkLoopManager.TickRate);

        public void Start() => IsStarted = true;

        public void Stop() => IsStarted = false;

        public void Return() { }

        public void AdvanceEarly(float frameDeltaMilliseconds)
        {
            if (IsStarted)
            {
                _driver.AdvanceEarly(frameDeltaMilliseconds, isVariableUpdateAllowed: true);
            }
        }

        public void AdvanceLate(float frameDeltaMilliseconds)
        {
            if (IsStarted)
            {
                _driver.AdvanceLate(frameDeltaMilliseconds, isVariableUpdateAllowed: true);
            }
        }
    }
}
