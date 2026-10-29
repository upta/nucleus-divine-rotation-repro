using System;
using Nucleus.Integrations.Unity.Managers.Transports;
using Nucleus.Integrations.Unity.Physics;
using Nucleus.Integrations.Unity.Transports;
using Nucleus.Integrations.Unity.Serializers;
using UnityEngine;

namespace DivineRotationRepro
{
    /// <summary>
    /// Starts the session from the command line (<c>-host</c>, or <c>-join &lt;address&gt;</c>), then once a second logs
    /// the floating body's rotation and the rotation its NetworkPhysicsComponent holds, each as degrees away from where the
    /// body should lie (upright, facing 180 degrees of yaw, give or take its gentle rock). The host's stay within a few
    /// degrees. Under Divine a client's received rotation turns steadily away, and its body follows it over. After
    /// <c>-seconds</c> (default 45) it logs a verdict and quits.
    /// </summary>
    public sealed class ReproDriver : MonoBehaviour
    {
        [SerializeField]
        private UnityTransportManager _transports;

        [SerializeField]
        private SynapseTransport _synapse;

        [SerializeField]
        private NetworkRigidbody _body;

        private bool _isClient;
        private float _nextLog = 5f;
        private float _quitAt = 45f;
        private float _worst;

        private float _yaw = 180f;
        private bool _interval;
        private bool _isModeSet;

        private float AwayFromRest(Quaternion rotation) => Quaternion.Angle(rotation, Quaternion.Euler(0f, _yaw, 0f));

        private static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == name)
                {
                    return i + 1 < args.Length ? args[i + 1] : "";
                }
            }

            return null;
        }

        private async void Start()
        {
            Application.runInBackground = true;
            string seconds = Arg("-seconds");
            if (!string.IsNullOrEmpty(seconds))
            {
                _quitAt = float.Parse(seconds, System.Globalization.CultureInfo.InvariantCulture);
            }

            string yaw = Arg("-yaw");
            if (!string.IsNullOrEmpty(yaw))
            {
                _yaw = float.Parse(yaw, System.Globalization.CultureInfo.InvariantCulture);
            }

            // -interval sends the body's pose as Interval instead of Divine: pass it to both host and client.
            _interval = Arg("-interval") != null;

            string join = Arg("-join");
            if (join != null)
            {
                _isClient = true;
                _synapse.RemoteHost = join.Length > 0 ? join : "127.0.0.1";
                Debug.Log("REPRO joining " + _synapse.RemoteHost);
                await _transports.StartClientAsync();
            }
            else
            {
                // The host turns the body to the yaw asked for before the session starts; nothing turns it after.
                _body.transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
                _body.GetComponent<Rigidbody>().rotation = _body.transform.rotation;
                Debug.Log("REPRO hosting");
                await _transports.StartHostAsync();
            }
        }

        private void Update()
        {
            if (_interval && !_isModeSet && _body.PhysicsComponent != null)
            {
                _body.PhysicsComponent.WorldPosition.SetTransmissionMode(Nucleus.Components.TransmissionMode.Interval);
                _body.PhysicsComponent.WorldRotation.SetTransmissionMode(Nucleus.Components.TransmissionMode.Interval);
                _isModeSet = true;
            }

            float now = Time.realtimeSinceStartup;
            if (now < _nextLog)
            {
                return;
            }

            _nextLog = now + 1f;
            if (_body.PhysicsComponent == null || !_body.PhysicsComponent.HasReceivedState)
            {
                Debug.Log("REPRO waiting for the body's state");
            }
            else
            {
                Quaternion received = _body.PhysicsComponent.WorldRotation.Value.ToUnity();
                Quaternion body = _body.transform.rotation;
                float receivedOff = AwayFromRest(received);
                if (_isClient)
                {
                    _worst = Mathf.Max(_worst, receivedOff);
                }

                Debug.Log(
                    $"REPRO {(_isClient ? "client" : "host")} t={now:F0}s mode={_body.PhysicsComponent.WorldRotation.TransmissionMode} "
                        + $"component rotation {receivedOff:F1} deg from rest {received}, body {AwayFromRest(body):F1} deg from rest"
                );
            }

            if (now >= _quitAt)
            {
                if (_isClient)
                {
                    Debug.Log(
                        _worst > 20f
                            ? $"REPRO RESULT: REPRODUCED, the client's received rotation drifted {_worst:F0} deg from where the host holds it"
                            : $"REPRO RESULT: not reproduced, worst {_worst:F1} deg"
                    );
                }

                Application.Quit();
            }
        }
    }
}
