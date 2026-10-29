# Nucleus: a guest's NetworkRigidbody rotation drifts under Divine

A minimal Unity project for a Nucleus Pro 0.4.0 bug: a client's received `NetworkPhysicsComponent.WorldRotation`
(Divine, the default) turns steadily away from the server's, about 150 degrees in 25 seconds, while the server's
body holds still. The client's body follows it over. With `WorldPosition` and `WorldRotation` sent as Interval, it
doesn't. The full report is in [BUG_REPORT.md](BUG_REPORT.md).

## The scene

One heavy body (73,100 kg, a ship's proportions) carrying `NetworkRigidbody` with a smoothed visual, placed in the
scene. `Floater` keeps it afloat at y = 0 with a buoyancy spring and a righting torque and rocks it 2 degrees, with
forces applied on `PhysicsSimulationDriver.WorldStepStarted` on every peer. The network manager runs a 50 Hz tick
with the default `NucleusPerTick` physics and a Synapse (UDP) transport. Nothing turns the body after the session
starts.

## Running it

1. Unity 6000.6.4f1. Copy `Assets/Nucleus` from a Nucleus **Pro 0.4.0** install into `Assets/` (it isn't in this repo).
2. Open the project, then **Repro > Build**. It writes the scene, the network prefab collection and
   `Builds/Repro/Repro.exe`. Batch mode: `-executeMethod DivineRotationRepro.Editor.ReproBuilder.Build`.
3. Run a host and a client on one machine:

   ```
   Builds\Repro\Repro.exe -host -yaw 180 -seconds 45 -logFile host.log
   Builds\Repro\Repro.exe -join 127.0.0.1 -yaw 180 -seconds 40 -logFile client.log
   ```

   Each logs `REPRO` lines once a second: how far the component's rotation and the body are from rest (upright, at
   the given yaw). The client ends with a verdict, `REPRO RESULT: REPRODUCED ...` when its received rotation has
   drifted more than 20 degrees.

Options (give the same ones to both): `-yaw <degrees>` (default 180) turns the body before the host starts;
`-interval` sends the pose as Interval instead of Divine; `-seconds <n>` quits after n seconds.

## Results (Windows 11, loopback)

| Run | Client | Host |
|---|---|---|
| `-yaw 180` (Divine) | REPRODUCED, drifted 180 deg | within 2.4 deg |
| `-yaw 180 -interval` | not reproduced, worst 2.4 deg | within 2.4 deg |
| `-yaw 0` (Divine) | REPRODUCED, drifted 178 deg | within 2.4 deg |

The logs' `REPRO` lines are in [results/](results/).

## dotnet-harness

A core-only attempt (no Unity): a server and a client `CoreManager` in one process over loopback, writing a rocking
rotation to a Divine and an Interval member of one component, with and without a quaternion sign flip. It does **not**
reproduce the drift, which points at the Unity integration's physics path (`NetworkRigidbody`, convergence, or how
the received pose is applied to the body) rather than Divine's member encoding alone. Build it against your own
Nucleus DLLs: `dotnet run -p:NucleusPath=C:\path\to\Assets\Nucleus`.
