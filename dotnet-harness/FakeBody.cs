using System.Numerics;
using Nucleus.Physics;

namespace DivineRotationRepro
{
    /// <summary>A stand-in physics body for the server to capture: what a game's engine adapter would hand Nucleus.</summary>
    public sealed class FakeBody : IPhysicsBody
    {
        public Vector3 Position { get; set; }

        public Quaternion Rotation { get; set; } = Quaternion.Identity;

        public Vector3 LinearVelocity { get; set; }

        public Vector3 AngularVelocity { get; set; }

        public bool IsSleeping { get; set; }

        public bool IsKinematic => false;

        public Vector3 Gravity => new(0f, -9.81f, 0f);

        public float Drag => 0f;

        public float Mass => 73100f;

        public void AddForce(Vector3 force) { }
    }
}
