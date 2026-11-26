using UnityEngine;
using ZugSim.Core;
using ZugSim.Core.Safety;

namespace ZugSim.Game
{
    /// <summary>
    /// Vollstaendig synthetisierter Ton (keine Audio-Assets noetig): Fahrmotor, Rollgeraeusch, Schienenstoesse,
    /// Bremsen, Wind, Regen, Horn, Tuerwarnton und Sicherheits-Warnsignale. Echte Samples koennen spaeter ergaenzt werden.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class ProceduralAudio : MonoBehaviour
    {
        // Parameter (Hauptthread schreibt, Audio-Thread liest)
        public float Master = 0.8f;
        float speed, throttle, brake, rain, hiss, curve, interior = 1f;
        bool horn, doorBeep, alarm, alarmFast;
        float chime;

        // Synth-Zustand (nur Audio-Thread)
        double sampleRate = 48000;
        double phMotor, phMotor2, phHorn1, phHorn2, phSqueal, phBeep, phAlarm, phGate, phJoint, phChime;
        float lpRumble, lpWind, lpRain, hpRain, jointEnv, hornEnv, chimeEnv, noiseState = 1f;
        uint rng = 2463534242u;

        public void Configure(SimulationWorld w, bool cabView, bool hornHeld)
        {
            var t = w.Train;
            speed = (float)t.Speed;
            throttle = (float)t.ThrottleApplied;
            brake = (float)t.Brakes.Fill;
            rain = (float)w.Weather.Current.Rain;
            curve = (float)Mathf.Clamp01((float)(w.Follower.CurveRadiusAtHead() > 0 ? 800.0 / w.Follower.CurveRadiusAtHead() - 0.5 : 0.0));
            horn = hornHeld;
            doorBeep = t.Doors.State == DoorState.Closing;
            alarm = w.Safety.SifaWarning || w.Safety.State == SafetyState.Warning;
            alarmFast = w.Safety.EmergencyBrakeActive;
            interior = cabView ? 0.55f : 1f;
        }

        public void TriggerChime() { chime = 1f; }
        public void SetHiss(float level) { hiss = level; }

        void Awake()
        {
            sampleRate = AudioSettings.outputSampleRate;
            var src = GetComponent<AudioSource>();
            src.clip = AudioClip.Create("ZugSimSilence", 1024, 1, (int)sampleRate, false);
            src.loop = true;
            src.spatialBlend = 0f;
            src.playOnAwake = false;
            src.Play();
        }

        float Noise()
        {
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            return (rng & 0xFFFFFF) / 8388608f - 1f;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            float spd = speed, thr = throttle, brk = brake, rn = rain, hs = hiss, cv = curve, ins = interior;
            bool hn = horn, db = doorBeep, al = alarm, alf = alarmFast;
            double inv = 1.0 / sampleRate;
            float vNorm = Mathf.Clamp01(spd / 45f);
            double jointRate = spd / 24.0;   // Schienenstoesse pro Sekunde
            float chimeLevel = chime; chime = 0f; if (chimeLevel > 0) chimeEnv = 1f;

            for (int i = 0; i < data.Length; i += channels)
            {
                float n = Noise();
                // Rollgeraeusch (tiefpassgefiltertes Rauschen)
                lpRumble += (n - lpRumble) * (0.015f + 0.05f * vNorm);
                float s = lpRumble * (0.55f * vNorm + 0.02f);
                // Wind
                lpWind += (n - lpWind) * 0.12f;
                s += (n - lpWind) * vNorm * vNorm * 0.11f * ins;
                // Schienenstoesse
                phJoint += jointRate * inv;
                if (phJoint >= 1.0) { phJoint -= 1.0; jointEnv = 1f; }
                jointEnv *= 0.9992f;
                s += jointEnv * lpRumble * 1.4f * vNorm;
                // Motor: Basis steigt mit Geschwindigkeit, Lautstaerke mit Fahrstufe
                double f = 70.0 + spd * 6.5;
                phMotor += f * inv; phMotor2 += f * 2.01 * inv;
                float motor = (float)(System.Math.Sin(phMotor * 6.2831853) * 0.6 + System.Math.Sin(phMotor2 * 6.2831853) * 0.3);
                s += motor * (0.012f + 0.07f * thr) * (0.5f + 0.5f * vNorm);
                // Kurvenquietschen
                if (cv > 0.05f && spd > 3f)
                {
                    phSqueal += (3100.0 + 600.0 * lpRumble) * inv;
                    s += (float)System.Math.Sin(phSqueal * 6.2831853) * cv * vNorm * 0.05f;
                }
                // Bremse: Quietschen bei niedriger Geschwindigkeit + Luftrauschen beim Loesen
                if (brk > 0.02f && spd > 0.3f && spd < 10f)
                {
                    phSqueal += (2300.0 + 400.0 * n) * inv;
                    s += (float)System.Math.Sin(phSqueal * 6.2831853) * brk * 0.04f * (1f - spd / 10f);
                }
                s += (n - lpWind) * hs * 0.25f;
                // Regen
                lpRain += (n - lpRain) * 0.4f;
                s += (n - lpRain) * rn * 0.10f;
                // Horn (Zweiklang)
                hornEnv += ((hn ? 1f : 0f) - hornEnv) * (hn ? 0.0012f : 0.0007f);
                if (hornEnv > 0.002f)
                {
                    phHorn1 += 370.0 * inv; phHorn2 += 494.0 * inv;
                    float h1 = Squareish(phHorn1), h2 = Squareish(phHorn2);
                    s += (h1 + h2) * 0.5f * hornEnv * 0.28f * (ins < 1f ? 0.55f : 1f);
                }
                // Tuerwarnton
                if (db)
                {
                    phGate += 4.0 * inv; phBeep += 1900.0 * inv;
                    if ((phGate % 1.0) < 0.5) s += (float)System.Math.Sin(phBeep * 6.2831853) * 0.08f;
                }
                // Tuer-Gong
                if (chimeEnv > 0.001f)
                {
                    phChime += 880.0 * inv;
                    s += (float)System.Math.Sin(phChime * 6.2831853) * chimeEnv * 0.12f;
                    chimeEnv *= 0.9997f;
                }
                // Warnsignal (Sifa/Ueberwachung) bzw. Zwangsbremsung
                if (al || alf)
                {
                    phAlarm += (alf ? 650.0 : 1000.0) * inv;
                    phGate += (alf ? 6.0 : 2.5) * inv;
                    if ((phGate % 1.0) < 0.6) s += (float)System.Math.Sin(phAlarm * 6.2831853) * 0.13f;
                }
                s *= Master;
                s = Mathf.Clamp(s, -0.98f, 0.98f);
                for (int c = 0; c < channels; c++) data[i + c] += s;
            }
        }

        static float Squareish(double ph)
        {
            double x = System.Math.Sin(ph * 6.2831853);
            return (float)(x * 0.7 + System.Math.Sin(ph * 3.0 * 6.2831853) * 0.2 + System.Math.Sin(ph * 5.0 * 6.2831853) * 0.1);
        }
    }
}
