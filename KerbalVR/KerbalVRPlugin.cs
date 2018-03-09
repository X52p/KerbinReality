using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;
using Valve.VR;


namespace KerbalVR
{
    // Start plugin on entering the Flight scene
    //
    [KSPAddon(KSPAddon.Startup.Instantly, false)]
    public class KerbalVRPlugin : MonoBehaviour
    {
        static Vessel activeVessel;
        static RenderSlave leftSlave;
        static RenderSlave rightSlave;

        static Kerbal activeKerbal = null;
        static int lastKerbalID = -1;

        static CameraManager.CameraMode activeCameraMode;

        public static List<Camera> leftCameras = new List<Camera>();
        public static List<Camera> rightCameras = new List<Camera>();

        public static List<GameObject> allCamerasGameObject = new List<GameObject>();

        public bool left = false;


        private static object mutex = new object();

        public static Camera camLeft_Interior, camRight_Interior;
        public static Camera camLeft_Near, camLeft_Far;
        public static Camera camRight_Near, camRight_Far;
        public static Camera leftSky, leftStars, rightSky, rightStars;
        public static Camera O_SclaledSpace, O_Galaxy, O_Near, O_Far, O_Interior;

        public static bool leftReady = false;
        public static bool rightReady = false;

        private static CVRSystem vrSystem;
        private static CVRCompositor vrCompositor;

        private static RenderTexture hmdLeftEyeRenderTexture, hmdRightEyeRenderTexture, skyTexture;
        

        private VRControllerState_t ctrlStateLeft = new VRControllerState_t();
        private VRControllerState_t ctrlStateRight = new VRControllerState_t();
        private uint ctrlStateLeft_lastPacketNum, ctrlStateRight_lastPacketNum;


        public static Texture_t hmdLeftEyeTexture, hmdRightEyeTexture;


        private System.Timers.Timer initTmr = new System.Timers.Timer(1000);

        // define controller button masks
        //--------------------------------------------------------------

        // trigger button
        public const ulong CONTROLLER_BUTTON_MASK_TRIGGER = 1ul << (int)EVRButtonId.k_EButton_SteamVR_Trigger;

        // app menu button
        public const ulong CONTROLLER_BUTTON_MASK_APP_MENU = 1ul << (int)EVRButtonId.k_EButton_ApplicationMenu;

        // touchpad
        public const ulong CONTROLLER_BUTTON_MASK_TOUCHPAD = 1ul << (int)EVRButtonId.k_EButton_SteamVR_Touchpad;

        // list of all cameras in the game
        //--------------------------------------------------------------
        private string[] cameraNames = new string[7]
        {
        "GalaxyCamera",
        "Camera ScaledSpace",
        "Camera 01",
        "Camera 00",
        "InternalCamera",
        "UIMainCamera",
        "UIVectorCamera",
        };

        // list of cameras to render (string names), defined on Start()
        static List<string> cameraNamesToRender;

        // struct to keep track of Camera properties
        private struct CameraProperties
        {
            public Camera camera;
            public Matrix4x4 originalProjMatrix;
            public Matrix4x4 hmdLeftProjMatrix;
            public Matrix4x4 hmdRightProjMatrix;

            public CameraProperties(Camera camera, Matrix4x4 originalProjMatrix, Matrix4x4 hmdLeftProjMatrix, Matrix4x4 hmdRightProjMatrix)
            {
                this.camera = camera;
                this.originalProjMatrix = originalProjMatrix;
                this.hmdLeftProjMatrix = hmdLeftProjMatrix;
                this.hmdRightProjMatrix = hmdRightProjMatrix;
            }
        }

        // list of cameras to render (Camera objects)
        private List<CameraProperties> camerasToRender;

        private TrackedDevicePose_t[] vrDevicePoses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
        private TrackedDevicePose_t[] vrRenderPoses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
        private TrackedDevicePose_t[] vrGamePoses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];

        private uint ctrlIndexLeft = 0;
        private uint ctrlIndexRight = 0;

        //how far into the future the Hmd position will be predicted
        public static float predict = 0.05f;

        private class RenderSlave : MonoBehaviour
        {
            EVRCompositorError lastError = EVRCompositorError.None;

            public bool HmdOn = false;
            public bool left = false;

            private static VRTextureBounds_t hmdTextureBounds;

