# A client's NetworkRigidbody rotation drifts under Divine transmission

**Nucleus:** Pro 0.4.0 (`VERSION.txt`: `Nucleus 0.4.0`)
**Unity:** 6000.6.4f1, Windows 11, Windows Standalone player, Mono
**Repro:** https://github.com/upta/nucleus-divine-rotation-repro

## Summary

On a client, the `WorldRotation` a `NetworkRigidbody`'s `NetworkPhysicsComponent` receives turns steadily away from
the server's while the server's body sits still: about 70 degrees 14 seconds after launch, about 150 by 24 seconds, and it keeps
wandering (180 at worst in 40 seconds). The client's body follows the received rotation, so it rolls over. The
server's component and body stay within 2.4 degrees of rest the whole time. Switching the component's
`WorldPosition` and `WorldRotation` to `TransmissionMode.Interval` makes it go away (client worst 2.4 degrees).

## Steps

1. A scene with a `UnityCoreManager` (50 Hz tick, `UnityPhysicsManager` left at `NucleusPerTick`, a `SynapseTransport`).
2. A heavy rigidbody (73,100 kg, box collider 8 x 4 x 24 m) placed in the scene with `NetworkRigidbody` (defaults,
   `SmoothedVisual` set to a child). Interpolation off.
3. A script holding it afloat at y = 0 with forces on `PhysicsSimulationDriver.WorldStepStarted` on every peer: a
   buoyancy spring and a righting torque, plus a 2 degree rock on a 6 second period.
4. Start a host, then a client on the same machine. Log `PhysicsComponent.WorldRotation.Value` once a second on both.

The repro project does exactly this (`Repro > Build`, then `Repro.exe -host` and `Repro.exe -join 127.0.0.1`).

## Expected

The client's received rotation tracks the server's: within a few degrees of upright.

## Actual

Client, Divine (`results/yaw180-divine.txt`), degrees from rest:

```
t=5s   component rotation 23.1   body 20.2
t=14s  component rotation 71.7   body 67.3
t=24s  component rotation 149.9  body 146.6
t=34s  component rotation 152.0  body 148.2
REPRO RESULT: REPRODUCED, the client's received rotation drifted 180 deg from where the host holds it
```

Host over the same run: 0.5 to 2.4 degrees.

- It happens at 0 degrees of yaw too (178 degrees of drift), so it isn't the quaternion's sign near W = 0.
- The received value itself drifts (`NetworkPhysicsComponent.WorldRotation.Value`), before convergence or the visual
  smoothing touch the body: the body trails it by a few degrees.
- The drift is steady and one-signed, like an accumulated delta: each tick adds a small error that is never
  corrected by a full value.
- A core-only .NET test (two `CoreManager`s over loopback, writing a rocking rotation to Divine and Interval members
  of a plain `NetworkComponent`, with and without sign flips) does **not** reproduce it (`dotnet-harness/` in the
  repo). So the trigger seems specific to the Unity integration's physics path: `NetworkRigidbody` and
  `NetworkPhysicsComponent`, written from the body each tick under `NucleusPerTick`.

## Workaround

In a `NetworkRigidbody` subclass, before the base links:

```csharp
protected override void OnSystemLinked()
{
    PhysicsComponent.WorldPosition.SetTransmissionMode(TransmissionMode.Interval);
    PhysicsComponent.WorldRotation.SetTransmissionMode(TransmissionMode.Interval);
    base.OnSystemLinked();
}
```

The repro's `-interval` flag sets the same thing at runtime.
