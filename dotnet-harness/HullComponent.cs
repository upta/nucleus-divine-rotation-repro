using System.Numerics;
using Nucleus.Components;

namespace DivineRotationRepro
{
    /// <summary>
    /// The same rotation carried twice: once under Divine (as NetworkPhysicsComponent.WorldRotation sends a hull in Pro),
    /// once under Interval, so a client can compare what each delivers.
    /// </summary>
    public partial class HullComponent : NetworkComponent
    {
        public readonly NetworkMember<Quaternion> Divine = new(transmissionMode: TransmissionMode.Divine);

        public readonly NetworkMember<Quaternion> Interval = new(transmissionMode: TransmissionMode.Interval);
    }
}