            void Start()
            {

                hmdTextureBounds.uMin = 0.0f;
                hmdTextureBounds.uMax = 1.0f;
                hmdTextureBounds.vMin = 1.0f;
                hmdTextureBounds.vMax = 0.0f;
            }

            public static bool leftReady = false;
            public static bool rightReady = false;

            void OnRenderImage(RenderTexture r, RenderTexture r2)
            {
                if (HmdOn)
                {
                    EVRCompositorError vrCompositorError = EVRCompositorError.None;
                    if (left && !leftReady)
                    {                        
                        lock (KerbalVRPlugin.hmdRightEyeRenderTexture) lock (KerbalVRPlugin.hmdLeftEyeRenderTexture) lock (r) lock (vrCompositor)
                                    {
                                        hmdLeftEyeTexture.handle = r.GetNativeTexturePtr();

                                        vrCompositorError = vrCompositor.Submit(EVREye.Eye_Left, ref hmdLeftEyeTexture, ref hmdTextureBounds, EVRSubmitFlags.Submit_Default);
                                        leftReady = true;
                                        if (vrCompositorError != EVRCompositorError.None && vrCompositorError != lastError)
                                        {
                                            lastError = vrCompositorError;

                                            warn("Submit (Eye_Left) failed: " + vrCompositorError.ToString());
                                        }
                                    }
                    }
                    else if (!rightReady)
                    {
                        lock (KerbalVRPlugin.hmdRightEyeRenderTexture) lock (KerbalVRPlugin.hmdLeftEyeRenderTexture) lock (r) lock (vrCompositor)
                                    {
                                        hmdRightEyeTexture.handle = r.GetNativeTexturePtr();

                                        vrCompositorError = vrCompositor.Submit(EVREye.Eye_Right, ref hmdRightEyeTexture, ref hmdTextureBounds, EVRSubmitFlags.Submit_Default);
                                        rightReady = true;
                                        if (vrCompositorError != EVRCompositorError.None && vrCompositorError != lastError)
                                        {
                                            lastError = vrCompositorError;

                                            warn("Submit (Eye_Left) failed: " + vrCompositorError.ToString());
                                        }
                                        posTracker.gotPoses = false;
                                    }
                    }
                }
            }
        }

        private class posTracker : MonoBehaviour
        {
            public static bool HmdOn = false;
            public static bool gotPoses = false;

            private TrackedDevicePose_t[] vrDevicePoses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
            private TrackedDevicePose_t[] vrRenderPoses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
            private TrackedDevicePose_t[] vrGamePoses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];

            private uint ctrlIndexLeft = 0;
            private uint ctrlIndexRight = 0;

            private static Utils.RigidTransform hmdTransform;
            private static Utils.RigidTransform hmdLeftEyeTransform;
            private static Utils.RigidTransform hmdRightEyeTransform;
            private static Utils.RigidTransform ctrlPoseLeft;
            private static Utils.RigidTransform ctrlPoseRight;

