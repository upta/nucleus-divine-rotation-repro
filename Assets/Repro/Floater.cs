using Nucleus.Integrations.Unity.Physics;
using UnityEngine;

namespace DivineRotationRepro
{
    /// <summary>
    /// Keeps a heavy body afloat at y = 0 and rocking gently, like a ship riding at anchor: a buoyancy spring, a righting
    /// torque, and a slow pitch and roll from "waves". Forces go on the network's physics step (WorldStepStarted), on every
    /// peer, so a client's proxy floats too while NetworkRigidbody eases it onto the host's.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class Floater : MonoBehaviour
    {
        [SerializeField]
        private float _rockDegrees = 2f;

        [SerializeField]
        private float _rockPeriod = 6f;

        private Rigidbody _body;
        private PhysicsSimulationDriver _driver;
        private float _time;

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
        }

        private void OnDisable()
        {
            PhysicsSimulationDriver.UnregisterForScene(gameObject.scene, OnDriver);
            if (_driver != null)
            {
                _driver.WorldStepStarted -= OnStep;
                _driver = null;
            }
        }

        private void OnDriver(PhysicsSimulationDriver driver)
        {
            if (_driver != null || driver == null)
            {
                return;
            }

            _driver = driver;
            _driver.WorldStepStarted += OnStep;
        }

        private void OnEnable()
        {
            PhysicsSimulationDriver.RegisterForScene(gameObject.scene, OnDriver);
        }

        private void OnStep(uint tick, float stepDelta)
        {
            _time += stepDelta;
            float mass = _body.mass;

            // Buoyancy: hold the centre of mass at the waterline.
            float y = _body.worldCenterOfMass.y;
            float lift = mass * (-Physics.gravity.y) - mass * 4f * y - mass * 3f * _body.linearVelocity.y;
            _body.AddForce(Vector3.up * lift);

            // Righting toward upright, with damping, and a slow rock from the waves.
            Vector3 inertia = _body.inertiaTensor;
            Vector3 up = transform.up;
            Vector3 tilt = Vector3.Cross(up, Vector3.up);
            float wave = Mathf.Sin(2f * Mathf.PI * _time / _rockPeriod) * _rockDegrees * Mathf.Deg2Rad;
            Vector3 waveTilt = transform.right * wave + transform.forward * (wave * 0.7f);
            Vector3 righting = (tilt + waveTilt) * 6f - _body.angularVelocity * 3f;
            _body.AddTorque(Vector3.Scale(righting, inertia));
        }
    }
}
