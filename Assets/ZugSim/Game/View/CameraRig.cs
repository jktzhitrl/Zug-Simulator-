using UnityEngine;
using ZugSim.Core;

namespace ZugSim.Game
{
    public enum CameraMode { Cab, Exterior, Trackside }

    /// <summary>Kameras: Fuehrerstand (frei schwenkbar), Aussenkamera (Orbit + Pinch-Zoom) und Streckenkamera.</summary>
    public sealed class CameraRig : MonoBehaviour
    {
        public Camera Cam;
        public CameraMode Mode = CameraMode.Cab;
        /// <summary>Langsames Umkreisen (Menue-Hintergrund).</summary>
        public bool AutoOrbit;

        float lookYaw, lookPitch;
        float idleTime;
        float orbitYaw = 155f, orbitPitch = 16f, orbitDist = 34f;
        Vector3 smoothTarget;
        bool haveTarget;
        Vector3 tracksidePos;
        bool haveTrackside;
        float sway;

        public static CameraRig Build(Transform parent)
        {
            var go = new GameObject("Camera");
            go.transform.SetParent(parent, false);
            var rig = go.AddComponent<CameraRig>();
            rig.Cam = go.AddComponent<Camera>();
            rig.Cam.nearClipPlane = 0.08f;
            rig.Cam.farClipPlane = 3000f;
            rig.Cam.fieldOfView = 70f;
            rig.Cam.allowHDR = true;
            go.tag = "MainCamera";
            go.AddComponent<AudioListener>();
            return rig;
        }

        public void SetMode(CameraMode m)
        {
            Mode = m;
            lookYaw = lookPitch = 0; idleTime = 0;
            haveTrackside = false;
        }

        public void Cycle() { SetMode((CameraMode)(((int)Mode + 1) % 3)); }

        public void Look(Vector2 pixelDelta)
        {
            idleTime = 0;
            float k = 100f / Mathf.Max(Screen.height, 1);
            switch (Mode)
            {
                case CameraMode.Cab:
                    lookYaw = Mathf.Clamp(lookYaw + pixelDelta.x * k, -120f, 120f);
                    lookPitch = Mathf.Clamp(lookPitch - pixelDelta.y * k, -35f, 50f);
                    break;
                case CameraMode.Exterior:
                    orbitYaw += pixelDelta.x * k * 1.4f;
                    orbitPitch = Mathf.Clamp(orbitPitch - pixelDelta.y * k, -4f, 80f);
                    break;
            }
        }

        public void Pinch(float amount)
        {
            if (Mode == CameraMode.Exterior) orbitDist = Mathf.Clamp(orbitDist * (1f - amount), 8f, 140f);
            else if (Mode == CameraMode.Cab) Cam.fieldOfView = Mathf.Clamp(Cam.fieldOfView * (1f - amount * 0.5f), 40f, 85f);
        }

        public void UpdateRig(TrainView train, SimulationWorld w, float dt)
        {
            switch (Mode)
            {
                case CameraMode.Cab: UpdateCab(train, w, dt); break;
                case CameraMode.Exterior: UpdateExterior(train, dt); break;
                default: UpdateTrackside(train, w, dt); break;
            }
        }

        void UpdateCab(TrainView train, SimulationWorld w, float dt)
        {
            idleTime += dt;
            if (idleTime > 4f)
            {
                lookYaw = Mathf.Lerp(lookYaw, 0f, 1f - Mathf.Exp(-1.2f * dt));
                lookPitch = Mathf.Lerp(lookPitch, 0f, 1f - Mathf.Exp(-0.6f * dt));
            }
            sway = Mathf.Lerp(sway, (float)w.Train.Acceleration * -0.5f, 1f - Mathf.Exp(-3f * dt));
            Transform a = train.HeadAnchor;
            Vector3 eye = a.TransformPoint(CabView.EyeLocal);
            float rumble = Mathf.Sin(Time.time * 17f) * 0.0025f * Mathf.Clamp01((float)w.Train.Speed / 40f);
            Cam.transform.position = eye + a.up * rumble;
            Cam.transform.rotation = a.rotation * Quaternion.Euler(-14f + lookPitch + sway, lookYaw, 0);
            if (Cam.fieldOfView < 40f || Cam.fieldOfView > 85f) Cam.fieldOfView = 70f;
        }

        void UpdateExterior(TrainView train, float dt)
        {
            Cam.fieldOfView = Mathf.Lerp(Cam.fieldOfView, 50f, 1f - Mathf.Exp(-4f * dt));
            if (AutoOrbit) orbitYaw += dt * 5f;
            Vector3 target = train.CenterAnchor.position;
            smoothTarget = haveTarget ? Vector3.Lerp(smoothTarget, target, 1f - Mathf.Exp(-12f * dt)) : target;
            haveTarget = true;
            float trainYaw = train.CenterAnchor.eulerAngles.y;
            Quaternion rot = Quaternion.Euler(orbitPitch, trainYaw + orbitYaw, 0);
            Vector3 pos = smoothTarget - rot * Vector3.forward * orbitDist;
            Cam.transform.position = pos;
            Cam.transform.rotation = Quaternion.LookRotation(smoothTarget - pos, Vector3.up);
        }

        void UpdateTrackside(TrainView train, SimulationWorld w, float dt)
        {
            Vector3 head = train.HeadAnchor.position;
            Vector3 fwd = train.HeadAnchor.forward;
            if (!haveTrackside || Vector3.Dot(head - tracksidePos, fwd) > 45f)
            {
                var f = w.Follower;
                double off = System.Math.Min(f.Offset + 260.0, f.Edge.Length);
                var p = w.Graph.PoseAt(f.EdgeId, off);
                Quaternion r = TrackView.ToRotation(p);
                tracksidePos = TrackView.ToVector(p) + r * new Vector3(9f, 2.6f, 0f);
                haveTrackside = true;
            }
            Cam.transform.position = tracksidePos;
            Vector3 aim = head + fwd * 12f + Vector3.up * 1.8f;
            Cam.transform.rotation = Quaternion.LookRotation(aim - tracksidePos, Vector3.up);
            float dist = Vector3.Distance(tracksidePos, aim);
            Cam.fieldOfView = Mathf.Lerp(Cam.fieldOfView, Mathf.Clamp(Mathf.Atan(7f / Mathf.Max(dist, 8f)) * Mathf.Rad2Deg * 2f, 18f, 60f), 1f - Mathf.Exp(-8f * dt));
        }
    }
}