            void OnPreRender()
            {
                if (!gotPoses && HmdOn)
                {
                    Part hoveredPart = Mouse.HoveredPart;
                    if (hoveredPart != null)
                    {
                        hoveredPart.HighlightActive = false;
                    }

                        lock (KerbalVRPlugin.hmdRightEyeRenderTexture) lock (KerbalVRPlugin.hmdLeftEyeRenderTexture)
                        {                           
                            //check if active kerbal changed
                            if (CameraManager.Instance.currentCameraMode.Equals(CameraManager.CameraMode.IVA) && CameraManager.Instance.IVACameraActiveKerbalIndex != lastKerbalID)
                            {
                                
                                //reenable last kerbal
                                activeKerbal.SetVisibleInPortrait(true);
                                activeKerbal.gameObject.active = true;

                                activeKerbal = CameraManager.Instance.IVACameraActiveKerbal;
                                lastKerbalID = CameraManager.Instance.IVACameraActiveKerbalIndex;

                                //deactivate curent kerbal
                                 activeKerbal.SetVisibleInPortrait(false);
                                activeKerbal.gameObject.active = false;
                            }

                            gotPoses = true;
                            
                            EVRCompositorError vrCompositorError = EVRCompositorError.None;

                            //get poses from VR api
                            vrSystem.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseSeated, predict, vrDevicePoses);
                            HmdMatrix34_t vrLeftEyeTransform = vrSystem.GetEyeToHeadTransform(EVREye.Eye_Left);
                            HmdMatrix34_t vrRightEyeTransform = vrSystem.GetEyeToHeadTransform(EVREye.Eye_Right);
                            vrCompositorError = vrCompositor.WaitGetPoses(vrRenderPoses, vrGamePoses);
                            RenderSlave.leftReady = false;
                            RenderSlave.rightReady = false;
                            if (vrCompositorError != EVRCompositorError.None)
                            {
                                KerbalVRPlugin.warn("WaitGetPoses failed: " + vrCompositorError.ToString());
                            }

                            // convert SteamVR poses to Unity coordinates
                            hmdTransform = new Utils.RigidTransform(vrDevicePoses[OpenVR.k_unTrackedDeviceIndex_Hmd].mDeviceToAbsoluteTracking);
                            hmdLeftEyeTransform = new Utils.RigidTransform(vrLeftEyeTransform);
                            hmdRightEyeTransform = new Utils.RigidTransform(vrRightEyeTransform);
                            ctrlPoseLeft = new Utils.RigidTransform(vrDevicePoses[ctrlIndexLeft].mDeviceToAbsoluteTracking);
                            ctrlPoseRight = new Utils.RigidTransform(vrDevicePoses[ctrlIndexRight].mDeviceToAbsoluteTracking);

                           
                            //calculate corect position acording to vessel orientation
                            hmdTransform.rot = (O_Interior.transform.rotation) * hmdTransform.rot;
                            hmdTransform.pos = (O_Interior.transform.rotation) * hmdTransform.pos + O_Interior.transform.position;

                            //shema:
                            //rotate Camera acording to Hmd rotation
                            //reset local position
                            //set new local position acording to eye position
                            //add position of hmd

                            //internal camera has no special transformations
                            camLeft_Interior.transform.localRotation = hmdTransform.rot;
                            camLeft_Interior.transform.localPosition = new Vector3(0f, 0f, 0f);
                            camLeft_Interior.transform.Translate(hmdLeftEyeTransform.pos);
                            camLeft_Interior.transform.localPosition += hmdTransform.pos;
                                                          
                            camRight_Interior.transform.localRotation = hmdTransform.rot;
                            camRight_Interior.transform.localPosition = new Vector3(0f, 0f, 0f);
                            camRight_Interior.transform.Translate(hmdRightEyeTransform.pos);
                            camRight_Interior.transform.localPosition += hmdTransform.pos;

                            //rotations and positions for all following cameras are converted from internal to wolrd space:
                            camLeft_Near.transform.localRotation = InternalSpace.InternalToWorld(hmdTransform.rot);
                            camLeft_Near.transform.localPosition = new Vector3(0f, 0f, 0f);
                            camLeft_Near.transform.Translate(hmdLeftEyeTransform.pos);
                            camLeft_Near.transform.localPosition += InternalSpace.InternalToWorld(hmdTransform.pos);

                            camRight_Near.transform.localRotation = InternalSpace.InternalToWorld(hmdTransform.rot);
                            camRight_Near.transform.localPosition = new Vector3(0f, 0f, 0f);
                            camRight_Near.transform.Translate(hmdRightEyeTransform.pos);
                            camRight_Near.transform.localPosition += InternalSpace.InternalToWorld(hmdTransform.pos);

                            camLeft_Far.transform.localRotation = InternalSpace.InternalToWorld(hmdTransform.rot);
                            camLeft_Far.transform.localPosition = new Vector3(0f, 0f, 0f);
                            camLeft_Far.transform.Translate(hmdLeftEyeTransform.pos);
                            camLeft_Far.transform.localPosition += InternalSpace.InternalToWorld(hmdTransform.pos);

                            camRight_Far.transform.localRotation = InternalSpace.InternalToWorld(hmdTransform.rot);
                            camRight_Far.transform.localPosition = new Vector3(0f, 0f, 0f);
                            camRight_Far.transform.Translate(hmdRightEyeTransform.pos);
                            camRight_Far.transform.localPosition += InternalSpace.InternalToWorld(hmdTransform.pos);

                            //the sky and star Cameras are in ScaledSpace so the vectors have to be scaled down
                            leftSky.transform.localRotation = InternalSpace.InternalToWorld(hmdTransform.rot);
                            leftSky.transform.localPosition = new Vector3(0f, 0f, 0f);
                            leftSky.transform.Translate(hmdLeftEyeTransform.pos * ScaledSpace.InverseScaleFactor);
                            leftSky.transform.localPosition += (hmdTransform.pos * ScaledSpace.InverseScaleFactor);

                            rightSky.transform.localRotation = InternalSpace.InternalToWorld(hmdTransform.rot);
                            rightSky.transform.localPosition = new Vector3(0f, 0f, 0f);
                            rightSky.transform.Translate(hmdRightEyeTransform.pos * ScaledSpace.InverseScaleFactor);
                            rightSky.transform.localPosition += (hmdTransform.pos * ScaledSpace.InverseScaleFactor);

                            leftStars.transform.localRotation = InternalSpace.InternalToWorld(hmdTransform.rot);
                            leftStars.transform.localPosition = new Vector3(0f, 0f, 0f);
                            leftStars.transform.Translate(hmdLeftEyeTransform.pos* ScaledSpace.InverseScaleFactor);
                            leftStars.transform.localPosition += InternalSpace.InternalToWorld(hmdTransform.pos* ScaledSpace.InverseScaleFactor);

                            rightStars.transform.localRotation = InternalSpace.InternalToWorld(hmdTransform.rot);
                            rightStars.transform.localPosition = new Vector3(0f, 0f, 0f);
                            rightStars.transform.Translate(hmdRightEyeTransform.pos* ScaledSpace.InverseScaleFactor);
                            rightStars.transform.localPosition += InternalSpace.InternalToWorld(hmdTransform.pos* ScaledSpace.InverseScaleFactor);                            
                        }
                }
            }
        }

        /// <summary>
        /// Overrides the Start method for a MonoBehaviour plugin.
        /// </summary>
        void Start()
        {

            log("KerbalVrPlugin started.");
            DontDestroyOnLoad(this);
            log("dont destroy!");

            // define what cameras to render to HMD
            cameraNamesToRender = new List<string>();
            cameraNamesToRender.Add(cameraNames[0]); // renders the galaxy
            log("cameras 0 added");
            cameraNamesToRender.Add(cameraNames[1]); // renders space/planets?
            log("cameras 1 added");
            cameraNamesToRender.Add(cameraNames[2]); // renders things far away (like out to the horizon)
            log("cameras 2 added");
            cameraNamesToRender.Add(cameraNames[3]); // renders things close to you
            log("cameras 3 added");
            cameraNamesToRender.Add(cameraNames[4]); // renders the IVA view (cockpit)
            log("cameras 4 added");
            //cameraNamesToRender.Add(cameraNames[5]); // don't render UI, it looks shitty
            //cameraNamesToRender.Add(cameraNames[6]); // don't render UI, it looks shitty

            camerasToRender = new List<CameraProperties>(cameraNamesToRender.Count);
        }

        

        bool HmdOn = false;
        bool master = true;

        private WaitForEndOfFrame waitForEndOfFrame = new WaitForEndOfFrame();

        public void onVesselChange(Vessel v)
        {
            new WaitForEndOfFrame();
            this.gameObject.AddComponent<KerbalVRPlugin>();
            Destroy(this);
        }

        void Update()
        {

            //increase prediction
            if (Input.GetKeyDown(KeyCode.KeypadPlus))
            {
                predict += 0.01f;
                log("predict interval set to: " + predict);
            }

            //decrease prediction
            if (Input.GetKeyDown(KeyCode.KeypadMinus))
            {
                predict -= 0.01f;
                log("predict interval set to: " + predict);
            }

            //If Hmd is initialised and key is pressed -> reset position
            if (Input.GetKeyDown(KeyCode.Keypad0) && HmdOn)
            {
                vrSystem.ResetSeatedZeroPose();
                log("Seated pose reset!");
            }

            //If Hmd is not initialised and key is pressed -> initialise everything
            if (Input.GetKeyDown(KeyCode.Keypad0) && !HmdOn)
            {
                //get Active Vessel
                activeVessel = FlightGlobals.ActiveVessel;

                HmdOn = true;

                //setup OpenVR
                setup();

                uint renderTextureWidth = 0;
                uint renderTextureHeight = 0;
                vrSystem.GetRecommendedRenderTargetSize(ref renderTextureWidth, ref renderTextureHeight);

                HmdMatrix34_t vrLeftEyeTransform = vrSystem.GetEyeToHeadTransform(EVREye.Eye_Left);
                HmdMatrix34_t vrRightEyeTransform = vrSystem.GetEyeToHeadTransform(EVREye.Eye_Right);                

                //eyeDiference = (int)(Camera.main.WorldToScreenPoint(new Utils.RigidTransform(vrLeftEyeTransform).pos) - Camera.main.WorldToScreenPoint(new Utils.RigidTransform(vrRightEyeTransform).pos)).magnitude;

                // skyTexture = new RenderTexture((int)(renderTextureWidth + eyeDiference), (int)renderTextureHeight, 24, RenderTextureFormat.ARGB32);
                // skyTexture.Create();

                hmdLeftEyeRenderTexture = new RenderTexture((int)renderTextureWidth, (int)renderTextureHeight, 24, RenderTextureFormat.ARGB32);
                hmdLeftEyeRenderTexture.Create();

                hmdRightEyeRenderTexture = new RenderTexture((int)renderTextureWidth, (int)renderTextureHeight, 24, RenderTextureFormat.ARGB32);
                hmdRightEyeRenderTexture.Create();

                setupCameras();

                //add on vesselchange callback
                GameEvents.onVesselChange.Add(onVesselChange);
            }
            if (HmdOn)
            {
                if (!CameraManager.Instance.currentCameraMode.Equals(activeCameraMode))
                {
                    if (CameraManager.Instance.currentCameraMode.Equals(CameraManager.CameraMode.Flight) || CameraManager.Instance.currentCameraMode.Equals(CameraManager.CameraMode.External))
                    {
                     //   Camera.main.enabled = true;
                        setRenderTexturesTo(null, null);

                        O_SclaledSpace.enabled = true;
                        O_Galaxy.enabled = true;
                        O_Near.enabled = true;
                        O_Far.enabled = true;
                        //O_Interior.enabled = false;
                    }
                    else
                    {
                       setRenderTexturesTo(null, null);
                       O_SclaledSpace.enabled = false;
                       O_Galaxy.enabled = false;
                       O_Near.enabled = false;
                       O_Far.enabled = false;
                       O_Interior.enabled = false;
                    }
                }

                

                if (CameraManager.Instance.currentCameraMode.Equals(CameraManager.CameraMode.IVA)) {
                    
                    leftStars.Render();
                    leftSky.Render();
                    camLeft_Far.Render();
                    camLeft_Near.Render();
                    camLeft_Interior.Render();

                    rightStars.Render();
                    rightSky.Render();
                    camRight_Far.Render();
                    camRight_Near.Render();
                    camRight_Interior.Render();

                }
                //if someone knows how to fix the other cameras corectly please tell me!
                else if(CameraManager.Instance.currentCameraMode.Equals(CameraManager.CameraMode.Map))
                {
                    O_Galaxy.Render();
                    O_SclaledSpace.Render();
                }
                else
                {
                    O_Galaxy.Render();
                    O_SclaledSpace.Render();
                    O_Far.Render();
                    O_Near.Render();                    
                }
            }
        }

        

        /*void OnDestroy()
        {
            log("KerbalVrPlugin OnDestroy");
            posTracker.HmdOn = false;
            rightSlave.HmdOn = false;
            leftSlave.HmdOn = false;
            vrSystem.ReleaseInputFocus();
            OpenVR.Shutdown();
            HmdOn = false;

        }*/

        private void setRenderTexturesTo(RenderTexture left, RenderTexture right)
        {
            leftStars.targetTexture = left;
            rightStars.targetTexture = right;

            leftSky.targetTexture = left;
            rightSky.targetTexture = right;

            camLeft_Near.targetTexture = left;
            camRight_Near.targetTexture = right;

            camLeft_Interior.targetTexture = left;
            camRight_Interior.targetTexture = right;

            camLeft_Far.targetTexture = left;
            camRight_Far.targetTexture = right;
        }

        private void setupCameras()
        {
            foreach (string cameraName in cameraNamesToRender)
            {
                foreach (Camera camera in Camera.allCameras)
                {
                    if (cameraName.Equals(camera.name))
                    {
                        switch (camera.name)
                        {
                            case "GalaxyCamera":
                                O_Galaxy = camera;
                                break;
                            case "Camera ScaledSpace":
                                O_SclaledSpace = camera;
                                break;
                            case "Camera 01":
                                O_Far = camera;
                                break;
                            case "Camera 00":
                                O_Near = camera;
                                break;
                            default:
                                break;
                        }


                        camera.gameObject.AddOrGetComponent<posTracker>();

                        log("Camera:");
                        log("  Name:  " + camera.name);
                        log("  mask:  " + Convert.ToString(camera.cullingMask, 2));
                        log("  depth: " + camera.depth);
                        log("");

                        if (cameraName.Equals("InternalCamera"))
                        {
                            O_Interior = camera;
                        }
                        if (cameraName.Equals("GalaxyCamera"))
                        {
                            O_Galaxy = camera;
                        }

                        if (cameraName.Equals("Camera ScaledSpace"))
                        {
                            O_SclaledSpace = camera;
                            log("sky cam rot = " + camera.transform.rotation.eulerAngles.ToString());
                        }
                    }
                }
            }
            
            //Instantiate Cameras for all Layers
            camRight_Interior = (Camera)Camera.Instantiate(Camera.main, Camera.main.transform.position + new Vector3(0, 0, 0), Camera.main.transform.rotation);
            camLeft_Interior = (Camera)Camera.Instantiate(Camera.main, Camera.main.transform.position + new Vector3(0, 0, 0), Camera.main.transform.rotation);

            camRight_Near = (Camera)Camera.Instantiate(Camera.main, Camera.main.transform.position + new Vector3(0, 0, 0), Camera.main.transform.rotation);
            camLeft_Near = (Camera)Camera.Instantiate(Camera.main, Camera.main.transform.position + new Vector3(0, 0, 0), Camera.main.transform.rotation);

            camRight_Far = (Camera)Camera.Instantiate(Camera.main, Camera.main.transform.position + new Vector3(0, 0, 0), Camera.main.transform.rotation);
            camLeft_Far = (Camera)Camera.Instantiate(Camera.main, Camera.main.transform.position + new Vector3(0, 0, 0), Camera.main.transform.rotation);

            leftSky = (Camera)Camera.Instantiate(Camera.main, Camera.main.transform.position + new Vector3(0, 0, 0), Camera.main.transform.rotation);
            rightSky = (Camera)Camera.Instantiate(Camera.main, Camera.main.transform.position + new Vector3(0, 0, 0), Camera.main.transform.rotation);

            leftStars = (Camera)Camera.Instantiate(Camera.main, Camera.main.transform.position + new Vector3(0, 0, 0), Camera.main.transform.rotation);
            rightStars = (Camera)Camera.Instantiate(Camera.main, Camera.main.transform.position + new Vector3(0, 0, 0), Camera.main.transform.rotation);

            //copy Properties from Original Cameras
            leftSky.CopyFrom(O_SclaledSpace);
            rightSky.CopyFrom(O_SclaledSpace);
            leftStars.CopyFrom(O_Galaxy);
            rightStars.CopyFrom(O_Galaxy);
            camRight_Near.CopyFrom(O_Near);
            camLeft_Near.CopyFrom(O_Near);
            camLeft_Interior.CopyFrom(O_Interior);
            camRight_Interior.CopyFrom(O_Interior);
            camRight_Far.CopyFrom(O_Far);
            camLeft_Far.CopyFrom(O_Far);


            //set RenderTextures for Cameras
            setRenderTexturesTo(hmdLeftEyeRenderTexture, hmdRightEyeRenderTexture);

            //create left slave
            leftSlave = camLeft_Interior.gameObject.AddOrGetComponent<RenderSlave>();
            leftSlave.left = true;

            //create right slave
            rightSlave = camRight_Interior.gameObject.AddOrGetComponent<RenderSlave>();
            rightSlave.left = false;



            //Set Projectsions for all Cameras
            HmdMatrix44_t projLeft = vrSystem.GetProjectionMatrix(EVREye.Eye_Left, camLeft_Near.nearClipPlane, camLeft_Near.farClipPlane);
            HmdMatrix44_t projRight = vrSystem.GetProjectionMatrix(EVREye.Eye_Right, camRight_Near.nearClipPlane, camRight_Near.farClipPlane);

            camLeft_Near.projectionMatrix = MathUtils.Matrix4x4_OpenVr2UnityFormat(ref projLeft);
            camRight_Near.projectionMatrix = MathUtils.Matrix4x4_OpenVr2UnityFormat(ref projRight);

            HmdMatrix44_t projLeft2 = vrSystem.GetProjectionMatrix(EVREye.Eye_Left, camLeft_Interior.nearClipPlane, camLeft_Interior.farClipPlane);
            HmdMatrix44_t projRight2 = vrSystem.GetProjectionMatrix(EVREye.Eye_Right, camRight_Interior.nearClipPlane, camRight_Interior.farClipPlane);
                        
            camLeft_Interior.projectionMatrix = MathUtils.Matrix4x4_OpenVr2UnityFormat(ref projLeft2);
            camRight_Interior.projectionMatrix = MathUtils.Matrix4x4_OpenVr2UnityFormat(ref projRight2);
            camLeft_Interior.SetStereoProjectionMatrix(Camera.StereoscopicEye.Left, MathUtils.Matrix4x4_OpenVr2UnityFormat(ref projLeft2));
            camLeft_Interior.SetStereoProjectionMatrix(Camera.StereoscopicEye.Right, MathUtils.Matrix4x4_OpenVr2UnityFormat(ref projLeft2));

            HmdMatrix44_t projLeft3 = vrSystem.GetProjectionMatrix(EVREye.Eye_Left, camLeft_Far.nearClipPlane, camLeft_Far.farClipPlane);
            HmdMatrix44_t projRight3 = vrSystem.GetProjectionMatrix(EVREye.Eye_Right, camRight_Far.nearClipPlane, camRight_Far.farClipPlane);

            camLeft_Far.projectionMatrix = MathUtils.Matrix4x4_OpenVr2UnityFormat(ref projLeft3);
            camRight_Far.projectionMatrix = MathUtils.Matrix4x4_OpenVr2UnityFormat(ref projRight3);

            HmdMatrix44_t projLeft4 = vrSystem.GetProjectionMatrix(EVREye.Eye_Left, leftSky.nearClipPlane, leftSky.farClipPlane);
            HmdMatrix44_t projRight4 = vrSystem.GetProjectionMatrix(EVREye.Eye_Right, rightSky.nearClipPlane, rightSky.farClipPlane);

            leftSky.projectionMatrix = MathUtils.Matrix4x4_OpenVr2UnityFormat(ref projLeft4);
            rightSky.projectionMatrix = MathUtils.Matrix4x4_OpenVr2UnityFormat(ref projRight4);

            HmdMatrix44_t projLeft5 = vrSystem.GetProjectionMatrix(EVREye.Eye_Left, leftStars.nearClipPlane, leftStars.farClipPlane);
            HmdMatrix44_t projRight5 = vrSystem.GetProjectionMatrix(EVREye.Eye_Right, rightStars.nearClipPlane, rightStars.farClipPlane);

            leftStars.projectionMatrix = MathUtils.Matrix4x4_OpenVr2UnityFormat(ref projLeft5);
            rightStars.projectionMatrix = MathUtils.Matrix4x4_OpenVr2UnityFormat(ref projRight5);


            //disable All Cameras to increase Performance
            O_SclaledSpace.enabled = false;
            O_Galaxy.enabled = false;
            O_Near.enabled = false;
            O_Far.enabled = false;
            O_Interior.enabled = false;

            camLeft_Near.enabled = false;
            camRight_Near.enabled = false;

            camLeft_Far.enabled = false;
            camRight_Far.enabled = false;

            leftSky.enabled = false;
            rightSky.enabled = false;

            leftStars.enabled = false;
            rightStars.enabled = false;

            camLeft_Interior.enabled = false;
            camRight_Interior.enabled = false;



            //activate slaves
            posTracker.HmdOn = true;
            leftSlave.HmdOn = true;
            rightSlave.HmdOn = true;

            //initialize active kerbal:
            activeKerbal = CameraManager.Instance.IVACameraActiveKerbal;
            lastKerbalID = CameraManager.Instance.IVACameraActiveKerbalIndex;

        }

        private void setup()
        {
            //init VR System and check for errors
            var error = EVRInitError.None;
            vrSystem = OpenVR.Init(ref error, EVRApplicationType.VRApplication_Scene);

            if ((int)error != (int)EVRInitError.None)
            {
                log("KerbalVrPlugin started.");
            }
            else
            {
                err(error.ToString());
            }

            //rendervalues #########################################################
            // Setup render values
            uint w = 0, h = 0;
            vrSystem.GetRecommendedRenderTargetSize(ref w, ref h);
            float sceneWidth = (float)w;
            float sceneHeight = (float)h;

            float l_left = 0.0f, l_right = 0.0f, l_top = 0.0f, l_bottom = 0.0f;
            vrSystem.GetProjectionRaw(EVREye.Eye_Left, ref l_left, ref l_right, ref l_top, ref l_bottom);

            float r_left = 0.0f, r_right = 0.0f, r_top = 0.0f, r_bottom = 0.0f;
            vrSystem.GetProjectionRaw(EVREye.Eye_Right, ref r_left, ref r_right, ref r_top, ref r_bottom);

            Vector2 tanHalfFov = new Vector2(Mathf.Max(-l_left, l_right, -r_left, r_right), Mathf.Max(-l_top, l_bottom, -r_top, r_bottom));

            //Setup rendertextures
            hmdLeftEyeTexture = new Texture_t();
            hmdLeftEyeTexture.eColorSpace = EColorSpace.Auto;

            hmdRightEyeTexture = new Texture_t();
            hmdRightEyeTexture.eColorSpace = EColorSpace.Auto;

            //select Texture Type depending on RenderAPI (Currently only DirectX11 is tested)
            switch (SystemInfo.graphicsDeviceType)
            {
                case UnityEngine.Rendering.GraphicsDeviceType.OpenGL2:
                    log("OpenGL2");
                    hmdLeftEyeTexture.eType = ETextureType.OpenGL;
                    hmdRightEyeTexture.eType = ETextureType.OpenGL;
                    break;
                case UnityEngine.Rendering.GraphicsDeviceType.OpenGLCore:
                    log("OpenCore");
                    hmdLeftEyeTexture.eType = ETextureType.OpenGL;
                    hmdRightEyeTexture.eType = ETextureType.OpenGL;
                    break;
                case UnityEngine.Rendering.GraphicsDeviceType.OpenGLES2:
                    log("OpenGLES2");
                    hmdLeftEyeTexture.eType = ETextureType.OpenGL;
                    hmdRightEyeTexture.eType = ETextureType.OpenGL;
                    break;
                case UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3:
                    log("OpenGLES3");
                    hmdLeftEyeTexture.eType = ETextureType.OpenGL;
                    hmdRightEyeTexture.eType = ETextureType.OpenGL;
                    break;
                case UnityEngine.Rendering.GraphicsDeviceType.Direct3D9:
                    log("Direct3D9");
                    warn("DirectX 9 mode not Supported! There be Dragons!");
                    hmdLeftEyeTexture.eType = ETextureType.DirectX;
                    hmdRightEyeTexture.eType = ETextureType.DirectX;
                    break;
                case UnityEngine.Rendering.GraphicsDeviceType.Direct3D11:
                    log("Direct3D11");
                    hmdLeftEyeTexture.eType = ETextureType.DirectX;
                    hmdRightEyeTexture.eType = ETextureType.DirectX;
                    break;
                case UnityEngine.Rendering.GraphicsDeviceType.Direct3D12:
                    log("Direct3D12");
                    warn("DirectX 12 mode not implemented! There be Dragons!");
                    hmdLeftEyeTexture.eType = ETextureType.DirectX12;
                    hmdRightEyeTexture.eType = ETextureType.DirectX12;
                    break;
                default:
                    throw (new Exception(SystemInfo.graphicsDeviceType.ToString() + " not supported"));
            }

            vrCompositor = OpenVR.Compositor;

            if (!vrCompositor.CanRenderScene())
            {
                err("can not render scene");
            }
        }

        public static void log(string msg)
        {
            Debug.Log("[KerbalVR] " + msg);
        }

        public static void warn(string msg)
        {
            Debug.LogWarning("[KerbalVR] " + msg);
        }

        public static void err(string msg)
        {
            Debug.LogError("[KerbalVR] " + msg);
        }
    }

}
