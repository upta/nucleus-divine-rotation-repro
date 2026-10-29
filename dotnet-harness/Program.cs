using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Nucleus.Connections;
using Nucleus.Managers.Core;
using Nucleus.Physics;
using Nucleus.Systems;
using Nucleus.Transports.Synapse;

namespace DivineRotationRepro
{
    /// <summary>
    /// A server and a client in one process over loopback UDP. The server writes a ship's rotation every frame: facing
    /// about 180 degrees of yaw, gently pitching and rolling, as a hull rides at anchor. It writes it to two members of
    /// one component, one Divine and one Interval. The client compares each with what the server last wrote.
    ///
    /// Two systems carry the same motion: in one the quaternion moves continuously (its W crosses zero as the hull swings
    /// either side of 180 degrees), in the other it's kept with W >= 0, as a physics engine may hand it back (q and -q are
    /// the same rotation).
    /// </summary>
    public static class Program
    {
        private const ushort Port = 47777;
        private const uint TickRate = 50;
        private const float FrameMilliseconds = 1000f / 60f;
        private const float Seconds = 20f;
        private const float WarmUpSeconds = 2f;

        // Rocking amplitude scale, period scale and per-tick jitter (degrees), from the command line: rock, speed, jitter.
        private static float s_rock = 1f;
        private static float s_speed = 1f;
        private static float s_jitter = 0f;

        // Seconds after which the hull holds still where she is (0: never), from the command line's fourth value.
        private static float s_holdAfter = 0f;
        private static readonly Random s_random = new(1234);

        public static async Task<int> Main(string[] args)
        {
            if (args.Length >= 3)
            {
                s_rock = float.Parse(args[0], System.Globalization.CultureInfo.InvariantCulture);
                s_speed = float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
                s_jitter = float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
                if (args.Length >= 4)
                {
                    s_holdAfter = float.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture);
                }
            }

            var serverLoop = new ManualLoopStepProvider();
            var clientLoop = new ManualLoopStepProvider();
            var server = new CoreManager(TickRate, serverLoop);
            var client = new CoreManager(TickRate, clientLoop);

            // As Unity's UnitySystemManager sets it by default (the core's default is 0).
            server.SystemManager.StateInterpolation = 1;
            client.SystemManager.StateInterpolation = 1;

            var serverSynapse = (Synapse)await server.TransportManager.AddTransportAsync<Synapse>();
            serverSynapse.SetPort(Port);
            var clientSynapse = (Synapse)await client.TransportManager.AddTransportAsync<Synapse>();
            clientSynapse.SetPort(Port);
            await serverSynapse.ConnectAsync(Invoker.Server);
            await clientSynapse.ConnectAsync(Invoker.Client);

            NetworkPhysicsComponent? clientPhysics = null;
            NetworkSystemPool.Watch<NetworkSystem, NetworkPhysicsComponent>(
                client,
                system =>
                {
                    if (system.TryGetComponent(out NetworkPhysicsComponent physics))
                    {
                        clientPhysics = physics;
                    }
                },
                system => { }
            );
            var clientHulls = new List<HullComponent>();
            NetworkSystemPool.Watch<NetworkSystem, HullComponent>(
                client,
                system =>
                {
                    if (system.TryGetComponent(out HullComponent hull))
                    {
                        clientHulls.Add(hull);
                    }
                },
                system => { }
            );

            // Step until the client is in, then spawn the two hulls.
            var clock = Stopwatch.StartNew();
            while (!client.TransportManager.IsClientStarted && clock.Elapsed.TotalSeconds < 10)
            {
                Step(serverLoop, clientLoop);
            }

            if (!client.TransportManager.IsClientStarted)
            {
                Console.WriteLine("The client never connected.");
                return 2;
            }

            var serverHulls = new HullComponent[2];
            for (int variant = 0; variant < 2; variant++)
            {
                if (
                    !NetworkSystemPool.TryRent<NetworkSystem, HullComponent>(server, out NetworkSystem? system)
                    || !system.TryGetComponent(out HullComponent hull)
                )
                {
                    Console.WriteLine("Couldn't spawn a hull on the server.");
                    return 2;
                }

                serverHulls[variant] = hull;
            }

            // As NetworkRigidbody does it: a body captured into Nucleus's own physics component once a tick.
            if (
                !NetworkSystemPool.TryRent<NetworkSystem, NetworkPhysicsComponent>(server, out NetworkSystem? physicsSystem)
                || !physicsSystem.TryGetComponent(out NetworkPhysicsComponent serverPhysics)
            )
            {
                Console.WriteLine("Couldn't spawn a physics body on the server.");
                return 2;
            }

            var body = new FakeBody();
            var physicsResult = new Divergence();
            var results = new[] { new Divergence(), new Divergence() };
            var lastWritten = new Quaternion[2];
            int frames = (int)(Seconds * 1000f / FrameMilliseconds);
            for (int frame = 0; frame < frames; frame++)
            {
                float time = frame * FrameMilliseconds / 1000f;
                Quaternion rotation = HullRotation(s_holdAfter > 0f ? MathF.Min(time, s_holdAfter) : time);
                for (int variant = 0; variant < 2; variant++)
                {
                    Quaternion written = variant == 0 || rotation.W >= 0f ? rotation : Quaternion.Negate(rotation);
                    serverHulls[variant].Divine.Value = written;
                    serverHulls[variant].Interval.Value = written;
                    lastWritten[variant] = written;
                }

                body.Rotation = rotation;
                serverLoop.AdvanceEarly(FrameMilliseconds);
                if (serverLoop.IsTickFrame)
                {
                    serverPhysics.CaptureBody(body, server.NetworkLoopManager.Tick);
                }

                serverLoop.AdvanceLate(FrameMilliseconds);
                clientLoop.AdvanceEarly(FrameMilliseconds);
                clientLoop.AdvanceLate(FrameMilliseconds);
                Thread.Sleep(1);

                if (time >= WarmUpSeconds && clientPhysics != null && clientPhysics.HasReceivedState)
                {
                    physicsResult.Add(Degrees(clientPhysics.WorldRotation.Value, rotation), 0f, time);
                }

                if (time < WarmUpSeconds || clientHulls.Count < 2)
                {
                    continue;
                }

                // The client's systems arrive in the order the server spawned them.
                for (int variant = 0; variant < 2; variant++)
                {
                    HullComponent hull = clientHulls[variant];
                    results[variant].Add(
                        Degrees(hull.Divine.InterpolatedValue, lastWritten[variant]),
                        Degrees(hull.Interval.InterpolatedValue, lastWritten[variant]),
                        time
                    );
                }
            }

            Console.WriteLine($"Nucleus Divine vs Interval on a rotation near 180 degrees of yaw, {Seconds - WarmUpSeconds} s at {TickRate} Hz:");
            Console.WriteLine($"  W continuous (crosses zero): {results[0]}");
            Console.WriteLine($"  W kept >= 0 (sign flips):     {results[1]}");
            Console.WriteLine($"  NetworkPhysicsComponent (CaptureBody, W continuous): {physicsResult}");
            client.Deinitialize();
            server.Deinitialize();

            // A tick or two behind is expected of both: anything past a few degrees is wrong.
            bool divineWrong =
                results[0].MaxDivine > 10f || results[1].MaxDivine > 10f || physicsResult.MaxDivine > 10f;
            Console.WriteLine(divineWrong ? "REPRODUCED: Divine delivers a different rotation." : "Not reproduced.");
            return divineWrong ? 1 : 0;
        }

        // About 180 degrees of yaw (W near zero), swinging 3 degrees either side, pitching 2 and rolling 1.5.
        private static Quaternion HullRotation(float time) =>
            Quaternion.CreateFromYawPitchRoll(
                MathF.PI + Radians(3f * s_rock) * MathF.Sin(2f * MathF.PI * 0.05f * s_speed * time) + Jitter(),
                Radians(2f * s_rock) * MathF.Sin(2f * MathF.PI * 0.2f * s_speed * time) + Jitter(),
                Radians(1.5f * s_rock) * MathF.Sin(2f * MathF.PI * 0.13f * s_speed * time) + Jitter()
            );

        private static float Jitter() => Radians(s_jitter) * (float)(s_random.NextDouble() * 2.0 - 1.0);

        private static float Radians(float degrees) => degrees * MathF.PI / 180f;

        private static float Degrees(Quaternion a, Quaternion b)
        {
            float dot = MathF.Abs(Quaternion.Dot(Quaternion.Normalize(a), Quaternion.Normalize(b)));
            return 2f * MathF.Acos(MathF.Min(dot, 1f)) * 180f / MathF.PI;
        }

        private static void Step(ManualLoopStepProvider serverLoop, ManualLoopStepProvider clientLoop)
        {
            serverLoop.AdvanceEarly(FrameMilliseconds);
            serverLoop.AdvanceLate(FrameMilliseconds);
            clientLoop.AdvanceEarly(FrameMilliseconds);
            clientLoop.AdvanceLate(FrameMilliseconds);
            Thread.Sleep(1);
        }

        private sealed class Divergence
        {
            private int _samples;
            private float _sumDivine;
            private float _sumInterval;

            public float MaxDivine { get; private set; }

            public float MaxInterval { get; private set; }

            public float WorstAt { get; private set; }

            public void Add(float divine, float interval, float time)
            {
                _samples++;
                _sumDivine += divine;
                _sumInterval += interval;
                if (divine > MaxDivine)
                {
                    MaxDivine = divine;
                    WorstAt = time;
                }

                MaxInterval = MathF.Max(MaxInterval, interval);
            }

            public override string ToString() =>
                _samples == 0
                    ? "no samples"
                    : $"Divine off by {_sumDivine / _samples:F2} deg mean, {MaxDivine:F2} max (at {WorstAt:F1} s); Interval {_sumInterval / _samples:F2} mean, {MaxInterval:F2} max";
        }
    }
}
